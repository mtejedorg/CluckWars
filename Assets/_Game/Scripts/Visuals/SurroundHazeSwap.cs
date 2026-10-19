using System.Text;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Mobile-tier surround haze (Phase 8 C F1 stage 3): while the active render pipeline is not the PC one
    /// (<see cref="RenderTierPolicy.MobileBakedHazeFor"/>) every surround renderer in the map's
    /// <see cref="SurroundHazeSet"/> draws its baked hazed material copies; on PC (which hazes with the full-screen
    /// <see cref="CWHazeFeature"/> pass instead) the authored originals are drawn. Local presentation only.
    /// </summary>
    /// <remarks>
    /// <para><b>Hook.</b> Added by <see cref="MatchCamera"/> next to <see cref="FencePerformanceSwap"/>. It polls the
    /// active pipeline asset once a frame (a reference compare) like <c>MatchCamera.ApplyRenderTier</c>, so a quality
    /// switch or the Editor capture tools' live tier swap are followed in both directions. The Mobile background
    /// colour half of the haze lives in <see cref="MatchCamera"/>.</para>
    /// <para><b>Swap, never mutate.</b> Only <c>Renderer.sharedMaterials</c> references are assigned; the shared
    /// material assets are never written. The renderers are static-batched, which is fine: materials are per renderer.</para>
    /// <para><b>Errors.</b> The set is baked data: a Map scene that is loaded but has no <see cref="SurroundHazeSet"/>,
    /// a null renderer / material, or a copy list that does not match the original's slots can only come from a missing
    /// re-bake (<c>Cluck Wars/Map/Bake Mobile Surround Haze</c>, which must follow <c>GroundDressingBaker</c>). They never
    /// self-heal, so each is reported once through <see cref="ILogService"/> and the bad entry is skipped; the rest of
    /// the set still swaps.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SurroundHazeSwap : MonoBehaviour
    {
        private const string Source = "SurroundHazeSwap";
        private const string MapSceneName = "Map";

        /// <summary>While no set is found, how often to look again (seconds, unscaled).</summary>
        private const float RescanInterval = 1f;

        private ILogService _log;
        private SurroundHazeSet _set;
        private bool _bakedShown;
        private bool _reportedMissing;
        private bool _firstSyncDone;
        private float _nextScanTime;

        /// <summary>True while the hazed copies are the ones drawn.</summary>
        public bool BakedShown => _bakedShown;

        /// <summary>True once the first <see cref="Sync"/> has found the map's set and brought the materials in line with the tier
        /// (or reported the set missing). Read by the arena readiness Tier gate.</summary>
        public bool FirstSyncDone => _firstSyncDone;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            // Plain MonoBehaviour added at runtime: Zenject never injects it, so self-inject (CONVENTIONS
            // "Self-injection"). Never gate on ProjectContext.HasInstance.
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
        }

        private void LateUpdate() => Sync(RenderTierPolicy.MobileBakedHazeFor(GraphicsSettings.currentRenderPipeline));

        private void OnDisable()
        {
            // Leave whatever renders next on the authored materials.
            if (_bakedShown && _set != null) Apply(_set, baked: false);
            _bakedShown = false;
        }

        /// <summary>
        /// Brings the drawn materials in line with <paramref name="wantBaked"/>. Returns true when materials were
        /// swapped. Idempotent: a repeat call with the same answer does nothing.
        /// </summary>
        public bool Sync(bool wantBaked)
        {
            if (_set == null)
            {
                // First look, or the map was unloaded (scene change): whatever loads next starts from the originals.
                _bakedShown = false;
                if (Time.unscaledTime < _nextScanTime) return false;
                _nextScanTime = Time.unscaledTime + RescanInterval;
                _set = FindFirstObjectByType<SurroundHazeSet>();
                if (_set == null)
                {
                    // Not yet loaded is legal (the map streams in after the camera); a loaded Map without the set is a wiring bug.
                    if (!_reportedMissing && SceneManager.GetSceneByName(MapSceneName).isLoaded)
                    {
                        _reportedMissing = true;
                        _log?.Error(Source, $"Scene '{MapSceneName}' is loaded but has no {nameof(SurroundHazeSet)} " +
                                            "(run Cluck Wars/Map/Bake Mobile Surround Haze); the Mobile tier surround will be unhazed.");
                    }
                    if (_reportedMissing) _firstSyncDone = true;   // a wiring bug is logged, not a reason to hold the arena gate
                    return false;
                }
                _reportedMissing = false;
                ReportWiring(_set, _log);
            }

            _firstSyncDone = true;
            if (wantBaked == _bakedShown) return false;
            Apply(_set, wantBaked);
            _bakedShown = wantBaked;
            return true;
        }

        /// <summary>
        /// Assigns the hazed (<paramref name="baked"/>) or original material set to every valid entry of
        /// <paramref name="set"/>; invalid entries (see <see cref="EntryProblem"/>) are skipped. Returns the number of
        /// renderers assigned. Reports nothing; <see cref="ReportWiring"/> does that once per set.
        /// </summary>
        public static int Apply(SurroundHazeSet set, bool baked)
        {
            int n = 0;
            foreach (var e in set.Entries)
            {
                if (EntryProblem(e) != null) continue;
                e.Renderer.sharedMaterials = baked ? e.Hazed : e.Original;
                n++;
            }
            return n;
        }

        /// <summary>Logs one error summarising every invalid entry of <paramref name="set"/> (nothing when it is sound). Returns the bad-entry count.</summary>
        public static int ReportWiring(SurroundHazeSet set, ILogService log)
        {
            int bad = 0;
            var sb = new StringBuilder();
            var entries = set.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                string problem = EntryProblem(entries[i]);
                if (problem == null) continue;
                if (bad++ < 5) sb.Append("\n  #").Append(i).Append(": ").Append(problem);
            }
            if (entries.Count == 0)
            {
                bad = 1;
                sb.Append("\n  the set has no entries");
            }
            if (bad > 0)
                log?.Error(Source, $"{nameof(SurroundHazeSet)} is not wired correctly ({bad} problem(s), first shown); " +
                                   "re-run Cluck Wars/Map/Bake Mobile Surround Haze:" + sb);
            return bad;
        }

        /// <summary>Why <paramref name="e"/> cannot be swapped, or null when it is sound.</summary>
        public static string EntryProblem(SurroundHazeSet.Entry e)
        {
            if (e == null) return "null entry";
            if (e.Renderer == null) return "renderer is null/destroyed (stale after GroundDressingBaker?)";
            if (e.Original == null || e.Hazed == null) return $"{e.Renderer.name}: material array missing";
            int slots = e.Renderer.sharedMaterials.Length;
            if (e.Original.Length != e.Hazed.Length || e.Hazed.Length != slots)
                return $"{e.Renderer.name}: slot mismatch (renderer {slots}, original {e.Original.Length}, hazed {e.Hazed.Length})";
            for (int s = 0; s < e.Hazed.Length; s++)
                if (e.Hazed[s] == null || e.Original[s] == null) return $"{e.Renderer.name}: null material in slot {s}";
            return null;
        }
    }
}
