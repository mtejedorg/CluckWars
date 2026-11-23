using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CluckWars.Gameplay;

namespace CluckWars.Balance
{
    /// <summary>One candidate scored by the match term: part 1's score plus the weighted match term, and the per-replicate scores the paired noise rests on.</summary>
    public sealed class MatchEval
    {
        /// <summary>False when the candidate breaks a hard limit (the book refuses it, the food rules fail); it is never scored.</summary>
        public bool Usable = true;
        /// <summary>Lower is better.</summary>
        public float Score;
        /// <summary>One score per replicate (block) of the run, each including part 1's constant share; null unless asked for.</summary>
        public float[] Blocks;
        public MatchReportSection Section;
        /// <summary>Matches this evaluation played.</summary>
        public int Matches;
    }

    /// <summary>Scores one set of values at <paramref name="replicates"/> replicates of the Mixed design on <paramref name="seed"/> (common random numbers: the same seed for every candidate of a pass).</summary>
    public delegate MatchEval MatchEvaluator(float[] values, int replicates, ulong seed, bool blocks);

    /// <summary>One move the search made (or tried to keep): a value one step further, with the evidence for and against it.</summary>
    public sealed class MatchMove
    {
        public int Pass;
        public int Index;
        public string Key;
        public float From;
        public float To;
        /// <summary>The gain at the screen's replicates, and at the confirm run's (current score minus the move's).</summary>
        public float ScreenGain;
        public float ConfirmGain;
        /// <summary>Twice the standard error of the paired block differences at the confirm run: the gain had to beat it.</summary>
        public float ConfirmNoise2;
        /// <summary>On fresh seeds: the gain of this move on top of the moves before it, and of all moves up to and including it against the start.</summary>
        public float HoldoutGain;
        public float HoldoutCumulativeGain;
        public float HoldoutNoise2;
        public bool Reverted;
    }

    /// <summary>What the search is given: the values and limits, the evaluator, the guard's numbers and the optional seat check.</summary>
    public sealed class MatchSearchSpec
    {
        public string[] Keys;
        public float[] Start;
        public float[] Lo;
        public float[] Hi;
        public float[] Step;
        public int MaxPasses = 6;
        public int MaxMovedValues = 3;
        public int ScreenReplicates = 3;
        public int ConfirmReplicates = 10;
        public int ConfirmBest = 3;
        public ulong RunSeed = 1UL;
        public MatchEvaluator Evaluate;
        /// <summary>Plays the seat check on a set of values; returns null when every seat is within its margin, else what is flagged. Null skips the check.</summary>
        public Func<float[], string> SeatCheck;
        public Action<string> Progress;
    }

    public sealed class MatchSearchResult
    {
        public float[] StartValues;
        public float[] Values;
        public List<MatchMove> Moves = new List<MatchMove>();
        /// <summary>Passes that kept a move.</summary>
        public int Passes;
        /// <summary>Null unless the seat check at the start was flagged: then nothing was searched and no value changed.</summary>
        public string Aborted;
        public string SeatCheckBefore;
        public string SeatCheckAfter;
        /// <summary>Scores of the start and of the final values on the holdout seeds (fresh, never searched on); NaN when no move was made.</summary>
        public float HoldoutScoreStart = float.NaN;
        public float HoldoutScoreFinal = float.NaN;
        public int Matches;
        public int Evaluations;
        public double ElapsedSeconds;
        public List<string> Trace = new List<string>();

        public IEnumerable<MatchMove> Kept => Moves.Where(m => !m.Reverted);
        public IEnumerable<MatchMove> Reverted => Moves.Where(m => m.Reverted);
    }

    /// <summary>
    /// The match simulator's term in the solver's score and the guarded search that uses it (spec 7.1). The score is the squared distance of every
    /// kit's and class's win rate from 25 % beyond one interval half-width, in win-rate points, under each of the three policy populations
    /// (D4: a kit's call needs P0, P1 and the mixed population to agree, so no population is optimised alone, and a kit the populations disagree on adds nothing).
    /// </summary>
    public static class MatchSolverTerm
    {
        /// <summary>A move must beat the current score by more than this to count (float noise).</summary>
        public const float MinGain = 1e-4f;

        private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ score

        private sealed class Shape
        {
            public bool[] KitSkip;
            public float[][] KitHalf;
            public float[][] KitRate;
            public ChickenClass[] Classes;
            public bool[] ClassSkip;
            public float[][] ClassHalf;
            public float[][] ClassRate;
        }

        private static Shape ShapeOf(MatchReportSection s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var shape = new Shape
            {
                KitSkip = new bool[s.Kits.Count], KitHalf = new float[3][], KitRate = new float[3][],
                Classes = s.Classes.Select(c => c.Class).ToArray(), ClassSkip = new bool[s.Classes.Count], ClassHalf = new float[3][], ClassRate = new float[3][],
            };
            for (int p = 0; p < 3; p++)
            {
                shape.KitHalf[p] = new float[s.Kits.Count]; shape.KitRate[p] = new float[s.Kits.Count];
                shape.ClassHalf[p] = new float[s.Classes.Count]; shape.ClassRate[p] = new float[s.Classes.Count];
            }
            for (int k = 0; k < s.Kits.Count; k++)
            {
                var line = s.Kits[k];
                shape.KitSkip[k] = line.Call == KitCall.PolicySensitive || line.Call == KitCall.NoCall;
                var w = new[] { line.Win, line.WinP1, line.WinMixed };
                for (int p = 0; p < 3; p++) { shape.KitRate[p][k] = w[p].Rate; shape.KitHalf[p][k] = Half(w[p]); }
            }
            for (int c = 0; c < s.Classes.Count; c++)
            {
                var line = s.Classes[c];
                var w = new[] { line.Pooled, line.PooledP1, line.PooledMixed };
                var call = MatchRobustness.Call(w[0], w[1], w[2]);
                shape.ClassSkip[c] = call == KitCall.PolicySensitive || call == KitCall.NoCall;
                for (int p = 0; p < 3; p++) { shape.ClassRate[p][c] = w[p].Rate; shape.ClassHalf[p][c] = Half(w[p]); }
            }
            return shape;
        }

        private static float Half(WinInterval w) => w.Trials == 0 ? 0f : (w.High - w.Low) * 0.5f;

        // Points of win rate by which a rate lies beyond the dead zone around the target, squared; a population with no data adds nothing.
        private static float Excess2(float rate, float half)
        {
            if (float.IsNaN(rate)) return 0f;
            float beyond = Math.Max(0f, Math.Abs(rate - MatchReports.Target) - half) * 100f;
            return beyond * beyond;
        }

        private static float Total(Shape shape, float[][] kitRate, float[][] classRate)
        {
            float total = 0f;
            for (int k = 0; k < shape.KitSkip.Length; k++)
                if (!shape.KitSkip[k]) total += Mean(p => Excess2(kitRate[p][k], shape.KitHalf[p][k]), kitRate, k);
            for (int c = 0; c < shape.ClassSkip.Length; c++)
                if (!shape.ClassSkip[c]) total += Mean(p => Excess2(classRate[p][c], shape.ClassHalf[p][c]), classRate, c);
            return total;
        }

        // The mean over the populations that have a rate for the item.
        private static float Mean(Func<int, float> term, float[][] rates, int item)
        {
            float sum = 0f;
            int n = 0;
            for (int p = 0; p < 3; p++)
            {
                if (float.IsNaN(rates[p][item])) continue;
                sum += term(p);
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>
        /// The match term of one report (kits and classes, three populations, the mixed design's rates): the squared distance of each win rate from
        /// <see cref="MatchReports.Target"/> in points beyond one interval half-width, averaged over the populations, summed over the kits and classes.
        /// A kit tagged policy-sensitive or no-call adds nothing, and neither does a class whose three populations disagree about its band.
        /// </summary>
        public static float Score(MatchReportSection match)
        {
            var shape = ShapeOf(match);
            return Total(shape, shape.KitRate, shape.ClassRate);
        }

        /// <summary>
        /// One score per replicate of the runs (P0, P1 and mixed, in that order, all over the Mixed design): the same term, with each replicate's own win
        /// rates, the dead zones and the skipped kits taken from <paramref name="reference"/> (the report of the whole run). Each replicate is a full mixed
        /// design, so a block is a small copy of the run and the spread between blocks is the noise a gain has to beat.
        /// </summary>
        public static float[] BlockScores(MatchContext context, IReadOnlyList<MatchRunOutput> runs, MatchReportSection reference)
        {
            if (runs == null || runs.Count != 3) throw new ArgumentException("Block scores need the P0, P1 and mixed runs.", nameof(runs));
            var shape = ShapeOf(reference);
            int blocks = 0;
            foreach (var cell in runs[0].Cells) blocks = Math.Max(blocks, cell.Replicate + 1);
            var scores = new float[blocks];
            int kits = shape.KitSkip.Length, classes = shape.ClassSkip.Length;

            for (int b = 0; b < blocks; b++)
            {
                var kitRate = new float[3][];
                var classRate = new float[3][];
                for (int p = 0; p < 3; p++)
                {
                    var run = runs[p];
                    var appear = new int[kits]; var wins = new int[kits];
                    var classAppear = new int[classes]; var classWins = new int[classes];
                    for (int i = 0; i < run.Cells.Length; i++)
                    {
                        if (run.Cells[i].Replicate != b) continue;
                        var seats = run.Cells[i].KitOfSeat;
                        for (int s = 0; s < seats.Length; s++)
                        {
                            if (seats[s] < 0) continue;
                            int c = Array.IndexOf(shape.Classes, context.Kits[seats[s]].Class);
                            appear[seats[s]]++; classAppear[c]++;
                            if (run.Results[i].Winner == s) { wins[seats[s]]++; classWins[c]++; }
                        }
                    }
                    kitRate[p] = appear.Select((n, k) => n > 0 ? wins[k] / (float)n : float.NaN).ToArray();
                    classRate[p] = classAppear.Select((n, c) => n > 0 ? classWins[c] / (float)n : float.NaN).ToArray();
                }
                scores[b] = Total(shape, kitRate, classRate);
            }
            return scores;
        }

        // ---------------------------------------------------------------- the guard

        /// <summary>
        /// The seed every candidate of a pass plays on (common random numbers): a function of the run seed and the pass only, so a difference between a
        /// candidate and the current point is the change and not the dice. Each pass has its own; the holdout uses pass <c>MaxPasses + 1</c>.
        /// </summary>
        public static ulong PassSeed(ulong runSeed, int pass)
        {
            ulong state = runSeed + 0x9E3779B97F4A7C15UL * (ulong)(pass + 1);
            return MatchRng.SplitMix64(ref state);
        }

        /// <summary>The standard error of the mean of the paired block differences (sample variance over the blocks); infinity with fewer than two blocks.</summary>
        public static float StandardError(IReadOnlyList<float> blockDiffs)
        {
            int n = blockDiffs.Count;
            if (n < 2) return float.PositiveInfinity;
            double mean = 0.0;
            foreach (float d in blockDiffs) mean += d;
            mean /= n;
            double ss = 0.0;
            foreach (float d in blockDiffs) ss += (d - mean) * (d - mean);
            return (float)Math.Sqrt(ss / (n - 1) / n);
        }

        /// <summary>A move counts only when its gain beats <paramref name="minGain"/> and twice the standard error of the paired block differences.</summary>
        public static bool Accepts(float gain, IReadOnlyList<float> blockDiffs, float minGain)
        {
            if (!(gain > minGain)) return false;
            return gain > 2f * StandardError(blockDiffs);
        }

        /// <summary>The values still free: all of them until <paramref name="maxMovedValues"/> distinct ones have moved, then only the moved ones.</summary>
        public static int[] FreeIndices(IReadOnlyList<bool> moved, int maxMovedValues)
        {
            int count = 0;
            for (int i = 0; i < moved.Count; i++) if (moved[i]) count++;
            var free = new List<int>(moved.Count);
            for (int i = 0; i < moved.Count; i++) if (count < maxMovedValues || moved[i]) free.Add(i);
            return free.ToArray();
        }

        /// <summary>
        /// How many of the last moves to revert after the holdout. <paramref name="holdoutGains"/>[j] is the gain of the first j + 1 moves together against
        /// the start, on fresh seeds, and <paramref name="thresholds"/>[j] what it had to beat; the last move goes first, until the moves left hold.
        /// </summary>
        public static int RevertCount(IReadOnlyList<float> holdoutGains, IReadOnlyList<float> thresholds)
        {
            if (holdoutGains.Count != thresholds.Count) throw new ArgumentException("One threshold per gain.", nameof(thresholds));
            int keep = holdoutGains.Count;
            while (keep > 0 && !(holdoutGains[keep - 1] > thresholds[keep - 1])) keep--;
            return holdoutGains.Count - keep;
        }

        // ----------------------------------------------------------------- search

        /// <summary>
        /// The guarded search of spec 7.1: per pass a screen of every one-step move at few replicates, a confirm of the best few at many with a paired noise
        /// estimate, the best kept move taken; at most <see cref="MatchSearchSpec.MaxMovedValues"/> distinct values change; a holdout on fresh seeds reverts the
        /// moves that do not hold, last first; a seat check flagged at the start aborts. Deterministic for the evaluator it is given.
        /// </summary>
        public static MatchSearchResult Search(MatchSearchSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            int n = spec.Start.Length;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = new MatchSearchResult { StartValues = (float[])spec.Start.Clone(), Values = (float[])spec.Start.Clone() };
            void Say(string line) { result.Trace.Add(line); spec.Progress?.Invoke(line); }
            MatchEval Eval(float[] v, int reps, ulong seed, bool blocks)
            {
                var e = spec.Evaluate(v, reps, seed, blocks);
                result.Matches += e.Matches;
                result.Evaluations++;
                return e;
            }

            result.SeatCheckBefore = spec.SeatCheck?.Invoke(result.Values);
            if (result.SeatCheckBefore != null)
            {
                result.Aborted = "The seat check is flagged at the start, so a seat bias of the model or the map would be read as balance: " + result.SeatCheckBefore;
                Say(result.Aborted);
                result.ElapsedSeconds = clock.Elapsed.TotalSeconds;
                return result;
            }

            var moved = new bool[n];
            var values = (float[])spec.Start.Clone();
            for (int pass = 0; pass < spec.MaxPasses; pass++)
            {
                var free = FreeIndices(moved, spec.MaxMovedValues);
                if (free.Length == 0) break;
                ulong seed = PassSeed(spec.RunSeed, pass);

                var current = Eval(values, spec.ScreenReplicates, seed, false);
                if (!current.Usable) throw new InvalidOperationException("The values the search stands on are not usable.");
                var screened = new List<(int Index, float To, float Score)>();
                foreach (int i in free)
                    foreach (int direction in new[] { 1, -1 })
                    {
                        float to = Math.Min(spec.Hi[i], Math.Max(spec.Lo[i], (float)Math.Round(values[i] + direction * spec.Step[i], 4)));
                        if (to == values[i]) continue;
                        var trial = (float[])values.Clone();
                        trial[i] = to;
                        var e = Eval(trial, spec.ScreenReplicates, seed, false);
                        if (e.Usable && e.Score < current.Score - MinGain) screened.Add((i, to, e.Score));
                    }
                Say(Inv($"pass {pass + 1}: screened {free.Length * 2} moves at R {spec.ScreenReplicates}, {screened.Count} improve on {current.Score:0.00}."));
                if (screened.Count == 0) break;

                var best = screened.Select((c, order) => (c, order)).OrderBy(t => t.c.Score).ThenBy(t => t.order).Take(spec.ConfirmBest).Select(t => t.c).ToList();
                var confirmed = Eval(values, spec.ConfirmReplicates, seed, true);
                MatchMove pick = null;
                float[] pickValues = null;
                foreach (var c in best)
                {
                    var trial = (float[])values.Clone();
                    trial[c.Index] = c.To;
                    var e = Eval(trial, spec.ConfirmReplicates, seed, true);
                    if (!e.Usable) continue;
                    float gain = confirmed.Score - e.Score;
                    var diffs = new float[confirmed.Blocks.Length];
                    for (int b = 0; b < diffs.Length; b++) diffs[b] = confirmed.Blocks[b] - e.Blocks[b];
                    float noise2 = 2f * StandardError(diffs);
                    bool kept = Accepts(gain, diffs, MinGain);
                    Say(Inv($"pass {pass + 1}: {spec.Keys[c.Index]} {values[c.Index]:0.####} -> {c.To:0.####}: gain {gain:0.00} against noise {noise2:0.00} at R {spec.ConfirmReplicates}, {(kept ? "kept" : "dropped")}."));
                    if (!kept || (pick != null && gain <= pick.ConfirmGain)) continue;
                    pick = new MatchMove
                    {
                        Pass = pass + 1, Index = c.Index, Key = spec.Keys[c.Index], From = values[c.Index], To = c.To,
                        ScreenGain = current.Score - c.Score, ConfirmGain = gain, ConfirmNoise2 = noise2,
                    };
                    pickValues = trial;
                }
                if (pick == null) break;
                result.Moves.Add(pick);
                moved[pick.Index] = true;
                values = pickValues;
                result.Passes++;
            }

            if (result.Moves.Count > 0) Holdout(spec, result, Say, Eval);
            result.Values = FinalValues(spec.Start, result.Moves);
            result.SeatCheckAfter = spec.SeatCheck?.Invoke(result.Values);
            result.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            return result;
        }

        // Fresh seeds: every prefix of the moves against the start. The moves left after RevertCount are the ones that hold.
        private static void Holdout(MatchSearchSpec spec, MatchSearchResult result, Action<string> say, Func<float[], int, ulong, bool, MatchEval> evaluate)
        {
            ulong seed = PassSeed(spec.RunSeed, spec.MaxPasses + 1);
            var states = new List<MatchEval> { evaluate(spec.Start, spec.ConfirmReplicates, seed, true) };
            var prefix = (float[])spec.Start.Clone();
            foreach (var move in result.Moves)
            {
                prefix[move.Index] = move.To;
                states.Add(evaluate((float[])prefix.Clone(), spec.ConfirmReplicates, seed, true));
            }

            var gains = new float[result.Moves.Count];
            var thresholds = new float[result.Moves.Count];
            for (int j = 0; j < gains.Length; j++)
            {
                var diffs = new float[states[0].Blocks.Length];
                for (int b = 0; b < diffs.Length; b++) diffs[b] = states[0].Blocks[b] - states[j + 1].Blocks[b];
                gains[j] = states[0].Score - states[j + 1].Score;
                thresholds[j] = Math.Max(MinGain, 2f * StandardError(diffs));
                result.Moves[j].HoldoutCumulativeGain = gains[j];
                result.Moves[j].HoldoutNoise2 = thresholds[j];
                result.Moves[j].HoldoutGain = states[j].Score - states[j + 1].Score;
            }
            int revert = RevertCount(gains, thresholds);
            for (int j = gains.Length - revert; j < gains.Length; j++) result.Moves[j].Reverted = true;
            result.HoldoutScoreStart = states[0].Score;
            result.HoldoutScoreFinal = states[states.Count - 1 - revert].Score;
            say(Inv($"holdout on fresh seeds: {gains.Length} move(s), {revert} reverted; score {result.HoldoutScoreStart:0.00} -> {result.HoldoutScoreFinal:0.00}."));
        }

        /// <summary>The start values with the moves that were not reverted applied in order.</summary>
        public static float[] FinalValues(IReadOnlyList<float> start, IEnumerable<MatchMove> moves)
        {
            var values = start.ToArray();
            foreach (var m in moves) if (!m.Reverted) values[m.Index] = m.To;
            return values;
        }

        // ------------------------------------------------------------ playing runs

        /// <summary>The three populations over the Mixed design at <paramref name="replicates"/>, in the order P0, P1, mixed (what <see cref="BlockScores"/> and the report's calls need).</summary>
        public static List<MatchRunOutput> PlayMixed(MatchContext context, int replicates, ulong seed, int threads)
        {
            var runs = new List<MatchRunOutput>(3);
            foreach (var population in new[] { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed })
                runs.Add(MatchRunner.Run(context, new MatchRunSpec
                {
                    Design = MatchDesignKind.Mixed, Replicates = replicates, Population = population, RunSeed = seed, Threads = threads,
                }));
            return runs;
        }

        /// <summary>
        /// Plays the seat check (every kit four times, a full mirror) under the three populations and returns what is flagged, or null when every seat is
        /// within its margin. A flag is a model or map bug, never balance.
        /// </summary>
        public static string SeatCheckText(MatchContext context, int replicates, ulong seed, int threads)
        {
            var runs = new List<MatchRunOutput>();
            foreach (var population in new[] { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed })
                runs.Add(MatchRunner.Run(context, new MatchRunSpec
                {
                    Design = MatchDesignKind.SeatCheck, Replicates = replicates, Population = population, RunSeed = seed, Threads = threads,
                }));
            var report = MatchReports.Build(context, runs, null);
            return FlaggedSeats(report);
        }

        /// <summary>The flagged seats of a report's seat checks as one sentence; null when none is flagged.</summary>
        public static string FlaggedSeats(MatchReportSection report)
        {
            var parts = new List<string>();
            foreach (var section in report.Policies.Count > 0 ? report.Policies : new List<MatchReportSection> { report })
                foreach (var seat in section.SeatCheck)
                    if (seat.Flagged)
                        parts.Add(Inv($"{section.Population} seat {seat.Seat} wins {seat.WinExCorner.Rate * 100f:0.0} % (interval {seat.WinExCorner.Low * 100f:0.0} to {seat.WinExCorner.High * 100f:0.0} %)"));
            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }
    }

    /// <summary>The plain-words half of a solver proposal that used the match term: values, moves, win rates before and after, the Spoiler table.</summary>
    public static class MatchProposalText
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string F(float v, string format = "0.00") => float.IsNaN(v) || float.IsInfinity(v) ? "n/a" : v.ToString(format, Inv);
        private static string Pct(float v) => float.IsNaN(v) ? "n/a" : (v * 100f).ToString("0.0", Inv);
        private static float Margin(WinInterval w) => (w.High - w.Low) * 50f;
        private static string Cell(WinInterval before, WinInterval after) =>
            before.Trials == 0 || after.Trials == 0 ? "n/a" : $"{Pct(before.Rate)} (+/-{F(Margin(before), "0.0")}) to {Pct(after.Rate)} (+/-{F(Margin(after), "0.0")})";
        private static string Name(KitCall c) => c == KitCall.PolicySensitive ? "policy-sensitive" : c.ToString();

        public static void Append(StringBuilder sb, OracleProposal p)
        {
            var m = p.Match;
            var before = p.Before.Match;
            var after = p.After.Match;
            sb.AppendLine("## The match search");
            sb.AppendLine();
            if (m.Aborted != null)
            {
                sb.AppendLine("**The solve was aborted and no value changed.** " + m.Aborted);
                sb.AppendLine();
                return;
            }
            sb.AppendLine($"The score is part 1's score (seconds squared) plus the match term: the squared distance, in win-rate points, of every kit's and class's rate from 25 % beyond one interval half-width, under P0, P1 and the mixed population, averaged over the three. A kit the three populations disagree on adds nothing, so no kit is tuned to a single policy (and a kit that becomes policy-sensitive stops counting, which the search cannot tell from a fix: see below). Played: {m.Evaluations} scored candidates, {m.Matches} matches, {F((float)(m.ElapsedSeconds / 60.0), "0.0")} minutes.");
            sb.AppendLine();
            sb.AppendLine($"Score on fresh seeds (the holdout, never searched on): {F(m.HoldoutScoreStart)} before, {F(m.HoldoutScoreFinal)} after the moves that held.");
            sb.AppendLine();
            sb.AppendLine("### Moves, in the order they were made");
            sb.AppendLine();
            if (m.Moves.Count == 0) sb.AppendLine("No move beat the guard (the gain over twice the paired noise), so the proposal changes nothing.");
            else
            {
                sb.AppendLine("| Pass | Value | From | To | Gain at the confirm run | Had to beat | Gain on fresh seeds (this move) | Together with the moves before | Had to beat | Verdict |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                foreach (var mv in m.Moves)
                    sb.AppendLine($"| {mv.Pass} | {mv.Key} | {F(mv.From, "0.####")} | {F(mv.To, "0.####")} | {F(mv.ConfirmGain)} | {F(mv.ConfirmNoise2)} | {F(mv.HoldoutGain)} | {F(mv.HoldoutCumulativeGain)} | {F(mv.HoldoutNoise2)} | {(mv.Reverted ? "REVERTED: did not hold on fresh seeds" : "held")} |");
                sb.AppendLine();
                sb.AppendLine(m.Reverted.Any()
                    ? "Reverted after the holdout: " + string.Join("; ", m.Reverted.Reverse().Select(r => r.Key)) + "."
                    : "No move was reverted: every move held on fresh seeds.");
            }
            sb.AppendLine();
            sb.AppendLine($"Seat check (a flag is a model bug, not balance; with twelve seat lines at 95 % one flag by chance is likely at this many matches): before {m.SeatCheckBefore ?? "all seats within their margin"}; after {m.SeatCheckAfter ?? "all seats within their margin"}.");
            sb.AppendLine();

            if (before == null || after == null) return;
            sb.AppendLine("### Win rate per kit, before and after (percent of the seats the kit sat in, with its margin in points)");
            sb.AppendLine();
            sb.AppendLine("| Kit | P0 | P1 | Mixed | Call before | Call after |");
            sb.AppendLine("|---|---|---|---|---|---|");
            for (int k = 0; k < before.Kits.Count; k++)
            {
                var b = before.Kits[k]; var a = after.Kits[k];
                sb.AppendLine($"| {b.Label} | {Cell(b.Win, a.Win)} | {Cell(b.WinP1, a.WinP1)} | {Cell(b.WinMixed, a.WinMixed)} | {Name(b.Call)} | {Name(a.Call)} |");
            }
            sb.AppendLine();
            sb.AppendLine("### Win rate per class, before and after (headline: one kit per class; pooled: a seat of the class in the mixed design)");
            sb.AppendLine();
            sb.AppendLine("| Class | Headline P0 | Headline P1 | Headline mixed | Pooled P0 | Pooled P1 | Pooled mixed |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            for (int c = 0; c < before.Classes.Count; c++)
            {
                var b = before.Classes[c]; var a = after.Classes[c];
                sb.AppendLine($"| {b.Class} | {Cell(b.Headline, a.Headline)} | {Cell(b.HeadlineP1, a.HeadlineP1)} | {Cell(b.HeadlineMixed, a.HeadlineMixed)} | {Cell(b.Pooled, a.Pooled)} | {Cell(b.PooledP1, a.PooledP1)} | {Cell(b.PooledMixed, a.PooledMixed)} |");
            }
            sb.AppendLine();

            sb.AppendLine("### Kits still policy-sensitive (the populations disagree, so these were not tuned)");
            sb.AppendLine();
            var sensitive = after.Kits.Where(k => k.Call == KitCall.PolicySensitive).ToList();
            if (sensitive.Count == 0) sb.AppendLine("None.");
            foreach (var k in sensitive) sb.AppendLine($"- {k.Label}: P0 {Pct(k.Win.Rate)} %, P1 {Pct(k.WinP1.Rate)} %, mixed {Pct(k.WinMixed.Rate)} %.");
            var changed = new List<string>();
            for (int k = 0; k < before.Kits.Count; k++)
                if ((before.Kits[k].Call == KitCall.PolicySensitive) != (after.Kits[k].Call == KitCall.PolicySensitive))
                    changed.Add($"{before.Kits[k].Label} went from {Name(before.Kits[k].Call)} to {Name(after.Kits[k].Call)}");
            if (changed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A kit that becomes policy-sensitive stops counting in the score, so read these as possible gaming of the term rather than as a fix: " + string.Join("; ", changed) + ".");
            }
            sb.AppendLine();

            AppendMovements(sb, before, after);

            sb.AppendLine("### Spoiler: win rate by kills granted (forced kills, P0 on the other seats)");
            sb.AppendLine();
            sb.AppendLine("Win rate of the Spoiler's seat without the passive's end-of-clock bonus, then with it, for each number of kills granted and for victims that carry nothing (empty) or a full load (loaded). The label compares the without-passive rate with the targets on the reference (they label a row, never gate).");
            sb.AppendLine();
            sb.AppendLine("| Kills | Victims | Without passive before | after | With passive before | after | Label before | Label after |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < before.KillsToWin.Count && i < after.KillsToWin.Count; i++)
            {
                var b = before.KillsToWin[i]; var a = after.KillsToWin[i];
                sb.AppendLine($"| {b.Kills} | {b.Victims} | {Pct(b.Win.Rate)} | {Pct(a.Win.Rate)} | {Pct(b.WinWithPassive.Rate)} | {Pct(a.WinWithPassive.Rate)} | {b.Label ?? "no target"} | {a.Label ?? "no target"} |");
            }
            sb.AppendLine();
        }

        // What the moves did beyond their margins: kits that went further from 25 %, and kits whose populations moved apart.
        private static void AppendMovements(StringBuilder sb, MatchReportSection before, MatchReportSection after)
        {
            sb.AppendLine("### What the moves did that the score may hide");
            sb.AppendLine();
            var worse = new List<string>();
            var apart = new List<string>();
            for (int k = 0; k < before.Kits.Count; k++)
            {
                var b = before.Kits[k]; var a = after.Kits[k];
                var pairs = new[] { ("P0", b.Win, a.Win), ("P1", b.WinP1, a.WinP1), ("mixed", b.WinMixed, a.WinMixed) };
                var deltas = pairs.Select(t => (Name: t.Item1, Delta: (t.Item3.Rate - t.Item2.Rate) * 100f,
                    Margin: (float)Math.Sqrt(Margin(t.Item2) * Margin(t.Item2) + Margin(t.Item3) * Margin(t.Item3)),
                    Away: (Math.Abs(t.Item3.Rate - MatchReports.Target) - Math.Abs(t.Item2.Rate - MatchReports.Target)) * 100f)).ToArray();
                var mixed = deltas[2];
                if (mixed.Away > mixed.Margin) worse.Add($"{b.Label} moved {F(mixed.Away, "0.0")} points further from 25 % in the mixed population");
                var up = deltas.Where(d => d.Delta > d.Margin).Select(d => d.Name).ToArray();
                var down = deltas.Where(d => d.Delta < -d.Margin).Select(d => d.Name).ToArray();
                if (up.Length > 0 && down.Length > 0)
                    apart.Add($"{b.Label}: up under {string.Join(" and ", up)}, down under {string.Join(" and ", down)}");
            }
            sb.AppendLine("Kits that moved further from 25 % in the mixed population by more than the margin: " + (worse.Count > 0 ? string.Join("; ", worse) : "none") + ".");
            sb.AppendLine();
            sb.AppendLine("Kits one population moved up and another down by more than their margins (a move that helps one policy and hurts another): " + (apart.Count > 0 ? string.Join("; ", apart) : "none") + ".");
            sb.AppendLine();
        }
    }
}
