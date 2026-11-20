using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Balance;
using CluckWars.Diagnostics;
using CluckWars.Localization;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The dev-tools balance editor (ARCH-DATA-14, owner ruling 2026-11-18): that it and the local balance path are reachable
    /// only behind <see cref="DevTools.Available"/> (the Editor has the tools on, so the gates are pinned in the sources), that
    /// nothing in it talks to a network service, that the menu sees only its interface, and its pure helpers (filter, oracle report
    /// text). The behaviour of the local path is <c>BalanceLocalTests</c>.
    /// </summary>
    public sealed class DevBalanceEditorTests
    {
        private const string BookLocal = "Assets/_Game/Scripts/Balance/BalanceBook.Local.cs";
        private const string StoreFile = "Assets/_Game/Scripts/Balance/BalanceLocalStore.cs";
        private const string OverrideFile = "Assets/_Game/Scripts/Balance/BalanceLocalOverride.cs";
        private const string SctFile = "Assets/_Game/Scripts/Balance/BalanceSctCheck.cs";
        private const string EditorFile = "Assets/_Game/Scripts/Diagnostics/DevBalanceEditor.cs";
        private const string InstallerFile = "Assets/_Game/Scripts/Installers/ProjectInstaller.cs";

        private static string Read(string relative) =>
            File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), relative)).Replace("\r\n", "\n");

        private static int Count(string text, string needle)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        // ---- The gates, pinned in the source -----------------------------------------------------------------

        [Test]
        public void EveryLocalEntryPointOfTheBook_ChecksTheDevToolsGateFirst()
        {
            string src = Read(BookLocal);
            Assert.AreEqual(1, Count(src, "LocalAllowed(DevTools.Available)"), "one place reads DevTools for the book's local path");
            foreach (string method in new[]
            {
                "public bool ApplyLocal(", "public bool TryApplyLocal(", "public IReadOnlyList<BalanceFieldInfo> DescribeFields(",
                "public bool TryBuildDeltaJson(", "public OracleCheck CheckOracle(",
            })
            {
                int at = src.IndexOf(method, System.StringComparison.Ordinal);
                Assert.Greater(at, 0, method);
                int next = src.IndexOf("\n        public ", at + 10, System.StringComparison.Ordinal);
                string body = src.Substring(at, (next < 0 ? src.Length : next) - at);
                Assert.Greater(body.IndexOf("CheckLocalAllowed(", System.StringComparison.Ordinal), 0, method + " must check the gate");
                // The check precedes any use of the book's state.
                Assert.Less(body.IndexOf("CheckLocalAllowed(", System.StringComparison.Ordinal),
                            body.IndexOf("_current", System.StringComparison.Ordinal) < 0 ? int.MaxValue : body.IndexOf("_current", System.StringComparison.Ordinal),
                            method);
            }
        }

        [Test]
        public void ApplyItself_RefusesALocalOriginWithoutDevTools()
        {
            string src = Read("Assets/_Game/Scripts/Balance/BalanceBook.cs");
            int apply = src.IndexOf("public bool Apply(BalanceVerdict verdict, BalanceOrigin origin)", System.StringComparison.Ordinal);
            Assert.Greater(apply, 0);
            int gate = src.IndexOf("origin == BalanceOrigin.Local && !CheckLocalAllowed(", apply, System.StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(apply).And.LessThan(src.IndexOf("RestoreBaseline();", apply, System.StringComparison.Ordinal)),
                "the gate precedes any change of state");
        }

        [Test]
        public void TheLocalFileStore_GatesEveryOperationInsideItself()
        {
            string src = Read(StoreFile);
            Assert.AreEqual(3, Count(src, "BalanceBook.LocalAllowed(devToolsAvailable)"), "Read, Write and Clear each check the gate");
            Assert.AreEqual(3, Count(src, "DevTools.Available"), "the no-argument overloads pass the real answer");
            int read = src.IndexOf("public string Read(bool devToolsAvailable)", System.StringComparison.Ordinal);
            Assert.Less(read, src.IndexOf("File.ReadAllText", System.StringComparison.Ordinal));
            Assert.Less(src.IndexOf("LocalAllowed(devToolsAvailable)", read, System.StringComparison.Ordinal),
                        src.IndexOf("File.Exists", read, System.StringComparison.Ordinal), "the gate comes before the file is touched");
        }

        [Test]
        public void TheOverrideService_ChecksTheGateInApplyAtBootApplyAndReset()
        {
            string src = Read(OverrideFile);
            Assert.AreEqual(3, Count(src, "if (!BalanceBook.LocalAllowed(DevTools.Available))"));
            foreach (string method in new[] { "public void ApplyAtBoot()", "public LocalOverrideResult Apply(string json)", "public LocalOverrideResult Reset()" })
            {
                int at = src.IndexOf(method, System.StringComparison.Ordinal);
                Assert.Greater(at, 0, method);
                Assert.Less(src.IndexOf("LocalAllowed(DevTools.Available)", at, System.StringComparison.Ordinal) - at, 120, method + " starts with the gate");
            }
        }

        [Test]
        public void TheOverrideAndTheEditor_AreBoundOnlyInTheDevToolsInstallPaths()
        {
            string installer = Read(InstallerFile);
            int balance = installer.IndexOf("private void InstallBalance(", System.StringComparison.Ordinal);
            int diag = installer.IndexOf("private void InstallDiagnostics()", System.StringComparison.Ordinal);
            Assert.Greater(balance, 0);
            Assert.Greater(diag, balance);

            Assert.AreEqual(1, Count(installer, "new BalanceLocalOverride("));
            int ctor = installer.IndexOf("new BalanceLocalOverride(", System.StringComparison.Ordinal);
            int gate = installer.LastIndexOf("if (devTools)", ctor, System.StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(balance).And.LessThan(ctor));
            Assert.IsFalse(installer.Substring(gate, ctor - gate).Contains("}"), "the construction sits directly inside the devTools block");
            Assert.AreEqual(1, Count(installer, "Bind<BalanceLocalOverride>()"));

            Assert.AreEqual(1, Count(installer, "BindInterfacesAndSelfTo<DevBalanceEditor>()"));
            Assert.Greater(installer.IndexOf("BindInterfacesAndSelfTo<DevBalanceEditor>()", System.StringComparison.Ordinal), diag, "bound in InstallDiagnostics");
            int call = installer.IndexOf("if (devTools) InstallDiagnostics();", System.StringComparison.Ordinal);
            Assert.Greater(call, 0, "InstallDiagnostics runs only when DevTools.Available");
        }

        [Test]
        public void TheEditor_RefusesToLiveOrOpenWithoutDevTools()
        {
            string src = Read(EditorFile);
            int awake = src.IndexOf("private void Awake()", System.StringComparison.Ordinal);
            int gate = src.IndexOf("if (!DevTools.Available)", awake, System.StringComparison.Ordinal);
            int destroy = src.IndexOf("Destroy(gameObject);", awake, System.StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(awake).And.LessThan(destroy));

            int open = src.IndexOf("public void Open()", System.StringComparison.Ordinal);
            int openGate = src.IndexOf("if (!DevTools.Available)", open, System.StringComparison.Ordinal);
            Assert.That(openGate, Is.GreaterThan(open).And.LessThan(src.IndexOf("EnsureUi()", open, System.StringComparison.Ordinal)));
        }

        [Test]
        public void TheMenuSeesOnlyTheInterface_AndTheButtonHidesWithoutOne()
        {
            var controllers = Directory.GetFiles(Path.Combine(Application.dataPath, "_Game/Scripts/UI"), "MenuUiController*.cs")
                .Select(f => File.ReadAllText(f)).ToList();
            foreach (string src in controllers)
                Assert.IsFalse(Regex.IsMatch(src, @"(?<![A-Za-z])DevBalanceEditor\b"), "the menu must not name the Diagnostics class");
            string all = string.Concat(controllers);
            StringAssert.Contains("[InjectOptional] CluckWars.Diagnostics.IDevBalanceEditor", all);
            StringAssert.Contains("DevToolsAvailable && _devBalanceEditor != null", all);
        }

        [Test]
        public void NothingInTheLocalPath_TalksToANetworkServiceOrLogsThroughDebug()
        {
            foreach (string file in new[] { BookLocal, StoreFile, OverrideFile, SctFile, EditorFile })
            {
                string src = Read(file);
                foreach (string banned in new[]
                {
                    "UnityWebRequest", "HttpClient", "WebClient", "Unity.Services", "IUGSService", "UGS_SERVICE_",
                    "services.api.unity.com", "Debug.Log", "FromJsonOverwrite",
                })
                    Assert.IsFalse(src.Contains(banned), $"{file} contains {banned}");
            }
        }

        // ---- The UXML ----------------------------------------------------------------------------------------

        [Test]
        public void TheEditorDocument_HasEveryElementTheCodeQueries_AndOnlyKeyOrEmptyText()
        {
            string uxml = Read("Assets/_Game/Resources/Dev/DevBalanceEditor.uxml");
            foreach (string name in new[]
            {
                "BalanceRoot", "List", "StateLine", "Report", "Filter", "DeltaText", "CheckBtn", "ApplyBtn", "ResetBtn", "CopyBtn", "CloseBtn",
            })
                StringAssert.Contains($"name=\"{name}\"", uxml, name);

            string row = Read("Assets/_Game/Resources/Dev/DevBalanceRow.uxml");
            foreach (string name in new[] { "Row", "FieldName", "Shipped", "Current", "NumberInput", "BoolInput", "EnumInput" })
                StringAssert.Contains($"name=\"{name}\"", row, name);

            foreach (string doc in new[] { uxml, row })
                foreach (Match m in Regex.Matches(doc, "\\btext=\"([^\"]*)\""))
                {
                    string text = m.Groups[1].Value;
                    if (text.Length == 0) continue;
                    Assert.IsTrue(UiText.IsKeyReference(text), $"literal UXML text \"{text}\": use an @key");
                    Assert.IsTrue(UiText.HasKey(UiText.KeyOfReference(text)), $"missing UiText.csv row for {text}");
                }
        }

        [Test]
        public void TheMainMenuHasABalanceEditorButton_InTheDevRowOnly()
        {
            string uxml = Read("Assets/UI/MainMenu.uxml");
            int dev = uxml.IndexOf("name=\"DevRow\"", System.StringComparison.Ordinal);
            int button = uxml.IndexOf("name=\"BalanceEditorBtn\"", System.StringComparison.Ordinal);
            Assert.Greater(dev, 0);
            Assert.Greater(button, dev, "the button sits inside the Developer Mode row, which C# shows only with dev tools and Developer Mode on");
            Assert.IsTrue(UiText.HasKey(UiKeys.BtnBalanceEditor));
        }

        // ---- Pure helpers ------------------------------------------------------------------------------------

        [Test]
        public void Matches_FiltersByAssetOrFieldIgnoringCase_AndBlankPassesEverything()
        {
            Assert.IsTrue(DevBalanceEditor.Matches("Snatch", "Cooldown", ""));
            Assert.IsTrue(DevBalanceEditor.Matches("Snatch", "Cooldown", "   "));
            Assert.IsTrue(DevBalanceEditor.Matches("Snatch", "Cooldown", "snat"));
            Assert.IsTrue(DevBalanceEditor.Matches("Snatch", "Cooldown", " COOL "));
            Assert.IsFalse(DevBalanceEditor.Matches("Snatch", "Cooldown", "speed"));
        }
    }
}
