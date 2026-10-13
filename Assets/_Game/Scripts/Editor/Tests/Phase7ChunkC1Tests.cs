using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Settings;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;
using Entry = CluckWars.UI.MatchStandings.Entry;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 7 chunk C1 (round-4 findings 4, 7, 8, 11, 13, 16): rival nameplates, the tie rule, the countdown digit, the
    /// hold hint, the Defense icon contrast and Auto-Peck's touch default.
    /// </summary>
    public sealed class Phase7ChunkC1Tests
    {
        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private static Entry E(int corner, float food, int kos, bool local = false, ChickenClass cls = ChickenClass.Speedy) =>
            new Entry(corner, food, kos, local, !local, cls);

        // ---- 7: tie for 1st --------------------------------------------------------------------------------

        [Test]
        public void TwoWayTie_FoodAndKosLevel_IsTiedAtTheTop_AndBothAreCrowned()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 20, 1), E(1, 20, 1, local: true, cls: ChickenClass.Warrior), E(2, 8, 0), E(3, 8, 0) }, 0);
            Assert.IsTrue(MatchStandings.IsTiedAtTop(r, 0));
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchTiedTop), MatchStandings.WinBanner(r, 0));
            StringAssert.DoesNotContain("WINS", MatchStandings.WinBanner(r, 0));
            StringAssert.DoesNotContain("YOU WIN", MatchStandings.WinBanner(r, 0));
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchTied), MatchStandings.WinSubLine(r, 0));
            CollectionAssert.AreEqual(new[] { true, true, false, false }, MatchStandings.Crowned(r, 0));
        }

        [Test]
        public void TiedAtTheTop_ReadsTheSame_WhicheverBirdGameManagerNamed()
        {
            // GameManager's corner-order pick may be the local bird or a CPU: the screen never names it.
            var all = new[] { E(0, 20, 0), E(1, 20, 0, local: true, cls: ChickenClass.Warrior) };
            foreach (int named in new[] { 0, 1 })
            {
                var r = MatchStandings.Ranked(all, named);
                Assert.AreEqual(UiText.Get(UiKeys.PostmatchTiedTop), MatchStandings.WinBanner(r, named), $"named {named}");
                CollectionAssert.AreEqual(new[] { true, true }, MatchStandings.Crowned(r, named));
            }
        }

        [Test]
        public void TiedAtTheTop_GivesYouAChip_EvenWhenYouAreTheNamedCorner()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 20, 0), E(1, 20, 0, local: true, cls: ChickenClass.Warrior) }, 1);
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchYouPlaceTied, ("you", UiText.Get(UiKeys.LabelYou)), ("place", MatchStandings.Ordinal(1))),
                MatchStandings.YouPlaceChip(r, 1), "the banner no longer says YOU WIN, so the chip says where you stand");
        }

        [Test]
        public void ThreeWayTie_CrownsAllThree_AndNotTheFourth()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 15, 0), E(1, 15, 0), E(2, 15, 0, local: true, cls: ChickenClass.Fatty), E(3, 3, 0) }, 1);
            Assert.IsTrue(MatchStandings.IsTiedAtTop(r, 1));
            CollectionAssert.AreEqual(new[] { true, true, true, false }, MatchStandings.Crowned(r, 1));
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchTiedTop), MatchStandings.WinBanner(r, 1));
        }

        [Test]
        public void SameShownFood_ButADifferentHiddenFraction_StillReadsAsATie()
        {
            // 20.9 vs 20.1 both show 20, and the KO column does not explain it: nothing visible settles it.
            var r = MatchStandings.Ranked(new[] { E(0, 20.9f, 0), E(1, 20.1f, 3, local: true, cls: ChickenClass.Warrior) }, 0);
            Assert.IsTrue(MatchStandings.IsTiedAtTop(r, 0));
            CollectionAssert.AreEqual(new[] { true, true }, MatchStandings.Crowned(r, 0));
        }

        [Test]
        public void KoDecidedTie_KeepsTheWinnersBanner_AndCrownsOnlyThem()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 20, 2), E(1, 20, 0, local: true, cls: ChickenClass.Warrior) }, 0);
            Assert.IsFalse(MatchStandings.IsTiedAtTop(r, 0));
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchWins, ("name", MatchStandings.DisplayName(r[0]).ToUpperInvariant())), MatchStandings.WinBanner(r, 0));
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchTiedKos), MatchStandings.WinSubLine(r, 0));
            CollectionAssert.AreEqual(new[] { true, false }, MatchStandings.Crowned(r, 0));

            // ... and you, winning on KOs, still get YOU WIN!
            var you = MatchStandings.Ranked(new[] { E(0, 20, 0), E(1, 20, 3, local: true, cls: ChickenClass.Warrior) }, 1);
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchYouWin), MatchStandings.WinBanner(you, 1));
        }

        [Test]
        public void ThreeWayTie_WhereKosBeatOnlyOneBird_IsStillATie_BecauseTheOtherTwoAreLevel()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 15, 2), E(1, 15, 2), E(2, 15, 0) }, 0);
            Assert.IsTrue(MatchStandings.IsTiedAtTop(r, 0));
            CollectionAssert.AreEqual(new[] { true, true, true }, MatchStandings.Crowned(r, 0));
        }

        [Test]
        public void ClearWin_AndTheAllZeroDraw_AreUnchanged()
        {
            var clear = MatchStandings.Ranked(new[] { E(0, 30, 0), E(1, 20, 0, local: true, cls: ChickenClass.Warrior) }, 0);
            Assert.IsFalse(MatchStandings.IsTiedAtTop(clear, 0));
            CollectionAssert.AreEqual(new[] { true, false }, MatchStandings.Crowned(clear, 0));
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchWinSub, ("cls", MatchStandings.ClassName(ChickenClass.Speedy))), MatchStandings.WinSubLine(clear, 0));

            var zero = MatchStandings.Ranked(new[] { E(0, 0, 0), E(1, 0, 0, local: true, cls: ChickenClass.Warrior) }, 0);
            Assert.IsFalse(MatchStandings.IsTiedAtTop(zero, 0), "a draw is not a tie at the top");
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchDraw), MatchStandings.WinBanner(zero, 0));
            Assert.IsNull(MatchStandings.WinSubLine(zero, 0));
            CollectionAssert.AreEqual(new[] { false, false }, MatchStandings.Crowned(zero, 0));
        }

        [Test]
        public void NoNamedWinner_CrownsNobody()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 20, 0), E(1, 20, 0) }, -1);
            Assert.IsFalse(MatchStandings.IsTiedAtTop(r, -1));
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchEnded), MatchStandings.WinBanner(r, -1));
            CollectionAssert.AreEqual(new[] { false, false }, MatchStandings.Crowned(r, -1));
        }

        [Test]
        public void TiedAtTheTopHeadline_IsPlayerCopy_InTheDictionary()
        {
            Assert.AreEqual("TIED AT THE TOP!", UiText.Get(UiKeys.PostmatchTiedTop));
            StringAssert.DoesNotContain("1st", UiText.Get(UiKeys.PostmatchTied), "the sub line no longer repeats the headline");
        }

        [Test]
        public void Podium_CrownsEverySlotTheStandingsSay_AndScalesThemTogether()
        {
            var scales = PodiumLayout.ChickenScales(new[] { 200f, 260f, 200f }, new[] { true, true, false });
            Assert.AreEqual(scales[0] * 200f, scales[1] * 260f, 0.01f, "the two crowned birds are drawn the same size");
            Assert.Greater(scales[0] * 200f, scales[2] * 200f, "and larger than the uncrowned bird");
            CollectionAssert.AreEqual(PodiumLayout.ChickenScales(new[] { 200f, 260f, 200f }, 1),
                PodiumLayout.ChickenScales(new[] { 200f, 260f, 200f }, new[] { false, true, false }), "the single-winner overload still agrees");

            string ctl = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("MatchStandings.Crowned(ranked, gm.WinnerCorner)", ctl);
            StringAssert.Contains("PodiumLayout.ChickenScales(sides, _crownSlots)", ctl);
            StringAssert.Contains("cw-me-pod--crowned", ctl);
            StringAssert.Contains(".cw-me-pod--crowned .cw-me-pod__chicken", Read("Assets/UI/Styles/MatchOverlays.uss"));
        }

        [Test]
        public void TheYouChip_SizesOutsideTheNamesMaxWidth_OnTheNarrowPodium()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            var rule = Regex.Match(css, @"\.cw-me-pod__name\.cw-you-mark \{([^}]*)\}");
            Assert.IsTrue(rule.Success, "a chip rule on the podium name");
            StringAssert.Contains("max-width: none;", rule.Groups[1].Value);
            StringAssert.Contains("text-overflow: clip;", rule.Groups[1].Value);
            var plate = Regex.Match(css, @"\.cw-me-pod__plate \{([^}]*)\}").Groups[1].Value;
            StringAssert.Contains("align-self: center;", plate, "the plate can grow past the step for the chip");
            StringAssert.Contains("min-width: 100%;", plate);
        }

        // ---- 4: rival nameplates ---------------------------------------------------------------------------

        [Test]
        public void RivalNameplate_IsCreamOnOpaqueInk_AtLeastAA_OnAnyGround()
        {
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(PlayerPalette.RivalPillText, PlayerPalette.RivalPillFill), AbilityPalette.MinLabelContrast);
            // The pill is what the text sits on, so its fill pixel must be fully opaque: nothing behind it matters.
            var tex = ChickenNameplate.BuildPillTexture(PlayerPalette.RivalPillFill, PlayerPalette.RivalPillRim);
            try
            {
                var middle = tex.GetPixel(tex.width / 2, tex.height / 2);
                Assert.AreEqual(1f, middle.a, 0.001f, "opaque fill");
                Assert.AreEqual(PlayerPalette.RivalPillFill.r, middle.r, 0.01f);
                Assert.AreEqual(PlayerPalette.RivalPillFill.g, middle.g, 0.01f);
                // Worst ground: white, black and every player colour behind a 100% opaque pill make no difference.
                Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(PlayerPalette.RivalPillText, middle), AbilityPalette.MinLabelContrast);
            }
            finally { Object.DestroyImmediate(tex); }
        }

        [Test]
        public void RivalNameplate_CarriesThePlayerColourAsADot_NotAsTheTextColour()
        {
            string plate = Read("Assets/_Game/Scripts/Visuals/ChickenNameplate.cs");
            StringAssert.Contains("_text.color = isLocal ? PlayerPalette.YouMarkInk : PlayerPalette.RivalPillText;", plate);
            StringAssert.Contains("FitMarks(isLocal, PlayerPalette.ForCorner(corner));", plate);
            StringAssert.Contains("_rivalDot.color = dot;", plate);
            StringAssert.DoesNotContain("_text.color = isLocal ? PlayerPalette.YouMarkInk : PlayerPalette.ForCorner", plate);
        }

        [Test]
        public void YouMarkPill_IsStillGoldOnInk_AfterThePillRefactor()
        {
            var tex = ChickenNameplate.BuildPillTexture(PlayerPalette.YouMarkFill, PlayerPalette.YouMarkInk);
            try
            {
                var middle = tex.GetPixel(tex.width / 2, tex.height / 2);
                Assert.AreEqual(PlayerPalette.YouMarkFill.r, middle.r, 0.01f);
                Assert.AreEqual(PlayerPalette.YouMarkFill.g, middle.g, 0.01f);
                Assert.AreEqual(PlayerPalette.YouMarkFill.b, middle.b, 0.01f);
                var edge = tex.GetPixel(tex.width / 2, 1);   // inside the 5 px rim
                Assert.Less(edge.r + edge.g + edge.b, middle.r + middle.g + middle.b, "the rim is the darker ink");
            }
            finally { Object.DestroyImmediate(tex); }
        }

        // ---- 8: the countdown digit leaves the YOU marker uncovered -----------------------------------------------

        [Test]
        public void CountdownDigit_SitsInTheTopThird_AndIsSmallerThanBefore()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            var stack = Regex.Match(css, @"\.cw-intro-stack \{[^}]*top: -(\d+)%;");
            Assert.IsTrue(stack.Success);
            // Digit centre = 50% - top. The top third ends at 33%, so a centre at or above ~17% keeps the digit's lower half out of the
            // 25-45% band the bird's YOU marker floats in.
            Assert.GreaterOrEqual(int.Parse(stack.Groups[1].Value), 30, "digit centre at or above 20% of the screen height");
            var size = Regex.Match(css, @"\.cw-intro-number \{[^}]*font-size: (\d+)px");
            Assert.IsTrue(size.Success);
            Assert.LessOrEqual(int.Parse(size.Groups[1].Value), 260, "was 342 px");
        }

        // ---- 11: the hold hint -----------------------------------------------------------------------------

        [Test]
        public void HoldHint_Copy_UsesTheNewWordingAndMidDots_OnEveryDevice()
        {
            Assert.AreEqual("Hold to see the reach · Let go to use it · Slide to the edge to cancel", UiText.Get(UiKeys.HudHintHoldTouch));
            Assert.AreEqual("Hold to see the reach · Let go to use it · B to cancel", UiText.Get(UiKeys.HudHintHoldPad));
            Assert.AreEqual("Hold to see the reach · Let go to use it · Esc to cancel", UiText.Get(UiKeys.HudHintHoldKeys));
            foreach (var key in new[] { UiKeys.HudHintHoldTouch, UiKeys.HudHintHoldPad, UiKeys.HudHintHoldKeys })
                Assert.AreEqual(2, UiText.Get(key).Count(c => c == '·'), key);
        }

        [Test]
        public void HoldHint_OnTouchIsNarrowAndOverTheCluster_OnDesktopAndPadIsOneLineOverTheStrip()
        {
            string css = Read("Assets/UI/Styles/TouchControls.uss");
            // Touch: bottom 560 px puts it above the top hex (a2's top edge is 511 px up); right-aligned over the cluster, narrower
            // than the cluster's reach to the screen centre.
            var host = Regex.Match(css, @"\.cw-hold-hint-host \{([^}]*)\}").Groups[1].Value;
            StringAssert.Contains("bottom: 560px;", host);
            StringAssert.Contains("align-items: flex-end;", host);
            var plate = Regex.Match(css, @"\.cw-hold-hint \{[^}]*max-width: (\d+)px").Groups[1].Value;
            Assert.LessOrEqual(int.Parse(plate), 700, "touch plate stays over the cluster's side of the screen");
            StringAssert.Contains(".cw-hud--desktop .cw-hold-hint-host, .cw-hud--pad .cw-hold-hint-host", css);
            StringAssert.Contains(".cw-hud--desktop .cw-hold-hint, .cw-hud--pad .cw-hold-hint { max-width: 1400px; }", css);
        }

        // ---- 13: icon contrast on every category fill -------------------------------------------------------------

        [Test]
        public void EveryCategoryFill_CarriesTheCreamIcon_AtLeastThreeToOne()
        {
            foreach (AbilityCategory cat in System.Enum.GetValues(typeof(AbilityCategory)))
            {
                var fill = AbilityPalette.FillColor(cat);
                Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(AbilityPalette.IconCream, fill), AbilityPalette.MinIconContrast,
                    $"{cat}: cream icon on {ColorUtility.ToHtmlStringRGB(fill)}");
            }
        }

        [Test]
        public void DefenseFill_KeepsItsLabelAA_WhileTheIconClearsThree()
        {
            var fill = AbilityPalette.DefenseFill;
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(AbilityPalette.IconCream, fill), 3f, "icon (was 2.74 on #8e9d43)");
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(AbilityPalette.InkOn(fill), fill), AbilityPalette.MinLabelContrast, "ink label");
            Assert.AreEqual("80902c", ColorUtility.ToHtmlStringRGB(fill).ToLowerInvariant());
        }

        // ---- 16: Auto-Peck on by default on touch ------------------------------------------------------------------

        [Test]
        public void AutoPeck_DefaultsOnForTouchDevices_OffOnDesktop()
        {
            Assert.IsTrue(PlayerPreferences.AutoPeckDefaultFor(true));
            Assert.IsFalse(PlayerPreferences.AutoPeckDefaultFor(false));
            Assert.AreEqual(Application.isMobilePlatform, PlayerPreferences.DefaultAutoPeck);
            StringAssert.Contains("PlayerPrefs.GetInt(AutoPeckKey, DefaultAutoPeck ? 1 : 0)", Read("Assets/_Game/Scripts/Settings/PlayerPreferences.cs"));
        }

        [Test]
        public void AutoPeck_ASavedChoiceAlwaysWins_BothWays_AndNoKeyMeansTheDefault()
        {
            bool had = PlayerPrefs.HasKey(PlayerPreferences.AutoPeckKey);
            int saved = PlayerPrefs.GetInt(PlayerPreferences.AutoPeckKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.AutoPeckKey);
                PlayerPreferences.ResetCache();
                Assert.AreEqual(PlayerPreferences.DefaultAutoPeck, PlayerPreferences.AutoPeckEnabled, "nothing saved: the platform default");

                PlayerPreferences.AutoPeckEnabled = false;
                PlayerPreferences.ResetCache();
                Assert.IsFalse(PlayerPreferences.AutoPeckEnabled, "an explicit OFF survives a restart, even where the default is on");

                PlayerPreferences.AutoPeckEnabled = true;
                PlayerPreferences.ResetCache();
                Assert.IsTrue(PlayerPreferences.AutoPeckEnabled, "an explicit ON survives a restart, even on desktop");
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(PlayerPreferences.AutoPeckKey, saved); else PlayerPrefs.DeleteKey(PlayerPreferences.AutoPeckKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }
    }
}
