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
        /// <param name="opensSheet">False for a press that is not an explicit back (a pad's B, which fires slot 3 in a match):
        /// it never OPENS the sheet and never leaves the waiting room, but still closes the sheet and leaves the podium.</param>
        public static MatchBackAction Resolve(MatchState? state, bool sheetOpen, bool leaving, bool abilityHeld = false, bool opensSheet = true)
        {
            if (leaving || !state.HasValue) return MatchBackAction.None;
            switch (state.Value)
            {
                case MatchState.WaitingForPlayers: return opensSheet ? MatchBackAction.LeaveWaitingRoom : MatchBackAction.None;
                case MatchState.Ended:             return MatchBackAction.BackToLobby;
                default:
                    if (sheetOpen) return MatchBackAction.CloseLeaveSheet;
                    return abilityHeld || !opensSheet ? MatchBackAction.None : MatchBackAction.OpenLeaveSheet;
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

        private static bool _carry;

        /// <summary>True while this peer's input must not reach the simulation: the sheet is up, or it just closed and a
        /// move button that was down is still down (<see cref="LeaveSheetInputGate"/>). Safe to call several times a frame.</summary>
        public static bool BlocksInput(bool anyAbilityHeld) => LeaveSheetInputGate.Blocks(ref _carry, IsOpen, anyAbilityHeld);
    }

    /// <summary>
    /// Pure rule behind pad B (BL-202): B closes the Leave sheet AND is slot 3. On the frame the sheet closes the button is
    /// still down, so without this its press edge and then its hold bit would reach the simulation and cast slot 3 on
    /// release. After the sheet has been up, input stays blocked until every move button is up.
    /// </summary>
    public static class LeaveSheetInputGate
    {
        /// <param name="carry">Remembered between calls: the sheet was up and no release has been seen since.</param>
        public static bool Blocks(ref bool carry, bool sheetOpen, bool anyAbilityHeld)
        {
            if (sheetOpen) { carry = true; return true; }
            if (carry && anyAbilityHeld) return true;
            carry = false;
            return false;
        }
    }
}
