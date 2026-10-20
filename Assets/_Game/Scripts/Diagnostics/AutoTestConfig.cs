using System;
using System.Collections.Generic;
using System.Globalization;
using CluckWars.Gameplay;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// The auto-test keys (test-session item 7), parsed from Android intent extras (<c>am start … -e cw_autojoin ABC123</c>)
    /// or from the Windows / Editor command line (<c>-cw_autojoin ABC123</c>, booleans as <c>1</c> / <c>true</c> or a bare
    /// flag). Pure: <see cref="FromCommandLine"/> and <see cref="FromMap"/> are pinned by tests.
    /// </summary>
    public sealed class AutoTestConfig
    {
        public const string KeyPrefix = "cw_";
        public const string AutoHostKey = "cw_autohost", AutoJoinKey = "cw_autojoin", ClassKey = "cw_class",
            AutoStartKey = "cw_autostart", AutoStartTimeoutKey = "cw_autostart_timeout", AutoLoopKey = "cw_autoloop",
            AutoPlayKey = "cw_autoplay", DevModeKey = "cw_devmode", HudKey = "cw_hud";

        public static readonly string[] Keys =
            { AutoHostKey, AutoJoinKey, ClassKey, AutoStartKey, AutoStartTimeoutKey, AutoLoopKey, AutoPlayKey, DevModeKey, HudKey };

        public const ChickenClass DefaultClass = ChickenClass.Warrior;
        public const float DefaultAutoStartTimeout = 90f;

        public bool AutoHost;
        /// <summary>Join code (trimmed, upper-case), or null.</summary>
        public string AutoJoin;
        public ChickenClass Class = DefaultClass;
        /// <summary>True when <c>cw_class</c> was given (valid or not).</summary>
        public bool ClassGiven;
        /// <summary>Humans to wait for before the host starts; 0 = not set (a human presses START).</summary>
        public int AutoStart;
        public float AutoStartTimeout = DefaultAutoStartTimeout;
        /// <summary>Extra rounds to take with PLAY AGAIN after the first.</summary>
        public int AutoLoop;
        public bool AutoPlay, DevMode, Hud;

        /// <summary>At least one <c>cw_</c> key was present (valid or not).</summary>
        public bool AnyKey;
        /// <summary>Human-readable parse problems (each logged as a Warn).</summary>
        public readonly List<string> Problems = new List<string>();

        public bool WantsMenuFlow => AutoHost || AutoJoin != null;

        /// <summary><c>-cw_key value</c> pairs; a <c>-cw_key</c> followed by another switch (or nothing) is a bare flag (= 1).</summary>
        public static AutoTestConfig FromCommandLine(IReadOnlyList<string> args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddCommandLine(args, map);
            return FromMap(map);
        }

        /// <summary>Adds the command line's <c>-cw_key [value]</c> pairs to <paramref name="map"/>.</summary>
        public static void AddCommandLine(IReadOnlyList<string> args, IDictionary<string, string> map)
        {
            if (args == null) return;
            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                if (string.IsNullOrEmpty(a) || a.Length < 2 || a[0] != '-') continue;
                var key = a.TrimStart('-');
                if (!key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                string value = "1";
                if (i + 1 < args.Count && !string.IsNullOrEmpty(args[i + 1]) && args[i + 1][0] != '-')
                    value = args[++i];
                map[key] = value;
            }
        }

        /// <summary>
        /// Adds an Editor spec (<c>AutoTest.EditorConfigure("autohost=1 class=Warrior autostart=2 autoplay")</c>): space
        /// separated <c>key=value</c>, the <c>cw_</c> prefix optional, a bare key meaning 1.
        /// </summary>
        public static void AddSpec(string spec, IDictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            foreach (var token in spec.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = token.IndexOf('=');
                var key = (eq >= 0 ? token.Substring(0, eq) : token).Trim().TrimStart('-');
                var value = eq >= 0 ? token.Substring(eq + 1).Trim() : "1";
                if (key.Length == 0) continue;
                if (!key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)) key = KeyPrefix + key;
                map[key] = value;
            }
        }

        /// <summary>From a key → value map (Android intent extras, or the command line's pairs). Keys are case-insensitive.</summary>
        public static AutoTestConfig FromMap(IEnumerable<KeyValuePair<string, string>> map)
        {
            var cfg = new AutoTestConfig();
            if (map == null) return cfg;
            foreach (var kv in map)
            {
                if (kv.Key == null || !kv.Key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                cfg.AnyKey = true;
                var key = kv.Key.ToLowerInvariant();
                var value = (kv.Value ?? string.Empty).Trim();
                switch (key)
                {
                    case AutoHostKey: cfg.AutoHost = cfg.ParseBool(key, value); break;
                    case AutoPlayKey: cfg.AutoPlay = cfg.ParseBool(key, value); break;
                    case DevModeKey:  cfg.DevMode = cfg.ParseBool(key, value); break;
                    case HudKey:      cfg.Hud = cfg.ParseBool(key, value); break;
                    case AutoJoinKey:
                        if (value.Length == 0 || value == "1") cfg.Problems.Add($"{key} needs a join code (got '{value}')");
                        else cfg.AutoJoin = value.ToUpperInvariant();
                        break;
                    case ClassKey:
                        cfg.ClassGiven = true;
                        if (TryParseClass(value, out var cls)) cfg.Class = cls;
                        else cfg.Problems.Add($"{key} '{value}' is not a class ({string.Join("|", Enum.GetNames(typeof(ChickenClass)))}); using {DefaultClass}");
                        break;
                    case AutoStartKey:        cfg.AutoStart = cfg.ParseInt(key, value, 0, 4); break;
                    case AutoLoopKey:         cfg.AutoLoop = cfg.ParseInt(key, value, 0, 1000); break;
                    case AutoStartTimeoutKey: cfg.AutoStartTimeout = cfg.ParseInt(key, value, 1, 3600, (int)DefaultAutoStartTimeout); break;
                    default: cfg.Problems.Add($"unknown key {kv.Key}"); break;
                }
            }
            if (cfg.AutoHost && cfg.AutoJoin != null)
            {
                cfg.Problems.Add($"{AutoHostKey} and {AutoJoinKey} both given; hosting ({AutoJoinKey} ignored)");
                cfg.AutoJoin = null;
            }
            return cfg;
        }

        /// <summary>A class name, case-insensitive. Numbers are refused (Enum.TryParse would accept "2").</summary>
        public static bool TryParseClass(string value, out ChickenClass cls)
        {
            cls = DefaultClass;
            if (string.IsNullOrEmpty(value) || !char.IsLetter(value[0])) return false;
            return Enum.TryParse(value, true, out cls) && Enum.IsDefined(typeof(ChickenClass), cls);
        }

        private bool ParseBool(string key, string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "": case "1": case "true": case "yes": case "on": return true;
                case "0": case "false": case "no": case "off": return false;
                default: Problems.Add($"{key} '{value}' is not a boolean (1/0/true/false); treated as off"); return false;
            }
        }

        private int ParseInt(string key, string value, int min, int max, int fallback = 0)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max) return n;
            Problems.Add($"{key} '{value}' is not a whole number in {min}..{max}; using {fallback}");
            return fallback;
        }

        /// <summary><c>AutoTest config autohost=.. autojoin=.. class=.. autostart=.. autoloop=.. autoplay=..</c> (+ the rest).</summary>
        public string Describe() =>
            "AutoTest config autohost=" + (AutoHost ? 1 : 0) +
            " autojoin=" + (AutoJoin ?? "-") +
            " class=" + Class +
            " autostart=" + AutoStart.ToString(CultureInfo.InvariantCulture) +
            " autoloop=" + AutoLoop.ToString(CultureInfo.InvariantCulture) +
            " autoplay=" + (AutoPlay ? 1 : 0) +
            " timeout=" + AutoStartTimeout.ToString("0", CultureInfo.InvariantCulture) +
            " devmode=" + (DevMode ? 1 : 0) +
            " hud=" + (Hud ? 1 : 0);
    }

    /// <summary>The auto-test's timing decisions (start, play again, join retries), pure so tests pin them.</summary>
    public static class AutoTestRules
    {
        public const float JoinRetrySeconds = 5f;
        public const float JoinGiveUpSeconds = 120f;
        public const float PlayAgainDelaySeconds = 4f;

        /// <summary>
        /// Host in the waiting room: start once <paramref name="target"/> humans are in (1 = at once), or after the timeout
        /// with whoever is there. <paramref name="target"/> 0 means auto-start is off.
        /// </summary>
        public static bool ShouldStart(int target, int humans, float waitedSeconds, float timeoutSeconds, out string reason)
        {
            reason = null;
            if (target <= 0) return false;
            if (target == 1) { reason = "autostart=1"; return true; }
            if (humans >= target) { reason = $"{humans}/{target} humans in"; return true; }
            if (waitedSeconds >= timeoutSeconds) { reason = $"timeout {timeoutSeconds:0}s with {humans}/{target} humans"; return true; }
            return false;
        }

        /// <summary>Take PLAY AGAIN after a round when fewer than <paramref name="autoLoop"/> extra rounds have been played.</summary>
        public static bool ShouldPlayAgain(int extraRoundsPlayed, int autoLoop) => extraRoundsPlayed < autoLoop;

        /// <summary>Retry a failed join while the first attempt is less than <see cref="JoinGiveUpSeconds"/> old.</summary>
        public static bool ShouldRetryJoin(float secondsSinceFirstAttempt) => secondsSinceFirstAttempt < JoinGiveUpSeconds;
    }
}
