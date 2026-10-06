using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Localization;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the wording dictionary (<c>Resources/Text/UiText.csv</c>) and the rule that no
    /// player-facing literal lives in menu code or UXML: every key referenced anywhere exists,
    /// every template's placeholders are the ones its call sites supply, and a missing key or
    /// argument is loud rather than silent.
    /// </summary>
    public sealed class UiTextTests
    {
        private static readonly string[] MenuUxml =
        {
            "Assets/UI/MainMenu.uxml",
            "Assets/UI/CharacterSelectClass.uxml",
            "Assets/UI/CharacterSelect.uxml",
            "Assets/UI/Lobby.uxml",
        };

        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        // ---- Helpers ------------------------------------------------------------------

        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Errors = new();
            public LogLevel MinLevel { get; set; } = LogLevel.Verbose;
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) => Errors.Add(message);
        }

        private static string ProjectPath(string assetPath) =>
            Path.Combine(Application.dataPath, "..", assetPath);

        /// <summary>Every <see cref="UiKeys"/> constant: (key value, UiArgs contract or empty).</summary>
        private static List<(string field, string key, string[] args)> AllKeyConstants() =>
            typeof(UiKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (f.Name, (string)f.GetRawConstantValue(),
                    f.GetCustomAttribute<UiArgsAttribute>()?.Names ?? Array.Empty<string>()))
                .ToList();

        private static string[] KeysCsvColumn() => UiText.Keys.ToArray();

        // ---- Parser ---------------------------------------------------------------------

        [Test]
        public void Csv_HandlesQuotedCommasQuotesAndNewlines()
        {
            var t = UiText.ParseCsv("key,en\n" +
                                    "a,plain\n" +
                                    "b,\"has, comma\"\n" +
                                    "c,\"say \"\"hi\"\"\"\n" +
                                    "d,\"two\nlines\"\r\n" +
                                    "e,▶ unicode\n");
            Assert.AreEqual("plain", t["a"]);
            Assert.AreEqual("has, comma", t["b"]);
            Assert.AreEqual("say \"hi\"", t["c"]);
            Assert.AreEqual("two\nlines", t["d"]);
            Assert.AreEqual("▶ unicode", t["e"]);
        }

        [Test]
        public void Csv_DuplicateKey_IsRejected()
        {
            Assert.Throws<FormatException>(() => UiText.ParseCsv("key,en\na,1\na,2\n"));
        }

        [Test]
        public void Csv_UnterminatedQuote_IsRejected()
        {
            Assert.Throws<FormatException>(() => UiText.ParseCsv("key,en\na,\"oops\n"));
        }

        [Test]
        public void ShippedCsv_Loads_WithNoDuplicateKeysAndNoProblems()
        {
            Assert.Greater(KeysCsvColumn().Length, 50, "The shipped wording table did not load or is nearly empty.");
            Assert.IsEmpty(UiText.Problems, string.Join("\n", UiText.Problems));
        }

        // ---- Missing key / missing arg are loud --------------------------------------------

        [Test]
        public void Get_MissingKey_RendersAVisibleMarker_AndLogsOnce()
        {
            var log = new RecordingLog();
            UiText.SetLogger(log);

            Assert.AreEqual("#no.such.key#", UiText.Get("no.such.key"));
            UiText.Get("no.such.key");

            Assert.AreEqual(1, log.Errors.Count, "A missing key must be logged exactly once, not per read.");
            StringAssert.Contains("no.such.key", log.Errors[0]);
        }

        [Test]
        public void Problems_HeldBeforeALoggerExists_AreFlushedToIt()
        {
            UiText.Get("early.miss");
            var log = new RecordingLog();
            UiText.SetLogger(log);

            Assert.AreEqual(1, log.Errors.Count, "A problem found before the logger was registered must still reach it.");
        }

        [Test]
        public void Format_FillsNamedPlaceholders()
        {
            Assert.AreEqual("PICK 3 MORE", UiText.Format(UiKeys.BtnPickMoreMany, ("n", 3)));
            Assert.AreEqual("WARRIOR · Brawler",
                UiText.Format(UiKeys.LobbyClassLine, ("cls", "WARRIOR"), ("role", "Brawler")));
            Assert.IsEmpty(UiText.Problems);
        }

        [Test]
        public void Format_MissingArgument_RendersAMarkerAndLogs_NeverASilentBlank()
        {
            var log = new RecordingLog();
            UiText.SetLogger(log);

            string text = UiText.Format(UiKeys.BtnPickMoreMany); // {n} not supplied

            Assert.AreEqual("PICK #n# MORE", text);
            Assert.AreEqual(1, log.Errors.Count);
            StringAssert.Contains("{n}", log.Errors[0]);
        }

        // ---- Keys and constants agree --------------------------------------------------------

        [Test]
        public void EveryUiKeysConstant_HasACsvRow()
        {
            var missing = AllKeyConstants().Where(c => !UiText.HasKey(c.key)).Select(c => c.field).ToList();
            Assert.IsEmpty(missing, "UiKeys constants with no row in UiText.csv: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryCsvRow_HasAUiKeysConstant()
        {
            var known = new HashSet<string>(AllKeyConstants().Select(c => c.key));
            var orphans = UiText.Keys.Where(k => !known.Contains(k)).ToList();
            Assert.IsEmpty(orphans, "UiText.csv rows with no UiKeys constant (add one): " + string.Join(", ", orphans));
        }

        [Test]
        public void EveryTemplate_PlaceholdersMatchItsUiArgsContract()
        {
            var bad = new List<string>();
            foreach (var (field, key, args) in AllKeyConstants())
            {
                if (!UiText.TryGet(key, out var template)) continue;
                var actual = UiText.Placeholders(template).OrderBy(x => x).ToArray();
                var declared = args.OrderBy(x => x).ToArray();
                if (!actual.SequenceEqual(declared))
                    bad.Add($"{field}: template has {{{string.Join(",", actual)}}} but [UiArgs] says {{{string.Join(",", declared)}}}");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ---- Call sites ------------------------------------------------------------------------

        private static readonly string[] CodeFiles =
        {
            "Assets/_Game/Scripts/UI/MenuUiController.cs",
            "Assets/_Game/Scripts/UI/MatchSettingsText.cs",
            "Assets/_Game/Scripts/Abilities/PassiveAbilitySO.cs",
        };

        private static IEnumerable<string> AllCodeFiles() =>
            CodeFiles.Concat(Directory.GetFiles(ProjectPath("Assets/_Game/Scripts/Abilities/Passives"), "*PassiveSO.cs")
                .Select(Path.GetFullPath));

        private static string ReadCode(string path) =>
            File.ReadAllText(Path.IsPathRooted(path) ? path : ProjectPath(path));

        [Test]
        public void CodeNeverPassesAKeyLiteral_ToUiText()
        {
            var literalCall = new Regex(@"UiText\.(Get|Format|TryGet|HasKey)\(\s*""");
            var offenders = AllCodeFiles().Where(f => literalCall.IsMatch(ReadCode(f))).ToList();
            Assert.IsEmpty(offenders,
                "Name a UiKeys constant instead of a key literal (typos become compile errors): " + string.Join(", ", offenders));
        }

        [Test]
        public void MenuController_HasNoPlayerFacingStringLiterals()
        {
            // Text assignments and new Labels must come from the dictionary. Element names,
            // class names and log messages are not text and are not matched here.
            var literalText = new Regex(@"\.text\s*=\s*\$?""[^""]|new Label\(\s*\$?""[^""]|new Button\(\s*\$?""[^""]");
            string src = ReadCode("Assets/_Game/Scripts/UI/MenuUiController.cs");
            var hits = src.Split('\n')
                .Select((line, i) => (line, n: i + 1))
                .Where(x => !x.line.TrimStart().StartsWith("//") && literalText.IsMatch(x.line))
                .Select(x => $"line {x.n}: {x.line.Trim()}")
                .ToList();
            Assert.IsEmpty(hits, "Player-facing literals in MenuUiController (move to UiText.csv):\n" + string.Join("\n", hits));
        }

        [Test]
        public void EveryFormatCallSite_SuppliesExactlyTheArgsItsTemplateContract_Declares()
        {
            var contracts = AllKeyConstants().ToDictionary(c => c.field, c => c.args);
            var bad = new List<string>();

            foreach (var file in AllCodeFiles())
            {
                string src = ReadCode(file);
                int from = 0;
                while ((from = src.IndexOf("UiText.Format(", from, StringComparison.Ordinal)) >= 0)
                {
                    int open = from + "UiText.Format".Length;
                    int depth = 0, end = open;
                    for (; end < src.Length; end++)
                    {
                        if (src[end] == '(') depth++;
                        else if (src[end] == ')' && --depth == 0) break;
                    }
                    string call = src.Substring(open, end - open + 1);
                    from = end;

                    var keys = Regex.Matches(call, @"UiKeys\.(\w+)").Select(m => m.Groups[1].Value).Distinct().ToList();
                    var passed = Regex.Matches(call, @"\(\s*""(\w+)""\s*,").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();

                    // PassiveAbilitySO formats its own virtual key with PerkArgs; PerkTextTests covers every shipped passive.
                    if (keys.Count == 0 && Regex.IsMatch(call, @"^\(\s*Perk(Line|Detail)Key\s*,")) continue;
                    if (keys.Count == 0) { bad.Add($"{Path.GetFileName(file)}: Format call without a UiKeys constant: {call.Replace('\n', ' ')}"); continue; }
                    foreach (var k in keys)
                    {
                        if (!contracts.TryGetValue(k, out var declared)) { bad.Add($"{Path.GetFileName(file)}: unknown UiKeys.{k}"); continue; }
                        if (!passed.SequenceEqual(declared.OrderBy(x => x)))
                            bad.Add($"{Path.GetFileName(file)}: UiKeys.{k} wants {{{string.Join(",", declared)}}} but the call passes {{{string.Join(",", passed)}}}");
                    }
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void EveryPlainGetCallSite_UsesATemplateWithNoPlaceholders()
        {
            var contracts = AllKeyConstants().ToDictionary(c => c.field, c => c.args);
            var bad = new List<string>();
            var get = new Regex(@"UiText\.Get\(([^;]*?)\)(?=[;,)\s])");
            foreach (var file in AllCodeFiles())
                foreach (Match m in get.Matches(ReadCode(file)))
                    foreach (Match k in Regex.Matches(m.Groups[1].Value, @"UiKeys\.(\w+)"))
                        if (contracts.TryGetValue(k.Groups[1].Value, out var a) && a.Length > 0)
                            bad.Add($"{Path.GetFileName(file)}: UiText.Get(UiKeys.{k.Groups[1].Value}) returns a raw template; it needs Format with {{{string.Join(",", a)}}}");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ---- UXML -------------------------------------------------------------------------------------

        private static VisualElement CloneUxml(string path)
        {
            var vta = TestAssets.Load<VisualTreeAsset>(path);
            return vta.CloneTree();
        }

        private static IEnumerable<(string name, string text)> TextsIn(VisualElement root)
        {
            foreach (var ve in root.Query<VisualElement>().ToList())
            {
                if (ve is Toggle t) yield return (t.name, t.text);
                else if (ve is TextElement te) yield return (te.name, te.text);
                // Tooltips are the accessible names of icon-only buttons: player-facing too.
                if (!string.IsNullOrEmpty(ve.tooltip)) yield return (ve.name + " (tooltip)", ve.tooltip);
            }
        }

        [Test]
        public void MenuUxml_TextIsEitherEmptyOrAKeyReference_NeverALiteral()
        {
            var literals = new List<string>();
            foreach (var path in MenuUxml)
                foreach (var (name, text) in TextsIn(CloneUxml(path)))
                    if (!string.IsNullOrEmpty(text) && !UiText.IsKeyReference(text))
                        literals.Add($"{path} [{(string.IsNullOrEmpty(name) ? "unnamed" : name)}]: \"{text}\"");

            Assert.IsEmpty(literals,
                "Player-facing literals in menu UXML. Use text=\"@key\" (key in UiText.csv) or leave it " +
                "empty when code fills it:\n" + string.Join("\n", literals));
        }

        [Test]
        public void MenuUxml_EveryKeyReference_ExistsInTheDictionary()
        {
            var missing = new List<string>();
            foreach (var path in MenuUxml)
                foreach (var (name, text) in TextsIn(CloneUxml(path)))
                    if (UiText.IsKeyReference(text) && !UiText.HasKey(UiText.KeyOfReference(text)))
                        missing.Add($"{path} [{name}]: {text}");

            Assert.IsEmpty(missing, "UXML references keys the table lacks:\n" + string.Join("\n", missing));
        }

        [Test]
        public void ResolveTree_ReplacesKeyReferences_AndLeavesEverythingElseAlone()
        {
            var root = new VisualElement();
            var button = new Button { text = "@btn.ready" };
            var toggle = new Toggle { text = "@settings.devMode" };
            var plain = new Label("untouched");
            root.Add(button); root.Add(toggle); root.Add(plain);

            UiText.ResolveTree(root);

            Assert.AreEqual(UiText.Get(UiKeys.BtnReady), button.text);
            Assert.AreEqual(UiText.Get(UiKeys.SettingsDevMode), toggle.text);
            Assert.AreEqual("untouched", plain.text);
        }

        [Test]
        public void ResolvedMenuUxml_ShowsNoMarkers()
        {
            // After the controller's one-time resolve, no label may still read @key or #key#.
            foreach (var path in MenuUxml)
            {
                var root = CloneUxml(path);
                UiText.ResolveTree(root);
                foreach (var (name, text) in TextsIn(root))
                {
                    Assert.IsFalse(text != null && (text.StartsWith("@") || text.StartsWith("#")),
                        $"{path} [{name}] renders '{text}' after resolve.");
                }
            }
            Assert.IsEmpty(UiText.Problems, string.Join("\n", UiText.Problems));
        }
    }
}
