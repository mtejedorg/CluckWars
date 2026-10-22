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
            AutoPlayKey = "cw_autoplay", DevModeKey = "cw_devmode", HudKey = "cw_hud", ProfileKey = "cw_profile",
            AutoSoloKey = "cw_autosolo", AutoDisconnectKey = "cw_autodisconnect", AutoZoneKey = "cw_autozone",
            ShotsKey = "cw_shots", ShotTagKey = "cw_shottag", BandKey = "cw_band", ZonesKey = "cw_zones",
            FloorArrowsKey = "cw_floorarrows", EdgeArrowsKey = "cw_edgearrows";
        /// <summary>WP4: see <see cref="AutoAura"/>.</summary>
        public const string AutoAuraKey = "cw_autoaura";

        public static readonly string[] Keys =
            { AutoHostKey, AutoJoinKey, ClassKey, AutoStartKey, AutoStartTimeoutKey, AutoLoopKey, AutoPlayKey, DevModeKey, HudKey, ProfileKey, AutoSoloKey, AutoDisconnectKey, AutoZoneKey, ShotsKey, ShotTagKey, BandKey, ZonesKey, FloorArrowsKey, EdgeArrowsKey, AutoAuraKey };

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
        public bool AutoPlay, DevMode;
        /// <summary><c>cw_hud</c>: 0 off, 1 compact strip, 2 full panel (true / a bare flag = 1).</summary>
        public int Hud;
        /// <summary>
        /// <c>cw_profile</c>: the Unity Services authentication profile (sanitised, see <see cref="SanitizeProfile"/>), or
        /// null for the default profile. Two clients on one PC share the anonymous identity in the default profile, so
        /// the second one's join fails with "player is already a member of the lobby".
        /// </summary>
        public string Profile;

        /// <summary>At least one <c>cw_</c> key was present (valid or not).</summary>
        public bool AnyKey;
        /// <summary>Human-readable parse problems (each logged as a Warn).</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary><c>cw_autosolo</c>: PLAY SOLO (vs 3 bots); loops with <c>cw_autoloop</c> like the others.</summary>
        public bool AutoSolo;

        /// <summary>
        /// <c>cw_autodisconnect</c> (CW-218): seconds into this peer's first multiplayer round after which the driver
        /// freezes the main thread for <see cref="AutoTestRules.AutoDisconnectStallSeconds"/> (what a phone sent to the
        /// background does, so Fusion times the peer out). With the key set, the driver also presses REJOIN on ANY disconnect
        /// sheet that comes up (its own freeze or a real drop) and logs the outcome. 0 = off.
        /// </summary>
        public int AutoDisconnect;

        /// <summary>
        /// <c>cw_autozone</c> (B10): every round, the local chicken keeps its zone-placing ability (Root Egg first, see
        /// <see cref="AutoTestRules.PickZoneSlot"/>) out of the fake player's rotation and casts it once with
        /// <see cref="AutoTestRules.ZoneLeadSeconds"/> left, and PLAY AGAIN is pressed after
        /// <see cref="AutoTestRules.PlayAgainDelay"/>, so a zone is alive when the world resets. The next round-start
        /// <c>Invariant</c> check then shows whether the reset despawned it (<c>zn=0</c>) or not (<c>zones-zero</c>).
        /// Needs <c>cw_autoplay</c> (the press goes through the fake player's input).
        /// </summary>
        public bool AutoZone;

        /// <summary>
        /// <c>cw_autoaura</c> (WP4): every round, the local chicken keeps its Feather Aura out of the fake player's rotation,
        /// walks into the nearest rival from <see cref="AutoAuraRules.ChaseAtRemaining"/> s left, casts it there and keeps
        /// walking into that rival while the aura lives. Every peer's <c>Invariant</c> probe then samples the victim's
        /// replicated <c>ControlVfx.Snared</c> (<c>Invariant aura ... remote=1 verdict=ok</c>), and merge.ps1 requires that
        /// line once an aura was cast: a REMOTE victim proven slowed by state (CW-217). Needs <c>cw_autoplay</c>; not with
        /// <c>cw_autozone</c> (one reserved slot).
        /// </summary>
        public bool AutoAura;

        /// <summary>
        /// <c>cw_shots 15,22,29</c> (dev tools only): seconds since this peer's first Active round began at which the driver
        /// saves a screenshot (<see cref="AutoTestRules.ShotFileName"/>). Sorted, distinct, 0..<see cref="MaxShotSecond"/>; empty = off.
        /// </summary>
        public int[] Shots = new int[0];
        /// <summary><c>cw_shottag</c>: sanitised text appended to every screenshot file name, or null.</summary>
        public string ShotTag;
        /// <summary><c>cw_band 290|330</c>: dev override of the landscape top-band info zone height in panel px; 0 = the shipped value.</summary>
        public int BandOverride;
        /// <summary><c>cw_zones</c>: draw the HUD info / control zones as an outlined overlay (dev tools only).</summary>
        public bool Zones;
        /// <summary><c>cw_floorarrows</c> (dev tools only): explicitly ask for the floor-ring rival arrows
        /// (<c>FloorRivalArrows</c>). Since 2026-10-22 they are the dev-build default, so this only documents intent.</summary>
        public bool FloorArrows;
        /// <summary><c>cw_edgearrows</c> (dev tools only): switch a dev build back to the shipped edge chevrons, for
        /// comparisons. Production always has the chevrons.</summary>
        public bool EdgeArrows;

        public const int MaxShotSecond = 3600;

        /// <summary>The landscape top-band heights <c>cw_band</c> accepts: the bar without its 40 px fade, and the whole bar.</summary>
        public static bool IsValidBand(int px) => px == 290 || px == 330;

        /// <summary>A comma-separated list of whole seconds, sorted and de-duplicated; bad items are reported in <paramref name="problems"/>.</summary>
        public static int[] ParseShots(string value, ICollection<string> problems)
        {
            var list = new List<int>();
            foreach (var item in (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(item.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec) && sec >= 0 && sec <= MaxShotSecond)
                { if (!list.Contains(sec)) list.Add(sec); }
                else problems?.Add($"{ShotsKey} item '{item}' is not a whole number of seconds in 0..{MaxShotSecond}; skipped");
            }
            list.Sort();
            return list.ToArray();
        }

        public bool WantsMenuFlow => AutoHost || AutoJoin != null || AutoSolo;

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
                    case AutoSoloKey: cfg.AutoSolo = cfg.ParseBool(key, value); break;
                    case AutoPlayKey: cfg.AutoPlay = cfg.ParseBool(key, value); break;
                    case DevModeKey:  cfg.DevMode = cfg.ParseBool(key, value); break;
                    case HudKey:
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) && level >= 0 && level <= 2) cfg.Hud = level;
                        else cfg.Hud = cfg.ParseBool(key, value) ? 1 : 0;
                        break;
                    case AutoJoinKey:
                        if (!IsJoinCode(value))
                            cfg.Problems.Add($"{key} '{value}' is not a whole join code ({CluckWars.UI.JoinCodeEntry.MinLength}-" +
                                             $"{CluckWars.UI.JoinCodeEntry.MaxLength} characters, the JOIN MATCH rule); auto-join off");
                        else cfg.AutoJoin = value.ToUpperInvariant();
                        break;
                    case ClassKey:
                        cfg.ClassGiven = true;
                        if (TryParseClass(value, out var cls)) cfg.Class = cls;
                        else cfg.Problems.Add($"{key} '{value}' is not a class ({string.Join("|", Enum.GetNames(typeof(ChickenClass)))}); using {DefaultClass}");
                        break;
                    case ProfileKey:
                        cfg.Profile = SanitizeProfile(value);
                        if (cfg.Profile == null) cfg.Problems.Add($"{key} '{value}' has no letters, digits, '-' or '_'; using the default profile");
                        else if (cfg.Profile != value) cfg.Problems.Add($"{key} '{value}' sanitised to '{cfg.Profile}' (A-Z a-z 0-9 - _, max {MaxProfileLength})");
                        break;
                    case AutoStartKey:        cfg.AutoStart = cfg.ParseInt(key, value, 0, 4); break;
                    case AutoLoopKey:         cfg.AutoLoop = cfg.ParseInt(key, value, 0, 1000); break;
                    case AutoStartTimeoutKey: cfg.AutoStartTimeout = cfg.ParseInt(key, value, 1, 3600, (int)DefaultAutoStartTimeout); break;
                    case AutoDisconnectKey:   cfg.AutoDisconnect = cfg.ParseInt(key, value, 1, 600); break;
                    case AutoZoneKey:         cfg.AutoZone = cfg.ParseBool(key, value); break;
                    case AutoAuraKey:         cfg.AutoAura = cfg.ParseBool(key, value); break;
                    case ShotsKey:            cfg.Shots = ParseShots(value, cfg.Problems); break;
                    case ShotTagKey:
                        cfg.ShotTag = SanitizeProfile(value);
                        if (cfg.ShotTag == null) cfg.Problems.Add($"{key} '{value}' has no letters, digits, '-' or '_'; no tag");
                        break;
                    case BandKey:
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int band) && IsValidBand(band)) cfg.BandOverride = band;
                        else cfg.Problems.Add($"{key} '{value}' is not 290 or 330; using the shipped band");
                        break;
                    case ZonesKey:            cfg.Zones = cfg.ParseBool(key, value); break;
                    case FloorArrowsKey:      cfg.FloorArrows = cfg.ParseBool(key, value); break;
                    case EdgeArrowsKey:       cfg.EdgeArrows = cfg.ParseBool(key, value); break;
                    default: cfg.Problems.Add($"unknown key {kv.Key}"); break;
                }
            }
            if (cfg.FloorArrows && cfg.EdgeArrows)
            {
                cfg.Problems.Add($"{FloorArrowsKey} and {EdgeArrowsKey} both given; edge chevrons ({FloorArrowsKey} ignored)");
                cfg.FloorArrows = false;
            }
            if (cfg.AutoZone && !cfg.AutoPlay)
            {
                cfg.Problems.Add($"{AutoZoneKey} needs {AutoPlayKey} (the cast is a fake-player press); {AutoZoneKey} off");
                cfg.AutoZone = false;
            }
            if (cfg.AutoAura && !cfg.AutoPlay)
            {
                cfg.Problems.Add($"{AutoAuraKey} needs {AutoPlayKey} (the walk and the cast are fake-player input); {AutoAuraKey} off");
                cfg.AutoAura = false;
            }
            if (cfg.AutoAura && cfg.AutoZone)
            {
                cfg.Problems.Add($"{AutoAuraKey} with {AutoZoneKey}: one reserved slot; {AutoZoneKey} wins ({AutoAuraKey} off)");
                cfg.AutoAura = false;
            }
            if (cfg.AutoSolo && (cfg.AutoHost || cfg.AutoJoin != null))
            {
                cfg.Problems.Add($"{AutoSoloKey} with {AutoHostKey}/{AutoJoinKey}; the session wins ({AutoSoloKey} ignored)");
                cfg.AutoSolo = false;
            }
            if (cfg.AutoHost && cfg.AutoJoin != null)
            {
                cfg.Problems.Add($"{AutoHostKey} and {AutoJoinKey} both given; hosting ({AutoJoinKey} ignored)");
                cfg.AutoJoin = null;
            }
            return cfg;
        }

        /// <summary>
        /// A code JOIN MATCH would accept (<see cref="CluckWars.UI.JoinCodeEntry.IsComplete"/>) and that fits the join field
        /// (<see cref="CluckWars.UI.JoinCodeEntry.MaxLength"/>): anything else would sit on THE COOP forever.
        /// </summary>
        public static bool IsJoinCode(string value) =>
            CluckWars.UI.JoinCodeEntry.IsComplete(value) && value.Trim().Length <= CluckWars.UI.JoinCodeEntry.MaxLength;

        /// <summary>Unity Authentication's profile-name limit.</summary>
        public const int MaxProfileLength = 30;

        /// <summary>
        /// A name Unity Authentication accepts (<c>^[a-zA-Z0-9_-]{1,30}$</c>): every other character dropped, cut to 30;
        /// null when nothing is left (the default profile is then used).
        /// </summary>
        public static string SanitizeProfile(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var sb = new System.Text.StringBuilder(Math.Min(value.Length, MaxProfileLength));
            foreach (char c in value)
            {
                if (sb.Length >= MaxProfileLength) break;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : null;
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
            " autosolo=" + (AutoSolo ? 1 : 0) +
            " class=" + Class +
            " autostart=" + AutoStart.ToString(CultureInfo.InvariantCulture) +
            " autoloop=" + AutoLoop.ToString(CultureInfo.InvariantCulture) +
            " autoplay=" + (AutoPlay ? 1 : 0) +
            " timeout=" + AutoStartTimeout.ToString("0", CultureInfo.InvariantCulture) +
            " devmode=" + (DevMode ? 1 : 0) +
            " hud=" + Hud.ToString(CultureInfo.InvariantCulture) +
            " profile=" + (Profile ?? "default");
    }

    /// <summary>cw_autodisconnect's menu half: waiting for the disconnect sheet, REJOIN pressed, finished.</summary>
    public enum AutoRejoinPhase { WaitSheet, Pressed, Done }

    /// <summary>What a REJOIN press led to (<see cref="AutoTestRules.DecideRejoinOutcome"/>).</summary>
    public enum AutoRejoinOutcome { Pending, Joined, Failed, TimedOut }

    /// <summary>The auto-test's timing decisions (start, play again, join retries), pure so tests pin them.</summary>
    public static class AutoTestRules
    {
        public const float JoinRetrySeconds = 5f;
        public const float JoinGiveUpSeconds = 120f;
        /// <summary>One JOIN MATCH press must end (joined + match scene loading, or a failure) within this.</summary>
        public const float JoinAttemptTimeoutSeconds = 60f;
        public const float PlayAgainDelaySeconds = 4f;

        // ---- cw_autozone (B10) -----------------------------------------------------------------------------------

        /// <summary>Playable seconds left when <c>cw_autozone</c> presses its zone slot (released, i.e. cast, one hold later).</summary>
        public const float ZoneLeadSeconds = 1.5f;

        /// <summary>
        /// PLAY AGAIN under <c>cw_autozone</c>: soon enough that the zone cast at <see cref="ZoneLeadSeconds"/> is still alive
        /// at the next round-start check (Root Egg lives 8 s: lead 1.5 + this 1.5 + GET READY 1-2 + 0.5 s probe delay), as
        /// fast as a person who taps the button at once. Otherwise the person-paced <see cref="PlayAgainDelaySeconds"/>.
        /// </summary>
        public static float PlayAgainDelay(bool autoZone) => autoZone ? 1.5f : PlayAgainDelaySeconds;

        /// <summary>Press the reserved zone slot: the round is playable, it was not pressed yet, and the mark is reached.</summary>
        public static bool ShouldCastZone(bool matchRunning, bool alreadyPressed, float timeRemaining) =>
            matchRunning && !alreadyPressed && timeRemaining <= ZoneLeadSeconds;

        /// <summary>
        /// The slot <c>cw_autozone</c> casts: a Root Egg (8 s, the longest-lived zone), else any other zone-placing ability
        /// (a Static AoE: Feather Trap, Smoke Roost), else a Doppelganger
        /// (its decoy subscribes to the same reset). -1 = nothing in the loadout outlives a round.
        /// </summary>
        public static int PickZoneSlot(IReadOnlyList<CluckWars.Abilities.AbilityBaseSO> slots)
        {
            if (slots == null) return -1;
            int zone = -1, decoy = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                var a = slots[i];
                if (a == null) continue;
                if (a is CluckWars.Abilities.RootEggAbilitySO) return i;
                // Placed zones only: an Aura ends with its 3 s cast, so it never outlives a round (WP4 widened PlacesZone).
                if (zone < 0 && a.AreaKind == CluckWars.Abilities.AreaEffectKind.StaticAoE) zone = i;
                if (decoy < 0 && a is CluckWars.Abilities.DoppelgangerAbilitySO) decoy = i;
            }
            return zone >= 0 ? zone : decoy;
        }

        /// <summary>
        /// <c>cw_autodisconnect</c>'s main-thread freeze. The CW-218 phone stalled 12.8 s and was timed out; 15 s clears
        /// Fusion's timeout with margin.
        /// </summary>
        public const int AutoDisconnectStallSeconds = 15;

        /// <summary>After the freeze, the menu's disconnect sheet must be up within this, or the hook reports an Error.</summary>
        public const float AutoRejoinSheetWaitSeconds = 60f;

        /// <summary>How long the sheet is up before the auto-test presses REJOIN (a person reads it first).</summary>
        public const float AutoRejoinPressDelaySeconds = 1.2f;

        /// <summary>After REJOIN: back in the match or a reported failure within this, else the outcome is a timeout.</summary>
        public const float AutoRejoinOutcomeWaitSeconds = 60f;

        public static bool ShouldPressRejoin(float sheetVisibleSeconds) => sheetVisibleSeconds >= AutoRejoinPressDelaySeconds;

        /// <summary>The REJOIN press's result: a valid match manager again wins, then a reported failure, then the timeout.</summary>
        public static AutoRejoinOutcome DecideRejoinOutcome(bool backInMatch, bool failureReported, float secondsSincePress)
        {
            if (backInMatch) return AutoRejoinOutcome.Joined;
            if (failureReported) return AutoRejoinOutcome.Failed;
            return secondsSincePress > AutoRejoinOutcomeWaitSeconds ? AutoRejoinOutcome.TimedOut : AutoRejoinOutcome.Pending;
        }

        /// <summary>Freeze once this peer's round has been Active for <paramref name="afterSeconds"/> (0 = the hook is off).</summary>
        public static bool ShouldAutoDisconnect(int afterSeconds, bool multiplayer, float activeSeconds) =>
            afterSeconds > 0 && multiplayer && activeSeconds >= afterSeconds;

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
            if (waitedSeconds >= timeoutSeconds) { reason = FormattableString.Invariant($"timeout {timeoutSeconds:0}s with {humans}/{target} humans"); return true; }
            return false;
        }

        // ---- cw_shots: screenshots at fixed seconds into the first Active round ------------------------------------

        /// <summary>Take the next screenshot (<paramref name="taken"/> already done) once the round has been Active for its second.</summary>
        public static bool ShouldTakeShot(int[] shots, int taken, float activeSeconds) =>
            shots != null && taken >= 0 && taken < shots.Length && activeSeconds >= shots[taken];

        /// <summary>The directory of the <c>-logFile</c> argument (the session folder), or null when it is absent, <c>-</c> or has no directory.</summary>
        public static string ShotDirectoryFromArgs(IReadOnlyList<string> args)
        {
            if (args == null) return null;
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (!string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase)) continue;
                var path = args[i + 1];
                if (string.IsNullOrWhiteSpace(path) || path == "-") return null;
                try { var dir = System.IO.Path.GetDirectoryName(path); return string.IsNullOrEmpty(dir) ? null : System.IO.Path.Combine(dir, "shots"); }
                catch (ArgumentException) { return null; }
            }
            return null;
        }

        /// <summary><c>&lt;profile&gt;-t&lt;sec&gt;[-&lt;tag&gt;].png</c>; profile "default" when none.</summary>
        public static string ShotFileName(string profile, int second, string tag) =>
            (string.IsNullOrEmpty(profile) ? "default" : profile) + "-t" + second.ToString(CultureInfo.InvariantCulture) +
            (string.IsNullOrEmpty(tag) ? string.Empty : "-" + tag) + ".png";

        /// <summary>A looped round never waits longer than this for the previous round's humans to come back.</summary>
        public const float LoopRestartMaxWaitSeconds = 15f;

        /// <summary>
        /// Round 2+ of an auto-loop (the waiting room re-armed by PLAY AGAIN): START as soon as every human who was in
        /// the previous round is back (<paramref name="humansAtEnd"/>), or after <see cref="LoopRestartMaxWaitSeconds"/>.
        /// <c>cw_autostart</c>'s N and its long timeout only govern the first round (a 3-seat target with 2 humans left
        /// used to hold every looped round for the whole timeout).
        /// </summary>
        public static bool ShouldRestartLoop(int humansNow, int humansAtEnd, float waitedSeconds, out string reason)
        {
            reason = null;
            if (humansNow >= Math.Max(1, humansAtEnd)) { reason = $"{humansNow}/{humansAtEnd} of last round's humans back"; return true; }
            if (waitedSeconds >= LoopRestartMaxWaitSeconds) { reason = FormattableString.Invariant($"max wait {LoopRestartMaxWaitSeconds:0}s with {humansNow}/{humansAtEnd}"); return true; }
            return false;
        }

        /// <summary>Take PLAY AGAIN after a round when fewer than <paramref name="autoLoop"/> extra rounds have been played.</summary>
        public static bool ShouldPlayAgain(int extraRoundsPlayed, int autoLoop) => extraRoundsPlayed < autoLoop;

        /// <summary>Retry a failed join while the first attempt is less than <see cref="JoinGiveUpSeconds"/> old.</summary>
        public static bool ShouldRetryJoin(float secondsSinceFirstAttempt) => secondsSinceFirstAttempt < JoinGiveUpSeconds;
    }

    /// <summary>The <c>AutoPlay</c> log lines (transition + 5 s heartbeat), invariant culture so every device prints them alike.</summary>
    public static class AutoPlayLog
    {
        public struct Snapshot
        {
            public AutoPlayState State;
            public AutoPlayGoal Goal;
            public float GoalDistance, Cargo;
            public int StuckTicks, Unsticks, PeckPresses;
            public UnityEngine.Vector2 Position;
            public bool CanForage;
            public string Personality;
        }

        /// <summary><c>&lt;prefix&gt; state=.. goal=.. dist=0.0 cargo=0 stuck_ticks=0 unsticks=0 pecks=0 pos=0.0,0.0 forage=0|1 personality=..</c></summary>
        public static string Line(string prefix, in Snapshot s) => prefix + FormattableString.Invariant(
            $" state={s.State} goal={s.Goal} dist={s.GoalDistance:0.0} cargo={s.Cargo:0} stuck_ticks={s.StuckTicks} unsticks={s.Unsticks} pecks={s.PeckPresses} pos={s.Position.x:0.0},{s.Position.y:0.0} forage={(s.CanForage ? 1 : 0)} personality={s.Personality}");
    }
}
