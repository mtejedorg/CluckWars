using System.IO;
using CluckWars.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The dev-tools build stamp (version + build number at the bottom of every screen): the pure composition, and that
    /// it exists only behind <see cref="DevTools.Available"/> (the Editor has the tools on, so the gate is pinned in the source).
    /// </summary>
    public sealed class DevBuildStampTests
    {
        private static string Read(string relative) =>
            File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), relative));

        [Test]
        public void Compose_JoinsVersionAndBuildNumberWithAPlus()
        {
            Assert.AreEqual("0.1+26462977", DevBuildStamp.Compose("0.1", BuildInfo.BuildNumberLabel(26462977)));
        }

        [Test]
        public void Compose_KeepsTheUnknownAndEditorLabels()
        {
            Assert.AreEqual("0.1+unknown", DevBuildStamp.Compose("0.1", BuildInfo.BuildNumberLabel(0)));
            Assert.AreEqual("0.1+editor", DevBuildStamp.Compose("0.1", "editor"));
        }

        /// <summary>
        /// The main menu shows no version stamp (owner, 2026-11-18); the only player-facing version is the About line at the
        /// bottom of the Settings sheet, and dev-tools builds add their own stamp. Pinned in the sources because the Editor has
        /// the tools on and a rendered check would pass either way.
        /// </summary>
        [Test]
        public void TheMainMenuHasNoVersionStamp_TheVersionShowsOnlyInTheSettingsAbout()
        {
            string uxml = Read("Assets/UI/MainMenu.uxml");
            StringAssert.DoesNotContain("name=\"BuildStamp\"", uxml, "MainMenu.uxml must not carry a version stamp");
            StringAssert.DoesNotContain("cw-menu-build", uxml);
            StringAssert.DoesNotContain("cw-menu-build", Read("Assets/UI/Styles/CluckWarsTheme.uss"));
            StringAssert.Contains("name=\"AboutVersion\"", uxml, "the Settings About line");

            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.DoesNotContain("BuildStamp", menu);
            StringAssert.DoesNotContain("Application.version", menu);
            StringAssert.DoesNotContain("MainBuild", menu);

            string sheets = Read("Assets/_Game/Scripts/UI/MenuUiController.Sheets.cs");
            Assert.AreEqual(1, Count(sheets, "Application.version"), "the version is read in exactly one place");
            Assert.AreEqual(1, Count(sheets, "UiKeys.MainBuild"));
            int build = sheets.IndexOf("private void BuildSettingsSheet()", System.StringComparison.Ordinal);
            int at = sheets.IndexOf("Application.version", System.StringComparison.Ordinal);
            int next = sheets.IndexOf("\n        private ", build + 10, System.StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(build).And.LessThan(next), "inside BuildSettingsSheet");

            StringAssert.Contains("main.build,", Read("Assets/_Game/Resources/Text/UiText.csv"));
            StringAssert.Contains("name=\"BuildStamp\"", Read("Assets/_Game/Resources/Dev/DevBuildStamp.uxml"), "the dev-tools stamp keeps its own document");
        }

        private static int Count(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        [Test]
        public void TheStampIsOnlyBoundInTheDevToolsInstallPath_AndRefusesToLiveWithoutDevTools()
        {
            string installer = Read("Assets/_Game/Scripts/Installers/ProjectInstaller.cs").Replace("\r\n", "\n");
            int diag = installer.IndexOf("private void InstallDiagnostics()", System.StringComparison.Ordinal);
            Assert.Greater(diag, 0);
            Assert.Greater(installer.IndexOf("Bind<DevBuildStamp>()", diag, System.StringComparison.Ordinal), diag, "bound next to the other dev-tools objects");
            int call = installer.IndexOf("if (devTools) InstallDiagnostics();", System.StringComparison.Ordinal);
            Assert.Greater(call, 0, "InstallDiagnostics runs only when DevTools.Available");
            Assert.Less(call, diag);

            string stamp = Read("Assets/_Game/Scripts/Diagnostics/DevBuildStamp.cs").Replace("\r\n", "\n");
            int awake = stamp.IndexOf("private void Awake()", System.StringComparison.Ordinal);
            int gate = stamp.IndexOf("if (!DevTools.Available)", awake, System.StringComparison.Ordinal);
            int destroy = stamp.IndexOf("Destroy(gameObject);", awake, System.StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(awake).And.LessThan(destroy), "without dev tools the component destroys itself first");
        }
    }
}
