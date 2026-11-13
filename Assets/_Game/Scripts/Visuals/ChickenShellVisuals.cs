using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Injection;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The Egg Shell body (batch D2, vfx.md 1.2-1.5): a bottom cup that rises around the legs while the bird waddles, a cap that
    /// drops to close the egg, an idle egg that wobbles and cracks, and the hatch. Purely local and purely derived: every phase
    /// comes from replicated state (the Shield slot's active flag and <see cref="ChickenController.IsControlImmune"/>), edge-polled
    /// like <c>ControlStateVFX.ObserveKnockbackEdge</c> (the first observation only seeds, so a late join shows the right pose
    /// without replaying the animation). No RPC, no networked field.
    /// </summary>
    /// <remarks>
    /// <b>Phases.</b> Walk = slot active and not control-immune; Egg = slot active and control-immune; the End edge is either one
    /// dropping (<see cref="PhaseOf"/>, <see cref="EdgeOf"/>, both pure and tested).
    /// <b>Geometry.</b> The two halves come from <c>EggShell.fbx</c> (1 m tall, origin at the egg base, the top half pivoting at the
    /// crack seam), re-parented under pivots built at runtime below a shell root that is a child of the chicken ROOT (never the
    /// animated model) and sits on the grass (<see cref="ChickenGround"/>). The shell is scaled to
    /// <c>ShellEggBaseHeight x min(ClassScale, ShellEggScaleCap)</c> in world space, so the tallest egg stays under the nameplate.
    /// The meshes are opaque and the chicken keeps rendering inside them (no renderer toggling).
    /// <b>Single write.</b> The waddle is a plain roll value handed to <see cref="ChickenAnimator.ExtraRollDegrees"/>, which adds it
    /// to its own bank accumulator; this component never writes the model transform.
    /// <b>Overlays.</b> It never asks the stun stars, the status ring, the badges or the nameplate to hide anything: a stun cannot
    /// land during the egg (control immunity), so there is nothing to suppress (pinned by <c>ShellVisualsTests</c>).
    /// <b>Material.</b> One shared <c>Egg_Shell.mat</c> (<c>Cluck Wars/Egg Shell</c>, D3) with a MaterialPropertyBlock
    /// (<c>_BaseColor</c>, <c>_Crack</c>, <c>_StripeColor</c>); the stripe bands read UV.v = whole-egg height, so the two halves need no offset.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenShellVisuals : MonoBehaviour
    {
        private const string Source = "ChickenShellVisuals";

        // ---- Pure phase rules (public for tests) ----------------------------------

        /// <summary>The Shield's body phase.</summary>
        public enum Phase : byte { None = 0, Walk = 1, Egg = 2 }

        /// <summary>What changed between two observed phases.</summary>
        public enum Edge : byte { None = 0, WalkStart, Close, Open, WalkEnd }

        /// <summary>Walk = slot active and not control-immune; Egg = slot active and control-immune; otherwise none.</summary>
        public static Phase PhaseOf(bool slotActive, bool controlImmune) =>
            !slotActive ? Phase.None : controlImmune ? Phase.Egg : Phase.Walk;

        /// <summary>
        /// The edge between the last observed phase and this one. Walk start (none to walk), close (anything to egg: a walk that
        /// was skipped still closes), open (the egg ends by EITHER edge: the slot over, or immunity closed while it still runs:
        /// timeout, second press, another cast), walk end (a walk that ends without an egg).
        /// </summary>
        public static Edge EdgeOf(Phase prev, Phase cur)
        {
            if (prev == cur) return Edge.None;
            if (cur == Phase.Egg) return Edge.Close;
            if (prev == Phase.Egg) return Edge.Open;
            if (cur == Phase.Walk) return Edge.WalkStart;
            return Edge.WalkEnd;
        }

        /// <summary>The egg's scale for a class: <c>min(ClassScale, cap)</c>.</summary>
        public static float EggScale(float classScale) => Mathf.Min(classScale, FeedbackTuning.ShellEggScaleCap);

        /// <summary>The seated egg's world height for a class (m).</summary>
        public static float EggWorldHeight(float classScale) => FeedbackTuning.ShellEggBaseHeight * EggScale(classScale);

        /// <summary>H2: where the cap-top feathers start: the seam point lifted by <see cref="FeedbackTuning.ShellCapFeatherOffset"/>.</summary>
        public static Vector3 CapFeatherOrigin(Vector3 seamPoint) => seamPoint + Vector3.up * FeedbackTuning.ShellCapFeatherOffset;

        /// <summary>
        /// H2: the baked lift (m/s) that makes the cap-top feathers peak <see cref="FeedbackTuning.ShellCapFeatherClearance"/> above the
        /// hatched bird's head: head = <see cref="FeedbackTuning.ShellModelHeight"/> x class scale, apex = seam + offset + rise, and the rise is
        /// <see cref="ChickenVFX.FeatherRise"/> inverted at the shortest life (the conservative end).
        /// </summary>
        public static float CapFeatherLift(float seamHeight, float classScale)
        {
            const float life = HatchFeatherLifeMin;
            float rise = FeedbackTuning.ShellModelHeight * classScale + FeedbackTuning.ShellCapFeatherClearance
                       - seamHeight - FeedbackTuning.ShellCapFeatherOffset;
            float vy = rise <= ChickenVFX.FeatherSpeedLimit * life
                ? rise / life
                : ChickenVFX.FeatherSpeedLimit + (rise - ChickenVFX.FeatherSpeedLimit * life) / ChickenVFX.FeatherDragSettleSeconds;
            return vy + 0.5f * FeedbackTuning.ShellCapBurstGravity * 9.81f * life;
        }

        private const float HatchFeatherLifeMin = 0.45f, HatchFeatherLifeMax = 0.55f;   // both feather halves

        /// <summary>H2: the cap-top feathers' height above the feet (m) at the end of their shortest life, from the same drag-aware rise the runtime flies.</summary>
        public static float CapFeatherApexHeight(float seamHeight, in ChickenVFX.FxEmit capFeather) =>
            seamHeight + FeedbackTuning.ShellCapFeatherOffset + ChickenVFX.FeatherRise(in capFeather, capFeather.LifeMin);

        /// <summary>F2: the hatch debris disc radius in metres, scaled per class exactly like the egg.</summary>
        public static float BurstRadius(float classScale) => FeedbackTuning.ShellBurstRadius * EggScale(classScale);

        /// <summary>
        /// The uniform scale the bird wears inside the egg: the class's tuck (<see cref="FeedbackTuning.ShellBodyTuckFor"/>, E1a),
        /// or less when a capped egg (the largest class) would otherwise leave more than the comb allowance above it.
        /// </summary>
        public static float BodyScaleFor(float classScale, ChickenClass cls) =>
            Mathf.Min(FeedbackTuning.ShellBodyTuckFor(cls),
                (EggWorldHeight(classScale) / Mathf.Max(0.01f, classScale) + FeedbackTuning.ShellBodyTopAllowance) / FeedbackTuning.ShellModelHeight);

        /// <summary>
        /// The cap's height as a multiple of its seated height during the close: <see cref="FeedbackTuning.ShellCapDropFromScale"/>
        /// down to 1 on an ease-in quad over <see cref="FeedbackTuning.ShellCloseDropSeconds"/>. <see cref="TickClose"/> flies exactly
        /// this, so a guard can ask when the shell has closed far enough to be drawn over the bird.
        /// </summary>
        public static float CapSeatFactor(float stateT)
        {
            float drop = Mathf.Clamp01(stateT / FeedbackTuning.ShellCloseDropSeconds);
            return Mathf.Lerp(FeedbackTuning.ShellCapDropFromScale, 1f, drop * drop);
        }

        /// <summary>How far down the cap has travelled at <paramref name="stateT"/>: 0 still up where it started, 1 seated.</summary>
        public static float CapTravel01(float stateT) =>
            Mathf.InverseLerp(FeedbackTuning.ShellCapDropFromScale, 1f, CapSeatFactor(stateT));

        /// <summary>
        /// The cup's vertical scale during the hatch: 1 down to 0 on an ease-in quad over
        /// <see cref="FeedbackTuning.ShellCupSinkSeconds"/>. <see cref="TickOpen"/> flies exactly this; at 0 there is no shell left
        /// for the returning bird to be drawn over.
        /// </summary>
        public static float CupSinkYScale(float stateT)
        {
            float sink = Mathf.Clamp01(stateT / FeedbackTuning.ShellCupSinkSeconds);
            return 1f - sink * sink;
        }

        /// <summary>
        /// The extra uniform shrink the body wears while it is concealed: 1 at conceal 0 down to
        /// <see cref="FeedbackTuning.ShellBodyVanishScale"/> at 1, on <c>conceal^<see cref="FeedbackTuning.ShellBodyVanishLead"/></c>
        /// so the shrink LEADS the fade. This is why a bird bigger than its egg never stipples over or outside the shell: by the
        /// time it is translucent enough to read as a dither, it is already small enough to sit inside it.
        /// </summary>
        public static float BodyVanishScale(float conceal01) =>
            Mathf.Lerp(1f, FeedbackTuning.ShellBodyVanishScale,
                       Mathf.Pow(Mathf.Clamp01(conceal01), FeedbackTuning.ShellBodyVanishLead));

        /// <summary>
        /// How far the body is concealed over the CLOSE phase: 0 at the start, 1 once
        /// <see cref="FeedbackTuning.ShellBodyFadeOutFraction"/> of the phase has passed — by construction the moment the cap
        /// seats and the egg seals. Smoothstepped so it rides the same shape as the tuck instead of reading as a cut.
        /// </summary>
        /// <remarks>Taken as a fraction of the phase it belongs to, so it is identical for every class and for Reduced Motion
        /// (which only shortens the phase), and can never outlast it.</remarks>
        public static float CloseConceal01(float stateT, float closeSeconds)
        {
            if (closeSeconds <= 0f) return 1f;
            float span = closeSeconds * FeedbackTuning.ShellBodyFadeOutFraction;
            if (span <= 0f) return 1f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stateT / span));
        }

        /// <summary>
        /// How far the body is still concealed over the OPEN phase: fully hidden for the first
        /// <see cref="FeedbackTuning.ShellBodyFadeInDelayFraction"/> of it (the cap pops, the shards and yolk fly, the cap
        /// feathers climb), then back to 0 over <see cref="FeedbackTuning.ShellBodyFadeInFraction"/> — finished before the
        /// phase ends, so the bird is whole by the time the shell is gone and never appears before it has broken.
        /// </summary>
        public static float OpenConceal01(float stateT, float openSeconds)
        {
            if (openSeconds <= 0f) return 0f;
            float delay = openSeconds * FeedbackTuning.ShellBodyFadeInDelayFraction;
            float span = openSeconds * FeedbackTuning.ShellBodyFadeInFraction;
            if (span <= 0f) return stateT < delay ? 1f : 0f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((stateT - delay) / span));
        }

        /// <summary>Fraction of the egg phase already elapsed, from the slot's remaining seconds.</summary>
        public static float EggElapsed01(float remainingSeconds, float eggSeconds) =>
            eggSeconds <= 0f ? 1f : Mathf.Clamp01(1f - remainingSeconds / eggSeconds);

        /// <summary><c>smoothstep(start, end, elapsed01)</c>: first cracks at 40 % of the egg, the full web at 90 %.</summary>
        public static float Crack01(float elapsed01)
        {
            float t = Mathf.Clamp01((elapsed01 - FeedbackTuning.ShellCrackStart) /
                                    (FeedbackTuning.ShellCrackEnd - FeedbackTuning.ShellCrackStart));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Ease-out-back with the given peak (1.06 = a 6 % overshoot): <c>1 + (s+1)x^3 + s x^2</c>, x = t - 1.</summary>
        public static float EaseOutBack(float t, float overshoot)
        {
            float s = BackStrength(overshoot);
            float x = Mathf.Clamp01(t) - 1f;
            return 1f + (s + 1f) * x * x * x + s * x * x;
        }

        /// <summary>The back-ease strength whose peak is <paramref name="overshoot"/> (peak = 1 + 4s^3 / (27 (s+1)^2)).</summary>
        public static float BackStrength(float overshoot)
        {
            float target = Mathf.Max(0f, overshoot - 1f), lo = 0f, hi = 4f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float peak = 4f * mid * mid * mid / (27f * (mid + 1f) * (mid + 1f));
                if (peak < target) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        // ---- Serialized assets (Chicken.prefab) -------------------------------------

        [Tooltip("Art/Meshes/Fx/EggShell.fbx: EggShell_Top (pivot at the seam) and EggShell_Bottom; EggShell_Whole is discarded.")]
        [SerializeField] private GameObject _shellModel;
        [Tooltip("The shared Egg Shell material (Art/Materials/Egg_Shell.mat, shader Cluck Wars/Egg Shell). Never instanced; per-chicken values go through a MaterialPropertyBlock.")]
        [SerializeField] private Material _shellMaterial;

        // ---- Runtime state ------------------------------------------------------------

        private enum State : byte { Off, Walk, Close, Idle, Open, Fold }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CrackId = Shader.PropertyToID("_Crack");
        private static readonly int StripeColorId = Shader.PropertyToID("_StripeColor");

        private ILogService _log;
        private IAudioService _audio;         // E1: the clack / crack-pop one-shots (local, observed off the edges)
        private AudioRegistrySO _sfx;
        private IHapticsService _haptics;     // E1: the caster's close tick
        private bool _missingAudioReported;
        private ChickenController _controller;
        private ChickenCombat _combat;
        private AbilityController _abilities;
        private ChickenAnimator _animator;
        private ChickenVFX _vfx;
        private CharacterController _cc;
        private ChickenWorldBars _bars;
        private ChickenVisuals _visuals;      // the one body-opacity path (ghost fade + renderer toggle), shared with Invisibility
        private bool _missingVisualsReported;
        private float _bodyConceal;           // 0 the bird draws, 1 it is sealed inside the egg (Maestro 2026-11-13)
        private float _bodyScale = 1f;
        private float _bodySink;              // E1a: world metres the bird sinks into the egg, eased with the tuck
        private float _bodySqueezeZ = 1f;     // E1a: front-back squeeze (model local z), eased with the tuck

        private Transform _shellRoot, _cupPivot, _capPivot;
        private MeshRenderer _cupRenderer, _capRenderer;
        private MaterialPropertyBlock _mpb;
        private float _seamY;                 // seated height of the cap's seam in shell-model units

        private State _state;
        private float _stateT;
        private Phase _prevPhase;
        private bool _seeded;
        private bool _reduced;

        /// <summary>The live preference, for the CONTINUOUS motion (waddle, idle wobble/breath, heel dust): a mid-match toggle takes effect on
        /// the next frame. The per-state transition variants stay latched in <c>_reduced</c> at their edge so one close/open never mixes both.</summary>
        private static bool Reduced => PlayerPreferences.ReducedMotionEnabled;
        private bool _built;
        private bool _warned;

        // Pose, rewritten every frame by Tick.
        private float _cupRise;               // 0..1 of the seated cup (walk 0.35)
        private float _cupYScale = 1f;
        private float _capSeatFactor = 1f;    // cap height as a multiple of its seated height
        private float _capLift, _capDrift, _capTumble;   // metres, metres, degrees
        private float _capScale = 1f;
        private bool _capVisible, _cupVisible, _rootActive;
        private Vector3 _squash = Vector3.one;
        private float _uniform = 1f;
        private float _rootRoll;
        private float _breath = 1f;

        private float _closeRise0;
        private float _foldRise0;
        private bool _closePuffsDone;

        private float _crack;
        private float _stripeAlpha = 1f;
        private float _nextPush;
        private float _eggUnitWorld = 1f;     // world scale of the shell root (m per model unit)

        private float _waddleBlend;
        private Vector3 _lastPos;
        private float _dustDist;

        private FxEmitSet _fx;

        private struct FxEmitSet
        {
            public ChickenVFX.FxEmit Heel, Puff, Shard, Feather, CapFeather, Yolk;
        }

        [Inject]
        public void Construct(ILogService log, [InjectOptional] IAudioService audio, [InjectOptional] AudioRegistrySO sfx,
                              [InjectOptional] IHapticsService haptics)
        {
            _log = log;
            _audio = audio;
            _sfx = sfx;
            _haptics = haptics;
        }

        // ---- Lifecycle ------------------------------------------------------------------

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();
            _abilities = GetComponent<AbilityController>();
            _animator = GetComponent<ChickenAnimator>();
            _vfx = GetComponent<ChickenVFX>();
            _cc = GetComponent<CharacterController>();
            _bars = GetComponent<ChickenWorldBars>();   // read-only: the identity colour for the stripe
            _visuals = GetComponent<ChickenVisuals>();  // the body's opacity / renderer owner
            BuildShell();
            BuildPresets();
        }

        private void OnDisable()
        {
            if (_animator != null) { _animator.ExtraRollDegrees = 0f; _animator.ExtraUniformScale = 1f; _animator.ExtraZScale = 1f; _animator.ExtraYOffset = 0f; }
            SetBodyConceal(0f);
            _bodyScale = 1f;
            _bodySink = 0f;
            _bodySqueezeZ = 1f;
            _waddleBlend = 0f;
            _seeded = false;
            _state = State.Off;
            if (_shellRoot != null) _shellRoot.gameObject.SetActive(false);
            _rootActive = false;
        }

        private void BuildShell()
        {
            if (_shellModel == null || _shellMaterial == null)
            {
                Warn("The shell model or material is not wired on " + name + "; the Egg Shell has no body (re-wire ChickenShellVisuals on Chicken.prefab).");
                return;
            }

            _shellRoot = new GameObject("EggShell").transform;
            _shellRoot.SetParent(transform, worldPositionStays: false);

            var model = Instantiate(_shellModel, _shellRoot, worldPositionStays: false);
            Transform top = null, bottom = null;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.EndsWith("_Top")) top = mf.transform;
                else if (mf.name.EndsWith("_Bottom")) bottom = mf.transform;
            }
            if (top == null || bottom == null)
            {
                Warn("EggShell.fbx on " + name + " has no EggShell_Top / EggShell_Bottom child.");
                Discard(model);
                Discard(_shellRoot.gameObject);
                _shellRoot = null;
                return;
            }

            // The top half is authored with its origin at the seam; its authored position IS the seated seam height.
            _seamY = top.localPosition.y;
            _cupPivot = new GameObject("Cup").transform;
            _cupPivot.SetParent(_shellRoot, false);
            _capPivot = new GameObject("Cap").transform;
            _capPivot.SetParent(_shellRoot, false);
            _capPivot.localPosition = new Vector3(0f, _seamY, 0f);
            bottom.SetParent(_cupPivot, worldPositionStays: false);
            top.SetParent(_capPivot, worldPositionStays: false);
            top.localPosition = Vector3.zero;

            _cupRenderer = bottom.GetComponent<MeshRenderer>();
            _capRenderer = top.GetComponent<MeshRenderer>();
            Discard(model);   // the whole egg and the empty FBX root

            SetupRenderer(_cupRenderer);
            SetupRenderer(_capRenderer);
            ApplyShadowTier();

            _mpb = new MaterialPropertyBlock();
            _shellRoot.gameObject.SetActive(false);
            _built = true;
        }

        private void BuildPresets()
        {
            var cream = FeedbackPalette.Current.CreamColor;
            var heel = cream; heel.a = FeedbackTuning.ShellWalkDustAlpha;
            _fx.Heel = new ChickenVFX.FxEmit
            {
                Count = 1, Color = heel, SizeMin = 0.22f, SizeMax = 0.30f, SpeedMin = 0.15f, SpeedMax = 0.35f,
                LifeMin = 0.30f, LifeMax = 0.40f, Shape = ChickenVFX.FxShape.Disc, Radius = 0.10f,
            };
            _fx.Puff = new ChickenVFX.FxEmit
            {
                Count = FeedbackTuning.ShellClosePuffs, Color = cream, SizeMin = 0.30f, SizeMax = 0.45f, SpeedMin = 0.6f, SpeedMax = 1.0f,
                LifeMin = 0.25f, LifeMax = 0.35f, Shape = ChickenVFX.FxShape.Disc, Radius = FeedbackTuning.ShellClosePuffRadius,
            };
            // F2: the whole hatch leaves from a seam-height disc (radius scaled per class in EmitOpenBurst), flying outward with a
            // constant lift and a light 0.3 g pull, so it clears the shell outline before it drops instead of pooling under the ring.
            _fx.Shard = new ChickenVFX.FxEmit
            {
                Count = FeedbackTuning.ShellShardCount, Color = cream, SizeMin = 0.14f, SizeMax = 0.20f, SpeedMin = 2f, SpeedMax = 4f,
                LifeMin = 0.55f, LifeMax = 0.65f, Shape = ChickenVFX.FxShape.Disc, Radius = FeedbackTuning.ShellBurstRadius,
                Lift = FeedbackTuning.ShellBurstLift,
                Gravity = FeedbackTuning.ShellBurstGravity - 1f,   // the shard system carries real gravity 1: bake back all but 0.3 g
            };
            _fx.Feather = new ChickenVFX.FxEmit
            {
                Count = FeedbackTuning.ShellFeatherCount - FeedbackTuning.ShellCapFeatherCount, Color = Color.white, SizeMin = 0.30f, SizeMax = 0.50f, SpeedMin = 2.0f, SpeedMax = 4.5f,
                LifeMin = HatchFeatherLifeMin, LifeMax = HatchFeatherLifeMax, Shape = ChickenVFX.FxShape.Disc, Radius = FeedbackTuning.ShellBurstRadius,
                Lift = FeedbackTuning.ShellBurstLift, Gravity = FeedbackTuning.ShellBurstGravity, RandomRotation = true,
            };
            // H2: the other half leaves from the cap top and climbs past the head (see FeedbackTuning.ShellCapBurstLift for the arithmetic).
            _fx.CapFeather = _fx.Feather;
            _fx.CapFeather.Count = FeedbackTuning.ShellCapFeatherCount;
            // (the lift is solved per class at emit time: CapFeatherLift)
            _fx.CapFeather.Gravity = FeedbackTuning.ShellCapBurstGravity;
            var yolk = FeedbackPalette.Current.YolkColor; yolk.a = FeedbackTuning.ShellYolkAlpha;
            _fx.Yolk = new ChickenVFX.FxEmit
            {
                Count = FeedbackTuning.ShellYolkPuffs, Color = yolk, SizeMin = 0.30f, SizeMax = 0.45f, SpeedMin = 1.2f, SpeedMax = 1.2f,
                LifeMin = 0.30f, LifeMax = 0.40f, Shape = ChickenVFX.FxShape.Disc, Radius = FeedbackTuning.ShellBurstRadius,
                Lift = FeedbackTuning.ShellBurstLift, Gravity = FeedbackTuning.ShellBurstGravity,
            };
        }

        private void ApplyShadowTier()
        {
            bool shadows = RenderTierPolicy.ShellShadowsFor(GraphicsSettings.currentRenderPipeline);
            SetShadows(_cupRenderer, shadows);
            SetShadows(_capRenderer, shadows);
        }

        // EditMode tests wake the component without play mode, where Destroy is illegal.
        private static void Discard(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        private void SetupRenderer(MeshRenderer r)
        {
            if (r == null) return;
            r.sharedMaterial = _shellMaterial;       // the shared asset, never an instance
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void SetShadows(MeshRenderer r, bool on)
        {
            if (r == null) return;
            r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = on;
        }

        // ---- Per frame -----------------------------------------------------------------------

        private void LateUpdate()
        {
            if (!_built || _controller == null || _combat == null) return;
            float dt = Time.deltaTime;

            if (!ReadShell(out float remaining, out float eggSeconds)) return;
            Phase cur = PhaseOf(true, _controller.IsControlImmune);
            // ReadShell reports "not active" as false; fold that into the phase.
            Observe(cur, remaining, eggSeconds);

            if (_state == State.Off && _waddleBlend <= 0f) return;
            Tick(dt, remaining, eggSeconds);
            Apply();
        }

        /// <summary>Reads the Shield slot off the replicated mask and timer. False only before the objects are valid.</summary>
        private bool ReadShell(out float remaining, out float eggSeconds)
        {
            remaining = 0f;
            eggSeconds = 0f;
            var obj = _controller.Object;
            if (obj == null || !obj.IsValid || _abilities == null) return false;
            var abilitiesObj = _abilities.Object;
            if (abilitiesObj == null || !abilitiesObj.IsValid) return false;

            _activeNow = false;
            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                if (!_abilities.IsSlotActive(i)) continue;
                var shield = _abilities.GetSlot(i);
                if (shield.Is(AbilityJob.Shield))
                {
                    _activeNow = true;
                    eggSeconds = shield.FollowUpPhaseSeconds;
                    remaining = _abilities.ActiveRemaining01For(i) * shield.ResolveActiveDuration(_controller);
                    break;
                }
            }
            return true;
        }

        private bool _activeNow;

        private void Observe(Phase curIfActive, float remaining, float eggSeconds)
        {
            Phase cur = _activeNow ? curIfActive : Phase.None;

            if (!_seeded)
            {
                _seeded = true;
                _prevPhase = cur;
                _reduced = PlayerPreferences.ReducedMotionEnabled;
                _lastPos = transform.position;
                if (cur == Phase.Walk) EnterWalk(seeded: true);
                else if (cur == Phase.Egg) EnterIdle(remaining, eggSeconds);
                return;
            }

            // A removed bird shows no shell and fires no burst (the death VFX owns that moment).
            if (_combat.IsRemoved)
            {
                _prevPhase = cur;
                if (_state != State.Off) GoOff();
                return;
            }

            var edge = EdgeOf(_prevPhase, cur);
            _prevPhase = cur;
            if (edge == Edge.None) return;

            _reduced = PlayerPreferences.ReducedMotionEnabled;
            switch (edge)
            {
                case Edge.WalkStart: EnterWalk(seeded: false); break;
                case Edge.Close: EnterClose(); break;
                case Edge.Open: EnterOpen(); break;
                case Edge.WalkEnd:
                    if (_state == State.Walk) { _state = State.Fold; _stateT = 0f; _foldRise0 = _cupRise; }
                    break;
            }
        }

        // ---- Edges ---------------------------------------------------------------------------

        private void PrepareShell()
        {
            ApplyShadowTier();
            _lastPos = transform.position;
            _dustDist = 0f;
        }

        private void EnterWalk(bool seeded)
        {
            PrepareShell();
            ResetPose();
            _state = State.Walk;
            _stateT = seeded ? 10f : 0f;
            _capVisible = false;
            _cupVisible = true;
            _cupRise = seeded ? FeedbackTuning.ShellWalkCupRise : 0f;
            _crack = 0f;
            _stripeAlpha = 1f;
            PushBlock();   // F2: a walk re-cast after an early-ended egg must not keep the last egg's cracks on the cup
        }

        private void EnterClose()
        {
            PrepareShell();
            float rise = _cupRise;            // a walk's risen cup closes from where it is; from nothing it rises from the ground
            ResetPose();
            _cupRise = rise;
            _closeRise0 = rise;
            _state = State.Close;
            _stateT = 0f;
            _closePuffsDone = false;
            if (_bars != null && _controller.HasInputAuthority && !ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity))
                _bars.FlashIdentityRing(FeedbackTuning.ShellRingFlashSeconds);
            _capVisible = true;
            _cupVisible = true;
            _cupYScale = 1f;
            _crack = 0f;
            _stripeAlpha = 1f;
            PushBlock();

            PlayEdgeSfx(open: false);
            // Reuses the 15 ms cancel tick (HapticRules.CancelArm) on the caster's own phone only; reveals nothing to anyone else.
            if (_controller.HasInputAuthority && !_controller.IsBot && !_controller.IsDecoy)
                _haptics?.Buzz(HapticKind.CancelArm);
        }

        private void EnterIdle(float remaining, float eggSeconds)
        {
            PrepareShell();
            _state = State.Idle;
            _stateT = 0f;
            _capVisible = true;
            _cupVisible = true;
            _cupRise = 1f;
            _cupYScale = 1f;
            _capSeatFactor = 1f;
            // A peer that starts observing mid-egg (the seeding pass) hides the bird on the spot: it is already sealed in,
            // so there is no fade to replay and nothing to see fading.
            SetBodyConceal(1f);
            _crack = Crack01(EggElapsed01(remaining, eggSeconds));
            PushBlock();
        }

        private void EnterOpen()
        {
            if (_state == State.Off) { GoOff(); return; }
            // An early end snaps the cracks to 0.6 first so the halves have a seam to split along.
            _crack = Mathf.Max(_crack, FeedbackTuning.ShellCrackOnEarlyEnd);
            PushBlock();
            _state = State.Open;
            _stateT = 0f;
            _capVisible = true;
            _cupVisible = true;
            EmitOpenBurst();
            PlayEdgeSfx(open: true);
        }

        /// <summary>
        /// E1: an edge one-shot, on every peer that sees the egg (the shell is drawn for everyone). Silent while the bird is
        /// faded, under the same opacity rule as every emission here, so Hide never leaks a position through audio. Reached only
        /// from the edge switch in <see cref="Observe"/>, never from the seeding pass, so a late join stays silent. An unfilled
        /// clip slot is a legal silent no-op; a missing service is reported once.
        /// </summary>
        private void PlayEdgeSfx(bool open)
        {
            // Self-injection on first use (Fusion spawns the chicken outside the container), as Warn does below.
            SelfInject.Ensure(this, _log != null);
            if (ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity) || HiddenVisibilityRules.IsHiddenFor(_controller))
                return;
            if (_audio == null || _sfx == null)
            {
                if (!_missingAudioReported)
                {
                    _missingAudioReported = true;
                    _log?.Warn(Source, "No IAudioService / AudioRegistrySO injected: the Egg Shell clack and hatch are silent.");
                }
                return;
            }
            _audio.TryPlaySFX(open ? SfxId.ShellOpen : SfxId.ShellClose);
        }

        /// <summary>The body conceal this shell currently asks for: 0 the bird draws, 1 it is sealed in (test/diagnostic seam).</summary>
        public float BodyConceal => _bodyConceal;

        /// <summary>
        /// Hands the body conceal to <see cref="ChickenVisuals"/>, which owns every way a chicken body stops being drawn
        /// (the shared dithered ghost for the fade, the renderers for the sealed stretch). Local presentation, derived from the
        /// same replicated phase as the shell itself, so it is identical on the owner's screen and on every remote peer.
        /// </summary>
        private void SetBodyConceal(float conceal01)
        {
            float c = Mathf.Clamp01(conceal01);
            if (_visuals == null)
            {
                // A wiring bug, not a game state: without it the bird stays visible and clips through its own egg.
                if (!_missingVisualsReported && c > 0f)
                {
                    _missingVisualsReported = true;
                    SelfInject.Ensure(this, _log != null);
                    _log?.Error(Source, name + ": no ChickenVisuals sibling; the bird cannot be hidden inside its Egg Shell " +
                                        "(re-add ChickenVisuals to Chicken.prefab).");
                }
                _bodyConceal = c;
                return;
            }
            _bodyConceal = c;
            _visuals.SetShellConcealment(c);
        }

        private void ResetPose()
        {
            _cupRise = 0f; _cupYScale = 1f; _capSeatFactor = 1f; _capLift = _capDrift = _capTumble = 0f; _capScale = 1f;
            _squash = Vector3.one; _uniform = 1f; _rootRoll = 0f; _breath = 1f;
            _bodyScale = 1f;
            _bodySink = 0f;
            _bodySqueezeZ = 1f;
        }

        private void GoOff()
        {
            _state = State.Off;
            ResetPose();
            SetBodyConceal(0f);
            _crack = 0f;
            _stripeAlpha = 1f;
            PushBlock();   // F2: the renderers carry a clean block while off, so nothing re-shows a stale crack web
            _capVisible = _cupVisible = false;
            if (_animator != null) { _animator.ExtraUniformScale = 1f; _animator.ExtraZScale = 1f; _animator.ExtraYOffset = 0f; }
            if (_shellRoot != null && _rootActive) { _shellRoot.gameObject.SetActive(false); _rootActive = false; }
        }

        // ---- Per-state pose --------------------------------------------------------------------

        private void Tick(float dt, float remaining, float eggSeconds)
        {
            _stateT += dt;
            float waddleTarget = 0f;

            switch (_state)
            {
                case State.Walk:
                {
                    waddleTarget = 1f;
                    float riseT = _stateT / (_reduced ? FeedbackTuning.ShellWalkRiseReducedSeconds : FeedbackTuning.ShellWalkRiseSeconds);
                    float e = _reduced ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(riseT))
                                       : EaseOutBack(riseT, FeedbackTuning.ShellWalkRiseOvershoot);
                    _cupRise = FeedbackTuning.ShellWalkCupRise * e;
                    HeelDust();
                    break;
                }
                case State.Close:
                    TickClose();
                    break;
                case State.Idle:
                    TickIdle(remaining, eggSeconds);
                    break;
                case State.Open:
                    TickOpen();
                    break;
                case State.Fold:
                {
                    float u = Mathf.Clamp01(_stateT / FeedbackTuning.ShellFoldSeconds);
                    _cupRise = _foldRise0 * (1f - u * u);
                    if (u >= 1f) GoOff();
                    break;
                }
            }

            // The waddle: a roll handed to the animator's bank accumulator, blended in and out.
            // The bird shrinks to fit inside the egg (and the Warrior sinks, E1a): eased over the close, held through the idle,
            // eased back over the open. tuck01 is how much of the class's egg pose is applied; scale and sink share it.
            float tuck01;
            switch (_state)
            {
                case State.Close:
                    tuck01 = Mathf.SmoothStep(0f, 1f, _stateT / (_reduced ? FeedbackTuning.ShellCloseReducedSeconds : FeedbackTuning.ShellCloseSeconds));
                    break;
                case State.Idle: tuck01 = 1f; break;
                case State.Open:
                    tuck01 = 1f - Mathf.SmoothStep(0f, 1f, _stateT / (_reduced ? FeedbackTuning.ShellOpenReducedSeconds : FeedbackTuning.ShellOpenSeconds));
                    break;
                default: tuck01 = 0f; break;
            }
            // The bird itself disappears into the egg and comes back out of the hatch (Maestro 2026-11-13). The egg is capped
            // per class, so the larger classes never fit it and used to clip through the shell; hiding the body is the fix that
            // changes no size. The ramps ride the same phases as the tuck above, so the fade and the squash read as one move.
            //
            // Reduced Motion gets no fade at all, on purpose: its close does not drop a cap, it grows the already-CLOSED egg
            // from ShellReducedGrowFrom, and its open shrinks that whole egg away. There is no open shell for a translucent
            // bird to be seen inside, so a partial alpha there could only ever be drawn over the egg. Hidden for the whole
            // of close / idle / open, released by GoOff: "alpha only, no travel", which is the Reduced Motion rule anyway.
            float conceal;
            switch (_state)
            {
                case State.Close: conceal = _reduced ? 1f : CloseConceal01(_stateT, FeedbackTuning.ShellCloseSeconds); break;
                case State.Idle:  conceal = 1f; break;
                case State.Open:  conceal = _reduced ? 1f : OpenConceal01(_stateT, FeedbackTuning.ShellOpenSeconds); break;
                default:          conceal = 0f; break;
            }
            SetBodyConceal(conceal);

            var cls = _controller.Class;   // Tick only runs after ReadShell found the object valid
            // The vanish shrink rides the conceal and LEADS it, so the body is already small enough to sit inside the shell
            // before it is translucent enough to read as a dither. Transient body scale only: the egg is untouched.
            _bodyScale = Mathf.Lerp(1f, BodyScaleFor(transform.localScale.x, cls), tuck01) * BodyVanishScale(conceal);
            _bodySink = FeedbackTuning.ShellBodySinkFor(cls) * tuck01;
            _bodySqueezeZ = Mathf.Lerp(1f, FeedbackTuning.ShellBodySqueezeZFor(cls), tuck01);
            if (_animator != null)
            {
                _animator.ExtraUniformScale = _bodyScale;
                _animator.ExtraZScale = _bodySqueezeZ;
                _animator.ExtraYOffset = -_bodySink;
            }

            float rate = 1f / FeedbackTuning.ShellWaddleBlendSeconds;
            _waddleBlend = Mathf.MoveTowards(_waddleBlend, waddleTarget, dt * rate);
            if (_animator != null)
            {
                float deg = Reduced ? FeedbackTuning.ShellWaddleReducedDegrees : FeedbackTuning.ShellWaddleDegrees;
                _animator.ExtraRollDegrees = _waddleBlend > 0f
                    ? deg * Mathf.Sin(2f * Mathf.PI * FeedbackTuning.ShellWaddleHz * Time.time) * _waddleBlend
                    : 0f;
            }
        }

        private void TickClose()
        {
            if (_reduced)
            {
                float dur = FeedbackTuning.ShellCloseReducedSeconds;
                float u = Mathf.Clamp01(_stateT / dur);
                _cupRise = 1f; _capSeatFactor = 1f;
                _uniform = Mathf.Lerp(FeedbackTuning.ShellReducedGrowFrom, 1f, 1f - (1f - u) * (1f - u));
                if (!_closePuffsDone) { _closePuffsDone = true; EmitClosePuffs(); }
                if (_stateT >= dur) { _uniform = 1f; _state = State.Idle; _stateT = 0f; }
                return;
            }

            float drop = Mathf.Clamp01(_stateT / FeedbackTuning.ShellCloseDropSeconds);
            _capSeatFactor = CapSeatFactor(_stateT);                                                        // ease-in quad
            _cupRise = Mathf.Lerp(_closeRise0, 1f, 1f - (1f - drop) * (1f - drop));                           // ease-out quad
            if (_stateT >= FeedbackTuning.ShellCloseDropSeconds)
            {
                if (!_closePuffsDone) { _closePuffsDone = true; EmitClosePuffs(); }
                float k = Mathf.Clamp01((_stateT - FeedbackTuning.ShellCloseDropSeconds) / FeedbackTuning.ShellSquashSeconds);
                float e = EaseOutBack(k, 1.06f);
                _squash = new Vector3(
                    Mathf.LerpUnclamped(FeedbackTuning.ShellSquashXZ, 1f, e),
                    Mathf.LerpUnclamped(FeedbackTuning.ShellSquashY, 1f, e),
                    Mathf.LerpUnclamped(FeedbackTuning.ShellSquashXZ, 1f, e));
            }
            if (_stateT >= FeedbackTuning.ShellCloseSeconds)
            {
                _squash = Vector3.one; _capSeatFactor = 1f; _cupRise = 1f;
                _state = State.Idle; _stateT = 0f;
            }
        }

        private void TickIdle(float remaining, float eggSeconds)
        {
            _cupRise = 1f; _capSeatFactor = 1f; _squash = Vector3.one; _uniform = 1f;
            bool jitter = remaining > 0f && remaining <= FeedbackTuning.ShellJitterSeconds;
            float t = Time.time;
            if (Reduced)
            {
                // Cracks only; the stripe pulses alpha 1 -> 0.6 at 2 Hz over the last 0.3 s instead of any motion.
                _rootRoll = 0f; _breath = 1f;
                _stripeAlpha = jitter ? 0.8f + 0.2f * Mathf.Cos(2f * Mathf.PI * 2f * t) : 1f;
            }
            else
            {
                _rootRoll = jitter
                    ? FeedbackTuning.ShellJitterDegrees * Mathf.Sin(2f * Mathf.PI * FeedbackTuning.ShellJitterHz * t)
                    : FeedbackTuning.ShellWobbleDegrees * Mathf.Sin(2f * Mathf.PI * FeedbackTuning.ShellWobbleHz * t);
                _breath = 1f + FeedbackTuning.ShellBreathAmount * Mathf.Sin(2f * Mathf.PI * FeedbackTuning.ShellBreathHz * t);
                _stripeAlpha = 1f;
            }

            // Cracks (and the reduced stripe pulse) are pushed at the status-arc rate, not per frame.
            if (Time.time >= _nextPush)
            {
                _nextPush = Time.time + 1f / FeedbackTuning.StatusArcDrainUpdateHz;
                _crack = Mathf.Max(_crack, Crack01(EggElapsed01(remaining, eggSeconds)));
                PushBlock();
            }
        }

        private void TickOpen()
        {
            _rootRoll = 0f; _breath = 1f; _squash = Vector3.one;
            if (_reduced)
            {
                float dur = FeedbackTuning.ShellOpenReducedSeconds;
                float u = Mathf.Clamp01(_stateT / dur);
                _uniform = 1f - u;
                _cupRise = 1f; _cupYScale = 1f; _capSeatFactor = 1f; _capLift = _capDrift = _capTumble = 0f; _capScale = 1f;
                if (_stateT >= dur) GoOff();
                return;
            }

            float up = Mathf.Clamp01(_stateT / FeedbackTuning.ShellCapPopSeconds);
            float arc = 1f - (1f - up) * (1f - up);           // decelerating rise along the parabola
            _capLift = FeedbackTuning.ShellCapPopHeight * arc;
            _capDrift = FeedbackTuning.ShellCapDrift * up;
            _capTumble = FeedbackTuning.ShellCapTumbleDegrees * up;
            float outStart = FeedbackTuning.ShellOpenSeconds - FeedbackTuning.ShellCapScaleOutSeconds;
            _capScale = _stateT <= outStart ? 1f : Mathf.Clamp01(1f - (_stateT - outStart) / FeedbackTuning.ShellCapScaleOutSeconds);

            _cupYScale = CupSinkYScale(_stateT);              // ease-in
            if (_stateT >= FeedbackTuning.ShellOpenSeconds) GoOff();
        }

        // ---- Apply ------------------------------------------------------------------------------

        private void Apply()
        {
            if (_shellRoot == null) return;

            bool hidden = HiddenVisibilityRules.IsHiddenFor(_controller);
            bool show = _state != State.Off && !hidden;
            if (show != _rootActive) { _shellRoot.gameObject.SetActive(show); _rootActive = show; }
            if (!show) return;

            // World height = base x min(ClassScale, cap): children of the scaled root carry the class scale already.
            float rootScale = Mathf.Max(0.01f, Mathf.Abs(transform.localScale.x));
            _eggUnitWorld = EggWorldHeight(rootScale);
            float local = _eggUnitWorld / rootScale;
            float groundY = _cc != null ? ChickenGround.LocalFeetY(_cc) - _cc.skinWidth / rootScale
                                        : ChickenGround.FallbackLocalFeetY - ChickenGround.FallbackSkinWidth / rootScale;
            _shellRoot.localPosition = new Vector3(0f, groundY, 0f);
            _shellRoot.localRotation = Quaternion.Euler(0f, 0f, _rootRoll);
            _shellRoot.localScale = new Vector3(_squash.x, _squash.y, _squash.z) * (local * _uniform * _breath);

            _cupPivot.localPosition = new Vector3(0f, -(1f - _cupRise) * _seamY, 0f);
            _cupPivot.localScale = new Vector3(1f, Mathf.Max(0.0001f, _cupYScale), 1f);

            _capPivot.localPosition = new Vector3(0f, _seamY * _capSeatFactor + _capLift / _eggUnitWorld, _capDrift / _eggUnitWorld);
            _capPivot.localRotation = Quaternion.Euler(_capTumble, 0f, 0f);
            _capPivot.localScale = Vector3.one * Mathf.Max(0.0001f, _capScale);

            if (_cupRenderer != null && _cupRenderer.enabled != _cupVisible) _cupRenderer.enabled = _cupVisible;
            if (_capRenderer != null && _capRenderer.enabled != _capVisible) _capRenderer.enabled = _capVisible;
        }

        private void PushBlock()
        {
            if (_mpb == null) return;
            var cream = FeedbackPalette.Current.CreamColor;
            var stripe = _bars != null ? _bars.IdentityColor : Color.white;   // the player identity colour (vfx.md 1.1)
            stripe.a = _stripeAlpha;
            _mpb.Clear();
            _mpb.SetColor(BaseColorId, cream);
            _mpb.SetFloat(CrackId, _crack);
            _mpb.SetColor(StripeColorId, stripe);
            if (_cupRenderer != null) _cupRenderer.SetPropertyBlock(_mpb);
            if (_capRenderer != null) _capRenderer.SetPropertyBlock(_mpb);
        }

        // ---- Emissions (each gated on the opacity rule) -------------------------------------------

        private bool CanEmit() => _vfx != null && !ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity);

        private Vector3 GroundPoint(float height)
        {
            float rootScale = Mathf.Max(0.01f, Mathf.Abs(transform.localScale.x));
            float groundY = _cc != null ? ChickenGround.LocalFeetY(_cc) - _cc.skinWidth / rootScale
                                        : ChickenGround.FallbackLocalFeetY - ChickenGround.FallbackSkinWidth / rootScale;
            Vector3 p = transform.TransformPoint(new Vector3(0f, groundY, 0f));
            p.y += height;
            return p;
        }

        private void HeelDust()
        {
            Vector3 p = transform.position;
            Vector3 d = p - _lastPos;
            _lastPos = p;
            d = Planar.Flat(d);
            float step = d.magnitude;
            if (step > 1.5f) return;                            // a teleport, not a walk
            _dustDist += step;
            float spacing = 1f / (FeedbackTuning.ShellWalkDustPerMeter * (Reduced ? 0.5f : 1f));
            while (_dustDist >= spacing)
            {
                _dustDist -= spacing;
                if (!CanEmit()) continue;
                Vector3 heel = GroundPoint(0.08f) - transform.forward * FeedbackTuning.ShellWalkDustBehind;
                _vfx.EmitDustAt(in _fx.Heel, heel);
            }
        }

        private void EmitClosePuffs()
        {
            if (!CanEmit()) return;
            var e = _fx.Puff;
            if (_reduced) e.Count = Mathf.Max(1, e.Count / 2);
            _vfx.EmitDustAt(in e, GroundPoint(0.08f));
        }

        private void EmitOpenBurst()
        {
            if (_combat.IsRemoved || !CanEmit()) return;   // never double-burst with the death VFX
            float seam = _seamY * _eggUnitWorld;
            Vector3 seamPoint = GroundPoint(seam);
            float radius = BurstRadius(Mathf.Abs(transform.localScale.x));   // class-scaled like the egg

            var shard = _fx.Shard;
            shard.Radius = radius;
            if (_reduced)
            {
                // Two shards, no pull, no lift: a short outward drift (the shard system's gravity is cancelled by the baked lift).
                shard.Count = Mathf.Max(1, shard.Count - 1);
                shard.SpeedMin = shard.SpeedMax = 0.5f;
                shard.Gravity = -1f;
                shard.Lift = 0f;
            }
            _vfx.EmitShards(in shard, seamPoint);

            var feathers = _fx.Feather;
            feathers.Radius = radius;
            var tint = _controller.ClassBodyTint;
            tint.a = FeedbackTuning.ShellFeatherAlpha;
            feathers.Color = tint;
            if (_reduced) feathers.Count = Mathf.Max(1, feathers.Count / 2);
            _vfx.EmitFeathersAt(in feathers, seamPoint);

            var capFeathers = _fx.CapFeather;
            capFeathers.Radius = radius;
            capFeathers.Lift = CapFeatherLift(seam, Mathf.Abs(transform.localScale.x));
            capFeathers.Color = tint;
            if (_reduced) capFeathers.Count = Mathf.Max(1, capFeathers.Count / 2);
            _vfx.EmitFeathersAt(in capFeathers, CapFeatherOrigin(seamPoint));

            var yolk = _fx.Yolk;
            yolk.Radius = radius;
            if (_reduced) yolk.Count = Mathf.Max(1, yolk.Count / 2);
            _vfx.EmitDustAt(in yolk, seamPoint);
        }

        private void Warn(string message)
        {
            if (_warned) return;
            _warned = true;
            SelfInject.Ensure(this, _log != null);
            _log?.Warn(Source, message);
        }
    }
}
