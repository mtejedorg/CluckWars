using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CluckWars.Gameplay;

namespace CluckWars.Balance
{
    /// <summary>What the report says about a kit's win rate. With one policy population nothing is final (<see cref="NoCall"/>).</summary>
    public enum KitCall : byte { InBand, Above, Below, PolicySensitive, NoCall }

    public sealed class KitWinLine
    {
        public string Label;
        public ChickenClass Class;
        public WinInterval Win;
        public KitCall Call;
        /// <summary>Why the call is what it is; with no call, where the kit stands provisionally and what is missing.</summary>
        public string Reason;
        /// <summary>The kit's rate under each population in the mixed design; <see cref="Win"/> is P0's. A population that did not run has no trials.</summary>
        public WinInterval WinP1;
        public WinInterval WinMixed;
        /// <summary>The lowest and highest of the three populations' rates (the range a policy-sensitive kit shows instead of a call).</summary>
        public float RangeLow;
        public float RangeHigh;
    }

    public sealed class ClassWinLine
    {
        public ChickenClass Class;
        /// <summary>One kit per class, every class in every match (the headline, D3).</summary>
        public WinInterval Headline;
        /// <summary>A seat of this class in the mixed design.</summary>
        public WinInterval Pooled;
        /// <summary>The same two lines under P1 and under the mixed population (<see cref="Headline"/> and <see cref="Pooled"/> are P0's).</summary>
        public WinInterval HeadlineP1, PooledP1, HeadlineMixed, PooledMixed;
    }

    /// <summary>The head-to-head diagnostic: P1 seats against P0 seats, same kits, seats rotated (not a test, a line).</summary>
    public sealed class PolicyMatchupLine
    {
        public string Label;
        public int Matches;
        /// <summary>Matches won by a P1 seat; the other two seats are P0's, so an equal strength gives half.</summary>
        public WinInterval P1Share;
    }

    public sealed class HeadToHeadLine
    {
        public ChickenClass Class;
        public string KitA;
        public string KitB;
        /// <summary>Share of matches holding both kits in which <see cref="KitA"/> banked more than <see cref="KitB"/> (a tie counts half).</summary>
        public float AAhead;
        public int Matches;
    }

    public sealed class ThiefLine
    {
        public string Victim;
        /// <summary>Food taken from this kit per appearance of the Thief in a match that also held it.</summary>
        public float StolenPerMatch;
        /// <summary>Share of everything the Thief took.</summary>
        public float Share;
    }

    public sealed class PeckCutLine
    {
        public ChickenClass Class;
        public CutCause Cause;
        /// <summary>Peck casts that began with a rival holding a ready cutter of this cause in range.</summary>
        public int Exposed;
        /// <summary>Every cut of this cause (exposed or not).</summary>
        public int Cut;
        /// <summary>Of the exposed casts, the share then cut by that cause; NaN where no cast was exposed (empty pile and the chicken's own move have no cutter).</summary>
        public float Hazard;
        /// <summary>Nominal food of the cut casts (cuts times the class's peck amount) per match the class played.</summary>
        public float FoodLostPerMatch;
    }

    public sealed class ContactLine
    {
        public float Median;
        public float Q1;
        public float Q3;
        /// <summary>Matches in which two chickens never came within the smallest offensive reach.</summary>
        public int Never;
        public int Matches;
    }

    public sealed class EconomyShareLine
    {
        public string Label;
        public float Foraged;
        public float Stolen;
        public float Bounty;
    }

    public sealed class SeatLine
    {
        public int Seat;
        /// <summary>Over every match of the seat check.</summary>
        public WinInterval Win;
        /// <summary>Over the matches the corner rule did not decide. When nothing separates the four chickens the game gives the win to the lowest corner, a seat bias of the game's own.</summary>
        public WinInterval WinExCorner;
        /// <summary>The interval of <see cref="WinExCorner"/> excludes an equal share: a model or map bug (a line, never a test).</summary>
        public bool Flagged;
    }

    /// <summary>How the four seats of a full mirror of one kit did: wins per seat and matches the corner rule decided.</summary>
    public sealed class SeatKitLine
    {
        public string Kit;
        public int Matches;
        public int[] WinsBySeat;
        public int CornerDecided;
    }

    public sealed class DriftLine
    {
        public string Label;
        public float Points;
    }

    public sealed class RunLine
    {
        public MatchDesignKind Design;
        public PolicyPopulation Population;
        public int Replicates;
        public int Matches;
        public ulong RunSeed;
        public float ReactionDelay;
        public float ReactionJitter;
        public int EndedOnTarget;
        public int EndedOnClock;
        public float MeanEndSeconds;
        public double ElapsedMilliseconds;
    }

    /// <summary>The Thief's income in the mixed design.</summary>
    public sealed class ThiefSummary
    {
        public string Kit;
        public int Matches;
        public float StealsPerMatch;
        public float StolenPerMatch;
        public float ForagedPerMatch;
        public float BankedPerMatch;
        /// <summary>The loot mark is on for this Thief (the shipped Thief has none); the fields below are 0 when it is not.</summary>
        public bool LootActive;
        public float MarksDrawnPerMatch;
        /// <summary>Marks consumed by a marked steal, per match.</summary>
        public float MarksTakenPerMatch;
        /// <summary>Marks that moved on unpaid (timeout) or lost their bird, per match.</summary>
        public float MarksLostPerMatch;
        /// <summary>Steal casts that met the marked bird behind a wall or a shell, per match.</summary>
        public float MarksRefusedPerMatch;
        /// <summary>Food marked steals took from the marked rivals into the Thief's cargo, per match.</summary>
        public float LootTakeFoodPerMatch;
        /// <summary>New bounty food marked steals put into the Thief's cargo, per match.</summary>
        public float LootBountyFoodPerMatch;
        /// <summary>Loot food (take plus bounty) that did not fit the Thief's beak, per match.</summary>
        public float LootLostToRoomPerMatch;
        /// <summary>Loot food the Thief banked, per match.</summary>
        public float LootBankedPerMatch;
        /// <summary>Loot food rivals' steals took from the Thief, per match.</summary>
        public float LootLostPerMatch;
        /// <summary>Trips home (deposit visits that emptied the cargo) per match.</summary>
        public float TripsPerMatch;
        /// <summary>Share of the Thief's matches in which it made two or more marked steals between two trips home.</summary>
        public float StackedMarksShare;
        /// <summary>Of the matches the Thief won on the win target, the share in which it had stacked two or more marked steals in one load (NaN with no win).</summary>
        public float WinsWithStackedMarks;
        public int WinsOnTarget;
        /// <summary>Share of the Thief's matches in which it made three or more marked steals in one load: what Maestro's third note rules out.</summary>
        public float ThreeInOneLoadShare;
        /// <summary>Of its wins on the target, the share with three or more marked steals in one load (NaN with no win).</summary>
        public float WinsWithThreeInOneLoad;
        /// <summary>Share of the Thief's matches that it won only through the clock prize.</summary>
        public float WonOnPrize;
        /// <summary>Share of the Thief's matches in which its loot mark shared a bird with a Spoiler's Hunt mark for any time.</summary>
        public float OverlapShare;
        public float OverlapSecondsPerMatch;
        /// <summary>Of the Thief's matches that held a Spoiler, the share with an overlap (NaN with none).</summary>
        public float OverlapShareWithSpoiler;
        public int MatchesWithSpoiler;
    }

    /// <summary>
    /// One Warrior kit's rows in one population (mixed design, design 2026-11-25 section 8.3): the Bully's hit-steal, the Champion's breaks and trophies,
    /// the overflow to the centre pile and the worst loss one victim took inside <see cref="MatchReports.LossWindowSeconds"/>. Per match, over the matches that held the kit.
    /// </summary>
    public sealed class WarriorLine
    {
        public string Kit;
        public int Matches;
        public WinInterval Win;
        public bool HitActive, BiteActive, TrophyKit;
        public float HitTakesPerMatch, HitFoodPerMatch, HitRefusedLockPerMatch, HitRefusedNoFoodPerMatch, HitRefusedRoomPerMatch;
        /// <summary>Food hit-steals took from this kit (the victim's side).</summary>
        public float HitLostFoodPerMatch;
        /// <summary>Hits on a fresh rival that stacked the extra trophy (the Champion's, whether or not they took food).</summary>
        public float FreshHitsPerMatch;
        public float BitesFromCastPerMatch, BitesFromCargoPerMatch, BiteFoodFromCastPerMatch, BiteFoodFromCargoPerMatch, BiteLeftInPilePerMatch, BiteLeftWithVictimPerMatch, BitesSpentPerMatch;
        /// <summary>This kit's Peck casts that rivals bit, and the food bites took from its cargo (the victim's side).</summary>
        public float PecksBittenPerMatch, BiteLostFoodPerMatch;
        public float TrophiesEarnedPerMatch, TrophiesDecayedPerMatch, TrophiesClearedPerMatch, TrophyCashesPerMatch, TrophyBonusFoodPerMatch;
        /// <summary>Trophies consumed per Steal that cashed (NaN when none cashed).</summary>
        public float TrophiesPerCash;
        public float StealsPerMatch, StealFoodPerMatch;
        /// <summary>Food per Steal cast, over the matches that held the kit (NaN when it made none).</summary>
        public float MeanStealSize;
        /// <summary>Food per hit-steal take (NaN when it made none).</summary>
        public float MeanHitSize;
        /// <summary>The food the kit's identity brought in per match: hit-steal food, bite food and the food the trophies added to Steals.</summary>
        public float IdentityFoodPerMatch;
        public float WorstLossMean, WorstLossP95;
    }

    /// <summary>What the executions of one kit paid on victims of one class (mixed design): the victim's whole beak, and how much of it and the bounty fitted the killer's cargo.</summary>
    public sealed class KillLootLine
    {
        public string Killer;
        public ChickenClass Victim;
        public int Kills;
        /// <summary>Cargo the victims carried and the executions drained, per kill.</summary>
        public float CargoPerKill;
        /// <summary>Food really gained per kill (victim cargo first, then the bounty, held to the killer's free room).</summary>
        public float GainedPerKill;
        /// <summary>Food that did not fit per kill (victim cargo plus bounty, less the gain).</summary>
        public float UnfitPerKill;
    }

    /// <summary>The match simulator's section of the oracle report (spec 6). Plain data; <see cref="ToMarkdown"/> and <see cref="ToJson"/> print it.</summary>
    public sealed class MatchReportSection
    {
        public string BalanceHash;
        public string MapFingerprint;
        public ulong RunSeed;
        public List<RunLine> Runs = new List<RunLine>();
        public List<KitWinLine> Kits = new List<KitWinLine>();
        public List<ClassWinLine> Classes = new List<ClassWinLine>();
        public List<HeadToHeadLine> HeadToHead = new List<HeadToHeadLine>();
        /// <summary>The Spoiler's kills-to-win from the forced-kills design.</summary>
        public List<SpoilerKillRow> KillsToWin = new List<SpoilerKillRow>();
        /// <summary>The Spoiler's win rate by the kills a policy really made in the mixed design (survivor bias included).</summary>
        public List<SpoilerKillRow> KillsPlayed = new List<SpoilerKillRow>();
        /// <summary>Executions by killer kit and victim class: the food a kill gained and the food that did not fit (mixed design).</summary>
        public List<KillLootLine> KillLoot = new List<KillLootLine>();
        public List<ThiefLine> Thief = new List<ThiefLine>();
        public ThiefSummary ThiefIncome;
        /// <summary>One row per Warrior kit (mixed design); empty when no Warrior kit played.</summary>
        public List<WarriorLine> Warriors = new List<WarriorLine>();
        /// <summary>The Thief's win rate by the marked steals it really took (mixed design; survivor bias included). Empty with the loot mark off.</summary>
        public List<LootStealRow> LootSteals = new List<LootStealRow>();
        public List<PeckCutLine> PeckCuts = new List<PeckCutLine>();
        public ContactLine FirstContact;
        /// <summary>The same, over the matches that held no Assassin (whose Search walk makes the contact in the first seconds).</summary>
        public ContactLine FirstContactNoAssassin;
        public List<EconomyShareLine> Economy = new List<EconomyShareLine>();
        public int CornerDecided;
        public int MixedMatches;
        /// <summary>Share of the mixed design's matches won from each seat. The designs turn the table so each kit sits in each seat about as often; what is left here is a seat bias of the model or the map (none was left after the 2026-11-22 tie fix in P0's pile choice).</summary>
        public List<SeatLine> MixedSeats = new List<SeatLine>();
        public List<SeatLine> SeatCheck = new List<SeatLine>();
        public List<SeatKitLine> SeatByKit = new List<SeatKitLine>();
        /// <summary>Matches of the seat check decided by the corner rule (all banked food and kills level, so seat 0 wins).</summary>
        public int SeatCheckCornerDecided;
        public int SeatCheckMatches;
        public List<DriftLine> Drift = new List<DriftLine>();
        /// <summary>One line per match record in the reference, oldest first (date, note, balance hash, seed); empty when there is none.</summary>
        public List<string> RecordHistory = new List<string>();
        /// <summary>Which population the tables of this section are for (P0 for the section Build returns; <see cref="Policies"/> holds one per population).</summary>
        public PolicyPopulation Population;
        /// <summary>One section per population that ran, in the order P0, P1, mixed; the first is this section's own tables.</summary>
        public List<MatchReportSection> Policies = new List<MatchReportSection>();
        /// <summary>P1 against P0, from the split runs.</summary>
        public List<PolicyMatchupLine> Matchups = new List<PolicyMatchupLine>();
        public MatchSweepOutput Sweep;
        /// <summary>Lowest rank correlation of the kits' order across the sweep against the default point (NaN when there is none).</summary>
        public float SweepTauMin = float.NaN;
        /// <summary>Per kit, the furthest the mixed rate went past the band edge its call sits behind, over the sweep, in points.</summary>
        public float[] SweepEdgeCross;
        /// <summary>Per kit, the lowest rank correlation of its place among the kits over the sweep against the default point (NaN when no pair counts).</summary>
        public float[] SweepKitTau;
        public List<string> LeftOut = new List<string>();
        /// <summary>The line that says which policy populations exist; empty once every population ran.</summary>
        public string PopulationNote;

        /// <summary>The section of one population (this one when it is that population's).</summary>
        public MatchReportSection ForPopulation(PolicyPopulation population)
        {
            if (Population == population && Policies.Count <= 1) return this;
            foreach (var s in Policies) if (s.Population == population) return s;
            if (Population == population) return this;
            throw new InvalidOperationException($"The report holds no section for {population}.");
        }

        public string ToMarkdown() => MatchReports.Markdown(this);
        public string ToJson() => MatchReports.Json(this);
    }

    public static class MatchReports
    {
        /// <summary>The window of the "worst loss for a victim" row of the Warrior block, in seconds. A report constant, not a game number.</summary>
        public const float LossWindowSeconds = 5f;
        public const float Target = 0.25f;
        public const float BandLow = 0.20f;
        public const float BandHigh = 0.30f;

        /// <summary>Replicates of the four designs the match report plays (spec 5.3).</summary>
        public const int MixedReplicates = 10, OnePerClassReplicates = 25, SeatCheckReplicates = 25, ForcedKillsReplicates = 5;

        /// <summary>
        /// The runs of the standard match report: the mixed, one-per-class and seat-check designs under each of the three populations, the forced
        /// kills once (P0), and with <paramref name="withSplits"/> the two head-to-head splits. <paramref name="progress"/> gets one line per run.
        /// <paramref name="replicateScale"/> (default 1: the report's own replicates) scales every design's replicates, see <see cref="ScaledReplicates"/>.
        /// </summary>
        public static List<MatchRunOutput> PlayStandard(MatchContext context, ulong seed, int threads, bool withSplits, Action<string> progress = null, double replicateScale = 1.0)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!(replicateScale > 0.0)) throw new ArgumentOutOfRangeException(nameof(replicateScale), "The replicate scale must be above zero.");
            int mixedR = ScaledReplicates(MixedReplicates, replicateScale), onePerClassR = ScaledReplicates(OnePerClassReplicates, replicateScale);
            int seatR = ScaledReplicates(SeatCheckReplicates, replicateScale), forcedR = ScaledReplicates(ForcedKillsReplicates, replicateScale);
            var plan = new List<(MatchDesignKind, int, PolicyPopulation)>();
            foreach (var population in new[] { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed })
            {
                plan.Add((MatchDesignKind.Mixed, mixedR, population));
                plan.Add((MatchDesignKind.OnePerClass, onePerClassR, population));
                plan.Add((MatchDesignKind.SeatCheck, seatR, population));
            }
            plan.Add((MatchDesignKind.ForcedKills, forcedR, PolicyPopulation.P0));
            if (withSplits)
            {
                plan.Add((MatchDesignKind.Mixed, mixedR, PolicyPopulation.Split));
                plan.Add((MatchDesignKind.Mixed, mixedR, PolicyPopulation.SplitOdd));
            }
            var runs = new List<MatchRunOutput>(plan.Count);
            foreach (var (design, replicates, population) in plan)
            {
                var run = MatchRunner.Run(context, new MatchRunSpec { Design = design, Replicates = replicates, Population = population, RunSeed = seed, Threads = threads });
                runs.Add(run);
                progress?.Invoke($"Match run {design} {population} R={replicates}: {run.Results.Length} matches in {run.ElapsedMilliseconds / 1000.0:0.0} s on {threads} threads ({run.ElapsedMilliseconds / Math.Max(1, run.Results.Length):0.00} ms per match across threads).");
            }
            return runs;
        }

        /// <summary>
        /// <paramref name="replicates"/> times <paramref name="scale"/>, rounded to the nearest whole number and never below one. A scale of 1 gives the
        /// report's own replicates; a what-if run uses a smaller one to fit more scenarios in the time (the margins grow by about the square root).
        /// </summary>
        public static int ScaledReplicates(int replicates, double scale) => Math.Max(1, (int)Math.Round(replicates * scale, MidpointRounding.AwayFromZero));

        private static readonly CutCause[] Causes = { CutCause.Stun, CutCause.Shove, CutCause.Execution, CutCause.EmptyPile, CutCause.OwnMove };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The "left out" column of spec 2.4, with what each costs in accuracy: printed with every report.</summary>
        public static readonly string[] LeftOut =
        {
            "Body collision between chickens and the collision slow: needs physics. Crowding at the centre costs nothing here, so contested piles are slightly too easy; Featherfoot's immunity to the collision slow is worth nothing here (understated).",
            "Pile footprints as solid ovals: a pile is a point plus its reach. Under 1 m per approach.",
            "Golden Pile spot: the game also redraws a spot that overlaps a crate, a pile or a base; only the walls are known here, so some Golden Piles stand nearer a prop than in the game. Not quantified.",
            "Underdog Surge on equal banked food goes to the lowest seat here; the game takes an arbitrary chicken. A seat bias on ties, which are common when the event fires early. The designs turn the table by one seat per replicate to share it out.",
            "A Peck cast by hand (the simulator's Peck is the auto-Peck chain): a policy that hand-pecks through its own Shield is not modelled.",
            "A shove that meets a wall slides along the wall's clearance edge; the game slides along the wall's box. Well under 0.1 m a push.",
            "Aim, misclicks and hesitation: one reaction delay for every policy, P0 included (the game's bot acts on the think itself). Overstates a single cast and understates a chain of two casts in one window: the Spoiler's stun then Kill pays the delay twice inside one stun, so at the default delay about half the marks whose target was stunned once armed end without a kill, against about one in ten at no delay. The sensitivity sweep (a later task) measures it.",
            "Facing: the simulator has no heading, so the game bot's 'turn first, cast next think' costs nothing here. Slightly overstates directional abilities for P0.",
            "A rival base as a limit on walking: chickens may walk into a rival's base zone; only the ability pair rule (no cast across a base wall) is modelled.",
            "A placed Slow Area zone (an aura is modelled; a kit that ships a zone makes the simulator refuse to start).",
            "Network latency, prediction, animation, effects, audio and UI: not balance. Latency of 0.05 to 0.15 s is inside the reaction delay.",
            "Jump's arc and landing: a dash that crosses walls at the ability's speed for its seconds. Under 1 m per jump.",
            "Retired abilities and free loadouts: fixed kits only. None.",
            "P0's own slow in the walk-home estimate and the class think interval (0.22 to 0.30 s): the simulator uses one think rhythm of 0.25 s and the unslowed speed. Small.",
        };

        // ------------------------------------------------------------------ build

        /// <summary>
        /// Builds the report from the runs played. Runs are grouped by population: P0 (the section Build returns holds its tables), P1 and the mixed
        /// population (under <see cref="MatchReportSection.Policies"/>), and the two split runs (the head-to-head line). A kit gets a call only when all
        /// three populations ran and agree; the sweep, when given, can tag it policy-sensitive. <paramref name="reference"/> may be null (no kill labels).
        /// </summary>
        public static MatchReportSection Build(MatchContext context, IReadOnlyList<MatchRunOutput> runs, OracleReferenceSO reference, MatchSweepOutput sweep = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (runs == null) throw new ArgumentNullException(nameof(runs));

            var sections = new List<MatchReportSection>();
            foreach (var population in new[] { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed })
            {
                var own = runs.Where(r => r.Spec.Population == population).ToList();
                if (own.Count == 0) continue;
                var one = BuildOne(context, own, reference);
                one.Population = population;
                sections.Add(one);
            }
            var primary = sections.Count > 0 ? sections[0] : BuildOne(context, new List<MatchRunOutput>(), reference);
            primary.Policies = sections.Count > 0 ? sections : new List<MatchReportSection> { primary };
            primary.Runs = runs.Select(RunInfo).ToList();
            if (runs.Count > 0) primary.RunSeed = runs[runs.Count - 1].Spec.RunSeed;
            primary.PopulationNote = PopulationText(sections);

            foreach (var split in runs.Where(r => r.Spec.Population == PolicyPopulation.Split || r.Spec.Population == PolicyPopulation.SplitOdd))
                primary.Matchups.Add(Matchup(split));
            if (primary.Matchups.Count == 2)
            {
                int wins = primary.Matchups[0].P1Share.Wins + primary.Matchups[1].P1Share.Wins;
                int n = primary.Matchups[0].Matches + primary.Matchups[1].Matches;
                primary.Matchups.Add(new PolicyMatchupLine { Label = "both parities pooled", Matches = n, P1Share = WinInterval.Wilson(wins, n) });
            }

            ApplyCalls(primary, sections);
            if (sweep != null) ApplySweep(primary, sweep);
            if (reference != null && reference.MatchRecords != null && reference.MatchRecords.Count > 0)
            {
                primary.Drift = DriftAgainst(primary, reference.MatchRecords[reference.MatchRecords.Count - 1]);
                primary.RecordHistory = reference.MatchRecords.Select(RecordSummary).ToList();
            }
            return primary;
        }

        private static string RecordSummary(MatchRecord r) =>
            $"{r.AcceptedOn}: \"{r.Note}\" (bal={r.BalanceHash}, seed {r.RunSeed}, {r.Design}; {r.Population}; {(r.Lines != null ? r.Lines.Count : 0)} lines)";

        private static string PopulationName(PolicyPopulation p) => p == PolicyPopulation.Mixed ? "Mixed" : p.ToString();

        /// <summary>
        /// Every win rate a match record stores, from a report: each kit and each class (pooled, and the one-per-class headline when it ran)
        /// under each population the report holds, with its interval.
        /// </summary>
        public static List<MatchRecordLine> RecordLines(MatchReportSection report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var lines = new List<MatchRecordLine>();
            var sections = report.Policies.Count > 0 ? report.Policies : new List<MatchReportSection> { report };
            foreach (var section in sections)
            {
                string pop = PopulationName(section.Population);
                foreach (var k in section.Kits) Add(lines, k.Label, pop, false, k.Win);
                foreach (var c in section.Classes)
                {
                    Add(lines, c.Class.ToString(), pop, true, c.Pooled);
                    Add(lines, c.Class + " (one per class)", pop, true, c.Headline);
                }
            }
            return lines;
        }

        private static void Add(List<MatchRecordLine> lines, string label, string population, bool isClass, WinInterval w)
        {
            if (w.Trials == 0) return;
            lines.Add(new MatchRecordLine { Label = label, Population = population, IsClass = isClass, Rate = w.Rate, Low = w.Low, High = w.High, Trials = w.Trials });
        }

        /// <summary>
        /// The change of every win rate in <paramref name="report"/> from <paramref name="record"/>, in points (positive: higher now), one line per kit
        /// or class and population that both hold. The label carries both rates and both margins, so a change inside the noise reads as one.
        /// A report line, never a test (D7).
        /// </summary>
        public static List<DriftLine> DriftAgainst(MatchReportSection report, MatchRecord record)
        {
            var drift = new List<DriftLine>();
            if (record.Lines == null) return drift;
            var then = new Dictionary<(string, string), MatchRecordLine>();
            foreach (var l in record.Lines) then[(l.Label, l.Population)] = l;
            foreach (var now in RecordLines(report))
            {
                if (!then.TryGetValue((now.Label, now.Population), out var old)) continue;
                drift.Add(new DriftLine
                {
                    Label = $"{now.Label}, {now.Population}: {Pct(now.Rate)} (+/- {((now.High - now.Low) * 50f).ToString("0.0", Inv)}) now, {Pct(old.Rate)} (+/- {((old.High - old.Low) * 50f).ToString("0.0", Inv)}) on {record.AcceptedOn}",
                    Points = (now.Rate - old.Rate) * 100f,
                });
            }
            return drift;
        }

        private static PolicyMatchupLine Matchup(MatchRunOutput run)
        {
            int p1Seat = run.Spec.Population == PolicyPopulation.Split ? 0 : 1;
            int wins = 0;
            foreach (var r in run.Results) if (r.Winner >= 0 && r.Winner % 2 == p1Seat) wins++;
            return new PolicyMatchupLine
            {
                Label = run.Spec.Population == PolicyPopulation.Split ? "P1 on seats 0 and 2" : "P1 on seats 1 and 3",
                Matches = run.Results.Length, P1Share = WinInterval.Wilson(wins, run.Results.Length),
            };
        }

        // One population's tables over its own runs.
        private static MatchReportSection BuildOne(MatchContext context, IReadOnlyList<MatchRunOutput> runs, OracleReferenceSO reference)
        {
            var section = new MatchReportSection
            {
                BalanceHash = context.BalanceHash,
                MapFingerprint = context.Map != null ? context.Map.SceneFingerprint : string.Empty,
                LeftOut = new List<string>(LeftOut),
            };
            var targets = reference != null ? reference.KillTargets : null;
            var lootTargets = reference != null ? reference.ThiefLootTargets : null;

            MatchRunOutput mixed = null, onePerClass = null, seat = null, forced = null;
            foreach (var run in runs)
            {
                switch (run.Spec.Design)
                {
                    case MatchDesignKind.Mixed: mixed = run; break;
                    case MatchDesignKind.OnePerClass: onePerClass = run; break;
                    case MatchDesignKind.SeatCheck: seat = run; break;
                    case MatchDesignKind.ForcedKills: forced = run; break;
                }
                section.Runs.Add(RunInfo(run));
                section.RunSeed = run.Spec.RunSeed;
            }

            Dictionary<ChickenClass, WinInterval> pooled = null;
            if (mixed != null) pooled = BuildMixed(section, context, mixed, targets, lootTargets);
            if (onePerClass != null || pooled != null) BuildClasses(section, context, onePerClass, pooled);
            if (seat != null) BuildSeatCheck(section, context, seat);
            if (forced != null) BuildForced(section, forced, targets);
            return section;
        }

        private static string PopulationText(IReadOnlyList<MatchReportSection> sections)
        {
            var seen = new HashSet<PolicyPopulation>(sections.Select(x => x.Population));
            var missing = new List<string>();
            if (!seen.Contains(PolicyPopulation.P0)) missing.Add("P0");
            if (!seen.Contains(PolicyPopulation.P1)) missing.Add("P1");
            if (!seen.Contains(PolicyPopulation.Mixed)) missing.Add("the mixed population");
            if (missing.Count == 0)
                return "All three populations ran: P0 (the game's own bot, transcribed), P1 (a utility chooser that plays the kit) and a mix drawn per seat. A kit gets a call only when the three agree; otherwise it is policy-sensitive and shows its range.";
            string ran = string.Join(" and ", sections.Select(x => x.Population == PolicyPopulation.Mixed ? "the mixed population" : x.Population.ToString()));
            return $"Only {(ran.Length > 0 ? ran : "nothing")} (P0 is the game's own bot, transcribed) has run so far; missing: {string.Join(", ", missing)}. Every call below is provisional: a kit's call is final only when P0, P1 and the mixed population agree.";
        }

        // The kits' calls, from the populations that ran.
        private static void ApplyCalls(MatchReportSection primary, IReadOnlyList<MatchReportSection> sections)
        {
            MatchReportSection p0 = sections.FirstOrDefault(x => x.Population == PolicyPopulation.P0);
            MatchReportSection p1 = sections.FirstOrDefault(x => x.Population == PolicyPopulation.P1);
            MatchReportSection mx = sections.FirstOrDefault(x => x.Population == PolicyPopulation.Mixed);
            bool all = p0 != null && p1 != null && mx != null;

            for (int k = 0; k < primary.Kits.Count; k++)
            {
                var line = primary.Kits[k];
                line.WinP1 = p1 != null && k < p1.Kits.Count ? p1.Kits[k].Win : WinInterval.Wilson(0, 0);
                line.WinMixed = mx != null && k < mx.Kits.Count ? mx.Kits[k].Win : WinInterval.Wilson(0, 0);
                var rates = new[] { line.Win, line.WinP1, line.WinMixed }.Where(w => w.Trials > 0).Select(w => w.Rate).ToArray();
                line.RangeLow = rates.Length > 0 ? rates.Min() : float.NaN;
                line.RangeHigh = rates.Length > 0 ? rates.Max() : float.NaN;
                if (!all) continue;

                line.Call = MatchRobustness.Call(line.Win, line.WinP1, line.WinMixed);
                string where = $"P0 {Pct(line.Win.Rate)}, P1 {Pct(line.WinP1.Rate)}, mixed {Pct(line.WinMixed.Rate)}";
                line.Reason = line.Call == KitCall.PolicySensitive
                    ? $"the populations disagree ({MatchRobustness.CallOf(line.Win)}, {MatchRobustness.CallOf(line.WinP1)}, {MatchRobustness.CallOf(line.WinMixed)}); range {Pct(line.RangeLow)} to {Pct(line.RangeHigh)}; {where}"
                    : $"P0, P1 and the mixed population agree: {where}";
            }

            foreach (var cls in primary.Classes)
            {
                var none = WinInterval.Wilson(0, 0);
                var c1 = p1?.Classes.FirstOrDefault(c => c.Class == cls.Class);
                var cm = mx?.Classes.FirstOrDefault(c => c.Class == cls.Class);
                cls.HeadlineP1 = c1?.Headline ?? none; cls.PooledP1 = c1?.Pooled ?? none;
                cls.HeadlineMixed = cm?.Headline ?? none; cls.PooledMixed = cm?.Pooled ?? none;
            }
        }

        // The sweep's checks: rank order of the kits against the default point, and how far the rate ran past the band edge of its call.
        private static void ApplySweep(MatchReportSection primary, MatchSweepOutput sweep)
        {
            primary.Sweep = sweep;
            int kits = primary.Kits.Count;
            primary.SweepEdgeCross = new float[kits];
            primary.SweepKitTau = new float[kits];
            bool all = primary.Policies.Count == 3;

            var defaultMixed = sweep.Default.ForPopulation(PolicyPopulation.Mixed);
            var defaultRates = defaultMixed.Kits.Select(k => k.Win.Rate).ToArray();
            // A pair of kits within the noise of the run (the default's margin) has no order to keep.
            float gap = defaultMixed.Kits.Max(k => (k.Win.High - k.Win.Low) * 0.5f) * 2f;
            var swept = sweep.Reports.Concat(sweep.DelayReports).ToArray();
            float tauMin = float.NaN;
            foreach (var report in swept)
            {
                var rates = report.ForPopulation(PolicyPopulation.Mixed).Kits.Select(k => k.Win.Rate).ToArray();
                float tau = MatchRobustness.TauAgainstDefault(defaultRates, rates, gap);
                if (!float.IsNaN(tau) && (float.IsNaN(tauMin) || tau < tauMin)) tauMin = tau;
            }
            primary.SweepTauMin = tauMin;

            for (int k = 0; k < kits; k++)
            {
                var line = primary.Kits[k];
                float worst = 0f;
                float kitTau = float.NaN;
                float low = line.RangeLow, high = line.RangeHigh;
                foreach (var report in swept)
                {
                    var rates = report.ForPopulation(PolicyPopulation.Mixed).Kits.Select(x => x.Win.Rate).ToArray();
                    float rate = rates[k];
                    low = Math.Min(low, rate); high = Math.Max(high, rate);
                    worst = Math.Max(worst, MatchRobustness.EdgeCrossPoints(line.Call, rate));
                    float tau = MatchRobustness.KitTauAgainstDefault(defaultRates, rates, k, gap);
                    if (!float.IsNaN(tau) && (float.IsNaN(kitTau) || tau < kitTau)) kitTau = tau;
                }
                primary.SweepEdgeCross[k] = worst;
                primary.SweepKitTau[k] = kitTau;
                if (!all) continue;
                var tagged = MatchRobustness.Tag(line.Call, kitTau, worst);
                if (tagged == line.Call)
                {
                    if (line.Call != KitCall.PolicySensitive)
                        line.Reason += $"; the sweep holds it (this kit's lowest rank tau {Num(kitTau)}, furthest past a band edge {Num(worst, "0.0")} points)";
                    continue;
                }
                line.Call = tagged;
                line.RangeLow = low; line.RangeHigh = high;
                line.Reason = $"policy-sensitive in the sweep (this kit's lowest rank tau {Num(kitTau)} against a floor of {MatchRobustness.MinTau:0.0}, furthest past a band edge {Num(worst, "0.0")} points against {MatchRobustness.MaxEdgeCrossPoints:0} allowed); range over the populations and the sweep {Pct(low)} to {Pct(high)}";
            }
        }

        private static RunLine RunInfo(MatchRunOutput run)
        {
            int target = 0;
            double seconds = 0.0;
            foreach (var r in run.Results)
            {
                if (r.ReachedTarget) target++;
                seconds += r.EndSeconds;
            }
            int n = run.Results.Length;
            return new RunLine
            {
                Design = run.Spec.Design, Population = run.Spec.Population, Replicates = run.Spec.Replicates, Matches = n,
                RunSeed = run.Spec.RunSeed, ReactionDelay = run.Spec.Params.ReactionDelay, ReactionJitter = run.Spec.Params.ReactionJitter, EndedOnTarget = target, EndedOnClock = n - target,
                MeanEndSeconds = n > 0 ? (float)(seconds / n) : float.NaN, ElapsedMilliseconds = run.ElapsedMilliseconds,
            };
        }

        private static string Provisional(WinInterval w)
        {
            if (w.Trials == 0) return "no matches";
            string where = w.Rate > BandHigh ? "above" : w.Rate < BandLow ? "below" : "inside";
            bool straddles = w.Low < BandLow && w.High > BandLow || w.Low < BandHigh && w.High > BandHigh;
            return (where == "inside" ? "inside the band" : where + " the band") + (straddles ? " (the margin reaches a band edge)" : string.Empty);
        }

        private static Dictionary<ChickenClass, WinInterval> BuildMixed(MatchReportSection section, MatchContext context, MatchRunOutput run, IReadOnlyList<SpoilerKillTarget> targets,
            IReadOnlyList<ThiefLootTarget> lootTargets)
        {
            int kits = context.Kits.Length;
            var appear = new int[kits];
            var wins = new int[kits];
            var foraged = new double[kits];
            var stolen = new double[kits];
            var bounty = new double[kits];
            var contacts = new List<float>();
            var contactsNoAssassin = new List<float>();
            var cutExposed = new Dictionary<(ChickenClass, CutCause), int>();
            var cutWhile = new Dictionary<(ChickenClass, CutCause), int>();
            var cuts = new Dictionary<(ChickenClass, CutCause), int>();
            var classAppear = new Dictionary<ChickenClass, int>();
            var classWins = new Dictionary<ChickenClass, int>();

            for (int i = 0; i < run.Results.Length; i++)
            {
                var cell = run.Cells[i];
                var r = run.Results[i];
                if (r.CornerDecided) section.CornerDecided++;
                float contact = -1f;
                for (int s = 0; s < cell.KitOfSeat.Length; s++)
                {
                    int k = cell.KitOfSeat[s];
                    if (k < 0) continue;
                    var cls = context.Kits[k].Class;
                    appear[k]++;
                    Bump(classAppear, cls, 1);
                    if (r.Winner == s) { wins[k]++; Bump(classWins, cls, 1); }
                    var c = r.Counters[s];
                    foraged[k] += c.Foraged; stolen[k] += c.Stolen; bounty[k] += c.Bounty;
                    if (c.FirstContact >= 0f) contact = Math.Max(contact, c.FirstContact);
                    foreach (var cause in Causes)
                    {
                        Bump(cutExposed, (cls, cause), c.ExposedByCause[(int)cause]);
                        Bump(cutWhile, (cls, cause), c.CutWhileExposedByCause[(int)cause]);
                        Bump(cuts, (cls, cause), c.CutsByCause[(int)cause]);
                    }
                }
                contacts.Add(contact);
                if (!cell.KitOfSeat.Any(k => k >= 0 && context.Kits[k].Class == ChickenClass.Assassin)) contactsNoAssassin.Add(contact);
            }
            section.MixedMatches = run.Results.Length;
            var seatWins = new int[OracleMapSO.SeatCount];
            foreach (var res in run.Results) if (res.Winner >= 0) seatWins[res.Winner]++;
            for (int st = 0; st < seatWins.Length; st++)
            {
                var w = WinInterval.Wilson(seatWins[st], run.Results.Length);
                section.MixedSeats.Add(new SeatLine { Seat = st, Win = w, WinExCorner = w, Flagged = w.Low > Target || w.High < Target });
            }

            for (int k = 0; k < kits; k++)
            {
                var win = WinInterval.Wilson(wins[k], appear[k]);
                section.Kits.Add(new KitWinLine
                {
                    Label = context.Kits[k].Label, Class = context.Kits[k].Class, Win = win, Call = KitCall.NoCall,
                    Reason = "provisional, one population: " + Provisional(win) + "; final only when P0, P1 and the mixed population agree (missing populations: P1, the mixed population).",
                });
                int n = Math.Max(1, appear[k]);
                section.Economy.Add(new EconomyShareLine { Label = context.Kits[k].Label, Foraged = (float)(foraged[k] / n), Stolen = (float)(stolen[k] / n), Bounty = (float)(bounty[k] / n) });
            }

            section.FirstContact = Contact(contacts);
            section.FirstContactNoAssassin = Contact(contactsNoAssassin);
            BuildHeadToHead(section, context, run);
            BuildThief(section, context, run, lootTargets);
            BuildWarriors(section, context, run);
            BuildPeckCuts(section, context, cutExposed, cutWhile, cuts, classAppear);
            BuildPlayedKills(section, context, run, targets);
            BuildKillLoot(section, context, run);
            var pooled = new Dictionary<ChickenClass, WinInterval>();
            foreach (var cls in classAppear.Keys)
                pooled[cls] = WinInterval.Wilson(classWins.TryGetValue(cls, out int w) ? w : 0, classAppear[cls]);
            return pooled;
        }

        private static void Bump<K>(Dictionary<K, int> map, K key, int by) { map.TryGetValue(key, out int v); map[key] = v + by; }

        private static ContactLine Contact(List<float> times)
        {
            var seen = times.Where(t => t >= 0f).OrderBy(t => t).ToList();
            var line = new ContactLine { Never = times.Count - seen.Count, Matches = times.Count, Median = float.NaN, Q1 = float.NaN, Q3 = float.NaN };
            if (seen.Count == 0) return line;
            line.Median = Quantile(seen, 0.5f);
            line.Q1 = Quantile(seen, 0.25f);
            line.Q3 = Quantile(seen, 0.75f);
            return line;
        }

        // Linear interpolation between order statistics.
        private static float Quantile(List<float> sorted, float q)
        {
            if (sorted.Count == 1) return sorted[0];
            float at = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(at);
            int hi = Math.Min(sorted.Count - 1, lo + 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (at - lo);
        }

        private static void BuildHeadToHead(MatchReportSection section, MatchContext context, MatchRunOutput run)
        {
            foreach (var group in context.Kits.Select((k, i) => (k, i)).GroupBy(t => t.k.Class).OrderBy(g => g.Key))
            {
                var pair = group.Select(t => t.i).ToArray();
                if (pair.Length != 2) continue;
                float ahead = 0f;
                int matches = 0;
                for (int i = 0; i < run.Results.Length; i++)
                {
                    var seats = run.Cells[i].KitOfSeat;
                    int a = Array.IndexOf(seats, pair[0]), b = Array.IndexOf(seats, pair[1]);
                    if (a < 0 || b < 0) continue;
                    var banked = run.Results[i].Banked;
                    matches++;
                    ahead += banked[a] > banked[b] ? 1f : banked[a] < banked[b] ? 0f : 0.5f;
                }
                section.HeadToHead.Add(new HeadToHeadLine
                {
                    Class = group.Key, KitA = context.Kits[pair[0]].Label, KitB = context.Kits[pair[1]].Label,
                    AAhead = matches > 0 ? ahead / matches : float.NaN, Matches = matches,
                });
            }
        }

        private static void BuildThief(MatchReportSection section, MatchContext context, MatchRunOutput run, IReadOnlyList<ThiefLootTarget> lootTargets)
        {
            // The Thief is the kit of the Thief's passive (its loot facts say so, whatever the numbers), or one whose steal factor is above 1.
            int thief = -1;
            for (int k = 0; k < context.Kits.Length; k++)
                if (context.Kits[k].Passive.Loot.IsThief || context.Kits[k].Passive.StealFactor > 1f) { thief = k; break; }
            if (thief < 0) return;
            bool lootOn = context.Kits[thief].Passive.Loot.Active;

            int kits = context.Kits.Length;
            var from = new double[kits];
            var meetings = new int[kits];
            int matches = 0, steals = 0;
            double stolen = 0, foraged = 0, banked = 0;
            double drawn = 0, taken = 0, lost = 0, refused = 0, takeFood = 0, bountyFood = 0, roomLost = 0, lootBanked = 0, lootLost = 0, trips = 0, overlapSeconds = 0;
            int stacked = 0, targetWins = 0, targetWinsStacked = 0, three = 0, targetWinsThree = 0;
            int wonOnPrize = 0, overlapMatches = 0, withSpoiler = 0, withSpoilerOverlap = 0;
            var byTaken = new SortedDictionary<int, List<(MatchResult, int)>>();
            int mostTaken = 0;
            for (int i = 0; i < run.Results.Length; i++)
            {
                var seats = run.Cells[i].KitOfSeat;
                int t = Array.IndexOf(seats, thief);
                if (t < 0) continue;
                var c = run.Results[i].Counters[t];
                matches++;
                steals += c.Steals;
                stolen += c.Stolen;
                foraged += c.Foraged;
                banked += run.Results[i].Banked[t];
                drawn += c.LootOffered; taken += c.LootTaken; lost += c.LootTimeouts; refused += c.LootRefused;
                takeFood += c.LootTakeFood; bountyFood += c.LootBountyFood; roomLost += c.LootLostToRoom; lootBanked += c.LootBankedFood; lootLost += c.LootLostToSteals;
                trips += c.Trips;
                if (c.MaxMarksPerTrip >= 2) stacked++;
                if (c.MaxMarksPerTrip >= 3) three++;
                if (run.Results[i].Winner == t && run.Results[i].ReachedTarget) { targetWins++; if (c.MaxMarksPerTrip >= 2) targetWinsStacked++; if (c.MaxMarksPerTrip >= 3) targetWinsThree++; }
                overlapSeconds += c.LootHuntOverlapSeconds;
                if (c.LootHuntOverlapSeconds > 0f) overlapMatches++;
                bool spoilerPlays = false;
                for (int o = 0; o < seats.Length; o++)
                    if (o != t && seats[o] >= 0 && MatchDesigns.HoldsDeathMark(context.Kits[seats[o]])) spoilerPlays = true;
                if (spoilerPlays) { withSpoiler++; if (c.LootHuntOverlapSeconds > 0f) withSpoilerOverlap++; }
                var res = run.Results[i];
                if (res.Winner == t && res.WinnerWithoutSpoilerBonus != t) wonOnPrize++;
                if (lootOn)
                {
                    mostTaken = Math.Max(mostTaken, c.LootTaken);
                    if (!byTaken.TryGetValue(c.LootTaken, out var list)) byTaken[c.LootTaken] = list = new List<(MatchResult, int)>();
                    list.Add((res, t));
                }
                for (int v = 0; v < seats.Length; v++)
                {
                    if (v == t || seats[v] < 0) continue;
                    from[seats[v]] += c.StolenFromSeat[v];
                    meetings[seats[v]]++;
                }
            }
            if (matches == 0) return;
            section.ThiefIncome = new ThiefSummary
            {
                Kit = context.Kits[thief].Label, Matches = matches, StealsPerMatch = steals / (float)matches, StolenPerMatch = (float)(stolen / matches),
                ForagedPerMatch = (float)(foraged / matches), BankedPerMatch = (float)(banked / matches),
                LootActive = lootOn,
                MarksDrawnPerMatch = (float)(drawn / matches), MarksTakenPerMatch = (float)(taken / matches), MarksLostPerMatch = (float)(lost / matches),
                MarksRefusedPerMatch = (float)(refused / matches), LootTakeFoodPerMatch = (float)(takeFood / matches),
                LootBountyFoodPerMatch = (float)(bountyFood / matches), LootLostToRoomPerMatch = (float)(roomLost / matches),
                LootBankedPerMatch = (float)(lootBanked / matches), LootLostPerMatch = (float)(lootLost / matches), TripsPerMatch = (float)(trips / matches),
                StackedMarksShare = stacked / (float)matches, WinsOnTarget = targetWins,
                WinsWithStackedMarks = targetWins > 0 ? targetWinsStacked / (float)targetWins : float.NaN,
                ThreeInOneLoadShare = three / (float)matches, WinsWithThreeInOneLoad = targetWins > 0 ? targetWinsThree / (float)targetWins : float.NaN,
                WonOnPrize = wonOnPrize / (float)matches, OverlapShare = overlapMatches / (float)matches,
                OverlapSecondsPerMatch = (float)(overlapSeconds / matches),
                OverlapShareWithSpoiler = withSpoiler > 0 ? withSpoilerOverlap / (float)withSpoiler : float.NaN, MatchesWithSpoiler = withSpoiler,
            };
            if (lootOn)
            {
                // Every row up to the largest target, so a row with no matches still says "no data" against its target.
                int last = Math.Max(mostTaken, lootTargets != null && lootTargets.Count > 0 ? lootTargets.Max(x => x.Steals) : 0);
                for (int k = 0; k <= last; k++)
                {
                    byTaken.TryGetValue(k, out var list);
                    section.LootSteals.Add(MatchStats.LootStealsToWin(list ?? new List<(MatchResult, int)>(), k, lootTargets));
                }
            }
            for (int k = 0; k < kits; k++)
            {
                if (k == thief || meetings[k] == 0) continue;
                section.Thief.Add(new ThiefLine
                {
                    Victim = context.Kits[k].Label, StolenPerMatch = (float)(from[k] / meetings[k]), Share = stolen > 0 ? (float)(from[k] / stolen) : 0f,
                });
            }
        }

        private static void BuildWarriors(MatchReportSection section, MatchContext context, MatchRunOutput run)
        {
            for (int k = 0; k < context.Kits.Length; k++)
            {
                var kit = context.Kits[k];
                if (kit.Class != ChickenClass.Warrior) continue;
                int n = 0, stealCasts = 0, hitTakes = 0, cashes = 0;
                double takes = 0, hitFood = 0, lockRef = 0, noFood = 0, roomRef = 0, hitLost = 0, bitesCast = 0, bitesCargo = 0, freshHits = 0, biteFoodCast = 0, biteFoodCargo = 0,
                    biteLeftPile = 0, biteLeftVictim = 0, spent = 0, pecksBitten = 0, biteLost = 0, earned = 0, decayed = 0, cleared = 0, cashed = 0, bonus = 0,
                    stealFood = 0, worstSum = 0;
                var worst = new List<float>();
                for (int i = 0; i < run.Results.Length; i++)
                {
                    int seat = Array.IndexOf(run.Cells[i].KitOfSeat, k);
                    if (seat < 0) continue;
                    var r = run.Results[i];
                    var c = r.Counters[seat];
                    n++;
                    takes += c.HitTakes; hitFood += c.HitFood; lockRef += c.HitRefusedLock; noFood += c.HitRefusedNoFood; roomRef += c.HitRefusedRoom; hitLost += c.HitLostFood;
                    hitTakes += c.HitTakes;
                    freshHits += c.FreshHits;
                    bitesCast += c.BitesFromCast; bitesCargo += c.BitesFromCargo; biteFoodCast += c.BiteFoodFromCast; biteFoodCargo += c.BiteFoodFromCargo;
                    biteLeftPile += c.BiteLeftInPile; biteLeftVictim += c.BiteLeftWithVictim; spent += c.BitesSpent; pecksBitten += c.PecksBitten; biteLost += c.BiteLostFood;
                    earned += c.TrophiesEarned; decayed += c.TrophiesDecayed; cleared += c.TrophiesCleared; cashes += c.TrophyCashes; cashed += c.TrophiesCashed; bonus += c.TrophyBonusFood;
                    stealCasts += c.Steals; stealFood += c.StealFood;
                    worstSum += c.WorstVictimLoss5s;
                    worst.Add(c.WorstVictimLoss5s);
                }
                if (n == 0) continue;
                worst.Sort();
                var win = k < section.Kits.Count ? section.Kits[k].Win : WinInterval.Wilson(0, 0);
                section.Warriors.Add(new WarriorLine
                {
                    Kit = kit.Label, Matches = n, Win = win,
                    HitActive = kit.Passive.HitSteal.Active, BiteActive = kit.Passive.Bite.Active, TrophyKit = kit.Passive.Trophy.IsChampion,
                    HitTakesPerMatch = (float)(takes / n), HitFoodPerMatch = (float)(hitFood / n), HitRefusedLockPerMatch = (float)(lockRef / n),
                    HitRefusedNoFoodPerMatch = (float)(noFood / n), HitRefusedRoomPerMatch = (float)(roomRef / n), HitLostFoodPerMatch = (float)(hitLost / n),
                    FreshHitsPerMatch = (float)(freshHits / n), BitesFromCastPerMatch = (float)(bitesCast / n), BitesFromCargoPerMatch = (float)(bitesCargo / n),
                    BiteFoodFromCastPerMatch = (float)(biteFoodCast / n), BiteFoodFromCargoPerMatch = (float)(biteFoodCargo / n),
                    BiteLeftInPilePerMatch = (float)(biteLeftPile / n), BiteLeftWithVictimPerMatch = (float)(biteLeftVictim / n), BitesSpentPerMatch = (float)(spent / n),
                    PecksBittenPerMatch = (float)(pecksBitten / n), BiteLostFoodPerMatch = (float)(biteLost / n),
                    TrophiesEarnedPerMatch = (float)(earned / n), TrophiesDecayedPerMatch = (float)(decayed / n), TrophiesClearedPerMatch = (float)(cleared / n),
                    TrophyCashesPerMatch = (float)(cashes / (double)n), TrophyBonusFoodPerMatch = (float)(bonus / n),
                    TrophiesPerCash = cashes > 0 ? (float)(cashed / cashes) : float.NaN,
                    StealsPerMatch = stealCasts / (float)n, StealFoodPerMatch = (float)(stealFood / n),
                    MeanStealSize = stealCasts > 0 ? (float)(stealFood / stealCasts) : float.NaN,
                    MeanHitSize = hitTakes > 0 ? (float)(hitFood / hitTakes) : float.NaN,
                    IdentityFoodPerMatch = (float)((hitFood + biteFoodCast + biteFoodCargo + bonus) / n),
                    WorstLossMean = (float)(worstSum / n), WorstLossP95 = Quantile(worst, 0.95f),
                });
            }
        }

        private static void BuildPeckCuts(MatchReportSection section, MatchContext context, Dictionary<(ChickenClass, CutCause), int> exposed,
            Dictionary<(ChickenClass, CutCause), int> cutWhile, Dictionary<(ChickenClass, CutCause), int> cuts, Dictionary<ChickenClass, int> classAppear)
        {
            foreach (var group in context.Kits.GroupBy(k => k.Class).OrderBy(g => g.Key))
            {
                var cls = group.Key;
                float peck = group.First().Farm.PeckAmount;
                classAppear.TryGetValue(cls, out int appearances);
                foreach (var cause in Causes)
                {
                    exposed.TryGetValue((cls, cause), out int ex);
                    cutWhile.TryGetValue((cls, cause), out int cw);
                    cuts.TryGetValue((cls, cause), out int cut);
                    section.PeckCuts.Add(new PeckCutLine
                    {
                        Class = cls, Cause = cause, Exposed = ex, Cut = cut, Hazard = PeckCutHazard(ex, cw),
                        FoodLostPerMatch = appearances > 0 ? cut * peck / appearances : 0f,
                    });
                }
            }
        }

        /// <summary>The hazard of a cut: cuts among the casts that began exposed to that cutter, over those casts. NaN with none exposed.</summary>
        public static float PeckCutHazard(int exposed, int cutWhileExposed) => exposed > 0 ? cutWhileExposed / (float)exposed : float.NaN;

        private static void BuildPlayedKills(MatchReportSection section, MatchContext context, MatchRunOutput run, IReadOnlyList<SpoilerKillTarget> targets)
        {
            var byKills = new SortedDictionary<int, List<(MatchResult, int)>>();
            int most = 0;
            for (int i = 0; i < run.Results.Length; i++)
            {
                var seats = run.Cells[i].KitOfSeat;
                for (int s = 0; s < seats.Length; s++)
                {
                    if (seats[s] < 0 || !MatchDesigns.HoldsDeathMark(context.Kits[seats[s]])) continue;
                    int kills = run.Results[i].Kills[s];
                    most = Math.Max(most, kills);
                    if (!byKills.TryGetValue(kills, out var list)) byKills[kills] = list = new List<(MatchResult, int)>();
                    list.Add((run.Results[i], s));
                }
            }
            if (byKills.Count == 0) return;
            // Every row up to the largest target kill count, so a row with no matches still says "no data" against its target.
            int last = Math.Max(most, targets != null && targets.Count > 0 ? targets.Max(t => t.Kills) : 0);
            for (int k = 0; k <= last; k++)
            {
                byKills.TryGetValue(k, out var list);
                section.KillsPlayed.Add(MatchStats.KillsToWin(list ?? new List<(MatchResult, int)>(), k, ForcedVictims.Empty, targets));
            }
        }

        private static void BuildKillLoot(MatchReportSection section, MatchContext context, MatchRunOutput run)
        {
            var kills = new Dictionary<(int Kit, ChickenClass Victim), (int Kills, double Cargo, double Gained, double Unfit)>();
            for (int i = 0; i < run.Results.Length; i++)
            {
                var seats = run.Cells[i].KitOfSeat;
                for (int s = 0; s < seats.Length; s++)
                {
                    if (seats[s] < 0) continue;
                    var c = run.Results[i].Counters[s];
                    if (c.KillsOnSeat == null) continue;
                    for (int v = 0; v < seats.Length; v++)
                    {
                        if (v == s || seats[v] < 0 || c.KillsOnSeat[v] == 0) continue;
                        var key = (seats[s], context.Kits[seats[v]].Class);
                        kills.TryGetValue(key, out var sum);
                        kills[key] = (sum.Kills + c.KillsOnSeat[v], sum.Cargo + c.KillCargoFromSeat[v], sum.Gained + c.KillGainedFromSeat[v], sum.Unfit + c.KillUnfitFromSeat[v]);
                    }
                }
            }
            foreach (var pair in kills.OrderBy(p => p.Key.Kit).ThenBy(p => p.Key.Victim))
            {
                var v = pair.Value;
                section.KillLoot.Add(new KillLootLine
                {
                    Killer = context.Kits[pair.Key.Kit].Label, Victim = pair.Key.Victim, Kills = v.Kills,
                    CargoPerKill = (float)(v.Cargo / v.Kills), GainedPerKill = (float)(v.Gained / v.Kills), UnfitPerKill = (float)(v.Unfit / v.Kills),
                });
            }
        }

        private static void BuildClasses(MatchReportSection section, MatchContext context, MatchRunOutput onePerClass, Dictionary<ChickenClass, WinInterval> pooled)
        {
            var wins = new Dictionary<ChickenClass, int>();
            int cells = 0;
            if (onePerClass != null)
            {
                cells = onePerClass.Results.Length;
                for (int i = 0; i < cells; i++)
                {
                    int k = onePerClass.Cells[i].KitOfSeat[onePerClass.Results[i].Winner];
                    Bump(wins, context.Kits[k].Class, 1);
                }
            }
            foreach (var cls in context.Kits.Select(k => k.Class).Distinct().OrderBy(c => c))
            {
                wins.TryGetValue(cls, out int w);
                section.Classes.Add(new ClassWinLine
                {
                    Class = cls,
                    Headline = onePerClass != null ? WinInterval.Wilson(w, cells) : WinInterval.Wilson(0, 0),
                    Pooled = pooled != null && pooled.TryGetValue(cls, out var p) ? p : WinInterval.Wilson(0, 0),
                });
            }
        }

        private static void BuildSeatCheck(MatchReportSection section, MatchContext context, MatchRunOutput run)
        {
            int seats = OracleMapSO.SeatCount;
            var wins = new int[seats];
            var winsEx = new int[seats];
            int exMatches = 0;
            var byKit = new Dictionary<int, SeatKitLine>();
            for (int i = 0; i < run.Results.Length; i++)
            {
                var r = run.Results[i];
                int kit = run.Cells[i].KitOfSeat[0];
                if (!byKit.TryGetValue(kit, out var line))
                    byKit[kit] = line = new SeatKitLine { Kit = context.Kits[kit].Label, WinsBySeat = new int[seats] };
                line.Matches++;
                if (r.Winner >= 0) { wins[r.Winner]++; line.WinsBySeat[r.Winner]++; }
                if (r.CornerDecided) { section.SeatCheckCornerDecided++; line.CornerDecided++; continue; }
                exMatches++;
                if (r.Winner >= 0) winsEx[r.Winner]++;
            }
            section.SeatCheckMatches = run.Results.Length;
            section.SeatByKit.AddRange(byKit.OrderBy(p => p.Key).Select(p => p.Value));
            for (int s = 0; s < seats; s++)
            {
                var w = WinInterval.Wilson(wins[s], run.Results.Length);
                var ex = WinInterval.Wilson(winsEx[s], exMatches);
                section.SeatCheck.Add(new SeatLine { Seat = s, Win = w, WinExCorner = ex, Flagged = ex.Trials > 0 && (ex.Low > Target || ex.High < Target) });
            }
        }

        private static void BuildForced(MatchReportSection section, MatchRunOutput run, IReadOnlyList<SpoilerKillTarget> targets)
        {
            for (int k = 0; k <= MatchDesigns.MaxForcedKills; k++)
                foreach (var victims in new[] { ForcedVictims.Empty, ForcedVictims.Loaded })
                {
                    var rows = new List<(MatchResult, int)>();
                    for (int i = 0; i < run.Results.Length; i++)
                    {
                        var f = run.Cells[i].Forced;
                        if (f.Kills == k && f.Victims == victims && f.Spoiler >= 0) rows.Add((run.Results[i], f.Spoiler));
                    }
                    section.KillsToWin.Add(MatchStats.KillsToWin(rows, k, victims, targets));
                }
        }

        // ------------------------------------------------------------------ markdown

        private static string Pct(float v) => float.IsNaN(v) ? "n/a" : (v * 100f).ToString("0.0", Inv) + " %";
        private static string Pm(WinInterval w) => w.Trials == 0 ? "n/a" : "+/- " + ((w.High - w.Low) * 50f).ToString("0.0", Inv);
        private static string Rate(WinInterval w) => Pct(w.Rate) + " (" + Pm(w) + ", " + w.Trials + " seats)";
        private static string Num(float v, string f = "0.00") => float.IsNaN(v) ? "n/a" : v.ToString(f, Inv);

        private static string Name(PolicyPopulation p) => p == PolicyPopulation.Mixed ? "mixed" : p.ToString();
        private static string Cell(WinInterval w) => w.Trials == 0 ? "n/a" : $"{Pct(w.Rate)} ({Pm(w)})";

        public static string Markdown(MatchReportSection r)
        {
            var sb = new StringBuilder();
            var pops = r.Policies.Count > 0 ? r.Policies : new List<MatchReportSection> { r };
            sb.AppendLine("# Match simulator report (Balance Oracle v2, part 2)");
            sb.AppendLine();
            sb.AppendLine($"Balance `bal={r.BalanceHash}`, map `{r.MapFingerprint}`, run seed `0x{r.RunSeed:x16}`.");
            sb.AppendLine();
            sb.AppendLine("## Read this first");
            sb.AppendLine();
            sb.AppendLine($"- **{r.PopulationNote}**");
            sb.AppendLine($"- Balanced means each kit wins {BandLow * 100:0} to {BandHigh * 100:0} % of mixed four-player matches AND each class does (target {Target * 100:0} %). The oracle is a guide: it reports, it never blocks.");
            sb.AppendLine("- Every rate carries its 95 % Wilson margin (+/- points), which counts the matches as independent trials. They are not quite: the only chance in a match is the comeback event and the random extra reaction delay of each intent, so the margin is a floor on the real uncertainty. A rate whose margin reaches a band edge is not a verdict on its own.");
            var first = r.Runs.FirstOrDefault();
            if (first != null)
                sb.AppendLine($"- **Reaction delay baseline: {Num(first.ReactionDelay)} s plus a random 0 to {Num(first.ReactionJitter)} s per intent** (the jitter makes replicates differ). P0 is the game's bot and the bot has no delay, so it runs at 0; the delay is the sweep's model of human slowness (0, 0.1 and 0.3 s, and 0 to 0.6 s in the hypercube). The first reports (to 2026-11-22) ran P0 at 0.30 s plus the jitter, which halves the Spoiler's kills; reproduce them with `MatchRunSpec.WithJitter(PolicyParams.Default, MatchRunSpec.DefaultReactionJitter)` (its `ReactionDelay` is 0.3 s).");
            foreach (var p in pops)
            {
                var mixed = p.Runs.FirstOrDefault(x => x.Design == MatchDesignKind.Mixed);
                if (mixed != null)
                    sb.AppendLine($"- Mixed design, {Name(p.Population)}: {mixed.Matches} matches; {mixed.EndedOnTarget} ended on the 40 food target ({Pct(mixed.EndedOnTarget / (float)Math.Max(1, mixed.Matches))}) and {mixed.EndedOnClock} on the clock; mean match length {Num(mixed.MeanEndSeconds, "0.0")} s.");
            }
            sb.AppendLine();
            foreach (var p in pops)
                if (p.MixedSeats.Count > 0)
                    sb.AppendLine($"- Mixed design, {Name(p.Population)}, share of matches won from each seat: " + string.Join(", ", p.MixedSeats.Select(x => $"seat {x.Seat} {Pct(x.Win.Rate)} ({Pm(x.Win)})")) + ".");
            sb.AppendLine("  The designs turn the table so each kit sits in each seat about as often; a share away from 25 % beyond its margin is a seat bias (see the seat check).");
            sb.AppendLine();
            sb.AppendLine("### Runs");
            sb.AppendLine();
            sb.AppendLine("| Design | Population | R | Reaction delay | Matches | Ended on target | Ended on clock | Mean length (s) | Run time |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var x in r.Runs)
                sb.AppendLine($"| {x.Design} | {x.Population} | {x.Replicates} | {Num(x.ReactionDelay)} s + 0 to {Num(x.ReactionJitter)} s | {x.Matches} | {x.EndedOnTarget} | {x.EndedOnClock} | {Num(x.MeanEndSeconds, "0.0")} | {(x.ElapsedMilliseconds / 1000.0).ToString("0.0", Inv)} s |");
            sb.AppendLine();

            sb.AppendLine("## What this model leaves out");
            sb.AppendLine();
            sb.AppendLine("| # | Left out, why and the expected error |");
            sb.AppendLine("|---|---|");
            for (int i = 0; i < r.LeftOut.Count; i++) sb.AppendLine($"| {i + 1} | {r.LeftOut[i]} |");
            sb.AppendLine();

            sb.AppendLine("## Win rate per kit (mixed design)");
            sb.AppendLine();
            sb.AppendLine("The share of the seats a kit sat in that won the match. One seat in four wins, so over a whole design the average is 25 %. A kit has a call only when P0, P1 and the mixed population agree (and the sweep holds it); a policy-sensitive kit shows its range.");
            sb.AppendLine();
            bool three = pops.Count == 3;
            if (three)
            {
                sb.AppendLine("| Kit | Class | P0 | P1 | Mixed | Range | Call |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var k in r.Kits)
                    sb.AppendLine($"| {k.Label} | {k.Class} | {Cell(k.Win)} | {Cell(k.WinP1)} | {Cell(k.WinMixed)} | {Pct(k.RangeLow)} to {Pct(k.RangeHigh)} | **{(k.Call == KitCall.PolicySensitive ? "policy-sensitive" : k.Call.ToString())}**: {k.Reason} |");
            }
            else
            {
                sb.AppendLine("| Kit | Class | Win rate | Margin | Seats | Call |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var k in r.Kits)
                    sb.AppendLine($"| {k.Label} | {k.Class} | {Pct(k.Win.Rate)} | {Pm(k.Win)} | {k.Win.Trials} | {k.Call}: {k.Reason} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Win rate per class");
            sb.AppendLine();
            sb.AppendLine("Headline: one kit per class, every class in every match. Second line: a seat of that class in the mixed design.");
            sb.AppendLine();
            if (three)
            {
                sb.AppendLine("| Class | Headline P0 | Headline P1 | Headline mixed | Pooled P0 | Pooled P1 | Pooled mixed |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var c in r.Classes)
                    sb.AppendLine($"| {c.Class} | {Cell(c.Headline)} | {Cell(c.HeadlineP1)} | {Cell(c.HeadlineMixed)} | {Cell(c.Pooled)} | {Cell(c.PooledP1)} | {Cell(c.PooledMixed)} |");
            }
            else
            {
                sb.AppendLine("| Class | Headline (one per class) | Pooled from mixed |");
                sb.AppendLine("|---|---|---|");
                foreach (var c in r.Classes) sb.AppendLine($"| {c.Class} | {Rate(c.Headline)} | {Rate(c.Pooled)} |");
            }
            sb.AppendLine();

            if (r.HeadToHead.Count > 0)
            {
                sb.AppendLine("### Class head-to-head, P0 (mixed matches holding both kits of a class)");
                sb.AppendLine();
                sb.AppendLine("| Class | Kit A | Kit B | A banked more | Matches |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var h in r.HeadToHead) sb.AppendLine($"| {h.Class} | {h.KitA} | {h.KitB} | {Pct(h.AAhead)} | {h.Matches} |");
                sb.AppendLine();
            }

            sb.AppendLine("## P1 against P0, head to head (a diagnostic line, not a test)");
            sb.AppendLine();
            if (r.Matchups.Count == 0) sb.AppendLine("Not run.");
            else
            {
                sb.AppendLine("Two P1 seats and two P0 seats in one match, the mixed design's kits, the table turned one seat per cell so each kit plays both policies. Equal strength is 50 %.");
                sb.AppendLine();
                sb.AppendLine("| Seats | Matches | Won by a P1 seat | Margin |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var m in r.Matchups) sb.AppendLine($"| {m.Label} | {m.Matches} | {Pct(m.P1Share.Rate)} | {Pm(m.P1Share)} |");
            }
            sb.AppendLine();

            if (r.Sweep != null) SweepMarkdown(sb, r);

            sb.AppendLine("## Seat check (each kit four times, a full mirror)");
            sb.AppendLine();
            sb.AppendLine("Every seat should win 25 % within its margin; outside it is a model or map bug, shown here and never a test.");
            sb.AppendLine();
            foreach (var p in pops)
            {
                sb.AppendLine($"### {Name(p.Population)}");
                sb.AppendLine();
                if (p.SeatCheck.Count == 0) { sb.AppendLine("Not run."); sb.AppendLine(); continue; }
                sb.AppendLine("| Seat | Win rate, all matches | Margin | Win rate, corner-decided matches left out | Margin | Flag (on the second) |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var s in p.SeatCheck)
                    sb.AppendLine($"| {s.Seat} | {Pct(s.Win.Rate)} | {Pm(s.Win)} | {Pct(s.WinExCorner.Rate)} | {Pm(s.WinExCorner)} | {(s.Flagged ? "OUTSIDE 25 %" : "ok")} |");
                sb.AppendLine();
                sb.AppendLine($"{p.SeatCheckMatches} matches ({p.SeatCheck[0].WinExCorner.Trials} not decided by the corner rule); {p.SeatCheckCornerDecided} were decided by it: nothing separated the four chickens, so the lowest corner won. That is the game's own seat bias and it is left out of the flag.");
                sb.AppendLine();
                sb.AppendLine("| Mirror of | Matches | Seat 0 | Seat 1 | Seat 2 | Seat 3 | Corner-decided |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var k in p.SeatByKit)
                    sb.AppendLine($"| {k.Kit} | {k.Matches} | {k.WinsBySeat[0]} | {k.WinsBySeat[1]} | {k.WinsBySeat[2]} | {k.WinsBySeat[3]} | {k.CornerDecided} |");
                sb.AppendLine();
            }

            sb.AppendLine("## Spoiler: kills to win");
            sb.AppendLine();
            sb.AppendLine("Targets are data on `OracleReference.asset` (starting values 90 / 80 / 20 %); a label is a report line, nothing is tested on a rate. The forced design takes the other seats from the population it ran under (P0).");
            sb.AppendLine();
            foreach (var p in pops)
            {
                sb.AppendLine($"### From played matches, {Name(p.Population)} (kills the policies really made, mixed design)");
                sb.AppendLine();
                sb.AppendLine("Survivor bias applies: a Spoiler who kills often is usually already doing well, so these rates are not the worth of a kit's kill. The label compares the row with the `KillTargets` data.");
                sb.AppendLine();
                KillTable(sb, p.KillsPlayed, false);
                if (p.KillLoot.Count > 0)
                {
                    sb.AppendLine($"Food per kill by victim class, {Name(p.Population)} (the kill loot is ordinary cargo: the victim's beak first, then the bounty, held to the killer's free room):");
                    sb.AppendLine();
                    sb.AppendLine("| Killer | Victim class | Kills | Victim's beak | Gained | Did not fit |");
                    sb.AppendLine("|---|---|---|---|---|---|");
                    foreach (var l in p.KillLoot)
                        sb.AppendLine($"| {l.Killer} | {l.Victim} | {l.Kills} | {Num(l.CargoPerKill, "0.0")} | {Num(l.GainedPerKill, "0.0")} | {Num(l.UnfitPerKill, "0.0")} |");
                    sb.AppendLine();
                }
            }
            sb.AppendLine("### From the forced design (exactly k kills on schedule, same victims every time)");
            sb.AppendLine();
            KillTable(sb, r.KillsToWin, true);

            sb.AppendLine("## Thief");
            sb.AppendLine();
            bool anyThief = false;
            foreach (var p in pops)
            {
                if (p.ThiefIncome == null) continue;
                anyThief = true;
                var t = p.ThiefIncome;
                sb.AppendLine($"- {Name(p.Population)}: {t.Kit}: {t.Matches} matches; {Num(t.StealsPerMatch)} steals and {Num(t.StolenPerMatch)} food stolen per match, {Num(t.ForagedPerMatch)} foraged, {Num(t.BankedPerMatch)} banked.");
            }
            if (!anyThief) sb.AppendLine("No Thief kit in these matches.");
            else
            {
                foreach (var p in pops)
                {
                    var t = p.ThiefIncome;
                    if (t == null || !t.LootActive) continue;
                    sb.AppendLine($"- Loot mark, {Name(p.Population)}: {Num(t.MarksDrawnPerMatch)} marks drawn, {Num(t.MarksTakenPerMatch)} consumed, {Num(t.MarksLostPerMatch)} moved on or lost unpaid, {Num(t.MarksRefusedPerMatch)} refused (wall or shell) per match; marked take {Num(t.LootTakeFoodPerMatch, "0.0")} + bounty {Num(t.LootBountyFoodPerMatch, "0.0")} food into the beak ({Num(t.LootLostToRoomPerMatch, "0.0")} did not fit), {Num(t.LootBankedPerMatch, "0.0")} of it banked and {Num(t.LootLostPerMatch, "0.0")} lost to rivals' steals (pro rata of the cargo); {Num(t.TripsPerMatch)} trips home per match, two or more marked steals in one load in {Pct(t.StackedMarksShare)} of matches and in {Pct(t.WinsWithStackedMarks)} of its {t.WinsOnTarget} wins on the target; three or more in one load in {Pct(t.ThreeInOneLoadShare)} of matches and {Pct(t.WinsWithThreeInOneLoad)} of those wins; won only through the clock prize in {Pct(t.WonOnPrize)} of matches; loot mark and a Hunt mark on one bird in {Pct(t.OverlapShare)} of the Thief's matches ({Pct(t.OverlapShareWithSpoiler)} of those with a Spoiler), {Num(t.OverlapSecondsPerMatch)} s per match.");
                }
                foreach (var p in pops)
                {
                    if (p.LootSteals.Count == 0) continue;
                    sb.AppendLine();
                    sb.AppendLine($"Thief marked steals to win, {Name(p.Population)} (steals taken in the match, mixed design; survivor bias applies; the label compares the row with the `ThiefLootTargets` data):");
                    sb.AppendLine();
                    sb.AppendLine("| Marked steals | Matches | Win, no prize | Win, with prize | Final banked | Loot share banked | Reached 40 | Label |");
                    sb.AppendLine("|---|---|---|---|---|---|---|---|");
                    foreach (var row in p.LootSteals)
                    {
                        string reached = float.IsNaN(row.NobodyReachedTarget) ? "n/a" : Pct(1f - row.NobodyReachedTarget);
                        sb.AppendLine($"| {row.Steals} | {row.Win.Trials} | {Pct(row.Win.Rate)} ({Pm(row.Win)}) | {Pct(row.WinWithPrize.Rate)} ({Pm(row.WinWithPrize)}) | {Num(row.MeanFinal, "0.0")} | {Pct(row.BankedShare)} | {reached} | {row.Label ?? "no target"} |");
                    }
                }
                sb.AppendLine();
                sb.AppendLine("Food taken per meeting, by victim kit:");
                sb.AppendLine();
                sb.AppendLine("| Victim kit | " + string.Join(" | ", pops.Select(p => Name(p.Population))) + " |");
                sb.AppendLine("|---|" + string.Concat(pops.Select(p => "---|")));
                foreach (var l in r.Thief)
                {
                    var cells = pops.Select(p =>
                    {
                        var line = p.Thief.FirstOrDefault(x => x.Victim == l.Victim);
                        return line == null ? "n/a" : $"{Num(line.StolenPerMatch)} ({Pct(line.Share)})";
                    });
                    sb.AppendLine($"| {l.Victim} | {string.Join(" | ", cells)} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Warrior kits");
            sb.AppendLine();
            if (!pops.Any(p => p.Warriors.Count > 0)) sb.AppendLine("No Warrior kit in these matches.");
            else
            {
                sb.AppendLine("Per match, over the matches that held the kit (mixed design). The Bully's rows are the hit-steal (takes and food, then the hits that took nothing: rival locked, rival empty, beak full; food lost is the victim's side). The Champion's rows are the fresh hits (a hit on a rival that is casting a Peck or just pecked: one extra trophy stacked, no food unless the passive's bite share is above 0), the bites (the food taken when it is: from a casting rival's pile, from the cargo of a rival whose Peck just landed, with their food; spent bites are hits on a state somebody had already bitten; Pecks bitten and food lost are the victim's side), the trophies (earned, decayed, cleared by an execution, the Steals that cashed them, the trophies consumed per cash, the food they added to Steals), the mean Steal size, the food the identity brought in (hit-steal food, bite food and the trophies' food), and the worst loss one victim took inside " + MatchReports.LossWindowSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s from this kit's hit-steals, Steals and bites (mean over matches and 95th percentile).");
                sb.AppendLine();
                sb.AppendLine("| Kit | Population | Matches | Win | Hit takes | Hit food | Refused: lock / empty / full | Hit food lost | Fresh hits | Bites cast / cargo | Bite food cast / cargo | Bites spent | Pecks bitten | Bite food lost | Trophies earned | Decayed | Cleared | Cashes | Trophies per cash | Trophy food | Steals | Mean Steal size | Identity food | Worst loss mean | Worst loss p95 |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
                foreach (var p in pops)
                    foreach (var w in p.Warriors)
                        sb.AppendLine($"| {w.Kit} | {Name(p.Population)} | {w.Matches} | {Pct(w.Win.Rate)} ({Pm(w.Win)}) | {Num(w.HitTakesPerMatch, "0.00")} | {Num(w.HitFoodPerMatch, "0.0")} | {Num(w.HitRefusedLockPerMatch, "0.00")} / {Num(w.HitRefusedNoFoodPerMatch, "0.00")} / {Num(w.HitRefusedRoomPerMatch, "0.00")} | {Num(w.HitLostFoodPerMatch, "0.0")} | {Num(w.FreshHitsPerMatch, "0.00")} | {Num(w.BitesFromCastPerMatch, "0.00")} / {Num(w.BitesFromCargoPerMatch, "0.00")} | {Num(w.BiteFoodFromCastPerMatch, "0.0")} / {Num(w.BiteFoodFromCargoPerMatch, "0.0")} | {Num(w.BitesSpentPerMatch, "0.00")} | {Num(w.PecksBittenPerMatch, "0.00")} | {Num(w.BiteLostFoodPerMatch, "0.0")} | {Num(w.TrophiesEarnedPerMatch, "0.00")} | {Num(w.TrophiesDecayedPerMatch, "0.00")} | {Num(w.TrophiesClearedPerMatch, "0.00")} | {Num(w.TrophyCashesPerMatch, "0.00")} | {Num(w.TrophiesPerCash, "0.00")} | {Num(w.TrophyBonusFoodPerMatch, "0.0")} | {Num(w.StealsPerMatch, "0.00")} | {Num(w.MeanStealSize, "0.0")} | {Num(w.IdentityFoodPerMatch, "0.0")} | {Num(w.WorstLossMean, "0.0")} | {Num(w.WorstLossP95, "0.0")} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Peck cuts per class and cutter");
            sb.AppendLine();
            sb.AppendLine("Hazard = of the Peck casts that began with a rival holding a ready cutter of that kind in range, the share then cut by it. The raw count is shown but confounds exposure.");
            sb.AppendLine();
            sb.AppendLine("| Class | Cutter | " + string.Join(" | ", pops.Select(p => Name(p.Population) + " exposed / cuts / hazard / food lost per match")) + " |");
            sb.AppendLine("|---|---|" + string.Concat(pops.Select(p => "---|")));
            foreach (var l in r.PeckCuts)
            {
                var cells = pops.Select(p =>
                {
                    var line = p.PeckCuts.FirstOrDefault(x => x.Class == l.Class && x.Cause == l.Cause);
                    return line == null ? "n/a" : $"{line.Exposed} / {line.Cut} / {Pct(line.Hazard)} / {Num(line.FoodLostPerMatch)}";
                });
                sb.AppendLine($"| {l.Class} | {MatchEvent.CauseName(l.Cause)} | {string.Join(" | ", cells)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Time to first contact");
            sb.AppendLine();
            foreach (var p in pops)
            {
                if (p.FirstContact == null) continue;
                sb.AppendLine($"- {Name(p.Population)}: all mixed matches: median {Num(p.FirstContact.Median, "0.0")} s (quartiles {Num(p.FirstContact.Q1, "0.0")} to {Num(p.FirstContact.Q3, "0.0")} s); never in {p.FirstContact.Never} of {p.FirstContact.Matches}."
                    + (p.FirstContactNoAssassin != null ? $" With no Assassin: median {Num(p.FirstContactNoAssassin.Median, "0.0")} s (quartiles {Num(p.FirstContactNoAssassin.Q1, "0.0")} to {Num(p.FirstContactNoAssassin.Q3, "0.0")} s); never in {p.FirstContactNoAssassin.Never} of {p.FirstContactNoAssassin.Matches}." : string.Empty));
            }
            if (r.FirstContact == null) sb.AppendLine("Not measured.");
            else sb.AppendLine("The Assassin's Search walk to the nearest rival makes the contact within the first seconds, so the overall median is its time.");
            sb.AppendLine();

            sb.AppendLine("## Economy per kit (food per appearance)");
            sb.AppendLine();
            sb.AppendLine("| Kit | " + string.Join(" | ", pops.Select(p => Name(p.Population) + " foraged / stolen / bounty")) + " |");
            sb.AppendLine("|---|" + string.Concat(pops.Select(p => "---|")));
            foreach (var e in r.Economy)
            {
                var cells = pops.Select(p =>
                {
                    var line = p.Economy.FirstOrDefault(x => x.Label == e.Label);
                    return line == null ? "n/a" : $"{Num(line.Foraged, "0.0")} / {Num(line.Stolen, "0.0")} / {Num(line.Bounty, "0.0")}";
                });
                sb.AppendLine($"| {e.Label} | {string.Join(" | ", cells)} |");
            }
            sb.AppendLine();
            sb.AppendLine($"Corner tie-breaks in the mixed design: " + string.Join(", ", pops.Select(p => $"{Name(p.Population)} {p.CornerDecided} of {p.MixedMatches}")) + ".");
            sb.AppendLine();

            sb.AppendLine("## Drift from the last match record");
            sb.AppendLine();
            if (r.RecordHistory.Count == 0) sb.AppendLine("No match record yet.");
            else
            {
                sb.AppendLine("Records, oldest first (a record is data to compare with, not a statement that the state is balanced):");
                sb.AppendLine();
                foreach (var h in r.RecordHistory) sb.AppendLine($"- {h}");
                sb.AppendLine();
                sb.AppendLine("Change from the last record, in win-rate points (both margins are in the line; a change inside them is noise):");
                sb.AppendLine();
                foreach (var d in r.Drift) sb.AppendLine($"- {d.Label}: {Num(d.Points, "+0.0;-0.0")} points");
            }
            return sb.ToString();
        }

        // The sweep's tables: the one-factor reaction delay, the hypercube's sensitivity, the leader weight, and the robustness checks behind the tags.
        private static void SweepMarkdown(StringBuilder sb, MatchReportSection r)
        {
            var o = r.Sweep;
            sb.AppendLine("## Sensitivity sweep");
            sb.AppendLine();
            sb.AppendLine($"{o.Points.Length} hypercube points x 3 populations over the mixed design (R {o.Default.Policies[0].Runs.FirstOrDefault()?.Replicates ?? 0}, seed `0x{o.Default.RunSeed:x16}`), plus the one-factor reaction delay and leader-weight runs: {o.Matches} matches in {o.ElapsedSeconds.ToString("0", Inv)} s. A rate here is from far fewer matches than the main report's (about +/- 3 points), so a difference under that is noise.");
            sb.AppendLine();
            sb.AppendLine($"Robustness: the kits' rank order against the default point, over the sweep and the delay runs, keeps a lowest overall tau of **{Num(r.SweepTauMin)}** (information: some pair of kits swaps somewhere). A kit's call is judged by its own tau, over the pairs that hold it and that the default tells apart (floor {MatchRobustness.MinTau:0.0}), and by how far its mixed rate ran past the band edge of its call (limit {MatchRobustness.MaxEdgeCrossPoints:0} points); both are in the last table. The delay is swept together with every other axis; the one-factor table below isolates it.");
            sb.AppendLine();

            sb.AppendLine("### Reaction delay, one factor (everything else at the baseline)");
            sb.AppendLine();
            var cols = new List<string>();
            foreach (var pop in MatchSensitivity.Populations)
                foreach (var d in o.Delays) cols.Add($"{Name(pop)} {d.ToString("0.0#", Inv)} s");
            sb.AppendLine("| Kit | " + string.Join(" | ", cols) + " |");
            sb.AppendLine("|---|" + string.Concat(cols.Select(c => "---|")));
            for (int k = 0; k < o.KitLabels.Length; k++)
            {
                var cells = new List<string>();
                foreach (var pop in MatchSensitivity.Populations)
                    for (int i = 0; i < o.Delays.Length; i++) cells.Add(Pct(o.DelayReports[i].ForPopulation(pop).Kits[k].Win.Rate));
                sb.AppendLine($"| {o.KitLabels[k]} | {string.Join(" | ", cells)} |");
            }
            sb.AppendLine();
            sb.AppendLine("Share of matches that end on the target and mean length, by delay (mixed design):");
            sb.AppendLine();
            sb.AppendLine("| Policy | Delay | Ended on target | Mean length (s) |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var pop in MatchSensitivity.Populations)
                for (int i = 0; i < o.Delays.Length; i++)
                {
                    var line = o.DelayReports[i].ForPopulation(pop).Runs.FirstOrDefault(x => x.Design == MatchDesignKind.Mixed);
                    if (line == null) continue;
                    sb.AppendLine($"| {Name(pop)} | {o.Delays[i].ToString("0.0#", Inv)} s | {Pct(line.EndedOnTarget / (float)Math.Max(1, line.Matches))} | {Num(line.MeanEndSeconds, "0.0")} |");
                }
            sb.AppendLine();

            sb.AppendLine("### Which parameter moves which kit (effect over the whole axis, in win-rate points)");
            sb.AppendLine();
            sb.AppendLine("A number's effect is the least-squares slope times its range, signed (plus: a larger value helps the kit); a choice (target rule, route) shows the gap between its best and worst level, never negative. With 16 points each effect carries several points of noise.");
            sb.AppendLine();
            foreach (var pop in MatchSensitivity.Populations)
            {
                sb.AppendLine($"#### {Name(pop)}");
                sb.AppendLine();
                sb.AppendLine("| Parameter | " + string.Join(" | ", o.KitLabels) + " | Largest |");
                sb.AppendLine("|---|" + string.Concat(o.KitLabels.Select(c => "---|")) + "---|");
                foreach (var axis in MatchSweep.AxisNames)
                {
                    var lines = o.Sensitivity.Where(l => l.Population == pop && l.Parameter == axis).ToList();
                    var cells = o.KitLabels.Select(kit => Num(lines.First(l => l.Kit == kit).Effect, "+0.0;-0.0;0.0"));
                    var big = lines.OrderByDescending(l => Math.Abs(l.Effect)).First();
                    sb.AppendLine($"| {axis} | {string.Join(" | ", cells)} | {big.Kit} {Num(big.Effect, "+0.0;-0.0;0.0")} |");
                }
                sb.AppendLine();
            }
            sb.AppendLine("The three largest single effects, any population:");
            sb.AppendLine();
            foreach (var l in o.Sensitivity.OrderByDescending(x => Math.Abs(x.Effect)).Take(3))
                sb.AppendLine($"- {l.Parameter} on {l.Kit} ({Name(l.Population)}): {Num(l.Effect, "+0.0;-0.0;0.0")} points");
            sb.AppendLine();

            sb.AppendLine("### Leader pick weight of the Death Mark (BL-210)");
            sb.AppendLine();
            if (o.LeaderWeightReports.Length == 0) sb.AppendLine(o.Note ?? "Not run.");
            else
            {
                sb.AppendLine("The weight goes through the balance book like any proposed value. Win rate in the mixed design, by population:");
                sb.AppendLine();
                var cols2 = new List<string>();
                foreach (var pop in new[] { PolicyPopulation.P1, PolicyPopulation.Mixed })
                    foreach (var w in o.LeaderWeights) cols2.Add($"{Name(pop)} w {w.ToString("0.0#", Inv)}");
                sb.AppendLine("| Kit | " + string.Join(" | ", cols2) + " |");
                sb.AppendLine("|---|" + string.Concat(cols2.Select(c => "---|")));
                for (int k = 0; k < o.KitLabels.Length; k++)
                {
                    var cells = new List<string>();
                    foreach (var pop in new[] { PolicyPopulation.P1, PolicyPopulation.Mixed })
                        for (int i = 0; i < o.LeaderWeights.Length; i++) cells.Add(Pct(o.LeaderWeightReports[i].ForPopulation(pop).Kits[k].Win.Rate));
                    sb.AppendLine($"| {o.KitLabels[k]} | {string.Join(" | ", cells)} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("### Furthest past a band edge, per kit (points)");
            sb.AppendLine();
            sb.AppendLine("| Kit | Mixed rate calls | Furthest past its band edge | This kit's lowest tau | Final |");
            sb.AppendLine("|---|---|---|---|---|");
            for (int k = 0; k < r.Kits.Count && r.SweepEdgeCross != null; k++)
                sb.AppendLine($"| {r.Kits[k].Label} | {MatchRobustness.CallOf(r.Kits[k].WinMixed)} | {Num(r.SweepEdgeCross[k], "0.0")} | {Num(r.SweepKitTau[k])} | {r.Kits[k].Call} |");
            sb.AppendLine();
        }

        private static void KillTable(StringBuilder sb, List<SpoilerKillRow> rows, bool victims)
        {
            if (rows.Count == 0) { sb.AppendLine("No rows."); sb.AppendLine(); return; }
            sb.AppendLine(victims
                ? "| Kills | Victims | Matches | Win, no passive | Win, with passive | Final / best rival | Reached 40 | Label |"
                : "| Kills | Matches | Win, no passive | Win, with passive | Final / best rival | Reached 40 | Label |");
            sb.AppendLine(victims ? "|---|---|---|---|---|---|---|---|" : "|---|---|---|---|---|---|---|");
            foreach (var row in rows)
            {
                string reached = float.IsNaN(row.NobodyReachedTarget) ? "n/a" : Pct(1f - row.NobodyReachedTarget);
                string head = victims ? $"| {row.Kills} | {row.Victims} " : $"| {row.Kills} ";
                sb.AppendLine($"{head}| {row.Win.Trials} | {Pct(row.Win.Rate)} ({Pm(row.Win)}) | {Pct(row.WinWithPassive.Rate)} ({Pm(row.WinWithPassive)}) | {Num(row.MeanFinal, "0.0")} / {Num(row.MeanBestRival, "0.0")} | {reached} | {row.Label ?? "no target"} |");
            }
            sb.AppendLine();
        }

        // ------------------------------------------------------------------ json

        private static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", Inv);
        private static string S(string v) => v == null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
        private static string W(WinInterval w) => $"{{\"wins\":{w.Wins},\"trials\":{w.Trials},\"rate\":{N(w.Rate)},\"low\":{N(w.Low)},\"high\":{N(w.High)}}}";

        public static string Json(MatchReportSection r)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"schema\":1,\"balanceHash\":{S(r.BalanceHash)},\"mapFingerprint\":{S(r.MapFingerprint)},\"runSeed\":{S("0x" + r.RunSeed.ToString("x16"))},\"populationNote\":{S(r.PopulationNote)}");
            sb.Append(",\"runs\":[" + string.Join(",", r.Runs.Select(x =>
                $"{{\"design\":{S(x.Design.ToString())},\"population\":{S(x.Population.ToString())},\"replicates\":{x.Replicates},\"reactionDelay\":{N(x.ReactionDelay)},\"reactionJitter\":{N(x.ReactionJitter)},\"matches\":{x.Matches},\"endedOnTarget\":{x.EndedOnTarget},\"endedOnClock\":{x.EndedOnClock},\"meanEndSeconds\":{N(x.MeanEndSeconds)},\"elapsedMilliseconds\":{N((float)x.ElapsedMilliseconds)}}}")) + "]");
            sb.Append(",\"kits\":[" + string.Join(",", r.Kits.Select(k =>
                $"{{\"label\":{S(k.Label)},\"class\":{S(k.Class.ToString())},\"win\":{W(k.Win)},\"winP1\":{W(k.WinP1)},\"winMixed\":{W(k.WinMixed)},\"rangeLow\":{N(k.RangeLow)},\"rangeHigh\":{N(k.RangeHigh)},\"call\":{S(k.Call.ToString())},\"reason\":{S(k.Reason)}}}")) + "]");
            sb.Append(",\"classes\":[" + string.Join(",", r.Classes.Select(c =>
                $"{{\"class\":{S(c.Class.ToString())},\"headline\":{W(c.Headline)},\"pooled\":{W(c.Pooled)},\"headlineP1\":{W(c.HeadlineP1)},\"pooledP1\":{W(c.PooledP1)},\"headlineMixed\":{W(c.HeadlineMixed)},\"pooledMixed\":{W(c.PooledMixed)}}}")) + "]");
            sb.Append(",\"headToHead\":[" + string.Join(",", r.HeadToHead.Select(h =>
                $"{{\"class\":{S(h.Class.ToString())},\"kitA\":{S(h.KitA)},\"kitB\":{S(h.KitB)},\"aAhead\":{N(h.AAhead)},\"matches\":{h.Matches}}}")) + "]");
            sb.Append(",\"killsPlayed\":[" + string.Join(",", r.KillsPlayed.Select(KillJson)) + "]");
            sb.Append(",\"killLoot\":[" + string.Join(",", r.KillLoot.Select(KillLootJson)) + "]");
            sb.Append(",\"killsToWin\":[" + string.Join(",", r.KillsToWin.Select(KillJson)) + "]");
            sb.Append(",\"lootSteals\":[" + string.Join(",", r.LootSteals.Select(LootJson)) + "]");
            sb.Append(",\"thief\":[" + string.Join(",", r.Thief.Select(t =>
                $"{{\"victim\":{S(t.Victim)},\"stolenPerMatch\":{N(t.StolenPerMatch)},\"share\":{N(t.Share)}}}")) + "]");
            if (r.ThiefIncome != null)
            {
                var t = r.ThiefIncome;
                sb.Append($",\"thiefIncome\":{{\"kit\":{S(t.Kit)},\"matches\":{t.Matches},\"stealsPerMatch\":{N(t.StealsPerMatch)},\"stolenPerMatch\":{N(t.StolenPerMatch)},\"foragedPerMatch\":{N(t.ForagedPerMatch)},\"bankedPerMatch\":{N(t.BankedPerMatch)}}}");
            }
            sb.Append(",\"warriors\":[" + string.Join(",", r.Warriors.Select(WarriorJson)) + "]");
            sb.Append(",\"peckCuts\":[" + string.Join(",", r.PeckCuts.Select(l =>
                $"{{\"class\":{S(l.Class.ToString())},\"cause\":{S(MatchEvent.CauseName(l.Cause))},\"exposed\":{l.Exposed},\"cut\":{l.Cut},\"hazard\":{N(l.Hazard)},\"foodLostPerMatch\":{N(l.FoodLostPerMatch)}}}")) + "]");
            if (r.FirstContactNoAssassin != null)
                sb.Append($",\"firstContactNoAssassin\":{{\"median\":{N(r.FirstContactNoAssassin.Median)},\"q1\":{N(r.FirstContactNoAssassin.Q1)},\"q3\":{N(r.FirstContactNoAssassin.Q3)},\"never\":{r.FirstContactNoAssassin.Never},\"matches\":{r.FirstContactNoAssassin.Matches}}}");
            if (r.FirstContact != null)
                sb.Append($",\"firstContact\":{{\"median\":{N(r.FirstContact.Median)},\"q1\":{N(r.FirstContact.Q1)},\"q3\":{N(r.FirstContact.Q3)},\"never\":{r.FirstContact.Never},\"matches\":{r.FirstContact.Matches}}}");
            sb.Append(",\"economy\":[" + string.Join(",", r.Economy.Select(e =>
                $"{{\"label\":{S(e.Label)},\"foraged\":{N(e.Foraged)},\"stolen\":{N(e.Stolen)},\"bounty\":{N(e.Bounty)}}}")) + "]");
            sb.Append($",\"cornerDecided\":{r.CornerDecided},\"mixedMatches\":{r.MixedMatches}");
            sb.Append(",\"mixedSeats\":[" + string.Join(",", r.MixedSeats.Select(x => $"{{\"seat\":{x.Seat},\"win\":{W(x.Win)}}}")) + "]");
            sb.Append(",\"seatCheck\":[" + string.Join(",", r.SeatCheck.Select(s =>
                $"{{\"seat\":{s.Seat},\"win\":{W(s.Win)},\"winExCorner\":{W(s.WinExCorner)},\"flagged\":{(s.Flagged ? "true" : "false")}}}")) + "]");
            sb.Append(",\"seatByKit\":[" + string.Join(",", r.SeatByKit.Select(k =>
                $"{{\"kit\":{S(k.Kit)},\"matches\":{k.Matches},\"winsBySeat\":[{string.Join(",", k.WinsBySeat)}],\"cornerDecided\":{k.CornerDecided}}}")) + "]");
            sb.Append($",\"seatCheckMatches\":{r.SeatCheckMatches},\"seatCheckCornerDecided\":{r.SeatCheckCornerDecided}");
            sb.Append(",\"drift\":[" + string.Join(",", r.Drift.Select(d => $"{{\"label\":{S(d.Label)},\"points\":{N(d.Points)}}}")) + "]");
            sb.Append(",\"records\":[" + string.Join(",", r.RecordHistory.Select(S)) + "]");
            sb.Append(",\"leftOut\":[" + string.Join(",", r.LeftOut.Select(S)) + "]");
            sb.Append(",\"matchups\":[" + string.Join(",", r.Matchups.Select(m => $"{{\"label\":{S(m.Label)},\"matches\":{m.Matches},\"p1Share\":{W(m.P1Share)}}}")) + "]");
            sb.Append(",\"policies\":[" + string.Join(",", r.Policies.Select(PolicyJson)) + "]");
            if (r.Sweep != null) sb.Append(",\"sweep\":" + SweepJson(r));
            sb.Append('}');
            return sb.ToString();
        }

        // A population's own tables, without the lists every population shares.
        private static string PolicyJson(MatchReportSection p)
        {
            var sb = new StringBuilder();
            sb.Append($"{{\"population\":{S(p.Population.ToString())}");
            sb.Append(",\"runs\":[" + string.Join(",", p.Runs.Select(x =>
                $"{{\"design\":{S(x.Design.ToString())},\"matches\":{x.Matches},\"endedOnTarget\":{x.EndedOnTarget},\"endedOnClock\":{x.EndedOnClock},\"meanEndSeconds\":{N(x.MeanEndSeconds)}}}")) + "]");
            sb.Append(",\"kits\":[" + string.Join(",", p.Kits.Select(k => $"{{\"label\":{S(k.Label)},\"win\":{W(k.Win)}}}")) + "]");
            sb.Append(",\"classes\":[" + string.Join(",", p.Classes.Select(c => $"{{\"class\":{S(c.Class.ToString())},\"headline\":{W(c.Headline)},\"pooled\":{W(c.Pooled)}}}")) + "]");
            sb.Append(",\"mixedSeats\":[" + string.Join(",", p.MixedSeats.Select(x => $"{{\"seat\":{x.Seat},\"win\":{W(x.Win)}}}")) + "]");
            sb.Append(",\"seatCheck\":[" + string.Join(",", p.SeatCheck.Select(x => $"{{\"seat\":{x.Seat},\"win\":{W(x.Win)},\"winExCorner\":{W(x.WinExCorner)},\"flagged\":{(x.Flagged ? "true" : "false")}}}")) + "]");
            sb.Append(",\"killsPlayed\":[" + string.Join(",", p.KillsPlayed.Select(KillJson)) + "]");
            sb.Append(",\"killLoot\":[" + string.Join(",", p.KillLoot.Select(KillLootJson)) + "]");
            sb.Append(",\"lootSteals\":[" + string.Join(",", p.LootSteals.Select(LootJson)) + "]");
            if (p.ThiefIncome != null)
            {
                var t = p.ThiefIncome;
                sb.Append($",\"thiefIncome\":{{\"kit\":{S(t.Kit)},\"matches\":{t.Matches},\"stealsPerMatch\":{N(t.StealsPerMatch)},\"stolenPerMatch\":{N(t.StolenPerMatch)},\"foragedPerMatch\":{N(t.ForagedPerMatch)},\"bankedPerMatch\":{N(t.BankedPerMatch)}}}");
            }
            sb.Append(",\"warriors\":[" + string.Join(",", p.Warriors.Select(WarriorJson)) + "]");
            sb.Append(",\"peckCuts\":[" + string.Join(",", p.PeckCuts.Select(l =>
                $"{{\"class\":{S(l.Class.ToString())},\"cause\":{S(MatchEvent.CauseName(l.Cause))},\"exposed\":{l.Exposed},\"cut\":{l.Cut},\"hazard\":{N(l.Hazard)},\"foodLostPerMatch\":{N(l.FoodLostPerMatch)}}}")) + "]");
            sb.Append('}');
            return sb.ToString();
        }

        private static string SweepJson(MatchReportSection r)
        {
            var o = r.Sweep;
            var sb = new StringBuilder();
            sb.Append($"{{\"points\":{o.Points.Length},\"matches\":{o.Matches},\"elapsedSeconds\":{N((float)o.ElapsedSeconds)},\"tauMin\":{N(r.SweepTauMin)}");
            sb.Append(",\"edgeCross\":[" + string.Join(",", (r.SweepEdgeCross ?? new float[0]).Select(N)) + "]");
            sb.Append(",\"kitTau\":[" + string.Join(",", (r.SweepKitTau ?? new float[0]).Select(N)) + "]");
            sb.Append(",\"delays\":[" + string.Join(",", o.Delays.Select(N)) + "]");
            sb.Append(",\"delayRates\":[" + string.Join(",", o.DelayReports.Select(d => "[" + string.Join(",", MatchSensitivity.Populations.Select(pop =>
                "[" + string.Join(",", d.ForPopulation(pop).Kits.Select(k => N(k.Win.Rate))) + "]")) + "]")) + "]");
            sb.Append(",\"leaderWeights\":[" + string.Join(",", o.LeaderWeights.Select(N)) + "]");
            sb.Append(",\"leaderWeightRates\":[" + string.Join(",", o.LeaderWeightReports.Select(d => "[" + string.Join(",", MatchSensitivity.Populations.Select(pop =>
                "[" + string.Join(",", d.ForPopulation(pop).Kits.Select(k => N(k.Win.Rate))) + "]")) + "]")) + "]");
            sb.Append(",\"sensitivity\":[" + string.Join(",", o.Sensitivity.Select(l =>
                $"{{\"population\":{S(l.Population.ToString())},\"parameter\":{S(l.Parameter)},\"kit\":{S(l.Kit)},\"effect\":{N(l.Effect)}}}")) + "]");
            sb.Append('}');
            return sb.ToString();
        }

        private static string WarriorJson(WarriorLine w) =>
            $"{{\"kit\":{S(w.Kit)},\"matches\":{w.Matches},\"win\":{W(w.Win)},\"hitTakes\":{N(w.HitTakesPerMatch)},\"hitFood\":{N(w.HitFoodPerMatch)},\"hitRefusedLock\":{N(w.HitRefusedLockPerMatch)},\"hitRefusedNoFood\":{N(w.HitRefusedNoFoodPerMatch)},\"hitRefusedRoom\":{N(w.HitRefusedRoomPerMatch)},\"hitLostFood\":{N(w.HitLostFoodPerMatch)},\"freshHits\":{N(w.FreshHitsPerMatch)},\"bitesFromCast\":{N(w.BitesFromCastPerMatch)},\"bitesFromCargo\":{N(w.BitesFromCargoPerMatch)},\"biteFoodFromCast\":{N(w.BiteFoodFromCastPerMatch)},\"biteFoodFromCargo\":{N(w.BiteFoodFromCargoPerMatch)},\"biteLeftInPile\":{N(w.BiteLeftInPilePerMatch)},\"biteLeftWithVictim\":{N(w.BiteLeftWithVictimPerMatch)},\"bitesSpent\":{N(w.BitesSpentPerMatch)},\"pecksBitten\":{N(w.PecksBittenPerMatch)},\"biteLostFood\":{N(w.BiteLostFoodPerMatch)},\"trophiesEarned\":{N(w.TrophiesEarnedPerMatch)},\"trophiesDecayed\":{N(w.TrophiesDecayedPerMatch)},\"trophiesCleared\":{N(w.TrophiesClearedPerMatch)},\"trophyCashes\":{N(w.TrophyCashesPerMatch)},\"trophiesPerCash\":{N(w.TrophiesPerCash)},\"trophyBonusFood\":{N(w.TrophyBonusFoodPerMatch)},\"steals\":{N(w.StealsPerMatch)},\"stealFood\":{N(w.StealFoodPerMatch)},\"meanStealSize\":{N(w.MeanStealSize)},\"meanHitSize\":{N(w.MeanHitSize)},\"identityFood\":{N(w.IdentityFoodPerMatch)},\"worstLossMean\":{N(w.WorstLossMean)},\"worstLossP95\":{N(w.WorstLossP95)}}}";

        private static string LootJson(LootStealRow k) =>
            $"{{\"steals\":{k.Steals},\"win\":{W(k.Win)},\"winWithPrize\":{W(k.WinWithPrize)},\"meanFinal\":{N(k.MeanFinal)},\"bankedShare\":{N(k.BankedShare)},\"nobodyReachedTarget\":{N(k.NobodyReachedTarget)},\"label\":{S(k.Label)}}}";

        private static string KillLootJson(KillLootLine l) =>
            $"{{\"killer\":{S(l.Killer)},\"victim\":{S(l.Victim.ToString())},\"kills\":{l.Kills},\"cargoPerKill\":{N(l.CargoPerKill)},\"gainedPerKill\":{N(l.GainedPerKill)},\"unfitPerKill\":{N(l.UnfitPerKill)}}}";

        private static string KillJson(SpoilerKillRow k) =>
            $"{{\"kills\":{k.Kills},\"victims\":{S(k.Victims.ToString())},\"win\":{W(k.Win)},\"winWithPassive\":{W(k.WinWithPassive)},\"meanFinal\":{N(k.MeanFinal)},\"meanBestRival\":{N(k.MeanBestRival)},\"nobodyReachedTarget\":{N(k.NobodyReachedTarget)},\"label\":{S(k.Label)}}}";
    }
}
