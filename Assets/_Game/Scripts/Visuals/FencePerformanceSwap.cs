using System.Collections.Generic;
using CluckWars.Logging;
using CluckWars.Settings;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Performance Mode fence (Phase 8 S5): while <see cref="PlayerPreferences.PerformanceModeEnabled"/> is on,
    /// every fence panel (outer <c>Wall_*</c> and arm <c>InnerWall_*</c>) is drawn with its ~3 000-tri stand-in
    /// (<see cref="FencePerformanceMeshesSO"/>) instead of the full 4 000-tri mesh. Local presentation only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why twin renderers, not <c>sharedMesh = perf</c>.</b> The fence is <c>BatchingStatic</c> in
    /// <c>Map.unity</c>: Unity has merged every panel into combined meshes, and reassigning a batched renderer's
    /// mesh does not change what is drawn. So the first time Performance Mode is on, each panel gets one twin
    /// (same parent, same local transform, same materials and shadow settings, perf mesh), the twins are
    /// static-batched together (<see cref="StaticBatchingUtility.Combine(GameObject[], GameObject)"/>), and from then
    /// on the mode just flips <c>enabled</c> on the originals vs the twins (a disabled member of a static batch is
    /// simply not drawn). Default mode never builds anything: no twins, no extra memory.
    /// </para>
    /// <para>
    /// The twins share the fade material, so <see cref="FenceFadeDriver"/> sees them as ordinary fence renderers
    /// (and keeps working from the originals' cached bounds: the perf mesh has the same bounds within 1 cm).
    /// Added by <see cref="MatchCamera"/> next to the driver; the mode is polled once a frame (one cached bool read),
    /// so the in-match settings sheet and the menus need no extra wiring.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FencePerformanceSwap : MonoBehaviour
    {
        private const string Source = "FencePerformanceSwap";

        // A fence panel is "<root>/wall_segment" under MapProps. It is found by NAME, not by mesh: the panels are
        // static-batched, so at runtime their MeshFilter reports a "Combined Mesh", not wall_segment(_inner).
        // Phase8ChunkS5Tests ties these names to the shipped meshes (arm wall roots <-> wall_segment_inner).
        private const string PanelMeshName = "wall_segment";
        private const string OuterPanelPrefix = "Wall_";
        private const string InnerPanelPrefix = "InnerWall_";

        /// <summary>While no fence is found, how often to look again (seconds, unscaled).</summary>
        private const float RescanInterval = 1f;

        private sealed class Pair
        {
            public MeshRenderer Original;
            public MeshRenderer Twin;
        }

        private ILogService _log;
        private FencePerformanceMeshesSO _meshes;
        private readonly List<Pair> _pairs = new List<Pair>();
        private bool _performanceShown;
        private bool _gaveUp;
        private bool _settled;
        private float _nextScanTime;

        /// <summary>True while the twins (perf meshes) are the ones drawn.</summary>
        public bool PerformanceShown => _performanceShown;

        /// <summary>True once the drawn meshes match the preference (the first <see cref="Sync"/> that had nothing left to do, or
        /// the twins were built and shown, or the swap gave up with an Error). Read by the arena readiness Tier gate.</summary>
        public bool Settled => _settled;

        /// <summary>The perf twins built so far (empty until Performance Mode was first on). Tests / captures.</summary>
        public IEnumerable<MeshRenderer> Twins
        {
            get { foreach (var p in _pairs) if (p.Twin != null) yield return p.Twin; }
        }

        [Inject]
        public void Construct(ILogService log, FencePerformanceMeshesSO meshes)
        {
            _log = log;
            _meshes = meshes;
        }

        private void Awake()
        {
            // Plain MonoBehaviour added at runtime: Zenject never injects it, so self-inject (CONVENTIONS
            // "Self-injection"). Never gate on ProjectContext.HasInstance.
            if (_meshes == null) ProjectContext.Instance.Container.Inject(this);
        }

        private void Update() => Sync(batch: true);

        /// <summary>Brings the drawn meshes in line with the current preference. Returns true when something changed.
        /// <paramref name="batch"/> false skips the runtime static batching (EditMode tests).</summary>
        public bool Sync(bool batch)
        {
            bool want = PlayerPreferences.PerformanceModeEnabled;
            if (want == _performanceShown || _gaveUp) { _settled = true; return false; }

            if (_pairs.Count > 0 && _pairs[0].Original == null)
            {
                // The map was unloaded (scene change): the twins went with it. Start over for whatever loads next.
                _pairs.Clear();
                _performanceShown = false;
                if (!want) return false;
            }

            if (!want)
            {
                Show(false);
                _settled = true;
                return true;
            }

            if (_pairs.Count == 0)
            {
                if (Time.unscaledTime < _nextScanTime) return false;
                _nextScanTime = Time.unscaledTime + RescanInterval;
                if (!BuildTwins(batch)) return false;
            }
            Show(true);
            _settled = true;
            return true;
        }

        private void OnDisable()
        {
            // Leave whatever renders next on the shipped fence.
            if (_performanceShown) Show(false);
        }

        private void Show(bool performance)
        {
            for (int i = 0; i < _pairs.Count; i++)
            {
                var p = _pairs[i];
                if (p.Original != null) p.Original.enabled = !performance;
                if (p.Twin != null) p.Twin.enabled = performance;
            }
            _performanceShown = performance;
        }

        /// <summary>True for the mesh child of an outer (<c>Wall_*</c>) or arm wall (<c>InnerWall_*</c>) panel.</summary>
        public static bool TryClassify(Renderer r, out bool inner)
        {
            inner = false;
            var root = r.transform.parent;
            if (root == null || r.name != PanelMeshName) return false;
            inner = root.name.StartsWith(InnerPanelPrefix);
            return inner || root.name.StartsWith(OuterPanelPrefix);
        }

        /// <summary>Finds every fence panel and builds its twin. False when there is none yet (retry later) or the
        /// swap cannot work at all (logged once, then <see cref="_gaveUp"/>: the shipped fence stays).</summary>
        private bool BuildTwins(bool batch)
        {
            if (_meshes == null || _meshes.OuterPerformance == null || _meshes.InnerPerformance == null)
            {
                _log?.Error(Source, "FencePerformanceMeshesSO is not assigned on ProjectInstaller (or has no meshes); " +
                    "Performance Mode keeps the full fence.");
                _gaveUp = true;
                return false;
            }

            var twinObjects = new List<GameObject>();
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                var original = renderers[i];
                if (!original.enabled || !TryClassify(original, out bool inner)) continue;
                var mf = original.GetComponent<MeshFilter>();
                var perfMesh = _meshes.For(inner);
                if (mf == null) continue;

                var go = new GameObject(mf.name) { layer = mf.gameObject.layer };
                var t = go.transform;
                t.SetParent(mf.transform.parent, false);
                t.SetLocalPositionAndRotation(mf.transform.localPosition, mf.transform.localRotation);
                t.localScale = mf.transform.localScale;

                go.AddComponent<MeshFilter>().sharedMesh = perfMesh;
                var twin = go.AddComponent<MeshRenderer>();
                twin.sharedMaterials = original.sharedMaterials;
                twin.shadowCastingMode = original.shadowCastingMode;
                twin.receiveShadows = original.receiveShadows;
                twin.lightProbeUsage = original.lightProbeUsage;
                twin.reflectionProbeUsage = original.reflectionProbeUsage;
                twin.motionVectorGenerationMode = original.motionVectorGenerationMode;
                twin.renderingLayerMask = original.renderingLayerMask;

                _pairs.Add(new Pair { Original = original, Twin = twin });
                twinObjects.Add(go);
            }

            if (_pairs.Count == 0) return false; // no fence loaded yet / a scene without one (the Ability Lab)

            if (batch)
            {
                if (_meshes.OuterPerformance.isReadable && _meshes.InnerPerformance.isReadable)
                {
                    var root = new GameObject("FencePerformanceBatch");
                    root.transform.SetParent(twinObjects[0].transform.root, false);
                    StaticBatchingUtility.Combine(twinObjects.ToArray(), root);
                }
                else
                {
                    _log?.Warn(Source, "Performance fence meshes are not Read/Write enabled; drawing them unbatched.");
                }
            }
            return true;
        }
    }
}
