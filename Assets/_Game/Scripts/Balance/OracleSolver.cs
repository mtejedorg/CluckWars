using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Balance
{
    /// <summary>One value the solver may move: a field of a named asset, as a schema-1 delta names it.</summary>
    public sealed class FreeValue
    {
        /// <summary>The asset name a delta uses ("Fatty", "Snatch", "MatchConfig").</summary>
        public string Asset;
        /// <summary>The field ("CargoCapacity", "PeckCastSeconds").</summary>
        public string Field;
        /// <summary>Lower limit of the search; NaN takes the field's own [Range] / [Min].</summary>
        public float Min = float.NaN;
        /// <summary>Upper limit of the search; NaN takes the field's [Range] maximum (a field without one must state it).</summary>
        public float Max = float.NaN;
        /// <summary>Size of one move; positive.</summary>
        public float Step;

        public string Key => Asset + "." + Field;
    }

    /// <summary>
    /// How the match term of the solver searches (spec 7.1): how many replicates each stage plays, how many screened moves are confirmed, the seed.
    /// Read from the problem's <c>"match"</c> block; every key is optional. <see cref="ArenaHalf"/> and <see cref="SightArrivalRadius"/> come from the Editor
    /// (the bake and the bot prefab), never from the file.
    /// </summary>
    public sealed class MatchProblemBlock
    {
        public const ulong DefaultSeed = 20261122UL;
        /// <summary>Replicates of the Mixed design per candidate in the screen (three populations each).</summary>
        public int ScreenReplicates = 3;
        /// <summary>Replicates of the confirm and holdout runs; each replicate is one block of the paired noise estimate, so at least 2.</summary>
        public int ConfirmReplicates = 10;
        /// <summary>How many of the best screened moves go to the confirm run.</summary>
        public int ConfirmBest = 3;
        public ulong RunSeed = DefaultSeed;
        /// <summary>Worker threads of a match run; zero or less means every core. The result does not depend on it.</summary>
        public int Threads = 12;
        /// <summary>Replicates of the seat check (full mirrors, three populations) that can abort the solve.</summary>
        public int SeatCheckReplicates = 25;
        /// <summary>Play the full match report (every design, the report's replicates) for the start and the proposal, for the proposal's plain-words half. About 100 s each on 12 threads; tests turn it off.</summary>
        public bool FullReports = true;
        /// <summary>The reference asset the Editor passes in: its Spoiler kill targets label the proposal's rows and its match records give the drift lines. Null for none.</summary>
        public OracleReferenceSO Reference;
        /// <summary>The arena half-size of the bake; the Editor sets it (the asset does not store it).</summary>
        public float ArenaHalf;
        /// <summary>The arrival radius of the game's bot, which the sight rule needs; the Editor sets it.</summary>
        public float SightArrivalRadius;
        /// <summary>Receives one line per stage of a long solve (the Editor logs them); null for none. Never part of the problem file.</summary>
        public Action<string> Progress;
    }

    /// <summary>What to search and what to aim at. Everything not listed in <see cref="Free"/> stays as it is.</summary>
    public sealed class OracleProblem
    {
        public List<FreeValue> Free = new List<FreeValue>();
        /// <summary>The band the standard farmer's banked food (mean of the four seats in the four-standard-farmer run) should sit in.</summary>
        public float BandLow = 28f;
        public float BandHigh = 36f;
        public int MaxPasses = 6;
        /// <summary>
        /// When no single step of any value improves the score, the search also tries one move of 2 up to this many steps in each direction for each
        /// value, so it can cross a flat stretch of the score. 1 turns that off.
        /// </summary>
        public int MaxFlatSteps = 4;

        /// <summary>Weight of the match simulator's term in the score (<see cref="MatchSolverTerm.Score"/>, in win-rate points squared next to part 1's seconds squared). 0 leaves the solver exactly as part 1 built it and plays no match.</summary>
        public float MatchWeight;
        /// <summary>With a match term, at most this many distinct values change in one solve (spec 7.1, guard 3).</summary>
        public int MaxMovedValues = 3;
        /// <summary>The <c>"match"</c> block; null when the problem has none.</summary>
        public MatchProblemBlock Match;

        /// <summary>
        /// Reads <c>{"bandLow":28,"bandHigh":36,"maxPasses":6,"maxFlatSteps":4,"free":[{"asset":"Fatty","field":"CargoCapacity","min":10,"max":60,"step":5}]}</c>.
        /// Every key but <c>free</c> is optional, <c>min</c> and <c>max</c> too (see <see cref="FreeValue"/>); an unknown key is an error, never skipped.
        /// </summary>
        public static bool TryParse(string json, out OracleProblem problem, out string error)
        {
            problem = null;
            if (!BalanceJson.TryParse(json, out var root, out error)) { error = "not valid JSON (" + error + ")"; return false; }
            if (root.Kind != JsonKind.Object) { error = "the problem is not a JSON object"; return false; }

            var result = new OracleProblem();
            foreach (var member in root.Members)
            {
                switch (member.Key)
                {
                    case "bandLow": if (!Number(member.Value, "bandLow", out result.BandLow, out error)) return false; break;
                    case "bandHigh": if (!Number(member.Value, "bandHigh", out result.BandHigh, out error)) return false; break;
                    case "maxPasses":
                        if (!Number(member.Value, "maxPasses", out float passes, out error)) return false;
                        result.MaxPasses = (int)passes;
                        break;
                    case "maxFlatSteps":
                        if (!Number(member.Value, "maxFlatSteps", out float flat, out error)) return false;
                        result.MaxFlatSteps = (int)flat;
                        break;
                    case "match":
                        if (!ParseMatch(member.Value, result, out error)) return false;
                        break;
                    case "free":
                        if (member.Value.Kind != JsonKind.Array) { error = "\"free\" must be an array"; return false; }
                        for (int i = 0; i < member.Value.Items.Count; i++)
                            if (!ParseFree(member.Value.Items[i], i, result.Free, out error)) return false;
                        break;
                    default:
                        error = $"unknown key \"{member.Key}\"";
                        return false;
                }
            }
            if (!(result.BandLow <= result.BandHigh)) { error = "bandLow must not exceed bandHigh"; return false; }
            if (result.MaxPasses < 1) { error = "maxPasses must be 1 or more"; return false; }
            if (result.MaxFlatSteps < 1) { error = "maxFlatSteps must be 1 or more"; return false; }
            problem = result;
            error = null;
            return true;
        }

        /// <summary>Step of the Death Mark's <c>LeaderPickWeight</c>, of a pile's food, of a class's <c>PeckCastSeconds</c>, of a cooldown and of an area radius in <see cref="DefaultMatchFree"/>.</summary>
        public const float WeightStep = 0.25f, FoodStep = 1f, CastStep = 0.05f, CooldownStep = 0.25f, RadiusStep = 0.25f;

        /// <summary>
        /// The values spec 7.1 lets the match term move (D8 (b)): the Death Mark's <c>LeaderPickWeight</c>, the three pile amounts on the match config,
        /// <c>PeckCastSeconds</c> of each class's stats, and for every non-Peck ability in the fixed kits each of its <see cref="AbilityBaseSO.CooldownFields"/>
        /// and its <see cref="AbilityBaseSO.AimRadiusField"/> when it has one. A field with a [Range] takes it as its limits; with only a
        /// [Min] (and a book to read the shipped value from) its upper limit is twice the shipped value, so every entry resolves. Steps are the constants above.
        /// </summary>
        public static List<FreeValue> DefaultMatchFree(OracleInputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Abilities == null || inputs.Classes == null || inputs.Match == null)
                throw new ArgumentException("OracleInputs.Abilities, Classes and Match are required.", nameof(inputs));

            var free = new List<FreeValue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(string asset, string field, float step)
            {
                var value = new FreeValue { Asset = asset, Field = field, Step = step };
                if (!seen.Add(value.Key)) return;
                if (inputs.Book != null && inputs.Book.TryProbe(asset, field, out float shipped, out _, out float max, out _) && float.IsInfinity(max))
                    value.Max = Math.Max(shipped * 2f, shipped + step * 8f);
                free.Add(value);
            }

            var abilities = new List<AbilityBaseSO>();
            var classes = new List<ChickenClass>();
            foreach (var passive in inputs.Abilities.Passives)
            {
                if (passive == null) continue;
                var cls = passive.Subclass.ClassOf();
                if (!classes.Contains(cls)) classes.Add(cls);
                foreach (var ability in OracleKits.ResolveKit(inputs, passive))
                    if (ability != null && ability.Job != AbilityJob.Peck && !abilities.Contains(ability)) abilities.Add(ability);
            }

            foreach (var ability in abilities)
                if (ability.Job == AbilityJob.DeathMark) Add(ability.name, "LeaderPickWeight", WeightStep);
            Add(inputs.Match.name, nameof(MatchConfigSO.PersonalPileFood), FoodStep);
            Add(inputs.Match.name, nameof(MatchConfigSO.ContestedPileFood), FoodStep);
            Add(inputs.Match.name, nameof(MatchConfigSO.CentrePileFood), FoodStep);
            foreach (var cls in classes)
            {
                if (!inputs.Classes.TryGet(cls, out var entry) || entry.Stats == null)
                    throw new InvalidOperationException($"The class registry has no stats for {cls}.");
                Add(entry.Stats.name, nameof(ChickenStatsSO.PeckCastSeconds), CastStep);
            }
            foreach (var ability in abilities)
            {
                foreach (string field in ability.CooldownFields) Add(ability.name, field, CooldownStep);
                if (ability.AimRadiusField != null) Add(ability.name, ability.AimRadiusField, RadiusStep);
            }
            return free;
        }

        private static bool ParseMatch(JsonValue item, OracleProblem into, out string error)
        {
            error = null;
            if (item.Kind != JsonKind.Object) { error = "\"match\" must be an object"; return false; }
            var block = new MatchProblemBlock();
            foreach (var m in item.Members)
            {
                string at = "match." + m.Key;
                switch (m.Key)
                {
                    case "weight":
                        if (!Number(m.Value, at, out into.MatchWeight, out error)) return false;
                        if (!(into.MatchWeight >= 0f) || float.IsInfinity(into.MatchWeight)) { error = "match.weight must be 0 or more"; return false; }
                        break;
                    case "maxMovedValues": if (!WholeNumber(m.Value, at, 1, out into.MaxMovedValues, out error)) return false; break;
                    case "screenReplicates": if (!WholeNumber(m.Value, at, 1, out block.ScreenReplicates, out error)) return false; break;
                    case "confirmReplicates": if (!WholeNumber(m.Value, at, 2, out block.ConfirmReplicates, out error)) return false; break;
                    case "confirmBest": if (!WholeNumber(m.Value, at, 1, out block.ConfirmBest, out error)) return false; break;
                    case "threads": if (!WholeNumber(m.Value, at, 0, out block.Threads, out error)) return false; break;
                    case "fullReports":
                        if (m.Value.Kind != JsonKind.Bool) { error = "match.fullReports must be true or false"; return false; }
                        block.FullReports = m.Value.Bool;
                        break;
                    case "seatCheckReplicates": if (!WholeNumber(m.Value, at, 1, out block.SeatCheckReplicates, out error)) return false; break;
                    case "seed":
                        if (m.Value.Kind == JsonKind.Number && m.Value.Number >= 0 && m.Value.Number == Math.Floor(m.Value.Number) && m.Value.Number < 9007199254740992.0)
                            block.RunSeed = (ulong)m.Value.Number;
                        else if (!(m.Value.Kind == JsonKind.String && TrySeed(m.Value.Text, out block.RunSeed)))
                        {
                            error = "match.seed must be a whole number or a hexadecimal string like \"0x1234\"";
                            return false;
                        }
                        break;
                    default: error = $"match: unknown key \"{m.Key}\""; return false;
                }
            }
            into.Match = block;
            return true;
        }

        private static bool TrySeed(string text, out ulong seed)
        {
            if (text != null && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ulong.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out seed);
            return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seed);
        }

        private static bool WholeNumber(JsonValue v, string name, int min, out int value, out string error)
        {
            value = 0;
            if (!Number(v, name, out float f, out error)) return false;
            if (f != Math.Floor(f) || f < min || f > int.MaxValue) { error = $"\"{name}\" must be a whole number of {min} or more"; return false; }
            value = (int)f;
            return true;
        }

        private static bool ParseFree(JsonValue item, int index, List<FreeValue> into, out string error)
        {
            string at = $"free[{index}]";
            error = null;
            if (item.Kind != JsonKind.Object) { error = at + " is not an object"; return false; }
            var free = new FreeValue();
            bool hasAsset = false, hasField = false, hasStep = false;
            foreach (var m in item.Members)
            {
                switch (m.Key)
                {
                    case "asset": if (m.Value.Kind != JsonKind.String) { error = at + ".asset must be a string"; return false; } free.Asset = m.Value.Text; hasAsset = true; break;
                    case "field": if (m.Value.Kind != JsonKind.String) { error = at + ".field must be a string"; return false; } free.Field = m.Value.Text; hasField = true; break;
                    case "min": if (!Number(m.Value, at + ".min", out free.Min, out error)) return false; break;
                    case "max": if (!Number(m.Value, at + ".max", out free.Max, out error)) return false; break;
                    case "step": if (!Number(m.Value, at + ".step", out free.Step, out error)) return false; hasStep = true; break;
                    default: error = $"{at}: unknown key \"{m.Key}\""; return false;
                }
            }
            if (!hasAsset || !hasField || !hasStep) { error = $"{at} needs asset, field and step"; return false; }
            into.Add(free);
            return true;
        }

        private static bool Number(JsonValue v, string name, out float value, out string error)
        {
            value = 0f;
            error = null;
            if (v.Kind != JsonKind.Number) { error = $"\"{name}\" must be a number"; return false; }
            value = (float)v.Number;
            return true;
        }

        public string ToJson()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{");
            sb.Append("\"bandLow\":").Append(BandLow.ToString("R", inv));
            sb.Append(",\"bandHigh\":").Append(BandHigh.ToString("R", inv));
            sb.Append(",\"maxPasses\":").Append(MaxPasses.ToString(inv));
            sb.Append(",\"maxFlatSteps\":").Append(MaxFlatSteps.ToString(inv));
            if (Match != null)
            {
                sb.Append(",\"match\":{\"weight\":").Append(MatchWeight.ToString("R", inv));
                sb.Append(",\"maxMovedValues\":").Append(MaxMovedValues.ToString(inv));
                sb.Append(",\"screenReplicates\":").Append(Match.ScreenReplicates.ToString(inv));
                sb.Append(",\"confirmReplicates\":").Append(Match.ConfirmReplicates.ToString(inv));
                sb.Append(",\"confirmBest\":").Append(Match.ConfirmBest.ToString(inv));
                sb.Append(",\"threads\":").Append(Match.Threads.ToString(inv));
                sb.Append(",\"seatCheckReplicates\":").Append(Match.SeatCheckReplicates.ToString(inv));
                sb.Append(",\"fullReports\":").Append(Match.FullReports ? "true" : "false");
                sb.Append(",\"seed\":\"0x").Append(Match.RunSeed.ToString("x16", inv)).Append("\"}");
            }
            sb.Append(",\"free\":[");
            for (int i = 0; i < Free.Count; i++)
            {
                var f = Free[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"asset\":").Append(BalanceJson.Quote(f.Asset)).Append(",\"field\":").Append(BalanceJson.Quote(f.Field));
                if (!float.IsNaN(f.Min)) sb.Append(",\"min\":").Append(f.Min.ToString("R", inv));
                if (!float.IsNaN(f.Max)) sb.Append(",\"max\":").Append(f.Max.ToString("R", inv));
                sb.Append(",\"step\":").Append(f.Step.ToString("R", inv)).Append('}');
            }
            return sb.Append("]}").ToString();
        }
    }

    /// <summary>One free value before and after the solve.</summary>
    public sealed class ProposedValue
    {
        public string Asset;
        public string Field;
        public float Old;
        public float New;
        public bool Changed;
        /// <summary>The limits and step the search used (NaN when the solver did not report them).</summary>
        public float Lo = float.NaN, Hi = float.NaN, Step = float.NaN;
        /// <summary>The proposed value sits on one of its limits: the search wanted to go further (a sign the range, not the balance, decided it).</summary>
        public bool AtLimit;
    }

    public sealed class OracleProposal
    {
        /// <summary>A schema-1 balance delta holding every changed game value (<c>{"schema":1}</c> when none changed).</summary>
        public string DeltaJson = "{\"schema\":1}";
        public OracleReport Before;
        public OracleReport After;
        public float ScoreBefore;
        public float ScoreAfter;
        /// <summary>Every free value with its old and proposed value, in the order the problem lists them.</summary>
        public List<ProposedValue> Values = new List<ProposedValue>();
        /// <summary>How many passes ran (a pass that finds no better move ends the search and is not counted).</summary>
        public int Passes;
        /// <summary>Null unless the seat check aborted the solve (then no value changed and the proposal says why).</summary>
        public string Aborted;
        /// <summary>The moves of the match search that did not hold on fresh seeds, in plain words, last first; empty without a match term.</summary>
        public List<string> Reverted = new List<string>();
        /// <summary>What the guarded match search did; null when the problem had no match weight.</summary>
        public MatchSearchResult Match;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string ToJson()
        {
            var sb = new StringBuilder(16384);
            sb.Append("{\"scoreBefore\":").Append(N(ScoreBefore)).Append(",\"scoreAfter\":").Append(N(ScoreAfter));
            sb.Append(",\"passes\":").Append(Passes.ToString(Inv));
            sb.Append(",\"delta\":").Append(DeltaJson);
            sb.Append(",\"values\":[");
            for (int i = 0; i < Values.Count; i++)
            {
                var v = Values[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"asset\":").Append(BalanceJson.Quote(v.Asset)).Append(",\"field\":").Append(BalanceJson.Quote(v.Field));
                sb.Append(",\"old\":").Append(N(v.Old)).Append(",\"new\":").Append(N(v.New)).Append('}');
            }
            sb.Append(']');
            if (Match != null)
            {
                sb.Append(",\"matchSearch\":{\"aborted\":").Append(Aborted == null ? "null" : BalanceJson.Quote(Aborted));
                sb.Append(",\"matches\":").Append(Match.Matches.ToString(Inv)).Append(",\"seconds\":").Append(N((float)Match.ElapsedSeconds));
                sb.Append(",\"holdoutScoreStart\":").Append(N(Match.HoldoutScoreStart)).Append(",\"holdoutScoreFinal\":").Append(N(Match.HoldoutScoreFinal));
                sb.Append(",\"moves\":[");
                for (int i = 0; i < Match.Moves.Count; i++)
                {
                    var m = Match.Moves[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"pass\":").Append(m.Pass.ToString(Inv)).Append(",\"key\":").Append(BalanceJson.Quote(m.Key));
                    sb.Append(",\"from\":").Append(N(m.From)).Append(",\"to\":").Append(N(m.To));
                    sb.Append(",\"screenGain\":").Append(N(m.ScreenGain)).Append(",\"confirmGain\":").Append(N(m.ConfirmGain)).Append(",\"confirmNoise2\":").Append(N(m.ConfirmNoise2));
                    sb.Append(",\"holdoutGain\":").Append(N(m.HoldoutGain)).Append(",\"holdoutCumulativeGain\":").Append(N(m.HoldoutCumulativeGain)).Append(",\"holdoutNoise2\":").Append(N(m.HoldoutNoise2));
                    sb.Append(",\"reverted\":").Append(m.Reverted ? "true" : "false").Append('}');
                }
                sb.Append("],\"seatCheckBefore\":").Append(Match.SeatCheckBefore == null ? "null" : BalanceJson.Quote(Match.SeatCheckBefore));
                sb.Append(",\"seatCheckAfter\":").Append(Match.SeatCheckAfter == null ? "null" : BalanceJson.Quote(Match.SeatCheckAfter)).Append('}');
            }
            sb.Append(",\"before\":").Append(Before.ToJson()).Append(",\"after\":").Append(After.ToJson()).Append('}');
            return sb.ToString();
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("# Balance Oracle proposal");
            sb.AppendLine();
            sb.AppendLine("A proposal only: nothing here was applied to the game's data or published. It is the solver's best answer to the problem it was given, measured on the simulated four-chicken matches of the oracle report.");
            sb.AppendLine();
            sb.AppendLine($"- Score (lower is better): {F2(ScoreBefore)} before, {F2(ScoreAfter)} after, {Passes} pass(es)");
            sb.AppendLine($"- Balance before: `{Before.BalanceHash}`, after: `{After.BalanceHash}`");
            sb.AppendLine($"- Map bake: `{Before.MapFingerprint}`");
            sb.AppendLine();
            sb.AppendLine("## Proposed values");
            sb.AppendLine();
            bool ranges = Values.Exists(v => !float.IsNaN(v.Step));
            sb.AppendLine(ranges ? "| Value | Now | Proposed | Search range | Step |" : "| Value | Now | Proposed |");
            sb.AppendLine(ranges ? "|---|---|---|---|---|" : "|---|---|---|");
            foreach (var v in Values)
                sb.AppendLine($"| {v.Asset}.{v.Field} | {F2(v.Old)} | {F2(v.New)}{(v.Changed ? "" : " (unchanged)")}{(v.AtLimit ? " (ON THE LIMIT of its range)" : "")} |" + (ranges ? $" {F2(v.Lo)} to {F2(v.Hi)} | {F2(v.Step)} |" : ""));
            sb.AppendLine();
            sb.AppendLine("Balance delta (schema 1):");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(DeltaJson);
            sb.AppendLine("```");
            sb.AppendLine();
            if (Match != null) MatchProposalText.Append(sb, this);
            sb.AppendLine("## Four plain Warriors");
            sb.AppendLine();
            sb.AppendLine("| Seat | Before | After |");
            sb.AppendLine("|---|---|---|");
            for (int s = 0; s < Before.StandardSeatBanked.Length; s++)
                sb.AppendLine($"| {s + 1} | {F1(Before.StandardSeatBanked[s])} | {F1(After.StandardSeatBanked[s])} |");
            sb.AppendLine();
            sb.AppendLine("## Each kit");
            sb.AppendLine();
            sb.AppendLine("Result is the seconds the kit needs to bank the win target on its fastest route (seat mean, a seat that misses the goal is worked out from its income), minus the kit's offset (its price): lower is better. Food is the supporting seat mean banked when the clock ends on that route.");
            sb.AppendLine();
            sb.AppendLine("| Kit | Result before (s) | Result after (s) | Seats reaching the goal before | after | Food before | Food after |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var b in Before.Economy)
            {
                var a = After.Economy.FirstOrDefault(l => l.Label == b.Label);
                if (!b.Applicable || a == null || !a.Applicable) { sb.AppendLine($"| {b.Label} | n/a, does not forage | | | | | |"); continue; }
                sb.AppendLine($"| {b.Label} | {F1(b.Result)} | {F1(a.Result)} | {b.SeatsReachingGoal} | {a.SeatsReachingGoal} | {F1(b.BestBanked)} | {F1(a.BestBanked)} |");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", Inv);
        private static string F1(float v) => v.ToString("0.0", Inv);
        private static string F2(float v) => v.ToString("0.00", Inv);
    }

    /// <summary>
    /// Proposes values for the free values of a problem (Balance Oracle v2, spec 5). It only proposes: nothing is applied to an
    /// asset or published. Deterministic: the same inputs give the same proposal.
    /// <para>Score to minimise: the variance of the kits' <see cref="KitLine.Result"/> (seconds to the goal, offset applied) plus the squared distance of the standard
    /// farmers' banked food (mean of the four seats) outside the band. <see cref="FoodRules"/> and each field's [Range] / [Min]
    /// are hard limits: a candidate that breaks one is never scored. Search: coordinate descent, the best single move of one step
    /// per pass; when no single step improves, the best move of up to <see cref="OracleProblem.MaxFlatSteps"/> steps (so a flat stretch can be crossed);
    /// until nothing improves or <see cref="OracleProblem.MaxPasses"/> passes ran.</para>
    /// </summary>
    public static class OracleSolver
    {
        /// <summary>A move must beat the current score by more than this to count (stops float noise from moving values).</summary>
        private const float MinGain = 1e-4f;

        public static OracleProposal Solve(OracleInputs inputs, OracleProblem problem, IReadOnlyDictionary<string, float> offsets)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            if (inputs.Verdict != null && !inputs.Verdict.IsEmpty)
                throw new ArgumentException("The solver starts from the shipped assets: OracleInputs.Verdict must be null or empty.", nameof(inputs));

            var specs = Resolve(inputs, problem);
            if (problem.MatchWeight > 0f) return SolveWithMatch(inputs, problem, offsets, specs);
            var values = specs.Select(s => s.Start).ToArray();

            var current = Evaluate(inputs, problem, specs, values, offsets);
            var start = current;
            int passes = 0;
            while (passes < problem.MaxPasses)
            {
                // The best single step first; only when none improves, one longer move (2 to MaxFlatSteps steps) to cross a flat stretch.
                var best = BestMove(inputs, problem, specs, values, offsets, current.Score, 1, 1, out var bestValues);
                if (best == null && problem.MaxFlatSteps > 1)
                    best = BestMove(inputs, problem, specs, values, offsets, current.Score, 2, problem.MaxFlatSteps, out bestValues);
                if (best == null) break;
                current = best;
                values = bestValues;
                passes++;
            }

            var proposal = new OracleProposal
            {
                DeltaJson = current.DeltaJson,
                Before = start.Report,
                After = current.Report,
                ScoreBefore = start.Score,
                ScoreAfter = current.Score,
                Passes = passes,
            };
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                proposal.Values.Add(new ProposedValue
                {
                    Asset = s.Free.Asset, Field = s.Free.Field, Old = s.Start, New = values[i],
                    Changed = values[i] != s.Start,
                });
            }
            return proposal;
        }

        // ------------------------------------------------------- the match term

        /// <summary>
        /// The search of spec 7.1 with the match term (<see cref="OracleProblem.MatchWeight"/> above 0): <see cref="MatchSolverTerm.Search"/> over this problem's
        /// values, scored as part 1's score plus the weighted match term, then a full match report of the start and of the proposal for the plain-words half.
        /// </summary>
        private static OracleProposal SolveWithMatch(OracleInputs inputs, OracleProblem problem, IReadOnlyDictionary<string, float> offsets, List<Spec> specs)
        {
            var block = problem.Match ?? throw new ArgumentException("A match weight needs a \"match\" block in the problem.", nameof(problem));
            if (!(block.ArenaHalf > 0f))
                throw new ArgumentException("The match term needs MatchProblemBlock.ArenaHalf (the bake's arena half-size); the Editor menu sets it.", nameof(problem));
            if (block.ConfirmReplicates < 2) throw new ArgumentException("The confirm run needs at least two replicates (its blocks give the noise).", nameof(problem));

            var partOne = new Dictionary<string, Candidate>(StringComparer.Ordinal);
            var contexts = new Dictionary<string, MatchContext>(StringComparer.Ordinal);
            Candidate PartOne(float[] values)
            {
                string key = BuildDelta(specs, values) ?? string.Empty;
                if (!partOne.TryGetValue(key, out var c)) partOne[key] = c = Evaluate(inputs, problem, specs, values, offsets);
                return c;
            }
            MatchContext ContextOf(float[] values)
            {
                string key = BuildDelta(specs, values) ?? string.Empty;
                if (!contexts.TryGetValue(key, out var c))
                    contexts[key] = c = MatchContext.From(PartOne(values).Run, block.ArenaHalf, block.SightArrivalRadius);
                return c;
            }

            MatchEval ScoreMatch(float[] values, int replicates, ulong seed, bool wantBlocks)
            {
                var c = PartOne(values);
                if (!c.Usable) return new MatchEval { Usable = false, Score = float.PositiveInfinity };
                var context = ContextOf(values);
                var runs = MatchSolverTerm.PlayMixed(context, replicates, seed, block.Threads);
                var section = MatchReports.Build(context, runs, null);
                var e = new MatchEval { Section = section, Matches = runs.Sum(r => r.Results.Length) };
                e.Score = c.Score + problem.MatchWeight * MatchSolverTerm.Score(section);
                if (wantBlocks)
                {
                    e.Blocks = MatchSolverTerm.BlockScores(context, runs, section);
                    for (int b = 0; b < e.Blocks.Length; b++) e.Blocks[b] = c.Score + problem.MatchWeight * e.Blocks[b];
                }
                return e;
            }

            var starts = specs.Select(s => s.Start).ToArray();
            var search = MatchSolverTerm.Search(new MatchSearchSpec
            {
                Keys = specs.Select(s => s.Free.Key).ToArray(),
                Start = starts,
                Lo = specs.Select(s => s.Lo).ToArray(),
                Hi = specs.Select(s => s.Hi).ToArray(),
                Step = specs.Select(s => s.Free.Step).ToArray(),
                MaxPasses = problem.MaxPasses,
                MaxMovedValues = problem.MaxMovedValues,
                ScreenReplicates = block.ScreenReplicates,
                ConfirmReplicates = block.ConfirmReplicates,
                ConfirmBest = block.ConfirmBest,
                RunSeed = block.RunSeed,
                Evaluate = ScoreMatch,
                SeatCheck = values => MatchSolverTerm.SeatCheckText(ContextOf(values), block.SeatCheckReplicates, block.RunSeed, block.Threads),
                Progress = block.Progress,
            });

            var finalValues = search.Values;
            var start = PartOne(starts);
            var final = PartOne(finalValues);
            // The plain-words half: a full match report of the start and of the proposal (the report's designs and replicates, on the run seed).
            MatchReportSection Full(float[] values)
            {
                var context = ContextOf(values);
                block.Progress?.Invoke("Playing the full match report for " + (BuildDelta(specs, values) ?? "the shipped values") + ".");
                return MatchReports.Build(context, MatchReports.PlayStandard(context, block.RunSeed, block.Threads, false), block.Reference);
            }
            var reportBefore = start.Report;
            var reportAfter = final.Report;
            if (search.Aborted == null && block.FullReports)
            {
                reportBefore.Match = Full(starts);
                reportAfter.Match = finalValues.SequenceEqual(starts) ? reportBefore.Match : Full(finalValues);
            }

            float holdoutStart = search.HoldoutScoreStart;
            if (float.IsNaN(holdoutStart) && search.Aborted == null)
                holdoutStart = ScoreMatch(starts, block.ConfirmReplicates, MatchSolverTerm.PassSeed(block.RunSeed, problem.MaxPasses + 1), false).Score;
            if (float.IsNaN(holdoutStart)) holdoutStart = start.Score;
            var proposal = new OracleProposal
            {
                DeltaJson = final.DeltaJson,
                Before = reportBefore,
                After = reportAfter,
                ScoreBefore = holdoutStart,
                ScoreAfter = float.IsNaN(search.HoldoutScoreFinal) ? holdoutStart : search.HoldoutScoreFinal,
                Passes = search.Passes,
                Aborted = search.Aborted,
                Match = search,
            };
            foreach (var m in search.Reverted.Reverse())
                proposal.Reverted.Add($"{m.Key} {m.From:0.####} -> {m.To:0.####} (pass {m.Pass}) did not hold on fresh seeds");
            for (int i = 0; i < specs.Count; i++)
                proposal.Values.Add(new ProposedValue
                {
                    Asset = specs[i].Free.Asset, Field = specs[i].Free.Field, Old = specs[i].Start, New = finalValues[i], Changed = finalValues[i] != specs[i].Start,
                    Lo = specs[i].Lo, Hi = specs[i].Hi, Step = specs[i].Free.Step,
                    AtLimit = finalValues[i] != specs[i].Start && (finalValues[i] <= specs[i].Lo || finalValues[i] >= specs[i].Hi),
                });
            return proposal;
        }

        /// <summary>
        /// The usable move of <paramref name="fromSteps"/> to <paramref name="toSteps"/> steps of one value, either direction, that lowers the score
        /// the most (by more than <see cref="MinGain"/>); null when none does. Ties keep the first found: fewer steps, then the value listed first, then up.
        /// </summary>
        private static Candidate BestMove(OracleInputs inputs, OracleProblem problem, List<Spec> specs, float[] values,
            IReadOnlyDictionary<string, float> offsets, float currentScore, int fromSteps, int toSteps, out float[] bestValues)
        {
            Candidate best = null;
            bestValues = null;
            for (int steps = fromSteps; steps <= toSteps; steps++)
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    foreach (int direction in new[] { 1, -1 })
                    {
                        float moved = Clamp(specs[i], Round(values[i] + direction * steps * specs[i].Free.Step));
                        if (moved == values[i]) continue;
                        var trial = (float[])values.Clone();
                        trial[i] = moved;
                        var candidate = Evaluate(inputs, problem, specs, trial, offsets);
                        if (!candidate.Usable) continue;
                        if (candidate.Score < (best?.Score ?? currentScore - MinGain)) { best = candidate; bestValues = trial; }
                    }
                }
            }
            return best;
        }

        // ------------------------------------------------------------- problem

        private sealed class Spec
        {
            public FreeValue Free;
            public float Start;
            public float Lo;
            public float Hi;
        }

        private static List<Spec> Resolve(OracleInputs inputs, OracleProblem problem)
        {
            var specs = new List<Spec>(problem.Free.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var free in problem.Free)
            {
                if (free == null) throw new ArgumentException("The problem lists a null free value.", nameof(problem));
                if (!(free.Step > 0f) || float.IsInfinity(free.Step)) throw new ArgumentException($"{free.Key}: the step must be positive.", nameof(problem));
                if (!seen.Add(free.Key)) throw new ArgumentException($"{free.Key} is listed twice.", nameof(problem));

                var spec = new Spec { Free = free };
                if (inputs.Book == null) throw new ArgumentException($"{free.Key}: reading a game value needs OracleInputs.Book.", nameof(problem));
                if (!inputs.Book.TryProbe(free.Asset, free.Field, out spec.Start, out float attrMin, out float attrMax, out string why))
                    throw new ArgumentException($"{free.Key}: {why}.", nameof(problem));

                spec.Lo = float.IsNaN(free.Min) ? attrMin : Math.Max(free.Min, attrMin);
                spec.Hi = float.IsNaN(free.Max) ? attrMax : Math.Min(free.Max, attrMax);
                if (float.IsInfinity(spec.Lo) || float.IsInfinity(spec.Hi))
                    throw new ArgumentException($"{free.Key}: no {(float.IsInfinity(spec.Lo) ? "min" : "max")} given and the field has no [Range] / [Min] to take it from.", nameof(problem));
                if (spec.Lo > spec.Hi) throw new ArgumentException($"{free.Key}: the allowed range {spec.Lo} to {spec.Hi} is empty.", nameof(problem));
                spec.Start = Round(spec.Start);
                specs.Add(spec);
            }
            return specs;
        }

        // ---------------------------------------------------------- evaluation

        private sealed class Candidate
        {
            public bool Usable;
            public OracleReport Report;
            public float Score;
            public string DeltaJson;
            /// <summary>The inputs the candidate was measured on (the shipped assets with its verdict over them); the match term reads its kits from these.</summary>
            public OracleInputs Run;
        }

        private static Candidate Evaluate(OracleInputs inputs, OracleProblem problem, List<Spec> specs, float[] values,
            IReadOnlyDictionary<string, float> offsets)
        {
            string delta = BuildDelta(specs, values);
            BalanceVerdict verdict = null;
            if (inputs.Book != null && delta != null)
            {
                verdict = inputs.Book.Evaluate(delta, "oracle solver");
                // Anything the book refuses or skips is a candidate the game would not take as written: never scored.
                if (!verdict.IsAccepted || verdict.Ignored.Count > 0)
                    return new Candidate { Usable = false, DeltaJson = delta };
            }

            var run = new OracleInputs
            {
                Map = inputs.Map, Book = inputs.Book, Verdict = verdict, Abilities = inputs.Abilities, Classes = inputs.Classes,
                Match = inputs.Match, FoodOverride = inputs.FoodOverride,
            };
            var report = OracleRunner.Run(run, offsets);

            return new Candidate
            {
                Usable = FoodRules.OwnPlusOneIsNotEnough(report.Food, report.WinTarget) && FoodRules.OwnPlusTwoIsEnough(report.Food, report.WinTarget),
                Report = report,
                Score = Score(report, problem),
                DeltaJson = verdict != null && !verdict.IsEmpty ? verdict.Canonical : "{\"schema\":1}",
                Run = run,
            };
        }

        /// <summary>The delta of every game value that differs from its shipped value; null when none does.</summary>
        private static string BuildDelta(List<Spec> specs, float[] values)
        {
            var byAsset = new SortedDictionary<string, SortedDictionary<string, float>>(StringComparer.Ordinal);
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                if (values[i] == s.Start) continue;
                if (!byAsset.TryGetValue(s.Free.Asset, out var fields))
                    byAsset[s.Free.Asset] = fields = new SortedDictionary<string, float>(StringComparer.Ordinal);
                fields[s.Free.Field] = values[i];
            }
            if (byAsset.Count == 0) return null;

            var sb = new StringBuilder("{\"schema\":1,\"assets\":{");
            bool firstAsset = true;
            foreach (var asset in byAsset)
            {
                if (!firstAsset) sb.Append(',');
                firstAsset = false;
                sb.Append(BalanceJson.Quote(asset.Key)).Append(":{");
                bool firstField = true;
                foreach (var field in asset.Value)
                {
                    if (!firstField) sb.Append(',');
                    firstField = false;
                    sb.Append(BalanceJson.Quote(field.Key)).Append(':').Append(field.Value.ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append('}');
            }
            return sb.Append("}}").ToString();
        }

        /// <summary>Variance of the applicable kits' results (seconds) plus the squared distance of the standard farmers' mean food outside the band.</summary>
        public static float Score(OracleReport report, OracleProblem problem)
        {
            var results = report.Economy.Where(l => l.Applicable).Select(l => l.Result).ToArray();
            float variance = 0f;
            if (results.Length > 0)
            {
                float mean = results.Average();
                variance = results.Sum(r => (r - mean) * (r - mean)) / results.Length;
            }

            float standard = report.StandardSeatBanked.Length > 0 ? report.StandardSeatBanked.Average() : 0f;
            float outside = standard < problem.BandLow ? problem.BandLow - standard
                : standard > problem.BandHigh ? standard - problem.BandHigh
                : 0f;
            return variance + outside * outside;
        }

        private static float Clamp(Spec s, float v) => Math.Min(s.Hi, Math.Max(s.Lo, v));

        /// <summary>Four decimals: keeps repeated steps from drifting (0.1 + 0.1 + 0.1) and the delta readable.</summary>
        private static float Round(float v) => (float)Math.Round(v, 4);
    }
}
