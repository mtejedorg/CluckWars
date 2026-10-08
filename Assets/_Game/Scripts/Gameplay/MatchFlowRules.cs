using CluckWars.Services;
using Fusion;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>Which page the menu opens on after a scene load that carries a flow intent.</summary>
    public enum MenuLanding
    {
        MainMenu,
        /// <summary>THE COOP, with the last class / perk / loadout restored.</summary>
        Lobby,
    }

    /// <summary>What a scene load with a flow intent should do: whether the one-shot flag was set (and
    /// so must be cleared), where to land, and the mode to open THE COOP in.</summary>
    public readonly struct PostMatchIntent
    {
        public readonly bool ClearFlag;
        public readonly MenuLanding Landing;
        public readonly SessionMode Mode;

        public PostMatchIntent(bool clearFlag, MenuLanding landing, SessionMode mode)
        {
            ClearFlag = clearFlag; Landing = landing; Mode = mode;
        }
    }

    /// <summary>The scene-level facts that tell whether a previous round left the world dirty.</summary>
    public readonly struct WorldDirt
    {
        public readonly float MaxBaseFood;
        public readonly bool AnyPileBelowMax;
        public readonly bool EventPilePresent;

        public WorldDirt(float maxBaseFood, bool anyPileBelowMax, bool eventPilePresent)
        {
            MaxBaseFood = maxBaseFood; AnyPileBelowMax = anyPileBelowMax; EventPilePresent = eventPilePresent;
        }
    }

    /// <summary>The <see cref="GameManager"/> networked fields a round reset writes, as plain values.</summary>
    public readonly struct RoundResetFields
    {
        public readonly MatchState State;
        public readonly TickTimer IntroTimer;
        public readonly TickTimer MatchTimer;
        public readonly PlayerRef WinnerPlayer;
        public readonly int WinnerCorner;
        public readonly float WinnerFoodTotal;
        public readonly MatchEventKind ActiveEvent;

        private RoundResetFields(MatchState state, TickTimer intro, TickTimer match, PlayerRef winner,
            int winnerCorner, float winnerFood, MatchEventKind activeEvent)
        {
            State = state; IntroTimer = intro; MatchTimer = match; WinnerPlayer = winner;
            WinnerCorner = winnerCorner; WinnerFoodTotal = winnerFood; ActiveEvent = activeEvent;
        }

        /// <summary>The waiting room with every timer cleared and no winner (StartMatch re-arms the timers).</summary>
        public static RoundResetFields Fresh => new RoundResetFields(
            MatchFlowRules.PlayAgainState, default, default, PlayerRef.None, -1, 0f, MatchEventKind.None);
    }

    /// <summary>
    /// The post-match flow decisions (menu overhaul decision 5: explicit PLAY AGAIN and BACK TO
    /// LOBBY, never automatic), kept pure so they are testable without a Fusion runner.
    /// </summary>
    public static class MatchFlowRules
    {
        /// <summary>
        /// The state PLAY AGAIN resets into: the in-session waiting room. Never
        /// <see cref="MatchState.Active"/> - the host (or the solo player) must press START.
        /// </summary>
        public const MatchState PlayAgainState = MatchState.WaitingForPlayers;

        /// <summary>
        /// Only the state authority (the host / master client, or the solo player) may re-arm the
        /// waiting room, and only once the round has ended.
        /// </summary>
        public static bool CanPlayAgain(bool hasStateAuthority, MatchState state) =>
            hasStateAuthority && state == MatchState.Ended;

        /// <summary>
        /// Where BACK TO LOBBY lands. A joiner cannot re-host someone else's lobby, so Join (and
        /// any missing / invalid last setup) lands on the main menu; Solo and Host open THE COOP.
        /// </summary>
        public static MenuLanding LandingAfterMatch(SessionMode lastMode, bool hasValidLastSetup) =>
            hasValidLastSetup && lastMode != SessionMode.Join ? MenuLanding.Lobby : MenuLanding.MainMenu;

        /// <summary>
        /// The mode the main-menu PLAY AGAIN button opens THE COOP in: Host if that was the last
        /// mode, otherwise Solo (a Join setup replays as Solo - the class and loadout are still valid).
        /// </summary>
        public static SessionMode MainMenuPlayAgainMode(SessionMode lastMode) =>
            lastMode == SessionMode.Host ? SessionMode.Host : SessionMode.Solo;

        /// <summary>
        /// True when a previous round left state behind: banked food, a drained pile, or a live event
        /// pile. A freshly built world has none of these, so a GameManager that spawns over a dirty
        /// one (host change) must reset it first.
        /// </summary>
        public static bool IsWorldDirty(WorldDirt d) =>
            d.MaxBaseFood > 0f || d.AnyPileBelowMax || d.EventPilePresent;

        /// <summary>
        /// The menu-load decision: when the one-shot <c>OpenLobbyOnMenuLoad</c> flag is set it is
        /// always consumed (cleared), even with no valid last setup; the mode is only read from the
        /// setup when it is valid, otherwise Solo.
        /// </summary>
        public static PostMatchIntent DecidePostMatchIntent(bool openLobbyFlag, bool hasValidLastSetup, SessionMode lastMode)
        {
            if (!openLobbyFlag) return new PostMatchIntent(false, MenuLanding.MainMenu, SessionMode.Solo);
            var mode = hasValidLastSetup ? lastMode : SessionMode.Solo;
            return new PostMatchIntent(true, LandingAfterMatch(mode, hasValidLastSetup), mode);
        }

        /// <summary>
        /// The world-ready half of <see cref="IntroArmGate"/>: every connected real player has a live,
        /// non-decoy chicken under their input authority (a Doppelganger decoy shares its owner's
        /// authority and must not stand in for a joiner whose own chicken has not spawned yet).
        /// </summary>
        public static bool EveryPlayerHasAChicken(
            System.Collections.Generic.IReadOnlyList<PlayerRef> realPlayers,
            System.Collections.Generic.IReadOnlyList<(PlayerRef inputAuthority, bool isDecoy)> chickens)
        {
            for (int p = 0; p < realPlayers.Count; p++)
            {
                bool found = false;
                for (int c = 0; c < chickens.Count && !found; c++)
                    found = !chickens[c].isDecoy && chickens[c].inputAuthority == realPlayers[p];
                if (!found) return false;
            }
            return true;
        }

        /// <summary>
        /// The first connected real player the state authority has no "settled" report from
        /// (<see cref="SettleTracker"/>), or <see cref="PlayerRef.None"/> when everyone is settled.
        /// A player who left is no longer in <paramref name="realPlayers"/> and is not waited for.
        /// </summary>
        public static PlayerRef FirstUnsettled(
            System.Collections.Generic.IReadOnlyList<PlayerRef> realPlayers,
            System.Collections.Generic.ICollection<PlayerRef> settled)
        {
            for (int p = 0; p < realPlayers.Count; p++)
                if (!settled.Contains(realPlayers[p])) return realPlayers[p];
            return PlayerRef.None;
        }

        /// <summary>
        /// True when a peer should tell its player the host left (finding 13): the match manager it was
        /// watching through GET READY or a running round went away, or changed state authority, while this
        /// peer itself is still in the session (a session that ends shows the session-end screen instead).
        /// </summary>
        public static bool HostLeftMidRound(MatchState lastSeenState, bool managerGone, bool authorityChanged,
            bool sessionEnding) =>
            !sessionEnding && (managerGone || authorityChanged)
            && (lastSeenState == MatchState.Starting || lastSeenState == MatchState.Active);
    }

    /// <summary>
    /// One peer's "my world is ready" signal (re-audit round 2, finding 13): its own chicken has spawned and
    /// then <see cref="SettleSeconds"/> of consecutive smooth frames have rendered; a hitch restarts the
    /// settle. Every peer runs one during <see cref="MatchState.Starting"/> and reports it to the state
    /// authority once (<c>GameManager.RPC_ReportSettled</c>), so a slow joiner still sees its "3".
    /// Fed once per rendered frame; latches; pure so it is testable without a runner.
    /// </summary>
    public sealed class SettleTracker
    {
        /// <summary>Consecutive smooth frame time required once the chicken is up.</summary>
        public const float SettleSeconds = 0.4f;

        /// <summary>A frame longer than this is a hitch (under 10 fps) and restarts the settle.</summary>
        public const float MaxSmoothFrameSeconds = 0.1f;

        private float _smooth;
        private bool _settled;

        /// <summary>True on the frame this peer settles and on every frame after that.</summary>
        public bool Observe(bool ownChickenReady, float frameSeconds)
        {
            if (_settled) return true;
            if (!ownChickenReady || frameSeconds < 0f || frameSeconds > MaxSmoothFrameSeconds) _smooth = 0f;
            else _smooth += frameSeconds;
            return _settled = _smooth >= SettleSeconds;
        }
    }

    /// <summary>
    /// When the state authority may arm a round's intro (<see cref="MatchState.Starting"/>, re-audit
    /// item 4 + round-2 findings 11 and 13). The intro is a networked TickTimer: ticks keep advancing
    /// through frame hitches, so a timer armed during one could run out its "3" before the digit was
    /// ever drawn. The gate opens once EVERY connected player has reported settled
    /// (<see cref="SettleTracker"/>) and GET READY has been on screen for <see cref="MinShowSeconds"/>
    /// (PLAY AGAIN used to flash it for 0.42 s). <see cref="MaxWaitSeconds"/> caps the whole wait so a
    /// slow device or a wiring bug can never strand the room on GET READY.
    /// Fed once per rendered frame on the state authority; pure so the rule is testable.
    /// </summary>
    public sealed class IntroArmGate
    {
        /// <summary>GET READY stays up at least this long (time it was actually on screen: a frame's
        /// time counts from the second observed frame on, since the first frame's delta was spent before
        /// GET READY was drawn, e.g. processing the START tap, and each frame counts for at most
        /// <see cref="SettleTracker.MaxSmoothFrameSeconds"/>, so a load hitch does not use it up).</summary>
        public const float MinShowSeconds = 0.7f;

        /// <summary>After this long with someone unsettled, GET READY names who the room waits for.</summary>
        public const float WaitingNoticeSeconds = 3f;

        /// <summary>Total real time after which the gate opens regardless (and says so).</summary>
        public const float MaxWaitSeconds = 8f;

        private float _waited, _shown;
        private bool _opened, _drawn;

        /// <summary>True when the gate opened on <see cref="MaxWaitSeconds"/> rather than on a settled room.</summary>
        public bool TimedOut { get; private set; }

        /// <summary>True while the gate is still closed and has waited <see cref="WaitingNoticeSeconds"/>.</summary>
        public bool ShowsWaitingNotice => !_opened && _waited >= WaitingNoticeSeconds;

        /// <summary>Feeds one rendered frame. True on the frame the intro may arm, and on every
        /// frame after that (the caller consumes it once).</summary>
        public bool Observe(bool everyoneSettled, float frameSeconds)
        {
            if (_opened) return true;
            if (frameSeconds < 0f) frameSeconds = 0f;
            _waited += frameSeconds;
            if (_drawn) _shown += Mathf.Min(frameSeconds, SettleTracker.MaxSmoothFrameSeconds);
            _drawn = true;

            if (everyoneSettled && _shown >= MinShowSeconds) return _opened = true;
            if (_waited >= MaxWaitSeconds) { TimedOut = true; return _opened = true; }
            return false;
        }
    }

    /// <summary>
    /// When the comeback event fires (re-audit round 2, finding 3 / decision 2): once per round, in
    /// the last <c>MatchConfigSO.ComebackEventSecondsLeft</c> playable seconds, never during the intro
    /// (the playable clock is frozen at full length there) and never before. Pure so it is testable.
    /// </summary>
    public static class ComebackEventTiming
    {
        /// <param name="timeRemaining">Playable seconds left (<c>GameManager.TimeRemaining</c>).</param>
        /// <param name="secondsLeftThreshold">The configured window; 0 or less disables the event.</param>
        public static bool ShouldFire(MatchState state, bool introActive, float timeRemaining,
            float secondsLeftThreshold, MatchEventKind activeEvent) =>
            state == MatchState.Active && !introActive && activeEvent == MatchEventKind.None
            && secondsLeftThreshold > 0f && timeRemaining <= secondsLeftThreshold;
    }

    /// <summary>
    /// The match-start / match-end audio cues each peer plays for itself (re-audit item 4, round-2
    /// decision 4): the match music at GO (when the intro this peer watched runs out; the GO stinger
    /// itself belongs to the intro overlay, exactly one sound owns GO), the music for a peer that
    /// arrives after GO, the switch to the intense loop once the comeback event is live, and the stop +
    /// end cue when a round it saw ends. Exactly one of each per round, PLAY AGAIN rounds included.
    /// Observed state only, never RPC'd (CONVENTIONS: presentation is local); pure so it is testable.
    /// </summary>
    public sealed class MatchAudioCueTracker
    {
        public enum Cue
        {
            None,
            /// <summary>The intro this peer saw just ended: start the match music.</summary>
            Go,
            /// <summary>This peer arrived after GO: start the match music.</summary>
            JoinedRunning,
            /// <summary>The comeback event is live in a round this peer has music for: go intense.</summary>
            Intensify,
            /// <summary>A round this peer saw running just ended: stop the music, play the end cue.</summary>
            End,
        }

        private bool _sawIntro, _started, _intensified;

        /// <summary>True between this peer's start cue (Go / JoinedRunning) and the round's end: the
        /// match music it started is still playing.</summary>
        public bool RoundAudible => _started;

        /// <param name="comebackActive">The round's comeback event has fired (<c>GameManager.ActiveEvent</c>).</param>
        public Cue Observe(MatchState state, bool introActive, bool comebackActive = false)
        {
            if (state == MatchState.Active)
            {
                if (introActive) { _sawIntro = true; return Cue.None; }
                if (!_started)
                {
                    _started = true;
                    return _sawIntro ? Cue.Go : Cue.JoinedRunning;
                }
                if (comebackActive && !_intensified) { _intensified = true; return Cue.Intensify; }
                return Cue.None;
            }

            bool endsARoundWeSaw = state == MatchState.Ended && _started;
            _sawIntro = false;
            _started = false;
            _intensified = false;
            return endsARoundWeSaw ? Cue.End : Cue.None;
        }
    }
}
