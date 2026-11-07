using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
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
    /// <b>Material.</b> One shared URP Lit placeholder with a MaterialPropertyBlock (<c>_BaseColor</c>, <c>_Crack</c>,
    /// <c>_StripeColor</c>); D3 swaps in the shader that reads the last two.
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

        /// <summary>
        /// The uniform scale the bird wears inside the egg: <see cref="FeedbackTuning.ShellBodyScale"/>, or less when a capped egg
        /// (the largest class) would otherwise leave more than the comb allowance above it.
        /// </summary>
        public static float BodyScaleFor(float classScale) =>
            Mathf.Min(FeedbackTuning.ShellBodyScale,
                (EggWorldHeight(classScale) / Mathf.Max(0.01f, classScale) + FeedbackTuning.ShellBodyTopAllowance) / FeedbackTuning.ShellModelHeight);

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
        [Tooltip("The shared URP Lit placeholder (Art/Materials/Egg_Shell_Placeholder.mat). Never instanced; per-chicken values go through a MaterialPropertyBlock.")]
        [SerializeField] private Material _shellMaterial;

        // ---- Runtime state ------------------------------------------------------------

        private enum State : byte { Off, Walk, Close, Idle, Open, Fold }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CrackId = Shader.PropertyToID("_Crack");
        private static readonly int StripeColorId = Shader.PropertyToID("_StripeColor");

        private ILogService _log;
        private ChickenController _controller;
        private ChickenCombat _combat;
        private AbilityController _abilities;
        private ChickenAnimator _animator;
        private ChickenVFX _vfx;
        private CharacterController _cc;
        private ChickenWorldBars _bars;
        private float _bodyScale = 1f;

        private Transform _shellRoot, _cupPivot, _capPivot;
        private MeshRenderer _cupRenderer, _capRenderer;
        private MaterialPropertyBlock _mpb;
        private float _seamY;                 // seated height of the cap's seam in shell-model units

        private State _state;
        private float _stateT;
        private Phase _prevPhase;
        private bool _seeded;
        private bool _reduced;
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
            public ChickenVFX.FxEmit Heel, Puff, Shard, Feather, Yolk;
        }

        [Inject]
        public void Construct(ILogService log) => _log = log;

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
            BuildShell();
            BuildPresets();
        }

        private void OnDisable()
        {
            if (_animator != null) { _animator.ExtraRollDegrees = 0f; _animator.ExtraUniformScale = 1f; }
            _bodyScale = 1f;
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
            _fx.Shard = new ChickenVFX.FxEmit
            {
                Count = FeedbackTuning.ShellShardCount, Color = cream, SizeMin = 0.14f, SizeMax = 0.20f, SpeedMin = 2f, SpeedMax = 4f,
                LifeMin = 0.55f, LifeMax = 0.65f, Shape = ChickenVFX.FxShape.Cone, Radius = 0.05f, Axis = Vector3.up,
                ConeMinAngle = 45f, ConeAngle = 65f,   // 25-45 degrees of elevation
                Gravity = 0f,                          // the shard system carries real gravity 1
            };
            _fx.Feather = new ChickenVFX.FxEmit
            {
                Count = FeedbackTuning.ShellFeatherCount, Color = Color.white, SizeMin = 0.30f, SizeMax = 0.50f, SpeedMin = 2.0f, SpeedMax = 4.5f,
                LifeMin = 0.45f, LifeMax = 0.55f, Shape = ChickenVFX.FxShape.Sphere, Radius = 0.30f, Gravity = 0.6f, RandomRotation = true,
            };
            var yolk = FeedbackPalette.Current.YolkColor; yolk.a = FeedbackTuning.ShellYolkAlpha;
            _fx.Yolk = new ChickenVFX.FxEmit
            {
                Count = FeedbackTuning.ShellYolkPuffs, Color = yolk, SizeMin = 0.30f, SizeMax = 0.45f, SpeedMin = 1.2f, SpeedMax = 1.2f,
                LifeMin = 0.30f, LifeMax = 0.40f, Shape = ChickenVFX.FxShape.Disc, Radius = 0.10f,
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
                if (_abilities.GetSlot(i) is TurtleModeAbilitySO turtle)
                {
                    _activeNow = true;
                    eggSeconds = turtle.EggSeconds;
                    remaining = _abilities.ActiveRemaining01For(i) * turtle.ResolveActiveDuration(_controller);
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
        }

        private void ResetPose()
        {
            _cupRise = 0f; _cupYScale = 1f; _capSeatFactor = 1f; _capLift = _capDrift = _capTumble = 0f; _capScale = 1f;
            _squash = Vector3.one; _uniform = 1f; _rootRoll = 0f; _breath = 1f;
            _bodyScale = 1f;
        }

        private void GoOff()
        {
            _state = State.Off;
            ResetPose();
            _capVisible = _cupVisible = false;
            if (_animator != null) _animator.ExtraUniformScale = 1f;
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
            // The bird shrinks to fit inside the egg: eased over the close, held through the idle, eased back over the open.
            switch (_state)
            {
                case State.Close:
                    _bodyScale = Mathf.Lerp(1f, BodyScaleFor(transform.localScale.x),
                        Mathf.SmoothStep(0f, 1f, _stateT / (_reduced ? FeedbackTuning.ShellCloseReducedSeconds : FeedbackTuning.ShellCloseSeconds)));
                    break;
                case State.Idle: _bodyScale = BodyScaleFor(transform.localScale.x); break;
                case State.Open:
                    _bodyScale = Mathf.Lerp(BodyScaleFor(transform.localScale.x), 1f,
                        Mathf.SmoothStep(0f, 1f, _stateT / (_reduced ? FeedbackTuning.ShellOpenReducedSeconds : FeedbackTuning.ShellOpenSeconds)));
                    break;
                default: _bodyScale = 1f; break;
            }
            if (_animator != null) _animator.ExtraUniformScale = _bodyScale;

            float rate = 1f / FeedbackTuning.ShellWaddleBlendSeconds;
            _waddleBlend = Mathf.MoveTowards(_waddleBlend, waddleTarget, dt * rate);
            if (_animator != null)
            {
                float deg = _reduced ? FeedbackTuning.ShellWaddleReducedDegrees : FeedbackTuning.ShellWaddleDegrees;
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
            _capSeatFactor = Mathf.Lerp(FeedbackTuning.ShellCapDropFromScale, 1f, drop * drop);              // ease-in quad
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
            if (_reduced)
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

            float sink = Mathf.Clamp01(_stateT / FeedbackTuning.ShellCupSinkSeconds);
            _cupYScale = 1f - sink * sink;                    // ease-in
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
            d.y = 0f;
            float step = d.magnitude;
            if (step > 1.5f) return;                            // a teleport, not a walk
            _dustDist += step;
            float spacing = 1f / (FeedbackTuning.ShellWalkDustPerMeter * (_reduced ? 0.5f : 1f));
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

            var shard = _fx.Shard;
            if (_reduced)
            {
                // Two shards, no pull: a short drift (the shard system's gravity is cancelled by the baked lift).
                shard.Count = Mathf.Max(1, shard.Count - 1);
                shard.SpeedMin = shard.SpeedMax = 0.5f;
                shard.Gravity = -1f;
            }
            _vfx.EmitShards(in shard, seamPoint);

            var feathers = _fx.Feather;
            var tint = _controller.ClassBodyTint;
            tint.a = FeedbackTuning.ShellFeatherAlpha;
            feathers.Color = tint;
            if (_reduced) feathers.Count = Mathf.Max(1, feathers.Count / 2);
            _vfx.EmitFeathersAt(in feathers, seamPoint);

            var yolk = _fx.Yolk;
            if (_reduced) yolk.Count = Mathf.Max(1, yolk.Count / 2);
            _vfx.EmitDustAt(in yolk, GroundPoint(0.5f));
        }

        private void Warn(string message)
        {
            if (_warned) return;
            _warned = true;
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            _log?.Warn(Source, message);
        }
    }
}
