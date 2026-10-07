namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked match-lifecycle phases. Read by HUDs to gate overlays / inputs.
    /// </summary>
    public enum MatchState
    {
        /// <summary>The in-session waiting room (lobby overlay); the host presses START.</summary>
        WaitingForPlayers = 0,

        /// <summary>Match timer running, win condition checked each tick.</summary>
        Active = 1,

        /// <summary>Win condition met or timer expired; HUD shows the end overlay.</summary>
        Ended = 2,

        /// <summary>
        /// A round that has been asked to start (solo auto-start, or the host's START) but whose
        /// intro is not armed yet: the state authority waits until the world renders smoothly
        /// (<see cref="IntroArmGate"/>) so the "3" is never eaten by a load or tap hitch. Gameplay
        /// is frozen (not <see cref="Active"/>), no timer runs, and the intro overlay shows GET READY.
        /// Appended (not inserted) so the existing values keep their wire numbers.
        /// </summary>
        Starting = 3,
    }
}
