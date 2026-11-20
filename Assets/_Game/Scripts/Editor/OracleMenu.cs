using System;
using System.Collections.Generic;
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

        /// <summary>Runs the oracle with no offsets (the accepted reference arrives with a later task) and returns the two file paths written.</summary>
        public static (string json, string markdown) Write()
        {
            var log = new UnityLogService(LogLevel.Info);
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            if (map == null)
                throw new InvalidOperationException($"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath} first.");

            var inputs = new OracleInputs
            {
                Map = map,
                Abilities = Required<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = Required<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = Required<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
            var report = OracleRunner.Run(inputs, new Dictionary<string, float>());

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

        private static T Required<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new InvalidOperationException($"Required {typeof(T).Name} asset not found at '{path}'.");
    }
}
