using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 7d: match overlays on the wood style, Coop seats by corner, waiting-room seats, alignment and the
    /// 28 px text floor (round-3 findings 5, 6, 12, 16, 19).
    /// </summary>
    public sealed class Phase6Chunk7dTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Finding 19: the 28 px floor -----------------------------------------------------------------------

        [Test]
        public void EveryUiStylesheet_HasNoFontSizeBelow28px()
        {
            var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets/UI/Styles");
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(dir, "*.uss"))
            {
                // Strip comments so a "font-size: 22px" in prose never counts.
                string css = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                foreach (Match m in Regex.Matches(css, @"font-size:\s*([0-9.]+)px"))
                    if (float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) < 28f)
                        offenders.Add(Path.GetFileName(file) + ": " + m.Value);
            }
            CollectionAssert.IsEmpty(offenders, "Menu / overlay text never goes below 28 px (round-3 finding 19).");
        }

        [Test]
        public void GoldOnWoodSectionLabels_CarryAnInkOutline()
        {
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            var m = Regex.Match(css, @"\.cw-deck-group__title\s*\{\s*-unity-text-outline-width:\s*(\d+)px");
            Assert.IsTrue(m.Success, "ANY BIRD / class-group titles need their own outline rule.");
            Assert.GreaterOrEqual(int.Parse(m.Groups[1].Value), 4);
        }

        [Test]
        public void MiniHexMarks_AreBiggerThanTheirOldSeventyPercent()
        {
            StringAssert.Contains(".cw-mini-hex .cw-cat-mark { scale: 1 1;", Read("Assets/UI/Styles/CategoryMark.uss"));
        }

        // ---- Finding 6: Coop seats by corner --------------------------------------------------------------------

        [Test]
        public void LobbySeatOrder_SortsSeatsByCorner()
        {
            // Seat 0 (you) is on corner 3, seats 1, 2, 3 on corners 0, 1, 2: the row reads P1..P4.
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 0 }, CornerAssignment.LobbySeatOrder(new[] { 3, 0, 1, 2 }));
        }

        [Test]
        public void LobbySeatOrder_IsSeatOrderWhenCornersAreUnknownOrNotADistinctSet()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, CornerAssignment.LobbySeatOrder(new[] { -1, -1, -1, -1 }), "a guest");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, CornerAssignment.LobbySeatOrder(new[] { 2, 2, 1, 0 }), "a repeated corner");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, CornerAssignment.LobbySeatOrder(new[] { 0, 1, 2, 3 }));
            CollectionAssert.IsEmpty(CornerAssignment.LobbySeatOrder(null));
        }

        [Test]
        public void LobbySeatOrder_EveryPermutationReadsP1ToP4()
        {
            foreach (var perm in new[] { new[] { 0, 1, 2, 3 }, new[] { 1, 3, 2, 0 }, new[] { 3, 2, 1, 0 }, new[] { 2, 0, 3, 1 } })
            {
                var order = CornerAssignment.LobbySeatOrder(perm);
                for (int pos = 0; pos < 4; pos++) Assert.AreEqual(pos, perm[order[pos]]);
            }
        }

        [Test]
        public void MenuController_ReordersTheCoopRowAfterEveryLineupFill()
        {
            string src = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("CornerAssignment.LobbySeatOrder(", src);
            StringAssert.Contains("OrderSeatsByCorner(mode);", src);
        }

        // ---- Finding 12: waiting room ---------------------------------------------------------------------------

        [Test]
        public void InviteLabel_IsJustInviteCode()
        {
            Assert.AreEqual("INVITE CODE", UiText.Get(UiKeys.LobbyInviteLabel));
        }

        [Test]
        public void WaitingRoomSeats_CarryTheLoadoutHexes_AndAnEmptyPedestalRim()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("cw-seat__hexes", src);
            StringAssert.Contains("AbilityIconPainter.Paint(", src);
            StringAssert.Contains("new DashedRim()", src);
            StringAssert.Contains("new DashedRim()", Read("Assets/_Game/Scripts/UI/MenuUiController.cs"));
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            StringAssert.Contains(".cw-seat--empty .cw-seat__pedestal { opacity: 1; }", css);
            StringAssert.DoesNotContain(".cw-seat--empty .cw-seat__pedestal { opacity: 0.45; }", css);
            StringAssert.Contains("CategoryMark.uss", Read("Assets/UI/MatchOverlays.uxml"), "the waiting room needs the category marks' sheet");
        }

        [Test]
        public void DashedRim_DashesAreEvenlySpacedWithGaps()
        {
            var (a0, a1) = DashedRim.DashAngles(0, DashedRim.DashCount, DashedRim.DashFill);
            var (b0, _) = DashedRim.DashAngles(1, DashedRim.DashCount, DashedRim.DashFill);
            Assert.AreEqual(0f, a0, 1e-5f);
            Assert.Less(a1, b0, "a gap between dash 0 and dash 1");
            Assert.AreEqual(Mathf.PI * 2f / DashedRim.DashCount, b0, 1e-5f);
            Assert.AreEqual(0, DashedRim.DashCount % 2);
        }

        [Test]
        public void WaitingRoomHint_IsOnAnInkPlate()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            Assert.IsTrue(Regex.IsMatch(css, @"\.cw-wr-hint\s*\{[^}]*background-color:\s*rgba\(42, 26, 12, 0\.9\)"));
        }

        // ---- Finding 16: alignment ------------------------------------------------------------------------------

        [Test]
        public void BottomBar_HasOneFixedHeight_AndCaptionsLeaveTheFlow()
        {
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            StringAssert.Contains("flex-shrink: 0; min-height: 206px;", css);
            StringAssert.Contains("position: absolute; left: 0; right: 0; top: 100%; margin-top: 4px; min-height: 38px;", css);
            StringAssert.Contains(".layout--narrow .cw-deck { align-items: flex-start; }", css);
            StringAssert.Contains(".cw-starter-chip__icon > .cw-cat-mark", css);
        }

        // ---- Finding 5: overlays on the wood style --------------------------------------------------------------

        [Test]
        public void SessionEnd_IsAWoodPanelWithABackToTheBarnButton()
        {
            string uxml = Read("Assets/UI/MatchOverlays.uxml");
            StringAssert.Contains("name=\"SessionEndBackBtn\"", uxml);
            StringAssert.Contains("class=\"cw-wood-panel cw-se-panel\"", uxml);
            Assert.AreEqual("BACK TO THE BARN", UiText.Get(UiKeys.BtnBackToBarn));
            StringAssert.Contains("remaining <= 0f || _sessionEndLeaveNow",
                Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"), "the button leaves through the countdown's own path");
        }

        [Test]
        public void GetReadyPlate_IsTheWoodRibbon()
        {
            string css = Read("Assets/UI/Styles/GetReadyCard.uss");
            StringAssert.Contains("Frame_Ribbon.png", css);
            StringAssert.DoesNotContain("border-top-color: rgb(245, 200, 66)", css, "the gold-ruled ink plate is gone");
        }

        [Test]
        public void EventBanner_IsAWoodPlaqueInTheOverlayDocument_FedByMatchHud()
        {
            Assert.IsFalse(EventBannerFeed.Visible, "hidden until the HUD shows it");
            EventBannerFeed.Show("FINAL 10!", "PILES RESTOCKED!");
            Assert.IsTrue(EventBannerFeed.Visible);
            Assert.AreEqual("FINAL 10!", EventBannerFeed.Header);
            Assert.AreEqual("PILES RESTOCKED!", EventBannerFeed.Text);
            EventBannerFeed.Hide();
            Assert.IsFalse(EventBannerFeed.Visible);

            StringAssert.Contains("name=\"EventBanner\"", Read("Assets/UI/MatchOverlays.uxml"));
            string plaque = Regex.Match(Read("Assets/UI/Styles/MatchOverlays.uss"), @"\.cw-eventbanner__plaque\s*\{[^}]*\}").Value;
            StringAssert.Contains("Frame_WoodPanel.png", plaque);
            string hud = Read("Assets/_Game/Scripts/UI/MatchHud.cs");
            StringAssert.DoesNotContain("BuildEventBanner", hud, "no procedural UGUI plate any more");
            StringAssert.Contains("EventBannerFeed.Show(", hud);
        }
    }
}
