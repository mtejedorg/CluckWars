using System.Globalization;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// How much of the on-device overlay is shown: nothing, a one-line strip in a corner that leaves the match HUD
    /// visible (<c>cw_hud 1</c>), or the full Debug panel (<c>cw_hud 2</c>). F1 and the five-tap corner gesture cycle
    /// Off → Compact → Full → Off.
    /// </summary>
    public enum DebugHudMode : byte { Off = 0, Compact = 1, Full = 2 }

    /// <summary>Pure rules of the overlay modes and the compact strip's text, pinned by tests.</summary>
    public static class DebugHudModes
    {
        public static DebugHudMode Next(DebugHudMode mode) => mode switch
        {
            DebugHudMode.Off     => DebugHudMode.Compact,
            DebugHudMode.Compact => DebugHudMode.Full,
            _                    => DebugHudMode.Off,
        };

        /// <summary><c>cw_hud</c> value: 0 off, 1 compact, 2 full; anything above 2 is full.</summary>
        public static DebugHudMode FromLevel(int level) =>
            level <= 0 ? DebugHudMode.Off : level == 1 ? DebugHudMode.Compact : DebugHudMode.Full;

        /// <summary>
        /// <c>FPS 60 | p95 18.2ms | hitches 3 | visual 0 | rtt 45ms</c>; a probe that has not reported yet (negative
        /// value) prints <c>n/a</c>.
        /// </summary>
        public static string CompactLine(float fps, float p95Ms, int hitches, int visualOffenders, float rttMs) =>
            "FPS " + fps.ToString("0", CultureInfo.InvariantCulture) +
            " | p95 " + (p95Ms >= 0f ? p95Ms.ToString("0.0", CultureInfo.InvariantCulture) + "ms" : "n/a") +
            " | hitches " + hitches.ToString(CultureInfo.InvariantCulture) +
            " | visual " + (visualOffenders >= 0 ? visualOffenders.ToString(CultureInfo.InvariantCulture) : "n/a") +
            " | rtt " + (rttMs >= 0f ? rttMs.ToString("0", CultureInfo.InvariantCulture) + "ms" : "n/a");
    }
}
