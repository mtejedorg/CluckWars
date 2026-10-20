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
    /// NaN / Inf transforms, renderers outside the arena, chickens with no visible renderer — plus the camera set-up and
    /// the arena cover. One Info summary per scan, then one Warn per category with offenders (up to 8 hierarchy paths).
    /// </summary>
    /// <remarks>
    /// Runs when the arena is revealed (<see cref="RevealState.ArenaRevealed"/>), every 10 s while the Game scene is
    /// active, and on demand: F9 (dev keyboard check: diagnostics, not gameplay input), a three-finger tap, or the
    /// overlay's "Scan visuals" button (<see cref="DiagnosticsReadout.VisualScanRequested"/>). Dev tools only. A scan
    /// allocates (it lists the scene's renderers); the per-frame trigger checks do not. Reads only.
    /// </remarks>
    public sealed class VisualProbe : MonoBehaviour
    {
        private const string Source = "Visual";
        private const float PeriodicSeconds = 10f;
        private const string GameSceneName = "Game";

        private ILogService _log;
        private int _scanCount;
        private float _nextPeriodicAt = -1f;
        private bool _revealPending, _threeFingerLatched;

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

        // Deferred to the next Update so the scan runs outside the overlay's own Update.
        private void OnRevealed() => _revealPending = true;

        private void Update()
        {
            if (_revealPending) { _revealPending = false; Scan("reveal"); return; }

            if (DiagnosticsReadout.VisualScanRequested || F9Pressed() || ThreeFingerTap())
            {
                DiagnosticsReadout.VisualScanRequested = false;
                Scan("manual");
                return;
            }

            if (SceneManager.GetActiveScene().name != GameSceneName) { _nextPeriodicAt = -1f; return; }
            float now = Time.realtimeSinceStartup;
            if (_nextPeriodicAt < 0f) _nextPeriodicAt = now + PeriodicSeconds;
            else if (now >= _nextPeriodicAt) Scan("periodic");
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

        private void Scan(string reason)
        {
            _nextPeriodicAt = Time.realtimeSinceStartup + PeriodicSeconds;
            var sw = Stopwatch.StartNew();
            Array.Clear(_counts, 0, _counts.Length);
            for (int i = 0; i < _paths.Length; i++) _paths[i].Clear();

            var main = Camera.main;
            int mask = main != null ? main.cullingMask : 0;
            bool inArena = FindFirstObjectByType<MapGenerator>() != null;
            float half = MapGenerator.ArenaHalfSize;

            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int live = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                live++;
                CheckMaterials(r);
                var t = r.transform;
                if (VisualScanRules.IsNonFinite(t.position) || VisualScanRules.IsNonFinite(t.rotation) || VisualScanRules.IsNonFinite(t.lossyScale))
                    Add(VisualCategory.Nan, t, null);
                else if (inArena && VisualScanRules.IsLayerVisible(mask, r.gameObject.layer) && VisualScanRules.IsOutOfBounds(r.bounds.center, half))
                    Add(VisualCategory.OutOfBounds, t, null);
            }
            CheckChickens();

            sw.Stop();
            _scanCount++;
            int total = 0;
            for (int i = 0; i < _counts.Length; i++) total += _counts[i];
            DiagnosticsReadout.VisualOffenders = total;
            DiagnosticsReadout.LastVisualScanUtc = DateTime.UtcNow.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            _log?.Info(Source, VisualScanRules.Summary(_scanCount, reason, live, (float)sw.Elapsed.TotalMilliseconds, _counts,
                Camera.allCamerasCount, mask, RevealState.CoverShown));
            for (int i = 0; i < _counts.Length; i++)
                if (_counts[i] > 0) _log?.Warn(Source, VisualScanRules.OffenderLine((VisualCategory)i, _counts[i], _paths[i]));
        }

        private void CheckMaterials(Renderer r)
        {
            r.GetSharedMaterials(_materials);
            bool nullMat = _materials.Count == 0, err = false, unsupported = false;
            string detail = null;
            for (int i = 0; i < _materials.Count; i++)
            {
                var m = _materials[i];
                if (m == null) { nullMat = true; continue; }
                var shader = m.shader;
                if (shader == null || VisualScanRules.IsErrorShaderName(shader.name))
                {
                    err = true;
                    detail ??= m.name + " (" + (shader != null ? shader.name : "no shader") + ")";
                }
                else if (!shader.isSupported)
                {
                    unsupported = true;
                    detail ??= m.name + " (" + shader.name + ")";
                }
            }
            if (nullMat) Add(VisualCategory.NullMat, r.transform, null);
            if (err) Add(VisualCategory.ErrShader, r.transform, detail);
            if (unsupported) Add(VisualCategory.Unsupported, r.transform, detail);
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
