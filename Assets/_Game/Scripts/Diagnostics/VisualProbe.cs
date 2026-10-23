using System;
using System.Collections.Generic;
using System.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Visual-integrity probe (test-session item 4, Source <c>Visual</c>): scans every live renderer for the things that
    /// show as pink / cyan / missing on a device — error or Built-in-RP shaders, unsupported shaders, null materials,
    /// NaN / Inf transforms, gameplay objects outside the arena, chickens with no visible renderer — plus the camera set-up and
    /// the arena cover. One Info summary per scan, then one Warn per category with offenders (up to 8 hierarchy paths).
    /// </summary>
    /// <remarks>
    /// <para>When: after <see cref="VisualScanRules.RevealScanDelaySeconds"/> of uncovered play following the reveal
    /// (never inside the cover or its fade-out, so the probe cannot add a hitch to the reveal it is checking), every
    /// 10 s while the Game scene is active, and on demand: F9 (dev keyboard check: diagnostics, not gameplay input), a
    /// three-finger tap, or the overlay's "Scan visuals" button (<see cref="DiagnosticsReadout.VisualScanRequested"/>).
    /// The automatic scans only run when <see cref="HeavyProbes.Enabled"/>; on-demand scans always run.</para>
    /// <para>Cost: the renderer list is taken once per scan, then checked <see cref="RenderersPerFrame"/> at a time across
    /// frames; each shader's verdict is cached by instance id, so <c>shader.name</c> (which allocates) and
    /// <c>isSupported</c> are read once per shader per session, names of offenders only for their log line. Every frame
    /// that did scan work is published in <see cref="DiagnosticsReadout.ProbeWorkFrame"/>, and PerfProbe tags a hitch on
    /// such a frame <c>probe=1</c> (capped at Info). Dev tools only; reads only.</para>
    /// </remarks>
    public sealed class VisualProbe : MonoBehaviour
    {
        private const string Source = "Visual";
        private const float PeriodicSeconds = 10f;
        private const string GameSceneName = "Game";
        /// <summary>Renderers checked per frame while a scan is in progress.</summary>
        public const int RenderersPerFrame = 150;

        private ILogService _log;
        private int _scanCount;
        private float _nextPeriodicAt = -1f;
        private bool _threeFingerLatched;

        // Reveal scan: armed by the reveal, runs after enough uncovered play.
        private bool _revealArmed;
        private float _revealPlaySeconds;

        // The scan in progress (_scanRenderers == null: none).
        private Renderer[] _scanRenderers;
        private int _scanIndex, _scanLive, _scanFrames, _scanMask;
        private string _scanReason;
        private bool _scanInArena;
        private float _scanHalf;
        private readonly Stopwatch _scanWatch = new Stopwatch();

        private readonly Dictionary<int, ShaderVerdict> _verdicts = new Dictionary<int, ShaderVerdict>(64);
        private readonly List<Material> _materials = new List<Material>(8);
        private readonly List<Renderer> _chickenRenderers = new List<Renderer>(32);
        private readonly int[] _counts = new int[VisualScanRules.CategoryCount];
        private readonly List<string>[] _paths = new List<string>[VisualScanRules.CategoryCount];

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            for (int i = 0; i < _paths.Length; i++) _paths[i] = new List<string>(VisualScanRules.MaxPathsPerCategory);
        }

        private void Start()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
            RevealState.ArenaRevealed += OnRevealed;
        }

        private void OnDestroy() => RevealState.ArenaRevealed -= OnRevealed;

        private void OnRevealed()
        {
            _revealArmed = HeavyProbes.Enabled;
            _revealPlaySeconds = 0f;
        }

        private void Update()
        {
            if (_scanRenderers != null) { ContinueScan(); return; }

            if (DiagnosticsReadout.VisualScanRequested || F9Pressed() || ThreeFingerTap())
            {
                DiagnosticsReadout.VisualScanRequested = false;
                BeginScan("manual");
                return;
            }

            if (!HeavyProbes.Enabled || !InGameScene()) { _nextPeriodicAt = -1f; _revealArmed = false; return; }
            float now = Time.realtimeSinceStartup;

            if (_revealArmed)
            {
                // Only uncovered play counts: never the cover, never its fade-out (the reveal's own frames).
                if (!RevealState.CoverShown && !RevealState.InRevealFade(now)) _revealPlaySeconds += Time.unscaledDeltaTime;
                if (VisualScanRules.RevealScanDue(_revealPlaySeconds))
                {
                    _revealArmed = false;
                    BeginScan("reveal");
                }
                return;   // the periodic scan waits for the reveal scan
            }

            if (_nextPeriodicAt < 0f) _nextPeriodicAt = now + PeriodicSeconds;
            else if (now >= _nextPeriodicAt) BeginScan("periodic");
        }

        // Scene.name allocates a string on every call: compare the handle per frame, the name only when it changes.
        private int _sceneHandle = -1;
        private bool _inGameScene;

        private bool InGameScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != _sceneHandle)
            {
                _sceneHandle = scene.handle;
                _inGameScene = scene.name == GameSceneName;
            }
            return _inGameScene;
        }

        private static bool F9Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.f9Key.wasPressedThisFrame;
        }

        /// <summary>True once when a third finger lands; re-arms when fewer than three are down.</summary>
        private bool ThreeFingerTap()
        {
            var ts = Touchscreen.current;
            if (ts == null) return false;
            int down = 0;
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++) if (touches[i].isInProgress) down++;
            if (down < 3) { _threeFingerLatched = false; return false; }
            if (_threeFingerLatched) return false;
            _threeFingerLatched = true;
            return true;
        }

        private void BeginScan(string reason)
        {
            DiagnosticsReadout.ProbeWorkFrame = Time.frameCount;
            _scanWatch.Restart();
            Array.Clear(_counts, 0, _counts.Length);
            for (int i = 0; i < _paths.Length; i++) _paths[i].Clear();

            var main = Camera.main;
            _scanMask = main != null ? main.cullingMask : 0;
            _scanInArena = FindFirstObjectByType<MapGenerator>() != null;
            _scanHalf = MapGenerator.ArenaHalfSize;
            _scanReason = reason;
            _scanIndex = 0;
            _scanLive = 0;
            _scanFrames = 1;
            _scanRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _scanWatch.Stop();
        }

        private void ContinueScan()
        {
            DiagnosticsReadout.ProbeWorkFrame = Time.frameCount;
            _scanWatch.Start();
            _scanFrames++;
            int end = Mathf.Min(_scanRenderers.Length, _scanIndex + RenderersPerFrame);
            for (; _scanIndex < end; _scanIndex++)
            {
                var r = _scanRenderers[_scanIndex];
                // Destroyed since the list was taken (Unity null), disabled or inactive: not live.
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                _scanLive++;
                CheckMaterials(r);
                var t = r.transform;
                if (VisualScanRules.IsNonFinite(t.position) || VisualScanRules.IsNonFinite(t.rotation) || VisualScanRules.IsNonFinite(t.lossyScale))
                    Add(VisualCategory.Nan, t, null);
                else if (_scanInArena && VisualScanRules.IsLayerVisible(_scanMask, r.gameObject.layer)
                         && VisualScanRules.IsOutOfBounds(r.bounds.center, _scanHalf)
                         // Cheap test first; only an outside renderer pays for the gameplay / root checks (root name allocates).
                         && VisualScanRules.CountsForBounds(r.GetComponentInParent<Fusion.NetworkObject>() != null, t.root.name))
                    Add(VisualCategory.OutOfBounds, t, null);
            }
            if (_scanIndex < _scanRenderers.Length) { _scanWatch.Stop(); return; }

            CheckChickens();
            _scanWatch.Stop();
            _scanRenderers = null;
            _nextPeriodicAt = Time.realtimeSinceStartup + PeriodicSeconds;
            _scanCount++;
            int total = 0;
            for (int i = 0; i < _counts.Length; i++) total += _counts[i];
            DiagnosticsReadout.VisualOffenders = total;
            DiagnosticsReadout.LastVisualScanUtc = DateTime.UtcNow.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            _log?.Info(Source, VisualScanRules.Summary(_scanCount, _scanReason, _scanLive, (float)_scanWatch.Elapsed.TotalMilliseconds,
                _counts, Camera.allCamerasCount, _scanMask, RevealState.CoverShown, _scanFrames));
            for (int i = 0; i < _counts.Length; i++)
                if (_counts[i] > 0) _log?.Warn(Source, VisualScanRules.OffenderLine((VisualCategory)i, _counts[i], _paths[i]));
        }

        private void CheckMaterials(Renderer r)
        {
            r.GetSharedMaterials(_materials);
            bool nullMat = _materials.Count == 0, err = false, unsupported = false;
            Material bad = null;
            for (int i = 0; i < _materials.Count; i++)
            {
                var m = _materials[i];
                if (m == null) { nullMat = true; continue; }
                switch (VerdictOf(m.shader))
                {
                    case ShaderVerdict.Error:       err = true; bad ??= m; break;
                    case ShaderVerdict.Unsupported: unsupported = true; bad ??= m; break;
                }
            }
            if (!nullMat && !err && !unsupported) return;
            // Offenders only: names (which allocate) are read for the log line, never for a healthy renderer.
            string detail = bad != null ? bad.name + " (" + (bad.shader != null ? bad.shader.name : "no shader") + ")" : null;
            if (nullMat) Add(VisualCategory.NullMat, r.transform, null);
            if (err) Add(VisualCategory.ErrShader, r.transform, detail);
            if (unsupported) Add(VisualCategory.Unsupported, r.transform, detail);
        }

        /// <summary>Cached per shader instance: its name and support check are read once per shader per session.</summary>
        private ShaderVerdict VerdictOf(Shader shader)
        {
            if (shader == null) return ShaderVerdict.Error;
            int id = shader.GetInstanceID();
            if (_verdicts.TryGetValue(id, out var v)) return v;
            v = VisualScanRules.Classify(shader.name, shader.isSupported);
            _verdicts[id] = v;
            return v;
        }

        /// <summary>
        /// A chicken that is in the session but draws nothing. Invisibility / Smoke Roost / stun fade the body (a ghost
        /// material) rather than disabling it, so zero active renderers is not a legal state; the chicken's own state is
        /// printed with the path rather than used to skip it, so an odd case can be judged from the log.
        /// </summary>
        private void CheckChickens()
        {
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                c.GetComponentsInChildren(false, _chickenRenderers);
                int active = 0;
                for (int k = 0; k < _chickenRenderers.Count; k++)
                    if (_chickenRenderers[k] != null && _chickenRenderers[k].enabled) active++;
                if (active > 0) continue;
                Add(VisualCategory.ChickensNoRenderer, c.transform,
                    "opacity=" + c.VisualOpacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " cs=" + c.CurrentControlState +
                    " removed=" + (c.Combat != null && c.Combat.IsRemoved ? 1 : 0) +
                    " decoy=" + (c.IsDecoy ? 1 : 0) +
                    " bot=" + (c.IsBot ? 1 : 0) +
                    " ready=" + (c.PresentationReady ? 1 : 0));
            }
        }

        private void Add(VisualCategory category, Transform t, string detail)
        {
            int i = (int)category;
            _counts[i]++;
            if (_paths[i].Count >= VisualScanRules.MaxPathsPerCategory) return;
            var path = VisualScanRules.PathOf(t);
            _paths[i].Add(detail == null ? path : path + " [" + detail + "]");
        }
    }
}
