using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// Renders <see cref="MatchConfigSO"/> values as the strings the "MATCH SETTINGS"
    /// cards display. Two screens show that card — the menu lobby (Lobby.uxml, driven by
    /// <see cref="MenuUiController"/>) and the in-match waiting room (MatchOverlays.uxml,
    /// driven by <see cref="MatchOverlaysController"/>) — so the formatting lives here
    /// once instead of once per screen.
    /// </summary>
    /// <remarks>
    /// Pure and panel-free on purpose: the EditMode suite feeds it the real
    /// MatchConfig.asset and compares the result against the authored UXML text, which is
    /// what catches a placeholder creeping back in.
    /// </remarks>
    public static class MatchSettingsText
    {
        /// <summary>
        /// Match duration as <c>m:ss</c> — 45f renders "0:45", not "0:45.0" or "1:00".
        /// </summary>
        /// <remarks>
        /// Seconds are <b>truncated</b>, not rounded: <c>MatchDurationSeconds</c> is a float
        /// and may hold a fractional value, and truncating matches the live match timer in
        /// <see cref="MatchHudController"/>. Rounding here would let the lobby advertise
        /// "0:46" for a match the HUD starts counting down from "00:45".
        /// </remarks>
        public static string Time(float matchDurationSeconds)
        {
            int mm = Mathf.Max(0, Mathf.FloorToInt(matchDurationSeconds / 60f));
            int ss = Mathf.Max(0, Mathf.FloorToInt(matchDurationSeconds - mm * 60f));
            return $"{mm}:{ss:00}";
        }

        /// <summary>Food target as it reads on the settings card — 40 renders "40 food".</summary>
        public static string Goal(int foodTargetToWin) => $"{foodTargetToWin} food";
    }
}
