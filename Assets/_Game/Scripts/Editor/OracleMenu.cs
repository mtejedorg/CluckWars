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
        public const string ReferencePath = "Assets/_Game/Data/Balance/OracleReference.asset";
        public const string NoteArgument = "-oracleNote";
        private const string DefaultRecordNote = "baseline recorded from the menu";
        private const string DefaultAcceptNote = "accepted from the menu";
        private const string Source = "OracleReport";

        [MenuItem(MenuPath, priority = 61)]
        private static void ReportFromMenu() => Write();

        /// <summary>Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.ReportBatch</c>. A failure is logged and rethrown so the Editor exits non-zero.</summary>
        public static void ReportBatch() => InBatch("The oracle report failed.", () => Write());

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

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(projectRoot, "Captures", "oracle");
            Directory.CreateDirectory(dir);
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
