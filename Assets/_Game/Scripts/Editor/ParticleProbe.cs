using System.Collections.Generic;
using System.Globalization;
using CluckWars.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Capture-time particle probe (batch D, vfx.md 0.2 budget). While a window is open it samples, once per Editor update
    /// in Play mode, the live particle count of every chicken (the sum over its <see cref="ParticleSystem"/>s), the number of
    /// ParticleSystems per chicken, and the live total of the whole scene, and keeps the peaks. Driven from
    /// <c>tools/ui_capture/capture_feedback_lab.py</c> through the bridge (<see cref="Begin"/> / <see cref="End"/>), so the
    /// peaks are measured across a whole ability sequence rather than at the two instants a screenshot catches.
    /// </summary>
    /// <remarks>Editor-only (Editor/ folder): no player build ever sees it. A plain static with an
    /// <see cref="EditorApplication.update"/> hook, so it survives the bridge's per-call script compilation.</remarks>
    public static class ParticleProbe
    {
        private static bool _active;
        private static string _tag = "";
        private static int _samples;
        private static int _perChickenPeak;
        private static int _systemsPerChickenMax;
        private static int _sceneTotalPeak;
        private static string _peakChicken = "";
        private static int _pileKernelPeak;
        private static int _chunkMoversPeak;
        private static int _arenaExclPilesPeak;
        private static string _perChickenBreakdown = "";
        private static readonly List<ParticleSystem> Scratch = new List<ParticleSystem>(16);

        /// <summary>Starts a fresh window named <paramref name="tag"/> (replaces any open one).</summary>
        public static string Begin(string tag)
        {
            _tag = tag;
            _samples = 0;
            _perChickenPeak = 0;
            _systemsPerChickenMax = 0;
            _sceneTotalPeak = 0;
            _peakChicken = "";
            _pileKernelPeak = 0;
            _chunkMoversPeak = 0;
            _arenaExclPilesPeak = 0;
            _perChickenBreakdown = "";
            if (!_active)
            {
                _active = true;
                EditorApplication.update += Sample;
            }
            return "probe begin " + tag;
        }

        /// <summary>Closes the window and returns its peaks as one line.</summary>
        public static string End()
        {
            if (_active)
            {
                _active = false;
                EditorApplication.update -= Sample;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "perChickenPeak={0} peakChicken={1} systemsPerChickenMax={2} sceneTotalPeak={3} samples={4} pileKernelPeak={5} stealMoversPeak={6} sceneExclPileKernelPeak={7} peakChickenSystems=[{8}]",
                _perChickenPeak, _peakChicken, _systemsPerChickenMax, _sceneTotalPeak, _samples, _pileKernelPeak, _chunkMoversPeak, _arenaExclPilesPeak, _perChickenBreakdown);
        }

        /// <summary>The system names and live counts of the chicken currently held in <see cref="Scratch"/>, for the peak line.</summary>
        private static string Breakdown()
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < Scratch.Count; k++)
            {
                if (k > 0) sb.Append(' ');
                sb.Append(Scratch[k].name).Append('=').Append(Scratch[k].particleCount);
            }
            return sb.ToString();
        }

        private static void Sample()
        {
            if (!Application.isPlaying) return;
            _samples++;

            var list = ChickenController.ActiveControllers;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                c.GetComponentsInChildren<ParticleSystem>(true, Scratch);
                int live = 0;
                for (int k = 0; k < Scratch.Count; k++) live += Scratch[k].particleCount;
                if (Scratch.Count > _systemsPerChickenMax) _systemsPerChickenMax = Scratch.Count;
                if (live > _perChickenPeak)
                {
                    _perChickenPeak = live;
                    _peakChicken = c.name;
                    _perChickenBreakdown = Breakdown();
                }
            }

            int total = 0, kernels = 0;
            var all = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                int n = all[i].particleCount;
                total += n;
                if (all[i].name == "VFX_PileKernel") kernels += n;
            }
            if (total > _sceneTotalPeak) _sceneTotalPeak = total;
            if (kernels > _pileKernelPeak) _pileKernelPeak = kernels;
            if (total - kernels > _arenaExclPilesPeak) _arenaExclPilesPeak = total - kernels;
            int movers = CluckWars.Visuals.FoodChunkFlight.LiveCount;
            if (movers > _chunkMoversPeak) _chunkMoversPeak = movers;
        }
    }
}
