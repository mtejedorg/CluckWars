using System.Globalization;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// <c>cw_markshots</c> (dev tools): plays the execute so its feedback can be photographed. The Assassin presses the mark, walks
    /// at the target, stuns it from close and holds the Kill; one screenshot per phase the first time it appears
    /// (<c>mark-hunt</c>, <c>mark-near</c>, <c>mark-kill</c>). Any other bird just waits to be marked (<c>mark-victim</c>): run it with
    /// <c>-cw_botperks Assassin_Spoiler,Assassin_Spoiler,Assassin_Spoiler</c>.
    /// </summary>
    public sealed partial class AutoTestDriver
    {
        private string _markShotDir;
        private bool _shotHunt, _shotNear, _shotKill, _shotVictim;
        private float _markWalkFrom = -1f;
        private int _markFrames;

        private void TickMarkShots(GameManager gm)
        {
            if (!_cfg.MarkShots || !DevTools.Available || _autoPlay == null || gm == null || !gm.IsMatchRunning) return;
            var self = LocalChicken();
            var abilities = self != null ? self.Abilities : null;
            if (abilities == null || self.Runner == null) return;

            if (_markShotDir == null)
            {
                _markShotDir = AutoTestRules.ShotDirectoryFromArgs(System.Environment.GetCommandLineArgs())
                               ?? Path.Combine(Application.persistentDataPath, "shots");
                Directory.CreateDirectory(_markShotDir);
            }

            var phase = MarkFeedback.Phase;
            if (phase == MarkPhase.Victim) { MarkShot(ref _shotVictim, "mark-victim"); return; }

            var exec = self.Execute;
            int markSlot = SlotOf<MarkKillAbilitySO>(abilities);
            if (exec == null || markSlot < 0) return;

            if (exec.MarkedTarget == NetworkBehaviourId.None)
            {
                _autoPlay.SteerOverride = null;
                if (abilities.IsReady(markSlot)) _autoPlay.Press(markSlot, AutoPlayInputProvider.HoldSeconds);
                _markWalkFrom = Time.realtimeSinceStartup;
                return;
            }
            if (!self.Runner.TryFindBehaviour(exec.MarkedTarget, out ChickenController target) || target == null) return;

            // The hunt shot waits a moment, so the chevron, the arrow and the banner have all settled; the kill is never pressed
            // until its shot is taken, then the loop goes on to the next mark.
            float d = PlanarDistance(self.transform.position, target.transform.position);
            _autoPlay.SteerOverride = new Vector2(target.transform.position.x, target.transform.position.z);
            if (Time.realtimeSinceStartup - _markWalkFrom > 1f && d > exec.KillRange + 4f) MarkShot(ref _shotHunt, "mark-hunt");
            if (d <= exec.KillRange && phase == MarkPhase.Stun) MarkShot(ref _shotNear, "mark-near");
            if (phase == MarkPhase.Kill) { MarkShot(ref _shotKill, "mark-kill"); return; }

            for (int s = 0; s < AbilityController.SlotCount; s++)
            {
                var a = abilities.GetSlot(s);
                if (FixedKit.EmitsStun(a) && abilities.IsReady(s) && a.IsUsable(self))
                    _autoPlay.Press(s, AutoPlayInputProvider.HoldSeconds);
            }
        }

        private static int SlotOf<T>(AbilityController abilities) where T : AbilityBaseSO
        {
            for (int s = 0; s < AbilityController.SlotCount; s++)
                if (abilities.GetSlot(s) is T) return s;
            return -1;
        }

        private void MarkShot(ref bool taken, string name)
        {
            if (taken) return;
            if (++_markFrames < 20) return;   // a few frames into the phase: the first one has no marker yet
            _markFrames = 0;
            taken = true;
            var path = Path.Combine(_markShotDir, string.Format(CultureInfo.InvariantCulture, "{0}-{1}x{2}.png", name, Screen.width, Screen.height));
            ScreenCapture.CaptureScreenshot(path, 1);
            _log.Info(Source, $"AutoTest markshot {name} -> {path}");
        }
    }
}
