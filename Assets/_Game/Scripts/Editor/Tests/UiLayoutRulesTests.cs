using System.Collections.Generic;
using System.IO;
using CluckWars.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// WP8 / PORTRAIT_AND_LANDSCAPE_SPEC §3.5: the UI layout probe's pure rules (U1-U6) on synthetic panel rects, its
    /// allow-list, its log line contract (merge.ps1 keys on <c>ui offenders=N scope=...</c>) and its wiring.
    /// </summary>
    public sealed class UiLayoutRulesTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private static Rect R(float x, float y, float w, float h) => new Rect(x, y, w, h);

        // ---- geometry ------------------------------------------------------------------------------------------------

        [Test]
        public void Overlap_IntersectionArea_AndGap()
        {
            Assert.AreEqual(100f, UiLayoutRules.IntersectionArea(R(0, 0, 20, 20), R(10, 10, 20, 20)), 1e-4f);
            Assert.AreEqual(0f, UiLayoutRules.IntersectionArea(R(0, 0, 10, 10), R(10, 0, 10, 10)), "touching edges share no area");
            Assert.AreEqual(0f, UiLayoutRules.Gap(R(0, 0, 10, 10), R(5, 5, 10, 10)), "overlapping rects have no gap");
            Assert.AreEqual(8f, UiLayoutRules.Gap(R(0, 0, 10, 10), R(18, 0, 10, 10)), 1e-4f, "side by side");
            Assert.AreEqual(5f, UiLayoutRules.Gap(R(0, 0, 10, 10), R(13, 14, 10, 10)), 1e-4f, "diagonal: euclidean 3-4-5");
        }

        [Test]
        public void Containment_AndOutside()
        {
            var panel = R(0, 0, 1920, 1080);
            Assert.IsTrue(UiLayoutRules.Contains(panel, R(0, 0, 1920, 1080)));
            Assert.IsTrue(UiLayoutRules.Contains(panel, R(-0.4f, 0, 100, 100)), "sub-pixel rounding at an edge is inside");
            Assert.IsTrue(UiLayoutRules.PartlyOutside(R(-30, 10, 100, 40), panel), "a title cut at the left edge");
            Assert.IsFalse(UiLayoutRules.PartlyOutside(R(-300, 10, 100, 40), panel), "wholly off screen is not 'cut'");
            Assert.IsTrue(UiLayoutRules.FullyOutside(R(-300, 10, 100, 40), panel));
            Assert.IsFalse(UiLayoutRules.IsMeasurable(R(float.NaN, 0, 10, 10)));
            Assert.IsFalse(UiLayoutRules.IsMeasurable(R(0, 0, 0, 10)));
        }

        // ---- U1 -------------------------------------------------------------------------------------------------------

        [Test]
        public void U1_Clusters_NeedAnEightPixelGap()
        {
            var a = UiShape.Box(R(0, 0, 100, 100));
            Assert.IsFalse(UiLayoutRules.ClustersConflict(a, UiShape.Box(R(108, 0, 50, 50)), out float g1), "exactly 8 px is enough");
            Assert.AreEqual(8f, g1, 1e-4f);
            Assert.IsTrue(UiLayoutRules.ClustersConflict(a, UiShape.Box(R(107, 0, 50, 50)), out _), "7 px is too close");
            Assert.IsTrue(UiLayoutRules.ClustersConflict(a, UiShape.Box(R(90, 0, 50, 50)), out float g2), "overlap");
            Assert.Less(g2, 0f, "an overlap reads as a negative gap");
        }

        [Test]
        public void U1_HexRingPad_IsADisc()
        {
            // Thumb-arc centres (TouchControls.uss, from the bottom-right corner): a2 (217,436), a3 (377,344), 184.6 px apart.
            var a2 = UiShape.Disc(new Vector2(-217, -436), UiLayoutRules.HexRingRadiusPx);
            var a3 = UiShape.Disc(new Vector2(-377, -344), UiLayoutRules.HexRingRadiusPx);
            Assert.IsFalse(UiLayoutRules.ClustersConflict(a2, a3, out float gap), "the rings clear each other");
            Assert.AreEqual(184.58f - 160f, gap, 0.05f);

            // A disc against a box: the 4:3 touch case (banner plaque reaching over hex a4's ring).
            var hex = UiShape.Disc(new Vector2(100, 100), 80);
            Assert.IsTrue(UiLayoutRules.ClustersConflict(hex, UiShape.Box(R(170, 0, 200, 200)), out float g3));
            Assert.Less(g3, 0f);
            Assert.AreEqual(20f, UiLayoutRules.ShapeGap(hex, UiShape.Box(R(200, 0, 50, 200))), 1e-3f, "disc edge 180, box at 200");
            Assert.Less(UiLayoutRules.DiscRectGap(new Vector2(10, 10), 5, R(0, 0, 100, 100)), 0f, "centre inside the box");
        }

        [Test]
        public void U1_TextBoxes_OverlapUnlessOneContainsTheOther()
        {
            Assert.IsTrue(UiLayoutRules.TextBoxesOverlap(R(0, 0, 100, 40), R(90, 0, 100, 40)));
            Assert.IsFalse(UiLayoutRules.TextBoxesOverlap(R(0, 0, 300, 120), R(20, 20, 100, 40)), "a label on its card");
            Assert.IsFalse(UiLayoutRules.TextBoxesOverlap(R(0, 0, 100, 40), R(100, 0, 100, 40)), "touching is not overlapping");
            Assert.IsFalse(UiLayoutRules.TextBoxesOverlap(R(0, 0, 100, 40), R(99.9f, 0, 100, 0.5f)), "sub-pixel slivers are rounding");
        }

        // ---- U2 -------------------------------------------------------------------------------------------------------

        [Test]
        public void U2_TargetSize_And_Spacing()
        {
            Assert.IsFalse(UiLayoutRules.TargetTooSmall(R(0, 0, 116, 116)));
            Assert.IsTrue(UiLayoutRules.TargetTooSmall(R(0, 0, 200, 100)), "a wide but short plank fails");
            Assert.IsTrue(UiLayoutRules.TargetTooSmall(R(0, 0, 112.5f, 112.5f)), "a 150 px hex at 0.75 (desktop strip) is no touch target");

            Assert.IsFalse(UiLayoutRules.TargetsTooClose(R(0, 0, 150, 150), R(169, 0, 150, 150), out _), "19 px apart");
            Assert.IsTrue(UiLayoutRules.TargetsTooClose(R(0, 0, 226, 290), R(236, 0, 226, 290), out float gap), "deck cards 10 px apart");
            Assert.AreEqual(10f, gap, 1e-4f);
            Assert.IsTrue(UiLayoutRules.TargetsTooClose(R(0, 0, 150, 150), R(100, 0, 150, 150), out float g2), "overlapping targets");
            Assert.Less(g2, 0f);
            Assert.IsFalse(UiLayoutRules.TargetsTooClose(R(0, 0, 400, 300), R(10, 10, 116, 116), out _), "a button inside its card is not a neighbour");
        }

        [Test]
        public void OwnScale_IsJuice_TheBoxIsJudgedAtScaleOne()
        {
            // The countdown digit's 162x350 label slammed to 1.4 about its centre (960, 175): 227x490 from y -70.
            var slammed = R(960 - 113.4f, 175 - 245, 226.8f, 490);
            var rest = UiLayoutRules.Unscaled(slammed, 1.4f, 1.4f);
            Assert.AreEqual(162f, rest.width, 1e-3f);
            Assert.AreEqual(350f, rest.height, 1e-3f);
            Assert.AreEqual(0f, rest.yMin, 1e-3f, "at rest it starts at the panel top: not 'partly off the panel'");
            Assert.AreEqual(slammed, UiLayoutRules.Unscaled(slammed, 0f, 1f), "a zero scale leaves the box alone");
        }

        [Test]
        public void U2_HexRestRect_IgnoresTheHeldPop()
        {
            var r = UiLayoutRules.HexRestRect(new Vector2(500, 500), 0.75f);
            Assert.AreEqual(112.5f, r.width, 1e-4f);
            Assert.AreEqual(new Vector2(500, 500), r.center);
            Assert.AreEqual(150f, UiLayoutRules.HexRestRect(Vector2.zero, 1f).height, 1e-4f);
        }

        /// <summary>The shipped thumb arc: only the a2/a3 boxes break the 19 px rule, and the allow-list carries that pair.</summary>
        [Test]
        public void U2_ShippedThumbArc_OnlyA2A3_AndItIsAllowListed()
        {
            // Box centres from the bottom-right corner (TouchControls.uss): a1 (170,170) a2 (217,436) a3 (377,344) a4 (440,170).
            var centres = new[] { new Vector2(-170, -170), new Vector2(-217, -436), new Vector2(-377, -344), new Vector2(-440, -170) };
            var close = new List<string>();
            for (int i = 0; i < centres.Length; i++)
            for (int j = i + 1; j < centres.Length; j++)
                if (UiLayoutRules.TargetsTooClose(UiLayoutRules.HexRestRect(centres[i], 1f), UiLayoutRules.HexRestRect(centres[j], 1f), out _))
                    close.Add($"Ability{i + 1}~Ability{j + 1}");
            CollectionAssert.AreEqual(new[] { "Ability2~Ability3" }, close);

            string reason = UiLayoutAllowList.Find(UiRule.U2, s => s == "Ability3", s => s == "Ability2");
            Assert.IsNotNull(reason, "the pair is accepted in either order");
            StringAssert.Contains("TouchHudLayoutTests", reason);
            Assert.IsNull(UiLayoutAllowList.Find(UiRule.U2, s => s == "Ability1", s => s == "Ability2"), "no other hex pair is excused");
            Assert.IsNull(UiLayoutAllowList.Find(UiRule.U1, s => s == "Ability3", s => s == "Ability2"), "the U2 entry does not excuse a U1 overlap");
        }

        // ---- U3 / U4 --------------------------------------------------------------------------------------------------

        [Test]
        public void U3_TextFit()
        {
            Assert.IsFalse(UiLayoutRules.TextClipped(150.9f, 150f), "within the rounding tolerance");
            Assert.IsTrue(UiLayoutRules.TextClipped(155f, 150f), "RRIOR / ASSAS: the name is wider than its box");
            Assert.IsFalse(UiLayoutRules.WrappedTextClipped(80f, 80f));
            Assert.IsTrue(UiLayoutRules.WrappedTextClipped(120f, 80f), "a wrapped paragraph taller than its fixed box");

            Assert.IsNotNull(UiLayoutAllowList.Find(UiRule.U3, s => s == ".cw-lb-name", null), "leaderboard names ellipsize by design");
            Assert.IsNotNull(UiLayoutAllowList.Find(UiRule.U3, s => s == ".cw-me-pod__name", null), "podium names ellipsize by design");
            Assert.IsNull(UiLayoutAllowList.Find(UiRule.U3, s => s == ".cw-menu-title", null), "the title must be whole");
        }

        [Test]
        public void U4_TypeFloors_ByScope_AndCaptionAllowList()
        {
            Assert.AreEqual(28f, UiLayoutRules.FontFloor(UiScope.Hud));
            Assert.AreEqual(28f, UiLayoutRules.FontFloor(UiScope.Overlay));
            Assert.AreEqual(30f, UiLayoutRules.FontFloor(UiScope.Menu));
            Assert.IsFalse(UiLayoutRules.BelowFontFloor(28f, 28f));
            Assert.IsTrue(UiLayoutRules.BelowFontFloor(21f, 28f), "a 28 px hex label at 0.75 scale");
            Assert.IsFalse(UiLayoutRules.BelowFontFloor(30f, 30f));

            Assert.IsNotNull(UiLayoutAllowList.Find(UiRule.U4, s => s == ".cw-menu-build", null, 28f), "the build stamp may sit at 28 px");
            Assert.IsNull(UiLayoutAllowList.Find(UiRule.U4, s => s == ".cw-menu-build", null, 24f), "but never under its entry's floor");
            Assert.IsNull(UiLayoutAllowList.Find(UiRule.U4, s => s == ".cw-btn", null, 28f), "a button label is load-bearing");
        }

        // ---- U5 -------------------------------------------------------------------------------------------------------

        [Test]
        public void U5_SafeArea_InPanelSpace()
        {
            // 540x960 window, panel 1080x1920 (2x). Insets l,b,r,t = 0, 19, 0, 53 screen px (portrait 0.02 / 0.055).
            var safeScreen = Rect.MinMaxRect(0, 19, 540, 960 - 53);
            var safe = UiLayoutRules.ScreenSafeToPanel(safeScreen, 540, 960, R(0, 0, 1080, 1920));
            Assert.AreEqual(0f, safe.xMin, 1e-3f);
            Assert.AreEqual(106f, safe.yMin, 1e-3f, "top inset (screen y-up -> panel y-down) doubled");
            Assert.AreEqual(1920f - 38f, safe.yMax, 1e-3f, "bottom inset doubled");
            Assert.AreEqual(1080f, safe.xMax, 1e-3f);

            Assert.IsFalse(UiLayoutRules.Contains(safe, R(20, 60, 640, 256)), "a board drawn under the punch-hole is outside");
            Assert.IsTrue(UiLayoutRules.Contains(safe, R(20, 126, 640, 256)), "padded below the inset it is inside");

            var full = UiLayoutRules.ScreenSafeToPanel(Rect.MinMaxRect(0, 0, 960, 540), 960, 540, R(0, 0, 1920, 1080));
            Assert.AreEqual(R(0, 0, 1920, 1080), full, "no inset = the whole panel");
        }

        // ---- U6 -------------------------------------------------------------------------------------------------------

        [Test]
        public void U6_ExactlyOneOrientationClass_MatchingTheAspect()
        {
            Assert.AreEqual(OrientationVerdict.Ok, UiLayoutRules.CheckOrientation(false, true, 1920, 1080));
            Assert.AreEqual(OrientationVerdict.Ok, UiLayoutRules.CheckOrientation(true, false, 776, 1724));
            Assert.AreEqual(OrientationVerdict.Missing, UiLayoutRules.CheckOrientation(false, false, 1920, 1080));
            Assert.AreEqual(OrientationVerdict.Both, UiLayoutRules.CheckOrientation(true, true, 1920, 1080));
            Assert.AreEqual(OrientationVerdict.Mismatch, UiLayoutRules.CheckOrientation(false, true, 776, 1724), "a stale class after a rotation");
            Assert.AreEqual(OrientationVerdict.Ok, UiLayoutRules.CheckOrientation(false, true, 1000, 1000), "square is landscape (LayoutClasses)");
            Assert.AreEqual(OrientationVerdict.Ok, UiLayoutRules.CheckOrientation(false, true, float.NaN, float.NaN), "an unmeasured panel decides nothing");
            Assert.IsNotNull(UiLayoutAllowList.Find(UiRule.U6, s => s == "AbilityLab", null), "the landscape-only dev tool");
        }

        // ---- scope, pages, lines --------------------------------------------------------------------------------------

        [Test]
        public void Scope_OnlyTheActiveUncoveredMatchIsHud()
        {
            Assert.AreEqual(UiScope.Menu, UiLayoutRules.ScopeFor(gameScene: false, roundActive: false, coverShown: false));
            Assert.AreEqual(UiScope.Hud, UiLayoutRules.ScopeFor(true, true, false));
            Assert.AreEqual(UiScope.Overlay, UiLayoutRules.ScopeFor(true, true, true), "the arena cover is up");
            Assert.AreEqual(UiScope.Overlay, UiLayoutRules.ScopeFor(true, false, false), "waiting room / results");
        }

        [Test]
        public void MenuPages_Parse()
        {
            var problems = new List<string>();
            CollectionAssert.AreEqual(UiLayoutRules.MenuPages, UiLayoutRules.ParseMenuPages("1", problems), "a bare flag = every page");
            CollectionAssert.AreEqual(UiLayoutRules.MenuPages, UiLayoutRules.ParseMenuPages("all", problems));
            CollectionAssert.AreEqual(new[] { "gear", "main" }, UiLayoutRules.ParseMenuPages("Gear, main,gear", problems), "order kept, de-duplicated, case-insensitive");
            Assert.AreEqual(0, problems.Count);
            CollectionAssert.AreEqual(new[] { "class" }, UiLayoutRules.ParseMenuPages("class,lobby", problems));
            Assert.AreEqual(1, problems.Count, "an unknown id is reported");
            CollectionAssert.DoesNotContain(UiLayoutRules.MenuPages, "host", "THE COOP is never opened as host (it creates a UGS lobby)");
            Assert.AreEqual("menu-class.png", UiLayoutRules.MenuShotFileName("class", null));
            Assert.AreEqual("menu-coop-p540x960.png", UiLayoutRules.MenuShotFileName("coop", "p540x960"));
        }

        [Test]
        public void SummaryLine_StartsWithTheGatedPrefix()
        {
            var perRule = new int[7];
            perRule[2] = 1; perRule[3] = 2;
            string line = UiLayoutRules.Summary(3, UiScope.Hud, null, "periodic", 7, perRule, 1, 3, 412, 776, 1724, R(0, 106, 776, 1580), 1.234f);
            StringAssert.StartsWith("ui offenders=3 scope=hud page=- reason=periodic scan=7 ", line);
            StringAssert.Contains(" U1=0 U2=1 U3=2 U4=0 U5=0 U6=0 allowed=1 ", line);
            StringAssert.Contains(" panel=776x1724 orient=portrait safe=0,106,776x1580 ms=1.23", line);

            string off = UiLayoutRules.OffenderLine(UiRule.U2, UiScope.Hud, null, "TouchControls", "Ability2", R(1, 2, 150, 150),
                "Ability3", R(160, 2, 150, 150), "gap=10.0 (need >= 19)");
            Assert.AreEqual("ui offender U2 scope=hud doc=TouchControls el=Ability2 [1,2,150x150] other=Ability3 [160,2,150x150] gap=10.0 (need >= 19)", off);
            Assert.AreEqual(UiLayoutRules.OffenderKey(UiRule.U3, "a", null), UiLayoutRules.OffenderKey(UiRule.U3, "a", null));
            Assert.AreNotEqual(UiLayoutRules.OffenderKey(UiRule.U3, "a", null), UiLayoutRules.OffenderKey(UiRule.U4, "a", null));
        }

        [Test]
        public void AllowList_EveryEntryHasAReason()
        {
            foreach (var e in UiLayoutAllowList.Entries)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Element), $"{e.Rule}: selector");
                Assert.Greater(e.Reason?.Length ?? 0, 20, $"{e.Rule} {e.Element}: a reason a reviewer can check");
                if (e.MinFontPx > 0f) Assert.AreEqual(UiRule.U4, e.Rule, "a font floor only means something for U4");
            }
            Assert.IsTrue(UiLayoutAllowList.Matches(".cw-x", "Foo", c => c == "cw-x"));
            Assert.IsTrue(UiLayoutAllowList.Matches("Foo", "Foo", null));
            Assert.IsFalse(UiLayoutAllowList.Matches(".Foo", "Foo", null), "a class selector never matches a name");
        }

        // ---- wiring ---------------------------------------------------------------------------------------------------

        [Test]
        public void Probe_IsADevToolsProbe_AndHoldsTheMenuAutoTestDuringMenuShots()
        {
            string installer = Read("Assets/_Game/Scripts/Installers/ProjectInstaller.cs");
            int diag = installer.IndexOf("private void InstallDiagnostics()", System.StringComparison.Ordinal);
            Assert.Greater(diag, 0);
            Assert.Greater(installer.IndexOf("Bind<UiLayoutProbe>()", diag, System.StringComparison.Ordinal), diag, "bound with the other dev-tools probes");
            Assert.Greater(installer.IndexOf("Bind<MenuShotsDriver>()", diag, System.StringComparison.Ordinal), diag);

            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("AutoTestIdle => AutoTestPagesReady && !CluckWars.Diagnostics.MenuShotsDriver.Holding", menu);
            StringAssert.Contains("public bool AutoTestShowPage(string id)", menu);

            string probe = Read("Assets/_Game/Scripts/Diagnostics/UiLayoutProbe.cs");
            StringAssert.DoesNotContain("Debug.Log", probe, "ILogService only");
            StringAssert.Contains("private const string Source = \"UiProbe\";", probe, "merge.ps1 keys on the UiProbe source");

            string cfg = Read("Assets/_Game/Scripts/Diagnostics/AutoTestConfig.cs");
            StringAssert.Contains("case UiProbeArgs.MenuShotsKey: case UiProbeArgs.TouchHudKey:", cfg, "the WP8 keys are not 'unknown key' warnings");
        }
    }
}
