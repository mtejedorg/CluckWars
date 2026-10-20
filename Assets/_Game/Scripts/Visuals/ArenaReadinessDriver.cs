using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Ticks the arena readiness (<see cref="ArenaReadinessTracker"/>) once per rendered frame from the moment the Game scene
    /// starts, and runs the deterministic warm-up (<see cref="ArenaWarmupSequence"/>) behind the arena cover. Bound as
    /// <see cref="IArenaReadiness"/> in <c>GameInstaller</c> (scene scope: a new scene load starts a new readiness).
    /// </summary>
    /// <remarks>
    /// <para><b>Why its own object, not the MatchCamera.</b> The camera is a rendering component that must keep working in
    /// scenes with no match (the Ability Lab); readiness is a match-scene service with a Zenject lifetime, and a scene-scoped
    /// <c>FromNewComponentOnNewGameObject().NonLazy()</c> binding gives it exactly the scene's lifetime and makes a missing
    /// binding visible (consumers fall back and log an Error).</para>
    /// <para><b>Ticks from scene start, not from <c>Starting</c>.</b> In multiplayer the warm-up runs while the opaque waiting
    /// room is up, so the host's START finds every peer already warm.</para>
    /// <para><b>Presentation only.</b> Nothing here is networked, no RPC, no [Networked] value is touched: the warm-up forces
    /// local material variants on and moves the local camera, then releases both. The overrides are always released on
    /// completion, abort, disable and destroy.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]   // after MatchCamera.LateUpdate: it reads the camera's settled flag of this very frame
    public sealed class ArenaReadinessDriver : MonoBehaviour, IArenaReadiness
    {
        private const string Source = "ArenaReadiness";

        /// <summary>Metres of surround art beyond the arena edge that the overview frames.</summary>
        public const float SurroundMargin = 14f;

        /// <summary>Height of the tallest surround prop (the trees).</summary>
        public const float SurroundHeight = 7f;

        private ILogService _log;
        private readonly ArenaReadinessTracker _tracker = new ArenaReadinessTracker();
        private ArenaWarmupSequence _warmup;
        private readonly ArenaWarmupProxies _proxies = new ArenaWarmupProxies();

        private float _startTime = -1f;
        private float _warmupStartedAt = -1f, _warmupDoneAt = -1f;
        private bool _summaryLogged, _reportedNoCamera, _reportedNoMap, _dormant;
        private bool _rerun;
        private int _warmedClassMask, _pendingClassMask;

        private MapGenerator _map;
        private MatchCamera _camera;
        private FenceFadeDriver _fenceDriver;
        private FencePerformanceSwap _fenceSwap;
        private SurroundHazeSwap _hazeSwap;

        // ---- IArenaReadiness ---------------------------------------------------------------------------------
        public bool IsReady => _tracker.IsReady && !_rerun;
        public bool TimedOut => _tracker.TimedOut;
        public float ReadyAtSeconds => _tracker.ReadyAtSeconds;
        public string LastGate => _tracker.LastGate;
        public string PendingGates => _tracker.PendingGates;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            // Zenject builds this component with AddComponent, so Awake runs before the [Inject] call: self-inject
            // (ILogService is project-scoped; CONVENTIONS "Self-injection"). Never gate on ProjectContext.HasInstance.
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            _warmup = new ArenaWarmupSequence(ShaderCompileProbe.Create());
            _warmup.StepChanged += OnStepChanged;
            _tracker.GateCompleted += (gate, t) => _log?.Info(Source, $"Gate {gate} done at {t:0.00} s.");
        }

        private void OnDisable() => ReleaseOverrides();

        private void OnDestroy() => ReleaseOverrides();

        private void LateUpdate()
        {
            if (_dormant) return;
            // Real time, read live: Time.unscaledTime is frozen at the start of the frame, i.e. BEFORE the Game scene's load hitch of the
            // first frame, and would charge that hitch (1.5 s in the Editor) to the readiness cap.
            float now = Time.realtimeSinceStartup;
            if (_startTime < 0f) _startTime = now;
            float t = now - _startTime;
            float dt = Time.unscaledDeltaTime;

            var gm = GameManager.Instance;
            // From the round's intro on, the arena is on show: no warm-up may move the camera any more. A peer that is
            // ready simply goes dormant (the values stay readable); a peer that is not, keeps its cap and its gates.
            if (_tracker.IsReady && !_warmup.IsRunning && gm != null && (gm.State == MatchState.Active || gm.State == MatchState.Ended))
            {
                _dormant = true;
                return;
            }

            ResolveCamera(t);

            bool scene = _tracker.IsDone(ArenaGate.Scene) || SceneGateHolds(gm, t);
            bool tier = _tracker.IsDone(ArenaGate.Tier) || TierGateHolds();
            if (tier) LogRenderTier();

            if (!_tracker.IsReady)
            {
                if (scene && tier && _warmup.Step == WarmupStep.NotStarted)
                {
                    _warmupStartedAt = t;
                    _warmup.Begin();
                }
                else if (_warmup.IsRunning)
                {
                    _warmup.Tick(dt);
                }
            }
            else if (_warmup.IsRunning)
            {
                _warmup.Tick(dt);
                if (!_warmup.IsRunning) _rerun = false;
            }
            else
            {
                MaybeRerunForNewClass(gm);
            }

            bool warm = _warmup.Step == WarmupStep.Done;
            bool cameraSettled = warm && _camera != null && _camera.FollowSettled;
            bool wasReady = _tracker.IsReady;
            _tracker.Tick(t, scene, tier, warm, cameraSettled);

            if (!wasReady && _tracker.IsReady)
            {
                // The cap passed with the warm-up still going: stop it and hand the camera / materials back.
                if (_warmup.IsRunning) _warmup.Abort();
                LogReady(t);
            }
        }

        // ---- gates ------------------------------------------------------------------------------------------

        private void ResolveCamera(float t)
        {
            if (_camera == null) _camera = MatchCamera.Instance;
            if (_camera == null)
            {
                if (!_reportedNoCamera && t > 2f)
                {
                    _reportedNoCamera = true;
                    _log?.Error(Source, "No MatchCamera in the scene after 2 s: the warm-up and the Tier / Camera gates cannot " +
                        "complete, so the arena shows after the readiness cap. Add MatchCamera to the Main Camera in Game.unity.");
                }
                return;
            }
            if (_fenceDriver == null) _fenceDriver = _camera.GetComponent<FenceFadeDriver>();
            if (_fenceSwap == null) _fenceSwap = _camera.GetComponent<FencePerformanceSwap>();
            if (_hazeSwap == null) _hazeSwap = _camera.GetComponent<SurroundHazeSwap>();
        }

        // Breadcrumb (test-session item 5): which tier the RenderTierPolicy decisions were made for, and why. Logged when the
        // Tier gate first holds and again if the active pipeline asset changes before the round (a reference compare per frame).
        private RenderPipelineAsset _loggedTier;
        private bool _tierLogged;

        private void LogRenderTier()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (_tierLogged && rp == _loggedTier) return;
            _tierLogged = true;
            _loggedTier = rp;
            var names = QualitySettings.names;
            int q = QualitySettings.GetQualityLevel();
            _log?.Info(Source, $"Render tier: rp={(rp != null ? rp.name : "none")} quality={(q >= 0 && q < names.Length ? names[q] : q.ToString())} " +
                $"post={(RenderTierPolicy.PostProcessingFor(rp) ? "on" : "off")} haze={(RenderTierPolicy.PcHazePassFor(rp) ? "pass" : "baked")} " +
                $"(RenderTierPolicy keys on the active pipeline asset: post-processing and the haze pass are {RenderTierPolicy.PcRpAssetName} only).");
        }

        private bool TierGateHolds() =>
            _camera != null && _camera.RenderTierApplied
            && _fenceSwap != null && _fenceSwap.Settled
            && _hazeSwap != null && _hazeSwap.FirstSyncDone;

        /// <summary>The scene's content is all in. The expected counts come from the MapGenerator that spawns the bases and piles.</summary>
        private bool SceneGateHolds(GameManager gm, float t)
        {
            if (gm == null) return false;
            if (_map == null) _map = FindFirstObjectByType<MapGenerator>();
            int expectedBases = 0, expectedPiles = 0;
            if (_map != null)
            {
                expectedBases = _map.ExpectedBaseCount;
                expectedPiles = _map.ExpectedPileCount;
            }
            else if (!_reportedNoMap && t > 2f)
            {
                _reportedNoMap = true;
                _log?.Error(Source, "No MapGenerator in the scene after 2 s: the expected base / pile counts are unknown, " +
                    "so the Scene gate only checks the manager and the chickens.");
            }

            bool localUp = false;
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                if (!c.PresentationReady) return false;
                if (c.HasInputAuthority && !c.IsDecoy) localUp = true;
            }
            if (!localUp) return false;

            int bases = 0;
            var allBases = PlayerBase.ActiveBases;
            for (int i = 0; i < allBases.Count; i++)
                if (allBases[i] != null && allBases[i].Object != null && allBases[i].Object.IsValid) bases++;
            if (bases < expectedBases) return false;

            int piles = 0;
            var allPiles = FoodPile.ActivePiles;
            for (int i = 0; i < allPiles.Count; i++)
                if (allPiles[i] != null && allPiles[i].Object != null && allPiles[i].Object.IsValid) piles++;
            return piles >= expectedPiles;
        }

        // ---- warm-up side effects --------------------------------------------------------------------------

        private void OnStepChanged(WarmupStep from, WarmupStep to)
        {
            switch (to)
            {
                case WarmupStep.OverviewVariants:
                    _pendingClassMask = LiveClassMask();
                    if (_camera == null || !_camera.BeginWarmupFraming(MapGenerator.ArenaHalfSize, SurroundMargin, SurroundHeight))
                    {
                        _log?.Error(Source, "The warm-up could not frame the arena (no MatchCamera / Camera); ending the warm-up.");
                        _warmup.Abort();
                        return;
                    }
                    SetVariantOverrides(true);
                    break;

                case WarmupStep.OverviewPlain:
                    _warmedClassMask |= _pendingClassMask;
                    SetVariantOverrides(false);
                    if (_warmup.LastStepTimedOut) WarnCapped("variants");
                    break;

                case WarmupStep.Restore:
                    if (_warmup.LastStepTimedOut) WarnCapped("plain");
                    SetVariantOverrides(false);
                    if (_camera != null) _camera.EndWarmupFraming();
                    break;

                case WarmupStep.Done:
                    ReleaseOverrides();
                    _warmupDoneAt = Time.realtimeSinceStartup - _startTime;
                    break;
            }
        }

        private void WarnCapped(string step) =>
            _log?.Warn(Source, $"Warm-up step '{step}' hit its {ArenaWarmupSequence.StepCapSeconds:0} s cap with shaders still " +
                "compiling; moving on (the first frames after the reveal may show compile placeholders).");

        /// <summary>Forces the lazily-created variants on for the overview (or releases them). Presentation only: no networked value moves.</summary>
        private void SetVariantOverrides(bool on)
        {
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null) continue;
                var visuals = c.GetComponent<ChickenVisuals>();
                if (visuals != null) visuals.SetWarmupGhost(on);
            }
            if (_fenceDriver != null) _fenceDriver.SetWarmupKeyword(on);

            if (on) _proxies.Create(new Vector3(0f, 0.5f, 0f), _log);
            else _proxies.Destroy();
        }

        /// <summary>Releases every override and, when a framing is up, hands the camera back (snapped).</summary>
        private void ReleaseOverrides()
        {
            // Ghosts released through the live list; a chicken that despawned mid-warm-up took its override with it.
            if (_proxies.IsAlive || (_warmup != null && _warmup.IsRunning) || (_camera != null && _camera.WarmupFramingActive))
                SetVariantOverrides(false);
            if (_camera != null && _camera.WarmupFramingActive) _camera.EndWarmupFraming();
        }

        private static int LiveClassMask()
        {
            int mask = 0;
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
                if (chickens[i] != null) mask |= 1 << (int)chickens[i].Class;
            return mask;
        }

        /// <summary>
        /// A chicken whose class the warm-up has not shown (a joiner arriving while the waiting room is up, a bot that spawned after
        /// the Scene gate) re-runs the overview once, before the round's intro. <see cref="IsReady"/> reads false meanwhile.
        /// </summary>
        private void MaybeRerunForNewClass(GameManager gm)
        {
            if (gm != null && (gm.State == MatchState.Active || gm.State == MatchState.Ended)) return;
            if ((LiveClassMask() & ~_warmedClassMask) == 0) return;
            if (_camera == null || !_warmup.Begin()) return;
            _rerun = true;
            _log?.Info(Source, "A chicken of a class the warm-up has not shown arrived; re-running the overview.");
        }

        private void LogReady(float t)
        {
            if (_summaryLogged) return;
            _summaryLogged = true;
            if (_tracker.TimedOut)
            {
                _log?.Warn(Source, $"Arena readiness timed out after {ArenaReadinessTracker.MaxSeconds:0} s; pending gates: " +
                    $"{_tracker.PendingGates} (last gate done: {(_tracker.LastGate.Length > 0 ? _tracker.LastGate : "none")}). " +
                    "Revealing anyway; the first frames may show compile placeholders or a camera swing.");
                return;
            }
            float warmSeconds = _warmupStartedAt >= 0f && _warmupDoneAt >= 0f ? _warmupDoneAt - _warmupStartedAt : 0f;
            _log?.Info(Source, $"Arena ready in {_tracker.ReadyAtSeconds:0.00} s (last gate: {_tracker.LastGate}; " +
                $"warm-up {warmSeconds:0.00} s, {_warmup.OverviewFrames} overview frames, " +
                $"editor compile wait {_warmup.CompileWaitSeconds:0.00} s).");
        }
    }
}
