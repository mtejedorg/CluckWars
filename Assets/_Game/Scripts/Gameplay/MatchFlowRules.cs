using CluckWars.Services;
using Fusion;

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
    }
}
