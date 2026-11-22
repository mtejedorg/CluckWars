using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using CluckWars.Abilities;

namespace CluckWars.Balance
{
    /// <summary>Which set of matches a run plays (spec 5.2).</summary>
    public enum MatchDesignKind : byte { Mixed, OnePerClass, SeatCheck, ForcedKills }

    /// <summary>Who decides for each seat. Task 6 adds the utility policy and the mix.</summary>
    public enum PolicyPopulation : byte { P0 }

    /// <summary>One match to play: which kit sits in which seat, which replicate it is, and the scripted kills if the design has any.</summary>
    public sealed class MatchCell
    {
        public int Index;
        /// <summary>Index into <see cref="MatchContext.Kits"/> per seat; -1 for an empty seat.</summary>
        public int[] KitOfSeat;
        public int Replicate;
        public ForcedKills Forced = ForcedKills.Off;
    }

    /// <summary>
    /// The cells of each design, in a fixed order (design, kit set in lexicographic order, seating, then the design's own axes, replicate), so
    /// the same inputs give the same cells on every run.
    /// </summary>
    public static class MatchDesigns
    {
        private const int Seats = OracleMapSO.SeatCount;

        /// <summary>The kills a forced-kills match grants, 0 to this many.</summary>
        public const int MaxForcedKills = 4;
        /// <summary>The first forced kill's time, then the gap to the next (seconds into the match).</summary>
        public const float ForcedFirstKill = 5f;
        public const float ForcedKillGap = 3.5f;

        public static List<MatchCell> Cells(MatchDesignKind design, MatchKit[] kits, int replicates)
        {
            if (kits == null || kits.Length == 0) throw new ArgumentException("No kits to build a design from.", nameof(kits));
            if (replicates < 1) throw new ArgumentOutOfRangeException(nameof(replicates), "A design needs at least one replicate.");

            var cells = new List<MatchCell>();
            switch (design)
            {
                case MatchDesignKind.Mixed:
                    foreach (var set in Choose(kits.Length, Seats)) AddSeatings(cells, set, replicates);
                    break;
                case MatchDesignKind.OnePerClass:
                    foreach (var set in OnePerClassSets(kits)) AddSeatings(cells, set, replicates);
                    break;
                case MatchDesignKind.SeatCheck:
                    for (int k = 0; k < kits.Length; k++)
                        for (int r = 0; r < replicates; r++)
                            cells.Add(new MatchCell { Index = cells.Count, KitOfSeat = new[] { k, k, k, k }, Replicate = r });
                    break;
                case MatchDesignKind.ForcedKills:
                    for (int spoiler = 0; spoiler < kits.Length; spoiler++)
                    {
                        if (!HoldsDeathMark(kits[spoiler])) continue;
                        foreach (var set in OnePerClassSets(kits))
                            if (Array.IndexOf(set, spoiler) >= 0) AddForced(cells, set, spoiler, replicates);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(design));
            }
            return cells;
        }

        /// <summary>True when the kit holds a Death Mark ability (the Spoiler's).</summary>
        public static bool HoldsDeathMark(MatchKit kit)
        {
            if (kit?.Abilities == null) return false;
            foreach (var a in kit.Abilities) if (a.Job == AbilityJob.DeathMark) return true;
            return false;
        }

        // Every way to pick `take` of `n` kit indices, in lexicographic order.
        private static IEnumerable<int[]> Choose(int n, int take)
        {
            var pick = new int[take];
            for (int i = 0; i < take; i++) pick[i] = i;
            if (take > n) yield break;
            while (true)
            {
                yield return (int[])pick.Clone();
                int at = take - 1;
                while (at >= 0 && pick[at] == n - take + at) at--;
                if (at < 0) yield break;
                pick[at]++;
                for (int i = at + 1; i < take; i++) pick[i] = pick[i - 1] + 1;
            }
        }

        // One kit per class: the cartesian product of the kits of each class, classes in enum order, ascending kit order inside a set.
        private static List<int[]> OnePerClassSets(MatchKit[] kits)
        {
            var groups = kits.Select((k, i) => (k.Class, Index: i)).GroupBy(t => t.Class).OrderBy(g => g.Key)
                .Select(g => g.Select(t => t.Index).ToArray()).ToArray();
            if (groups.Length != Seats)
                throw new InvalidOperationException($"One kit per class needs {Seats} classes; the kits cover {groups.Length}.");

            var sets = new List<int[]> { new int[0] };
            foreach (var group in groups)
            {
                var next = new List<int[]>();
                foreach (var prefix in sets)
                    foreach (int kit in group) next.Add(prefix.Append(kit).ToArray());
                sets = next;
            }
            foreach (var set in sets) Array.Sort(set);
            return sets;
        }

        // The six seatings that differ up to rotation (the map is four-fold symmetric): the lowest kit first, the other three in every
        // order. Each cell of a set turns the table one seat further than the one before it, so a seat's bias (the lowest corner wins a tie,
        // a bot at corner 3 thinks later than the one at corner 0) is shared out between the kits instead of landing on the lowest kit.
        private static void AddSeatings(List<MatchCell> cells, int[] set, int replicates)
        {
            int turn = 0;
            foreach (var order in Seatings(set))
                for (int r = 0; r < replicates; r++)
                    cells.Add(new MatchCell { Index = cells.Count, KitOfSeat = Rotate(order, turn++), Replicate = r });
        }

        private static void AddForced(List<MatchCell> cells, int[] set, int spoilerKit, int replicates)
        {
            var rest = set.Where(k => k != spoilerKit).ToArray();
            int turn = 0;
            foreach (var tail in Permutations(rest))
                for (int kills = 0; kills <= MaxForcedKills; kills++)
                    foreach (var victims in new[] { ForcedVictims.Empty, ForcedVictims.Loaded })
                        for (int r = 0; r < replicates; r++)
                        {
                            var order = new[] { spoilerKit, tail[0], tail[1], tail[2] };
                            var seats = Rotate(order, turn++);
                            var times = new float[MaxForcedKills];
                            for (int i = 0; i < times.Length; i++) times[i] = ForcedFirstKill + ForcedKillGap * i;
                            cells.Add(new MatchCell
                            {
                                Index = cells.Count, KitOfSeat = seats, Replicate = r,
                                Forced = new ForcedKills { Spoiler = Array.IndexOf(seats, spoilerKit), Kills = kills, Victims = victims, Times = times },
                            });
                        }
        }

        /// <summary>The six distinct seatings of a set of four kits up to rotation: its lowest kit in seat 0, the rest in every order.</summary>
        public static List<int[]> Seatings(int[] set)
        {
            var sorted = (int[])set.Clone();
            Array.Sort(sorted);
            var tails = sorted.Skip(1).ToArray();
            var list = new List<int[]>();
            foreach (var p in Permutations(tails)) list.Add(new[] { sorted[0], p[0], p[1], p[2] });
            return list;
        }

        // All orders of the items, in lexicographic order of position.
        private static IEnumerable<int[]> Permutations(int[] items)
        {
            var sorted = (int[])items.Clone();
            Array.Sort(sorted);
            while (true)
            {
                yield return (int[])sorted.Clone();
                int i = sorted.Length - 2;
                while (i >= 0 && sorted[i] >= sorted[i + 1]) i--;
                if (i < 0) yield break;
                int j = sorted.Length - 1;
                while (sorted[j] <= sorted[i]) j--;
                (sorted[i], sorted[j]) = (sorted[j], sorted[i]);
                Array.Reverse(sorted, i + 1, sorted.Length - i - 1);
            }
        }

        // The table turned `by` seats: seat (i + by) holds what seat i held.
        private static int[] Rotate(int[] order, int by)
        {
            var seats = new int[Seats];
            for (int i = 0; i < Seats; i++) seats[(i + by) % Seats] = order[i];
            return seats;
        }
    }

    /// <summary>Everything a match run reads, resolved on the main thread: the plain values the simulation touches.</summary>
    public sealed class MatchContext
    {
        public OracleMapSO Map;
        public OracleNav Nav;
        public OracleFood Food;
        public MatchRules Rules;
        public MatchKit[] Kits;
        public string BalanceHash;

        /// <summary>Builds the context from the shipped assets (or a verdict over them) through <see cref="BalanceBook.Preview{T}"/>. Main thread only.</summary>
        public static MatchContext From(OracleInputs inputs, float arenaHalf, float sightArrivalRadius)
        {
            var kits = MatchKits.All(inputs);
            return new MatchContext
            {
                Map = inputs.Map,
                Nav = OracleNav.For(inputs.Map, arenaHalf),
                Food = OracleKits.Food(inputs),
                Rules = MatchKits.Rules(inputs, arenaHalf, sightArrivalRadius),
                Kits = kits,
                BalanceHash = inputs.Verdict != null ? inputs.Verdict.Hash : BalanceBook.BaselineHash,
            };
        }
    }

    /// <summary>One run: a design, how often each cell is replicated, who decides, the seed and how many threads.</summary>
    public sealed class MatchRunSpec
    {
        public MatchDesignKind Design;
        public int Replicates = 1;
        public PolicyPopulation Population;
        public ulong RunSeed = 1UL;
        /// <summary>Worker threads; zero or less means every core. The result does not depend on it.</summary>
        public int Threads = 1;
        /// <summary>The shipped report's reaction jitter: each intent takes 0 to this many seconds longer than the delay (an arbitrary "small", not a measured value).</summary>
        public const float DefaultReactionJitter = 0.1f;
        public PolicyParams Params = WithJitter(PolicyParams.Default, DefaultReactionJitter);

        public static PolicyParams WithJitter(PolicyParams p, float seconds)
        {
            p.ReactionJitter = seconds;
            return p;
        }
        /// <summary>Play only the first this many cells; 0 plays them all.</summary>
        public int CellLimit;
        /// <summary>Keep every match's event list (memory heavy; for reading a few matches).</summary>
        public bool RecordEvents;
    }

    public sealed class MatchRunOutput
    {
        public MatchRunSpec Spec;
        public MatchCell[] Cells;
        public MatchResult[] Results;
        /// <summary>Wall-clock time of the run; not part of any comparison.</summary>
        public double ElapsedMilliseconds;
    }

    /// <summary>
    /// Plays a design's matches in parallel. Each match owns its setup and writes its result into its own slot, so the totals do not depend
    /// on how the threads were scheduled; the seed of a match is a function of the run seed, its cell and its replicate.
    /// </summary>
    public static class MatchRunner
    {
        public static MatchRunOutput Run(MatchContext context, MatchRunSpec spec) => Run(context, spec, FactoryFor(spec.Population));

        /// <summary>Runs with an explicit policy factory for every seat (tests and later populations).</summary>
        public static MatchRunOutput Run(MatchContext context, MatchRunSpec spec, PolicyFactory factory)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            var cells = MatchDesigns.Cells(spec.Design, context.Kits, spec.Replicates);
            if (spec.CellLimit > 0 && cells.Count > spec.CellLimit) cells = cells.GetRange(0, spec.CellLimit);

            var output = new MatchRunOutput { Spec = spec, Cells = cells.ToArray(), Results = new MatchResult[cells.Count] };
            var options = new ParallelOptions { MaxDegreeOfParallelism = spec.Threads > 0 ? spec.Threads : Environment.ProcessorCount };
            var clock = Stopwatch.StartNew();
            try
            {
                Parallel.For(0, cells.Count, options, i => output.Results[i] = Play(context, spec, factory, output.Cells[i]));
            }
            catch (AggregateException e)
            {
                // The first failure by cell, rethrown as itself so the caller sees the cell and the seed, not a wrapper.
                ExceptionDispatchInfo.Capture(e.Flatten().InnerExceptions[0]).Throw();
            }
            clock.Stop();
            output.ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds;
            return output;
        }

        private static MatchResult Play(MatchContext context, MatchRunSpec spec, PolicyFactory factory, MatchCell cell)
        {
            ulong seed = MatchRng.MatchSeed(spec.RunSeed, cell.Index, cell.Replicate);
            try
            {
                var seats = new MatchKit[OracleMapSO.SeatCount];
                var policies = new PolicyFactory[OracleMapSO.SeatCount];
                var parameters = new PolicyParams[OracleMapSO.SeatCount];
                for (int s = 0; s < seats.Length; s++)
                {
                    int kit = cell.KitOfSeat[s];
                    seats[s] = kit >= 0 ? context.Kits[kit] : null;
                    policies[s] = kit >= 0 ? factory : null;
                    parameters[s] = spec.Params;
                }
                var setup = new MatchSetup
                {
                    Map = context.Map, Nav = context.Nav, Food = context.Food, Rules = context.Rules,
                    Seats = seats, Policies = policies, Params = parameters, RecordEvents = spec.RecordEvents, Forced = cell.Forced,
                };
                return MatchSim.Run(setup, seed);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Match failed in cell {cell.Index} (replicate {cell.Replicate}, seed 0x{seed:x16}): {e.Message}", e);
            }
        }

        public static PolicyFactory FactoryFor(PolicyPopulation population)
        {
            switch (population)
            {
                case PolicyPopulation.P0: return ScriptedPolicy.Factory;
                default: throw new ArgumentOutOfRangeException(nameof(population));
            }
        }
    }
}
