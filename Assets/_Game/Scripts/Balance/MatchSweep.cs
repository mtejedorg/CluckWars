using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace CluckWars.Balance
{
    /// <summary>The sweep of the match simulator (spec 4.3): how many hypercube points, how often each cell repeats, the seed, the leader weights.</summary>
    public sealed class MatchSweepSpec
    {
        public int Points = 16;
        public ulong RunSeed = 1UL;
        public int Replicates = 3;
        /// <summary>Worker threads; zero or less means every core.</summary>
        public int Threads = 12;
        /// <summary>The Death Mark's <c>LeaderPickWeight</c> values to try (its authored range, 1 to 4); each goes through <see cref="BalanceBook"/> like any other value.</summary>
        public float[] LeaderPickWeights = { 1f, 1.5f, 2f, 3f, 4f };
        /// <summary>The reaction delays of the one-factor table, as seconds: the bot (none), a quick human, a slow human.</summary>
        public float[] Delays = { 0f, 0.1f, 0.3f };
    }

    /// <summary>One axis of the hypercube and the effect of the whole axis on one kit's win rate, in points (summary of the sweep, spec 4.3).</summary>
    public sealed class SensitivityLine
    {
        public PolicyPopulation Population;
        public string Parameter;
        public string Kit;
        /// <summary>Win-rate points the kit moves over the axis's whole range (signed for a number: positive when a larger value helps the kit); for a choice, the spread between its best and worst level (never negative).</summary>
        public float Effect;
    }

    /// <summary>What the sweep measured. Each report holds the three populations of one set of parameters (the Mixed design at the sweep's replicates).</summary>
    public sealed class MatchSweepOutput
    {
        public PolicyParams[] Points;
        public MatchReportSection[] Reports;
        public MatchReportSection Default;
        public float[] Delays;
        public MatchReportSection[] DelayReports;
        public float[] LeaderWeights;
        /// <summary>One report per entry of <see cref="LeaderWeights"/>, at the default parameters; empty when the inputs hold no book to apply a delta with.</summary>
        public MatchReportSection[] LeaderWeightReports = new MatchReportSection[0];
        public List<SensitivityLine> Sensitivity = new List<SensitivityLine>();
        public string[] KitLabels;
        public int Matches;
        public double ElapsedSeconds;
        public string Note;
    }

    /// <summary>The Latin hypercube over the policy parameters and the runs it drives.</summary>
    public static class MatchSweep
    {
        public const int AxisCount = 6;
        public static readonly string[] AxisNames = { "reaction delay (s)", "return threshold", "flee radius (m)", "hunt detour (m)", "target rule", "route" };
        public const float DelayMin = 0f, DelayMax = 0.6f;
        public const float ReturnMin = 0.4f, ReturnMax = 1.0f;
        public const float FleeMin = 6f, FleeMax = 15f;
        public const float DetourMin = 0f, DetourMax = 15f;
        /// <summary>The target rules swept: nearest, richest, leader (the bot's own mix is the baseline, not an axis level).</summary>
        public static readonly TargetRule[] TargetLevels = { TargetRule.Nearest, TargetRule.Richest, TargetRule.Leader };
        public static readonly FarmRoute[] RouteLevels = { FarmRoute.Nearest, FarmRoute.SidesFirst, FarmRoute.CentreFirst, FarmRoute.EasySideThenCentre };

        private const ulong AxisStream = 200UL;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// <paramref name="points"/> parameter sets: on each numeric axis exactly one point in each of <paramref name="points"/> equal strata of its range
        /// (the point's place in its stratum is random), on each choice the levels as evenly as the count allows. The jitter is the report's.
        /// </summary>
        public static PolicyParams[] LatinHypercube(int points, ulong seed)
        {
            if (points < 1) throw new ArgumentOutOfRangeException(nameof(points), "A hypercube needs at least one point.");
            var unit = new float[AxisCount][];
            for (int axis = 0; axis < AxisCount; axis++)
            {
                var rng = MatchRng.Create(seed, AxisStream + (ulong)axis);
                var order = new int[points];
                for (int i = 0; i < points; i++) order[i] = i;
                for (int i = points - 1; i > 0; i--)
                {
                    int j = rng.NextInt(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                unit[axis] = new float[points];
                for (int i = 0; i < points; i++) unit[axis][i] = (order[i] + rng.NextFloat()) / points;
            }

            var result = new PolicyParams[points];
            for (int i = 0; i < points; i++)
            {
                var p = MatchRunSpec.Baseline();
                p.ReactionDelay = Lerp(DelayMin, DelayMax, unit[0][i]);
                p.ReturnThreshold = Lerp(ReturnMin, ReturnMax, unit[1][i]);
                p.FleeRadius = Lerp(FleeMin, FleeMax, unit[2][i]);
                p.HuntDetour = Lerp(DetourMin, DetourMax, unit[3][i]);
                p.Target = TargetLevels[Math.Min(TargetLevels.Length - 1, (int)(unit[4][i] * TargetLevels.Length))];
                p.Route = RouteLevels[Math.Min(RouteLevels.Length - 1, (int)(unit[5][i] * RouteLevels.Length))];
                result[i] = p;
            }
            return result;
        }

        private static float Lerp(float lo, float hi, float t) => lo + (hi - lo) * t;

        /// <summary>The value of axis <paramref name="axis"/> in a point: a number for the numeric axes, the level's index for the two choices.</summary>
        public static float AxisValue(in PolicyParams p, int axis)
        {
            switch (axis)
            {
                case 0: return p.ReactionDelay;
                case 1: return p.ReturnThreshold;
                case 2: return p.FleeRadius;
                case 3: return p.HuntDetour;
                case 4: return Array.IndexOf(TargetLevels, p.Target);
                case 5: return Array.IndexOf(RouteLevels, p.Route);
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }

        public static bool IsChoice(int axis) => axis >= 4;
        public static float AxisRange(int axis) => axis == 0 ? DelayMax - DelayMin : axis == 1 ? ReturnMax - ReturnMin : axis == 2 ? FleeMax - FleeMin : DetourMax - DetourMin;

        // ---- the runs ----

        /// <summary>The three populations of one parameter set, played over the Mixed design, as one report section (P0 primary, the others under it).</summary>
        public static MatchReportSection RunPopulations(MatchContext context, PolicyParams parameters, MatchSweepSpec spec, ref int matches)
        {
            var runs = new List<MatchRunOutput>(3);
            foreach (var population in new[] { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed })
            {
                var run = MatchRunner.Run(context, new MatchRunSpec
                {
                    Design = MatchDesignKind.Mixed, Replicates = spec.Replicates, Population = population, RunSeed = spec.RunSeed,
                    Threads = spec.Threads, Params = parameters,
                });
                matches += run.Results.Length;
                runs.Add(run);
            }
            return MatchReports.Build(context, runs, null);
        }

        /// <summary>
        /// The kits and rules of <paramref name="inputs"/> with the Death Mark's <c>LeaderPickWeight</c> set to <paramref name="weight"/> by a delta that goes
        /// through <see cref="BalanceBook.Evaluate"/>, like any proposed value. Needs <see cref="OracleInputs.Book"/>.
        /// </summary>
        public static MatchContext ContextWithLeaderWeight(OracleInputs inputs, float weight, float arenaHalf, float sightArrivalRadius)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Book == null) throw new ArgumentException("Applying a leader weight needs OracleInputs.Book.", nameof(inputs));
            string delta = "{\"schema\":1,\"assets\":{\"MarkKill\":{\"LeaderPickWeight\":" + weight.ToString("R", Inv) + "}}}";
            var verdict = inputs.Book.Evaluate(delta, "match sweep");
            if (!verdict.IsAccepted || verdict.Ignored.Count > 0)
                throw new InvalidOperationException($"The book did not take LeaderPickWeight {weight} on MarkKill as written (accepted {verdict.IsAccepted}, ignored {verdict.Ignored.Count}).");
            var moved = new OracleInputs
            {
                Map = inputs.Map, Book = inputs.Book, Verdict = verdict, Abilities = inputs.Abilities, Classes = inputs.Classes,
                Match = inputs.Match, FoodOverride = inputs.FoodOverride,
            };
            return MatchContext.From(moved, arenaHalf, sightArrivalRadius);
        }

        public static MatchSweepOutput Run(OracleInputs inputs, float arenaHalf, float sightArrivalRadius, MatchSweepSpec spec)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            var clock = Stopwatch.StartNew();
            var context = MatchContext.From(inputs, arenaHalf, sightArrivalRadius);
            int matches = 0;

            var output = new MatchSweepOutput
            {
                Points = LatinHypercube(spec.Points, spec.RunSeed),
                Delays = (float[])spec.Delays.Clone(),
                LeaderWeights = (float[])spec.LeaderPickWeights.Clone(),
                KitLabels = context.Kits.Select(k => k.Label).ToArray(),
            };
            output.Default = RunPopulations(context, MatchRunSpec.Baseline(), spec, ref matches);

            output.Reports = new MatchReportSection[output.Points.Length];
            for (int i = 0; i < output.Points.Length; i++) output.Reports[i] = RunPopulations(context, output.Points[i], spec, ref matches);

            output.DelayReports = new MatchReportSection[output.Delays.Length];
            for (int i = 0; i < output.Delays.Length; i++)
            {
                var p = MatchRunSpec.Baseline();
                p.ReactionDelay = output.Delays[i];
                output.DelayReports[i] = RunPopulations(context, p, spec, ref matches);
            }

            if (inputs.Book != null)
            {
                output.LeaderWeightReports = new MatchReportSection[output.LeaderWeights.Length];
                for (int i = 0; i < output.LeaderWeights.Length; i++)
                {
                    var weighted = ContextWithLeaderWeight(inputs, output.LeaderWeights[i], arenaHalf, sightArrivalRadius);
                    output.LeaderWeightReports[i] = RunPopulations(weighted, MatchRunSpec.Baseline(), spec, ref matches);
                }
            }
            else output.Note = "LeaderPickWeight was not swept: the inputs hold no BalanceBook to apply a delta with.";

            output.Matches = matches;
            MatchSensitivity.Fill(output);
            clock.Stop();
            output.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            return output;
        }
    }

    /// <summary>The effect of each axis on each kit's win rate (spec 4.3 / the report's sensitivity table).</summary>
    public static class MatchSensitivity
    {
        public static readonly PolicyPopulation[] Populations = { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed };

        public static float[][] RatesOf(IReadOnlyList<MatchReportSection> reports, PolicyPopulation population, int kits)
        {
            var table = new float[reports.Count][];
            for (int i = 0; i < reports.Count; i++)
            {
                table[i] = new float[kits];
                var section = reports[i].ForPopulation(population);
                for (int k = 0; k < kits; k++) table[i][k] = section.Kits[k].Win.Rate;
            }
            return table;
        }

        /// <summary>
        /// The effect of one axis on one series of rates over the points: for a number, the least-squares slope times the axis's whole range (points of win
        /// rate if the rates are in points), signed; for a choice, the spread of the level means (best minus worst), never negative. 0 for no variation in the axis.
        /// </summary>
        public static float Effect(IReadOnlyList<float> axisValues, IReadOnlyList<float> rates, bool choice, float range)
        {
            int n = axisValues.Count;
            if (n < 2) return 0f;
            if (choice)
            {
                var sum = new SortedDictionary<int, (double Sum, int N)>();
                for (int i = 0; i < n; i++)
                {
                    int level = (int)axisValues[i];
                    sum.TryGetValue(level, out var acc);
                    sum[level] = (acc.Sum + rates[i], acc.N + 1);
                }
                if (sum.Count < 2) return 0f;
                var means = sum.Values.Select(a => a.Sum / a.N).ToArray();
                return (float)(means.Max() - means.Min());
            }
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += axisValues[i]; my += rates[i]; }
            mx /= n; my /= n;
            double cov = 0, varx = 0;
            for (int i = 0; i < n; i++) { cov += (axisValues[i] - mx) * (rates[i] - my); varx += (axisValues[i] - mx) * (axisValues[i] - mx); }
            return varx > 1e-12 ? (float)(cov / varx * range) : 0f;
        }

        public static void Fill(MatchSweepOutput o)
        {
            int kits = o.KitLabels.Length;
            foreach (var population in Populations)
            {
                var rates = RatesOf(o.Reports, population, kits);
                for (int axis = 0; axis < MatchSweep.AxisCount; axis++)
                {
                    var values = o.Points.Select(p => MatchSweep.AxisValue(p, axis)).ToArray();
                    for (int k = 0; k < kits; k++)
                    {
                        var series = rates.Select(r => r[k] * 100f).ToArray();
                        o.Sensitivity.Add(new SensitivityLine
                        {
                            Population = population, Parameter = MatchSweep.AxisNames[axis], Kit = o.KitLabels[k],
                            Effect = Effect(values, series, MatchSweep.IsChoice(axis), MatchSweep.IsChoice(axis) ? 1f : MatchSweep.AxisRange(axis)),
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// When is a verdict a verdict (spec 4.4): a kit's call holds when the populations agree, the kits' rank order survives the sweep, and the kit's rate
    /// does not run far past a band edge anywhere in it. Otherwise it is policy-sensitive and the report shows the range.
    /// </summary>
    public static class MatchRobustness
    {
        public const float MinTau = 0.8f;
        public const float MaxEdgeCrossPoints = 3f;

        /// <summary>Where one rate stands against the band: below 20 %, above 30 %, else in it. No trials is no call.</summary>
        public static KitCall CallOf(WinInterval w)
        {
            if (w.Trials == 0 || float.IsNaN(w.Rate)) return KitCall.NoCall;
            return w.Rate > MatchReports.BandHigh ? KitCall.Above : w.Rate < MatchReports.BandLow ? KitCall.Below : KitCall.InBand;
        }

        /// <summary>The call three populations agree on, else <see cref="KitCall.PolicySensitive"/>; no call when any population has no matches.</summary>
        public static KitCall Call(WinInterval p0, WinInterval p1, WinInterval mixed)
        {
            var a = CallOf(p0); var b = CallOf(p1); var c = CallOf(mixed);
            if (a == KitCall.NoCall || b == KitCall.NoCall || c == KitCall.NoCall) return KitCall.NoCall;
            return a == b && b == c ? a : KitCall.PolicySensitive;
        }

        /// <summary>
        /// The sweep's checks on a call. <paramref name="tauAgainstDefault"/> is the lowest rank correlation of the kits' order over the sweep against the
        /// default point (NaN counts as no evidence against); <paramref name="worstEdgeCrossPoints"/> is how far, in win-rate points, the kit's rate went
        /// past the band edge its call sits behind.
        /// </summary>
        public static KitCall Tag(KitCall call, float tauAgainstDefault, float worstEdgeCrossPoints)
        {
            if (call == KitCall.NoCall || call == KitCall.PolicySensitive) return call;
            if (tauAgainstDefault < MinTau) return KitCall.PolicySensitive;
            if (worstEdgeCrossPoints > MaxEdgeCrossPoints) return KitCall.PolicySensitive;
            return call;
        }

        /// <summary>Points of win rate by which <paramref name="rate"/> lies on the wrong side of the band for <paramref name="call"/> (0 when it does not).</summary>
        public static float EdgeCrossPoints(KitCall call, float rate)
        {
            float over = (rate - MatchReports.BandHigh) * 100f;
            float under = (MatchReports.BandLow - rate) * 100f;
            switch (call)
            {
                case KitCall.Above: return Math.Max(0f, -over);       // fell back under the top edge it sits behind
                case KitCall.Below: return Math.Max(0f, -under);      // rose back over the bottom edge it sits behind
                case KitCall.InBand: return Math.Max(0f, Math.Max(over, under));
                default: return 0f;
            }
        }

        /// <summary>
        /// One kit's share of the same rank correlation: only the pairs that hold this kit (against each other kit the default tells it apart from,
        /// by at least <paramref name="minGap"/>), as (kept - flipped) / (kept + flipped). NaN when it has no such pair. This is the per-kit
        /// reading of spec 4.4's "the kits' rank order keeps tau 0.8": a kit that stays under the others in every sweep point keeps tau 1 even while
        /// two other kits swap places.
        /// </summary>
        public static float KitTauAgainstDefault(IReadOnlyList<float> defaultRates, IReadOnlyList<float> rates, int kit, float minGap)
        {
            long kept = 0, flipped = 0;
            for (int j = 0; j < defaultRates.Count; j++)
            {
                if (j == kit || Math.Abs(defaultRates[kit] - defaultRates[j]) < minGap) continue;
                int sa = Math.Sign(defaultRates[kit].CompareTo(defaultRates[j]));
                int sb = Math.Sign(rates[kit].CompareTo(rates[j]));
                if (sb == 0) continue;
                if (sa == sb) kept++; else flipped++;
            }
            long pairs = kept + flipped;
            return pairs == 0 ? float.NaN : (kept - flipped) / (float)pairs;
        }

        /// <summary>
        /// Kendall's tau-b of two orders of the kits, counting only the pairs the default order can tell apart (their default rates differ by at least
        /// <paramref name="minGap"/>): a pair whose two kits sit within the run's noise of each other has no order to keep. NaN when no pair counts.
        /// </summary>
        public static float TauAgainstDefault(IReadOnlyList<float> defaultRates, IReadOnlyList<float> rates, float minGap)
        {
            int n = defaultRates.Count;
            long concordant = 0, discordant = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (Math.Abs(defaultRates[i] - defaultRates[j]) < minGap) continue;
                    int sa = Math.Sign(defaultRates[i].CompareTo(defaultRates[j]));
                    int sb = Math.Sign(rates[i].CompareTo(rates[j]));
                    if (sb == 0) continue;
                    if (sa == sb) concordant++; else discordant++;
                }
            long pairs = concordant + discordant;
            return pairs == 0 ? float.NaN : (concordant - discordant) / (float)pairs;
        }
    }
}
