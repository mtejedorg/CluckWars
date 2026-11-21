using System.Globalization;
using System.Linq;
using System.Text;
using CluckWars.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Capture-time probe for the Peck chip (D11): samples once per Editor update in Play mode the local bird Peck-slot active edge, the
    /// nearest pile networked Amount and the live pile-kernel particles, and reports the first edge of each plus the peak, so the lab can
    /// prove the beat condition (Amount drops inside the Peck window) fires for the shipped Peck.asset. Editor-only; a plain static with an
    /// <see cref="EditorApplication.update"/> hook, so it survives the bridge per-call script compilation.
    /// </summary>
    public static class PeckEdgeProbe
    {
        private static bool _active;
        private static double _t0;
        private static bool _lastActive;
        private static float _lastAmount = float.NaN;
        private static double _activeOn = -1, _activeOff = -1, _dropAt = -1;
        private static float _dropFrom, _dropTo;
        private static int _kernelPeak, _samples;
        private static double _kernelFirst = -1;

        public static string Begin()
        {
            _t0 = EditorApplication.timeSinceStartup;
            _lastActive = false; _lastAmount = float.NaN;
            _activeOn = _activeOff = _dropAt = _kernelFirst = -1; _kernelPeak = 0; _samples = 0;
            if (!_active) { _active = true; EditorApplication.update += Sample; }
            return "peck probe begin";
        }

        public static string End()
        {
            if (_active) { _active = false; EditorApplication.update -= Sample; }
            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "slotActiveOn={0:0.000}s slotActiveOff={1:0.000}s amountDropAt={2:0.000}s ({3:0.0}->{4:0.0}) firstKernel={5:0.000}s kernelPeak={6} samples={7}",
                _activeOn, _activeOff, _dropAt, _dropFrom, _dropTo, _kernelFirst, _kernelPeak, _samples));
            if (_activeOn >= 0 && _dropAt >= 0)
                sb.Append(_dropAt >= _activeOn && (_activeOff < 0 || _dropAt <= _activeOff) ? " DROP-INSIDE-WINDOW" : " DROP-OUTSIDE-WINDOW");
            return sb.ToString();
        }

        private static void Sample()
        {
            if (!Application.isPlaying) return;
            var local = ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority);
            if (local == null) return;
            _samples++;
            double t = EditorApplication.timeSinceStartup - _t0;
            bool active = FoodPileVisualsWindow(local);
            if (active && !_lastActive && _activeOn < 0) _activeOn = t;
            if (!active && _lastActive && _activeOff < 0) _activeOff = t;
            _lastActive = active;

            var pile = FoodPile.ActivePiles.Where(p => p != null && p.Object != null && p.Object.IsValid)
                .OrderBy(p => p.DistanceToSurface(local.transform.position)).FirstOrDefault();
            if (pile != null)
            {
                if (!float.IsNaN(_lastAmount) && pile.Amount < _lastAmount - 0.01f && _dropAt < 0) { _dropAt = t; _dropFrom = _lastAmount; _dropTo = pile.Amount; }
                _lastAmount = pile.Amount;
                var ps = pile.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(p => p.name == "VFX_PileKernel");
                if (ps != null)
                {
                    int n = ps.particleCount;
                    if (n > 0 && _kernelFirst < 0) _kernelFirst = t;
                    if (n > _kernelPeak) _kernelPeak = n;
                }
            }
        }

        private static bool FoodPileVisualsWindow(ChickenController c) => CluckWars.Visuals.FoodPileVisuals.PeckWindowOpen(c);
    }
}
