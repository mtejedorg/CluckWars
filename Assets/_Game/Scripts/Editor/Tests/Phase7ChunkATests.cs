using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 7 chunk A (round-4 bugs 5, 6, 9, 12, 15): Android Back vs the HUD layout, the mouse-aim latch, pad B as
    /// back, invariant-culture HUD numbers, and the ability input seams.
    /// </summary>
    public sealed class Phase7ChunkATests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- 5: Android Back is not keyboard activity -----------------------------------------------------

        [Test]
        public void LoneEscape_OnAPhone_IsNotKeyboardActivity()
        {
            Assert.IsFalse(DesktopInputRules.KeyDownIsActivity(true, true, 1, mobilePlatform: true));
        }

        [Test]
        public void Escape_OnDesktop_IsStillActivity()
        {
            Assert.IsTrue(DesktopInputRules.KeyDownIsActivity(true, true, 1, mobilePlatform: false));
        }

        [Test]
        public void RealKeys_OnAPhone_StillCount_EvenWithEscape()
        {
            Assert.IsTrue(DesktopInputRules.KeyDownIsActivity(true, false, 0, mobilePlatform: true), "a plain key");
            Assert.IsTrue(DesktopInputRules.KeyDownIsActivity(true, true, 2, mobilePlatform: true), "Escape + another key");
            Assert.IsFalse(DesktopInputRules.KeyDownIsActivity(false, false, 0, mobilePlatform: true), "nothing down");
        }

        [Test]
        public void KeyboardProvider_RoutesActivityThroughTheRule()
        {
            StringAssert.Contains("DesktopInputRules.KeyDownIsActivity", Read("Assets/_Game/Scripts/Input/KeyboardInputProvider.cs"));
        }

        // ---- 6: mouse aim does not latch ------------------------------------------------------------------

        private const float Never = float.NegativeInfinity;

        [Test]
        public void MouseAim_IsOff_BeforeTheCursorEverMoved()
        {
            Assert.IsFalse(DesktopInputRules.MouseAimActive(10f, Never, Never));
            Assert.IsFalse(DesktopInputRules.MouseAimActive(10f, Never, 9f));
        }

        [Test]
        public void MouseAim_IsLive_ForAFewSecondsAfterTheCursorMoves()
        {
            Assert.IsTrue(DesktopInputRules.MouseAimActive(10f, 9f, 9.5f), "1 s ago, keys in use");
            Assert.IsTrue(DesktopInputRules.MouseAimActive(10.5f, 9f, 10f), "exactly the idle limit");
        }

        [Test]
        public void MouseAim_Drops_WhenTheCursorIsParkedAndKeysAreUsed()
        {
            Assert.IsFalse(DesktopInputRules.MouseAimActive(20f, 9f, 19f));
        }

        [Test]
        public void MouseAim_Returns_WhenTheCursorMovesAgain()
        {
            Assert.IsTrue(DesktopInputRules.MouseAimActive(20f, 19.9f, 19f));
        }

        [Test]
        public void MouseAim_Stays_ForAMouseOnlyPlayerWhoNeverTouchesMovementKeys()
        {
            Assert.IsTrue(DesktopInputRules.MouseAimActive(60f, 9f, Never));
        }

        // ---- 9: pad B is back, Start opens the sheet -------------------------------------------------------

        [Test]
        public void BackThatMayNotOpenTheSheet_StillClosesAndLeaves()
        {
            Assert.AreEqual(MatchBackAction.None, MatchBackRules.Resolve(MatchState.Active, false, false, false, opensSheet: false));
            Assert.AreEqual(MatchBackAction.OpenLeaveSheet, MatchBackRules.Resolve(MatchState.Active, false, false, false, opensSheet: true));
            Assert.AreEqual(MatchBackAction.CloseLeaveSheet, MatchBackRules.Resolve(MatchState.Active, true, false, false, opensSheet: false));
            Assert.AreEqual(MatchBackAction.BackToLobby, MatchBackRules.Resolve(MatchState.Ended, false, false, false, opensSheet: false));
            Assert.AreEqual(MatchBackAction.LeaveWaitingRoom, MatchBackRules.Resolve(MatchState.WaitingForPlayers, false, false, false, opensSheet: false));
        }

        [Test]
        public void PadB_IsBack_AndStartIsThePausePress()
        {
            string src = Read("Assets/_Game/Scripts/Input/GamepadInputProvider.cs");
            var back = Regex.Match(src, @"public bool GetBackPressed\(\)\s*\{(?<b>.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(back.Success);
            StringAssert.Contains("CancelButton", back.Groups["b"].Value, "B backs out of menus / sheet / podium");
            StringAssert.Contains("BackButton", back.Groups["b"].Value, "Start still backs out");
            var pause = Regex.Match(src, @"public bool GetPausePressed\(\)\s*\{(?<b>.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(pause.Success);
            StringAssert.DoesNotContain("CancelButton", pause.Groups["b"].Value, "B never opens the Leave sheet");
            StringAssert.Contains("GetPausePressed()", Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
            StringAssert.Contains("GetPausePressed", Read("Assets/_Game/Scripts/Input/CompositeInputProvider.cs"));
        }

        // ---- 12: numbers are invariant-culture --------------------------------------------------------------

        private sealed class Culture : System.IDisposable
        {
            private readonly CultureInfo _old = CultureInfo.CurrentCulture;
            public Culture(string name) { CultureInfo.CurrentCulture = new CultureInfo(name); }
            public void Dispose() { CultureInfo.CurrentCulture = _old; }
        }

        [Test]
        public void StatusSeconds_UseADot_UnderEsES()
        {
            using (new Culture("es-ES"))
            {
                Assert.AreEqual("1,5", (1.5f).ToString("0.0"), "the test culture really uses a comma");
                Assert.AreEqual("1.5s", HudFeedbackStyle.Seconds(15));
                Assert.AreEqual("0.3s", HudFeedbackStyle.Seconds(3));
            }
        }

        [Test]
        public void StatusBadgeText_UsesADot_AndSlowShowsNoMultiplier_UnderEsES()
        {
            var compose = typeof(ChickenStatusBadges).GetMethod("Compose", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(compose);
            var badgeType = typeof(ChickenStatusBadges).GetNestedType("Badge", BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(badgeType);
            using (new Culture("es-ES"))
            {
                string stun = (string)compose.Invoke(null, new[] { System.Enum.Parse(badgeType, "Stun"), (object)12 });
                string slow = (string)compose.Invoke(null, new[] { System.Enum.Parse(badgeType, "Slow"), (object)1 });
                StringAssert.Contains("1.2s", stun);
                StringAssert.DoesNotContain(",", stun);
                StringAssert.DoesNotContain("\u00d7", slow, "slow is icon only");
                StringAssert.DoesNotContain(",", slow);
            }
        }

        [TestCase("Assets/_Game/Scripts/Input/TouchControlsController.cs")]
        [TestCase("Assets/_Game/Scripts/Visuals/ChickenStatusBadges.cs")]
        [TestCase("Assets/_Game/Scripts/Visuals/HitFeedback.cs")]
        public void PlayerVisibleNumberFormatting_NeverUsesTheCurrentCulture(string path)
        {
            string src = Read(path);
            Assert.IsFalse(Regex.IsMatch(src, "\\.ToString\\(\"0[.#0]*\"\\)"), path + ": ToString(\"0.0\") without InvariantCulture");
            Assert.IsFalse(Regex.IsMatch(src, "\\$\"[^\"\\n]*\\{[A-Za-z_.]+:0\\.[0#]+\\}s"), path + ": interpolated seconds");
        }

        // ---- 15: input seams --------------------------------------------------------------------------------

        [Test]
        public void Fizzle_RestoresTheHeading_AndOnlyARealCastKeepsTheAimTurn()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            StringAssert.Contains("Quaternion headingBeforeAim", src);
            StringAssert.Contains("if (!TryActivate(decision.Slot) && _controller != null) _controller.RestoreFacing(headingBeforeAim);", src);
            StringAssert.Contains("public void RestoreFacing(Quaternion rotation)", Read("Assets/_Game/Scripts/Gameplay/ChickenController.cs"));
        }

        [Test]
        public void CanBeginCharge_AndTheTelegraph_ExcludeARunningSlot()
        {
            var cbc = Regex.Match(Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs"),
                @"private bool CanBeginCharge\(int slot\)\s*\{(?<b>.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(cbc.Success);
            StringAssert.Contains("IsSlotActive(slot)", cbc.Groups["b"].Value);
            StringAssert.Contains("!_abilities.IsSlotActive(i)", Read("Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs"));
        }

        [Test]
        public void PeckChain_PausesItsRepeatWhileAnAimGestureIsLive()
        {
            var fire = Regex.Match(Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs"),
                @"case PeckChainAction\.Fire:(?<b>.*?)TryActivate\(peckSlot\)", RegexOptions.Singleline);
            Assert.IsTrue(fire.Success);
            StringAssert.Contains("if (IsAimGestureLive) break;", fire.Groups["b"].Value);
        }

        [Test]
        public void DeniedAndFizzleBump_AreGatedOnReducedMotion()
        {
            string src = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            var start = Regex.Match(src, @"private void StartBump\(int slot\)\s*\{(?<b>.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(start.Success);
            StringAssert.Contains("ReducedMotionEnabled", start.Groups["b"].Value);
            Assert.IsFalse(Regex.IsMatch(src, @"_bumpTimer\[(denied|fizzled)\]\s*=\s*0f"), "every bump starts through StartBump");
        }
    }
}
