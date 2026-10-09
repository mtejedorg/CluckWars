using Fusion;

namespace CluckWars.Gameplay
{
    /// <summary>What one Back press (Esc, Android back, gamepad Start) does in the match scene.</summary>
    public enum MatchBackAction
    {
        None,
        /// <summary>Waiting room: LEAVE (the room's own exit, unchanged).</summary>
        LeaveWaitingRoom,
        /// <summary>GET READY, countdown or a running round: ask "Leave match?".</summary>
        OpenLeaveSheet,
        /// <summary>The sheet is up: Back again is KEEP PLAYING.</summary>
        CloseLeaveSheet,
        /// <summary>Post-match podium: the same exit as the BACK TO LOBBY button.</summary>
        BackToLobby,
    }

    /// <summary>
    /// Pure rules for leaving a match (Phase 6 chunk 7b, round-3 finding 4): what Back does per match
    /// phase, when the sheet is still valid, and when a solo session freezes under it.
    /// </summary>
    public static class MatchBackRules
    {
        /// <param name="state">The match phase, or null while there is no GameManager yet.</param>
        /// <param name="abilityHeld">A move is being held / aimed. Esc is also the keyboard's ability cancel, so a Back
        /// that would open the sheet only cancels the hold instead (Back again opens it).</param>
        public static MatchBackAction Resolve(MatchState? state, bool sheetOpen, bool leaving, bool abilityHeld = false)
        {
            if (leaving || !state.HasValue) return MatchBackAction.None;
            switch (state.Value)
            {
                case MatchState.WaitingForPlayers: return MatchBackAction.LeaveWaitingRoom;
                case MatchState.Ended:             return MatchBackAction.BackToLobby;
                default:
                    if (sheetOpen) return MatchBackAction.CloseLeaveSheet;
                    return abilityHeld ? MatchBackAction.None : MatchBackAction.OpenLeaveSheet;
            }
        }

        /// <summary>The sheet belongs to Starting / Active only; the waiting room and the podium own their own exits,
        /// so a round that ends (or a session reset) under an open sheet closes it.</summary>
        public static bool SheetAllowed(MatchState? state) =>
            state == MatchState.Starting || state == MatchState.Active;

        /// <summary>Solo freezes the simulation under the sheet once the round is armed; multiplayer never does
        /// (the match keeps going without you).</summary>
        public static bool PausesSimulation(bool solo, MatchState? state, bool sheetOpen) =>
            sheetOpen && solo && state == MatchState.Active;

    }

    /// <summary>
    /// Whether the local "Leave match?" sheet is up. The network service reads it so that no move, press or
    /// hold from this peer reaches the simulation while the player is deciding (a click on the sheet must not
    /// fire an ability). Local presentation state: never networked. Owned by MatchOverlaysController, which
    /// clears it whenever it goes away.
    /// </summary>
    public static class LeaveSheetState
    {
        public static bool IsOpen { get; set; }
    }
}
