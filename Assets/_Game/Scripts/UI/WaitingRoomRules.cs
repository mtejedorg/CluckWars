using System.Collections.Generic;
using CluckWars.Services;

namespace CluckWars.UI
{
    /// <summary>
    /// Pure rules behind the in-match waiting room (re-audit round 2, finding 2), testable without a
    /// runner. The room shows one seat per spawn corner; a seat with a chicken on it is READY (the bird is
    /// picked and spawned, nothing else is asked of it).
    /// </summary>
    public static class WaitingRoomRules
    {
        /// <summary>
        /// The "{n}/{max}" counter: seats that show READY. It used to count the runner's connected players,
        /// so a room whose cards all said READY could read "1/4" (CPU seats and a joiner whose chicken
        /// was still spawning were counted differently on the cards and in the counter).
        /// </summary>
        public static int ReadyCount(IReadOnlyList<bool> seatHasChicken)
        {
            int n = 0;
            for (int i = 0; i < seatHasChicken.Count; i++)
                if (seatHasChicken[i]) n++;
            return n;
        }

        /// <summary>
        /// HOST goes on the seat of the session's master client, and never in solo (there is no one to
        /// host for). A CPU seat says CPU instead.
        /// </summary>
        public static bool ShowHostTag(bool solo, bool seatIsMaster, bool seatIsBot) =>
            !solo && seatIsMaster && !seatIsBot;

        /// <summary>
        /// Whether a seat of THE COOP shows its READY / PICKING pill. A guest who is still on the join card
        /// (<see cref="SessionMode.Join"/>) has not joined anyone yet, so their own seat (0) shows no pill -
        /// READY there promised a lineup that did not exist (round-3 finding 21). Every other seat that is
        /// taken (host, solo, CPU) keeps it; open seats never have one.
        /// </summary>
        public static bool ShowsStatePill(SessionMode mode, int seat) => !(mode == SessionMode.Join && seat == 0);
    }
}
