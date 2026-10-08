using System.IO;
using System.Text.RegularExpressions;
using CluckWars.UI;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Menu overhaul Phase 4 (re-audit fixes, workstream A): Esc / Android back routing, the join-code
    /// gate, and the markup / source rules the fixes rely on.
    /// </summary>
    public sealed class MenuPhase4Tests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), rel));

        private const string ControllerPath = "Assets/_Game/Scripts/UI/MenuUiController.cs";

        // ---- Esc / Android back (item 20) ---------------------------------------------------------

        [Test]
        public void Back_FollowsEachPagesBackButton_AndDoesNothingOnTheMainMenu()
        {
            Assert.AreEqual(MenuBackStep.None, MenuUiController.MenuBackTarget(0), "main menu: no quit");
            Assert.AreEqual(MenuBackStep.MainMenu, MenuUiController.MenuBackTarget(1), "PICK YOUR BIRD -> HOME");
            Assert.AreEqual(MenuBackStep.ClassSelect, MenuUiController.MenuBackTarget(2), "GEAR UP -> BACK");
            Assert.AreEqual(MenuBackStep.Loadout, MenuUiController.MenuBackTarget(3), "THE COOP -> BACK");
            Assert.AreEqual(MenuBackStep.None, MenuUiController.MenuBackTarget(-1), "no page");
        }

        [Test]
        public void MenuReadsBackThroughIInputProvider_NeverTheDeviceDirectly()
        {
            string src = Read(ControllerPath);
            StringAssert.Contains("GetBackPressed()", src);
            StringAssert.DoesNotContain("Keyboard.current", src);
            StringAssert.DoesNotContain("UnityEngine.Input.", src);
            StringAssert.DoesNotContain("GetAbilityCancelPressed", src, "the menu must not consume the in-match cancel");
        }

        // ---- Join gate (item 9) -------------------------------------------------------------------

        [Test]
        public void JoinCode_IsCompleteAtSixCharacters_Trimmed()
        {
            Assert.IsFalse(JoinCodeEntry.IsComplete(null));
            Assert.IsFalse(JoinCodeEntry.IsComplete(""));
            Assert.IsFalse(JoinCodeEntry.IsComplete("   "));
            Assert.IsFalse(JoinCodeEntry.IsComplete("ABC12"));
            Assert.IsFalse(JoinCodeEntry.IsComplete("  ABC12  "));
            Assert.IsTrue(JoinCodeEntry.IsComplete("ABC123"), "a UGS lobby code");
            Assert.IsTrue(JoinCodeEntry.IsComplete("cluck-lan"), "the offline host's session name");
        }

        [Test]
        public void JoinCard_SitsInTheCentreOfTheBar_AndNoStrayDashKeyRemains()
        {
            string xml = Read("Assets/UI/Lobby.uxml");
            int center = xml.IndexOf("class=\"cw-coop-center\"");
            int join = xml.IndexOf("name=\"JoinCard\"");
            int right = xml.IndexOf("class=\"cw-coop-right\"");
            Assert.Greater(center, 0);
            Assert.Greater(join, center, "join card inside the centre column");
            Assert.Less(join, right);
            StringAssert.DoesNotContain("lobby.count.unknown", Read("Assets/_Game/Resources/Text/UiText.csv"));
        }

        // ---- Markup the fixes depend on -----------------------------------------------------------

        [Test]
        public void EveryClassTile_HasTheRingAndTheCheckBadge()
        {
            string xml = Read("Assets/UI/CharacterSelectClass.uxml");
            // Count class attributes, not mentions (the header comment names the classes too).
            Assert.AreEqual(4, Regex.Matches(xml, "class=\"cw-class-tile__ring\"").Count);
            Assert.AreEqual(4, Regex.Matches(xml, "class=\"cw-class-tile__check-mark\"").Count);
        }

        [Test]
        public void GearUp_HasTheDoorwayBird_InItsOwnSlotBetweenTheSlotsAndTheDeck()
        {
            // Round-2 finding 7: the bird used to sit under the body, so the deck hid all but its
            // forehead on phone / tablet. It now owns the doorway slot between the slot row and the deck.
            string xml = Read("Assets/UI/CharacterSelect.uxml");
            int slots = xml.IndexOf("name=\"SlotBoxRow\"");
            int doorway = xml.IndexOf("name=\"GearDoorway\"");
            int bird = xml.IndexOf("name=\"GearChicken\"");
            int deck = xml.IndexOf("name=\"Deck\"");
            Assert.Greater(slots, 0);
            Assert.Greater(doorway, slots, "doorway after the slot row");
            Assert.Greater(bird, doorway, "bird inside the doorway");
            Assert.Less(bird, deck, "doorway before the deck");
        }

        [Test]
        public void PlayAgain_SitsAbovePlaySolo()
        {
            string xml = Read("Assets/UI/MainMenu.uxml");
            Assert.Less(xml.IndexOf("name=\"PlayAgainRow\""), xml.IndexOf("name=\"SoloBtn\""));
        }

        [Test]
        public void ReadyBanner_IsAStatus_NotAPlank()
        {
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            var m = Regex.Match(css, @"\.cw-ready-banner\s*\{([^}]*)\}");
            Assert.IsTrue(m.Success);
            StringAssert.DoesNotContain("background-image", m.Groups[1].Value);
        }

        [Test]
        public void MenuAbilityColours_ComeFromThePalette_NotTheVfxAccent()
        {
            StringAssert.DoesNotContain("AccentColor", Read(ControllerPath),
                "AccentColor is the in-match VFX colour; the menu colours abilities by category (AbilityPalette)");
        }
    }
}
