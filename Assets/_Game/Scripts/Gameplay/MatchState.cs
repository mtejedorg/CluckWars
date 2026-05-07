namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked match-lifecycle phases. Read by HUDs to gate overlays / inputs.
    /// </summary>
    public enum MatchState
    {
        /// <summary>Default state on Spawned; reserved for a future pre-match lobby.</summary>
        WaitingForPlayers = 0,

        /// <summary>Match timer running, win condition checked each tick.</summary>
        Active = 1,

        /// <summary>Win condition met or timer expired; HUD shows the end overlay.</summary>
        Ended = 2,
    }
}
