using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CluckWars.Logging
{
    /// <summary>
    /// The one line format every <see cref="UnityLogService"/> line uses, and its parser:
    /// <c>[HH:mm:ss.fff][mm:ss.fff][Level][Source] message</c>.
    /// </summary>
    /// <remarks>
    /// The first bracket is the UTC wall clock: it is what lets the phone (logcat), the Windows EXEs (Player.log) and the
    /// Editor (Editor.log) be merged on one timeline (docs/TEST_SESSION_PLAN_2026-10.md §2.1; tools/test-session merges
    /// on it). The second is the time since process start (<c>realtimeSinceStartup</c>), kept because every older log
    /// note reads it; its minutes are not capped at 99. Both are truncated to the millisecond, never rounded up, so a
    /// value never prints as <c>60.000</c>. Pure: the tests pin the format and round-trip it through <see cref="Pattern"/>.
    /// </remarks>
    public static class LogLineFormat
    {
        /// <summary>Parses one formatted line: wall clock, uptime, level, source, message.</summary>
        public static readonly Regex Pattern = new Regex(
            @"^\[(\d\d:\d\d:\d\d\.\d{3})\]\[(\d+:\d\d\.\d{3})\]\[(\w+)\]\[([^\]]+)\] (.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static string Format(DateTime utcNow, double uptimeSeconds, LogLevel level, string source, string message) =>
            "[" + WallClock(utcNow) + "][" + Uptime(uptimeSeconds) + "][" + level + "][" + source + "] " + message;

        /// <summary><c>HH:mm:ss.fff</c> of <paramref name="utc"/> (24 h, invariant culture).</summary>
        public static string WallClock(DateTime utc) => utc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        /// <summary><c>m:ss.fff</c> with at least two minute digits; the minutes keep growing past 99.</summary>
        public static string Uptime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0d) seconds = 0d;
            long ms = (long)Math.Floor(seconds * 1000d);
            long minutes = ms / 60000;
            long rest = ms % 60000;
            return minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   (rest / 1000).ToString("00", CultureInfo.InvariantCulture) + "." +
                   (rest % 1000).ToString("000", CultureInfo.InvariantCulture);
        }

        public readonly struct Parsed
        {
            public readonly string WallClock, Uptime, Level, Source, Message;
            public Parsed(string wall, string up, string level, string source, string message)
            { WallClock = wall; Uptime = up; Level = level; Source = source; Message = message; }
        }

        public static bool TryParse(string line, out Parsed parsed)
        {
            parsed = default;
            if (string.IsNullOrEmpty(line)) return false;
            var m = Pattern.Match(line);
            if (!m.Success) return false;
            parsed = new Parsed(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value);
            return true;
        }
    }
}
