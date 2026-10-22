using System;
using System.Collections;
using System.IO;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using CluckWars.UI;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Auto-test driver (test-session item 7, Source <c>AutoTest</c>, dev tools only): reads the <c>cw_*</c> keys
    /// (<see cref="AutoTestArgs"/>) once at boot and drives the same menu and waiting-room code paths a human would —
    /// HOST / JOIN, the class tile, the loadout cards, READY, START / JOIN MATCH, the waiting room's START and the podium's
    /// PLAY AGAIN — through narrow hooks on <see cref="MenuUiController"/> and <see cref="MatchOverlaysController"/>.
    /// With <c>cw_autoplay</c> it switches the local fake input (<see cref="AutoPlayInputProvider"/>) on while a round runs.
    /// </summary>
    /// <remarks>
    /// Never writes game state: every action is a button handler a player could press, every movement is device input.
    /// Steps are spaced like a person's (≈0.6 s) so page transitions finish. A step that cannot happen (no menu, no code
    /// after 60 s, an incomplete loadout) is logged at Error and the driver stops, leaving the game where it is.
    /// </remarks>
    // Before FusionNetworkService.Update (default order 0), so a press decided this frame is read this frame.
    [DefaultExecutionOrder(-500)]
    public sealed partial class AutoTestDriver : MonoBehaviour
    {
        private const string Source = "AutoTest";
        private const float StepSeconds = 0.6f;
        private const float MenuWaitSeconds = 60f;
        private const float CodeWaitSeconds = 60f;
        public const string ResultFileName = "cw_autotest.json";

        private ILogService _log;
        private AutoPlayInputProvider _autoPlay;
        private AutoTestConfig _cfg;

        // In-match state (all local).
        private float _waitingSince = -1f, _endedSince = -1f;
        private bool _startPressed, _loopHandled, _loopDoneLogged, _joinerLoopNoted, _autoPlayOn;
        private int _extraRounds;

        [Serializable]
        private sealed class Result
        {
            public string code, role, utc, sha;
        }

        [Inject]
        public void Construct(ILogService log, [InjectOptional] AutoPlayInputProvider autoPlay)
        {
            _log = log;
            _autoPlay = autoPlay;
        }

        private void Start()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
            _cfg = AutoTestArgs.Read(_log);
            if (!_cfg.AnyKey) { enabled = false; return; }

            foreach (var p in _cfg.Problems) _log.Warn(Source, "AutoTest config: " + p);
            _log.Info(Source, _cfg.Describe());
            if (_cfg.AutoDisconnect > 0)
                _log.Info(Source, $"AutoTest autodisconnect={_cfg.AutoDisconnect}s: this peer freezes {AutoTestRules.AutoDisconnectStallSeconds} s " +
                    "into its first multiplayer round, then presses REJOIN (CW-218).");
            if (_cfg.AutoZone)
                _log.Info(Source, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "AutoTest autozone=1: every round this peer casts its zone ability once with {0:0.0} s left, " +
                    "and PLAY AGAIN waits {1:0.0} s (B10: the next round must start with zn=0).",
                    AutoTestRules.ZoneLeadSeconds, AutoTestRules.PlayAgainDelay(true)));
            _log.Info(Source, $"AutoTest persistentDataPath={Application.persistentDataPath}");
            ApplyScreenshotTools();

            if (_cfg.DevMode && !PlayerPreferences.DeveloperModeEnabled)
            {
                PlayerPreferences.DeveloperModeEnabled = true;
                _log.Info(Source, "AutoTest: Developer Mode switched on (cw_devmode).");
            }
            DiagnosticsReadout.HudModeAtBoot = DebugHudModes.FromLevel(_cfg.Hud);
            if (_cfg.AutoPlay && _autoPlay == null)
                _log.Error(Source, "cw_autoplay is set but no AutoPlayInputProvider is bound (ProjectInstaller); the bird will not move by itself.");

            if (_cfg.WantsMenuFlow) StartCoroutine(MenuFlow());
        }

        // ---- cw_shots / cw_band / cw_zones: comparable HUD screenshots (dev tools only) ---------------------------------

        private string _shotDir;
        private int _shotsTaken;
        private float _shotActiveSince = -1f;
        private bool _shotsDone;

        private void ApplyScreenshotTools()
        {
            bool wanted = _cfg.Shots.Length > 0 || _cfg.BandOverride != 0 || _cfg.Zones || _cfg.FloorArrows || _cfg.EdgeArrows;
            if (!DevTools.Available)
            {
                if (wanted) _log.Warn(Source, "AutoTest cw_shots/cw_band/cw_zones/cw_floorarrows/cw_edgearrows ignored: dev tools not available in this build");
                _shotsDone = true;
                return;
            }

            if (_cfg.BandOverride != 0) CluckWars.Visuals.HudControlZones.DevBandOverride = _cfg.BandOverride;
            _log.Info(Source, $"AutoTest band={CluckWars.Visuals.HudControlZones.TopBandLandscapeZonePx:0}px " +
                $"({(_cfg.BandOverride != 0 ? "cw_band override" : "shipped")}, landscape top-band info zone)");

            DiagnosticsReadout.ZonesOverlay = _cfg.Zones;
            if (_cfg.Zones) _log.Info(Source, "AutoTest zones=1: HUD info / control zones outlined by the overlay.");

            CluckWars.Visuals.FloorRivalArrows.EdgeArrowsRequested = _cfg.EdgeArrows;
            _log.Info(Source, _cfg.EdgeArrows
                ? "AutoTest arrows=edge (-cw_edgearrows): rival arrows are the shipped edge chevrons."
                : "AutoTest arrows=floor (dev default): hidden rivals get an arrow painted on a floor ring round the local " +
                  "chicken; their edge chevrons are suppressed. -cw_edgearrows restores the chevrons.");

            if (_cfg.Shots.Length == 0) { _shotsDone = true; return; }
            _shotDir = AutoTestRules.ShotDirectoryFromArgs(Environment.GetCommandLineArgs())
                       ?? Path.Combine(Application.persistentDataPath, "shots");
            try { Directory.CreateDirectory(_shotDir); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _log.Error(Source, $"AutoTest shots: cannot create {_shotDir}: {e.Message}; no screenshots this run.");
                _shotsDone = true;
                return;
            }
            _log.Info(Source, $"AutoTest shots at t={string.Join(",", _cfg.Shots)}s of the first Active round into {_shotDir} (tag={_cfg.ShotTag ?? "-"})");
        }

        /// <summary>Screenshots at the listed seconds since this peer's FIRST Active round began; done once that round ends.</summary>
        private void TickShots(GameManager gm)
        {
            if (_shotsDone || gm == null) return;
            if (gm.State != MatchState.Active)
            {
                if (_shotActiveSince >= 0f)
                {
                    _shotsDone = true;
                    if (_shotsTaken < _cfg.Shots.Length)
                        _log.Warn(Source, $"AutoTest shots: the round ended after {Time.realtimeSinceStartup - _shotActiveSince:0.0} s; " +
                            $"{_cfg.Shots.Length - _shotsTaken} of {_cfg.Shots.Length} shot(s) not taken.");
                }
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (_shotActiveSince < 0f) _shotActiveSince = now;
            if (!AutoTestRules.ShouldTakeShot(_cfg.Shots, _shotsTaken, now - _shotActiveSince)) return;

            int second = _cfg.Shots[_shotsTaken++];
            var path = Path.Combine(_shotDir, AutoTestRules.ShotFileName(_cfg.Profile, second, _cfg.ShotTag));
            ScreenCapture.CaptureScreenshot(path, 1);
            _log.Info(Source, $"AutoTest shot t={second}s window={Screen.width}x{Screen.height} -> {path}");
        }

        // ---- menu: HOST / JOIN -> class -> loadout -> READY -> START / JOIN MATCH ---------------------------------

        private IEnumerator MenuFlow()
        {
            var role = _cfg.AutoHost ? "host" : _cfg.AutoJoin != null ? "join" : "solo";
            float t0 = Time.realtimeSinceStartup;
            MenuUiController menu = null;
            while (menu == null || !menu.AutoTestIdle)
            {
                if (Time.realtimeSinceStartup - t0 > MenuWaitSeconds)
                {
                    _log.Error(Source, $"AutoTest {role}: no idle main menu after {MenuWaitSeconds:0} s (scene '{SceneManager.GetActiveScene().name}'); stopping.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
                menu = FindFirstObjectByType<MenuUiController>();
            }
            yield return new WaitForSecondsRealtime(1f);   // the first page's slide-in

            var mode = _cfg.AutoHost ? SessionMode.Host : _cfg.AutoJoin != null ? SessionMode.Join : SessionMode.Solo;
            _log.Info(Source, $"AutoTest {role}: {mode} button, class {_cfg.Class}.");
            if (!menu.AutoTestChooseMode(mode)) yield break;
            yield return new WaitForSecondsRealtime(StepSeconds);

            if (!menu.AutoTestPickClass(_cfg.Class))
            {
                _log.Error(Source, $"AutoTest {role}: could not pick {_cfg.Class} (no perk registered?); stopping.");
                yield break;
            }
            yield return new WaitForSecondsRealtime(StepSeconds);

            menu.AutoTestOpenLoadout();
            yield return new WaitForSecondsRealtime(StepSeconds);

            int missing = menu.AutoTestFillLoadout(out var loadout);
            if (missing > 0)
            {
                _log.Error(Source, $"AutoTest {role}: loadout still has {missing} empty slot(s) after picking every card ({loadout}); stopping.");
                yield break;
            }
            _log.Info(Source, $"AutoTest {role}: loadout {loadout}.");
            yield return new WaitForSecondsRealtime(StepSeconds);

            if (!menu.AutoTestPressReady())
            {
                _log.Error(Source, $"AutoTest {role}: READY refused; stopping.");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1f);

            if (_cfg.AutoHost) yield return HostFlow(menu);
            else if (_cfg.AutoJoin != null) yield return JoinFlow(menu, _cfg.AutoJoin);
            else
            {
                // PLAY SOLO: THE COOP's START loads the match; the solo round starts by itself (no waiting room).
                _log.Info(Source, "AutoTest solo: START (loading the match scene vs 3 bots).");
                menu.AutoTestPressStartOrJoin(null);
            }
        }

        private IEnumerator HostFlow(MenuUiController menu)
        {
            float t0 = Time.realtimeSinceStartup;
            string code;
            while (string.IsNullOrEmpty(code = menu != null ? menu.AutoTestJoinCode : null))
            {
                if (menu == null || Time.realtimeSinceStartup - t0 > CodeWaitSeconds)
                {
                    _log.Error(Source, $"AutoTest host: no join code after {CodeWaitSeconds:0} s (UGS CreateLobby failed? see MenuUI errors); stopping.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }

            _log.Info(Source, "AutoTest code=" + code);
            WriteResult(code, "host");
            yield return new WaitForSecondsRealtime(StepSeconds);

            _log.Info(Source, "AutoTest host: START (loading the match scene).");
            menu.AutoTestPressStartOrJoin(null);
        }

        private IEnumerator JoinFlow(MenuUiController menu, string code)
        {
            float first = Time.realtimeSinceStartup;
            for (int attempt = 1; ; attempt++)
            {
                if (menu == null) { GiveUp(code, attempt, "menu-gone"); yield break; }
                int failuresBefore = menu.AutoTestJoinFailures;
                _log.Info(Source, $"AutoTest join attempt {attempt} code={code}.");
                if (!menu.AutoTestPressStartOrJoin(code)) { GiveUp(code, attempt, "join-refused-by-menu"); yield break; }

                // Wait for the outcome: the scene swaps (joined), the menu reports a failure, or nothing happens in time.
                float attemptStarted = Time.realtimeSinceStartup;
                while (true)
                {
                    if (menu == null || SceneManager.GetActiveScene().handle != menu.gameObject.scene.handle)
                    {
                        _log.Info(Source, $"AutoTest join: lobby {code} joined on attempt {attempt}; loading the match scene.");
                        yield break;
                    }
                    if (menu.AutoTestJoinFailures > failuresBefore) break;
                    float now = Time.realtimeSinceStartup;
                    if (now - attemptStarted > AutoTestRules.JoinAttemptTimeoutSeconds)
                    {
                        GiveUp(code, attempt, $"attempt-timeout ({AutoTestRules.JoinAttemptTimeoutSeconds:0}s without a join result or the match scene)");
                        yield break;
                    }
                    if (!AutoTestRules.ShouldRetryJoin(now - first))
                    {
                        GiveUp(code, attempt, $"timeout ({AutoTestRules.JoinGiveUpSeconds:0}s)");
                        yield break;
                    }
                    yield return null;
                }

                if (!AutoTestRules.ShouldRetryJoin(Time.realtimeSinceStartup - first + AutoTestRules.JoinRetrySeconds))
                {
                    GiveUp(code, attempt, $"retries-exhausted ({AutoTestRules.JoinGiveUpSeconds:0}s)");
                    yield break;
                }
                _log.Info(Source, $"AutoTest join attempt {attempt} failed; retrying in {AutoTestRules.JoinRetrySeconds:0} s (the host may not be up yet).");
                yield return new WaitForSecondsRealtime(AutoTestRules.JoinRetrySeconds);
            }
        }

        private void GiveUp(string code, int attempts, string reason) =>
            _log.Error(Source, $"AutoTest join gave up reason={reason} code={code} attempts={attempts}");

        private void WriteResult(string code, string role)
        {
            var path = Path.Combine(Application.persistentDataPath, ResultFileName);
            try
            {
                BuildInfo.TryLoad(out var sha, out _, out _);
                var json = JsonUtility.ToJson(new Result
                {
                    code = code,
                    role = role,
                    utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
                    sha = sha,
                });
                File.WriteAllText(path, json);
                _log.Info(Source, $"AutoTest wrote {path}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _log.Warn(Source, $"AutoTest could not write {path}: {e.Message} (the code is in the log line above).");
            }
        }

        // ---- match: auto-play, auto-start, auto-loop ---------------------------------------------------------------

        private void Update()
        {
            TickAutoRejoin();
            var gm = GameManager.Instance;
            bool valid = gm != null && gm.Object != null && gm.Object.IsValid;

            TickAutoZone(valid ? gm : null);   // before autoplay: the reservation must hold before its first cast
            TickAutoAura(valid ? gm : null);   // same, and its steer must be set before the fake player decides (AutoTestDriver.Aura.cs)
            TickAutoPlay(valid ? gm : null);
            TickShots(valid ? gm : null);
            if (!valid) return;

            var runner = gm.Runner;
            if (runner == null) return;
            TickAutoDisconnect(gm, runner);
            ObserveLoop(gm);
            if (runner.GameMode == GameMode.Single)
            {
                // Solo: no waiting room to start; PLAY AGAIN loops the rounds (goes straight to GET READY).
                if (gm.HasStateAuthority) TickAutoLoop(gm);
                return;
            }
            if (!gm.HasStateAuthority)
            {
                if (_cfg.AutoLoop > 0 && !_joinerLoopNoted)
                {
                    _joinerLoopNoted = true;
                    _log.Info(Source, "AutoTest: joiner in a cw_autoloop session - stays on the results and follows the host's PLAY AGAIN.");
                }
                return;
            }

            TickAutoStart(gm, runner);
            TickAutoLoop(gm);
        }

        // ---- cw_autodisconnect (CW-218): freeze like a backgrounded phone, then REJOIN from the menu -----------------
        private float _activeSince = -1f;
        private float _autoDisconnectAt = -1f;      // realtime when the freeze ended; -1 = not done
        private float _rejoinSheetSeenAt = -1f;
        private float _rejoinPressedAt = -1f;
        private int _rejoinFailuresBefore;
        private float _nextRejoinLookAt;
        // WaitSheet: any disconnect sheet (from the freeze OR a real drop the hook did not cause) is answered.
        private AutoRejoinPhase _rejoinPhase = AutoRejoinPhase.WaitSheet;

        private bool _autoDisconnectSkipped;   // the first multiplayer round ended before the mark: the hook is off

        /// <summary>Fires in this peer's FIRST multiplayer round only; a round shorter than the mark switches the hook off.</summary>
        private void TickAutoDisconnect(GameManager gm, NetworkRunner runner)
        {
            if (_cfg.AutoDisconnect <= 0 || _autoDisconnectAt >= 0f || _autoDisconnectSkipped) return;
            if (_rejoinPhase != AutoRejoinPhase.WaitSheet) return;   // already dropped once (by anything) and rejoined: never freeze the rejoined round
            if (runner.GameMode == GameMode.Single) return;
            if (gm.State != MatchState.Active)
            {
                if (_activeSince >= 0f)
                {
                    _autoDisconnectSkipped = true;
                    _log.Warn(Source, $"AutoTest autodisconnect: the first multiplayer round ended after {Time.realtimeSinceStartup - _activeSince:0.0} s, " +
                        $"before the {_cfg.AutoDisconnect} s mark; the hook is off for this run.");
                }
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (_activeSince < 0f) _activeSince = now;
            if (!AutoTestRules.ShouldAutoDisconnect(_cfg.AutoDisconnect, runner.GameMode != GameMode.Single, now - _activeSince)) return;

            _log.Info(Source, $"AutoTest autodisconnect: freezing the main thread for {AutoTestRules.AutoDisconnectStallSeconds} s " +
                "(an app sent to the background past the Fusion timeout).");
            System.Threading.Thread.Sleep(AutoTestRules.AutoDisconnectStallSeconds * 1000);
            _autoDisconnectAt = Time.realtimeSinceStartup;
            _log.Info(Source, "AutoTest autodisconnect: freeze over; expecting a timeout, then the menu's disconnect sheet.");
        }

        /// <summary>
        /// cw_autodisconnect's menu half: answers the disconnect sheet whatever caused it (the freeze, or a real drop such as
        /// session-20261021-205856's cloud outage, which the old freeze-only arming never answered), presses REJOIN without
        /// waiting for the lobby check, then logs the outcome: back in the match, failed, or no answer in time.
        /// </summary>
        private void TickAutoRejoin()
        {
            if (_cfg.AutoDisconnect <= 0 || _rejoinPhase == AutoRejoinPhase.Done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextRejoinLookAt) return;
            _nextRejoinLookAt = now + 0.5f;
            var menu = FindFirstObjectByType<MenuUiController>();

            if (_rejoinPhase == AutoRejoinPhase.Pressed)
            {
                var gm = GameManager.Instance;
                bool backInMatch = gm != null && gm.Object != null && gm.Object.IsValid;
                bool failed = menu != null && menu.AutoTestRejoinFailures > _rejoinFailuresBefore;
                switch (AutoTestRules.DecideRejoinOutcome(backInMatch, failed, now - _rejoinPressedAt))
                {
                    case AutoRejoinOutcome.Joined:
                        _rejoinPhase = AutoRejoinPhase.Done;
                        _log.Info(Source, $"AutoTest autodisconnect: REJOIN outcome=joined, back in the match (state={gm.State}) " +
                            $"{now - _rejoinPressedAt:0.0} s after the press.");
                        break;
                    case AutoRejoinOutcome.Failed:
                        _rejoinPhase = AutoRejoinPhase.Done;
                        _log.Error(Source, "AutoTest autodisconnect: REJOIN outcome=failed (the sheet shows 'over or full'; the reason is in the " +
                            "MenuUI 'REJOIN ... failed' and UGS lines above).");
                        break;
                    case AutoRejoinOutcome.TimedOut:
                        _rejoinPhase = AutoRejoinPhase.Done;
                        _log.Error(Source, $"AutoTest autodisconnect: REJOIN outcome=timeout, neither back in the match nor failed " +
                            $"{AutoTestRules.AutoRejoinOutcomeWaitSeconds:0} s after the press (scene '{SceneManager.GetActiveScene().name}').");
                        break;
                }
                return;
            }

            // WaitSheet
            if (menu == null || !menu.AutoTestDisconnectSheetOpen)
            {
                if (_autoDisconnectAt >= 0f && now - _autoDisconnectAt > AutoTestRules.AutoRejoinSheetWaitSeconds)
                {
                    _rejoinPhase = AutoRejoinPhase.Done;
                    _log.Error(Source, $"AutoTest autodisconnect: no disconnect sheet on the menu {AutoTestRules.AutoRejoinSheetWaitSeconds:0} s after the freeze " +
                        $"(scene '{SceneManager.GetActiveScene().name}'; did the peer time out? see the Fusion / MatchOverlays lines).");
                }
                _rejoinSheetSeenAt = -1f;
                return;
            }
            if (_rejoinSheetSeenAt < 0f)
            {
                _rejoinSheetSeenAt = now;
                _log.Info(Source, "AutoTest autodisconnect: disconnect sheet up on the menu " +
                    (_autoDisconnectAt >= 0f ? "(after this hook's freeze)." : "(a drop this hook did NOT cause - answering it anyway)."));
                return;
            }
            if (!AutoTestRules.ShouldPressRejoin(now - _rejoinSheetSeenAt)) return;   // a person reads the sheet first

            _rejoinFailuresBefore = menu.AutoTestRejoinFailures;
            if (menu.AutoTestPressRejoin())
            {
                _rejoinPhase = AutoRejoinPhase.Pressed;
                _rejoinPressedAt = now;
                _log.Info(Source, "AutoTest autodisconnect: REJOIN pressed (not waiting for the lobby check).");
            }
            else
            {
                _rejoinPhase = AutoRejoinPhase.Done;
                _log.Error(Source, "AutoTest autodisconnect: REJOIN outcome=not-offered (the sheet has no REJOIN: no remembered code, the lobby is gone, " +
                    "or a join is already running).");
            }
        }

        // ---- loop observation (host and joiner) --------------------------------------------------------------------
        private MatchState _observedState = MatchState.WaitingForPlayers;
        private bool _observedAny;
        private int _loopRestarts;
        private float _playAgainPressedAt = -1f;
        private const float PlayAgainVerifySeconds = 10f;

        /// <summary>
        /// Every peer: a round that leaves Ended (the host's PLAY AGAIN) is logged as <c>AutoTest loop round=k/N</c>, and the
        /// host checks its own press actually restarted the round (Error if the state is still Ended 10 s later).
        /// </summary>
        private void ObserveLoop(GameManager gm)
        {
            var st = gm.State;
            if (_observedAny && _observedState == MatchState.Ended && st != MatchState.Ended)
            {
                _loopRestarts++;
                _playAgainPressedAt = -1f;
                if (_cfg.AutoLoop > 0)
                    _log.Info(Source, $"AutoTest loop round={_loopRestarts}/{_cfg.AutoLoop} match={_loopRestarts + 1} restarted (state={st}, " +
                        $"{(gm.HasStateAuthority ? "host" : "joiner")}).");
            }
            else if (_cfg.AutoLoop > 0 && !gm.HasStateAuthority && st == MatchState.Ended && _observedState != MatchState.Ended
                     && _loopRestarts >= _cfg.AutoLoop && !_loopDoneLogged)
            {
                _loopDoneLogged = true;
                _log.Info(Source, $"AutoTest loop done ({_loopRestarts}/{_cfg.AutoLoop} extra rounds); staying on the results screen.");
            }
            _observedState = st;
            _observedAny = true;

            if (_playAgainPressedAt >= 0f && st == MatchState.Ended && Time.realtimeSinceStartup - _playAgainPressedAt > PlayAgainVerifySeconds)
            {
                _log.Error(Source, $"AutoTest loop round={_extraRounds}/{_cfg.AutoLoop}: PLAY AGAIN did not restart the round " +
                    $"(state still Ended after {PlayAgainVerifySeconds:0} s).");
                _playAgainPressedAt = -1f;
            }
        }

        // ---- cw_autozone (B10): a zone alive at PLAY AGAIN, so the next round-start check sees whether the reset cleared it --
        private enum AutoZonePhase { Idle, Armed, Pressed, Unavailable }
        private AutoZonePhase _zonePhase = AutoZonePhase.Idle;
        private int _zoneSlot = -1;
        private string _zoneAbility;
        private float _zonePressedLeft;
        private readonly System.Collections.Generic.List<CluckWars.Abilities.AbilityBaseSO> _zoneSlots =
            new System.Collections.Generic.List<CluckWars.Abilities.AbilityBaseSO>(AbilityController.SlotCount);

        /// <summary>
        /// Idle -> Armed when a round becomes playable (the zone slot is reserved from the fake player's rotation, so it is
        /// off cooldown at the mark) -> Pressed at <see cref="AutoTestRules.ZoneLeadSeconds"/> left -> one report when the
        /// round ends (cast or not, and how many zones / decoys this peer still sees) -> Idle. A loadout with nothing to
        /// place says so once (Warn) and the hook stays off for the run.
        /// </summary>
        private void TickAutoZone(GameManager gm)
        {
            if (!_cfg.AutoZone || _autoPlay == null || _zonePhase == AutoZonePhase.Unavailable) return;
            if (gm == null)
            {
                // Left the match (host change, disconnect): the next manager starts a fresh round from Idle.
                _zonePhase = AutoZonePhase.Idle;
                _autoPlay.ReservedSlot = -1;
                return;
            }
            var abilities = LocalChicken()?.Abilities;

            if (gm.State == MatchState.Ended)
            {
                if (_zonePhase != AutoZonePhase.Idle) ReportAutoZoneRound(abilities);
                return;
            }
            if (!gm.IsMatchRunning || abilities == null) return;

            if (_zonePhase == AutoZonePhase.Idle)
            {
                _zoneSlots.Clear();
                for (int s = 0; s < AbilityController.SlotCount; s++) _zoneSlots.Add(abilities.GetSlot(s));
                _zoneSlot = AutoTestRules.PickZoneSlot(_zoneSlots);
                if (_zoneSlot < 0)
                {
                    _zonePhase = AutoZonePhase.Unavailable;
                    _log.Warn(Source, "AutoTest autozone: nothing in this loadout outlives a round (no zone ability, no Doppelganger); " +
                        "nothing is placed this run. Use cw_class Fatty (Root Egg) or Speedy (Feather Trap).");
                    return;
                }
                _zoneAbility = _zoneSlots[_zoneSlot].name;
                _autoPlay.ReservedSlot = _zoneSlot;
                _zonePhase = AutoZonePhase.Armed;
                _log.Info(Source, $"AutoTest autozone: slot {_zoneSlot} ({_zoneAbility}) reserved for one cast near the round end.");
                return;
            }

            if (AutoTestRules.ShouldCastZone(gm.IsMatchRunning, _zonePhase == AutoZonePhase.Pressed, gm.TimeRemaining))
            {
                _zonePressedLeft = gm.TimeRemaining;
                _autoPlay.Press(_zoneSlot, AutoPlayInputProvider.HoldSeconds);
                _zonePhase = AutoZonePhase.Pressed;
                _log.Info(Source, System.FormattableString.Invariant(
                    $"AutoTest autozone: casting {_zoneAbility} (slot {_zoneSlot}) with {_zonePressedLeft:0.00} s left."));
            }
        }

        private void ReportAutoZoneRound(AbilityController abilities)
        {
            int zones = 0, decoys = 0;
            var live = AbilityZone.ActiveZones;
            for (int i = 0; i < live.Count; i++) if (live[i] != null && live[i].Object != null && live[i].Object.IsValid) zones++;
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].Object != null && all[i].Object.IsValid && all[i].IsDecoy) decoys++;

            if (_zonePhase == AutoZonePhase.Armed)
                _log.Warn(Source, System.FormattableString.Invariant($"AutoTest autozone: the round ended before the {AutoTestRules.ZoneLeadSeconds:0.0} s mark ") +
                    $"(food target reached?); {_zoneAbility} was not cast this round.");
            else
            {
                bool cast = abilities != null && (abilities.CooldownRemaining(_zoneSlot) > 0f || abilities.IsSlotActive(_zoneSlot));
                if (cast)
                    _log.Info(Source, $"AutoTest autozone: {_zoneAbility} placed; at the round end this peer sees zones={zones} decoys={decoys}.");
                else
                    _log.Warn(Source, $"AutoTest autozone: {_zoneAbility} was pressed but did not cast (stunned / rooted at the mark?); " +
                        $"this round proves nothing about the reset. zones={zones} decoys={decoys}.");
            }
            _autoPlay.ReservedSlot = -1;
            _zoneSlot = -1;
            _zonePhase = AutoZonePhase.Idle;
        }

        private void TickAutoPlay(GameManager gm)
        {
            if (_autoPlay == null || !_cfg.AutoPlay) return;
            bool run = gm != null && gm.IsMatchRunning;
            var self = run ? LocalChicken() : null;
            if (run != _autoPlayOn)
            {
                _autoPlayOn = run;
                if (run)
                {
                    // One personality per client, from its PlayerId, so the clients of a session play differently.
                    int playerId = self != null ? self.Object.InputAuthority.PlayerId : 0;
                    _autoPlay.Configure(playerId);
                    _lastAutoPlayState = _autoPlay.State;
                    _lastAutoPlayGoal = _autoPlay.Goal;
                    _nextAutoPlayBeatAt = 0f;
                    _lastBanked = -1f;
                }
                _autoPlay.SetActive(run);
                _log.Info(Source, run
                    ? $"AutoTest autoplay on (round running), personality={_autoPlay.Personality.Name}."
                    : "AutoTest autoplay off.");
            }
            if (!run) return;

            _autoPlay.Tick(self, Time.unscaledTime);
            // Every state or goal change, and a heartbeat every 5 s: the next run's log explains itself.
            float now = Time.realtimeSinceStartup;
            if (_autoPlay.State != _lastAutoPlayState || _autoPlay.Goal != _lastAutoPlayGoal)
            {
                _log.Info(AutoPlaySource, AutoPlayLog.Line(
                    $"AutoPlay {_lastAutoPlayState}/{_lastAutoPlayGoal}->{_autoPlay.State}/{_autoPlay.Goal}", Snapshot()));
                _lastAutoPlayState = _autoPlay.State;
                _lastAutoPlayGoal = _autoPlay.Goal;
            }
            if (now >= _nextAutoPlayBeatAt)
            {
                _nextAutoPlayBeatAt = now + AutoPlayBeatSeconds;
                _log.Info(AutoPlaySource, AutoPlayLog.Line("AutoPlay hb", Snapshot()));
            }
            float banked = _autoPlay.OwnBaseFood;
            if (banked >= 0f && Mathf.FloorToInt(banked) != Mathf.FloorToInt(_lastBanked))
            {
                if (_lastBanked >= 0f)
                    _log.Info(AutoPlaySource, System.FormattableString.Invariant(
                        $"AutoPlay banked={banked:0} ({(banked - _lastBanked >= 0f ? "+" : "")}{banked - _lastBanked:0}) cargo={_autoPlay.Cargo:0} state={_autoPlay.State}"));
                _lastBanked = banked;
            }
        }

        private AutoPlayLog.Snapshot Snapshot() => new AutoPlayLog.Snapshot
        {
            State = _autoPlay.State, Goal = _autoPlay.Goal, GoalDistance = _autoPlay.GoalDistance, Cargo = _autoPlay.Cargo,
            StuckTicks = _autoPlay.StuckTicks, Unsticks = _autoPlay.Unsticks, PeckPresses = _autoPlay.PeckPresses,
            Position = _autoPlay.Position, CanForage = _autoPlay.CanForage, Personality = _autoPlay.Personality.Name,
        };

        private const float AutoPlayBeatSeconds = 5f;
        private float _nextAutoPlayBeatAt;
        private AutoPlayGoal _lastAutoPlayGoal;
        private const string AutoPlaySource = "AutoPlay";
        private AutoPlayState _lastAutoPlayState;
        private float _lastBanked = -1f;

        private void TickAutoStart(GameManager gm, NetworkRunner runner)
        {
            if (gm.State != MatchState.WaitingForPlayers)
            {
                _waitingSince = -1f;
                _startPressed = false;
                return;
            }
            bool looped = _extraRounds > 0 && _cfg.AutoLoop > 0;
            if (_startPressed || (!looped && _cfg.AutoStart <= 0)) return;

            float now = Time.realtimeSinceStartup;
            if (_waitingSince < 0f) _waitingSince = now;
            int humans = CountHumans(runner);
            float waited = now - _waitingSince;
            string reason;
            if (looped)
            {
                // Round 2+: the previous round's humans, or 15 s - never cw_autostart's full timeout again.
                if (!AutoTestRules.ShouldRestartLoop(humans, _humansAtRoundEnd, waited, out reason)) return;
            }
            else if (!AutoTestRules.ShouldStart(_cfg.AutoStart, humans, waited, _cfg.AutoStartTimeout, out reason)) return;

            var overlays = FindFirstObjectByType<MatchOverlaysController>();
            if (overlays == null) return;   // the overlay binds on its first Update; try again next frame
            _startPressed = true;
            if (looped) _log.Info(Source, System.FormattableString.Invariant($"AutoTest loop restart humans={humans} wait={waited:0.0}s ({reason}); pressing START."));
            else _log.Info(Source, $"AutoTest start: {reason}; pressing the waiting room's START.");
            overlays.AutoTestPressStart();
        }

        private int _humansAtRoundEnd;

        private static int CountHumans(NetworkRunner runner)
        {
            int n = 0;
            foreach (var p in runner.ActivePlayers) if (p.IsRealPlayer) n++;
            return n;
        }

        private void TickAutoLoop(GameManager gm)
        {
            if (gm.State != MatchState.Ended)
            {
                _endedSince = -1f;
                _loopHandled = false;
                return;
            }
            if (_loopHandled) return;
            float now = Time.realtimeSinceStartup;
            if (_endedSince < 0f) _endedSince = now;
            if (now - _endedSince < AutoTestRules.PlayAgainDelay(_cfg.AutoZone) || !gm.CanRequestPlayAgain) return;

            _loopHandled = true;
            if (AutoTestRules.ShouldPlayAgain(_extraRounds, _cfg.AutoLoop))
            {
                var overlays = FindFirstObjectByType<MatchOverlaysController>();
                if (overlays == null) { _loopHandled = false; return; }
                _extraRounds++;
                var loopRunner = gm.Runner;
                _humansAtRoundEnd = loopRunner != null ? CountHumans(loopRunner) : 1;
                _log.Info(Source, $"AutoTest loop round={_extraRounds}/{_cfg.AutoLoop} match={_extraRounds + 1}: PLAY AGAIN pressed (humans={_humansAtRoundEnd}).");
                overlays.AutoTestPressPlayAgain();
                _playAgainPressedAt = Time.realtimeSinceStartup;
            }
            else if (_cfg.AutoLoop > 0 && !_loopDoneLogged)
            {
                _loopDoneLogged = true;
                _log.Info(Source, $"AutoTest loop done ({_extraRounds}/{_cfg.AutoLoop} extra rounds); staying on the results screen.");
            }
        }

        private static ChickenController LocalChicken()
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && c.Object != null && c.Object.IsValid && c.HasInputAuthority && !c.IsBot && !c.IsDecoy) return c;
            }
            return null;
        }
    }
}
