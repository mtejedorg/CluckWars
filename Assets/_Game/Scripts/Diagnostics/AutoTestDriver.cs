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
    public sealed class AutoTestDriver : MonoBehaviour
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
            _log.Info(Source, $"AutoTest persistentDataPath={Application.persistentDataPath}");

            if (_cfg.DevMode && !PlayerPreferences.DeveloperModeEnabled)
            {
                PlayerPreferences.DeveloperModeEnabled = true;
                _log.Info(Source, "AutoTest: Developer Mode switched on (cw_devmode).");
            }
            if (_cfg.Hud) DiagnosticsReadout.HudRequestedAtBoot = true;
            if (_cfg.AutoPlay && _autoPlay == null)
                _log.Error(Source, "cw_autoplay is set but no AutoPlayInputProvider is bound (ProjectInstaller); the bird will not move by itself.");

            if (_cfg.WantsMenuFlow) StartCoroutine(MenuFlow());
        }

        // ---- menu: HOST / JOIN -> class -> loadout -> READY -> START / JOIN MATCH ---------------------------------

        private IEnumerator MenuFlow()
        {
            var role = _cfg.AutoHost ? "host" : "join";
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

            var mode = _cfg.AutoHost ? SessionMode.Host : SessionMode.Join;
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
            else yield return JoinFlow(menu, _cfg.AutoJoin);
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
                if (menu == null) yield break;
                int failuresBefore = menu.AutoTestJoinFailures;
                _log.Info(Source, $"AutoTest join attempt {attempt} code={code}.");
                if (!menu.AutoTestPressStartOrJoin(code)) yield break;

                while (true)
                {
                    if (menu == null || SceneManager.GetActiveScene().handle != menu.gameObject.scene.handle)
                    {
                        _log.Info(Source, $"AutoTest join: lobby {code} joined on attempt {attempt}; loading the match scene.");
                        yield break;
                    }
                    if (menu.AutoTestJoinFailures > failuresBefore) break;
                    yield return null;
                }

                if (!AutoTestRules.ShouldRetryJoin(Time.realtimeSinceStartup - first))
                {
                    _log.Error(Source, $"AutoTest join: gave up after {attempt} attempts in {AutoTestRules.JoinGiveUpSeconds:0} s (code {code}).");
                    yield break;
                }
                _log.Info(Source, $"AutoTest join attempt {attempt} failed; retrying in {AutoTestRules.JoinRetrySeconds:0} s (the host may not be up yet).");
                yield return new WaitForSecondsRealtime(AutoTestRules.JoinRetrySeconds);
            }
        }

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
            var gm = GameManager.Instance;
            bool valid = gm != null && gm.Object != null && gm.Object.IsValid;

            TickAutoPlay(valid ? gm : null);
            if (!valid) return;

            var runner = gm.Runner;
            if (runner == null || runner.GameMode == GameMode.Single) return;
            if (!gm.HasStateAuthority)
            {
                if (_cfg.AutoLoop > 0 && !_joinerLoopNoted)
                {
                    _joinerLoopNoted = true;
                    _log.Info(Source, "AutoTest: cw_autoloop only acts on the host (PLAY AGAIN is host-only); this peer follows.");
                }
                return;
            }

            TickAutoStart(gm, runner);
            TickAutoLoop(gm);
        }

        private void TickAutoPlay(GameManager gm)
        {
            if (_autoPlay == null || !_cfg.AutoPlay) return;
            bool run = gm != null && gm.IsMatchRunning;
            if (run != _autoPlayOn)
            {
                _autoPlayOn = run;
                _autoPlay.SetActive(run);
                _log.Info(Source, run ? "AutoTest autoplay on (round running)." : "AutoTest autoplay off.");
            }
            if (run) _autoPlay.Tick(LocalChicken(), Time.unscaledTime);
        }

        private void TickAutoStart(GameManager gm, NetworkRunner runner)
        {
            if (gm.State != MatchState.WaitingForPlayers)
            {
                _waitingSince = -1f;
                _startPressed = false;
                return;
            }
            if (_cfg.AutoStart <= 0 || _startPressed) return;

            float now = Time.realtimeSinceStartup;
            if (_waitingSince < 0f) _waitingSince = now;
            int humans = 0;
            foreach (var p in runner.ActivePlayers) if (p.IsRealPlayer) humans++;
            if (!AutoTestRules.ShouldStart(_cfg.AutoStart, humans, now - _waitingSince, _cfg.AutoStartTimeout, out var reason)) return;

            var overlays = FindFirstObjectByType<MatchOverlaysController>();
            if (overlays == null) return;   // the overlay binds on its first Update; try again next frame
            _startPressed = true;
            _log.Info(Source, $"AutoTest start: {reason}; pressing the waiting room's START.");
            overlays.AutoTestPressStart();
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
            if (now - _endedSince < AutoTestRules.PlayAgainDelaySeconds || !gm.CanRequestPlayAgain) return;

            _loopHandled = true;
            if (AutoTestRules.ShouldPlayAgain(_extraRounds, _cfg.AutoLoop))
            {
                var overlays = FindFirstObjectByType<MatchOverlaysController>();
                if (overlays == null) { _loopHandled = false; return; }
                _extraRounds++;
                _log.Info(Source, $"AutoTest round {_extraRounds}/{_cfg.AutoLoop}: PLAY AGAIN.");
                overlays.AutoTestPressPlayAgain();
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
