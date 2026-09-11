using CluckWars.Abilities;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Tags which system is currently contributing a slow to a chicken.
    /// Multiple sources can be active simultaneously; the minimum multiplier wins.
    /// Tracked so a future passive can exempt a specific source (e.g. a passive
    /// that ignores pile slow but not ability slow).
    /// </summary>
    [System.Flags]
    public enum SlowSource : byte
    {
        None      = 0,
        Collision = 1 << 0, // Two chickens brushing each other (GDD §6.1).
        Pile      = 1 << 1, // Standing on a food pile while collecting (GDD §6.2).
        Ability   = 1 << 2, // Applied by an ability (e.g. Feather Trap).
    }

    /// <summary>
    /// Compact, replicated control-state flags used purely to drive on-target VFX
    /// (the slow/root ground rings) on every peer. The gameplay effect itself
    /// already replicates via the networked transform (a slowed/rooted chicken
    /// moves differently, which every peer sees) — only this minimal trigger
    /// crosses the wire so the *particles/rings stay local* on each client.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Slowed"/> and <see cref="Snared"/> are one state split in two, not two
    /// states.</b> <see cref="Slowed"/> means "slowed at all"; <see cref="Snared"/> adds "at
    /// least one contributing source has enemy agency" — i.e. <see cref="SlowSource.Ability"/>
    /// (Feather Trap, Feather Aura, Dust Kick, Smoke Roost). A slow with no
    /// <see cref="Snared"/> bit is <i>Drag</i>: <see cref="SlowSource.Pile"/> or
    /// <see cref="SlowSource.Collision"/>, ambient friction with nobody to blame and no
    /// deadline.
    /// <para>
    /// <b><see cref="Snared"/> is only ever set alongside <see cref="Slowed"/></b> — see the
    /// single publish site in <see cref="ChickenController.FixedUpdateNetwork"/>. Consumers
    /// may therefore test one bit without re-deriving the other, and "Drag" is exactly
    /// <c>Slowed &amp;&amp; !Snared</c>. <c>SlowSourceTests</c> pins both halves.
    /// </para>
    /// <para>
    /// A fourth bit in a byte that used three costs zero bandwidth, which is why the
    /// distinction crosses the wire at all rather than being re-derived per peer:
    /// <c>_activeSlowSources</c> is StateAuthority-side only, so no remote peer could work
    /// out on its own whether the rival it is looking at was trapped or is just standing on
    /// a food pile.
    /// </para>
    /// </remarks>
    [System.Flags]
    public enum ControlVfx : byte
    {
        None   = 0,
        Slowed = 1 << 0,
        Rooted = 1 << 1,
        Stunned = 1 << 2,

        /// <summary>Slowed by a source with enemy agency (<see cref="SlowSource.Ability"/>).
        /// Never set without <see cref="Slowed"/>.</summary>
        Snared = 1 << 3,
    }

    /// <summary>
    /// Where a displacement impulse came from. The impulse itself is applied identically
    /// either way — this decides nothing about physics, only which replicated one-shot the
    /// impact fires on, and therefore which story the feedback system tells about it.
    /// </summary>
    /// <remarks>
    /// <b>Named for the concept, not for Feint.</b> Feint is the only self-applied impulse in
    /// the game today, but the thing that needed separating is "I moved myself" from "somebody
    /// moved me", and the next dodge or recoil-shove belongs on the same signal rather than on
    /// a second bespoke one.
    ///
    /// <b>Why it exists.</b> Every peer reads <see cref="ChickenController.KnockbackEventId"/>
    /// as a landed hit: <c>HitFeedback.ObserveKnockback</c> plays the white body flash, the
    /// animator's recoil and the victim-tier camera shake FEEDBACK.md §3.1 reserves for being
    /// hit, and <c>ControlStateVFX.ObserveKnockbackEdge</c> draws the ground shockwave off the
    /// same byte. Nothing attributes a self-applied impulse, so no direction lines follow it —
    /// and flash plus recoil plus a big shake with no attacker is the exact signature of being
    /// hit from off-screen. Speedy's own dodge was speaking the game's loudest "I am under
    /// attack" cue, to its caster and to every bystander.
    /// </remarks>
    public enum ImpulseOrigin : byte
    {
        /// <summary>Somebody did this to you. Fires <see cref="ChickenController.KnockbackEventId"/>.</summary>
        Hit = 0,

        /// <summary>You did this to yourself. Fires <see cref="ChickenController.SelfImpulseEventId"/>.</summary>
        SelfApplied = 1,
    }

    /// <summary>
    /// Top-level networked chicken. Class-aware: a <c>[Networked]</c>
    /// <see cref="Class"/> selects the active <see cref="ChickenStatsSO"/> from
    /// the injected registry, with the prefab's serialized <c>_stats</c> kept only
    /// as a safety fallback.
    /// </summary>
    /// <remarks>
    /// State authority drives motion via the Fusion input buffer; pure-MonoBehaviour
    /// helpers (animator, visuals) stay local and react to <c>[Networked]</c> state.
    ///
    /// v0.3: added <see cref="SlowMultiplier"/>, <see cref="Rooted"/>,
    /// <see cref="ExternalDisplacement"/> for the Interaction &amp; Control System
    /// (GDD §6). Also added passive hooks: <see cref="ApplySlow"/>,
    /// <see cref="ApplyKnockback"/>, <see cref="ApplyOutgoingDamage"/>.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ChickenController : NetworkBehaviour
    {
        private const string Source = "Chicken";

        // ---- Passive tuning constants (balance-pass values; Part B / test session) ---
        private const float CollisionSlowRadius      = 1.2f;  // metres — two chickens touching
        private const float CollisionSlowFactor      = 0.75f; // GDD TBD #7
        private const float PileSlowFactor           = 0.80f; // GDD TBD #6

        /// <summary>
        /// <see cref="SlowMultiplier"/> below which the replicated <see cref="ControlVfx.Slowed"/>
        /// bit is raised — i.e. the point at which a slow becomes visible to peers at all.
        /// Named rather than inlined so the single publish site and
        /// <c>SlowSourceTests</c> read the same number instead of restating a literal.
        /// </summary>
        /// <remarks>
        /// Every shipping slow source is authored well below this (0.80 pile, 0.75 collision,
        /// 0.45–0.55 abilities), so the band between here and 1.0 is currently unreachable.
        /// That is exactly why it needs a test: a source authored <i>inside</i> that band
        /// would be a real slow that no remote peer can see. See <see cref="ApplySlow"/>.
        /// </remarks>
        public const float SlowVfxFlagThreshold = 0.92f;
        private const float ToughDamageBonus         = 1.25f; // 25% bonus outgoing damage for Tough
        private const float AuraSlowSearchRadius     = 10f;   // broadphase for CheckAuraSlow

        public static readonly System.Collections.Generic.List<ChickenController> ActiveControllers = new System.Collections.Generic.List<ChickenController>();

        [Tooltip("Fallback used only if the class registry is missing or has no entry for this chicken's class.")]
        [SerializeField] private ChickenStatsSO _fallbackStats;

        private CharacterController _characterController;
        private ChickenMovement _movement;
        private ChickenTraversal _traversal;
        private ChickenClassRegistrySO _registry;
        private ChickenStatsSO _activeStats;
        private ChickenCombat _combat;
        private ChickenCargo _cargo;
        private AbilityController _abilities;
        private ILogService _log;

        // True once the class's avatar AND all five clips are bound, i.e. the Animator is really
        // driving the skeleton. Drives ChickenAnimator's two-path split — see SetSkeletalActive.
        private bool _skeletalAnimationActive;

        // Edge latch for the missing-ChickenMovement report in FixedUpdateNetwork. Local, not
        // networked: the condition and the log are both StateAuthority-side. Reset when
        // _movement comes back, so a genuinely intermittent failure reports each occurrence
        // instead of only the first.
        private bool _movementMissingReported;

        public ChickenTraversal Traversal => _traversal;

        // Slow accumulation — reset to None/1 at top of each FixedUpdateNetwork.
        private SlowSource _activeSlowSources;

        // Timer-based ability slow (StateAuthority-side; set by RPC_ApplyAbilitySlow).
        private double _abilitySlowUntil  = double.MinValue;
        private float  _abilitySlowFactor = 1f;

        /// <summary>The chicken's archetype. Replicated; set by the spawner via <c>OnBeforeSpawned</c>.</summary>
        [Networked] public ChickenClass Class { get; set; } = ChickenClass.Warrior;

        public ChickenStatsSO Stats => _activeStats != null ? _activeStats : _fallbackStats;
        public ChickenCombat Combat => _combat;
        public ChickenCargo Cargo => _cargo;
        public AbilityController Abilities => _abilities;

        // ---- Ability state (StateAuthority-side only) -------------------------
        // Abilities mutate these locally on the StateAuthority. Other peers don't
        // need to mirror these values — they observe the resulting [Networked]
        // position / HP changes instead.

        /// <summary>Multiplier applied to <c>Stats.MoveSpeed</c> by active abilities. 1 = no buff.</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// Multiplier on banking speed, driven by an ABILITY rather than a passive.
        /// 1 = normal.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="MoveSpeedMultiplier"/>: set in an ability's <c>OnActivate</c>,
        /// reset in <c>OnDeactivate</c>. It exists because banking speed previously had exactly
        /// one lever — <c>PassiveAbilitySO.ModifyDepositRate</c> — and a passive is free, whereas
        /// Drop and Go was measured at roughly <b>27% of Speedy's SCT</b>. Maestro's call on
        /// 2026-08-23 was to make that tempo cost a slot and a cooldown instead, which needs a
        /// route an ability can actually reach.
        ///
        /// Consumed alongside the passive hook in <c>ChickenCargo.ResolveDepositRate</c>, so the
        /// two stack multiplicatively and neither silently overrides the other.
        /// </remarks>
        public float DepositRateMultiplier { get; set; } = 1f;

        /// <summary>While true, <c>ChickenMovement</c> ignores planar input but keeps gravity.</summary>
        public bool MovementLocked { get; set; }

        /// <summary>
        /// 0 = invisible, 1 = fully opaque. Networked so the fade is visible to every player.
        /// </summary>
        [Networked] public float VisualOpacity { get; set; }

        /// <summary>True for the Doppelganger decoy: skips input processing.</summary>
        public bool IsDecoy { get; set; }

        /// <summary>True for AI-controlled bots spawned in solo mode.</summary>
        [Networked] public bool IsBot { get; set; }

        /// <summary>Spine Coat steal-back active hook.</summary>
        [Networked] public bool StealBackActive { get; set; }
        public float StealBackAmount { get; set; } = 4f;
        /// <summary>
        /// Spine Coat's shove-back impulse, world-units/sec. Scaled 8 -> 10.8 (x1.35) with
        /// the 2026-08-14 arena/move-speed rescale, for the same reason as the ability
        /// knockbacks — see <c>CluckShockAbilitySO.KnockbackForce</c>.
        /// </summary>
        public const float StealBackKnockback = 10.8f;
        private readonly System.Collections.Generic.Dictionary<NetworkBehaviourId, TickTimer> _stealBackCooldowns = new();

        /// <summary>
        /// Replicated slow/root state, set on the StateAuthority each tick. Read by
        /// <c>ControlStateVFX</c> on every peer to drive the local ground rings — the
        /// VFX themselves never cross the wire, only this flag does.
        /// </summary>
        [Networked] public ControlVfx ControlFlags { get; set; }

        /// <summary>
        /// Bumped on the StateAuthority each time a knockback impulse is applied. A
        /// one-shot networked "event" — peers watch for the change and fire the local
        /// knockback shockwave once. Wraps at 255 (only the change matters).
        /// </summary>
        [Networked] public byte KnockbackEventId { get; set; }

        /// <summary>
        /// Bumped on the StateAuthority each time this chicken applies a displacement impulse
        /// to <i>itself</i> (<see cref="ImpulseOrigin.SelfApplied"/> — Feint's sidestep). Same
        /// one-shot pattern as <see cref="KnockbackEventId"/>, and deliberately a separate
        /// counter rather than a flag beside it.
        /// </summary>
        /// <remarks>
        /// <b>A separate counter, not a companion flag.</b> A "was that one self-inflicted"
        /// bool sitting next to <see cref="KnockbackEventId"/> would be read at whatever tick
        /// the observing peer happens to render, which is not necessarily the tick that bumped
        /// the id — two impulses landing between two rendered frames would classify one of
        /// them by the other's flag. Two counters cannot desynchronise like that: an observer
        /// watching one byte simply never sees the other's edge, which is precisely the
        /// behaviour wanted here.
        ///
        /// <b>Nothing observes it yet, and that is the point.</b> Feint's presentation is its
        /// ability accent burst and the caster micro-shake, both fired off the cast event —
        /// what it needed was for the victim beat to stop firing, which it gets by this edge
        /// landing on a byte <c>HitFeedback</c> and <c>ControlStateVFX</c> do not read. A
        /// future dodge puff belongs here; a victim flash never does.
        /// </remarks>
        [Networked] public byte SelfImpulseEventId { get; set; }

        /// <summary>
        /// Bumped on the StateAuthority each time a length-based teleport jump actually
        /// moves this chicken (<c>AbilityController.ExecuteJumpIfAny</c>). A one-shot
        /// networked "event" on the <see cref="KnockbackEventId"/> pattern — peers watch
        /// for the change and play the local travel catch-up and landing shockwave once.
        /// Wraps at 255 (only the change matters).
        /// </summary>
        /// <remarks>
        /// <b>This exists to be a discriminator, not a notification.</b> The mesh catch-up
        /// it drives is measured from a position discontinuity, and a jump is not the only
        /// thing that produces one: <see cref="RPC_TeleportTo"/> fires on every chicken at
        /// every round reset (<c>GameManager.RestartMatch</c>). A catch-up applied there
        /// would ease the model back across the whole arena toward the corner it was
        /// standing in a moment ago — a chicken skating home over 0.18 s, at a round
        /// boundary, which is exactly the instant nobody is looking closely enough to
        /// report it. Consumers therefore key on this id changing rather than on the
        /// discontinuity itself, and <see cref="RPC_TeleportTo"/> deliberately does not
        /// bump it.
        /// </remarks>
        [Networked] public byte JumpEventId { get; set; }

        /// <summary>
        /// Corner (0..3) this chicken spawned at — its match identity. Drives base
        /// ownership, deposit gating, leaderboard attribution, nameplate numbering,
        /// and restart teleports for humans and bots alike. Stamped by
        /// <c>MatchBootstrapper</c> in <c>onBeforeSpawned</c>; -1 = not yet assigned.
        /// </summary>
        [Networked] public int HomeCornerIndex { get; set; } = -1;

        [Networked] public NetworkBool UnderdogSurgeActive { get; set; }
        [Networked] public NetworkBool LeaderBountyActive { get; set; }

        // ---- v0.3 Feather Aura (Networked so every peer sees the caster's state) ---

        /// <summary>True while the Feather Aura ability is active on this chicken.
        /// Replicated so nearby chickens can self-apply the slow in their own FUN.</summary>
        [Networked] public bool  AuraSlowActive { get; set; }
        /// <summary>World-units radius of the active aura slow effect.</summary>
        [Networked] public float AuraSlowRadius { get; set; }
        /// <summary>Speed multiplier broadcast by the aura (applied to chickens inside the radius).</summary>
        [Networked] public float AuraSlowFactor { get; set; }

        // ---- v0.3 Control-state fields (GDD §6.4) ----------------------------

        /// <summary>
        /// Accumulated speed multiplier from all active slow sources this tick.
        /// 1 = no slow; reset to 1 at the top of each <c>FixedUpdateNetwork</c>
        /// and then re-populated by <see cref="ApplySlow"/> calls.
        /// Read by <see cref="ChickenMovement"/>.
        /// </summary>
        /// <remarks>
        /// v0.6 (FEEDBACK.md §5.2, case 18/19): unlike <see cref="StunRemaining"/> and
        /// <see cref="RootRemaining"/> below, Slow deliberately has NO countdown timer.
        /// It is re-derived from scratch every tick from whichever sources are currently
        /// touching this chicken (collision, pile, ability zone/aura — see
        /// <see cref="ApplySlow"/>) and has no fixed end time to count down to; two
        /// overlapping slow sources with different remaining durations would make any
        /// single "time left" number meaningless anyway. Stage 5's HUD shows the
        /// *magnitude* instead (a <c>×0.45</c>-style label read straight off this field).
        /// Don't "fix" this by inventing a slow-end timer — there isn't one to invent.
        /// </remarks>
        public float SlowMultiplier { get; set; } = 1f;

        /// <summary>
        /// Planar movement blocked (like <see cref="MovementLocked"/>) but abilities
        /// can still be cast while rooted. Gravity still runs.
        /// </summary>
        public bool Rooted { get; set; }

        /// <summary>
        /// True while general control stun is active. Blocks movement, casting, and collecting.
        /// </summary>
        [Networked] public bool IsStunned { get; private set; }
        [Networked] private TickTimer StunTimer { get; set; }

        /// <summary>Seconds of stun remaining, for the Aftermath countdown (FEEDBACK.md
        /// §5.1, case 18). 0 when not stunned/expired — every peer can read this, not
        /// just the StateAuthority, since <see cref="StunTimer"/> is <c>[Networked]</c>.</summary>
        public float StunRemaining => StunTimer.ExpiredOrNotRunning(Runner) ? 0f : (StunTimer.RemainingTime(Runner) ?? 0f);

        /// <summary>
        /// Timer-based root, set by <see cref="RPC_ApplyRoot"/>. Promoted from a plain
        /// StateAuthority-only <c>double</c> to a <c>[Networked] TickTimer</c> (v0.6,
        /// ~4 bytes) so <see cref="RootRemaining"/> below can read a countdown on every
        /// peer, the same way <see cref="StunTimer"/> already does — a remote peer
        /// couldn't show "how long is that rival rooted" otherwise.
        /// </summary>
        [Networked] private TickTimer RootTimer { get; set; }

        /// <summary>
        /// While running, this chicken ignores incoming stun, root, ability-slow and
        /// knockback outright. Driven by an ability (Immovable), not a passive.
        /// </summary>
        /// <remarks>
        /// <b>Immunity, not resistance — and that distinction is the design.</b> Passives
        /// already shorten control (Slippery, Bulwark) by scaling duration through
        /// <see cref="ApplyPassiveControlDuration"/>. Scaling can never reach zero, so a
        /// resistance-shaped Immovable would just be a bigger Bulwark. Fatty's essence is the
        /// "unstoppable force", which only reads if there is a window where control simply
        /// does not land.
        ///
        /// <c>[Networked]</c> and a <c>TickTimer</c> for the same reason
        /// <see cref="StunTimer"/> is: every peer must agree on whether a hit landed, or the
        /// victim and the attacker disagree about what just happened.
        ///
        /// Deliberately does NOT clear control already applied — walking into a stomp and then
        /// pressing the button is too strong. It stops the NEXT one.
        /// </remarks>
        [Networked] private TickTimer ControlImmuneTimer { get; set; }

        /// <summary>True while a control-immunity window is open. Readable on every peer.</summary>
        public bool IsControlImmune =>
            Runner != null && !ControlImmuneTimer.ExpiredOrNotRunning(Runner);

        /// <summary>Opens a control-immunity window of <paramref name="seconds"/>.</summary>
        public void GrantControlImmunity(float seconds)
        {
            if (Runner == null || seconds <= 0f) return;
            ControlImmuneTimer = TickTimer.CreateFromSeconds(Runner, seconds);
        }

        /// <summary>Seconds of root remaining, for the Aftermath countdown (FEEDBACK.md
        /// §5.1, case 18). 0 when not rooted/expired.</summary>
        public float RootRemaining => RootTimer.ExpiredOrNotRunning(Runner) ? 0f : (RootTimer.RemainingTime(Runner) ?? 0f);

        /// <summary>
        /// Resolved control state following the severity ladder (Stunned > Rooted > Slowed > Free).
        /// </summary>
        public ControlState CurrentControlState => IsStunned ? ControlState.Stunned : (Rooted ? ControlState.Rooted : (SlowMultiplier < 0.99f ? ControlState.Slowed : ControlState.Free));

        /// <summary>
        /// External velocity impulse (units per second) applied by knockback effects.
        /// Decayed to zero by <see cref="ChickenMovement"/> each tick.
        /// Set via <see cref="ApplyKnockback"/> to respect the Immovable passive.
        /// </summary>
        public Vector3 ExternalDisplacement { get; set; }

        /// <summary>
        /// Vertical velocity in world-units per second: gravity accumulation, and the -2 peg
        /// that holds a grounded chicken against the floor. Integrated by
        /// <see cref="ChickenMovement"/> on the state authority every tick.
        /// </summary>
        /// <remarks>
        /// <b><c>[Networked]</c> for rollback safety — do not demote this to a plain field.</b>
        /// It lives on the controller rather than inside <see cref="ChickenMovement"/> (a pure
        /// C# class, which cannot carry <c>[Networked]</c>) precisely so Fusion snapshots it.
        /// As a plain field it sat outside predicted state: every resimulated tick re-integrated
        /// gravity on top of a value the rollback never restored, so the accumulating branch
        /// drifted further down with each resim — the same runaway-descent failure mode
        /// <c>ChickenMovement.ClampInsideArena</c> documents for the <c>isGrounded</c> path,
        /// arrived at from the other direction. Written back through <c>_owner</c> the same way
        /// <see cref="ExternalDisplacement"/> is. Listed as item 4 of
        /// <c>docs/adr/0002-server-mode-migration-plan.md</c>.
        /// </remarks>
        [Networked] public float VerticalVelocity { get; set; }

        [Inject]
        public void Construct(ChickenClassRegistrySO registry, ILogService log)
        {
            _registry = registry;
            _log = log;
        }

        public override void Spawned()
        {
            ActiveControllers.Add(this);
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            _log?.Debug(Source, $"Spawned. Class={Class}, HasStateAuthority={HasStateAuthority}, registryBound={_registry != null}.");

            _characterController = GetComponent<CharacterController>();
            _combat = GetComponent<ChickenCombat>();
            _cargo = GetComponent<ChickenCargo>();
            _abilities = GetComponent<AbilityController>();
            _activeStats = ResolveStatsForClass(Class);

            if (_activeStats == null)
            {
                _log?.Error(Source, $"{name}: could not resolve stats for class '{Class}'. Assign _fallbackStats on the prefab or populate ChickenClassRegistry.");
                return;
            }

            _log?.Info(Source, $"Stats resolved: '{_activeStats.DisplayName}', moveSpeed={_activeStats.MoveSpeed}, " +
                $"specialization={(Passive != null ? Passive.DisplayName : "(none)")}.");
            _movement = new ChickenMovement(_characterController, this);
            _traversal = new ChickenTraversal(_characterController, _log);

            if (HasStateAuthority && VisualOpacity <= 0f) VisualOpacity = 1f;

            transform.localScale = Vector3.one * _activeStats.Scale;
            _log?.Debug(Source, $"Applied scale {_activeStats.Scale} for class {Class}.");

            if (_registry != null && _registry.TryGet(Class, out var entry))
            {
                var modelRoot = AttachClassModel(entry);

                var visuals = GetComponent<ChickenVisuals>();
                if (visuals != null)
                {
                    // Order matters: point the tint at the model before colouring it. The model
                    // did not exist when ChickenVisuals.Awake built its renderer list.
                    visuals.SetModelRoot(modelRoot);
                    visuals.ApplyTint(entry.TintColor, entry.TintStrength);
                    _log?.Debug(Source, $"Applied tint {entry.TintColor} at strength {entry.TintStrength:0.##} for class {Class}.");
                }

                // Hand the same model root to the animator helper, and tell it whether the
                // skeletal path came up. Without the model root it has nothing to offset;
                // without the skeletal flag it cannot know which of its two paths to run.
                var animator = GetComponent<ChickenAnimator>();
                if (animator != null)
                {
                    animator.SetModelRoot(modelRoot);
                    animator.SetSkeletalActive(_skeletalAnimationActive);
                }
            }
            else
            {
                _log?.Error(Source, $"{name}: no ChickenClassRegistry entry for class '{Class}' " +
                                    $"(registryBound={_registry != null}). This chicken spawns with no " +
                                    "model and no class tint — populate ChickenClassRegistry.asset.");
            }
        }

        /// <summary>
        /// Instantiates the class's model under this chicken and binds it to the root
        /// <see cref="Animator"/>. Local presentation only — nothing here is networked, so
        /// every peer builds its own copy from the replicated <see cref="Class"/>.
        /// </summary>
        /// <returns>The attached model's root, or null if the class has no model to attach.</returns>
        private Transform AttachClassModel(in ChickenClassRegistrySO.Entry entry)
        {
            if (entry.ModelPrefab == null)
            {
                _log?.Error(Source, $"{name}: class '{Class}' has no ModelPrefab in ChickenClassRegistry. " +
                                    "The chicken will be invisible — assign it on the registry asset.");
                return null;
            }

            var model = Instantiate(entry.ModelPrefab, transform);
            // Strip the "(Clone)" suffix: the Avatar resolves its skeleton by transform name
            // from the Animator down, and the model root is the first name it looks for.
            model.name = entry.ModelPrefab.name;

            // The imported .fbx carries its own Animator. Two Animators in one hierarchy fight
            // over the same skeleton, and the nested one wins for the subtree it sits on — so
            // disable it in the same frame rather than waiting for Destroy to be reaped.
            var nestedAnimator = model.GetComponent<Animator>();
            if (nestedAnimator != null)
            {
                nestedAnimator.enabled = false;
                Destroy(nestedAnimator);
            }

            // The models are authored with their origin at the feet, so dropping the model root
            // to the bottom of the capsule puts the feet on the ground. The pivot sits at the
            // capsule's mid-height (center is zero), i.e. half the height above ground contact —
            // parenting at localPosition zero would leave every chicken hovering by that much.
            float groundLocalY = _characterController.center.y - _characterController.height * 0.5f;
            model.transform.localPosition = new Vector3(0f, groundLocalY, 0f);
            model.transform.localRotation = Quaternion.identity;

            // Caches its renderer set in Spawned, which may already have run — and could not have
            // included the model in any case, since the model only exists now.
            GetComponent<HitFeedback>()?.RefreshBodyRenderers();

            _skeletalAnimationActive = BindSkeletalAnimation(entry);

            _log?.Debug(Source, $"Attached model '{model.name}' at localY={groundLocalY:0.###}, " +
                                $"skeletal={_skeletalAnimationActive}.");
            return model.transform;
        }

        /// <summary>
        /// Binds the class's skeleton and its five clips to the root <see cref="Animator"/>, so the
        /// shared <c>Chicken.controller</c> drives the model that was just parented under us.
        /// </summary>
        /// <returns>
        /// True only when the skeletal path is fully live — avatar bound AND all five clips
        /// applied. False means the caller must fall back to procedural motion; the reason has
        /// already been logged as an Error/Warning here.
        /// </returns>
        /// <remarks>
        /// Order matters: avatar first (it defines the skeleton the clips resolve against), then
        /// the controller, then a single <c>Rebind()</c>. Rebinding before the controller is set
        /// would resolve the rig against the placeholder motions and have to be redone.
        /// </remarks>
        private bool BindSkeletalAnimation(in ChickenClassRegistrySO.Entry entry)
        {
            var animator = GetComponent<Animator>();
            if (!BindAvatar(entry, animator)) return false;

            if (!entry.Clips.IsComplete)
            {
                _log?.Error(Source, $"{name}: class '{Class}' is missing the " +
                                    $"'{entry.Clips.FirstMissing}' AnimationClip (and possibly others) " +
                                    "in ChickenClassRegistry. Skeletal animation is OFF for this class — " +
                                    "it falls back to procedural motion. Assign all five clips from " +
                                    $"the same .fbx as its ModelPrefab.");
                return false;
            }

            var baseController = animator.runtimeAnimatorController;
            if (baseController == null)
            {
                _log?.Error(Source, $"{name}: the root Animator has no RuntimeAnimatorController, so " +
                                    "the class clips have no states to override. Re-assign " +
                                    "Chicken.controller on Chicken.prefab. Falling back to procedural motion.");
                return false;
            }

            animator.runtimeAnimatorController = GetOrBuildOverride(Class, baseController, entry);
            animator.Rebind();
            return true;
        }

        /// <summary>
        /// Per-class <see cref="AnimatorOverrideController"/> cache. One instance is shared by every
        /// chicken of a class (four total), rather than one per spawn — a fresh override controller
        /// per chicken would allocate and duplicate the whole state machine on every respawn.
        /// </summary>
        /// <remarks>
        /// Rebuilt on miss, so a domain reload (which clears statics) or a destroyed asset simply
        /// regenerates it rather than handing out a stale reference. The base-controller identity is
        /// re-checked too: if the prefab is ever re-pointed at a different controller, a cached
        /// override built on the old one must not be reused.
        /// </remarks>
        private static readonly System.Collections.Generic.Dictionary<ChickenClass, AnimatorOverrideController>
            _overrideControllers = new();

        // Override keys — the names of the placeholder clips sitting in Chicken.controller's five
        // Motion slots. AnimatorOverrideController keys by the ORIGINAL clip, so these must match
        // the placeholder asset names exactly. DataIntegrityTests pins both ends of that contract.
        private const string ClipKeyIdle    = "Idle";
        private const string ClipKeyWalk    = "Walk";
        private const string ClipKeyCast    = "Cast";
        private const string ClipKeyHit     = "Hit";
        private const string ClipKeyStunned = "Stunned";

        private static AnimatorOverrideController GetOrBuildOverride(
            ChickenClass cls,
            RuntimeAnimatorController baseController,
            in ChickenClassRegistrySO.Entry entry)
        {
            if (_overrideControllers.TryGetValue(cls, out var cached) &&
                cached != null && cached.runtimeAnimatorController == baseController)
            {
                return cached;
            }

            var overrideController = new AnimatorOverrideController(baseController)
            {
                name = $"{baseController.name}_{cls}",
                [ClipKeyIdle]    = entry.Clips.Idle,
                [ClipKeyWalk]    = entry.Clips.Walk,
                [ClipKeyCast]    = entry.Clips.Cast,
                [ClipKeyHit]     = entry.Clips.Hit,
                [ClipKeyStunned] = entry.Clips.Stunned,
            };

            // The eight cast archetypes. Keyed by the placeholder clip name sitting in each
            // Cast_* state's Motion slot, exactly as the five above are.
            //
            // These have to be per-state rather than one Cast state whose clip is swapped at cast
            // time: this override controller is cached PER CLASS and shared by every chicken of
            // that class, so writing a clip into it mid-match would retarget every same-class
            // chicken's cast at once -- including ones already mid-animation.
            for (int i = 0; i < ChickenClassRegistrySO.ClassClips.CastArchetypeCount; i++)
            {
                var clip = entry.Clips.CastFor((Abilities.CastArchetype)i);
                if (clip != null)
                    overrideController["Cast_" + (Abilities.CastArchetype)i] = clip;
            }

            _overrideControllers[cls] = overrideController;
            return overrideController;
        }

        /// <summary>
        /// Hands the class's <see cref="Avatar"/> to the root <see cref="Animator"/>.
        /// </summary>
        /// <returns>True when the avatar was accepted and assigned.</returns>
        private bool BindAvatar(in ChickenClassRegistrySO.Entry entry, Animator animator)
        {
            if (animator == null)
            {
                _log?.Warn(Source, $"{name}: no Animator on the chicken root, so the class model " +
                                   "can never be animated. Re-add it to Chicken.prefab.");
                return false;
            }

            if (entry.ModelAvatar == null)
            {
                _log?.Warn(Source, $"{name}: class '{Class}' has a ModelPrefab but no ModelAvatar. " +
                                   "The model renders but the Animator cannot drive its skeleton.");
                return false;
            }

            // The avatar must be Generic. A Humanoid avatar maps cleanly (all 22 Mixamo-named bones
            // resolve, isValid && isHuman) but then rebuilds the pose in human muscle space every
            // frame: measured live, that stretched the model from 1.60m to 1.75m, splayed the limbs
            // and dropped the feet 1.04m below the capsule. Refusing it keeps the chicken looking
            // right — the Error and the failing EditMode test are what get the import fixed.
            if (entry.ModelAvatar.isHuman)
            {
                _log?.Error(Source, $"{name}: class '{Class}' has a Humanoid ModelAvatar " +
                                    $"('{entry.ModelAvatar.name}'), which deforms the chicken rig and " +
                                    "sinks it through the floor. Leaving the Animator unbound — " +
                                    "re-import the .fbx with Rig ▸ Animation Type = Generic.");
                return false;
            }

            // The caller Rebind()s once, after the override controller is in place.
            animator.avatar = entry.ModelAvatar;
            return true;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _traversal?.Abort();
            ActiveControllers.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            // The gate opens the method (CONVENTIONS.md), and the diagnostic below is not
            // weakened by sitting under it. _movement is built in Spawned from _activeStats,
            // and the ChickenStatsSO resolution footgun this log exists to surface is a
            // StateAuthority-side condition: the authority owns the simulation and is the peer
            // where unresolved stats actually stop the chicken moving. Proxies were never the
            // audience — on them this reported a problem they could not have and could not fix,
            // 32 times a second, at the Verbose level that is on by default in dev.
            if (_movement == null)
            {
                // Once per transition into the broken state, not once per tick. A stuck
                // condition that reprints every tick is how a log stops being read at all.
                if (!_movementMissingReported)
                {
                    _movementMissingReported = true;
                    _log?.Warn(Source, $"{name}: FixedUpdateNetwork has no ChickenMovement — " +
                        "stats never resolved in Spawned, so this chicken cannot move. Check " +
                        "that ChickenClassRegistrySO has an entry for its Class.");
                }
                return;
            }
            _movementMissingReported = false;

            if (_traversal != null)
            {
                _traversal.Tick(Runner.DeltaTime);

                // Let a slotted passive react while the window is genuinely open (Juggernaut's
                // barge-shove). Skipped during unstick — the caster is being extracted from
                // geometry there, not actively ploughing through it.
                if (_traversal.Tier != TerrainTraversal.None && !_traversal.IsUnsticking)
                {
                    var passive = _abilities != null ? _abilities.Passive : null;
                    passive?.OnTraversalTick(this, _traversal.Tier);
                }
            }

            // Decoys (Doppelganger) share input authority with the caster — skip all
            // logic so the decoy doesn't walk in lockstep with the real chicken.
            if (IsDecoy) return;

            // ---- Reset and re-compute slow sources each tick -----------------
            // This runs for both player chickens AND bots so BotController.BotTick
            // benefits from the final SlowMultiplier that's set here.
            SlowMultiplier     = 1f;
            _activeSlowSources = SlowSource.None;
            Rooted             = false; // re-evaluated by timer check below

            CheckCollisionSlow();

            // Pile slow: ChickenCargo sets IsPileSlow on the previous tick (1-tick
            // lag is imperceptible; piles don't move).
            // Featherfoot ignores this entirely. The slow matters far more since foraging
            // became Peck: a chicken now STANDS on a pile for seconds pressing the button,
            // where before it only brushed past.
            var pileSlowPassive = Passive;
            bool ignoresPileSlow = pileSlowPassive != null && pileSlowPassive.IgnoresPileSlow(this);
            if (_cargo != null && _cargo.IsPileSlow && !ignoresPileSlow)
                ApplySlow(SlowSource.Pile, PileSlowFactor);

            // Ability slow timer (RPC_ApplyAbilitySlow — zones, aura, etc.).
            if (Runner.SimulationTime < _abilitySlowUntil)
                ApplySlow(SlowSource.Ability, _abilitySlowFactor);

            // Feather Aura: self-check from nearby casters broadcasting an aura.
            CheckAuraSlow();

            // Placed-zone slow (Feather Trap).
            CheckAbilityZoneSlow();

            // General stun timer expiry
            if (IsStunned && StunTimer.Expired(Runner))
            {
                IsStunned = false;
            }

            // Root timer: RPC_ApplyRoot sets RootTimer; Rooted persists until it elapses.
            if (!RootTimer.ExpiredOrNotRunning(Runner)) Rooted = true;

            // Publish the compact control-state for remote VFX. Particles stay local
            // on each peer; this replicated flag is the only thing that crosses.
            //
            // The Snared bit is set ONLY inside the Slowed branch — that nesting is the whole
            // enforcement of the "Snared implies Slowed" invariant ControlVfx documents, and
            // this is the only place either bit is written. _activeSlowSources is
            // StateAuthority-side and reset at the top of this method, so it describes exactly
            // the sources that produced the SlowMultiplier being tested here.
            var vfx = ControlVfx.None;
            if (SlowMultiplier < SlowVfxFlagThreshold)
            {
                vfx |= ControlVfx.Slowed;
                if ((_activeSlowSources & SlowSource.Ability) != 0) vfx |= ControlVfx.Snared;
            }
            if (Rooted)                 vfx |= ControlVfx.Rooted;
            if (IsStunned)              vfx |= ControlVfx.Stunned;
            if (ControlFlags != vfx)    ControlFlags = vfx;

            // Bots exit here — BotController.BotTick handles their movement with
            // the SlowMultiplier already computed above.
            if (IsBot) return;

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return;

            if (_combat != null && _combat.IsRemoved) return;
            if (!ControlRules.CanMove(CurrentControlState)) return;

            if (GetInput<PlayerNetworkInput>(out var input))
            {
                _movement.Tick(input.Movement, Runner.DeltaTime, IsAimRotating());
            }
        }

        /// <summary>
        /// v0.6 hold-to-aim (FEEDBACK.md §2.3): true while this chicken is charging an
        /// ability whose footprint moves when they turn — the movement stick should rotate
        /// the caster's facing instead of translating them. Reads
        /// <see cref="AbilityController.ChargingAbility"/>, so it only ever fires here on
        /// the StateAuthority path above (bots never reach this — <c>IsBot</c> already
        /// returned earlier — and non-directional shapes fall through to normal movement).
        ///
        /// The shape membership itself lives on <see cref="AbilityBaseSO.IsDirectionalAim"/>
        /// rather than being restated here: it used to be a hard-coded list of three shapes,
        /// which a fourth (Capsule) would have quietly failed to join.
        /// </summary>
        private bool IsAimRotating()
        {
            if (_abilities == null || _abilities.ChargingSlot == 0) return false;
            var ability = _abilities.ChargingAbility;
            return ability != null && ability.IsDirectionalAim;
        }

        // ---- Passive hooks ---------------------------------------------------

        /// <summary>
        /// This chicken's equipped class specialization, or null. Every passive effect goes
        /// through a typed hook on it — see <see cref="Abilities.PassiveAbilitySO"/>.
        /// </summary>
        /// <remarks>
        /// This replaced <c>IsPassiveActive(ChickenPassive)</c>, a parallel enum path that
        /// mapped an enum member to a <c>p is XPassiveSO</c> check. Two systems described the
        /// same thing, and by the time it was removed only two of its four members still did
        /// anything — Tough scaled damage in a game with no damage, and Combo granted a third
        /// ability slot that every class now has.
        /// </remarks>
        public Abilities.PassiveAbilitySO Passive =>
            _abilities != null ? _abilities.Passive : null;

        /// <summary>
        /// Applies a speed penalty from a tagged source. Slippery does NOT reduce slow
        /// magnitude — per GDD §5.2 the passive is duration-only (handled in
        /// <see cref="RPC_ApplyAbilitySlow"/> / <see cref="RPC_ApplyRoot"/>).
        /// Call <em>after</em> resetting <c>SlowMultiplier = 1f</c> at the top of
        /// each tick.
        /// </summary>
        /// <remarks>
        /// <b>Composition invariant: slow composes by <see cref="Mathf.Min"/>, never by a
        /// product.</b> <see cref="SlowMultiplier"/> is therefore always <i>exactly one</i>
        /// authored factor — the harshest source currently touching this chicken — and can
        /// never land somewhere between two of them. Two overlapping slows do not stack.
        /// <para>
        /// That is why the several thresholds that read <see cref="SlowMultiplier"/> —
        /// <see cref="SlowVfxFlagThreshold"/> (0.92, the replicated VFX bit),
        /// <see cref="CurrentControlState"/>'s 0.99 gameplay ladder,
        /// <c>ChickenNameplate</c>'s and <c>ChickenStateOverlays.IsSlowed</c>'s 0.999 — cannot
        /// currently disagree: every shipping factor is ≤ 0.80, comfortably below all of them,
        /// so the band between 0.92 and 1.0 is unreachable. The trap is that a source authored
        /// <i>inside</i> that band would be a genuine slow that silently stops raising
        /// <see cref="ControlVfx.Slowed"/> and so becomes invisible on every remote peer, with
        /// nothing failing. <c>SlowSourceTests</c> exists to make that a red test instead —
        /// it pins both the compile-time factors here and each ability's <c>[Range]</c>
        /// ceiling against <see cref="SlowVfxFlagThreshold"/>.
        /// </para>
        /// </remarks>
        public void ApplySlow(SlowSource source, float factor)
        {
            _activeSlowSources |= source;
            SlowMultiplier = Mathf.Min(SlowMultiplier, factor);
        }

        /// <summary>
        /// Sets an external displacement impulse on this chicken (integrated and
        /// decayed by <see cref="ChickenMovement"/>).
        /// Scaled by the equipped specialization's <c>ModifyKnockback</c> (Bulwark
        /// inherits the old Immovable passive's role here).
        /// Must be called on the StateAuthority.
        ///
        /// <paramref name="origin"/> changes no physics whatsoever — only which replicated
        /// one-shot the impact fires on, and so whether peers read it as a hit or as a dodge.
        /// See <see cref="ImpulseOrigin"/>.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately not a hit-attribution push point.</b> This runs on the
        /// StateAuthority only and the impulse itself is never replicated, so the caller's
        /// identity is not available on the peer that has to draw the victim's direction cue.
        /// It does not need to be: a knockback in practice always accompanies a cast, and
        /// <c>HitFeedback.ConfirmHits</c> names the attacker on <i>every</i> peer inside
        /// <c>FeedbackTuning.HitAttributionWindowSeconds</c> — the knockback edge that
        /// <c>HitFeedback.ObserveKnockback</c> sees a frame or two later then claims it. That
        /// jitter between the two halves of one hit is precisely what the window exists for;
        /// see <see cref="Visuals.HitAttribution"/>. Do not add a push here.
        /// </remarks>
        public void ApplyKnockback(Vector3 impulse, ImpulseOrigin origin = ImpulseOrigin.Hit)
        {
            // Immovable: the shove does not land at all. Checked before the passive scale,
            // because scaling can only ever approach zero and never reach it.
            if (IsControlImmune) return;

            var p = Passive;
            if (p != null) impulse *= Mathf.Max(0f, p.ModifyKnockback(1f, this));
            ExternalDisplacement = impulse;

            // Fire the networked one-shot so every peer plays the impact locally — on the
            // counter that matches where the impulse came from. Everything above this line is
            // origin-blind on purpose: a dodge is resolved by the CharacterController against
            // real geometry, scaled by the same passive, decayed by the same ChickenMovement,
            // and refused by the same immunity as a shove. The ONLY thing an origin changes
            // is which byte moves, and therefore which story the feedback system tells.
            if (impulse.sqrMagnitude > 1f)
            {
                if (origin == ImpulseOrigin.SelfApplied) SelfImpulseEventId++;
                else                                     KnockbackEventId++;
            }
        }

        /// <summary>
        /// Applies slotted-passive control-duration modifiers (slow/root), and enforces the
        /// Immovable immunity window.
        /// </summary>
        /// <remarks>
        /// Every incoming control effect — ability-slow, stun and root — funnels through this
        /// one method, so returning 0 here is the whole of the immunity. Putting the check at
        /// the chokepoint rather than in each RPC means a control effect added later is immune
        /// by default instead of silently bypassing Immovable.
        /// </remarks>
        private float ApplyPassiveControlDuration(float seconds)
        {
            if (IsControlImmune) return 0f;

            var p = _abilities != null ? _abilities.Passive : null;
            return p != null ? p.ModifyControlDuration(seconds, this) : seconds;
        }

        // ---- Networking helpers ----------------------------------------------

        /// <summary>
        /// Places this chicken at <paramref name="position"/>. The only legitimate caller is
        /// <c>GameManager.RestartMatch</c>, putting everyone back on their corner for a new round.
        /// </summary>
        /// <remarks>
        /// <b>Sender-restricted, and it cannot become a direct call.</b> In Shared Mode each
        /// chicken's state authority is its own owning client, not the master, so the match
        /// authority genuinely has to reach it by RPC — which means <c>RpcSources.All</c> and a
        /// receiver-side check on who sent it, rather than the direct-write split
        /// <c>PlayerBase.AddFoodAuthoritative</c> could use. Without the check any peer could
        /// teleport any chicken anywhere, at any time.
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_TeleportTo(Vector3 position, RpcInfo info = default)
        {
            if (!IsFromMatchAuthority(info, out string rejection))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_TeleportTo({position}) from {info.Source} — {rejection}");
                return;
            }

            _traversal?.Abort();
            if (_characterController != null)
            {
                _characterController.enabled = false;
                transform.position = position;
                _characterController.enabled = true;
            }
            else
            {
                transform.position = position;
            }
            _log?.Debug(Source, $"Teleported to {position}.");
        }

        /// <summary>
        /// True when <paramref name="info"/> came from the peer that runs the match — the state
        /// authority of the one <see cref="GameManager"/>, which is a replicated
        /// <c>PlayerRef</c> every peer can read off <c>Object.StateAuthority</c>.
        /// </summary>
        /// <remarks>
        /// A local invocation is accepted outright. That covers solo play and the master moving
        /// its own chicken, and it grants nothing: a peer that already holds a chicken's state
        /// authority can write <c>transform.position</c> directly, so there is no privilege here
        /// for it to escalate to. What the check stops is a <em>remote</em> peer reaching into a
        /// chicken it does not own, and <c>IsInvokeLocal</c> is false for everything off the wire.
        /// </remarks>
        private bool IsFromMatchAuthority(in RpcInfo info, out string rejection)
        {
            rejection = null;
            if (info.IsInvokeLocal) return true;

            var gm = GameManager.Instance;
            if (gm == null || gm.Object == null || !gm.Object.IsValid)
            {
                rejection = "there is no live GameManager to resolve the match authority against.";
                return false;
            }

            if (info.Source != gm.Object.StateAuthority)
            {
                rejection = $"only the match authority ({gm.Object.StateAuthority}) may place chickens.";
                return false;
            }

            return true;
        }

        // ---- v0.3 Interaction primitive RPCs (B1) — all route to StateAuthority ----

        /// <summary>
        /// Applies an external displacement (knockback) impulse. Routes to the
        /// chicken's StateAuthority; integrated + decayed by <see cref="ChickenMovement"/>.
        /// Knockback is scaled by the specialization (Bulwark).
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyKnockback(Vector3 impulse)
        {
            ApplyKnockback(impulse); // already scales by Immovable passive
            _log?.Debug(Source, $"RPC_ApplyKnockback: impulse={impulse:F2}.");
        }

        /// <summary>
        /// Applies a displacement impulse this chicken is inflicting on <b>itself</b> — a
        /// dodge, not a shove. Physically identical to <see cref="RPC_ApplyKnockback"/> in
        /// every respect; the difference is that peers observe it on
        /// <see cref="SelfImpulseEventId"/>, which the victim-impact systems do not read.
        /// </summary>
        /// <remarks>
        /// <b>Still an impulse, and that is load-bearing.</b> Feint shoves rather than
        /// teleports so a wall stops the sidestep exactly as it stops running — Speedy is
        /// specifically denied terrain-skipping (see <c>FeintAbilitySO</c>). Delivering this
        /// as a teleport to dodge the feedback problem would have handed that back; the fix
        /// separates presentation from transport and leaves the transport alone.
        ///
        /// A separate RPC rather than an <see cref="ImpulseOrigin"/> parameter on the existing
        /// one: the two are different acts with different meanings at the call site, and a
        /// sender that wants to shove someone should not be one enum value away from telling
        /// their victim it was self-inflicted.
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplySelfImpulse(Vector3 impulse)
        {
            ApplyKnockback(impulse, ImpulseOrigin.SelfApplied);
            _log?.Debug(Source, $"RPC_ApplySelfImpulse: impulse={impulse:F2}.");
        }

        /// <summary>
        /// Applies a timed ability slow, shortened by the equipped specialization's
        /// <c>ModifyControlDuration</c> (Slippery, Bulwark).
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyAbilitySlow(float duration, float factor)
        {
            duration = ApplyPassiveControlDuration(duration);
            _abilitySlowUntil  = Runner.SimulationTime + duration;
            _abilitySlowFactor = factor;
            _log?.Debug(Source, $"RPC_ApplyAbilitySlow: factor={factor:P0} for {duration:0.0}s.");
        }

        /// <summary>
        /// Stuns this chicken for <paramref name="seconds"/> seconds — blocks movement,
        /// casting, and collecting. Shortened by <c>ModifyControlDuration</c>.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyStun(float seconds)
        {
            seconds = ApplyPassiveControlDuration(seconds);
            IsStunned = true;
            StunTimer = TickTimer.CreateFromSeconds(Runner, seconds);
            _log?.Debug(Source, $"RPC_ApplyStun: stunned for {seconds:0.0}s.");
        }

        /// <summary>
        /// Roots this chicken for <paramref name="duration"/> seconds — movement
        /// blocked, abilities still castable (GDD §6.4). Shortened by
        /// <c>ModifyControlDuration</c>.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyRoot(float duration)
        {
            duration = ApplyPassiveControlDuration(duration);
            RootTimer = TickTimer.CreateFromSeconds(Runner, duration);
            _log?.Debug(Source, $"RPC_ApplyRoot: rooted for {duration:0.0}s.");
        }

        /// <summary>
        /// Resets all v0.3 control states (slow, root, knockback, aura).
        /// Called by <see cref="GameManager"/> on match restart.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetControlStates()
        {
            _traversal?.Abort();
            _abilitySlowUntil    = double.MinValue;
            _abilitySlowFactor   = 1f;
            RootTimer            = TickTimer.None;
            Rooted               = false;
            IsStunned            = false;
            StunTimer            = TickTimer.None;
            AuraSlowActive       = false;
            ExternalDisplacement = Vector3.zero;
            UnderdogSurgeActive  = false;
            LeaderBountyActive   = false;
            _log?.Debug(Source, "Control states reset for new match.");
        }

        /// <summary>
        /// Drives movement for bot-controlled chickens. Called by
        /// <see cref="BotController"/> each <c>FixedUpdateNetwork</c> tick instead
        /// of reading Fusion player input. Only valid on the StateAuthority peer.
        /// </summary>
        public void BotTick(Vector2 movement, float deltaTime)
        {
            if (!HasStateAuthority || _movement == null) return;
            _movement.Tick(movement, deltaTime);
        }

        /// <summary>
        /// Turn a bot toward <paramref name="worldDir"/> without translating it — the same
        /// facing-only motion a human gets while holding a directional ability to aim it
        /// (FEEDBACK.md §2.3), reached through the identical <c>aimRotateOnly</c> path so
        /// bots and players turn at the same <c>Stats.TurnSpeed</c>.
        /// </summary>
        /// <remarks>
        /// Bots had no way to aim at all before this. <see cref="BotTick"/> rotates as a
        /// side effect of moving, so a bot standing still — parked at a pile, or already
        /// inside a rival's face — kept whatever heading the navmesh last left it on and
        /// fired every Cone and Capsule ability into empty space. Gravity and knockback
        /// still run inside <c>ChickenMovement.Tick</c>, so a bot spending a tick turning
        /// does not float or stop being pushed.
        /// </remarks>
        public void BotFace(Vector3 worldDir, float deltaTime)
        {
            if (!HasStateAuthority || _movement == null) return;
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f) { BotTick(Vector2.zero, deltaTime); return; }
            worldDir.Normalize();
            _movement.Tick(new Vector2(worldDir.x, worldDir.z), deltaTime, aimRotateOnly: true);
        }

        // ---- Private helpers -------------------------------------------------

        /// <summary>
        /// Checks whether another live chicken is within contact range and, if so,
        /// applies the collision slow source. Runs once per FixedUpdateNetwork on
        /// the authority — avoids relying on OnTriggerStay which is unreliable for
        /// networked state (CONVENTIONS.md).
        /// </summary>
        /// <remarks>
        /// Iterates the capped four-entry <see cref="ActiveControllers"/> list with a distance
        /// test, exactly like <see cref="CheckAuraSlow"/> below and <c>AbilityZone</c>. This was
        /// the last <c>Physics.OverlapSphere</c> in the per-tick path: it ran a broadphase query
        /// plus a <c>GetComponentInParent</c> per chicken per tick to rediscover a list the class
        /// already keeps. Locked by <c>AbilityAimTests.NoAbilityScript_CallsPhysicsOverlapDirectly</c>.
        ///
        /// <b>Balance note:</b> the range is now measured to the other chicken's transform pivot
        /// rather than to the nearest point on its capsule, so contact registers roughly one
        /// collider radius later than it used to — the same shift, and the same reasoning,
        /// <c>AbilityZone</c> documented when it made this move. <see cref="CollisionSlowRadius"/>
        /// is 1.2 m against a 0.96 m body diameter, so "two chickens touching" is now what the
        /// constant literally says it is.
        ///
        /// Decoys are deliberately NOT skipped. They sit on the Chickens layer and the old query
        /// found them, so a Doppelganger has always slowed whoever walks into it — that is the
        /// point of a decoy. It carries no <c>ChickenCargo</c>, so the steal-back block below
        /// falls through on its own null check.
        /// </remarks>
        private void CheckCollisionSlow()
        {
            for (int i = 0; i < ActiveControllers.Count; i++)
            {
                var other = ActiveControllers[i];
                if (other == null || other == this) continue;
                // Physics could hand back a chicken whose NetworkObject isn't live yet; the
                // steal-back block below reads [Networked] Cargo, which throws on an
                // unspawned object. Same guard ChickenCargo's pile scan carries.
                if (other.Object == null || !other.Object.IsValid) continue;
                // Ignore dead chickens (stunned / falling through respawn).
                if (other.Combat != null && other.Combat.IsDead) continue;
                if ((other.transform.position - transform.position).sqrMagnitude >
                    CollisionSlowRadius * CollisionSlowRadius)
                {
                    continue;
                }

                if (StealBackActive && other.Cargo != null && Cargo != null)
                {
                    if (!_stealBackCooldowns.TryGetValue(other.Id, out var cd) || cd.Expired(Runner))
                    {
                        _stealBackCooldowns[other.Id] = TickTimer.CreateFromSeconds(Runner, 1.0f);

                        float attackerFreeSpace = Cargo.Capacity - Cargo.Cargo;
                        float defenderCargo = other.Cargo.Cargo;
                        float stolen = StealMath.Clamp(StealBackAmount, attackerFreeSpace, defenderCargo);
                        if (stolen > 0f)
                        {
                            // Spine Coat is a steal too, so it names its thief like every other
                            // caller — see ChickenCargo.RPC_DrainStolen. This one is trivially
                            // in range (CollisionSlowRadius) and the receiver's bound comes off
                            // SpineCoatAbilitySO.NominalStealAmount, which is the same authored
                            // number StealBackAmount was set from.
                            other.Cargo.RPC_DrainStolen(stolen, Id);
                            Cargo.Cargo += stolen;
                        }
                        var dir = (other.transform.position - transform.position);
                        dir.y = 0f;
                        if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
                        other.RPC_ApplyKnockback(dir.normalized * StealBackKnockback);
                    }
                }

                ApplySlow(SlowSource.Collision, CollisionSlowFactor);
                break; // One other chicken is enough to trigger the slow.
            }
        }

        /// <summary>
        /// Checks whether this chicken is inside the aura of any nearby caster with
        /// <see cref="AuraSlowActive"/> set. Runs locally on this chicken's authority —
        /// no RPC needed because <c>AuraSlowActive/Radius/Factor</c> are Networked.
        /// </summary>
        private void CheckAuraSlow()
        {
            for (int i = 0; i < ActiveControllers.Count; i++)
            {
                var caster = ActiveControllers[i];
                if (caster == null || caster == this) continue;
                if (!caster.AuraSlowActive) continue;
                float sqr = (caster.transform.position - transform.position).sqrMagnitude;
                if (sqr <= caster.AuraSlowRadius * caster.AuraSlowRadius)
                {
                    ApplySlow(SlowSource.Ability, caster.AuraSlowFactor);
                }
            }
        }

        /// <summary>
        /// Applies the slow factor from any active <see cref="AbilityZone"/>s of
        /// type <see cref="ZoneEffect.Slow"/> within their trigger radius. Runs
        /// locally — no RPC needed because zones are Networked scene objects.
        /// </summary>
        private void CheckAbilityZoneSlow()
        {
            for (int i = 0; i < AbilityZone.ActiveZones.Count; i++)
            {
                var zone = AbilityZone.ActiveZones[i];
                if (zone == null || zone.Effect != ZoneEffect.Slow) continue;
                if (zone.OwnerChicken == this.Id) continue; // own Feather Trap never slows the owner (GDD §7.2)
                float sqr = (zone.transform.position - transform.position).sqrMagnitude;
                float r   = zone.TriggerRadius;
                if (sqr <= r * r)
                    ApplySlow(SlowSource.Ability, zone.SlowFactor);
            }
        }

        private ChickenStatsSO ResolveStatsForClass(ChickenClass cls)
        {
            if (_registry != null && _registry.TryGet(cls, out var entry) && entry.Stats != null)
                return entry.Stats;
            return _fallbackStats;
        }
    }
}
