using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked ability slot manager on the chicken. Holds <see cref="SlotCount"/>
    /// equipped <see cref="AbilityBaseSO"/>s — as of v0.7 every class gets all four,
    /// which retires the Combo passive's old job of granting a third — drives activation
    /// from the Fusion input buffer, and owns the per-slot cooldown timers.
    /// </summary>
    /// <remarks>
    /// Exactly one ability can be active at a time — pressing another slot is ignored
    /// if one is mid-duration. Ability gameplay effects mutate
    /// <see cref="ChickenController"/> state (move multiplier, movement lock,
    /// damage immunity) which lives only on the StateAuthority; remote peers
    /// observe the resulting <c>[Networked]</c> state (position, HP) instead of
    /// re-running ability logic. Cooldown progress is drawn from the
    /// <c>ActiveSlot</c> + <c>Cooldown0/1/2</c> networked properties so every peer
    /// (including the local HUD) sees an accurate radial fill.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AbilityController : NetworkBehaviour
    {
        private const string Source = "Ability";
        public const int InvalidSlot = -1;

        /// <summary>
        /// Ability buttons every chicken has. Four as of v0.7: one is Peck on every class
        /// except Assassin, which cannot forage and spends all four on its kit.
        /// </summary>
        public const int SlotCount = 4;

        [Tooltip("Equipped class passive ability. Mandatory; if unassigned, falls back to class default.")]
        [SerializeField] private PassiveAbilitySO _passive;

        [Tooltip("Equipped ability for slot 0. Used by every class.")]
        [SerializeField] private AbilityBaseSO _slot0;

        [Tooltip("Equipped ability for slot 1. Used by every class.")]
        [SerializeField] private AbilityBaseSO _slot1;

        [Tooltip("Equipped ability for slot 2. Available to every class as of v0.7.")]
        [SerializeField] private AbilityBaseSO _slot2;

        [Tooltip("Equipped ability for slot 3. Available to every class as of v0.7.")]
        [SerializeField] private AbilityBaseSO _slot3;

        [Networked] public int ActiveSlot { get; set; }
        [Networked] private TickTimer ActivationTimer { get; set; }
        [Networked] private TickTimer Cooldown0 { get; set; }
        [Networked] private TickTimer Cooldown1 { get; set; }
        [Networked] private TickTimer Cooldown2 { get; set; }
        [Networked] private TickTimer Cooldown3 { get; set; }

        /// <summary>
        /// Hold-to-aim charge state (FEEDBACK.md §2, Stage 2). 0 = nothing charging;
        /// 1..3 = slot (index-1) currently being aimed, i.e. its hold button is down and
        /// no ability has fired yet. Drives the opponent-visible wind-up tell (§2.4) —
        /// every peer observes this, not just the caster. 1 byte.
        /// </summary>
        [Networked] public byte ChargingSlot { get; set; }

        /// <summary>
        /// Targets hit by the most recently completed activation; 0 = whiff (§3.3).
        /// Written by <see cref="TryActivate"/> BEFORE <see cref="LastCastEventId"/> is
        /// bumped, so any peer reacting to the id change always reads a consistent count
        /// for that same cast — never a stale count paired with a fresh id. 1 byte.
        /// </summary>
        [Networked] public byte LastCastHitCount { get; set; }

        /// <summary>
        /// Bumped on every activation. One-shot networked "event" — peers watch for the
        /// change and fire the local hit/whiff VFX exactly once, same pattern as
        /// <c>ChickenController.KnockbackEventId</c> (wraps at 255 by design; only the
        /// change matters, not the value). 1 byte.
        /// </summary>
        [Networked] public byte LastCastEventId { get; set; }

        public PassiveAbilitySO Passive => _passive;
        public AbilityBaseSO Slot0 => _slot0;
        public AbilityBaseSO Slot1 => _slot1;
        public AbilityBaseSO Slot2 => _slot2;
        public AbilityBaseSO Slot3 => _slot3;

        public AbilityBaseSO ActiveAbility => GetSlot(ActiveSlot);

        /// <summary>The ability currently charging (hold-to-aim), or null if
        /// <see cref="ChargingSlot"/> is 0. Slot-index view of the 1-based encoding.</summary>
        public AbilityBaseSO ChargingAbility => ChargingSlot == 0 ? null : GetSlot(ChargingSlot - 1);

        /// <summary>
        /// Active ability's remaining duration as a 0..1 fraction (1 = just activated, 0 =
        /// about to expire / nothing active). Read-only wrapper around the private
        /// <see cref="ActivationTimer"/> for Stage 5's self-ring drain (FEEDBACK.md §5,
        /// case 30) — exposed as a derived accessor rather than making the timer itself
        /// public.
        /// </summary>
        public float ActiveRemaining01
        {
            get
            {
                var ability = ActiveAbility;
                if (ActiveSlot == InvalidSlot || ability == null || ability.Duration <= 0f) return 0f;
                float remaining = ActivationTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(remaining / ability.Duration);
            }
        }

        private ChickenController _controller;
        private AbilityContext _ctx;
        private ChickenCombat _combat;
        private ChickenAnimator _animator;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private PrefabRegistrySO _prefabRegistry;
        private bool _initialized;

        // ---- Hold/release/cancel state machine scratch (StateAuthority-side only,
        // not networked — re-derived from the input buffer every tick). Reused instance
        // arrays instead of locals so FixedUpdateNetwork (32 Hz) doesn't allocate.
        private readonly bool[] _holdBits = new bool[SlotCount];
        private readonly bool[] _pressBits = new bool[SlotCount];
        private readonly bool[] _canBeginCharge = new bool[SlotCount];

        // ---- Pending-hold scratch (StateAuthority-side only, NOT networked) ----
        //
        // The pre-commitment half of the casting model (FEEDBACK.md §2, revised v0.6.1).
        // A hold enters "pending" first and is only promoted to ChargingSlot — the
        // networked, every-peer-visible aim state — once it has survived
        // FeedbackTuning.TapHoldThresholdSeconds. Releasing before then still fires
        // (release ALWAYS fires); it just never paid for a telegraph, target marks, the
        // opponent-visible wind-up glow, or the aim-rotate movement lock, all four of
        // which gate themselves on ChargingSlot and so need no changes of their own.
        //
        // Deliberately plain fields and not [Networked]: this is a decision the state
        // authority has not made yet, so there is nothing for a remote peer to render, and
        // replicating it would leak the wind-up tell the threshold exists to withhold.
        //
        // Tick arithmetic: at Fusion's 32 Hz, Runner.DeltaTime is 0.03125 s, so promotion
        // lands on the 4th accumulating tick after the rising one (4 x 0.03125 = 0.125 s,
        // the first multiple that clears 0.12). Three ticks would be 0.09375 s — short of
        // the threshold — so the effective boundary is 0.125 s, not 0.12.
        private int   _pendingHoldSlot = InvalidSlot;
        private float _pendingHoldSeconds;

        // Target-count scratch for LastCastHitCount (see TryActivate). Instance, not
        // static — AbilityBaseSO._scratch is a *different*, protected buffer meant for
        // ability subclasses to use from inside their own OnActivate.
        private readonly System.Collections.Generic.List<ChickenController> _hitCountScratch = new(4);

        // §6 "denied-press bump": a one-frame flag consumed by the local HUD (Stage 5)
        // so a refused press shakes the button exactly once, never repeatedly while the
        // player keeps holding a dead button.
        private bool _deniedPressPending;
        private int _deniedPressSlot;

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg, PrefabRegistrySO prefabRegistry)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
            _prefabRegistry = prefabRegistry;
        }

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();
            _animator = GetComponent<ChickenAnimator>();
            _ctx = new AbilityContext(_controller);

            if (HasStateAuthority)
            {
                ActiveSlot = InvalidSlot;
            }

            if (_passive == null && _controller != null)
            {
                var reg = ProjectContext.Instance.Container.TryResolve<AbilityRegistrySO>();
                if (reg != null) _passive = reg.GetDefaultPassiveForClass(_controller.Class);
            }

            if (_passive != null)
            {
                _passive.OnActivate(_ctx);
            }

            if (_combat != null) _combat.OnDeathAuthority += HandleOwnerDeath;

            _initialized = true;
            _log?.Debug(Source, $"Spawned. Passive={(_passive != null ? _passive.name : "(none)")}, " +
                $"Slot0={(Slot0 != null ? Slot0.name : "(none)")}, " +
                $"Slot1={(Slot1 != null ? Slot1.name : "(none)")}, " +
                $"Slot2={(Slot2 != null ? Slot2.name : "(none)")}, " +
                $"Slot3={(Slot3 != null ? Slot3.name : "(none)")}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_combat != null) _combat.OnDeathAuthority -= HandleOwnerDeath;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !_initialized) return;

            if (_controller != null && _controller.IsDecoy) return;

            // Auto-deactivate when the duration timer expires.
            if (ActiveSlot != InvalidSlot && ActivationTimer.Expired(Runner))
            {
                Deactivate();
            }

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning)
            {
                if (ActiveSlot != InvalidSlot) Deactivate();
                if (IsAimGestureLive) CancelCharge();
                return;
            }

            if (_combat != null && _combat.IsRemoved)
            {
                if (ActiveSlot != InvalidSlot) Deactivate();
                if (IsAimGestureLive) CancelCharge();
                return;
            }

            if (!GetInput<PlayerNetworkInput>(out var input)) return;

            bool canCast = _controller == null || ControlRules.CanCast(_controller.CurrentControlState);
            bool otherAbilityActive = ActiveSlot != InvalidSlot;

            if (otherAbilityActive && _log != null && _log.IsEnabled(Logging.LogLevel.Verbose) && AnyAbilityInputSet(input))
                _log.Verbose(Source, $"Ability input ignored — slot {ActiveSlot} already active.");

            _holdBits[0]  = input.Buttons.IsSet((int)InputButton.AbilityHold1);
            _holdBits[1]  = input.Buttons.IsSet((int)InputButton.AbilityHold2);
            _holdBits[2]  = input.Buttons.IsSet((int)InputButton.AbilityHold3);
            _holdBits[3]  = input.Buttons.IsSet((int)InputButton.AbilityHold4);
            _pressBits[0] = input.Buttons.IsSet((int)InputButton.Ability1);
            _pressBits[1] = input.Buttons.IsSet((int)InputButton.Ability2);
            _pressBits[2] = input.Buttons.IsSet((int)InputButton.Ability3);
            _pressBits[3] = input.Buttons.IsSet((int)InputButton.Ability4);
            bool cancelPressed = input.Buttons.IsSet((int)InputButton.AbilityCancel);

            _canBeginCharge[0] = CanBeginCharge(0);
            _canBeginCharge[1] = CanBeginCharge(1);
            _canBeginCharge[2] = CanBeginCharge(2);
            _canBeginCharge[3] = CanBeginCharge(3);

            // Clock a live pending hold BEFORE deciding, so the tick on which it crosses
            // the threshold is the tick it gets promoted — not the one after.
            if (_pendingHoldSlot != InvalidSlot) _pendingHoldSeconds += Runner.DeltaTime;

            var decision = AbilityHoldStateMachine.Decide(
                ChargingSlot, _pendingHoldSlot, _pendingHoldSeconds, FeedbackTuning.TapHoldThresholdSeconds,
                canCast, otherAbilityActive, cancelPressed, _holdBits, _pressBits, _canBeginCharge);

            switch (decision.Action)
            {
                case ChargeAction.BeginPendingHold:
                    _pendingHoldSlot = decision.Slot;
                    _pendingHoldSeconds = 0f;
                    break;
                case ChargeAction.BeginCharge:
                    ChargingSlot = (byte)(decision.Slot + 1);
                    ClearPendingHold();
                    break;
                case ChargeAction.Fire:
                    ChargingSlot = 0;
                    ClearPendingHold();
                    TryActivate(decision.Slot);
                    break;
                case ChargeAction.Cancel:
                    CancelCharge();
                    break;
                case ChargeAction.RefuseAttempt:
                    TryActivate(decision.Slot); // refuses cleanly and logs why — see EvaluateRefusalInternal
                    break;
                case ChargeAction.None:
                default:
                    break;
            }
        }

        /// <summary>True while the player has any pre-fire gesture in flight — a committed,
        /// networked charge or a local pending hold. The single condition every "tear the
        /// aim down" path checks, so neither state can be left behind by the other.</summary>
        private bool IsAimGestureLive => ChargingSlot != 0 || _pendingHoldSlot != InvalidSlot;

        private static bool AnyAbilityInputSet(PlayerNetworkInput input) =>
            input.Buttons.IsSet((int)InputButton.Ability1) || input.Buttons.IsSet((int)InputButton.Ability2) ||
            input.Buttons.IsSet((int)InputButton.Ability3) || input.Buttons.IsSet((int)InputButton.Ability4) ||
            input.Buttons.IsSet((int)InputButton.AbilityHold1) || input.Buttons.IsSet((int)InputButton.AbilityHold2) ||
            input.Buttons.IsSet((int)InputButton.AbilityHold3) || input.Buttons.IsSet((int)InputButton.AbilityHold4);

        // ---- Public read-only helpers (used by the HUD / debug overlays) -------

        public float CooldownRemaining(int slot)
        {
            var cd = GetCooldown(slot);
            if (cd.ExpiredOrNotRunning(Runner)) return 0f;
            return cd.RemainingTime(Runner) ?? 0f;
        }

        public float CooldownProgress01(int slot)
        {
            var ability = GetSlot(slot);
            if (ability == null || ability.Cooldown <= 0f) return 1f;
            var remaining = CooldownRemaining(slot);
            return 1f - Mathf.Clamp01(remaining / ability.Cooldown);
        }

        public bool IsReady(int slot) => GetSlot(slot) != null && CooldownRemaining(slot) <= 0f;

        /// <summary>
        /// Why <paramref name="slot"/> would refuse to fire right now, for the HUD to
        /// poll continuously (FEEDBACK.md §6) — a pure computed query, not stored state,
        /// so it stays accurate between presses (e.g. to grey a button the instant a
        /// cooldown starts, not just when the player tries to press it). Routes through
        /// <see cref="AbilityRefusalRules.Evaluate"/> — the exact same precedence table
        /// <see cref="TryActivate"/> uses via <see cref="EvaluateRefusalInternal"/> — so
        /// the two can never disagree about which reason wins.
        /// </summary>
        public AbilityRefusal EvaluateRefusal(int slot) => EvaluateRefusalInternal(slot, out _);

        /// <summary>
        /// One-shot flag: true (once) when a press was refused, so Stage 5 can play the
        /// §6 denied-press bump exactly once per press rather than every tick the button
        /// stays refused. Call from the HUD's own update, not FixedUpdateNetwork.
        /// </summary>
        public bool TryConsumeDeniedPress(out int slot)
        {
            if (_deniedPressPending)
            {
                slot = _deniedPressSlot;
                _deniedPressPending = false;
                return true;
            }
            slot = InvalidSlot;
            return false;
        }

        public void TriggerCooldown(int slot, float duration)
        {
            if (!HasStateAuthority) return;
            var timer = TickTimer.CreateFromSeconds(Runner, duration);
            if (slot == 0) Cooldown0 = timer;
            else if (slot == 1) Cooldown1 = timer;
            else if (slot == 2) Cooldown2 = timer;
            else if (slot == 3) Cooldown3 = timer;
        }

        /// <summary>
        /// Number of ability slots available to this chicken — <see cref="SlotCount"/> for
        /// every class as of v0.7.
        /// </summary>
        /// <remarks>
        /// This used to return 3 for the Combo passive and 2 otherwise, which was Combo's
        /// entire mechanical purpose. Giving every class four buttons retires that job.
        /// Combo itself is removed with the class-specialization pass, where GDD §5.3's
        /// Spoiler and Thief take over the Assassin's passive fork.
        /// </remarks>
        public int EquippedSlotCount => SlotCount;

        /// <summary>
        /// Bot-only activation API. Mirrors the exact gates from the player input
        /// path so cooldown / stun / double-cast rules are always honoured.
        /// Returns <c>true</c> if the ability fired this call.
        /// Must be called from the StateAuthority (bot FSM already guards this).
        /// </summary>
        public bool BotTryActivate(int slot)
        {
            if (!HasStateAuthority) return false;
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return false;
            if (_combat != null && _combat.IsRemoved) return false;
            if (ActiveSlot != InvalidSlot) return false;
            if (!IsReady(slot)) return false;
            TryActivate(slot);
            return ActiveSlot == slot;
        }

        /// <summary>
        /// Scans slots 0..<see cref="EquippedSlotCount"/>-1 for the first
        /// non-null, ready ability whose resolved <see cref="BotRole"/> matches
        /// <paramref name="role"/>. Returns <c>true</c> and writes the slot index
        /// to <paramref name="slot"/> when found.
        /// </summary>
        public bool TryGetReadySlotForRole(BotRole role, out int slot)
        {
            int count = EquippedSlotCount;
            for (int i = 0; i < count; i++)
            {
                var ability = GetSlot(i);
                if (ability == null || !IsReady(i)) continue;
                if (ability.ResolveBotRole() == role) { slot = i; return true; }
            }
            slot = InvalidSlot;
            return false;
        }

        /// <summary>
        /// Assigns ability assets before <see cref="Spawned"/> runs.
        /// Called by <see cref="MatchBootstrapper"/> inside the <c>onBeforeSpawned</c>
        /// callback so every peer already has the chosen abilities on first <c>Spawned</c>
        /// read. Null arguments leave the existing (prefab-default) value unchanged.
        /// </summary>
        public void SetSlots(PassiveAbilitySO passive, AbilityBaseSO slot0, AbilityBaseSO slot1,
            AbilityBaseSO slot2 = null, AbilityBaseSO slot3 = null)
        {
            if (passive != null) _passive = passive;
            if (slot0 != null) _slot0 = slot0;
            if (slot1 != null) _slot1 = slot1;
            if (slot2 != null) _slot2 = slot2;
            if (slot3 != null) _slot3 = slot3;
        }

        public void SetSlots(AbilityBaseSO slot0, AbilityBaseSO slot1,
            AbilityBaseSO slot2 = null, AbilityBaseSO slot3 = null)
        {
            SetSlots(null, slot0, slot1, slot2, slot3);
        }

        // ---- Internals ---------------------------------------------------------

        private AbilityBaseSO GetSlot(int slot) => slot switch
        {
            0 => _slot0,
            1 => _slot1,
            2 => _slot2,
            3 => _slot3,
            _ => null,
        };

        private TickTimer GetCooldown(int slot) => slot switch
        {
            0 => Cooldown0,
            1 => Cooldown1,
            2 => Cooldown2,
            3 => Cooldown3,
            _ => default,
        };

        private void SetCooldown(int slot, TickTimer timer)
        {
            if (slot == 0)      Cooldown0 = timer;
            else if (slot == 1) Cooldown1 = timer;
            else if (slot == 2) Cooldown2 = timer;
            else if (slot == 3) Cooldown3 = timer;
        }

        /// <summary>
        /// The full, live-state precedence check for <paramref name="slot"/>, gathering
        /// each boolean input and handing them to <see cref="AbilityRefusalRules.Evaluate"/>
        /// — the pure, EditMode-tested ordering. Both <see cref="EvaluateRefusal"/> (the
        /// HUD's polled query) and <see cref="TryActivate"/> (the actual gate) call this
        /// exact method, so they can never disagree about which reason wins when several
        /// apply at once.
        /// </summary>
        private AbilityRefusal EvaluateRefusalInternal(int slot, out AbilityBaseSO ability)
        {
            bool slotUnavailable = slot < 0 || slot >= EquippedSlotCount;
            ability = slotUnavailable ? null : GetSlot(slot);
            slotUnavailable |= ability == null;

            bool onCooldown = !slotUnavailable && !GetCooldown(slot).ExpiredOrNotRunning(Runner);
            bool stunned = _controller != null && !ControlRules.CanCast(_controller.CurrentControlState);
            bool otherActive = ActiveSlot != InvalidSlot;
            bool noTarget = !slotUnavailable && !onCooldown && !stunned && !otherActive &&
                             ability != null && !ability.IsUsable(_controller);

            return AbilityRefusalRules.Evaluate(slotUnavailable, onCooldown, stunned, otherActive, noTarget);
        }

        /// <summary>
        /// SlotUnavailable + Cooldown only — the two refusal reasons that are stable for
        /// an entire hold and would make charging pointless. Deliberately excludes
        /// Stunned/OtherAbilityActive (already gated before the hold state machine ever
        /// runs, see FixedUpdateNetwork) and NoTarget (a target may walk into the shape
        /// mid-hold — that is the whole point of aiming). Used only to decide whether a
        /// hold is even worth starting to charge.
        /// </summary>
        private bool CanBeginCharge(int slot)
        {
            if (slot < 0 || slot >= EquippedSlotCount) return false;
            if (GetSlot(slot) == null) return false;
            return GetCooldown(slot).ExpiredOrNotRunning(Runner);
        }

        /// <summary>Clears an in-progress hold-to-aim gesture — committed charge AND pending
        /// hold alike — without firing and without touching cooldown. The "cancel" side of
        /// every case in FEEDBACK.md §2.5/§2.6.</summary>
        private void CancelCharge()
        {
            ChargingSlot = 0;
            ClearPendingHold();
        }

        /// <summary>Drops the pre-commitment hold state. Called from every path that clears
        /// <see cref="ChargingSlot"/> (cancel, fire, promote, match stop, removal, death) so
        /// a stale pending slot can never survive into the next gesture and fire an ability
        /// the player already let go of.</summary>
        private void ClearPendingHold()
        {
            _pendingHoldSlot = InvalidSlot;
            _pendingHoldSeconds = 0f;
        }

        private void TryActivate(int slot)
        {
            var reason = EvaluateRefusalInternal(slot, out var ability);
            if (reason != AbilityRefusal.None)
            {
                _log?.Debug(Source, $"TryActivate slot {slot}: refused ({reason}).");
                _deniedPressPending = true;
                _deniedPressSlot = slot;
                return;
            }

            ActiveSlot = slot;
            ActivationTimer = TickTimer.CreateFromSeconds(Runner, ability.Duration);
            SetCooldown(slot, TickTimer.CreateFromSeconds(Runner, ability.Cooldown));
            // Refresh context fields that abilities need for NetworkObject spawning.
            _ctx.Runner         = Runner;
            _ctx.PrefabRegistry = _prefabRegistry;

            if (ability.TerrainTraversal != TerrainTraversal.None && _controller != null)
            {
                _controller.Traversal?.Begin(ability.TerrainTraversal);
            }

            // Length-based teleport jump (GDD 3.5). Ordered against the target resolve below
            // by the ability's own aim shape, never by its name:
            //
            //   * Default — jump FIRST. A shape anchored on the caster (circle, cone, landing
            //     ring) belongs where the caster ends up: an ability that blinks and then
            //     detonates should detonate at the landing point, not at the take-off point.
            //   * Capsule (AbilityBaseSO.ResolvesBeforeJump) — jump LAST. A capsule describes
            //     the lane the caster sweeps *through*, so its origin is the take-off point.
            //     Flying Peck is why: at JumpTier Short its lane was resolving 5 m past the
            //     press point, so it flew over everything the player aimed at while the
            //     telegraph drew the lane at the live position.
            //
            // Safe to move the transform here either way: FixedUpdateNetwork gates on
            // HasStateAuthority, and BotTryActivate re-checks it.
            if (!ability.ResolvesBeforeJump) ExecuteJumpIfAny(ability);

            // Snapshot who this cast actually hits BEFORE bumping LastCastEventId, so any
            // peer reacting to the id change always reads a hit count for THIS cast, never
            // a stale one from the previous activation (spec requirement, Stage 2C).
            // NOTE: two families always report 0 here and must NOT be read as a whiff —
            // AimShape.None self-buffs (no shape for GatherTargets to return anything from)
            // and placed zones (Root Egg / Feather Trap hit later, in AbilityZone's tick).
            // Both exemptions are declared once, on AbilityBaseSO.ReportsCastHits, which
            // every whiff-vs-hit consumer gates on.
            int hitCount = _controller != null ? ability.GatherTargets(_controller, _hitCountScratch) : 0;
            LastCastHitCount = (byte)Mathf.Min(hitCount, byte.MaxValue);

            ability.OnActivate(_ctx);

            // The deferred half of the ordering documented above: a capsule ability has now
            // resolved and applied its lane from the take-off pose, so it may travel.
            if (ability.ResolvesBeforeJump) ExecuteJumpIfAny(ability);

            _animator?.TriggerAbilityCast();
            _audio?.PlaySFX(_audioReg != null ? _audioReg.AbilityActivate : null);
            LastCastEventId++; // wraps at 255 by design (byte overflow) — a one-shot signal, not a counter
            _log?.Info(Source, $"Activated slot {slot} ({ability.DisplayName}) for {ability.Duration:0.00}s, " +
                $"CD {ability.Cooldown:0.00}s, hits={hitCount}.");
        }

        /// <summary>
        /// Runs <paramref name="ability"/>'s length-based teleport jump, if it has one. Pure
        /// extraction from <see cref="TryActivate"/> so the two orderings above can share one
        /// implementation instead of duplicating the resolve+apply pair.
        /// </summary>
        private void ExecuteJumpIfAny(AbilityBaseSO ability)
        {
            if (ability.JumpTier == JumpLengthTier.None) return;
            if (_controller == null || _controller.Traversal == null) return;

            var t = _controller.transform;
            var jump = _controller.Traversal.ExecuteJump(
                ability.JumpTier, t.position, t.forward, MapGenerator.ArenaHalfSize);
            _controller.Traversal.ApplyJump(jump);
        }

        private void Deactivate()
        {
            var ability = ActiveAbility;
            if (ability != null)
            {
                ability.OnDeactivate(_ctx);
                _audio?.PlaySFX(_audioReg != null ? _audioReg.AbilityExpire : null);
                _log?.Debug(Source, $"Deactivated {ability.DisplayName}.");
            }
            if (_controller != null && _controller.Traversal != null)
            {
                _controller.Traversal.End();
            }
            ActiveSlot = InvalidSlot;
        }

        private void HandleOwnerDeath(NetworkBehaviourId attackerId)
        {
            if (!HasStateAuthority) return;
            if (ActiveSlot != InvalidSlot) Deactivate();
            // Tear the aim down on the same frame as the death rather than a tick later via
            // FixedUpdateNetwork's IsRemoved branch — otherwise a chicken that dies mid-hold
            // keeps its wind-up glow for one visible tick after it has already fallen over.
            if (IsAimGestureLive) CancelCharge();
        }
    }
}
