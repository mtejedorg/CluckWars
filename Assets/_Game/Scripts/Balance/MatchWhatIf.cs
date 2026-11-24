using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using CluckWars.Gameplay;

namespace CluckWars.Balance
{
    /// <summary>One named balance delta of a what-if file.</summary>
    public sealed class WhatIfScenario
    {
        public string Name;
        /// <summary>The delta as JSON text (schema 1, the shape <see cref="BalanceBook.Evaluate"/> reads).</summary>
        public string DeltaJson;
    }

    /// <summary>The what-if file: <c>{"replicateScale":0.4,"scenarios":[{"name":"...","delta":{"schema":1,"assets":{...}}}]}</c>.</summary>
    public sealed class WhatIfSpec
    {
        public const string BaselineName = "baseline";
        public double ReplicateScale = 1.0;
        public List<WhatIfScenario> Scenarios = new List<WhatIfScenario>();

        public static bool TryParse(string text, out WhatIfSpec spec, out string error)
        {
            spec = null;
            if (!BalanceJson.TryParse(text, out var root, out error)) return false;
            if (root.Kind != JsonKind.Object) { error = "the what-if file is not a JSON object"; return false; }
            var result = new WhatIfSpec();
            foreach (var member in root.Members)
            {
                switch (member.Key)
                {
                    case "replicateScale":
                        if (member.Value.Kind != JsonKind.Number || !(member.Value.Number > 0.0)) { error = "\"replicateScale\" must be a number above zero"; return false; }
                        result.ReplicateScale = member.Value.Number;
                        break;
                    case "scenarios":
                        if (member.Value.Kind != JsonKind.Array) { error = "\"scenarios\" must be an array"; return false; }
                        foreach (var item in member.Value.Items)
                        {
                            if (item.Kind != JsonKind.Object) { error = "every scenario must be an object with \"name\" and \"delta\""; return false; }
                            if (!item.TryGet("name", out var name) || name.Kind != JsonKind.String || string.IsNullOrWhiteSpace(name.Text)) { error = "a scenario has no \"name\""; return false; }
                            if (name.Text == BaselineName) { error = $"\"{BaselineName}\" is the unchanged game and cannot be a scenario's name"; return false; }
                            if (result.Scenarios.Any(s => s.Name == name.Text)) { error = $"the scenario name \"{name.Text}\" is used twice"; return false; }
                            if (!item.TryGet("delta", out var delta)) { error = $"scenario \"{name.Text}\" has no \"delta\""; return false; }
                            result.Scenarios.Add(new WhatIfScenario { Name = name.Text, DeltaJson = delta.Kind == JsonKind.String ? delta.Text : Text(delta) });
                        }
                        break;
                    default:
                        error = $"unknown key \"{member.Key}\"";
                        return false;
                }
            }
            error = null;
            spec = result;
            return true;
        }

        /// <summary>A parsed value as compact JSON text; numbers keep their source token.</summary>
        public static string Text(JsonValue v)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return v.Bool ? "true" : "false";
                case JsonKind.Number: return v.Text;
                case JsonKind.String: return BalanceJson.Quote(v.Text);
                case JsonKind.Array: return "[" + string.Join(",", v.Items.Select(Text)) + "]";
                default: return "{" + string.Join(",", v.Members.Select(m => BalanceJson.Quote(m.Key) + ":" + Text(m.Value))) + "}";
            }
        }
    }

    /// <summary>One number of the report, in one population, with its margin: the unit the baseline and a scenario are compared in.</summary>
    public sealed class WhatIfNumber
    {
        public string Group;
        public string Label;
        public string Population;
        public float Value = float.NaN;
        /// <summary>Half-width of the 95 % interval in points; NaN for a count or a mean.</summary>
        public float Margin = float.NaN;
        public int Trials;
        public string Note;
        public string Key => Group + "|" + Label + "|" + Population;
    }

    /// <summary>A number in the baseline and in a scenario.</summary>
    public sealed class WhatIfLine
    {
        public string Group, Label, Population;
        public float Base = float.NaN, BaseMargin = float.NaN, Scenario = float.NaN, ScenarioMargin = float.NaN;
        public int BaseTrials, ScenarioTrials;
        public string BaseNote, ScenarioNote;
        public float Diff => Scenario - Base;
        /// <summary>The difference is larger than the two margins combined as if the runs were independent (common random numbers only make a difference steadier).</summary>
        public bool Beyond => !float.IsNaN(Diff) && !float.IsNaN(BaseMargin) && !float.IsNaN(ScenarioMargin)
            && Math.Abs(Diff) > Math.Sqrt(BaseMargin * BaseMargin + ScenarioMargin * ScenarioMargin);
    }

    public sealed class WhatIfResult
    {
        public string Name;
        public string DeltaJson;
        public bool Refused;
        public string Reason;
        public string Hash;
        public MatchReportSection Report;
        public List<WhatIfNumber> Numbers = new List<WhatIfNumber>();
        public double ElapsedSeconds;
    }

    public sealed class WhatIfOutput
    {
        public string BalanceHash, MapFingerprint;
        public ulong RunSeed;
        public double ReplicateScale;
        public int Threads;
        public int MatchesPlayed;
        public double ElapsedSeconds;
        public string SpoilerKit, ThiefKit;
        public string[] KitLabels;
        public WhatIfResult Baseline;
        public List<WhatIfResult> Scenarios = new List<WhatIfResult>();
    }

    /// <summary>
    /// The what-if report: plays the standard report designs for the shipped game and for each named delta with the same seeds (common random
    /// numbers), and lines the numbers up. A delta goes through <see cref="BalanceBook.Evaluate"/> and the verdict over the shipped assets, exactly as
    /// the solver's candidates do; one the book refuses or skips is reported with its reason and the others still run. Reports only: nothing is applied.
    /// </summary>
    public static class MatchWhatIf
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly PolicyPopulation[] Pops = { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed };
        public static string PopName(PolicyPopulation p) => p == PolicyPopulation.Mixed ? "mixed" : p.ToString();

        /// <summary>The context of <paramref name="deltaJson"/> over the shipped assets, or null with the reason the book gave. Needs <see cref="OracleInputs.Book"/>.</summary>
        public static MatchContext ContextFor(OracleInputs inputs, string deltaJson, float arenaHalf, float sightArrivalRadius, out string hash, out string reason)
        {
            hash = null;
            reason = null;
            var verdict = inputs.Book.Evaluate(deltaJson, "match what-if");
            if (!verdict.IsAccepted)
            {
                reason = verdict.Reason ?? "the book did not accept the delta";
                return null;
            }
            if (verdict.Ignored.Count > 0)
            {
                // The game would take the rest and skip these entries; a what-if of a half-applied delta would mislead.
                reason = "the book skipped " + string.Join("; ", verdict.Ignored);
                return null;
            }
            var moved = new OracleInputs
            {
                Map = inputs.Map, Book = inputs.Book, Verdict = verdict, Abilities = inputs.Abilities, Classes = inputs.Classes,
                Match = inputs.Match, FoodOverride = inputs.FoodOverride,
            };
            var context = MatchContext.From(moved, arenaHalf, sightArrivalRadius);
            hash = context.BalanceHash;
            return context;
        }

        public static WhatIfOutput Run(OracleInputs inputs, float arenaHalf, float sightArrivalRadius, WhatIfSpec spec, ulong seed, int threads,
            OracleReferenceSO reference, Action<string> progress = null)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (inputs.Book == null) throw new ArgumentException("A what-if run applies deltas through OracleInputs.Book.", nameof(inputs));
            if (inputs.Verdict != null && !inputs.Verdict.IsEmpty)
                throw new ArgumentException("A what-if run starts from the shipped assets: OracleInputs.Verdict must be null or empty.", nameof(inputs));

            var clock = Stopwatch.StartNew();
            var baseContext = MatchContext.From(inputs, arenaHalf, sightArrivalRadius);
            var output = new WhatIfOutput
            {
                BalanceHash = baseContext.BalanceHash, MapFingerprint = baseContext.Map != null ? baseContext.Map.SceneFingerprint : string.Empty,
                RunSeed = seed, ReplicateScale = spec.ReplicateScale, Threads = threads,
                KitLabels = baseContext.Kits.Select(k => k.Label).ToArray(),
                SpoilerKit = baseContext.Kits.FirstOrDefault(k => k.Passive.MatchEndBonusFood > 0f)?.Label,
                ThiefKit = baseContext.Kits.FirstOrDefault(k => k.Passive.Loot.IsThief || k.Passive.StealFactor > 1f)?.Label,
            };

            WhatIfResult Play(string name, string delta, MatchContext context, string hash)
            {
                var t = Stopwatch.StartNew();
                var runs = MatchReports.PlayStandard(context, seed, threads, false, null, spec.ReplicateScale);
                output.MatchesPlayed += runs.Sum(r => r.Results.Length);
                var result = new WhatIfResult { Name = name, DeltaJson = delta, Hash = hash, Report = MatchReports.Build(context, runs, reference) };
                result.Numbers = Numbers(result.Report);
                result.ElapsedSeconds = t.Elapsed.TotalSeconds;
                return result;
            }

            progress?.Invoke($"What-if: baseline and {spec.Scenarios.Count} scenarios, replicate scale {spec.ReplicateScale:0.##}, seed {seed}, {threads} threads.");
            output.Baseline = Play(WhatIfSpec.BaselineName, "{}", baseContext, baseContext.BalanceHash);
            progress?.Invoke($"What-if baseline played in {output.Baseline.ElapsedSeconds:0.0} s.");

            int index = 0;
            foreach (var scenario in spec.Scenarios)
            {
                index++;
                var context = ContextFor(inputs, scenario.DeltaJson, arenaHalf, sightArrivalRadius, out string hash, out string reason);
                if (context == null)
                {
                    output.Scenarios.Add(new WhatIfResult { Name = scenario.Name, DeltaJson = scenario.DeltaJson, Refused = true, Reason = reason });
                    progress?.Invoke($"What-if {index}/{spec.Scenarios.Count} \"{scenario.Name}\" REFUSED: {reason}");
                    continue;
                }
                var result = Play(scenario.Name, scenario.DeltaJson, context, hash);
                output.Scenarios.Add(result);
                progress?.Invoke($"What-if {index}/{spec.Scenarios.Count} \"{scenario.Name}\" (bal={hash}) played in {result.ElapsedSeconds:0.0} s; {clock.Elapsed.TotalMinutes:0.0} min so far.");
            }
            output.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            return output;
        }

        // ------------------------------------------------------------------ the numbers

        private static float Points(WinInterval w) => w.Trials == 0 ? float.NaN : w.Rate * 100f;
        private static float HalfPoints(WinInterval w) => w.Trials == 0 ? float.NaN : (w.High - w.Low) * 50f;

        /// <summary>Every number the what-if compares, from a report that holds the three populations.</summary>
        public static List<WhatIfNumber> Numbers(MatchReportSection report)
        {
            var list = new List<WhatIfNumber>();
            void Add(string group, string label, string pop, float value, float margin = float.NaN, int trials = 0, string note = null) =>
                list.Add(new WhatIfNumber { Group = group, Label = label, Population = pop, Value = value, Margin = margin, Trials = trials, Note = note });
            void AddRate(string group, string label, string pop, WinInterval w, string note = null) => Add(group, label, pop, Points(w), HalfPoints(w), w.Trials, note);

            var sections = report.Policies.Count > 0 ? report.Policies : new List<MatchReportSection> { report };
            foreach (var s in sections)
            {
                string pop = PopName(s.Population);
                foreach (var k in s.Kits) AddRate("kit", k.Label, pop, k.Win);
                foreach (var c in s.Classes)
                {
                    AddRate("class", c.Class + " (one per class)", pop, c.Headline);
                    AddRate("class", c.Class + " (pooled)", pop, c.Pooled);
                }
                long appear = 0, kills = 0;
                foreach (var row in s.KillsPlayed)
                {
                    appear += row.Win.Trials;
                    kills += (long)row.Kills * row.Win.Trials;
                    AddRate("kills-played", $"{row.Kills} kills, no passive", pop, row.Win, row.Label);
                    AddRate("kills-played", $"{row.Kills} kills, with passive", pop, row.WinWithPassive, row.Label);
                }
                if (appear > 0) Add("spoiler", "kills per match", pop, kills / (float)appear, trials: (int)appear);
                if (s.ThiefIncome != null)
                {
                    var t = s.ThiefIncome;
                    Add("thief", "steals per match", pop, t.StealsPerMatch, trials: t.Matches);
                    Add("thief", "food stolen per match", pop, t.StolenPerMatch, trials: t.Matches);
                    Add("thief", "food foraged per match", pop, t.ForagedPerMatch, trials: t.Matches);
                    Add("thief", "food banked per match", pop, t.BankedPerMatch, trials: t.Matches);
                    if (t.LootActive)
                    {
                        Add("thief", "marks drawn per match", pop, t.MarksDrawnPerMatch, trials: t.Matches);
                        Add("thief", "marks consumed per match", pop, t.MarksTakenPerMatch, trials: t.Matches);
                        Add("thief", "marks lost unpaid per match", pop, t.MarksLostPerMatch, trials: t.Matches);
                        Add("thief", "marks refused per match", pop, t.MarksRefusedPerMatch, trials: t.Matches);
                        Add("thief", "marked take per match", pop, t.LootTakeFoodPerMatch, trials: t.Matches);
                        Add("thief", "bounty food per match", pop, t.LootBountyFoodPerMatch, trials: t.Matches);
                        Add("thief", "bag food banked per match", pop, t.BagBankedPerMatch, trials: t.Matches);
                        Add("thief", "bag food lost to steals per match", pop, t.BagLostPerMatch, trials: t.Matches);
                        Add("thief", "won only on the prize (%)", pop, t.WonOnPrize * 100f, trials: t.Matches);
                        Add("thief", "loot mark on a Hunt-marked bird (% of Thief matches)", pop, t.OverlapShare * 100f, trials: t.Matches);
                        Add("thief", "loot mark on a Hunt-marked bird (% of Thief matches with a Spoiler)", pop, t.OverlapShareWithSpoiler * 100f, trials: t.MatchesWithSpoiler);
                        Add("thief", "seconds on a Hunt-marked bird per match", pop, t.OverlapSecondsPerMatch, trials: t.Matches);
                    }
                    var mirror = s.SeatByKit.FirstOrDefault(m => m.Kit == t.Kit);
                    if (mirror != null && mirror.Matches > 0)
                        Add("match", "Thief mirror decided by the corner (%)", pop, mirror.CornerDecided * 100f / mirror.Matches, trials: mirror.Matches);
                }
                foreach (var row in s.LootSteals)
                {
                    AddRate("loot-steals", $"{row.Steals} marked steals, no prize", pop, row.Win, row.Label);
                    AddRate("loot-steals", $"{row.Steals} marked steals, with prize", pop, row.WinWithPrize, row.Label);
                }
                var run = s.Runs.FirstOrDefault(r => r.Design == MatchDesignKind.Mixed);
                if (run != null && run.Matches > 0)
                {
                    Add("match", "ended on the clock (%)", pop, run.EndedOnClock * 100f / run.Matches, trials: run.Matches);
                    Add("match", "mean length (s)", pop, run.MeanEndSeconds, trials: run.Matches);
                }
            }
            foreach (var row in report.KillsToWin)
            {
                string who = $"{row.Kills} kills, {row.Victims} victims";
                AddRate("kills-forced", who + ", no passive", "P0", row.Win, row.Label);
                AddRate("kills-forced", who + ", with passive", "P0", row.WinWithPassive, row.Label);
            }
            return list;
        }

        /// <summary>The baseline's numbers lined up with a scenario's, in the baseline's order; a number only one of them has keeps NaN on the other side.</summary>
        public static List<WhatIfLine> Compare(IReadOnlyList<WhatIfNumber> baseline, IReadOnlyList<WhatIfNumber> scenario)
        {
            var lines = new List<WhatIfLine>();
            var byKey = new Dictionary<string, WhatIfNumber>(StringComparer.Ordinal);
            foreach (var n in scenario) byKey[n.Key] = n;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in baseline)
            {
                seen.Add(b.Key);
                byKey.TryGetValue(b.Key, out var s);
                lines.Add(new WhatIfLine
                {
                    Group = b.Group, Label = b.Label, Population = b.Population,
                    Base = b.Value, BaseMargin = b.Margin, BaseTrials = b.Trials, BaseNote = b.Note,
                    Scenario = s != null ? s.Value : float.NaN, ScenarioMargin = s != null ? s.Margin : float.NaN, ScenarioTrials = s != null ? s.Trials : 0, ScenarioNote = s?.Note,
                });
            }
            foreach (var s in scenario)
                if (!seen.Contains(s.Key))
                    lines.Add(new WhatIfLine { Group = s.Group, Label = s.Label, Population = s.Population, Scenario = s.Value, ScenarioMargin = s.Margin, ScenarioTrials = s.Trials, ScenarioNote = s.Note });
            return lines;
        }

        public static List<WhatIfLine> Lines(WhatIfOutput output, WhatIfResult scenario) =>
            scenario.Refused ? new List<WhatIfLine>() : Compare(output.Baseline.Numbers, scenario.Numbers);

        // ------------------------------------------------------------------ markdown

        private static string F(float v, string f = "0.0") => float.IsNaN(v) ? "n/a" : v.ToString(f, Inv);
        private static string D(WhatIfLine l, string f = "0.0")
        {
            if (l == null || float.IsNaN(l.Diff)) return "n/a";
            return (l.Diff >= 0 ? "+" : "") + l.Diff.ToString(f, Inv) + (l.Beyond ? "*" : "");
        }
        private static string Base(WhatIfLine l) => l == null ? "n/a" : float.IsNaN(l.BaseMargin) ? F(l.Base) : $"{F(l.Base)} +/-{F(l.BaseMargin)}";
        private static string Scn(WhatIfLine l) => l == null ? "n/a" : float.IsNaN(l.ScenarioMargin) ? F(l.Scenario) : $"{F(l.Scenario)} +/-{F(l.ScenarioMargin)}";
        private static string Short(string kit) { int i = kit.IndexOf('_'); return i >= 0 ? kit.Substring(i + 1) : kit; }

        private static WhatIfLine Find(List<WhatIfLine> lines, string group, string label, string pop) =>
            lines.FirstOrDefault(l => l.Group == group && l.Label == label && l.Population == pop);

        public static string Markdown(WhatIfOutput o)
        {
            var sb = new StringBuilder();
            var played = o.Scenarios.Where(s => !s.Refused).ToList();
            var all = played.ToDictionary(s => s.Name, s => Lines(o, s));
            var shownPops = new[] { "P0", "P1", "mixed" };

            sb.AppendLine("# Match what-if report (Balance Oracle v2, part 2)");
            sb.AppendLine();
            sb.AppendLine($"Balance `bal={o.BalanceHash}` (the shipped game), map `{o.MapFingerprint}`, run seed `0x{o.RunSeed:x16}`. {o.Scenarios.Count} scenarios ({o.Scenarios.Count(s => s.Refused)} refused), {o.MatchesPlayed} matches in {o.ElapsedSeconds / 60.0:0.0} min on {o.Threads} threads.");
            sb.AppendLine();
            sb.AppendLine("## How to read this");
            sb.AppendLine();
            sb.AppendLine($"- Every scenario plays the standard report designs (mixed, one per class, seat check under P0, P1 and the mixed population, and the forced kills) with **the same seeds as the baseline** (common random numbers), at {o.ReplicateScale:0.##} of the report's replicates. Only the balance delta differs.");
            sb.AppendLine("- Rates are the share of the seats a kit (or class) sat in that won, in points; `+/-` is the 95 % Wilson half-width. A difference marked `*` is larger than the two margins combined as if the runs were independent; the shared seeds make a difference steadier than that, so an unmarked difference is small, not proven zero.");
            sb.AppendLine("- The Spoiler's kills table here is the played one (the kills the policies really made, mixed design); survivor bias applies. The forced table (exactly k kills on schedule, P0) is the clean measure of what a kill pays.");
            sb.AppendLine("- A report line, never a test. The simulator is not calibrated against real matches, and the real Spoiler bot wastes its stun (BL-215): read the direction and the size of a step, not the absolute rate.");
            sb.AppendLine();

            // Overview: Spoiler.
            sb.AppendLine($"## Overview: the Spoiler ({o.SpoilerKit})");
            sb.AppendLine();
            sb.AppendLine("Win rate with the change from the baseline in brackets. Kills and the 3 and 4 kill rows are P1's played rows (win rate without the passive, matches in brackets).");
            sb.AppendLine();
            sb.AppendLine("| Scenario | P0 | P1 | mixed | Assassin class P1 (one per class) | kills per match P1 | 3 kills, no passive P1 | 4 kills, no passive P1 | on the clock P1 (%) | length P1 (s) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            void SpoilerRow(string name, List<WhatIfLine> ls, bool isBase)
            {
                string Rate(string pop) { var l = Find(ls, "kit", o.SpoilerKit, pop); return l == null ? "n/a" : isBase ? F(l.Base) : $"{F(l.Scenario)} ({D(l)})"; }
                string Gen(string g, string lab, string pop, string f = "0.0") { var l = Find(ls, g, lab, pop); return l == null ? "n/a" : isBase ? F(l.Base, f) : $"{F(l.Scenario, f)} ({D(l, f)})"; }
                string Kill(int k) { var l = Find(ls, "kills-played", $"{k} kills, no passive", "P1"); return l == null ? "n/a" : isBase ? $"{F(l.Base)} (n {l.BaseTrials})" : $"{F(l.Scenario)} ({D(l)}, n {l.ScenarioTrials})"; }
                string cls = Spoiler(o, ls, isBase);
                sb.AppendLine($"| {name} | {Rate("P0")} | {Rate("P1")} | {Rate("mixed")} | {cls} | {Gen("spoiler", "kills per match", "P1", "0.00")} | {Kill(3)} | {Kill(4)} | {Gen("match", "ended on the clock (%)", "P1")} | {Gen("match", "mean length (s)", "P1")} |");
            }
            SpoilerRow("baseline", Compare(o.Baseline.Numbers, o.Baseline.Numbers), true);
            foreach (var s in o.Scenarios)
            {
                if (s.Refused) { sb.AppendLine($"| {s.Name} | REFUSED: {s.Reason} | | | | | | | | |"); continue; }
                SpoilerRow(s.Name, all[s.Name], false);
            }
            sb.AppendLine();

            // Overview: Thief.
            sb.AppendLine($"## Overview: the Thief ({o.ThiefKit})");
            sb.AppendLine();
            sb.AppendLine("| Scenario | P0 | P1 | mixed | steals per match (mixed) | food stolen per match (mixed) | food banked per match (mixed) | on the clock mixed (%) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            void ThiefRow(string name, List<WhatIfLine> ls, bool isBase)
            {
                string Rate(string pop) { var l = Find(ls, "kit", o.ThiefKit, pop); return l == null ? "n/a" : isBase ? F(l.Base) : $"{F(l.Scenario)} ({D(l)})"; }
                string Gen(string g, string lab, string pop, string f = "0.0") { var l = Find(ls, g, lab, pop); return l == null ? "n/a" : isBase ? F(l.Base, f) : $"{F(l.Scenario, f)} ({D(l, f)})"; }
                sb.AppendLine($"| {name} | {Rate("P0")} | {Rate("P1")} | {Rate("mixed")} | {Gen("thief", "steals per match", "mixed", "0.00")} | {Gen("thief", "food stolen per match", "mixed")} | {Gen("thief", "food banked per match", "mixed")} | {Gen("match", "ended on the clock (%)", "mixed")} |");
            }
            ThiefRow("baseline", Compare(o.Baseline.Numbers, o.Baseline.Numbers), true);
            foreach (var s in o.Scenarios)
            {
                if (s.Refused) { sb.AppendLine($"| {s.Name} | REFUSED: {s.Reason} | | | | | | |"); continue; }
                ThiefRow(s.Name, all[s.Name], false);
            }
            sb.AppendLine();

            // Overview: the Thief's loot mark.
            if (played.Any(s => all[s.Name].Any(l => l.Group == "loot-steals")))
            {
                sb.AppendLine("## Overview: the Thief's loot mark (per match, mixed population unless said)");
                sb.AppendLine();
                sb.AppendLine("| Scenario | marks drawn | consumed | lost unpaid | refused | marked take | bounty food | bag banked | bag lost to steals | won only on the prize (%) | on a Hunt-marked bird (% Thief matches) | Thief mirror on the corner (%) |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
                foreach (var s in played)
                {
                    string G(string label, string f = "0.00") { var l = Find(all[s.Name], "thief", label, "mixed") ?? Find(all[s.Name], "match", label, "mixed"); return l == null ? "n/a" : F(l.Scenario, f); }
                    sb.AppendLine($"| {s.Name} | {G("marks drawn per match")} | {G("marks consumed per match")} | {G("marks lost unpaid per match")} | {G("marks refused per match")} | {G("marked take per match", "0.0")} | {G("bounty food per match", "0.0")} | {G("bag food banked per match", "0.0")} | {G("bag food lost to steals per match", "0.0")} | {G("won only on the prize (%)", "0.0")} | {G("loot mark on a Hunt-marked bird (% of Thief matches)", "0.0")} | {G("Thief mirror decided by the corner (%)", "0.0")} |");
                }
                sb.AppendLine();
                sb.AppendLine("## Overview: the Thief's marked steals to win (win rate, matches in brackets; IN or OUT is the label against `ThiefLootTargets`)");
                sb.AppendLine();
                sb.AppendLine("| Scenario | 2, with prize P0 | P1 | mixed | 3, no prize P0 | P1 | mixed | 4, no prize P0 | P1 | mixed |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                foreach (var s in played)
                {
                    string Row(int k, bool prize, string pop)
                    {
                        var l = Find(all[s.Name], "loot-steals", $"{k} marked steals, {(prize ? "with" : "no")} prize", pop);
                        if (l == null || float.IsNaN(l.Scenario)) return l == null ? "n/a" : $"n/a (n {l.ScenarioTrials})";
                        string note = l.ScenarioNote ?? string.Empty;
                        string verdict = note.Contains(" OUT ") ? "OUT" : note.Contains(" IN ") ? "IN" : "-";
                        return $"{F(l.Scenario)} (n {l.ScenarioTrials}) {verdict}";
                    }
                    sb.AppendLine($"| {s.Name} | {Row(2, true, "P0")} | {Row(2, true, "P1")} | {Row(2, true, "mixed")} | {Row(3, false, "P0")} | {Row(3, false, "P1")} | {Row(3, false, "mixed")} | {Row(4, false, "P0")} | {Row(4, false, "P1")} | {Row(4, false, "mixed")} |");
                }
                sb.AppendLine();
            }

            // Overview: cost to every kit, per population.
            foreach (var pop in new[] { "P1", "mixed", "P0" })
            {
                sb.AppendLine($"## Overview: what each scenario does to every kit, {pop} (points against the baseline)");
                sb.AppendLine();
                sb.AppendLine("| Scenario | " + string.Join(" | ", o.KitLabels.Select(Short)) + " |");
                sb.AppendLine("|---|" + string.Join("|", o.KitLabels.Select(_ => "---")) + "|");
                sb.AppendLine("| baseline rate | " + string.Join(" | ", o.KitLabels.Select(k => F(Find(Compare(o.Baseline.Numbers, o.Baseline.Numbers), "kit", k, pop)?.Base ?? float.NaN))) + " |");
                foreach (var s in played)
                    sb.AppendLine($"| {s.Name} | " + string.Join(" | ", o.KitLabels.Select(k => D(Find(all[s.Name], "kit", k, pop)))) + " |");
                sb.AppendLine();
            }

            // Baseline detail.
            sb.AppendLine("## Baseline (the shipped game)");
            sb.AppendLine();
            Detail(sb, o, o.Baseline, Compare(o.Baseline.Numbers, o.Baseline.Numbers), true, shownPops);

            sb.AppendLine("## Scenarios");
            foreach (var s in o.Scenarios)
            {
                sb.AppendLine();
                sb.AppendLine($"### {s.Name}");
                sb.AppendLine();
                sb.AppendLine("Delta: `" + s.DeltaJson + "`");
                sb.AppendLine();
                if (s.Refused)
                {
                    sb.AppendLine($"**Refused, not played:** {s.Reason}");
                    continue;
                }
                sb.AppendLine($"Balance `bal={s.Hash}`, played in {s.ElapsedSeconds:0.0} s.");
                sb.AppendLine();
                Detail(sb, o, s, all[s.Name], false, shownPops);
            }
            return sb.ToString();
        }

        private static string Spoiler(WhatIfOutput o, List<WhatIfLine> ls, bool isBase)
        {
            var l = ls.FirstOrDefault(x => x.Group == "class" && x.Population == "P1" && x.Label == "Assassin (one per class)");
            return l == null ? "n/a" : isBase ? F(l.Base) : $"{F(l.Scenario)} ({D(l)})";
        }

        // One scenario's tables.
        private static void Detail(StringBuilder sb, WhatIfOutput o, WhatIfResult r, List<WhatIfLine> ls, bool isBase, string[] pops)
        {
            string Cell(WhatIfLine l, bool b) => l == null ? "n/a" : b ? Base(l) : Scn(l);

            sb.AppendLine("Win rate per kit (mixed design, points):");
            sb.AppendLine();
            sb.AppendLine("| Kit | " + string.Join(" | ", pops.Select(p => isBase ? p : $"{p} base | {p} scenario | {p} diff")) + " |");
            sb.AppendLine("|---|" + string.Join("|", pops.Select(_ => isBase ? "---" : "---|---|---")) + "|");
            foreach (var k in o.KitLabels)
                sb.AppendLine($"| {k} | " + string.Join(" | ", pops.Select(p =>
                {
                    var l = Find(ls, "kit", k, p);
                    return isBase ? Cell(l, true) : $"{Cell(l, true)} | {Cell(l, false)} | {D(l)}";
                })) + " |");
            sb.AppendLine();

            var classes = ls.Where(l => l.Group == "class" && l.Population == "P0").Select(l => l.Label).ToList();
            sb.AppendLine("Win rate per class (one chicken of each class per match, then pooled over the mixed design):");
            sb.AppendLine();
            sb.AppendLine("| Class | " + string.Join(" | ", pops.Select(p => isBase ? p : $"{p} base | {p} scenario | {p} diff")) + " |");
            sb.AppendLine("|---|" + string.Join("|", pops.Select(_ => isBase ? "---" : "---|---|---")) + "|");
            foreach (var c in classes)
                sb.AppendLine($"| {c} | " + string.Join(" | ", pops.Select(p =>
                {
                    var l = Find(ls, "class", c, p);
                    return isBase ? Cell(l, true) : $"{Cell(l, true)} | {Cell(l, false)} | {D(l)}";
                })) + " |");
            sb.AppendLine();

            sb.AppendLine("Spoiler's played kills to win, P1 (mixed design; survivor bias applies). Win rates without and with the passive, and the label against the `KillTargets`:");
            sb.AppendLine();
            sb.AppendLine("| Kills | Matches | no passive base | no passive scenario | diff | with passive base | with passive scenario | diff | label |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            var rows = ls.Where(l => l.Group == "kills-played" && l.Population == "P1" && l.Label.EndsWith(", no passive")).ToList();
            foreach (var row in rows)
            {
                string k = row.Label.Substring(0, row.Label.IndexOf(' '));
                var w = Find(ls, "kills-played", $"{k} kills, with passive", "P1");
                sb.AppendLine($"| {k} | {row.BaseTrials} -> {row.ScenarioTrials} | {Base(row)} | {Scn(row)} | {D(row)} | {Base(w)} | {Scn(w)} | {D(w)} | {(isBase ? row.BaseNote : row.ScenarioNote) ?? "-"} |");
            }
            sb.AppendLine();

            sb.AppendLine("Spoiler's forced kills (exactly k kills on schedule, same victims every time, P0):");
            sb.AppendLine();
            sb.AppendLine("| Kills, victims | no passive base | no passive scenario | diff | with passive base | with passive scenario | diff | label |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var row in ls.Where(l => l.Group == "kills-forced" && l.Label.EndsWith(", no passive")))
            {
                string who = row.Label.Substring(0, row.Label.Length - ", no passive".Length);
                var w = Find(ls, "kills-forced", who + ", with passive", "P0");
                sb.AppendLine($"| {who} | {Base(row)} | {Scn(row)} | {D(row)} | {Base(w)} | {Scn(w)} | {D(w)} | {(isBase ? row.BaseNote : row.ScenarioNote) ?? "-"} |");
            }
            sb.AppendLine();

            var lootRows = ls.Where(l => l.Group == "loot-steals" && l.Population == "P1" && l.Label.EndsWith(", no prize")).ToList();
            if (lootRows.Count > 0)
            {
                foreach (var pop in new[] { "P0", "P1", "mixed" })
                {
                    sb.AppendLine($"Thief's played marked steals to win, {pop} (survivor bias applies). Win rate without and with the clock prize, label against the `ThiefLootTargets`:");
                    sb.AppendLine();
                    sb.AppendLine("| Marked steals | Matches | no prize | with prize | label |");
                    sb.AppendLine("|---|---|---|---|---|");
                    foreach (var row in ls.Where(l => l.Group == "loot-steals" && l.Population == pop && l.Label.EndsWith(", no prize")))
                    {
                        string k = row.Label.Substring(0, row.Label.IndexOf(' '));
                        var w = Find(ls, "loot-steals", $"{k} marked steals, with prize", pop);
                        sb.AppendLine($"| {k} | {(isBase ? row.BaseTrials.ToString() : row.ScenarioTrials.ToString())} | {(isBase ? Base(row) : Scn(row))} | {(isBase ? Base(w) : Scn(w))} | {(isBase ? row.BaseNote : row.ScenarioNote) ?? "-"} |");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("Other numbers (base -> scenario, difference):");
            sb.AppendLine();
            sb.AppendLine("| Number | " + string.Join(" | ", pops) + " |");
            sb.AppendLine("|---|" + string.Join("|", pops.Select(_ => "---")) + "|");
            foreach (var label in ls.Where(l => l.Group == "spoiler" || l.Group == "thief" || l.Group == "match").Select(l => (l.Group, l.Label)).Distinct())
                sb.AppendLine($"| {label.Group}: {label.Label} | " + string.Join(" | ", pops.Select(p =>
                {
                    var l = Find(ls, label.Group, label.Label, p);
                    if (l == null) return "n/a";
                    string f = label.Label.Contains("per match") && label.Group != "match" ? "0.00" : "0.0";
                    return isBase ? F(l.Base, f) : $"{F(l.Base, f)} -> {F(l.Scenario, f)} ({D(l, f)})";
                })) + " |");
            sb.AppendLine();
        }

        // ------------------------------------------------------------------ json

        private static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", Inv);
        private static string S(string s) => s == null ? "null" : BalanceJson.Quote(s);

        private static string LineJson(WhatIfLine l) =>
            $"{{\"group\":{S(l.Group)},\"label\":{S(l.Label)},\"population\":{S(l.Population)},\"base\":{N(l.Base)},\"baseMargin\":{N(l.BaseMargin)},\"baseTrials\":{l.BaseTrials},\"scenario\":{N(l.Scenario)},\"scenarioMargin\":{N(l.ScenarioMargin)},\"scenarioTrials\":{l.ScenarioTrials},\"diff\":{N(l.Diff)},\"beyondMargins\":{(l.Beyond ? "true" : "false")},\"baseNote\":{S(l.BaseNote)},\"scenarioNote\":{S(l.ScenarioNote)}}}";

        public static string Json(WhatIfOutput o)
        {
            var sb = new StringBuilder();
            sb.Append($"{{\"schema\":1,\"balanceHash\":{S(o.BalanceHash)},\"mapFingerprint\":{S(o.MapFingerprint)},\"runSeed\":{S("0x" + o.RunSeed.ToString("x16"))},\"replicateScale\":{N((float)o.ReplicateScale)},\"threads\":{o.Threads},\"matchesPlayed\":{o.MatchesPlayed},\"elapsedSeconds\":{N((float)o.ElapsedSeconds)},\"spoilerKit\":{S(o.SpoilerKit)},\"thiefKit\":{S(o.ThiefKit)}");
            sb.Append(",\"kits\":[" + string.Join(",", o.KitLabels.Select(S)) + "]");
            sb.Append(",\"baseline\":[" + string.Join(",", Compare(o.Baseline.Numbers, o.Baseline.Numbers).Select(LineJson)) + "]");
            sb.Append(",\"scenarios\":[" + string.Join(",", o.Scenarios.Select(s =>
                $"{{\"name\":{S(s.Name)},\"delta\":{S(s.DeltaJson)},\"refused\":{(s.Refused ? "true" : "false")},\"reason\":{S(s.Reason)},\"hash\":{S(s.Hash)},\"lines\":[{string.Join(",", Lines(o, s).Select(LineJson))}]}}")) + "]");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
