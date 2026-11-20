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
        public const string AcceptMenuPath = "Cluck Wars/Balance/Oracle/Accept as reference";
        public const string ReferencePath = "Assets/_Game/Data/Balance/OracleReference.asset";
        public const string NoteArgument = "-oracleNote";
        private const string DefaultNote = "accepted from the menu";
        private const string Source = "OracleReport";

        [MenuItem(MenuPath, priority = 61)]
        private static void ReportFromMenu() => Write();

        /// <summary>Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.ReportBatch</c>. A failure is logged and rethrown so the Editor exits non-zero.</summary>
        public static void ReportBatch()
        {
            try
            {
                Write();
            }
            catch (Exception e)
            {
                new UnityLogService(LogLevel.Info).Error(Source, "The oracle report failed.", e);
                throw;
            }
        }

        [MenuItem(AcceptMenuPath, priority = 62)]
        private static void AcceptFromMenu()
        {
            if (Application.isBatchMode) { Accept(DefaultNote); return; }
            OracleNoteWizard.Open(Accept);
        }

        /// <summary>
        /// Batch entry: <c>-executeMethod CluckWars.EditorTools.OracleMenu.AcceptBatch -oracleNote "why"</c>. Without the
        /// argument the note is the menu's default. A failure is logged and rethrown so the Editor exits non-zero.
        /// </summary>
        public static void AcceptBatch()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                string note = DefaultNote;
                int at = Array.IndexOf(args, NoteArgument);
                if (at >= 0 && at + 1 < args.Length) note = args[at + 1];
                Accept(note);
            }
            catch (Exception e)
            {
                new UnityLogService(LogLevel.Info).Error(Source, "Accepting the oracle reference failed.", e);
                throw;
            }
        }

        /// <summary>Measures the shipped assets and stores the result, the date and <paramref name="note"/> in the reference asset.</summary>
        public static void Accept(string note)
        {
            var reference = LoadReference();
            var report = OracleRunner.Run(Inputs(), null);
            string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            OracleReferenceSO.Accept(reference, report, string.IsNullOrWhiteSpace(note) ? DefaultNote : note.Trim(), date);
            EditorUtility.SetDirty(reference);
            AssetDatabase.SaveAssets();
            new UnityLogService(LogLevel.Info).Info(Source,
                $"Oracle reference accepted on {date}: {reference.Entries.Count} kits, note \"{reference.Note}\" (bal={report.BalanceHash}, map={report.MapFingerprint}).");
        }

        /// <summary>Runs the oracle on the shipped assets with the accepted reference's offsets and returns the two file paths written.</summary>
        public static (string json, string markdown) Write()
        {
            var log = new UnityLogService(LogLevel.Info);
            var report = OracleRunner.Run(Inputs(), LoadReference().Offsets());

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

    /// <summary>The one-line note asked for when accepting a reference from the menu.</summary>
    internal sealed class OracleNoteWizard : ScriptableWizard
    {
        public string Note = "";
        private Action<string> _onAccept;

        public static void Open(Action<string> onAccept)
        {
            var wizard = DisplayWizard<OracleNoteWizard>("Accept the oracle reference", "Accept");
            wizard._onAccept = onAccept;
            wizard.helpString = "A short note on why this state is accepted (kept with the date in OracleReference.asset).";
        }

        private void OnWizardCreate() => _onAccept?.Invoke(Note);
    }
}
