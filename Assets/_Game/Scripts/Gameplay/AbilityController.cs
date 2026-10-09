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
    /// Abilities run concurrently (Phase 6 chunk 3): every slot has its own active flag and duration
    /// timer, and a new cast never waits for a running one. Same-kind effects do not stack — each
    /// ability adds / removes a per-slot modifier on <see cref="ChickenController.Effects"/> and the
    /// strongest wins. The one exception to "keep running" is stealth: a successful cast ends
    /// Invisibility / Smoke Roost's fade, and any other cast ends Peck's lock. Ability gameplay effects
    /// mutate <see cref="ChickenController"/> state which lives only on the StateAuthority; remote peers
    /// observe the resulting <c>[Networked]</c> state (position, HP) instead of re-running ability
    /// logic. Cooldown and active progress are drawn from the networked per-slot timers so every peer
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

        /// <summary>Bit i set = slot i is mid-duration. The authoritative "is it running" flag (the timers
        /// alone cannot say it: an expired timer stays set until the next activation). 1 byte.</summary>
        [Networked] private byte ActiveMask { get; set; }
        [Networked] private TickTimer ActiveTimer0 { get; set; }
        [Networked] private TickTimer ActiveTimer1 { get; set; }
        [Networked] private TickTimer ActiveTimer2 { get; set; }
        [Networked] private TickTimer ActiveTimer3 { get; set; }

        /// <summary>
        /// The slot of the most recent successful cast, or <see cref="InvalidSlot"/> before the first. Set
        /// just before <see cref="LastCastEventId"/> is bumped, so a peer reacting to the id change reads the
        /// slot of THAT cast. Visuals that mean "the ability that just cast" (cast flash, hit feedback,
        /// range indicator) read this; "is slot N running" is <see cref="IsSlotActive"/>.
        /// </summary>
        [Networked] public int LastCastSlot { get; private set; }
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

        /// <summary>
        /// True while the Peck auto-chain runs (Phase 6 chunk 4, A6): the bird keeps pecking on its own each time
        /// the per-class cooldown is ready. Decided on the state authority by <see cref="PeckChainRules"/>; the
        /// HUD reads it to spin the ring on the Peck hex. Only input-driven chickens ever set it.
        /// </summary>
        [Networked] public NetworkBool PeckChainActive { get; private set; }

        public PassiveAbilitySO Passive => _passive;
        public AbilityBaseSO Slot0 => _slot0;
        public AbilityBaseSO Slot1 => _slot1;
        public AbilityBaseSO Slot2 => _slot2;
        public AbilityBaseSO Slot3 => _slot3;

        /// <summary>The ability of the most recent successful cast (see <see cref="LastCastSlot"/>), or null.</summary>
        public AbilityBaseSO LastCastAbility => LastCastSlot == InvalidSlot ? null : GetSlot(LastCastSlot);

        /// <summary>The ability currently charging (hold-to-aim), or null if
        /// <see cref="ChargingSlot"/> is 0. Slot-index view of the 1-based encoding.</summary>
        public AbilityBaseSO ChargingAbility => ChargingSlot == 0 ? null : GetSlot(ChargingSlot - 1);

        /// <summary>
        /// Is the ability in <paramref name="slot"/> mid-duration right now? The one place the HUD, VFX and bots
        /// ask "is THIS slot running"; several slots can be running at once.
        /// </summary>
        public bool IsSlotActive(int slot) => slot < SlotCount && AbilityRunRules.IsActive(ActiveMask, slot);

        /// <summary>True while ANY slot is running. Bots refuse to cast while this holds (see
        /// <see cref="BotTryActivate"/>); players are not gated by it.</summary>
        public bool AnyAbilityActive => ActiveMask != 0;

        /// <summary>
        /// <paramref name="slot"/>'s remaining duration as a 0..1 fraction: 1 = just activated, 0 = about to
        /// expire, that slot not running, or an ability with no meaningful duration. Read-only wrapper around
        /// the private per-slot timer for the self-ring drain (FEEDBACK.md section 5, case 30).
        /// </summary>
        public float ActiveRemaining01For(int slot)
        {
            if (!IsSlotActive(slot)) return 0f;
            var ability = GetSlot(slot);
            if (ability == null || ability.Duration <= 0f) return 0f;
            float remaining = GetActiveTimer(slot).RemainingTime(Runner) ?? 0f;
            return Mathf.Clamp01(remaining / ability.Duration);
        }

        /// <summary>
        /// The running slot that was cast most recently (smallest elapsed time), or <see cref="InvalidSlot"/>
        /// when nothing runs. Derived from the timers, so it needs no extra networked state. What the buff ring
        /// shows when several abilities overlap.
        /// </summary>
        public int MostRecentActiveSlot
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    var ability = GetSlot(i);
                    float remaining = IsSlotActive(i) ? GetActiveTimer(i).RemainingTime(Runner) ?? 0f : 0f;
                    _elapsedScratch[i] = (ability != null ? ability.Duration : 0f) - remaining;
                }
                return AbilityRunRules.MostRecent(ActiveMask, _elapsedScratch, InvalidSlot);
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

        // Whether this peer held state authority on the previous tick (see OnAuthorityGained).
        private bool _wasAuthority;

        // Phase 6 chunk 4 (A6), state-authority local. The slot of a move that succeeded THIS tick (reset at the
        // top of FixedUpdateNetwork), and how long the bird has gone without move input (Auto-Peck's idle timer).
        private int   _castSlotThisTick = InvalidSlot;
        private float _idleSeconds;
        private byte  _lastKnockbackId;

        // Scratch for AbilityRunRules (no per-tick allocation).
        private readonly float[] _elapsedScratch = new float[SlotCount];
        private readonly bool[] _flagScratch = new bool[SlotCount];

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

        // Phase 6 (A3) fizzle: a target-gated move released with nobody in range. Its own one-shot, distinct from
        // the denied press (Cooldown / Stunned / ...), so the HUD can play the whiff instead of the plain bump.
        private bool _fizzlePending;
        private int _fizzleSlot;

        // Phase 6 (A3) re-arm: seconds left per slot after a fizzle, during which that slot may not begin a hold
        // or press. State-authority local scratch (never networked) and NOT a cooldown, so the cooldown visuals
        // and TickTimers are untouched.
        private readonly float[] _rearmRemaining = new float[SlotCount];

        // Phase 6 (A4) suppression: a slot that was cancelled or switched away from reads as released until its
        // hold bit is observed low, so a still-down key cannot re-arm it. See AbilityHoldStateMachine.ApplySuppression.
        private readonly bool[] _holdSuppressed = new bool[SlotCount];

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
            // Ability SOs are assets, so Zenject cannot inject them and this project rules
            // out a service locator — the context is how they reach the logger. Assigned
            // here rather than at the per-activation refresh in TryActivate: _log is already
            // resolved (Construct, or the self-inject at the top of Spawned) and never
            // changes afterwards, and doing it before the passive's OnActivate below is what
            // makes the passive path logger-safe as well.
            _ctx.Log = _log;

            if (HasStateAuthority)
            {
                ActiveMask = 0;
                LastCastSlot = InvalidSlot;
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

            // Seed the authority edge so the original authority does not see a spurious "gained" on its first tick.
            _wasAuthority = HasStateAuthority;
            if (_controller != null) _lastKnockbackId = _controller.KnockbackEventId;
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
            bool hasAuthority = HasStateAuthority;
            if (hasAuthority != _wasAuthority)
            {
                _wasAuthority = hasAuthority;
                if (hasAuthority && _initialized) OnAuthorityGained();
            }

            if (!hasAuthority || !_initialized) return;

            if (_controller != null && _controller.IsDecoy) return;

            _castSlotThisTick = InvalidSlot;

            // Auto-deactivate each running slot whose own duration timer has expired (natural expiry: the only
            // path that plays the expire sound).
            for (int i = 0; i < SlotCount; i++) _flagScratch[i] = GetActiveTimer(i).Expired(Runner);
            byte expired = AbilityRunRules.ExpiredMask(ActiveMask, _flagScratch);
            for (int i = 0; i < SlotCount; i++)
            {
                if (AbilityRunRules.IsActive(expired, i)) Deactivate(i, natural: true);
            }

            for (int i = 0; i < SlotCount; i++)
                _rearmRemaining[i] = AbilityFizzleRules.Advance(_rearmRemaining[i], Runner.DeltaTime);

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning)
            {
                DeactivateAll();
                if (IsAimGestureLive) CancelCharge();
                return;
            }

            if (_combat != null && _combat.IsRemoved)
            {
                DeactivateAll();
                if (IsAimGestureLive) CancelCharge();
                return;
            }

            if (!GetInput<PlayerNetworkInput>(out var input)) return;

            bool canCast = _controller == null || ControlRules.CanCast(_controller.CurrentControlState);

            _holdBits[0]  = input.Buttons.IsSet((int)InputButton.AbilityHold1);
            _holdBits[1]  = input.Buttons.IsSet((int)InputButton.AbilityHold2);
            _holdBits[2]  = input.Buttons.IsSet((int)InputButton.AbilityHold3);
            _holdBits[3]  = input.Buttons.IsSet((int)InputButton.AbilityHold4);
            _pressBits[0] = input.Buttons.IsSet((int)InputButton.Ability1);
            _pressBits[1] = input.Buttons.IsSet((int)InputButton.Ability2);
            _pressBits[2] = input.Buttons.IsSet((int)InputButton.Ability3);
            _pressBits[3] = input.Buttons.IsSet((int)InputButton.Ability4);
            bool cancelPressed = input.Buttons.IsSet((int)InputButton.AbilityCancel);
            bool quickCast = input.Buttons.IsSet((int)InputButton.QuickMoves);

            _canBeginCharge[0] = CanBeginCharge(0);
            _canBeginCharge[1] = CanBeginCharge(1);
            _canBeginCharge[2] = CanBeginCharge(2);
            _canBeginCharge[3] = CanBeginCharge(3);

            // A cancelled / switched-away slot reads as released until its key or finger is actually up.
            AbilityHoldStateMachine.ApplySuppression(_holdBits, _pressBits, _holdSuppressed);

            // Clock a live pending hold BEFORE deciding, so the tick on which it crosses
            // the threshold is the tick it gets promoted — not the one after.
            if (_pendingHoldSlot != InvalidSlot) _pendingHoldSeconds += Runner.DeltaTime;

            var decision = AbilityHoldStateMachine.Decide(
                ChargingSlot, _pendingHoldSlot, _pendingHoldSeconds, FeedbackTuning.TapHoldThresholdSeconds,
                canCast, cancelPressed, _holdBits, _pressBits, _canBeginCharge, quickCast);

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
                    // Quick Moves fired on the press tick: ignore the slot until its key / finger is up, or the
                    // still-down hold bit would fire it again every tick.
                    if (quickCast) _holdSuppressed[decision.Slot] = true;
                    ApplyAimForFire(decision.Slot, input);
                    TryActivate(decision.Slot);
                    if (_controller != null) _controller.CastAim = Vector3.zero;
                    break;
                case ChargeAction.SwitchHold:
                    // Pressing another slot mid-aim: abandon the old gesture (no fire, no cooldown), start the new
                    // slot's pending hold this same tick, and ignore the old slot until it is released.
                    _holdSuppressed[decision.FromSlot] = true;
                    CancelCharge();
                    _pendingHoldSlot = decision.Slot;
                    _pendingHoldSeconds = 0f;
                    break;
                case ChargeAction.Cancel:
                    int liveSlot = AbilityHoldStateMachine.LiveSlot(ChargingSlot, _pendingHoldSlot);
                    CancelCharge();
                    if (cancelPressed && liveSlot >= 0 && liveSlot < SlotCount)
                    {
                        // A deliberate cancel (Esc / right mouse / touch edge band), not a stun tearing the aim down.
                        _holdSuppressed[liveSlot] = true;
                        PlayLocalSfx(_audioReg != null ? _audioReg.AbilityCancel : null, FeedbackTuning.CancelSfxVolume);
                    }
                    break;
                case ChargeAction.RefuseAttempt:
                    TryActivate(decision.Slot); // refuses cleanly and logs why — see EvaluateRefusalInternal
                    break;
                case ChargeAction.None:
                default:
                    break;
            }

            UpdatePeckChain(input, canCast);
        }

        // Scratch for the soft-lock pick (offsets to eligible rivals).
        private readonly Vector2[] _softLockOffsets = new Vector2[8];

        /// <summary>
        /// Phase 6 chunk 5 (A7): turns the aim byte into the bird's heading at FIRE time, on the state authority, before
        /// <see cref="TryActivate"/> resolves targets. So <c>GatherTargets</c>, the shape centre, the jump and every
        /// peer's shape test (the heading replicates through NetworkTransform) all follow the aim without any of them
        /// knowing about it.
        /// <list type="bullet">
        ///   <item>Aim 0 and no soft-lock bit: nothing changes (today's behaviour).</item>
        ///   <item>A move that does not <see cref="AbilityBaseSO.UsesAim"/> (self shapes, auras, Speed Burst, Shadowstep,
        ///   Roll &amp; Push) is left alone, on every device.</item>
        ///   <item>Single-target moves keep the heading and pick by angle through <c>ChickenController.CastAim</c>.</item>
        ///   <item>No explicit aim from touch or a pad: snap to the rival with the smallest angle within 30 / 12 degrees of
        ///   facing who would be inside the shape if the bird turned there. Single-target moves keep picking nearest.</item>
        /// </list>
        /// </summary>
        private void ApplyAimForFire(int slot, in PlayerNetworkInput input)
        {
            if (_controller == null) return;
            _controller.CastAim = Vector3.zero;

            var ability = GetSlot(slot);
            if (ability == null || !ability.UsesAim) return;

            Vector2 dir = CluckWars.Input.AimQuantizer.Decode(input.Aim);
            if (dir == Vector2.zero && ability.RotatesToAim)
            {
                float halfAngle = input.Buttons.IsSet((int)InputButton.SoftLockTouch) ? CluckWars.Input.AimSoftLock.TouchHalfAngleDegrees
                                : input.Buttons.IsSet((int)InputButton.SoftLockPad) ? CluckWars.Input.AimSoftLock.PadHalfAngleDegrees
                                : 0f;
                if (halfAngle > 0f) dir = PickSoftLockDirection(ability, halfAngle);
            }
            if (dir == Vector2.zero) return;

            var world = new Vector3(dir.x, 0f, dir.y);
            if (ability.RotatesToAim) _controller.FaceNow(world);
            else _controller.CastAim = world;
        }

        /// <summary>Direction to the eligible rival with the smallest angle to the bird's facing within
        /// <paramref name="halfAngleDegrees"/>, or zero. Eligible = would be hit if the bird faced them.</summary>
        private Vector2 PickSoftLockDirection(AbilityBaseSO ability, float halfAngleDegrees)
        {
            var all = ChickenController.ActiveControllers;
            Vector3 origin = _controller.transform.position;
            int n = 0;
            for (int i = 0; i < all.Count && n < _softLockOffsets.Length; i++)
            {
                var rival = all[i];
                if (rival == null || rival == _controller) continue;

                Vector3 to = rival.transform.position - origin;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) continue;
                if (!ability.WouldAffect(_controller, rival, to.normalized)) continue;

                _softLockOffsets[n++] = new Vector2(to.x, to.z);
            }

            Vector3 facing = _controller.transform.forward;
            int best = CluckWars.Input.AimSoftLock.PickClosestAngle(
                new Vector2(facing.x, facing.z), _softLockOffsets, n, halfAngleDegrees);
            return best < 0 ? Vector2.zero : _softLockOffsets[best].normalized;
        }

        /// <summary>The slot holding Peck, or <see cref="InvalidSlot"/> (the Assassin cannot forage).</summary>
        private int FindPeckSlot()
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetSlot(i) is PeckAbilitySO) return i;
            return InvalidSlot;
        }

        /// <summary>True when some running slot other than Peck would end on the next move (Invisibility, Smoke
        /// Roost's fade): Auto-Peck must not silently break the stealth the player just cast.</summary>
        private bool AnyStealthRunning(int peckSlot)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (i == peckSlot || !IsSlotActive(i)) continue;
                var running = GetSlot(i);
                if (running != null && running.EndsOnNextMove) return true;
            }
            return false;
        }

        private void StopPeckChain()
        {
            if (PeckChainActive) PeckChainActive = false;
        }

        /// <summary>
        /// Phase 6 chunk 4 (A6): the Peck auto-chain and Auto-Peck, decided here on the state authority from the
        /// networked input. A manual Peck that succeeded this tick starts the chain; Auto-Peck starts one after
        /// <see cref="PeckChainRules.AutoPeckIdleSeconds"/> of standing still by a pile. Repeats go through
        /// <see cref="TryActivate"/> like any press, so cooldown, food credit and per-peck feedback are unchanged.
        /// The pure decisions live in <see cref="PeckChainRules"/>.
        /// </summary>
        private void UpdatePeckChain(in PlayerNetworkInput input, bool canCast)
        {
            int peckSlot = FindPeckSlot();
            bool moving = PeckChainRules.IsMoving(input.Movement);
            bool otherCast = _castSlotThisTick != InvalidSlot && _castSlotThisTick != peckSlot;

            _idleSeconds = PeckChainRules.AdvanceIdle(_idleSeconds, moving, Runner.DeltaTime);

            // A knockback impulse (the networked event, not mere body-to-body pushes) ends the chain.
            byte knockbackId = _controller != null ? _controller.KnockbackEventId : _lastKnockbackId;
            bool knocked = knockbackId != _lastKnockbackId;
            _lastKnockbackId = knockbackId;
            // Any other move counts as activity too, so Auto-Peck waits a beat after it instead of pecking at once.
            if (otherCast) _idleSeconds = 0f;

            if (peckSlot == InvalidSlot)
            {
                StopPeckChain();
                return;
            }

            var cargo = _controller != null ? _controller.Cargo : null;
            bool cargoFull = cargo != null && cargo.Cargo >= cargo.Capacity;
            // IsUsable also demands room in the hold; the rules judge cargoFull first, so a full bird reads
            // CargoFull (with its sound), not NoPile.
            bool pileInReach = GetSlot(peckSlot).IsUsable(_controller);
            bool peckReady = IsReady(peckSlot) && !IsSlotActive(peckSlot);

            if (!PeckChainActive)
            {
                if (_castSlotThisTick == peckSlot)
                {
                    if (!moving) PeckChainActive = true; // a manual peck starts the chain
                    return;
                }

                if (PeckChainRules.ShouldAutoStart(input.Buttons.IsSet((int)InputButton.AutoPeck), _idleSeconds,
                        chainActive: false, pileInReach, cargoFull, canCast, peckReady, otherCast,
                        aiming: IsAimGestureLive, wouldBreakStealth: AnyStealthRunning(peckSlot)) &&
                    TryActivate(peckSlot))
                {
                    PeckChainActive = true;
                    _log?.Debug(Source, "Auto-Peck started a peck.");
                }
                return;
            }

            var state = new PeckChainInputs
            {
                Active = true, MatchRunning = true, Alive = true, Moving = moving, CargoFull = cargoFull,
                PileInReach = pileInReach, CanCast = canCast, Knocked = knocked, OtherAbilityCast = otherCast, PeckReady = peckReady,
            };
            switch (PeckChainRules.Decide(state, out var stop))
            {
                case PeckChainAction.Stop:
                    PeckChainActive = false;
                    // Nothing else plays CargoFull in gameplay (ChickenVFX only draws particles), so no double-play.
                    if (stop == PeckChainStop.CargoFull)
                        PlayLocalSfx(_audioReg != null ? _audioReg.CargoFull : null, 1f);
                    _log?.Debug(Source, $"Peck chain ended: {stop}.");
                    break;
                case PeckChainAction.Fire:
                    if (!TryActivate(peckSlot))
                    {
                        PeckChainActive = false;
                        _log?.Debug(Source, "Peck chain ended: the repeat was refused.");
                    }
                    break;
            }
        }

        /// <summary>True while the player has any pre-fire gesture in flight — a committed,
        /// networked charge or a local pending hold. The single condition every "tear the
        /// aim down" path checks, so neither state can be left behind by the other.</summary>
        private bool IsAimGestureLive => ChargingSlot != 0 || _pendingHoldSlot != InvalidSlot;

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
            float cd = ResolveCooldownFor(ability);
            if (ability == null || cd <= 0f) return 1f;
            var remaining = CooldownRemaining(slot);
            return 1f - Mathf.Clamp01(remaining / cd);
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

        /// <summary>
        /// One-shot flag: true (once) when a target-gated move was released with nobody in range (Phase 6, A3).
        /// Distinct from <see cref="TryConsumeDeniedPress"/>: the HUD plays the slash flash + whiff puff for it.
        /// A fizzle burns no cooldown and is not a move used (see <see cref="TryActivate"/>).
        /// </summary>
        public bool TryConsumeFizzle(out int slot)
        {
            if (_fizzlePending)
            {
                slot = _fizzleSlot;
                _fizzlePending = false;
                return true;
            }
            slot = InvalidSlot;
            return false;
        }

        /// <summary>True for <see cref="AbilityFizzleRules.RearmSeconds"/> after a fizzle on <paramref name="slot"/>.</summary>
        public bool IsRearming(int slot) =>
            slot >= 0 && slot < SlotCount && AbilityFizzleRules.IsRearming(_rearmRemaining[slot]);

        /// <summary>True while <paramref name="slot"/> was cancelled / switched away from and its key is still down;
        /// the local preview ignores such a slot so it does not keep drawing an aim the machine has dropped.</summary>
        public bool IsHoldSuppressed(int slot) => slot >= 0 && slot < SlotCount && _holdSuppressed[slot];

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
        /// Bot-only activation API. Mirrors the player gates (cooldown / stun) and additionally refuses
        /// while any of the bot's own slots is running — bots never overlap abilities.
        /// Returns <c>true</c> if the ability fired this call.
        /// Must be called from the StateAuthority (bot FSM already guards this).
        /// </summary>
        public bool BotTryActivate(int slot)
        {
            if (!HasStateAuthority) return false;
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return false;
            if (_combat != null && _combat.IsRemoved) return false;
            // Bots keep the one-at-a-time rule: no bot behaviour change, SCT untouched. Players are not gated.
            if (AnyAbilityActive) return false;
            if (!IsReady(slot)) return false;
            TryActivate(slot);
            return IsSlotActive(slot);
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
        /// The cooldown <paramref name="slot"/> will actually charge this chicken if fired
        /// now — the ability's per-caster value with the class specialization applied
        /// (Relentless). 0 for an empty slot.
        /// </summary>
        /// <remarks>
        /// Exposed for <c>BotTactics.ScoreCastCandidate</c>, which breaks ties inside one
        /// <see cref="BotRole"/> by preferring the cheaper tool. It has to be the
        /// <i>resolved</i> value rather than <c>AbilityBaseSO.Cooldown</c>: a Relentless
        /// Warrior's Wing Slam is materially cheaper than the authored number, and ranking
        /// on the raw field would have the bot avoid the very ability its specialization
        /// exists to make spammable.
        /// </remarks>
        public float ResolvedCooldownFor(int slot) => ResolveCooldownFor(GetSlot(slot));

        /// <summary>
        /// Assigns ability assets before <see cref="Spawned"/> runs.
        /// Called by <see cref="MatchBootstrapper"/> inside the <c>onBeforeSpawned</c>
        /// callback so every peer already has the chosen abilities on first <c>Spawned</c>
        /// read. Null arguments leave the existing (prefab-default) value unchanged.
        /// </summary>
        public void SetSlots(PassiveAbilitySO passive, AbilityBaseSO slot0, AbilityBaseSO slot1,
            AbilityBaseSO slot2 = null, AbilityBaseSO slot3 = null)
        {
            // A loadout swap on a live chicken must end what the OLD assets are running first, while the slots
            // still point at them; otherwise their modifiers / traversal holds would outlive the swap.
            if (_initialized && HasStateAuthority) DeactivateAll();

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

        /// <summary>
        /// The cooldown this chicken actually pays for <paramref name="ability"/>: the
        /// ability's own per-caster value, then the class specialization's say (Relentless).
        /// </summary>
        /// <remarks>
        /// Every cooldown read in this class goes through here — the timer that starts the
        /// lockout and the radial fill that draws it. Applying the passive in one but not the
        /// other would draw a hex whose sweep finishes at a different moment than the ability
        /// is actually ready, which reads as an unresponsive button.
        /// </remarks>
        private float ResolveCooldownFor(AbilityBaseSO ability)
        {
            if (ability == null) return 0f;
            float seconds = ability.ResolveCooldown(_controller);
            var passive = _controller != null ? _controller.Passive : null;
            return passive != null ? passive.ModifyCooldown(seconds, ability, _controller) : seconds;
        }

        /// <summary>
        /// The ability equipped in <paramref name="slot"/>, or null for an empty or
        /// out-of-range slot. The single slot-index → ability mapping in the codebase.
        /// </summary>
        /// <remarks>
        /// <b>Public on purpose, and it must stay public.</b> This was private, so
        /// <c>AbilityRangeIndicator</c> reimplemented the mapping as a local
        /// <c>SlotAbility(int)</c> ternary over slots 0/1/2 — and when the roster went to
        /// four slots (<see cref="SlotCount"/>), the copy went stale and slot 3's range was
        /// never drawn at all. Any consumer that needs "what is in slot N" calls this and
        /// iterates <c>0..SlotCount-1</c>; nobody re-derives it and nobody writes a literal
        /// slot count.
        /// </remarks>
        public AbilityBaseSO GetSlot(int slot) => slot switch
        {
            0 => _slot0,
            1 => _slot1,
            2 => _slot2,
            3 => _slot3,
            _ => null,
        };

        private TickTimer GetActiveTimer(int slot) => slot switch
        {
            0 => ActiveTimer0,
            1 => ActiveTimer1,
            2 => ActiveTimer2,
            3 => ActiveTimer3,
            _ => default,
        };

        private void SetActiveTimer(int slot, TickTimer timer)
        {
            if (slot == 0)      ActiveTimer0 = timer;
            else if (slot == 1) ActiveTimer1 = timer;
            else if (slot == 2) ActiveTimer2 = timer;
            else if (slot == 3) ActiveTimer3 = timer;
        }

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

            // A slot that is still running reads as "cooling": re-casting the same slot while it runs stays
            // refused (its cooldown outlasts its duration, so this only guards a Relentless-shortened edge).
            // Other slots running do NOT refuse anything — abilities run concurrently.
            bool onCooldown = !slotUnavailable &&
                              (!GetCooldown(slot).ExpiredOrNotRunning(Runner) || IsSlotActive(slot));
            bool stunned = _controller != null && !ControlRules.CanCast(_controller.CurrentControlState);
            bool noTarget = !slotUnavailable && !onCooldown && !stunned &&
                             ability != null && !ability.IsUsable(_controller);

            return AbilityRefusalRules.Evaluate(slotUnavailable, onCooldown, stunned, noTarget);
        }

        /// <summary>
        /// SlotUnavailable + Cooldown only — the two refusal reasons that are stable for
        /// an entire hold and would make charging pointless. Deliberately excludes
        /// Stunned (already gated before the hold state machine ever
        /// runs, see FixedUpdateNetwork) and NoTarget (a target may walk into the shape
        /// mid-hold — that is the whole point of aiming). Used only to decide whether a
        /// hold is even worth starting to charge.
        /// </summary>
        private bool CanBeginCharge(int slot)
        {
            if (slot < 0 || slot >= EquippedSlotCount) return false;
            if (GetSlot(slot) == null) return false;
            if (IsRearming(slot)) return false;
            return GetCooldown(slot).ExpiredOrNotRunning(Runner);
        }

        /// <summary>Local-only sound: only the chicken a human is driving plays it (never bots or remote peers).</summary>
        private void PlayLocalSfx(AudioClip clip, float volume)
        {
            if (clip == null || !Object.HasInputAuthority) return;
            if (_controller != null && _controller.IsBot) return;
            _audio?.PlaySFX(clip, volume);
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

        /// <summary>Runs the full activation path for <paramref name="slot"/>. True only if the move actually cast.</summary>
        private bool TryActivate(int slot)
        {
            // Re-arm window after a fizzle: no press may start a cast, whichever path it arrived by.
            if (IsRearming(slot))
            {
                _log?.Debug(Source, $"TryActivate slot {slot}: refused (re-arming after a fizzle).");
                _deniedPressPending = true;
                _deniedPressSlot = slot;
                return false;
            }

            var reason = EvaluateRefusalInternal(slot, out var ability);
            if (reason != AbilityRefusal.None)
            {
                _log?.Debug(Source, $"TryActivate slot {slot}: refused ({reason}).");
                if (AbilityFizzleRules.IsFizzle(reason))
                {
                    // Nobody in range on release: no effect, no cooldown (we return before any is set), and NOT a
                    // move used. Phase 6 chunk 3 makes casting end stealth; that logic sits BELOW this return,
                    // so a fizzle never reaches it.
                    _fizzlePending = true;
                    _fizzleSlot = slot;
                    _rearmRemaining[slot] = AbilityFizzleRules.Begin();
                    PlayLocalSfx(_audioReg != null ? _audioReg.AbilityFizzle : null, FeedbackTuning.FizzleSfxVolume);
                    return false;
                }
                _deniedPressPending = true;
                _deniedPressSlot = slot;
                return false;
            }

            // Refresh context fields that abilities need for NetworkObject spawning. This has
            // to happen before CanActivate, not merely before OnActivate: the zone abilities'
            // CanActivate reads exactly these two fields (via AbilityContext.CanSpawnZone), so
            // refreshing afterwards would have the gate judge a stale context and refuse every
            // first cast.
            _ctx.Runner         = Runner;
            _ctx.PrefabRegistry = _prefabRegistry;

            if (!ability.CanActivate(_ctx))
            {
                // No log line here on purpose. CanActivate's contract is that the override has
                // already logged an Error naming the exact missing reference; a second line
                // would just be noise pointing at the same bug.
                _deniedPressPending = true;
                _deniedPressSlot = slot;
                return false;
            }

            // "Using a move breaks it" (Phase 6 chunk 3). Only a SUCCESSFUL cast gets here: the fizzle return, every
            // refusal and CanActivate above, and any hold / preview never reach this line. Stealth (Invisibility,
            // Smoke Roost's fade) and Peck's lock end first, so the new cast's own modifiers apply to a clean
            // stack — a Smoke Roost cast during Invisibility fades again.
            EndStealthAndPeckForCast(slot);

            ActiveMask = AbilityRunRules.Begin(ActiveMask, slot);
            SetActiveTimer(slot, TickTimer.CreateFromSeconds(Runner, ability.Duration));
            SetCooldown(slot, TickTimer.CreateFromSeconds(Runner, ResolveCooldownFor(ability)));
            LastCastSlot = slot;

            if (ability.TerrainTraversal != TerrainTraversal.None && _controller != null)
            {
                _controller.Traversal?.Begin(ability.TerrainTraversal, slot);
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

            _ctx.Slot = slot;
            try { ability.OnActivate(_ctx); }
            finally { _ctx.Slot = -1; }
            _controller?.SyncAbilityEffects();

            // The deferred half of the ordering documented above: a capsule ability has now
            // resolved and applied its lane from the take-off pose, so it may travel.
            if (ability.ResolvesBeforeJump) ExecuteJumpIfAny(ability);

            // Peck keeps its own animation beat, and deliberately does not route through
            // CastArchetype: foraging is frequent enough that reusing a combat cast reads as a bug.
            // Everything else declares one of the eight archetypes on its asset, which selects the
            // matching animator state; the casting class's own clip is bound to that state by the
            // AnimatorOverrideController built at spawn.
            if (ability is Abilities.PeckAbilitySO)
            {
                _animator?.TriggerPeck();
            }
            else
            {
                if (ability.CastMotion == Abilities.CastArchetype.None)
                {
                    // None marks an ability that is never cast -- what the eight specializations
                    // carry. Reaching an activation with it means a passive has been routed through
                    // the active path, or an ability asset was authored without an archetype. Either
                    // is a wiring bug, and the cast silently playing nothing is exactly how it would
                    // otherwise go unnoticed.
                    _log?.Warn(Source, $"'{ability.DisplayName}' activated with CastArchetype.None, " +
                        "which marks an ability that is never cast. No cast animation will play. " +
                        "Give it an archetype, or keep it off the activation path.");
                }
                _animator?.TriggerAbilityCast(ability.CastMotion);
            }
            _audio?.PlaySFX(_audioReg != null ? _audioReg.AbilityActivate : null);
            LastCastEventId++; // wraps at 255 by design (byte overflow) — a one-shot signal, not a counter
            _log?.Info(Source, $"Activated slot {slot} ({ability.DisplayName}) for {ability.Duration:0.00}s, " +
                $"CD {ResolveCooldownFor(ability):0.00}s, hits={hitCount}.");
            _castSlotThisTick = slot;
            return true;
        }

        /// <summary>
        /// Runs <paramref name="ability"/>'s length-based teleport jump, if it has one. Pure
        /// extraction from <see cref="TryActivate"/> so the two orderings above can share one
        /// implementation instead of duplicating the resolve+apply pair.
        /// </summary>
        /// <remarks>
        /// Also the sole publisher of <c>ChickenController.JumpEventId</c>, which is what tells
        /// every peer's local visuals that this particular position discontinuity was a jump and
        /// not a round-reset teleport. Bumped only when the resolver actually moved the chicken:
        /// a jump that resolves to zero distance (pressed flat against a wall) is not a travel,
        /// and firing a landing impact for it would announce a movement that did not happen.
        /// </remarks>
        private void ExecuteJumpIfAny(AbilityBaseSO ability)
        {
            if (ability.JumpTier == JumpLengthTier.None) return;
            if (_controller == null || _controller.Traversal == null) return;

            var t = _controller.transform;
            var jump = _controller.Traversal.ExecuteJump(
                ability.JumpTier, t.position, t.forward, MapGenerator.ArenaHalfSize);
            _controller.Traversal.ApplyJump(jump);

            if (jump.EffectiveDistance > 0f) _controller.JumpEventId++;
        }

        /// <summary>
        /// Ends the run in <paramref name="slot"/>: removes that ability's own effect modifiers (never anyone
        /// else's), releases its traversal hold, and clears its active flag. Idempotent for a slot not running.
        /// Only <paramref name="natural"/> expiry plays the expire sound (and only for a human-driven chicken);
        /// death, round reset, a loadout swap and the stealth / Peck break end a slot silently so they don't
        /// stack on top of the sound the new cast already plays.
        /// </summary>
        private void Deactivate(int slot, bool natural = false)
        {
            if (!IsSlotActive(slot)) return;

            var ability = GetSlot(slot);
            System.Action<int> endTraversal = _controller != null && _controller.Traversal != null
                ? _controller.Traversal.End
                : null;
            AbilityRunRules.ReleaseSlot(slot, ability, _ctx, _controller != null ? _controller.Effects : null, endTraversal);
            if (ability != null)
            {
                if (natural) PlayLocalSfx(_audioReg != null ? _audioReg.AbilityExpire : null, 1f);
                _log?.Debug(Source, $"Deactivated {ability.DisplayName}.");
            }
            ActiveMask = AbilityRunRules.End(ActiveMask, slot);
            _controller?.SyncAbilityEffects();
        }

        /// <summary>Ends every running slot (match stop, removal, death, reset, loadout swap).</summary>
        private void DeactivateAll()
        {
            StopPeckChain();
            if (!AnyAbilityActive) return;
            for (int i = 0; i < SlotCount; i++) Deactivate(i);
        }

        /// <summary>
        /// Round reset: ends every running ability through its own OnDeactivate so its modifiers, traversal hold
        /// and any ability-owned state go with it. Authority only; the caller keeps <c>Effects.Clear</c> as a
        /// backstop afterwards.
        /// </summary>
        public void EndAllForReset()
        {
            if (!HasStateAuthority || !_initialized) return;
            DeactivateAll();
        }

        /// <summary>
        /// Authority migration (Shared Mode): the effect stack and traversal holders are authority-local, so a
        /// peer that has just gained authority has an EMPTY stack while the networked ActiveMask may still say
        /// slots run, and may hold stale modifiers from an earlier stint as authority. Simplest correct option:
        /// end whatever the mask says runs (those lose their remaining time) and scrub every slot's modifiers.
        /// </summary>
        private void OnAuthorityGained()
        {
            DeactivateAll();
            if (_controller == null) return;
            for (int i = 0; i < SlotCount; i++)
            {
                _controller.Effects.RemoveSource(i);
                _controller.Traversal?.End(i);
            }
            _controller.SyncAbilityEffects();
        }

        /// <summary>
        /// A successful cast of <paramref name="castSlot"/> ends every running stealth ability and Peck's lock.
        /// Smoke Roost's cloud is a separate zone and stays; only its fade (the opacity modifier) ends.
        /// </summary>
        private void EndStealthAndPeckForCast(int castSlot)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var running = GetSlot(i);
                _flagScratch[i] = running != null && running.EndsOnNextMove;
            }
            byte ended = AbilityRunRules.EndedByCast(ActiveMask, castSlot, _flagScratch);
            for (int i = 0; i < SlotCount; i++)
            {
                if (AbilityRunRules.IsActive(ended, i)) Deactivate(i);
            }
        }

        private void HandleOwnerDeath(NetworkBehaviourId attackerId)
        {
            if (!HasStateAuthority) return;
            DeactivateAll();
            // Tear the aim down on the same frame as the death rather than a tick later via
            // FixedUpdateNetwork's IsRemoved branch — otherwise a chicken that dies mid-hold
            // keeps its wind-up glow for one visible tick after it has already fallen over.
            if (IsAimGestureLive) CancelCharge();
        }
    }
}
