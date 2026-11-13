using CluckWars.Injection;
using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using CluckWars.Settings;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;


namespace CluckWars.UI
{
    /// <summary>
    /// <see cref="MatchOverlaysController"/>'s the round intro (A21, architecture audit 2026-11-12).
    /// </summary>
    /// <remarks>
    /// The arena cover, the GET READY card and the countdown scrim: everything drawn over the arena
    /// between the seats filling and the first tick of play.
    /// <para>
    /// One component, four files. The scene references this type by name and every serialized field and
    /// bound element reference has to stay on it, so the split is a partial class, never a second
    /// component: the main file keeps the services, the element references, the lifecycle, the binding and
    /// the coordinator (<c>Update</c> decides which overlay is up and calls one Refresh per frame). Each
    /// overlay file owns only its own members.
    /// </para>
    /// </remarks>
    public sealed partial class MatchOverlaysController
    {
        private void RefreshIntro(GameManager gm)
        {
            RefreshIntroCard(gm);
            RefreshArenaCover(gm);
        }

        /// <summary>
        /// The arena cover (<see cref="ArenaCoverRule"/>): opaque ink inside the intro overlay, up from the very first frame, revealed
        /// once per scene load on the round's networked "3" when this peer's arena is ready (<see cref="IArenaReadiness"/>). While it is
        /// up (or fading) the intro overlay is kept shown, so the countdown digits play OVER it when this peer is not ready yet.
        /// </summary>
        private void RefreshArenaCover(GameManager gm)
        {
            if (_arenaCover == null) return;
            if (_arenaReadiness == null && !_reportedNoReadiness)
            {
                _reportedNoReadiness = true;
                _log?.Error(Source, "IArenaReadiness is not bound (GameInstaller): the arena cover reveals on the networked 3 " +
                    "without waiting for this peer's warm-up.");
            }

            var state = gm != null ? gm.State : MatchState.WaitingForPlayers;
            var d = ArenaCoverRule.Decide(_arenaReadiness == null || _arenaReadiness.IsReady, gm != null, state,
                IsShown(_lobbyOverlay), IsShown(_matchEndOverlay), _shutdownReason.HasValue || _leavingToLobby,
                _noManagerTimedOut, _arenaRevealed, _arenaCoveredSeconds);

            if (d.CoverShown) _arenaCoveredSeconds += Time.unscaledDeltaTime;
            if (d.Reveal)
            {
                _arenaRevealed = true;
                _arenaCoverFadeEndsAt = PlayerPreferences.ReducedMotionEnabled
                    ? Time.unscaledTime
                    : Time.unscaledTime + ArenaCoverRule.FadeSeconds;
                _arenaCover.AddToClassList("cw-arena-cover--hidden");
                // Late join: this peer's first reveal lands on a round already past its intro (it never saw the 3-2-1).
                bool lateJoin = gm != null && (state == MatchState.Ended || (state == MatchState.Active && !gm.IsIntroActive));
                if (d.HardCap)
                    _log?.Error(Source, $"The arena cover was still up after {ArenaCoverRule.HardCapSeconds:0} s: revealing anyway " +
                        $"(readiness: ready={_arenaReadiness?.IsReady}, pending={_arenaReadiness?.PendingGates}, state={state}).");
                else
                    _log?.Info(Source, $"Arena revealed (state {state}, ready={(_arenaReadiness == null ? "n/a" : _arenaReadiness.IsReady.ToString())}, " +
                        $"covered {_arenaCoveredSeconds:0.00} s).");
                // Test-session breadcrumb (item 5): cover off, with what the reveal looked like on this peer.
                _log?.Info(Source, FormattableString.Invariant($"Cover off: covered={_arenaCoveredSeconds:0.00}s lateJoin={(lateJoin ? 1 : 0)} ") +
                    FormattableString.Invariant($"reducedMotion={(PlayerPreferences.ReducedMotionEnabled ? 1 : 0)} fade={(PlayerPreferences.ReducedMotionEnabled ? 0f : ArenaCoverRule.FadeSeconds):0.00}s ") +
                    $"hardCap={(d.HardCap ? 1 : 0)} state={state}");
                RevealState.NotifyRevealed(PlayerPreferences.ReducedMotionEnabled ? 0f : ArenaCoverRule.FadeSeconds);
            }

            if (_arenaRevealed)
            {
                // Fade out, then drop the element (and let the intro overlay follow its own rules again).
                bool fading = Time.unscaledTime < _arenaCoverFadeEndsAt;
                UiElements.SetShown(_arenaCover, fading);
                if (fading) UiElements.SetShown(_introOverlay, true);
                return;
            }

            UiElements.SetShown(_arenaCover, d.CoverShown);
            if (d.CoverShown != RevealState.CoverShown)
            {
                // Breadcrumb (item 5): every cover transition before the reveal (the waiting room hides it, Starting shows it).
                RevealState.CoverShown = d.CoverShown;
                _log?.Info(Source, $"Cover {(d.CoverShown ? "on" : "off (waiting room / overlay)")}: state={state} " +
                    FormattableString.Invariant($"ready={(_arenaReadiness == null ? "n/a" : _arenaReadiness.IsReady ? "1" : "0")} covered={_arenaCoveredSeconds:0.00}s"));
            }
            if (d.CoverShown)
            {
                _arenaCover.RemoveFromClassList("cw-arena-cover--hidden");
                UiElements.SetShown(_introOverlay, true);
            }
        }

        private void RefreshIntroCard(GameManager gm)
        {
            // Visibility only — the top-bar dim is decided once in Update() across
            // all three modals, so this method must not touch it or it would clear
            // a dim the match-end / lobby overlay still needs.
            // Before the round's intro is armed — no GameManager yet (the session is still starting)
            // or a round in Starting (the host's world is settling) — the overlay holds GET READY with
            // no digit: the continuation of the menu's GET READY card, never a blank arena.
            if (gm != null) _sawGameManager = true;
            else if (_waitingForManagerSince < 0f) _waitingForManagerSince = Time.unscaledTime;
            float waited = _waitingForManagerSince < 0f ? 0f : Time.unscaledTime - _waitingForManagerSince;
            var hold = IntroOverlayRule.Decide(gm != null, gm != null ? gm.State : MatchState.WaitingForPlayers,
                _sawGameManager, _leavingToLobby, waited);

            _noManagerTimedOut = hold == IntroOverlayRule.PreIntro.TimedOut;
            if (hold == IntroOverlayRule.PreIntro.TimedOut && !_reportedNoManager)
            {
                _reportedNoManager = true;
                _log?.Error(Source, $"No GameManager after {IntroOverlayRule.NoManagerTimeoutSeconds}s in the match scene " +
                    "(session start failed or the master never spawned it): hiding GET READY. The match cannot start; " +
                    "see the MatchBootstrapper / FusionNetworkService errors above.");
            }
            bool getReady = hold == IntroOverlayRule.PreIntro.GetReady && !_shutdownReason.HasValue;
            TrackGetReadyHold(getReady, gm);
            // GET READY is only the pre-intro card (finding 11): gone on the first digit, never under GO!.
            UiElements.SetShown(_introGetReady, getReady);
            RefreshWaitingLine(getReady ? gm : null);
            if (hold != IntroOverlayRule.PreIntro.None)
            {
                _introCues.Reset();
                if (_introNumber != null && _introNumber.text.Length > 0) _introNumber.text = string.Empty;
                SetIntroScrim(MatchPresentationRules.IntroScrimVisible(getReady, introActive: false));
                UiElements.SetShown(_introOverlay, getReady);
                return;
            }
            if (gm == null) { _introCues.Reset(); UiElements.SetShown(_introOverlay, false); return; }

            // The timer is networked and untouched; the cues below are this client's own presentation of it.
            var cue = _introCues.Observe(gm.IsIntroActive, gm.IsIntroActive ? gm.IntroRemaining : 0f, out int number);

            // The scrim dims GET READY and the 3-2-1; the GO! slam below plays on the bare arena (finding 17).
            SetIntroScrim(MatchPresentationRules.IntroScrimVisible(getReady: false, introActive: gm.IsIntroActive));

            if (gm.IsIntroActive)
            {
                if (_introNumber != null && cue == IntroCueTracker.Cue.Tick) _introNumber.text = number.ToString();
                UiElements.SetShown(_introOverlay, true);
                _goExpiresAtUnscaledTime = Time.unscaledTime + IntroCueTracker.GoHoldSeconds;
                if (cue == IntroCueTracker.Cue.Tick) { _audio.CountdownTick(number); _introJuice?.Slam(_introNumber); }
                return;
            }

            if (cue == IntroCueTracker.Cue.Go)
            {
                if (_introNumber != null) _introNumber.text = UiText.Get(UiKeys.CountdownGo);
                _audio.CountdownGo();
                _introJuice?.Slam(_introNumber);
            }

            if (Time.unscaledTime < _goExpiresAtUnscaledTime)
            {
                if (_introNumber != null && cue != IntroCueTracker.Cue.Go && _introNumber.text != UiText.Get(UiKeys.CountdownGo))
                    _introNumber.text = UiText.Get(UiKeys.CountdownGo);
                UiElements.SetShown(_introOverlay, true);
            }
            else
            {
                UiElements.SetShown(_introOverlay, false);
            }
        }

        // Breadcrumb (test-session item 5): how long GET READY stayed up each time, read-only.
        private float _getReadySince = -1f;

        private void TrackGetReadyHold(bool shown, GameManager gm)
        {
            if (shown && _getReadySince < 0f) _getReadySince = Time.unscaledTime;
            else if (!shown && _getReadySince >= 0f)
            {
                _log?.Info(Source, FormattableString.Invariant($"GET READY held {Time.unscaledTime - _getReadySince:0.00}s ") +
                    $"(now {(gm != null ? gm.State.ToString() : "no manager")}, {RevealState.Describe()}).");
                _getReadySince = -1f;
            }
        }

        private void SetIntroScrim(bool on)
        {
            _introScrim = on;
            _introOverlay?.EnableInClassList("cw-overlay--introdim", on);
        }

    }
}
