using System;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Tests;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Runs the Balance Oracle over the shipped assets and writes the report (spec 5.7) to <c>Captures/oracle/</c>
    /// (git-ignored build output) as JSON and Markdown. Menu for the Editor, <see cref="ReportBatch"/> for
    /// <c>-executeMethod</c>.
    /// </summary>
    public static class OracleMenu
    {
        public const string MenuPath = "Cluck Wars/Balance/Oracle/Report";
        public const string RecordMenuPath = "Cluck Wars/Balance/Oracle/Record baseline (keep prices)";
        public const string AcceptMenuPath = "Cluck Wars/Balance/Oracle/Accept as balanced (recompute prices)";
        public const string SweepMenuPath = "Cluck Wars/Balance/Oracle/Sweep food";
        public const string SolveMenuPath = "Cluck Wars/Balance/Oracle/Solve";
        public const string ProblemFileName = "problem.json";
        public const string ReferencePath = "Assets/_Game/Data/Balance/OracleReference.asset";
        public const string NoteArgument = "-oracleNote";
        public const string MatchMenuPath = "Cluck Wars/Balance/Oracle/Match report";
        public const string SensitivityMenuPath = "Cluck Wars/Balance/Oracle/Match sensitivity";
        public const string AcceptMatchMenuPath = "Cluck Wars/Balance/Oracle/Accept match as balanced";
        public const string SeedArgument = "-oracleSeed";
        public const string ThreadsArgument = "-oracleThreads";
        public const ulong DefaultMatchSeed = 20261122UL;
        public const int DefaultMatchThreads = 12;
        /// <summary>Replicates of the four designs the match report plays (spec 5.3).</summary>
        public const int MixedReplicates = MatchReports.MixedReplicates, OnePerClassReplicates = MatchReports.OnePerClassReplicates,
            SeatCheckReplicates = MatchReports.SeatCheckReplicates, ForcedKillsReplicates = MatchReports.ForcedKillsReplicates;
        /// <summary>Matches written out event by event next to the report, so a number that looks wrong can be read in play.</summary>
        public const int SampleMatches = 6;
        private const string DefaultRecordNote = "baseline recorded from the menu";
        private const string DefaultAcceptNote = "accepted from the menu";
        private const string DefaultAcceptMatchNote = "match record from the menu (a record to compare with, not a statement that the state is balanced)";
        private const string Source = "OracleReport";

        [MenuItem(MenuPath, priority = 61)]
        private static void ReportFromMenu() => Write();

        /// <summary>Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.ReportBatch</c>. A failure is logged and rethrown so the Editor exits non-zero.</summary>
        public static void ReportBatch() => InBatch("The oracle report failed.", () => Write());

        [MenuItem(SweepMenuPath, priority = 64)]
        private static void SweepFoodFromMenu() => SweepFood();

        /// <summary>Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.SweepFoodBatch</c>. Read-only: writes <c>Captures/oracle/sweep-food-*.md</c> and <c>.json</c>.</summary>
        public static void SweepFoodBatch() => InBatch("The food sweep failed.", () => SweepFood());

        /// <summary>Runs <see cref="OracleSweep.DefaultCandidates"/> over the shipped assets and returns the two file paths written; no asset is changed.</summary>
        public static (string json, string markdown) SweepFood()
        {
            var inputs = Inputs();
            var rows = OracleSweep.Food(inputs, OracleSweep.DefaultCandidates, LoadReference().Offsets());
            string hash = BalanceBook.BaselineHash;

            string dir = OutputDirectory();
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string json = Path.Combine(dir, $"sweep-food-{stamp}.json");
            string md = Path.Combine(dir, $"sweep-food-{stamp}.md");
            File.WriteAllText(json, OracleSweep.ToJson(rows, hash, inputs.Map.SceneFingerprint));
            File.WriteAllText(md, OracleSweep.ToMarkdown(rows, hash, inputs.Map.SceneFingerprint));

            new UnityLogService(LogLevel.Info).Info(Source, $"Oracle food sweep written: {md} and {json} ({rows.Count} setups).");
            return (json, md);
        }

        /// <summary>
        /// Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.MatchTimingBatch</c>. Plays 1 000 farm-only matches of four
        /// standard farmers on the shipped inputs, on one thread, and logs the milliseconds one match costs (the figure the match simulator's
        /// runtime budget rests on). Read-only.
        /// </summary>
        public static void MatchTimingBatch() => InBatch("The match timing run failed.", () =>
        {
            MatchTiming(1000);
            MatchTimingCombat(1000);
            MatchTimingCombat(1000, PolicyPopulation.P1);
        });

        /// <summary>
        /// Plays <paramref name="matches"/> real mixed-design matches under P0 on the shipped kits (combat, casts, the comeback event) on one
        /// thread after a short warm-up and returns the mean milliseconds per match: the cost the simulator's runtime budget rests on.
        /// </summary>
        public static double MatchTimingCombat(int matches, PolicyPopulation population = PolicyPopulation.P0)
        {
            const int warmUp = 20;
            var log = new UnityLogService(LogLevel.Info);
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), BotArrivalRadius());
            int replicates = Math.Max(1, (matches + 419) / 420);   // 420 cells per replicate of the mixed design
            MatchRunner.Run(context, new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = replicates, Population = population, RunSeed = 2UL, Threads = 1, CellLimit = warmUp });
            var run = MatchRunner.Run(context, new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = replicates, Population = population, RunSeed = 1UL, Threads = 1, CellLimit = matches });
            double perMatch = run.ElapsedMilliseconds / run.Results.Length;
            log.Info(Source, $"Match timing: {perMatch:0.000} ms per {population} mixed-design match with combat over {run.Results.Length} matches, one thread.");
            return perMatch;
        }

        /// <summary>Plays <paramref name="matches"/> farm-only matches after a short warm-up and returns the mean milliseconds per match.</summary>
        public static double MatchTiming(int matches)
        {
            const int warmUp = 20;
            var log = new UnityLogService(LogLevel.Info);
            var inputs = Inputs();
            var farmer = OracleKits.StandardFarmer(inputs);
            var kit = new MatchKit { Label = farmer.Label, Class = ChickenClass.Warrior, Farm = farmer };
            float half = OracleMapBaker.ArenaHalf();
            var setup = new MatchSetup
            {
                Map = inputs.Map,
                Nav = OracleNav.For(inputs.Map, half),
                Food = OracleKits.Food(inputs),
                Rules = new MatchRules { Farm = OracleKits.Rules(inputs, false, FarmFoodEvent.None), ArenaHalf = half },
                Seats = new[] { kit, kit, kit, kit },
                Policies = new[] { FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory },
            };

            float banked = 0f;
            for (int i = 0; i < warmUp; i++) banked += MatchSim.Run(setup, MatchRng.MatchSeed(1UL, 0, -1 - i)).Banked[0];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < matches; i++) banked += MatchSim.Run(setup, MatchRng.MatchSeed(1UL, 0, i)).Banked[0];
            clock.Stop();

            double perMatch = clock.Elapsed.TotalMilliseconds / matches;
            log.Info(Source, $"Match timing: {perMatch:0.000} ms per farm-only match over {matches} matches, one thread (seat 0 banked {banked / (matches + warmUp):0.0} on average).");
            return perMatch;
        }

        [MenuItem(MatchMenuPath, priority = 66)]
        private static void MatchReportFromMenu() => MatchReport();

        /// <summary>
        /// Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.MatchReportBatch [-oracleSeed n] [-oracleThreads n]</c>. Plays the Mixed,
        /// One-per-class, Seat-check and Forced-kills designs under P0 and writes <c>Captures/oracle/match-report-&lt;UTC&gt;.md</c> and <c>.json</c>
        /// (read-only: no asset changes).
        /// </summary>
        public static void MatchReportBatch() => InBatch("The match report failed.", () => MatchReport());

        [MenuItem(SensitivityMenuPath, priority = 67)]
        private static void MatchSensitivityFromMenu() => MatchReport(true);

        /// <summary>
        /// Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.MatchSensitivityBatch [-oracleSeed n] [-oracleThreads n]</c>. The match report
        /// plus the hypercube sweep (16 points x 3 populations), the reaction-delay and leader-weight runs, with the sweep's tags on the kits' calls.
        /// Writes <c>Captures/oracle/match-sensitivity-&lt;UTC&gt;.md</c> and <c>.json</c>.
        /// </summary>
        public static void MatchSensitivityBatch() => InBatch("The match sensitivity run failed.", () => MatchReport(true));

        public static (string json, string markdown) MatchReport(bool withSweep = false)
        {
            var log = new UnityLogService(LogLevel.Info);
            if (Application.isPlaying) throw new InvalidOperationException("Leave Play mode before the match report: it reads the shipped assets.");

            var (seed, threads) = MatchArguments();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), BotArrivalRadius());
            // The three populations play the mixed, one-per-class and seat-check designs; the forced kills (the other seats take P0's play) once;
            // the two splits (P1 on seats 0 and 2, then on 1 and 3) are the head-to-head line.
            var runs = MatchReports.PlayStandard(context, seed, threads, true, line => log.Info(Source, line));

            MatchSweepOutput sweep = null;
            if (withSweep)
            {
                var inputs = Inputs();
                using (var book = new BalanceBook(log, inputs.Abilities, inputs.Classes, inputs.Match, inputs.Map))
                {
                    inputs.Book = book;
                    sweep = MatchSweep.Run(inputs, OracleMapBaker.ArenaHalf(), BotArrivalRadius(), new MatchSweepSpec { RunSeed = seed, Threads = threads });
                }
                log.Info(Source, $"Match sweep: {sweep.Points.Length} points x 3 populations, {sweep.Matches} matches in {sweep.ElapsedSeconds:0.0} s on {threads} threads.");
            }

            var section = MatchReports.Build(context, runs, LoadReference(), sweep);
            string dir = OutputDirectory();
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string prefix = withSweep ? "match-sensitivity" : "match-report";
            string json = Path.Combine(dir, $"{prefix}-{stamp}.json");
            string md = Path.Combine(dir, $"{prefix}-{stamp}.md");
            File.WriteAllText(json, section.ToJson());
            File.WriteAllText(md, section.ToMarkdown());
            WriteSample(context, seed, Path.Combine(dir, $"match-sample-{stamp}.md"));
            log.Info(Source, $"Match report written: {md} and {json} (bal={section.BalanceHash}, map={section.MapFingerprint}, seed={seed}); the {runs.Count} runs{(withSweep ? ", the sweep" : string.Empty)} and the report took {clock.Elapsed.TotalSeconds:0.0} s.");
            return (json, md);
        }

        private static (ulong seed, int threads) MatchArguments()
        {
            ulong seed = DefaultMatchSeed;
            int threads = DefaultMatchThreads;
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, SeedArgument);
            if (at >= 0 && at + 1 < args.Length && !ulong.TryParse(args[at + 1], out seed))
                throw new ArgumentException($"{SeedArgument} wants a whole number, got '{args[at + 1]}'.");
            at = Array.IndexOf(args, ThreadsArgument);
            if (at >= 0 && at + 1 < args.Length && !int.TryParse(args[at + 1], out threads))
                throw new ArgumentException($"{ThreadsArgument} wants a whole number, got '{args[at + 1]}'.");
            return (seed, threads);
        }

        [MenuItem(AcceptMatchMenuPath, priority = 68)]
        private static void AcceptMatchFromMenu()
        {
            if (Application.isBatchMode) { AcceptMatch(DefaultAcceptMatchNote); return; }
            OracleNoteWizard.Open("Record the match report", "Record",
                "A short note on why this match report is recorded. It is data to compare later reports with (the drift line); it changes no price and no target.", AcceptMatch);
        }

        /// <summary>
        /// Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.AcceptMatchBatch -oracleNote "why" [-oracleSeed n] [-oracleThreads n]</c>. Plays the
        /// standard match report on the shipped assets and appends a match record to the reference (spec 7, D5 (b)): part 1's entries and prices are not touched.
        /// </summary>
        public static void AcceptMatchBatch() => InBatch("Recording the match report failed.", () => AcceptMatch(NoteFromArguments(DefaultAcceptMatchNote)));

        /// <summary>Plays the standard match report on the shipped assets (three populations, no splits) and appends it to the reference's match records.</summary>
        public static void AcceptMatch(string note)
        {
            var log = new UnityLogService(LogLevel.Info);
            if (Application.isPlaying) throw new InvalidOperationException("Leave Play mode before recording a match report: it reads the shipped assets.");
            var (seed, threads) = MatchArguments();
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), BotArrivalRadius());
            var reference = LoadReference();
            var runs = MatchReports.PlayStandard(context, seed, threads, false, line => log.Info(Source, line));
            var section = MatchReports.Build(context, runs, reference);
            string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            OracleReferenceSO.AcceptMatch(reference, section, string.IsNullOrWhiteSpace(note) ? DefaultAcceptMatchNote : note.Trim(), date);
            EditorUtility.SetDirty(reference);
            AssetDatabase.SaveAssets();
            var last = reference.MatchRecords[reference.MatchRecords.Count - 1];
            log.Info(Source, $"Match record {reference.MatchRecords.Count} stored on {date}: {last.Lines.Count} lines, note \"{last.Note}\" (bal={last.BalanceHash}, seed={last.RunSeed}); part 1's {reference.Entries.Count} entries untouched.");
        }

        // A few mixed matches played again with their events recorded, written as plain lines.
        private static void WriteSample(MatchContext context, ulong seed, string path)
        {
            var run = MatchRunner.Run(context, new MatchRunSpec
            {
                Design = MatchDesignKind.Mixed, Replicates = 1, Population = PolicyPopulation.P0, RunSeed = seed, Threads = 1,
                CellLimit = 0, RecordEvents = true,
            });
            var sb = new System.Text.StringBuilder("# Sample mixed matches (events)\n");
            int step = Math.Max(1, run.Results.Length / SampleMatches);
            for (int i = 0; i < run.Results.Length && i < step * SampleMatches; i += step)
            {
                var cell = run.Cells[i];
                var r = run.Results[i];
                sb.Append($"\n## cell {i}: ");
                for (int s = 0; s < cell.KitOfSeat.Length; s++) sb.Append($"seat {s} {context.Kits[cell.KitOfSeat[s]].Label}; ");
                sb.Append($"winner seat {r.Winner}, ended {r.EndSeconds:0.0} s, banked {string.Join("/", Array.ConvertAll(r.Banked, b => b.ToString("0.0")))}, rejected {r.RejectedIntents}, lost to delay {r.CastsLostToReactionDelay}\n");
                foreach (var e in r.Events)
                    sb.Append($"{e.Time:0.00} {e.Kind} seat {e.Seat} other {e.Other} amount {e.Amount:0.0} {MatchEvent.CauseName(e.Cause)}\n");
            }
            File.WriteAllText(path, sb.ToString());
        }

        [MenuItem(SolveMenuPath, priority = 65)]
        private static void SolveFromMenu() => Solve();

        /// <summary>Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.SolveBatch</c>. Reads <c>Captures/oracle/problem.json</c>; proposes only, nothing is applied or published.</summary>
        public static void SolveBatch() => InBatch("The oracle solve failed.", () => Solve());

        /// <summary>
        /// Reads <c>Captures/oracle/problem.json</c>, runs <see cref="OracleSolver"/> over the shipped assets and writes
        /// <c>proposal-&lt;UTC&gt;.json</c> and <c>.md</c> next to it; returns the two paths. A missing or malformed problem file is
        /// logged with its path and what is wrong, then thrown. No asset is changed and nothing is published.
        /// </summary>
        public static (string json, string markdown) Solve()
        {
            var log = new UnityLogService(LogLevel.Info);
            if (Application.isPlaying) throw new InvalidOperationException("Leave Play mode before solving: the solver reads the shipped assets.");

            string dir = OutputDirectory();
            string problemPath = Path.Combine(dir, ProblemFileName);
            if (!File.Exists(problemPath))
            {
                string missing = $"The oracle problem file is missing: {problemPath}. Write one first (see OracleProblem.TryParse for its shape).";
                log.Error(Source, missing);
                throw new FileNotFoundException(missing, problemPath);
            }
            if (!OracleProblem.TryParse(File.ReadAllText(problemPath), out var problem, out string error))
            {
                string bad = $"The oracle problem file is malformed: {problemPath}: {error}.";
                log.Error(Source, bad);
                throw new InvalidOperationException(bad);
            }

            var inputs = Inputs();
            if (problem.Match != null)
            {
                problem.Match.ArenaHalf = OracleMapBaker.ArenaHalf();
                problem.Match.SightArrivalRadius = BotArrivalRadius();
                problem.Match.Progress = line => log.Info(Source, line);
                problem.Match.Reference = LoadReference();
            }
            using (var book = new BalanceBook(log, inputs.Abilities, inputs.Classes, inputs.Match, inputs.Map))
            {
                inputs.Book = book;
                var started = DateTime.UtcNow;
                var proposal = OracleSolver.Solve(inputs, problem, LoadReference().Offsets());

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string json = Path.Combine(dir, $"proposal-{stamp}.json");
                string md = Path.Combine(dir, $"proposal-{stamp}.md");
                File.WriteAllText(json, proposal.ToJson());
                File.WriteAllText(md, proposal.ToMarkdown());
                log.Info(Source, $"Oracle proposal written (nothing applied): {md} and {json}; score {proposal.ScoreBefore:0.00} -> {proposal.ScoreAfter:0.00} in {(DateTime.UtcNow - started).TotalSeconds:0} s.");
                return (json, md);
            }
        }

        private static string OutputDirectory()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Captures", "oracle");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [MenuItem(RecordMenuPath, priority = 62)]
        private static void RecordFromMenu()
        {
            if (Application.isBatchMode) { Record(DefaultRecordNote); return; }
            OracleNoteWizard.Open("Record the oracle baseline", "Record",
                "A short note on why the baseline is recorded. The kits' prices (offsets) stay as they are.", Record);
        }

        [MenuItem(AcceptMenuPath, priority = 63)]
        private static void AcceptFromMenu()
        {
            if (Application.isBatchMode) { Accept(DefaultAcceptNote); return; }
            OracleNoteWizard.Open("Accept the state as balanced", "Accept",
                "A short note on why this state is accepted. Every kit's price (offset) is RECOMPUTED so all results are equal.", Accept);
        }

        /// <summary>Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.RecordBatch -oracleNote "why"</c>. Keeps the stored offsets.</summary>
        public static void RecordBatch() => InBatch("Recording the oracle baseline failed.", () => Record(NoteFromArguments(DefaultRecordNote)));

        /// <summary>Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.AcceptBatch -oracleNote "why"</c>. Recomputes every offset.</summary>
        public static void AcceptBatch() => InBatch("Accepting the oracle reference failed.", () => Accept(NoteFromArguments(DefaultAcceptNote)));

        /// <summary>Measures the shipped assets and stores the result, the date and <paramref name="note"/> in the reference asset; the stored offsets are kept.</summary>
        public static void Record(string note) => Store(note, DefaultRecordNote, OracleReferenceSO.Record, "baseline recorded");

        /// <summary>Measures the shipped assets, stores the result and recomputes every kit's offset so all results are equal.</summary>
        public static void Accept(string note) => Store(note, DefaultAcceptNote, OracleReferenceSO.Accept, "accepted as balanced");

        private static void Store(string note, string fallback, Action<OracleReferenceSO, OracleReport, string, string> write, string what)
        {
            var reference = LoadReference();
            var report = OracleRunner.Run(Inputs(), null);
            string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            write(reference, report, string.IsNullOrWhiteSpace(note) ? fallback : note.Trim(), date);
            EditorUtility.SetDirty(reference);
            AssetDatabase.SaveAssets();
            new UnityLogService(LogLevel.Info).Info(Source,
                $"Oracle reference {what} on {date}: {reference.Entries.Count} kits, note \"{reference.Note}\" (bal={report.BalanceHash}, map={report.MapFingerprint}).");
        }

        private static string NoteFromArguments(string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, NoteArgument);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }

        private static void InBatch(string failure, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                new UnityLogService(LogLevel.Info).Error(Source, failure, e);
                throw;
            }
        }

        /// <summary>Runs the oracle on the shipped assets with the stored reference's offsets and returns the two file paths written.</summary>
        public static (string json, string markdown) Write()
        {
            var log = new UnityLogService(LogLevel.Info);
            var reference = LoadReference();
            var report = OracleRunner.Run(Inputs(), reference.Offsets());
            report.PricesNote = reference.PricesText();

            string dir = OutputDirectory();
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string json = Path.Combine(dir, $"report-{stamp}.json");
            string md = Path.Combine(dir, $"report-{stamp}.md");
            File.WriteAllText(json, report.ToJson());
            File.WriteAllText(md, report.ToMarkdown());

            log.Info(Source, $"Oracle report written: {md} and {json} (bal={report.BalanceHash}, map={report.MapFingerprint}).");
            return (json, md);
        }

        private static OracleInputs Inputs()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            if (map == null)
                throw new InvalidOperationException($"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath} first.");
            return new OracleInputs
            {
                Map = map,
                Abilities = Required<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = Required<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = Required<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
        }

        /// <summary>The bot's arrival radius as authored on the chicken prefab (<c>BotController.ArrivalRadius</c>); the match simulator's sight rule needs it.</summary>
        public static float BotArrivalRadius()
        {
            var prefab = Required<GameObject>(TestAssets.ChickenPrefabPath);
            var bot = prefab.GetComponentInChildren<BotController>(true);
            if (bot == null)
                throw new InvalidOperationException($"{TestAssets.ChickenPrefabPath} carries no BotController, so the bot's arrival radius cannot be read.");
            return bot.ArrivalRadius;
        }

        private static OracleReferenceSO LoadReference() => Required<OracleReferenceSO>(ReferencePath);

        private static T Required<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new InvalidOperationException($"Required {typeof(T).Name} asset not found at '{path}'.");
    }

    /// <summary>The one-line note asked for when recording or accepting a reference from the menu.</summary>
    internal sealed class OracleNoteWizard : ScriptableWizard
    {
        public string Note = "";
        private Action<string> _onCreate;

        public static void Open(string title, string button, string help, Action<string> onCreate)
        {
            var wizard = DisplayWizard<OracleNoteWizard>(title, button);
            wizard._onCreate = onCreate;
            wizard.helpString = help;
        }

        private void OnWizardCreate() => _onCreate?.Invoke(Note);
    }
}
