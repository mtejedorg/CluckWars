using System.Globalization;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.UI;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// <c>cw_markshots</c> (dev tools): plays Death Marks so their feedback can be photographed, one screenshot per state the
    /// first time it appears. As the Assassin (<c>-cw_perk Assassin_Spoiler</c>): <c>mark-draw</c> (mid-sweep),
    /// <c>mark-hunt-offscreen</c>, <c>mark-onscreen</c>, <c>mark-ambush-ready</c>, <c>mark-kill</c>, <c>mark-walled</c> (later marks
    /// walk to the target's base and wait for it to be home), <c>mark-end</c> (clean, after the end hold and fade),
    /// <c>mark-end-word</c> (0.3 s after a Missed or Safe end: the word and the glyph), <c>mark-outside-circle</c> (the marker on the
    /// target's floor arrow), <c>mark-inside-circle</c> (the marker over the head), <c>mark-arming</c> (the
    /// marker counting down to the arm) and <c>mark-live</c> (just armed). The stun is timed to the arm the way the Spoiler bot
    /// times it (<see cref="BotTactics.NextMarkStep"/>): it must still run when the 3 s arm ends. As anyone else with
    /// <c>-cw_botperks Assassin_Spoiler,Assassin_Spoiler,Assassin_Spoiler</c>: <c>victim-marked</c>, <c>victim-far</c>,
    /// <c>victim-close-visible</c>, <c>victim-close-invisible</c>, <c>victim-end</c>, <c>victim-arming</c>, <c>victim-live</c>. Every run: <c>playagain-clean</c> 2 s into round
    /// 2 (<c>-Rounds 2</c>), and a log line with Death Mark's cooldown at each GO.
    /// </summary>
    /// <remarks>
    /// <b>Dev hook, said out loud:</b> for <c>victim-close-invisible</c>, once the bot hunter is inside the victim's inner circle,
    /// the harness casts that bot's own Smoke Roost through <see cref="AbilityController.BotTryActivate"/> (the path the bot AI
    /// itself uses) if it has not gone invisible by itself. Logged as <c>AutoTest markshots dev-hook</c>.
    /// </remarks>
    public sealed partial class AutoTestDriver
    {
        private const int MarkSettleFrames = 6;

        private string _markShotDir;
        private bool _shotShelled;
        private float _shellHookAt = -10f;
        private bool _shotDraw, _shotOff, _shotOn, _shotAmbush, _shotKill, _shotWalled, _shotEnd;
        private bool _shotOutside, _shotInside, _shotEndWord, _endLogged;
        private MarkEndCause _markEndCause;
        private bool _shotVicMarked, _shotVicFar, _shotVicClose, _shotVicInvisible, _shotVicEnd, _shotRound2;
        private bool _shotArming, _shotLive, _shotVicArming, _shotVicLive;
        private float _markLockedAt = -1f, _markEndedAt = -1f;
        private MarkRole _lastMarkRole;
        private long _lastMarkHunter;
        private bool _hookCast;
        private bool _wasRunning;
        private int _markRound;
        private float _roundGoAt = -1f;
        private bool _goLogged;
        private string _lastMarkState;

        private void TickMarkShots(GameManager gm)
        {
            if (!_cfg.MarkShots || !DevTools.Available || _autoPlay == null || gm == null) return;
            var self = LocalChicken();
            var abilities = self != null ? self.Abilities : null;
            if (abilities == null || self.Runner == null) return;
            EnsureMarkShotDir();

            bool running = gm.IsMatchRunning;
            float now = Time.realtimeSinceStartup;
            if (running && !_wasRunning)
            {
                _markRound++;
                _roundGoAt = now;
                _goLogged = false;
            }
            _wasRunning = running;
            if (!running)
            {
                _autoPlay.SteerOverride = null;
                return;
            }

            int markSlot = SlotOf(abilities, AbilityJob.DeathMark);
            if (!_goLogged && markSlot >= 0 && now - _roundGoAt > 0.1f)
            {
                _goLogged = true;
                _log.Info(Source, Inv($"AutoTest markshots round={_markRound} GO+0.1s: Death Mark cooldown={abilities.CooldownRemaining(markSlot):0.00}s"));
            }

            var o = MarkFeedback.Current;
            if (_markRound >= 2 && !_shotRound2 && now - _roundGoAt >= 2f)
            {
                _log.Info(Source, $"AutoTest markshots playagain-clean: presentation clean={o.IsClean}, screen mood={MarkScreenEffect.Current}");
                Capture(ref _shotRound2, "playagain-clean");
            }

            // A mark just ended (any reason). The hunter's end holds the marker with its word and glyph (K1): photograph the word
            // 0.3 s in, and the clean HUD once the hold and the fade are over. The victim's end has no hold: 0.6 s.
            bool live = o.Role != MarkRole.None && (o.AssassinMarkActive || o.ClockAlpha >= 1f);
            if (_lastMarkRole != MarkRole.None && !live && _markEndedAt < 0f)
            {
                _markEndedAt = now;
                _markEndCause = MarkEndCause.None;
                _endLogged = false;
            }
            if (live) _markEndedAt = -1f;
            if (_markEndedAt > 0f && o.EndCause != MarkEndCause.None) _markEndCause = o.EndCause;
            if (_markEndedAt > 0f && _lastMarkRole == MarkRole.Assassin && now - _markEndedAt >= 0.3f)
            {
                if (!_endLogged)
                {
                    _endLogged = true;
                    _log.Info(Source, $"AutoTest markshots end cause={_markEndCause}");
                }
                if (HuntRules.ShowsEndWord(_markEndCause)) Capture(ref _shotEndWord, "mark-end-word");
            }
            float endShotAfter = _lastMarkRole == MarkRole.Assassin
                ? FeedbackTuning.MarkEndHoldSeconds + FeedbackTuning.MarkEndFadeSeconds + 0.1f
                : 0.6f;
            if (_markEndedAt > 0f && now - _markEndedAt >= endShotAfter)
            {
                if (_lastMarkRole == MarkRole.Assassin && !_shotEnd)
                {
                    _log.Info(Source, $"AutoTest markshots mark-end: presentation clean={o.IsClean}");
                    Capture(ref _shotEnd, "mark-end");
                }
                else if (_lastMarkRole == MarkRole.Victim && !_shotVicEnd)
                {
                    _log.Info(Source, $"AutoTest markshots victim-end: role now={o.Role} (ThirdParty = another mark, its skull only), " +
                        $"victim outputs clean={o.ToastAlpha == 0f && o.ClockAlpha == 0f && o.HunterBadgeAlpha == 0f}, screen mood={MarkScreenEffect.Current}");
                    Capture(ref _shotVicEnd, "victim-end");
                }
                _markEndedAt = -1f;
                _lastMarkRole = MarkRole.None;
            }
            if (live) _lastMarkRole = o.Role == MarkRole.ThirdParty ? _lastMarkRole : o.Role;

            if (o.Role == MarkRole.Victim && live) { TickVictimShots(self, o); return; }
            if (markSlot >= 0) TickAssassinShots(self, abilities, markSlot, o, now);
            else _autoPlay.SteerOverride = null;   // a victim no longer marked goes back to playing
        }

        // ---- Assassin --------------------------------------------------------------------------------------------------

        private void TickAssassinShots(ChickenController self, AbilityController abilities, int markSlot, in MarkOutputs o, float now)
        {
            var exec = self.Execute;
            if (exec == null) return;

            if (exec.MarkedTarget == NetworkBehaviourId.None)
            {
                _autoPlay.SteerOverride = null;
                _markLockedAt = -1f;
                if (abilities.IsReady(markSlot)) PressOnce(markSlot);
                return;
            }
            if (!self.Runner.TryFindBehaviour(exec.MarkedTarget, out ChickenController target) || target == null) return;

            // One line per change of what the HUD shows, with the execute facts behind it (evidence for every shot).
            string state = Inv($"mode={o.MarkerMode} kill={o.KillHex} lit={o.AmbushLit} ready={exec.KillReady} canKill={exec.CanKillNow} ") +
                           Inv($"stunned={target.CurrentControlState == ControlState.Stunned} selfCast={ControlRules.CanCast(self.CurrentControlState)} ") +
                           Inv($"walled={PlayerBase.WallBetween(self.transform.position, target.transform.position)} offscreen={MarkHudController.MarkerOffScreenNow} ") +
                           Inv($"anchor={MarkHudController.AnchorNow} blocker={o.Blocker} brackets={HuntBrackets.StateNow}");
            if (state != _lastMarkState)
            {
                _lastMarkState = state;
                _log.Info(Source, Inv($"AutoTest markshots state d={Planar.Distance(self.transform.position, target.transform.position):0.0} ") + state);
            }

            if (o.Role == MarkRole.Assassin && o.DrawAlpha > 0f && !o.DrawLocked && o.DrawStep >= 2 && o.DrawStep < o.DrawSteps - 1)
                Capture(ref _shotDraw, "mark-draw");
            if (!o.DrawLocked) { _autoPlay.SteerOverride = self.transform.position.XZ(); return; }
            if (_markLockedAt < 0f) _markLockedAt = now;

            if (o.MarkerMode == MarkMarkerMode.Shelled && Settled("shelled")) Capture(ref _shotShelled, "mark-shelled");
            // Dev hook (logged): a bot target that owns Turtle Mode raises it once the Assassin is close, through the bot API.
            if (!_shotShelled && _shotKill && target.IsBot && target.HasStateAuthority && target.Abilities != null &&
                Planar.Distance(self.transform.position, target.transform.position) <= 6f &&
                Time.realtimeSinceStartup - _shellHookAt >= 1f)
            {
                for (int s = 0; s < AbilityController.SlotCount; s++)
                {
                    if (!(target.Abilities.GetSlot(s).Is(AbilityJob.Shield))) continue;
                    _shellHookAt = Time.realtimeSinceStartup;
                    bool ok = target.Abilities.BotTryActivate(s);
                    _log.Info(Source, $"AutoTest markshots dev-hook: Turtle Mode on bot target {target.name} via BotTryActivate -> {ok}");
                    break;
                }
            }

            // Where the marker rides (K5r): on the target's floor arrow outside the circle, over its head inside it.
            if (MarkHudController.MarkerShownNow && MarkHudController.AnchorNow == MarkAnchor.OnArrow && Settled("outside")) Capture(ref _shotOutside, "mark-outside-circle");
            if (MarkHudController.MarkerShownNow && MarkHudController.AnchorNow == MarkAnchor.OverHead && Settled("inside")) Capture(ref _shotInside, "mark-inside-circle");

            bool firstPassDone = _shotOn && _shotAmbush && _shotKill;
            if (o.MarkerMode == MarkMarkerMode.Walled && Settled("walled")) Capture(ref _shotWalled, "mark-walled");

            // Later marks: walk to the target's base and wait there for it to come home (the walled state).
            if (firstPassDone && !_shotWalled)
            {
                var home = BaseOf(target.HomeCornerIndex);
                _autoPlay.SteerOverride = home != null ? home.transform.position.XZ() : target.transform.position.XZ();
                return;
            }

            // Hold still briefly first: the marker on the floor arrow of a target hidden from the clear viewport (nothing is pinned to the
            // edge any more), if the target starts there (0.4 s: the mark lasts 4 s, and the Kill still has to be photographed and pressed).
            bool arrowOffscreen = MarkHudController.MarkerShownNow && MarkHudController.AnchorNow == MarkAnchor.OnArrow && HiddenFromView(target);
            if (!_shotOff && now - _markLockedAt < 0.4f)
            {
                _autoPlay.SteerOverride = self.transform.position.XZ();
                if (arrowOffscreen && Settled("off")) Capture(ref _shotOff, "mark-hunt-offscreen");
                return;
            }
            if (arrowOffscreen && Settled("off")) Capture(ref _shotOff, "mark-hunt-offscreen");

            _autoPlay.SteerOverride = target.transform.position.XZ();
            if (MarkHudController.MarkerShownNow && !MarkHudController.MarkerOffScreenNow && o.MarkerMode == MarkMarkerMode.Hunt &&
                !o.AmbushLit && Settled("on"))
                Capture(ref _shotOn, "mark-onscreen");
            if (o.AmbushLit && Settled("ambush")) Capture(ref _shotAmbush, "mark-ambush-ready");
            if (MarkHudController.MarkerShownNow && !o.Armed && o.ArmCountdownSeconds <= 2 && Settled("arming")) Capture(ref _shotArming, "mark-arming");
            if (MarkHudController.MarkerShownNow && o.Armed && o.LiveScale > 1.05f) Capture(ref _shotLive, "mark-live");

            if (o.KillHex)
            {
                if (Settled("kill")) Capture(ref _shotKill, "mark-kill");
                if (_shotKill && abilities.IsReady(markSlot)) PressOnce(markSlot);
                return;
            }

            // Stun from close, but only once the lit-Ambush state has been photographed, and only when it will still be running
            // at the arm (the Spoiler bot's rule): a stun that ends before the arm is a wasted Ambush.
            if (!_shotAmbush && o.AmbushLit) return;
            float armLeft = exec.ArmRemaining, windowLeft = exec.WindowTimer.RemainingTime(self.Runner) ?? 0f;
            var passive = target.Abilities != null ? target.Abilities.Passive : null;
            for (int s = 0; s < AbilityController.SlotCount; s++)
            {
                var a = abilities.GetSlot(s);
                if (!FixedKit.EmitsStun(a) || !abilities.IsReady(s) || !a.IsUsable(self)) continue;
                float secs = passive != null ? passive.ModifyControlDuration(a.EmittedControl.Seconds, ControlKind.Stun, target) : a.EmittedControl.Seconds;
                // Both presses here are held HoldSeconds before they cast, so the hold cancels out; the Kill's ~6 photo frames
                // (~0.1 s) fit inside the bot rule's StunLeadMargin.
                if (BotTactics.NextMarkStep(true, false, armLeft, windowLeft, a.WouldAffect(self, target), secs) == BotMarkStep.Stun)
                {
                    _log.Info(Source, Inv($"AutoTest markshots stun: slot {s} with the arm {armLeft:0.00} s away, {secs:0.00} s on {target.name}"));
                    PressOnce(s);
                }
            }
        }

        // ---- Victim ----------------------------------------------------------------------------------------------------

        private void TickVictimShots(ChickenController self, in MarkOutputs o)
        {
            var hunter = MarkFeedback.Hunter;
            long hunterId = hunter != null ? (long)hunter.GetInstanceID() : 0;
            if (hunterId != _lastMarkHunter) { _lastMarkHunter = hunterId; _hookCast = false; }

            // Stand still while marked: the hunter comes to us.
            _autoPlay.SteerOverride = self.transform.position.XZ();

            if (o.ToastAlpha >= 0.95f) Capture(ref _shotVicMarked, "victim-marked");
            if (o.ClockAlpha >= 1f && !o.Armed && o.ToastAlpha <= 0f && Settled("vic-arming")) Capture(ref _shotVicArming, "victim-arming");
            if (o.ClockAlpha >= 1f && o.Armed && o.LiveScale > 1.05f) Capture(ref _shotVicLive, "victim-live");
            // Far: the darkness at its steady state (dim 1, held >= 0.3 s past its fade-in) and the toast gone, so the frame
            // shows what the victim lives with for the mark's whole life.
            if (MarkHudController.HunterBadgeShownNow && o.Cold <= 0f && o.Dim >= 1f && o.ToastAlpha <= 0f &&
                Settled("far", SteadyFrames)) Capture(ref _shotVicFar, "victim-far");
            if (o.Cold >= 0.99f && o.Red <= 0f && Settled("close")) Capture(ref _shotVicClose, "victim-close-visible");
            if (o.Cold >= 0.99f && o.Red >= 0.99f && Settled("invisible")) Capture(ref _shotVicInvisible, "victim-close-invisible");

            // Dev hook: the bot hunter's own Smoke Roost, cast through the bot API once it is inside the inner circle.
            if (_shotVicClose && !_shotVicInvisible && !_hookCast && Time.realtimeSinceStartup - _hookTriedAt >= 0.5f &&
                o.Cold >= 0.99f && hunter != null && hunter.IsBot &&
                hunter.HasStateAuthority && hunter.VisualOpacity >= RivalIndicator.VisibleOpacityThreshold && hunter.Abilities != null)
            {
                for (int s = 0; s < AbilityController.SlotCount; s++)
                {
                    var a = hunter.Abilities.GetSlot(s);
                    if (a == null || !a.IsStealth) continue;
                    _hookTriedAt = Time.realtimeSinceStartup;
                    _hookCast = hunter.Abilities.BotTryActivate(s);
                    _log.Info(Source, $"AutoTest markshots dev-hook: cast {a.DisplayName} on bot hunter {hunter.name} via BotTryActivate -> {_hookCast}");
                    break;
                }
            }
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------------

        /// <summary>The target's own edge-chevron verdict (<see cref="RivalIndicator.HiddenFromClearViewport"/>): off the frame, or mostly
        /// under a HUD zone.</summary>
        private static bool HiddenFromView(ChickenController target)
        {
            var indicator = target != null ? target.Indicator() : null;
            return indicator != null && indicator.HiddenFromClearViewport;
        }

        private void EnsureMarkShotDir()
        {
            if (_markShotDir != null) return;
            _markShotDir = AutoTestRules.ShotDirectoryFromArgs(System.Environment.GetCommandLineArgs())
                           ?? Path.Combine(Application.persistentDataPath, "shots");
            Directory.CreateDirectory(_markShotDir);
        }

        /// <summary>True once the condition named <paramref name="key"/> has held <see cref="MarkSettleFrames"/> frames running
        /// (the UI has caught up). Each key keeps its own run, so several conditions can settle side by side.</summary>
        private bool Settled(string key) => Settled(key, MarkSettleFrames);

        /// <summary>~0.3 s at 60 fps.</summary>
        private const int SteadyFrames = 20;

        private bool Settled(string key, int frames)
        {
            int frame = Time.frameCount;
            _settle.TryGetValue(key, out var run);
            if (run.last == frame - 1) run = (frame, run.count + 1);
            else if (run.last != frame) run = (frame, 1);
            _settle[key] = run;
            return run.count >= frames;
        }

        private readonly System.Collections.Generic.Dictionary<string, (int last, int count)> _settle =
            new System.Collections.Generic.Dictionary<string, (int last, int count)>();
        private int _capturedFrame = -1;
        private float _hookTriedAt = -10f;

        private static PlayerBase BaseOf(int corner)
        {
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++) if (bases[i] != null && bases[i].CornerIndex == corner) return bases[i];
            return null;
        }

        // Press restarts the hold, so a press every frame would never release.
        private float _markPressAt;

        private void PressOnce(int slot)
        {
            if (Time.realtimeSinceStartup < _markPressAt) return;
            _markPressAt = Time.realtimeSinceStartup + AutoPlayInputProvider.HoldSeconds + 0.3f;
            _autoPlay.Press(slot, AutoPlayInputProvider.HoldSeconds);
        }

        private static int SlotOf(AbilityController abilities, AbilityJob job)
        {
            for (int s = 0; s < AbilityController.SlotCount; s++)
                if (abilities.GetSlot(s).Is(job)) return s;
            return -1;
        }

        private void Capture(ref bool taken, string name)
        {
            // One capture per frame: ScreenCapture keeps only the last request of a frame. The other waits a frame.
            if (taken || _capturedFrame == Time.frameCount) return;
            _capturedFrame = Time.frameCount;
            taken = true;
            int k = Mathf.Max(1, _cfg.ShotSuper);
            var path = Path.Combine(_markShotDir, string.Format(CultureInfo.InvariantCulture, "{0}-{1}x{2}.png", name, Screen.width * k, Screen.height * k));
            ScreenCapture.CaptureScreenshot(path, k);
            _log.Info(Source, $"AutoTest markshot {name} -> {path}");
        }
    }

    internal static class MarkShotVectorExtensions
    {
        public static Vector2 XZ(this Vector3 v) => new Vector2(v.x, v.z);
    }
}
