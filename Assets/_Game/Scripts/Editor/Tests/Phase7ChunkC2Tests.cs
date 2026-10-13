using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 7 chunk C2 (round-4 findings 14, 18, 19): Spine Coat's text, second-person ability copy, one kid word
    /// (STUCK) for root / pin, pops out of C#, the KO plural, "rival" not "enemy", and the small UI leftovers.
    /// </summary>
    public sealed class Phase7ChunkC2Tests
    {
        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        /// <summary>Strips // and /* */ comments so a source scan only sees code.</summary>
        private static string Code(string rel) =>
            Regex.Replace(Read(rel), @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline);

        private static string[] AbilityAndPerkKeys() =>
            UiText.Keys.Where(k => k.StartsWith("ability.") || k.StartsWith("perk.")).OrderBy(k => k).ToArray();

        private static AbilityBaseSO[] AllAbilities() =>
            TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).OrderBy(a => a.name).ToArray();

        private static readonly Regex ThirdPerson =
            new Regex(@"\b(he|his|him|himself|she|her|herself|itself|its|it's)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RootWord =
            new Regex(@"\broot(s|ed|ing)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EnemyWord =
            new Regex(@"\b(enemy|enemies|carriers?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ---- 14: Spine Coat ----------------------------------------------------------------------------------

        [Test]
        public void SpineCoat_TextDescribesProximity_NotBeingHit()
        {
            string t = UiText.Format(UiKeys.AbilitySpineCoatDesc, ("secs", 2), ("n", 4));
            Assert.AreEqual("For 2s, any rival who bumps into you loses 4 food to you and gets knocked away.", t);
            StringAssert.DoesNotContain("hits you", t);
            var a = AllAbilities().First(x => x.name == "SpineCoat");
            Assert.AreEqual(a.DescriptionText, a.Description, "The .asset Description must be the rendered text.");
        }

        // ---- 18: voice and vocabulary --------------------------------------------------------------------------

        [Test]
        public void EveryAbilityAndPerkRow_IsSecondPerson()
        {
            var keys = AbilityAndPerkKeys();
            Assert.Greater(keys.Length, 35, "Expected every ability.* and perk.* row.");
            foreach (var k in keys)
            {
                var m = ThirdPerson.Match(UiText.Get(k));
                Assert.IsFalse(m.Success, $"{k} talks about the player in the third person (\"{m.Value}\"): {UiText.Get(k)}");
            }
        }

        [Test]
        public void EveryAbilityAsset_Description_IsSecondPerson()
        {
            foreach (var a in AllAbilities())
            {
                var m = ThirdPerson.Match(a.Description ?? "");
                Assert.IsFalse(m.Success, $"{a.name}: the asset Description says \"{m.Value}\": {a.Description}");
            }
        }

        [Test]
        public void NoPlayerFacingRow_OrAssetText_SaysEnemyOrCarrier()
        {
            foreach (var k in UiText.Keys)
                Assert.IsFalse(EnemyWord.IsMatch(UiText.Get(k)), $"{k} must say \"rival\": {UiText.Get(k)}");
            foreach (var a in AllAbilities())
                Assert.IsFalse(EnemyWord.IsMatch(a.Description ?? ""), $"{a.name}: the asset Description must say \"rival\": {a.Description}");
        }

        [Test]
        public void RootAndPin_AreOneKidWord_Stuck_EverywhereAPlayerReadsIt()
        {
            foreach (var k in UiText.Keys.Where(k => k.StartsWith("ability.") || k.StartsWith("perk.") || k.StartsWith("hud.")))
                Assert.IsFalse(RootWord.IsMatch(UiText.Get(k)), $"{k} still says root: {UiText.Get(k)}");
            foreach (var a in AllAbilities())
                Assert.IsFalse(RootWord.IsMatch(a.Description ?? ""), $"{a.name}: the asset Description still says root: {a.Description}");

            Assert.AreEqual("STUCK!", UiText.Get(UiKeys.HudPopStuck));
            Assert.AreEqual("STUCK", UiText.Get(UiKeys.HudStatusStuck));
            Assert.AreEqual("STUCK", HudFeedbackStyle.NameFor(HudStatusKind.Root));
            Assert.IsFalse(Regex.IsMatch(UiText.Get(UiKeys.AbilityGroundQuakeDesc), @"\bpins?\b"), "Ground Quake should say stuck, not pinned.");
            Assert.IsEmpty(UiText.Problems, string.Join("\n", UiText.Problems));
        }

        [Test]
        public void RootEgg_IsCalledStickyEgg_OnTheAsset_AndTheTypeIsUntouched()
        {
            var egg = AllAbilities().OfType<RootEggAbilitySO>().Single();
            Assert.AreEqual("Sticky Egg", egg.DisplayName);
            Assert.AreEqual("RootEgg", egg.name, "The asset file keeps its name (decision 8: display name only).");
            StringAssert.Contains("sticky egg", egg.DescriptionText.ToLowerInvariant());
            StringAssert.Contains("stuck", egg.DescriptionText);
            Assert.LessOrEqual(egg.ShortLabel.Length, 4);
        }

        [Test]
        public void PopsAndBars_AreReadFromUiText_NotTypedInCode()
        {
            Assert.AreEqual("STUN", UiText.Get(UiKeys.HudPopStun));
            Assert.AreEqual("SLOW", UiText.Get(UiKeys.HudPopSlow));
            Assert.AreEqual("IMMUNE", UiText.Get(UiKeys.HudPopImmune));
            Assert.AreEqual("FULL!", UiText.Get(UiKeys.HudPopFull));
            Assert.AreEqual("SAFE", UiText.Get(UiKeys.HudPopSafe));

            var literal = new Regex("\"(STUN|ROOT|STUCK!?|SLOW|IMMUNE|FULL!|SAFE)\"");
            foreach (var path in new[]
            {
                "Assets/_Game/Scripts/Visuals/HitFeedback.cs",
                "Assets/_Game/Scripts/Visuals/ChickenWorldBars.cs",
                "Assets/_Game/Scripts/UI/HudFeedbackStyle.cs",
                "Assets/_Game/Scripts/Visuals/ChickenStatusBadges.cs",
                "Assets/_Game/Scripts/Input/TouchControlsController.cs",
            })
            {
                var m = literal.Match(Code(path));
                Assert.IsFalse(m.Success, $"{path} types the player-visible word {m.Value} instead of reading it from UiText.");
            }
        }

        [Test]
        public void Kos_AreSingularOnlyForOne()
        {
            Assert.AreEqual("1 KO", MatchStandings.KoText(1));
            Assert.AreEqual("2 KOs", MatchStandings.KoText(2));
            Assert.AreEqual("0 KOs", MatchStandings.KoText(0));
            Assert.AreEqual("12 KOs", MatchStandings.KoText(12));
        }

        [Test]
        public void ThePostMatchRow_UsesTheKoHelper()
        {
            StringAssert.Contains("MatchStandings.KoText(", Code("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
        }

        // ---- 19: leftovers --------------------------------------------------------------------------------------

        [Test]
        public void LeaveTitle_IsInCaps_LikeEveryOtherHeadline()
        {
            Assert.AreEqual("LEAVE MATCH?", UiText.Get(UiKeys.LeaveTitle));
        }

        [Test]
        public void PlayAgainSubLabel_UsesTheRole_NotThePerk()
        {
            string template = UiText.Get(UiKeys.BtnPlayAgainSub);
            StringAssert.Contains("{role}", template);
            StringAssert.DoesNotContain("{perk}", template);
            Assert.AreEqual(UiText.Format(UiKeys.LobbyClassLine, ("cls", "FATTY"), ("role", "Hauler")),
                UiText.Format(UiKeys.BtnPlayAgainSub, ("cls", "FATTY"), ("role", "Hauler")),
                "The Play Again sub-label reads the same as the Coop's class line.");
            StringAssert.DoesNotContain("Passive", Code("Assets/_Game/Scripts/UI/MenuUiController.cs").Substring(
                Code("Assets/_Game/Scripts/UI/MenuUiController.cs").IndexOf("\"PlayAgainSub\"", System.StringComparison.Ordinal), 220));
        }

        [Test]
        public void FocusRing_IsRounded_AndHiddenAfterAPointerPress()
        {
            foreach (var path in new[] { "Assets/UI/Styles/CluckWarsTheme.uss", "Assets/UI/Styles/MatchOverlays.uss" })
            {
                string uss = Read(path);
                var ring = Regex.Match(uss, @"\.cw-btn:focus[^{]*\{([^}]*)\}");
                Assert.IsTrue(ring.Success, $"{path}: no focus ring rule.");
                StringAssert.Contains("border-top-left-radius", ring.Groups[1].Value, $"{path}: the focus ring must be rounded.");
                StringAssert.Contains(".cw-no-focus-ring .cw-btn:focus", uss, $"{path}: no pointer-mode override for the ring.");
            }
        }

        [Test]
        public void NavFocusRing_SetsAndClearsThePointerClass()
        {
            var root = new VisualElement();
            NavFocusRing.SetPointerMode(root, true);
            Assert.IsTrue(root.ClassListContains(NavFocusRing.NoRingClass));
            NavFocusRing.SetPointerMode(root, false);
            Assert.IsFalse(root.ClassListContains(NavFocusRing.NoRingClass));
            StringAssert.Contains("NavFocusRing.Attach(", Code("Assets/_Game/Scripts/UI/MenuUiController.cs"));
            StringAssert.Contains("NavFocusRing.Attach(", Code("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
        }

        [Test]
        public void SettingsScrollbar_IsThemed_ThinInkTrack_GoldThumb_NoArrows()
        {
            string uss = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            StringAssert.Contains(".cw-sheet__body .unity-scroller__low-button", uss);
            Assert.IsTrue(Regex.IsMatch(uss, @"\.cw-sheet__body \.unity-scroller__low-button[^{]*\{[^}]*display:\s*none"), "No arrow buttons.");
            var thumb = Regex.Match(uss, @"\.cw-sheet__body \.unity-scroller--vertical \.unity-base-slider__dragger\s*\{([^}]*)\}");
            Assert.IsTrue(thumb.Success, "No dragger rule.");
            StringAssert.Contains("rgb(212, 160, 32)", thumb.Groups[1].Value, "Gold thumb.");
            var track = Regex.Match(uss, @"\.cw-sheet__body \.unity-scroller--vertical \.unity-base-slider__tracker\s*\{([^}]*)\}");
            Assert.IsTrue(track.Success, "No tracker rule.");
            StringAssert.Contains("rgba(42, 26, 12", track.Groups[1].Value, "Ink track.");
        }

        [Test]
        public void InviteCard_KeepsSixteenPixelsOfPadding_OnNarrowLayouts()
        {
            foreach (var path in new[] { "Assets/UI/Styles/CluckWarsTheme.uss", "Assets/UI/Styles/MatchOverlays.uss" })
            {
                string uss = Read(path);
                Assert.IsTrue(Regex.IsMatch(uss,
                    @"layout--narrow \.cw-coop-left \.cw-lobby-card\s*\{[^}]*padding-left:\s*16px[^}]*padding-right:\s*16px"),
                    $"{path}: the narrow invite card must pad 16 px left and right.");
            }
        }
    }
}
