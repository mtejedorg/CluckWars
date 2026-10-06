using CluckWars.Services;

namespace CluckWars.Gameplay
{
    /// <summary>Which page the menu opens on after a scene load that carries a flow intent.</summary>
    public enum MenuLanding
    {
        MainMenu,
        /// <summary>THE COOP, with the last class / perk / loadout restored.</summary>
        Lobby,
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
    }
}
