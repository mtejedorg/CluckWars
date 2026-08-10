using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;
// CluckWars.Logging.LogLevel collides with Fusion.LogLevel; alias to ours (CONVENTIONS.md).
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Beat 2 of the feedback system: the Impact beat's victim side and caster hit-confirm
    /// (FEEDBACK.md §3.2), plus the execute/removal treatment (§3.4).
    ///
    /// <list type="bullet">
    ///   <item><b>Victim hit flash</b> — a white body flash for
    ///   <see cref="FeedbackTuning.VictimHitFlashDurationSeconds"/>, peaking at
    ///   <see cref="FeedbackTuning.VictimHitFlashPeakIntensity"/>.</item>
    ///   <item><b>Impact vector</b> — <see cref="FeedbackTuning.ImpactMotionLineCount"/>
    ///   short lines behind the victim, along the incoming direction, so the victim reads
    ///   <i>where it came from</i>.</item>
    ///   <item><b>Victim camera shake</b> — the missing middle tier between the caster's
    ///   micro-punch and the death shake.</item>
    ///   <item><b>Floating text</b> — <c>STUN 1.5s</c> / <c>ROOT 2.0s</c> / <c>SLOW 45%</c>
    ///   / <c>IMMUNE</c> via <see cref="FloatingCombatText"/>.</item>
    ///   <item><b>Caster hit-confirm</b> — a white spark at each victim plus the caster's
    ///   own micro-shake, so a landed cast never has to be guessed at.</item>
    ///   <item><b>Execute</b> — feather burst, death shake, and an optional hit-stop.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>The one component that legitimately drives body colour.</b>
    /// <see cref="TargetHighlight"/> and <see cref="ChickenStateOverlays"/> are strictly
    /// additive — they add their own sprites and never touch the chicken's material —
    /// precisely so they can never fight this flash. Do not "improve" either of them into
    /// tinting the body; the white flash owns that channel for its 0.12 s and nothing else
    /// may contend for it. The flash itself is written through a shared
    /// <see cref="MaterialPropertyBlock"/> on the exact renderer set
    /// <see cref="ChickenVisuals"/> tints (never an instantiated material — that would leak
    /// one material per chicken per hit) and hands the channel straight back to
    /// <see cref="ChickenVisuals"/> when it ends.
    ///
    /// <b>Local only, no new networking.</b> Every trigger is derived from state that is
    /// already replicated — <c>ChickenController.KnockbackEventId</c>,
    /// <c>ControlFlags</c>/<c>IsStunned</c>/<c>Rooted</c>, <c>ChickenCombat.IsRemoved</c>,
    /// <c>AbilityController.LastCastEventId</c>/<c>LastCastHitCount</c>, and (via
    /// <see cref="ChickenCargo"/>) <c>Cargo</c>. No RPCs, no new <c>[Networked]</c>
    /// properties.
    ///
    /// <b>Why edge-polling and not a ChangeDetector here.</b> Fusion's
    /// <c>ChangeDetector.DetectChanges</c> diffs the networked properties <i>of the
    /// behaviour it is handed</i>. This component declares none of its own — every
    /// property it watches lives on a sibling behaviour — so a detector rooted here would
    /// report nothing at all. Instead it uses the pattern already established for exactly
    /// this case by <c>ControlStateVFX.UpdateKnockback</c> and
    /// <c>AbilityRangeIndicator.ObserveCast</c>: read the replicated one-shot in
    /// <see cref="Render"/>, seed the baseline on first observation so a late joiner never
    /// false-fires, and act only on a change. Where the property <i>does</i> live on the
    /// observing component — <c>ChickenCargo.Cargo</c> — a real <c>ChangeDetector</c> +
    /// <c>PropertyReader&lt;float&gt;</c> is used, and that component calls
    /// <see cref="NotifyCargoLoss"/> here.
    ///
    /// <b>Maestro:</b> add to the Chicken prefab alongside <see cref="ChickenVFX"/>. No
    /// Inspector wiring required; the only knob is <c>_enableHitStop</c>.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HitFeedback : NetworkBehaviour
    {
        private const string Source = "HitFX";

        // ---- Geometry / presentation constants --------------------------------

        /// <summary>Height the impact motion lines are drawn at — body height, not ground
        /// height, so they read as motion through the air rather than as one more ground
        /// decal competing with the status ring and the telegraph outline.</summary>
        private const float MotionLineHeight = 0.85f;

        /// <summary>Gap between the victim's pivot and the near end of each motion line, so
        /// the lines trail behind the body instead of spearing through it.</summary>
        private const float MotionLineStandoff = 0.32f;

        /// <summary>Motion-line stroke width. Thinner than the telegraph outline (0.11) —
        /// this is a transient streak, not a persistent area.</summary>
        private const float MotionLineWidth = 0.055f;

        /// <summary>
        /// How far a rival can be and still be considered the source of a hit. Beyond this
        /// the hit almost certainly came from a placed zone (Feather Trap / Root Egg) with
        /// no rival attached, and pointing an arrow at the nearest bystander would be worse
        /// than pointing at nothing. Comfortably wider than the largest ability radius
        /// (Feather Aura at 3.0 + Roll offsets) plus a knockback's worth of travel.
        /// </summary>
        private const float AttackerSearchRadius = 12f;

        /// <summary>
        /// Squared-distance multiplier applied to a rival that is currently mid-ability
        /// when picking the likely attacker. A chicken actually resolving an ability is a
        /// far better candidate than a marginally closer bystander; 0.25 means a caster
        /// wins against a bystander up to 2x closer.
        /// </summary>
        private const float MidCastAttackerBias = 0.25f;

        /// <summary>Particles in the caster's white hit-confirm spark (§3.2).</summary>
        private const int HitSparkCount = 10;

        /// <summary>Particles in the execute feather burst (§3.4).</summary>
        private const int FeatherBurstCount = 20;

        /// <summary>Cream-white feather colour — deliberately distinct from
        /// <see cref="ChickenVFX"/>'s red death burst, which it layers on top of: the red
        /// burst says "destroyed", the feathers say "chicken".</summary>
        private static readonly Color FeatherColor = new Color(0.98f, 0.96f, 0.90f, 1f);

        // ---- Inspector --------------------------------------------------------

        [Tooltip("Execute hit-stop (FEEDBACK.md §3.4). Writes the process-global Time.timeScale " +
                 "for ExecuteHitStopDurationSeconds on the removed chicken's own peer only. " +
                 "Turn off if a live session shows the local Fusion clock snapping after a kill.")]
        [SerializeField] private bool _enableHitStop = true;

        // ---- Stage 5 hook -----------------------------------------------------

        /// <summary>
        /// Raised on the local player's peer only (<c>HasInputAuthority</c>) every time the
        /// local chicken takes a hit whose source could be resolved. The single argument is
        /// the <b>attacker's world position</b> at the moment of the hit — Stage 5's HUD
        /// converts it to a bearing relative to the camera and draws the §3.2 screen-edge
        /// direction arc with <see cref="FeedbackTuning.ScreenEdgeArcLifetimeSeconds"/> /
        /// <see cref="FeedbackTuning.ScreenEdgeArcWidthDegrees"/> /
        /// <see cref="FeedbackTuning.ScreenEdgeArcFadeExponent"/>.
        ///
        /// <code>
        /// // Stage 5 (HUD):
        /// void OnEnable()  => HitFeedback.OnLocalPlayerHit += HandleLocalHit;
        /// void OnDisable() => HitFeedback.OnLocalPlayerHit -= HandleLocalHit;
        /// void HandleLocalHit(Vector3 attackerWorldPos) { /* draw arc */ }
        /// </code>
        ///
        /// Contract notes for the subscriber:
        /// <list type="bullet">
        ///   <item>It is <b>not</b> raised when no attacker could be resolved (a zone tick,
        ///   a pile slow, a rival further than <see cref="AttackerSearchRadius"/>). No
        ///   event means "no bearing to show", never "no hit".</item>
        ///   <item>It can fire more than once in the same frame (knockback + stun land
        ///   together). Subscribers should be idempotent or refresh a single arc.</item>
        ///   <item>The position is a world-space point, not a direction — the camera yaws
        ///   per player corner (<see cref="MatchCamera"/>), so the bearing must be computed
        ///   against the live camera, not baked here.</item>
        ///   <item>This type is the publisher and holds no reference to the subscriber, so
        ///   it can never keep one alive; the static is nulled on subsystem registration so
        ///   a subscriber from a previous Editor play session is dropped rather than
        ///   resurrected. Unsubscribe in <c>OnDisable</c> anyway.</item>
        /// </list>
        /// </summary>
        public static event System.Action<Vector3> OnLocalPlayerHit;

        // ---- Cached components ------------------------------------------------

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenAnimator   _animator;
        private AbilityController _abilities;
        private ChickenVisuals    _visuals;
        private ILogService       _log;

        // ---- Body flash -------------------------------------------------------

        private MaterialPropertyBlock _propertyBlock;
        private Renderer[]            _bodyRenderers;
        private float                 _flashTimer = -1f; // <0 = idle
        private bool                  _flashRestorePending;

        // URP/Lit and the Standard fallback honour _BaseColor / _Color respectively —
        // same pair ChickenVisuals writes, so the flash and the tint address one channel.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId     = Shader.PropertyToID("_Color");

        // ---- Impact motion lines ----------------------------------------------

        private LineRenderer[]   _motionLines;
        private readonly Vector3[] _linePoints = new Vector3[2];
        private float   _lineTimer = -1f; // <0 = idle
        private Vector3 _lineDirection = Vector3.forward;

        // ---- Particles --------------------------------------------------------

        private ParticleSystem _sparkPS;
        private ParticleSystem _featherPS;

        // ---- Replicated one-shot baselines ------------------------------------

        private bool _knockInitialized;
        private byte _lastKnockEventId;

        private bool _castInitialized;
        private byte _lastCastEventId;
        private AbilityBaseSO _lastChargingAbility;

        private bool _controlInitialized;
        private bool _wasStunned;
        private bool _wasRooted;
        private bool _wasSlowed;

        private bool _subscribedToDeath;

        // Reused across cast events so a 32 Hz stream of activations never allocates.
        private readonly System.Collections.Generic.List<ChickenController> _castTargets = new(4);

        [Inject]
        public void Construct(ILogService log) => _log = log;

        // ---- Lifecycle --------------------------------------------------------

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _abilities  = GetComponent<AbilityController>();
            _visuals    = GetComponent<ChickenVisuals>();
            _animator   = GetComponent<ChickenAnimator>();

            _propertyBlock = new MaterialPropertyBlock();

            BuildMotionLines();
            BuildParticles();
        }

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            // Cached: GetComponentsInChildren allocates. The set only changes once, when
            // ChickenController attaches the per-class model — which calls RefreshBodyRenderers
            // below, since Spawned order between sibling NetworkBehaviours is not guaranteed.
            RefreshBodyRenderers();

            if (_combat != null && !_subscribedToDeath)
            {
                // ChickenCombat already raises OnDeath on EVERY peer from its own
                // ChangeDetector on IsRemoved — re-detecting the same transition here would
                // be a second source of truth for one event.
                _combat.OnDeath += HandleRemoval;
                _subscribedToDeath = true;
            }

            // Seed every baseline from current state so a late joiner inheriting a non-zero
            // event id or an already-stunned chicken never plays a hit that already happened.
            _knockInitialized   = false;
            _castInitialized    = false;
            _controlInitialized = false;

            _log?.Debug(Source, $"Spawned. HasInputAuthority={HasInputAuthority}, hitStop={_enableHitStop}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_combat != null && _subscribedToDeath)
            {
                _combat.OnDeath -= HandleRemoval;
                _subscribedToDeath = false;
            }
            EndFlash();
            HitStopDriver.CancelIfRequestedBy(this);
        }

        private void OnDisable()
        {
            EndFlash();
            HitStopDriver.CancelIfRequestedBy(this);
        }

        private void OnDestroy()
        {
            HitStopDriver.CancelIfRequestedBy(this);
        }

        /// <summary>Drops subscribers left over from a previous Editor play session when
        /// "Enter Play Mode Options" suppresses the domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnLocalPlayerHit = null;

        // ---- Observation (replicated state → local events) --------------------

        /// <summary>
        /// <b>Decoys get the on-body beat, never the local-player beat.</b> A Doppelganger
        /// decoy used to bail out of this method entirely ("a decoy is scenery"), which was
        /// right while decoys were untargetable. They are legitimate targets now
        /// (<see cref="AbilityBaseSO.WouldAffect"/>), so a decoy that absorbs a cast must
        /// flash and spark like a real chicken or the deception dies the first time anyone
        /// swings at it — the attacker would learn more from the *absence* of a hit-confirm
        /// than from any tell the decoy could give away.
        ///
        /// What must stay suppressed is anything that drives the local player's camera or
        /// HUD, and the reason is sharper than it looks: <b>a decoy shares input authority
        /// with its caster</b>, so <c>HasInputAuthority</c> is TRUE on the decoy on the
        /// caster's own peer. Letting the victim beat through unfiltered would shake the
        /// caster's camera and draw a §3.2 screen-edge arc pointing straight at whoever just
        /// hit their decoy — handing the caster free intel and violating §1.6. The two gates
        /// that enforce this live in <see cref="TriggerVictimHit"/> (shake + bearing) and
        /// <see cref="ObserveControlStates"/> (floating text).
        /// </summary>
        public override void Render()
        {
            if (_controller == null) return;

            var obj = Object;
            if (obj == null || !obj.IsValid) return;

            ObserveKnockback();
            ObserveControlStates();

            // A decoy's AbilityController early-returns before it can ever cast, so its
            // LastCastEventId never moves; skipping the observation says that out loud
            // instead of depending on it silently.
            if (!_controller.IsDecoy) ObserveOwnCast();
        }

        /// <summary>
        /// §3.2 case 14. <c>KnockbackEventId</c> is bumped on the StateAuthority every time
        /// an impulse lands and replicates to every peer, so this fires everywhere without
        /// an RPC. <see cref="ControlStateVFX"/> draws the ground shockwave off the same
        /// byte; this drives the body flash, the direction lines and the victim's shake.
        /// </summary>
        private void ObserveKnockback()
        {
            byte id = _controller.KnockbackEventId;
            if (!_knockInitialized)
            {
                _knockInitialized = true;
                _lastKnockEventId = id;
                return;
            }
            if (id == _lastKnockEventId) return;
            _lastKnockEventId = id;

            TriggerVictimHit();
        }

        /// <summary>
        /// §3.2 + case 17's entry moment. Only the <i>rising</i> edge of each control state
        /// counts as an impact — a state that is merely still running is the Aftermath
        /// beat's job (<see cref="ControlStateVFX"/> and Stage 5's HUD), not an impact.
        /// </summary>
        private void ObserveControlStates()
        {
            var flags = _controller.ControlFlags;
            bool stunned = _controller.IsStunned || (flags & ControlVfx.Stunned) != 0;
            bool rooted  = _controller.Rooted    || (flags & ControlVfx.Rooted)  != 0;
            bool slowed  = (flags & ControlVfx.Slowed) != 0;

            if (!_controlInitialized)
            {
                _controlInitialized = true;
                _wasStunned = stunned;
                _wasRooted  = rooted;
                _wasSlowed  = slowed;
                return;
            }

            bool anyEntered = false;

            // Floating combat text is suppressed on a decoy (see Render). Not only because
            // it is a local-player-facing channel: a decoy is never really slowed or rooted
            // either — ChickenController.FixedUpdateNetwork early-returns on IsDecoy before
            // any of that is re-derived — so "SLOW 45%" over its head would be a number that
            // does not exist. The flash and the impact lines below still run, because those
            // report the hit itself, which did happen.
            bool showText = !_controller.IsDecoy;

            if (stunned && !_wasStunned)
            {
                anyEntered = true;
                if (showText) SpawnControlText("STUN", _controller.StunRemaining, FeedbackTuning.CanonicalStunColor);
            }
            if (rooted && !_wasRooted)
            {
                anyEntered = true;
                if (showText) SpawnControlText("ROOT", _controller.RootRemaining, FeedbackTuning.CanonicalRootColor);
            }
            if (slowed && !_wasSlowed)
            {
                anyEntered = true;
                if (showText) SpawnSlowText();
            }

            _wasStunned = stunned;
            _wasRooted  = rooted;
            _wasSlowed  = slowed;

            if (anyEntered) TriggerVictimHit();
        }

        /// <summary>
        /// §3.2's caster side, observed on every peer from this chicken's own
        /// <c>LastCastEventId</c>. Baseline-seeded exactly like
        /// <c>AbilityRangeIndicator.ObserveCast</c>, whose ring half of the same event this
        /// deliberately does not duplicate.
        /// </summary>
        private void ObserveOwnCast()
        {
            if (_abilities == null) return;

            // TryActivate clears ChargingSlot in the same tick it fires, and a very short
            // ability's ActiveSlot may already have expired by the time this observes the
            // event — so remember what was last aimed, same as AbilityRangeIndicator.
            if (_abilities.ChargingSlot != 0)
            {
                var charging = _abilities.ChargingAbility;
                if (charging != null) _lastChargingAbility = charging;
            }

            byte id = _abilities.LastCastEventId;
            if (!_castInitialized)
            {
                _castInitialized = true;
                _lastCastEventId = id;
                return;
            }
            if (id == _lastCastEventId) return;
            _lastCastEventId = id;

            var ability = _abilities.ActiveAbility ?? _lastChargingAbility;
            if (ability == null) return;

            // Abilities whose cast-time target count is not a hit/whiff verdict at all sit
            // this beat out: self-buffs (no aim shape to gather from) and placed zones
            // (Root Egg / Feather Trap resolve later, in AbilityZone's own tick, and get
            // their confirmation through NotifyZoneTriggered instead). One predicate owns
            // both exemptions — see AbilityBaseSO.ReportsCastHits.
            if (!ability.ReportsCastHits) return;

            int hitCount = _abilities.LastCastHitCount;
            if (hitCount > 0) ConfirmHits(ability, hitCount);
            else              MarkImmuneTargets(ability);
        }

        /// <summary>
        /// §3.2 hit-confirm: a white spark on every chicken this cast landed on, plus the
        /// caster's own micro-shake. Target identities are not replicated (only the count
        /// is), so they are re-derived locally through <see cref="AbilityBaseSO.GatherTargets"/>
        /// — the same single predicate <c>OnActivate</c> and the telegraph use, which is why
        /// re-running it here cannot disagree with what was hit in any way that matters. That
        /// now includes single-target selection: <c>GatherTargets</c> truncates a
        /// <c>SingleTarget</c> ability to the one chicken it hits, so this sparks one victim
        /// instead of every eligible one. On a remote peer interpolation can shift a
        /// borderline target in or out; the count clamp below keeps the spark count honest
        /// even then.
        /// </summary>
        private void ConfirmHits(AbilityBaseSO ability, int hitCount)
        {
            // One exception to the re-derivation above, and it is structural rather than a
            // tolerance issue: an ability that resolves its shape BEFORE its teleport jump
            // (AbilityBaseSO.ResolvesBeforeJump — Flying Peck) was aimed from a pose that no
            // longer exists by the time any peer observes the cast event, and the pre-jump
            // origin is not replicated. Re-scanning here would sweep a lane from the LANDING
            // point and spark whoever happens to be standing at the far end. The caster still
            // gets their micro-punch below (the hit count is authoritative); only the
            // per-victim spark is dropped, and the victim's own beat is unaffected — they
            // feel the cargo loss through NotifyCargoLoss regardless.
            bool canReDeriveTargets = !ability.CastPoseIsUnreconstructable;

            if (canReDeriveTargets)
            {
                int found = ability.GatherTargets(_controller, _castTargets);
                int sparks = Mathf.Min(found, hitCount);
                for (int i = 0; i < sparks; i++)
                {
                    var victim = _castTargets[i];
                    if (victim == null || victim == _controller) continue;
                    victim.GetComponent<HitFeedback>()?.PlayHitSpark();
                }
            }

            // Caster's own punch. Gated on HasInputAuthority exactly like ChickenVFX's
            // existing §3.1 every-cast shake — peers must never feel each other's camera.
            // Both call sites now read the same FeedbackTuning constants and MatchCamera's
            // ApplyShake is max-wins, so the two coinciding on a landed cast is a no-op,
            // not a double shake.
            if (_controller.HasInputAuthority && MatchCamera.Instance != null)
            {
                bool steal = ability.Category == AbilityCategory.Steal;
                MatchCamera.Instance.ApplyShake(
                    steal ? FeedbackTuning.CasterStealShakeMagnitude    : FeedbackTuning.CasterMicroShakeMagnitude,
                    steal ? FeedbackTuning.CasterStealShakeDurationSeconds : FeedbackTuning.CasterMicroShakeDurationSeconds);
            }

            if (_log != null && _log.IsEnabled(LogLevel.Verbose))
                _log.Verbose(Source, canReDeriveTargets
                    ? $"Hit-confirm: {hitCount} hit(s) sparked for {ability.DisplayName}."
                    : $"Hit-confirm: {hitCount} hit(s) for {ability.DisplayName}; per-victim sparks skipped " +
                      "(shape resolved before a teleport, so the target set cannot be re-derived here).");
        }

        /// <summary>
        /// §3.2 case 15, restricted on purpose. A chicken that is inside the aim shape but
        /// was not affected is the "blocked / immune" case — but that gap also opens for a
        /// target the cast <i>did</i> hit (a stun ability's <c>ExtraTargetFilter</c> rejects
        /// an already-stunned chicken, and the cast is what just stunned them). Labelling
        /// only when the whole cast landed nothing removes that false positive completely:
        /// zero hits plus somebody standing in the shape can only mean they were immune.
        /// A whiff with an empty shape stays silent here — Stage 3's grey cast ring already
        /// tells that story (§3.3).
        /// </summary>
        private void MarkImmuneTargets(AbilityBaseSO ability)
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                // A decoy is a legitimate candidate now, so it earns the IMMUNE label like
                // anyone else standing in a whiffed cast's shape — and unlike the STUN/SLOW
                // text, IMMUNE is TRUE of a decoy, so there is nothing dishonest about it.
                if (candidate == null || candidate == _controller) continue;

                var o = candidate.Object;
                if (o == null || !o.IsValid) continue;
                if (candidate.Combat != null && candidate.Combat.IsDead) continue;

                if (!ability.IsInAimShape(_controller, candidate)) continue;

                FloatingCombatText.Spawn(
                    candidate.transform.position + Vector3.up * FeedbackTuning.FloatingTextSpawnHeight,
                    "IMMUNE",
                    FeedbackTuning.NeutralNoEffectColor);
            }
        }

        // ---- Victim beat ------------------------------------------------------

        /// <summary>
        /// Called from <see cref="ChickenCargo"/> when its <c>ChangeDetector</c> sees this
        /// chicken's <c>Cargo</c> drop by a meaningful amount — a steal, an execute
        /// transfer, or a death drop. A plain local method call, not an RPC: the cargo
        /// value is already replicated, so every peer's <see cref="ChickenCargo"/> reaches
        /// the same conclusion independently.
        /// </summary>
        public void NotifyCargoLoss(float amount)
        {
            if (_controller != null && _controller.IsDecoy) return;

            if (_log != null && _log.IsEnabled(LogLevel.Verbose))
                _log.Verbose(Source, $"Cargo loss of {amount:0.0} treated as a hit.");

            TriggerVictimHit();
        }

        /// <summary>
        /// Called by <see cref="AbilityZone"/> on the <i>caster's</i> HitFeedback the moment
        /// one of their placed zones (Root Egg, Feather Trap) actually catches someone. A
        /// plain local method call, not an RPC — exactly like <see cref="NotifyCargoLoss"/>:
        /// the zone's <c>Consumed</c> flag and every chicken's position are already
        /// replicated, so each peer's <c>AbilityZone.Render</c> reaches this conclusion
        /// independently.
        ///
        /// This is the <i>delayed</i> half of a placed-zone cast. At cast time the ability
        /// hits nobody by construction (<see cref="AbilityBaseSO.ReportsCastHits"/> is false
        /// for it), so a trap that worked perfectly would otherwise give its caster no
        /// confirmation at all — the §3.3 whiff ring was actively lying about it.
        ///
        /// Deliberately caster-side only. The victim's half is already fully covered by
        /// <see cref="ObserveControlStates"/>: entering root or slow raises a rising edge
        /// there, which plays the flash, the impact lines, the victim shake and the
        /// <c>ROOT 2.0s</c> / <c>SLOW 45%</c> text. Adding a spark here would be a second
        /// channel for one event, which is how a re-tune silently stops working.
        /// </summary>
        public void NotifyZoneTriggered()
        {
            if (_controller == null || _controller.IsDecoy) return;

            // The same micro-punch a landed direct cast gets (see ConfirmHits), gated on
            // HasInputAuthority for the same reason: peers never feel each other's camera.
            if (_controller.HasInputAuthority && MatchCamera.Instance != null)
            {
                MatchCamera.Instance.ApplyShake(
                    FeedbackTuning.CasterMicroShakeMagnitude,
                    FeedbackTuning.CasterMicroShakeDurationSeconds);
            }

            if (_log != null && _log.IsEnabled(LogLevel.Verbose))
                _log.Verbose(Source, "Placed zone triggered — caster hit-confirm.");
        }

        /// <summary>
        /// The victim-side impact beat: flash, direction lines, shake, and the Stage 5
        /// bearing hook. Safe to call several times in one frame (a knockback and a stun
        /// usually land together) — the flash simply restarts and <c>ApplyShake</c> is
        /// max-wins.
        ///
        /// The flash and the direction lines are <i>on-body</i> world-space visuals that
        /// every peer sees at the victim's own position, so a decoy plays them like any
        /// other chicken. The camera shake and the screen-edge bearing are the local
        /// player's channels and a decoy must never reach them — see <see cref="Render"/>
        /// for why <c>HasInputAuthority</c> alone is not enough of a gate here.
        /// </summary>
        private void TriggerVictimHit()
        {
            StartFlash();

            // The body squash is the flash's physical sibling — same beat, same trigger. Driving
            // it from here rather than from a ChangeDetector of its own is deliberate: this method
            // is already the single sink for every victim impact (knockback edge, control-state
            // entry, cargo loss, zone catch), and the detection feeding it is baseline-seeded so a
            // late joiner never replays a hit that already happened.
            _animator?.TriggerHit();

            bool haveAttacker = TryResolveAttacker(out var attackerPos);
            Vector3 direction = haveAttacker
                ? PlanarDirection(transform.position - attackerPos)
                : PlanarDirection(transform.forward);

            StartMotionLines(direction);

            if (_controller.HasInputAuthority && !_controller.IsDecoy)
            {
                MatchCamera.Instance?.ApplyShake(
                    FeedbackTuning.VictimHitShakeMagnitude,
                    FeedbackTuning.VictimHitShakeDurationSeconds);

                if (haveAttacker) RaiseLocalPlayerHit(attackerPos);
            }
        }

        private void RaiseLocalPlayerHit(Vector3 attackerPos)
        {
            var handler = OnLocalPlayerHit;
            if (handler == null) return;
            try
            {
                handler(attackerPos);
            }
            catch (System.Exception e)
            {
                // A subscriber blowing up must not cost the victim their own hit feedback,
                // but it must never vanish either.
                _log?.Error(Source, "OnLocalPlayerHit subscriber threw.", e);
            }
        }

        /// <summary>
        /// Best attacker this peer can name from replicated state alone.
        ///
        /// <b>Why not the victim's own motion.</b> The obvious source for "which way was I
        /// pushed" is <c>ChickenController.ExternalDisplacement</c> — but it is a plain
        /// property written only on the StateAuthority, so it reads zero on every other
        /// peer. Sampling the transform delta instead does not work either: the impulse is
        /// applied on the authority in the same tick the event byte flips, so on the frame
        /// this observes the hit the displacement has not replicated yet and the delta is
        /// still ~zero. Both would need new networked state, which this stage may not add.
        ///
        /// <b>So: bearing from the likeliest rival.</b> Positions are replicated on every
        /// peer, so the nearest live, non-decoy rival within
        /// <see cref="AttackerSearchRadius"/> is computable identically everywhere, with a
        /// bias toward one that is currently mid-ability. In a 4-player arena where every
        /// ability's reach is under ~3 m this is right nearly always, and when it is not
        /// (a placed zone with its owner long gone) the radius cut makes it decline to
        /// answer rather than lie.
        /// </summary>
        private bool TryResolveAttacker(out Vector3 attackerPos)
        {
            attackerPos = default;

            var all = ChickenController.ActiveControllers;
            Vector3 self = transform.position;
            ChickenController best = null;
            float bestScore = AttackerSearchRadius * AttackerSearchRadius;

            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || candidate == _controller) continue;
                if (candidate.IsDecoy) continue;

                var o = candidate.Object;
                if (o == null || !o.IsValid) continue;
                if (candidate.Combat != null && candidate.Combat.IsDead) continue;

                Vector3 p = candidate.transform.position;
                float dx = p.x - self.x, dz = p.z - self.z;
                float score = dx * dx + dz * dz;
                if (score > AttackerSearchRadius * AttackerSearchRadius) continue;

                var abilities = candidate.Abilities;
                if (abilities != null && abilities.ActiveSlot != AbilityController.InvalidSlot)
                    score *= MidCastAttackerBias;

                if (score < bestScore) { bestScore = score; best = candidate; }
            }

            if (best == null) return false;
            attackerPos = best.transform.position;
            return true;
        }

        // ---- Body flash -------------------------------------------------------

        private void StartFlash() => _flashTimer = 0f;

        /// <summary>
        /// Flash animation and motion-line animation both live in <c>LateUpdate</c>, not
        /// <see cref="Render"/>, so the body-colour write lands in the same phase as
        /// <see cref="ChickenVisuals"/>'s. That component only writes when its tint or
        /// opacity actually changes, so on virtually every frame this is the sole writer;
        /// on the rare frame it does write (stun grey-out) the flash re-reads its result
        /// next frame and self-corrects. At flash end this writes back exactly
        /// <see cref="ChickenVisuals.BodyColor"/> — the last value that component pushed —
        /// so the hand-back can never corrupt the tint.
        /// </summary>
        private void LateUpdate()
        {
            AnimateFlash();
            AnimateMotionLines();
        }

        private void AnimateFlash()
        {
            if (_flashTimer < 0f)
            {
                if (_flashRestorePending) EndFlash();
                return;
            }

            float duration = Mathf.Max(0.0001f, FeedbackTuning.VictimHitFlashDurationSeconds);
            _flashTimer += Time.deltaTime;
            float t = _flashTimer / duration;

            if (t >= 1f)
            {
                _flashTimer = -1f;
                EndFlash();
                return;
            }

            // Instant attack, linear release: a hit reads as a snap, not a swell. Peak on
            // the first frame is the whole point of a 3–4 frame flash at 30 fps.
            float k = FeedbackTuning.VictimHitFlashPeakIntensity * (1f - t);

            Color body = CurrentBodyColor();
            Color flashed = Color.Lerp(body, new Color(1f, 1f, 1f, body.a), k);
            PushBodyColor(flashed);
            _flashRestorePending = true;
        }

        private void EndFlash()
        {
            if (!_flashRestorePending) return;
            _flashRestorePending = false;
            _flashTimer = -1f;
            PushBodyColor(CurrentBodyColor());
        }

        private Color CurrentBodyColor() =>
            _visuals != null ? _visuals.BodyColor : Color.white;

        private void PushBodyColor(Color color)
        {
            if (_bodyRenderers == null) return;
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                var r = _bodyRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                _propertyBlock.SetColor(ColorId, color);
                r.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>
        /// Prefers <see cref="ChickenVisuals"/>'s own renderer list so the flash and the
        /// class tint address exactly the same surfaces. The fallback scan deliberately
        /// keeps only real body meshes: by the time <c>Spawned</c> runs, sibling components
        /// have already parented LineRenderers, SpriteRenderers, ParticleSystemRenderers
        /// and TextMeshes under this chicken, and flashing a nameplate white would be a bug.
        /// </summary>
        /// <summary>
        /// Re-caches the flashable body renderers. Called by
        /// <see cref="ChickenController.Spawned"/> after the per-class model is parented, so
        /// the flash addresses the model whichever order the two <c>Spawned</c> callbacks ran in.
        /// </summary>
        public void RefreshBodyRenderers() => _bodyRenderers = CollectBodyRenderers();

        private Renderer[] CollectBodyRenderers()
        {
            if (_visuals != null)
            {
                var tinted = _visuals.TintedRenderers;
                if (tinted != null && tinted.Length > 0) return tinted;
            }

            var all = GetComponentsInChildren<Renderer>(includeInactive: true);
            int kept = 0;
            for (int i = 0; i < all.Length; i++)
                if (IsBodyRenderer(all[i])) kept++;

            var result = new Renderer[kept];
            int w = 0;
            for (int i = 0; i < all.Length; i++)
                if (IsBodyRenderer(all[i])) result[w++] = all[i];
            return result;
        }

        private static bool IsBodyRenderer(Renderer r)
        {
            if (r == null) return false;
            if (r is SkinnedMeshRenderer) return true;
            if (r is not MeshRenderer) return false;          // excludes Line/Sprite/Particle renderers
            return r.GetComponent<TextMesh>() == null;        // excludes nameplate / cargo text
        }

        // ---- Impact motion lines ----------------------------------------------

        private void BuildMotionLines()
        {
            int count = Mathf.Max(1, FeedbackTuning.ImpactMotionLineCount);
            var mat = TelegraphShapes.BuildLineMaterial();
            _motionLines = new LineRenderer[count];
            for (int i = 0; i < count; i++)
            {
                _motionLines[i] = TelegraphShapes.BuildLine(
                    transform, "ImpactMotionLine" + i, mat, MotionLineWidth, loop: false);
                _motionLines[i].enabled = false;
            }
        }

        private void StartMotionLines(Vector3 direction)
        {
            _lineDirection = direction;
            _lineTimer = 0f;
        }

        /// <summary>
        /// §3.2's impact vector: a short fan of streaks sitting on the <i>incoming</i> side
        /// of the victim and pointing at them, so the victim's eye is dragged back toward
        /// where the hit came from. Outlives the flash by design — see
        /// <see cref="FeedbackTuning.ImpactMotionLineLifetimeSeconds"/>: the flash says
        /// "hit", the streaks say "from there", and they need their own beat to be read.
        /// </summary>
        private void AnimateMotionLines()
        {
            if (_lineTimer < 0f || _motionLines == null) return;

            float life = Mathf.Max(0.0001f, FeedbackTuning.ImpactMotionLineLifetimeSeconds);
            _lineTimer += Time.deltaTime;
            float t = _lineTimer / life;

            if (t >= 1f)
            {
                _lineTimer = -1f;
                for (int i = 0; i < _motionLines.Length; i++)
                    if (_motionLines[i] != null && _motionLines[i].enabled)
                        _motionLines[i].enabled = false;
                return;
            }

            Color c = FeedbackTuning.CanonicalKnockColor;
            c.a = 1f - t;

            Vector3 origin = transform.position;
            origin.y = MotionLineHeight;

            int count = _motionLines.Length;
            float fan = FeedbackTuning.ImpactMotionLineFanDegrees;
            float length = FeedbackTuning.ImpactMotionLineLength;

            for (int i = 0; i < count; i++)
            {
                var lr = _motionLines[i];
                if (lr == null) continue;

                // Symmetric fan around the incoming bearing: -fan, 0, +fan for the default
                // three lines, and still symmetric if the count is ever re-tuned.
                float spread = count > 1 ? (i / (float)(count - 1)) * 2f - 1f : 0f;
                Vector3 d = RotateY(_lineDirection, spread * fan);

                // The streaks slide outward slightly over their life so they read as motion
                // rather than as three static ticks.
                float drift = Mathf.Lerp(0f, length * 0.35f, t);

                Vector3 near = origin - d * (MotionLineStandoff + drift);
                Vector3 far  = near   - d * length;

                _linePoints[0] = far;
                _linePoints[1] = near;

                lr.startColor = lr.endColor = c;
                if (lr.positionCount != 2) lr.positionCount = 2;
                lr.SetPositions(_linePoints);
                if (!lr.enabled) lr.enabled = true;
            }
        }

        // ---- Control-state floating text --------------------------------------

        private void SpawnControlText(string label, float seconds, Color color)
        {
            string text = seconds > 0.05f ? $"{label} {seconds:0.0}s" : label;
            FloatingCombatText.Spawn(TextAnchorPoint(), text, color);
        }

        /// <summary>
        /// <b>Carried-forward design decision — do not "fix" this into a countdown.</b>
        /// Slow shows its <i>magnitude</i>, never a time. Unlike stun and root, which are
        /// backed by real <c>[Networked] TickTimer</c>s
        /// (<see cref="ChickenController.StunRemaining"/> /
        /// <see cref="ChickenController.RootRemaining"/>), slow is re-derived from scratch
        /// every tick from whatever is currently touching the chicken — collision, a food
        /// pile, a zone, an aura — and has no end time at all to count down to (see the
        /// remarks on <see cref="ChickenController.SlowMultiplier"/>). Two overlapping slow
        /// sources with different remaining durations would make any single "time left"
        /// number meaningless anyway. So it renders as <c>SLOW 45%</c>, meaning "moving at
        /// 45% of normal speed", matching §5.2's <c>×0.45</c> control-consequence label.
        ///
        /// The multiplier itself is StateAuthority-side only, so a remote peer watching a
        /// rival cannot read the number and gets the bare <c>SLOW</c> — still two coding
        /// channels (word + cyan) per §1.3.
        /// </summary>
        private void SpawnSlowText()
        {
            string text = "SLOW";
            if (HasStateAuthority)
            {
                int percent = Mathf.Clamp(Mathf.RoundToInt(_controller.SlowMultiplier * 100f), 0, 100);
                text = $"SLOW {percent}%";
            }
            FloatingCombatText.Spawn(TextAnchorPoint(), text, FeedbackTuning.CanonicalSlowColor);
        }

        private Vector3 TextAnchorPoint() =>
            transform.position + Vector3.up * FeedbackTuning.FloatingTextSpawnHeight;

        // ---- Execute / removal (§3.4) -----------------------------------------

        /// <summary>
        /// Fired by <see cref="ChickenCombat.OnDeath"/> on every peer the instant
        /// <c>IsRemoved</c> goes true. The death camera shake stays where it already lives
        /// (<c>ChickenCombat.Render</c>, <c>HasInputAuthority</c>-gated, now reading
        /// <see cref="FeedbackTuning.DeathShakeMagnitude"/>) rather than being re-issued
        /// here — two call sites for one shake is how a re-tune silently stops working.
        /// </summary>
        private void HandleRemoval()
        {
            _featherPS?.Play();

            // Hit-stop is the victim's own beat only — see HitStopDriver's remarks for why
            // the killer does not get one.
            if (_enableHitStop && HasInputAuthority)
            {
                HitStopDriver.Request(this,
                    FeedbackTuning.ExecuteHitStopTimeScale,
                    FeedbackTuning.ExecuteHitStopDurationSeconds);
            }
        }

        // ---- Particles --------------------------------------------------------

        /// <summary>Plays this chicken's white hit-confirm spark. Called locally by the
        /// <i>caster</i>'s <see cref="HitFeedback"/> — not an RPC; both peers reach the
        /// same conclusion from the replicated cast event.</summary>
        public void PlayHitSpark() => _sparkPS?.Play();

        private void BuildParticles()
        {
            var mat = BuildParticleMaterial();
            _sparkPS   = BuildSparkPS(mat);
            _featherPS = BuildFeatherPS(mat);
        }

        /// <summary>URP unlit particles first, with the same fallback chain
        /// <see cref="ChickenVFX"/> uses so this works without manual asset setup.</summary>
        private static Material BuildParticleMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            return sh != null ? new Material(sh) : null;
        }

        private ParticleSystem MakePS(string goName, Vector3 localOffset, Material mat)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localOffset;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop            = false;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode   = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = 3; // above ChickenVFX's bursts
            if (mat != null) rend.material = mat;

            return ps;
        }

        /// <summary>
        /// §3.2's "brief white hit-spark at each victim". Short, fast, pure white — the
        /// caster's confirmation, deliberately a different read from
        /// <see cref="ChickenVFX"/>'s warm gold ability burst so "it connected" and "I cast
        /// something" never look like the same event.
        /// </summary>
        private ParticleSystem BuildSparkPS(Material mat)
        {
            var ps = MakePS("VFX_HitConfirmSpark", new Vector3(0f, 0.9f, 0f), mat);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(3.0f, 6.0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor      = Color.white;
            main.gravityModifier = 0.6f;
            main.maxParticles    = HitSparkCount * 2;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, HitSparkCount) });

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = 0.16f;

            AddFadeOut(ps);
            return ps;
        }

        /// <summary>
        /// §3.4's feather burst. Slow, floaty, cream — reads as feathers, and layers on top
        /// of <see cref="ChickenVFX"/>'s existing sharp red death burst rather than
        /// replacing it (that one is the impact, this one is the aftermath drifting down).
        /// </summary>
        private ParticleSystem BuildFeatherPS(Material mat)
        {
            var ps = MakePS("VFX_FeatherBurst", new Vector3(0f, 0.7f, 0f), mat);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.70f, 1.30f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(1.2f, 3.0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
            main.startColor      = FeatherColor;
            main.gravityModifier = 0.18f;   // barely falls — feathers hang
            main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles    = FeatherBurstCount * 2;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, FeatherBurstCount) });

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = 0.35f;

            // Air drag so they slow to a drift instead of flying away like sparks.
            var vel = ps.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.dampen  = 0.35f;
            vel.limit   = new ParticleSystem.MinMaxCurve(0.6f);

            AddFadeOut(ps);
            return ps;
        }

        /// <summary>Alpha fade over the last 28% of each particle's life; RGB held so
        /// <c>main.startColor</c> stays the tint. Mirrors <c>ChickenVFX.AddFadeOut</c>.</summary>
        private static void AddFadeOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.72f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        // ---- Small math helpers -----------------------------------------------

        private static Vector3 PlanarDirection(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
        }

        /// <summary>Rotates a planar vector around +Y without building a quaternion —
        /// same helper <c>TelegraphShapes</c> keeps private for its cone arc.</summary>
        private static Vector3 RotateY(Vector3 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector3(v.x * c + v.z * s, 0f, -v.x * s + v.z * c);
        }
    }

    /// <summary>
    /// The single owner of the execute hit-stop (FEEDBACK.md §3.4). Exists as its own
    /// persistent object rather than as logic inside <see cref="HitFeedback"/> because
    /// <c>Time.timeScale</c> is <b>process-global</b>: in a Shared-Mode session it slows the
    /// local Fusion clock, every animator, every particle system and every other chicken on
    /// this peer, not just the one that died. A stuck <c>timeScale</c> is a hung game, so
    /// this type is built entirely around never leaving one behind.
    /// </summary>
    /// <remarks>
    /// The four guarantees, and how each is met:
    /// <list type="number">
    ///   <item><b>Scope.</b> <see cref="HitFeedback.HandleRemoval"/> only calls
    ///   <see cref="Request"/> when the removed chicken has <c>HasInputAuthority</c> — i.e.
    ///   on the dying player's own peer. The killer deliberately gets no hit-stop: naming
    ///   the killer locally would need the attacker id replicated, and this stage may not
    ///   add networked state. A peer watching two other chickens fight never dips.</item>
    ///   <item><b>No nesting.</b> <see cref="Request"/> returns immediately while a dip is
    ///   already running, so a double kill cannot stack two dips or extend one forever.</item>
    ///   <item><b>Always restored.</b> The dip is driven off <c>Time.unscaledTime</c>, which
    ///   the dip itself cannot slow, and is released from <c>Update</c>,
    ///   <c>OnDisable</c>, <c>OnDestroy</c>, <c>OnApplicationQuit</c>, a scene-load-safe
    ///   <c>DontDestroyOnLoad</c> host, and a subsystem-registration reset that forces
    ///   <c>timeScale = 1</c> at the start of every play session. The requester being
    ///   destroyed mid-dip cannot strand it — the driver is not parented to the chicken.</item>
    ///   <item><b>Escape hatch.</b> <c>HitFeedback._enableHitStop</c> turns the whole thing
    ///   off from the Inspector without a code change.</item>
    /// </list>
    ///
    /// <b>Known cost, accepted.</b> Fusion advances its local simulation on scaled time, so
    /// a 0.08 s dip at <see cref="FeedbackTuning.ExecuteHitStopTimeScale"/> = 0.15 leaves
    /// this peer roughly 68 ms behind the session clock and it resyncs afterwards. That
    /// lands at the exact moment a chicken was executed and is already visually chaotic,
    /// and it is one-shot per death. If a live session shows it as a snap, clear
    /// <c>_enableHitStop</c> on the Chicken prefab.
    /// </remarks>
    internal sealed class HitStopDriver : MonoBehaviour
    {
        private static HitStopDriver     _instance;
        private static UnityEngine.Object _requester;
        private static bool              _active;

        private float _endUnscaledTime;

        /// <summary>Starts a dip if none is running. No-ops on a nested request, a
        /// non-positive duration, outside play mode, or if the host cannot be created.</summary>
        public static void Request(UnityEngine.Object requester, float timeScale, float durationSeconds)
        {
            if (_active) return;                       // guarantee 2: never nest
            if (durationSeconds <= 0f) return;
            if (!Application.isPlaying) return;

            var driver = Ensure();
            if (driver == null) return;

            driver._endUnscaledTime = Time.unscaledTime + durationSeconds;
            _requester = requester;
            _active    = true;

            // Clamped so a bad tuning value can never fully freeze the process.
            Time.timeScale = Mathf.Clamp(timeScale, 0.05f, 1f);
        }

        /// <summary>Releases a dip early if <paramref name="requester"/> is the one that
        /// started it — called from the requester's <c>OnDisable</c>/<c>OnDestroy</c>/
        /// <c>Despawned</c> so a chicken destroyed mid-dip is doubly covered.</summary>
        public static void CancelIfRequestedBy(UnityEngine.Object requester)
        {
            if (!_active) return;
            if (requester != null && _requester != null && _requester != requester) return;
            Release();
        }

        private static HitStopDriver Ensure()
        {
            if (_instance != null) return _instance;

            // HideAndDontSave already implies "survives a scene load", so no
            // DontDestroyOnLoad call (which would only add a warning path here).
            var go = new GameObject("[HitStop]") { hideFlags = HideFlags.HideAndDontSave };
            _instance = go.AddComponent<HitStopDriver>();
            return _instance;
        }

        private static void Release()
        {
            _active    = false;
            _requester = null;
            Time.timeScale = 1f;
        }

        private void Update()
        {
            // Unscaled: the dip must not be able to extend its own deadline.
            if (_active && Time.unscaledTime >= _endUnscaledTime) Release();
        }

        private void OnDisable()          { if (_active) Release(); }
        private void OnDestroy()          { if (_active) Release(); if (_instance == this) _instance = null; }
        private void OnApplicationQuit()  { if (_active) Release(); }

        /// <summary>
        /// Belt and braces for the Editor: with "Enter Play Mode Options" suppressing the
        /// domain reload, a <c>timeScale</c> left dipped by a previous run would otherwise
        /// carry into the next one. Forces a clean slate at the start of every session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance  = null;
            _requester = null;
            _active    = false;
            if (!Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = 1f;
        }
    }
}
