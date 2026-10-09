using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using CluckWars.EditorTools;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Services;
using CluckWars.Settings;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 6: haptics (A9), the desktop / controller HUD and its device switch (A10), the first-time hold
    /// hint (A1), the thumb-reach cluster and the compact phone leaderboard (A11), and the settings rows. Pure rules
    /// plus guards on the USS / UXML / source that wires them.
    /// </summary>
    public sealed class Phase6Chunk6Tests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Haptics: patterns ----------------------------------------------------------------------------------

        [Test]
        public void Haptics_HitIs40msAtOrBelow180()
        {
            var p = HapticRules.PatternFor(HapticKind.Hit);
            Assert.IsTrue(p.IsOneShot);
            Assert.AreEqual(40, p.TimingsMs[1]);
            Assert.LessOrEqual(p.Amplitudes[1], 180, "The brief caps a hit buzz at amplitude 180.");
            Assert.Greater(p.Amplitudes[1], 0);
        }

        [Test]
        public void Haptics_StunAndDeathShareTheDoubleBuzz_60On50Off90On()
        {
            var p = HapticRules.PatternFor(HapticKind.Stun);
            Assert.IsFalse(p.IsOneShot);
            CollectionAssert.AreEqual(new long[] { 0, 60, 50, 90 }, p.TimingsMs);
            Assert.AreEqual(0, p.Amplitudes[2], "The gap is silent.");
            Assert.Greater(p.Amplitudes[1], 0);
            Assert.Greater(p.Amplitudes[3], 0);
            Assert.AreEqual(p.TimingsMs.Length, p.Amplitudes.Length, "One amplitude per segment.");
        }

        [Test]
        public void Haptics_CancelArmTickIs15msAndLighterThanAHit()
        {
            var tick = HapticRules.PatternFor(HapticKind.CancelArm);
            Assert.AreEqual(15, tick.TimingsMs[1]);
            Assert.Less(tick.Amplitudes[1], HapticRules.PatternFor(HapticKind.Hit).Amplitudes[1]);
        }

        // ---- Haptics: rate limit --------------------------------------------------------------------------------

        [Test]
        public void Haptics_MinimumGapIs200ms()
        {
            Assert.AreEqual(0.2f, HapticRules.MinGapSeconds);
            var gate = new HapticLimiter();
            Assert.IsTrue(gate.TryAcquire(10f, HapticKind.Hit), "The first buzz always plays.");
            Assert.IsFalse(gate.TryAcquire(10.05f, HapticKind.Hit));
            Assert.IsFalse(gate.TryAcquire(10.19f, HapticKind.Hit));
            Assert.IsTrue(gate.TryAcquire(10.21f, HapticKind.Hit), "Past 200 ms is allowed.");
            Assert.IsFalse(gate.TryAcquire(10.3f, HapticKind.Hit), "The window restarts from the buzz that played.");
        }

        [Test]
        public void Haptics_ASuppressedBuzzDoesNotExtendTheWindow()
        {
            var gate = new HapticLimiter();
            gate.TryAcquire(0f, HapticKind.Hit);
            gate.TryAcquire(0.15f, HapticKind.Hit);   // refused
            Assert.IsTrue(gate.TryAcquire(0.21f, HapticKind.Hit), "Measured from the buzz that played, not the refused one.");
        }

        [Test]
        public void Haptics_AStrongerBuzzMayInterruptInsideTheWindow_AWeakerMayNot()
        {
            // A hit and the stun it caused land on the same frame: the stun must not be swallowed.
            var gate = new HapticLimiter();
            Assert.IsTrue(gate.TryAcquire(5f, HapticKind.Hit));
            Assert.IsTrue(gate.TryAcquire(5f, HapticKind.Stun), "Stun escalates over the hit buzz.");
            Assert.IsFalse(gate.TryAcquire(5.05f, HapticKind.Hit), "A hit never interrupts the stun buzz.");
            Assert.IsFalse(gate.TryAcquire(5.05f, HapticKind.CancelArm));

            var tick = new HapticLimiter();
            tick.TryAcquire(1f, HapticKind.CancelArm);
            Assert.IsTrue(tick.TryAcquire(1.05f, HapticKind.Hit), "A hit beats the light cancel tick.");
        }

        private sealed class FakeDevice : IHapticDevice
        {
            public bool Can = true;
            public readonly List<HapticPattern> Played = new();
            public bool CanBuzz => Can;
            public void Play(HapticPattern pattern) => Played.Add(pattern);
        }

        [Test]
        public void HapticsService_PreferenceOffPlaysNothing_AndDoesNotSpendTheWindow()
        {
            var dev = new FakeDevice();
            bool on = false;
            float now = 0f;
            var svc = new HapticsService(dev, () => on, () => now);

            svc.Buzz(HapticKind.Hit);
            Assert.IsEmpty(dev.Played, "Buzz When Hit off: silence.");

            on = true;
            svc.Buzz(HapticKind.Hit);
            Assert.AreEqual(1, dev.Played.Count, "Switching it on must not be blocked by the muted attempt.");
        }

        [Test]
        public void HapticsService_NoVibratorPlaysNothing_AndPicksThePatternByKind()
        {
            var dev = new FakeDevice { Can = false };
            float now = 0f;
            var svc = new HapticsService(dev, () => true, () => now);
            svc.Buzz(HapticKind.Hit);
            Assert.IsEmpty(dev.Played);

            dev.Can = true;
            svc.Buzz(HapticKind.Hit);
            now = 1f;
            svc.Buzz(HapticKind.Stun);
            now = 2f;
            svc.Buzz(HapticKind.CancelArm);
            Assert.AreSame(HapticRules.Hit, dev.Played[0]);
            Assert.AreSame(HapticRules.Stun, dev.Played[1]);
            Assert.AreSame(HapticRules.CancelArm, dev.Played[2]);
        }

        [Test]
        public void BuzzWhenHit_DefaultsOn_AndIsIndependentOfReducedMotion()
        {
            bool hadBuzz = PlayerPrefs.HasKey(PlayerPreferences.BuzzWhenHitKey);
            int savedBuzz = PlayerPrefs.GetInt(PlayerPreferences.BuzzWhenHitKey, 1);
            bool hadReduced = PlayerPrefs.HasKey(PlayerPreferences.ReducedMotionKey);
            int savedReduced = PlayerPrefs.GetInt(PlayerPreferences.ReducedMotionKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.BuzzWhenHitKey);
                PlayerPrefs.DeleteKey(PlayerPreferences.ReducedMotionKey);
                PlayerPreferences.ResetCache();
                Assert.IsTrue(PlayerPreferences.BuzzWhenHitEnabled, "Defaults to ON.");

                PlayerPreferences.ReducedMotionEnabled = true;
                Assert.IsTrue(PlayerPreferences.BuzzWhenHitEnabled, "Reduced Motion does not touch the buzz.");
                PlayerPreferences.BuzzWhenHitEnabled = false;
                Assert.IsTrue(PlayerPreferences.ReducedMotionEnabled, "...and the buzz does not touch Reduced Motion.");

                PlayerPreferences.ResetCache();
                Assert.IsFalse(PlayerPreferences.BuzzWhenHitEnabled, "Persisted.");
            }
            finally
            {
                if (hadBuzz) PlayerPrefs.SetInt(PlayerPreferences.BuzzWhenHitKey, savedBuzz); else PlayerPrefs.DeleteKey(PlayerPreferences.BuzzWhenHitKey);
                if (hadReduced) PlayerPrefs.SetInt(PlayerPreferences.ReducedMotionKey, savedReduced); else PlayerPrefs.DeleteKey(PlayerPreferences.ReducedMotionKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        [Test]
        public void Haptics_AreWiredLocallyAndOnlyForTheVictimsOwnPeer()
        {
            string hit = Read("Assets/_Game/Scripts/Visuals/HitFeedback.cs");
            // Same local gate as the camera shake, and the stun / death kinds on their own edges.
            StringAssert.Contains("_haptics?.Buzz(haptic)", hit);
            StringAssert.Contains("TriggerVictimHit(stunEntered ? HapticKind.Stun : HapticKind.Hit)", hit);
            StringAssert.Contains("_haptics?.Buzz(HapticKind.Stun)", hit, "A knock-out buzzes like a stun.");
            Assert.IsFalse(Regex.IsMatch(hit, @"Rpc[^\n]*Buzz|Buzz[^\n]*Rpc"), "Haptics are local, never an RPC.");

            string touch = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            StringAssert.Contains("_haptics?.Buzz(HapticKind.CancelArm)", touch, "The edge-band arm plays the cancel tick.");

            string installer = Read("Assets/_Game/Scripts/Installers/ProjectInstaller.cs");
            StringAssert.Contains("Bind<IHapticsService>", installer);
            StringAssert.Contains("RuntimePlatform.Android", installer);
            StringAssert.Contains("NullHapticsService", installer, "A null implementation backs desktop and the Editor.");

            string android = Read("Assets/_Game/Scripts/Services/AndroidHapticDevice.cs");
            StringAssert.Contains("createOneShot", android);
            StringAssert.Contains("createWaveform", android);
            StringAssert.DoesNotContain("Handheld.Vibrate", android.Replace("NOT <c>Handheld.Vibrate</c>", ""));
            StringAssert.Contains("haptic_feedback_enabled", android, "Respects the system haptic setting.");
        }

        // ---- VIBRATE permission --------------------------------------------------------------------------------

        private const string Manifest =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.unity3d.player\">\n" +
            "  <application />\n" +
            "</manifest>\n";

        [Test]
        public void VibratePermission_IsAddedOnce()
        {
            string patched = AndroidVibratePermission.EnsurePermission(Manifest);
            var doc = new XmlDocument();
            doc.LoadXml(patched);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("android", "http://schemas.android.com/apk/res/android");
            Assert.AreEqual(1, doc.SelectNodes("/manifest/uses-permission[@android:name='android.permission.VIBRATE']", ns).Count);

            StringAssert.Contains("encoding=\"utf-8\"", patched, "The file is written back as UTF-8: the declaration must say so.");
            StringAssert.DoesNotContain("utf-16", patched);

            string again = AndroidVibratePermission.EnsurePermission(patched);
            Assert.AreEqual(patched, again, "Idempotent: a second pass leaves the manifest alone.");
        }

        [Test]
        public void VibratePermission_AMissingOrBrokenManifestFailsLoudly()
        {
            Assert.Throws<XmlException>(() => AndroidVibratePermission.EnsurePermission("<notmanifest/>"));
            Assert.Throws<XmlException>(() => AndroidVibratePermission.EnsurePermission("not xml at all"));
        }

        // ---- Device mode ----------------------------------------------------------------------------------------

        [Test]
        public void HudMode_FollowsTheLastUsedDevice_NoneUsesThePlatformDefault()
        {
            Assert.AreEqual(HudDeviceMode.Touch, HudLayoutRules.FromDevice(InputDeviceKind.Touch, false));
            Assert.AreEqual(HudDeviceMode.KeyboardMouse, HudLayoutRules.FromDevice(InputDeviceKind.KeyboardMouse, true));
            Assert.AreEqual(HudDeviceMode.Gamepad, HudLayoutRules.FromDevice(InputDeviceKind.Gamepad, true));
            Assert.AreEqual(HudDeviceMode.Touch, HudLayoutRules.FromDevice(InputDeviceKind.None, true), "A phone starts on touch.");
            Assert.AreEqual(HudDeviceMode.KeyboardMouse, HudLayoutRules.FromDevice(InputDeviceKind.None, false), "A PC starts on keyboard / mouse.");
        }

        private sealed class FakeInput : IInputProvider
        {
            public InputDeviceKind Kind;
            public float Active = float.NegativeInfinity;
            public Vector2 GetMovement() => Vector2.zero;
            public bool GetAbility1Pressed() => false;
            public bool GetAbility2Pressed() => false;
            public bool GetAbility3Pressed() => false;
            public bool GetAbility4Pressed() => false;
            public bool GetAbilityHeld(int slot) => false;
            public bool GetAbilityCancelPressed() => false;
            public bool GetBackPressed() => false;
            public bool IsAbilityCancelArmed() => false;
            public InputDeviceKind Device => Kind;
            public float LastActiveTime => Active;
        }

        private static HudDeviceMode ModeOf(params FakeInput[] providers) =>
            HudLayoutRules.FromDevice(new CompositeInputProvider(providers).Device, isMobilePlatform: false);

        [Test]
        public void HudMode_LastUsedWins_AndTouchRestoresTheThumbLayout()
        {
            var kb = new FakeInput { Kind = InputDeviceKind.KeyboardMouse };
            var touch = new FakeInput { Kind = InputDeviceKind.Touch };
            var pad = new FakeInput { Kind = InputDeviceKind.Gamepad };

            kb.Active = 1f;
            Assert.AreEqual(HudDeviceMode.KeyboardMouse, ModeOf(kb, touch, pad));

            pad.Active = 2f;
            Assert.AreEqual(HudDeviceMode.Gamepad, ModeOf(kb, touch, pad), "The pad was used last.");

            touch.Active = 3f;
            Assert.AreEqual(HudDeviceMode.Touch, ModeOf(kb, touch, pad), "Any touch brings the touch layout back.");

            kb.Active = 4f;
            Assert.AreEqual(HudDeviceMode.KeyboardMouse, ModeOf(kb, touch, pad), "And a key takes it away again.");
        }

        [Test]
        public void HudMode_ATouchscreenLaptopTouchBeatsTheMouseEventItRaises()
        {
            // Same frame: the touch and the mouse event Windows promotes from it.
            var kb = new FakeInput { Kind = InputDeviceKind.KeyboardMouse, Active = 7f };
            var touch = new FakeInput { Kind = InputDeviceKind.Touch, Active = 7f };
            Assert.AreEqual(HudDeviceMode.Touch, ModeOf(kb, touch));
            Assert.AreEqual(HudDeviceMode.Touch, ModeOf(touch, kb), "Provider order does not matter.");
        }

        [Test]
        public void HudMode_ClassesPickingAndScale()
        {
            Assert.IsNull(HudLayoutRules.RootClass(HudDeviceMode.Touch));
            Assert.AreEqual("cw-hud--desktop", HudLayoutRules.RootClass(HudDeviceMode.KeyboardMouse));
            Assert.AreEqual("cw-hud--pad", HudLayoutRules.RootClass(HudDeviceMode.Gamepad));

            Assert.IsTrue(HudLayoutRules.HexesAreInteractive(HudDeviceMode.Touch));
            Assert.IsFalse(HudLayoutRules.HexesAreInteractive(HudDeviceMode.KeyboardMouse), "Information only.");
            Assert.IsFalse(HudLayoutRules.HexesAreInteractive(HudDeviceMode.Gamepad));

            Assert.AreEqual(1f, HudLayoutRules.HexRestScale(HudDeviceMode.Touch));
            Assert.AreEqual(0.75f, HudLayoutRules.HexRestScale(HudDeviceMode.KeyboardMouse));
            Assert.AreEqual(0.75f, HudLayoutRules.HexRestScale(HudDeviceMode.Gamepad));
        }

        [Test]
        public void HudMode_WiringIsInTheControllerAndTheInputLayer()
        {
            string touch = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            StringAssert.Contains("_input.Device", touch, "The layout reads the composite's last-used device.");
            StringAssert.Contains("PickingMode.Ignore", touch, "Desktop / pad hexes take no pointer.");
            Assert.IsFalse(Regex.IsMatch(touch, @"Keyboard\.current|Gamepad\.current|Mouse\.current"),
                "Device reads stay in the input providers.");

            string touchProvider = Read("Assets/_Game/Scripts/Input/TouchInputProvider.cs");
            StringAssert.Contains("Touchscreen.current", touchProvider, "Any finger counts as touch activity, even off the controls.");
        }

        // ---- Badges -----------------------------------------------------------------------------------------------

        [Test]
        public void Badges_LabelTableByDevice()
        {
            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" }, Labels(HudDeviceMode.Touch));
            CollectionAssert.AreEqual(new[] { "Q", "E", "R", "F" }, Labels(HudDeviceMode.KeyboardMouse));
            CollectionAssert.AreEqual(new[] { "RB", "RT", "LB", "LT" }, Labels(HudDeviceMode.Gamepad));
            Assert.AreEqual(string.Empty, HudLayoutRules.BadgeLabel(HudDeviceMode.Touch, 4));
            Assert.AreEqual(string.Empty, HudLayoutRules.BadgeLabel(HudDeviceMode.Touch, -1));
        }

        private static string[] Labels(HudDeviceMode mode)
        {
            var a = new string[4];
            for (int i = 0; i < 4; i++) a[i] = HudLayoutRules.BadgeLabel(mode, i);
            return a;
        }

        [Test]
        public void Badges_AreDerivedFromTheRealBindingTables()
        {
            for (int slot = 0; slot < 4; slot++)
            {
                Assert.AreEqual(KeyboardInputProvider.AbilityKeys[slot][0].ToString(),
                    HudLayoutRules.BadgeLabel(HudDeviceMode.KeyboardMouse, slot), "Badge = the slot's first key.");
                Assert.AreEqual(HudLayoutRules.PadLabel(GamepadInputProvider.AbilityButtons[slot]),
                    HudLayoutRules.BadgeLabel(HudDeviceMode.Gamepad, slot));
            }
            Assert.AreEqual("RB", HudLayoutRules.PadLabel(UnityEngine.InputSystem.LowLevel.GamepadButton.RightShoulder));
        }

        private static float Luminance(Color c)
        {
            static float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        private static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        [Test]
        public void Badges_AreAtLeast28pxOnScreen_AndHaveAtLeast4_5To1Contrast()
        {
            string uss = Read("Assets/UI/Styles/TouchControls.uss");

            var text = Regex.Match(uss, @"\.cw-hud--desktop \.cw-hex-badge-text,\s*\.cw-hud--pad \.cw-hex-badge-text\s*\{\s*font-size:\s*(\d+)px");
            Assert.IsTrue(text.Success, "The desktop / pad badge text rule is missing.");
            float fontPx = float.Parse(text.Groups[1].Value) * HudLayoutRules.HexRestScale(HudDeviceMode.KeyboardMouse);
            Assert.GreaterOrEqual(fontPx, 28f, "Badge text on the 0.75 strip must still be >= 28 px.");

            // The badge cap and its text colour, straight from the stylesheet.
            var bg = Regex.Match(uss, @"\.cw-hex-badge\s*\{[^}]*background-color:\s*rgba\((\d+),\s*(\d+),\s*(\d+)");
            var fg = Regex.Match(uss, @"\.cw-hex-badge-text\s*\{[^}]*color:\s*rgb\((\d+),\s*(\d+),\s*(\d+)\)");
            Assert.IsTrue(bg.Success && fg.Success);
            Color Rgb(Match m) => new Color(int.Parse(m.Groups[1].Value) / 255f, int.Parse(m.Groups[2].Value) / 255f, int.Parse(m.Groups[3].Value) / 255f);
            Assert.GreaterOrEqual(Contrast(Rgb(fg), Rgb(bg)), 4.5f);
        }

        [Test]
        public void CooldownText_WholeSecondsRoundUp_TenthsInTheLastSecond()
        {
            Assert.AreEqual(string.Empty, HudLayoutRules.CooldownText(0f));
            Assert.AreEqual("5", HudLayoutRules.CooldownText(4.2f));
            Assert.AreEqual("2", HudLayoutRules.CooldownText(1.01f));
            Assert.AreEqual("1", HudLayoutRules.CooldownText(1f));
            Assert.AreEqual("0.9", HudLayoutRules.CooldownText(0.85f));
            Assert.AreEqual("0.1", HudLayoutRules.CooldownText(0.01f));
            Assert.AreEqual("1", HudLayoutRules.CooldownText(0.96f), "Never shows 0.10 tenths past ten.");
        }

        // ---- First-time hint -------------------------------------------------------------------------------------

        [Test]
        public void Hint_ShowsForTheFirstFiveHolds_ThenNeverAgain()
        {
            int count = 0;
            var shown = new List<bool>();
            for (int hold = 1; hold <= 9; hold++) shown.Add(HoldHintRules.OnHoldQualified(ref count));

            CollectionAssert.AreEqual(new[] { true, true, true, true, true, false, false, false, false }, shown);
            Assert.AreEqual(5, count, "The counter stops at the cap.");
            Assert.AreEqual(5, HoldHintRules.MaxHolds);
        }

        [Test]
        public void Hint_ACounterAlreadyAtTheCapStaysSilent_AcrossASessionRestart()
        {
            int count = 5;
            Assert.IsFalse(HoldHintRules.OnHoldQualified(ref count));
            Assert.AreEqual(5, count);

            bool had = PlayerPrefs.HasKey(PlayerPreferences.HoldHintCountKey);
            int saved = PlayerPrefs.GetInt(PlayerPreferences.HoldHintCountKey, 0);
            try
            {
                PlayerPreferences.HoldHintCount = 3;
                PlayerPreferences.ResetCache();
                Assert.AreEqual(3, PlayerPreferences.HoldHintCount, "Persisted across a cache reset (a restart).");
                PlayerPreferences.HoldHintCount = -4;
                Assert.AreEqual(0, PlayerPreferences.HoldHintCount, "Never negative.");
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(PlayerPreferences.HoldHintCountKey, saved); else PlayerPrefs.DeleteKey(PlayerPreferences.HoldHintCountKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        [Test]
        public void Hint_CopyByDevice()
        {
            UiText.Reset();
            Assert.AreEqual("Hold to aim. Let go to use it. Drag to the edge to cancel.", UiText.Get(HoldHintRules.KeyFor(HudDeviceMode.Touch)));
            Assert.AreEqual("Hold to aim · Release to use · B to cancel.", UiText.Get(HoldHintRules.KeyFor(HudDeviceMode.Gamepad)));
            Assert.AreEqual("Hold to aim · Release to use · Esc to cancel.", UiText.Get(HoldHintRules.KeyFor(HudDeviceMode.KeyboardMouse)));
            Assert.IsEmpty(UiText.Problems);
            UiText.Reset();
        }

        [Test]
        public void Hint_OnlyRealHoldsCount_TapsAndQuickMovesDoNot()
        {
            string touch = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            int at = touch.IndexOf("private void UpdateHoldHint()", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0);
            string body = touch.Substring(at, 1800);
            StringAssert.Contains("FeedbackTuning.TapHoldThresholdSeconds", body, "A hold counts only once it outlives the tap threshold.");
            StringAssert.Contains("QuickMovesEnabled", body, "Quick Moves has no hold to explain.");
            StringAssert.Contains("HoldHintRules.OnHoldQualified", body);
        }

        // ---- Compact leaderboard ---------------------------------------------------------------------------------

        private static bool[] Rows(int count, int you, bool showAll)
        {
            var v = new bool[4];
            CompactBoardRules.SelectRows(count, you, showAll, v);
            return v;
        }

        [Test]
        public void Board_CompactShowsTheLeaderAndYou()
        {
            CollectionAssert.AreEqual(new[] { true, false, true, false }, Rows(4, 2, false), "3rd: leader + you.");
            CollectionAssert.AreEqual(new[] { true, false, false, true }, Rows(4, 3, false), "Last: leader + you.");
            CollectionAssert.AreEqual(new[] { true, true, false, false }, Rows(4, 1, false), "2nd: leader + you, which is two rows.");
        }

        [Test]
        public void Board_WhenYouLeadYouGetTheRivalBehindYouToo()
        {
            CollectionAssert.AreEqual(new[] { true, true, false, false }, Rows(4, 0, false));
            CollectionAssert.AreEqual(new[] { true, true, false, false }, Rows(2, 0, false));
            CollectionAssert.AreEqual(new[] { true, false, false, false }, Rows(1, 0, false), "Alone: just you.");
        }

        [Test]
        public void Board_NotListedShowsOnlyTheLeader_AndEmptyShowsNothing()
        {
            CollectionAssert.AreEqual(new[] { true, false, false, false }, Rows(3, -1, false));
            CollectionAssert.AreEqual(new[] { false, false, false, false }, Rows(0, -1, false));
        }

        [Test]
        public void Board_ShowAllListsEveryRankedRow_NeverMoreThanExist()
        {
            CollectionAssert.AreEqual(new[] { true, true, true, true }, Rows(4, 2, true));
            CollectionAssert.AreEqual(new[] { true, true, true, false }, Rows(3, 1, true));
        }

        [Test]
        public void Board_ShowAllOnTapInTheLast10sAndAlwaysOffPhone()
        {
            Assert.IsTrue(CompactBoardRules.ShowAll(compact: false, tapOpen: false, matchActive: true, secondsLeft: 40f), "Desktop is unchanged.");
            Assert.IsFalse(CompactBoardRules.ShowAll(true, false, true, 40f), "Phone default is compact.");
            Assert.IsTrue(CompactBoardRules.ShowAll(true, true, true, 40f), "A tap opens it.");
            Assert.IsTrue(CompactBoardRules.ShowAll(true, false, true, 10f), "The last 10 s open it by themselves.");
            Assert.IsFalse(CompactBoardRules.ShowAll(true, false, true, 10.5f));
            Assert.IsFalse(CompactBoardRules.ShowAll(true, false, false, 3f), "Seconds left mean nothing outside an active match.");
        }

        // ---- Cluster reach ---------------------------------------------------------------------------------------

        private const float HexPx = 150f;
        private const float PxPerDp = 2.35f;

        private static Vector2[] ClusterCentres()
        {
            string uss = Read("Assets/UI/Styles/TouchControls.uss");
            var centres = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                var m = Regex.Match(uss, @"^\.cw-hex--a" + (i + 1) + @"\s*\{\s*right:\s*(\d+)px;\s*bottom:\s*(\d+)px;", RegexOptions.Multiline);
                Assert.IsTrue(m.Success, $".cw-hex--a{i + 1} rule not found in TouchControls.uss.");
                // Centre measured from the AbilityRoot's bottom-right corner (x grows leftwards, y upwards).
                centres[i] = new Vector2(float.Parse(m.Groups[1].Value) + HexPx / 2f, float.Parse(m.Groups[2].Value) + HexPx / 2f);
            }
            return centres;
        }

        [Test]
        public void Cluster_EveryHexCentreIsWithin125dpOfSlot1()
        {
            var c = ClusterCentres();
            for (int i = 1; i < 4; i++)
            {
                float dp = Vector2.Distance(c[0], c[i]) / PxPerDp;
                Assert.LessOrEqual(dp, 125f, $"Slot {i + 1} is {dp:0} dp from slot 1 (the thumb pivot).");
            }
        }

        [Test]
        public void Cluster_HexesDoNotOverlap_AndKeepAGap()
        {
            var c = ClusterCentres();
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    Assert.GreaterOrEqual(Vector2.Distance(c[i], c[j]), HexPx + 20f, $"Hexes {i + 1} and {j + 1} are too close.");
        }

        [Test]
        public void Cluster_StaysInsideTheSafeAreaAndClearOfTheJoystick_AtPhoneAndTabletShapes()
        {
            var c = ClusterCentres();
            // Panel sizes the shared PanelSettings (1920x1080 reference) yields at 20:9 and 4:3, matching either axis;
            // the safe-area zone is inset by a generous 100 px each side and 60 px top and bottom.
            var panels = new[] { new Vector2(2400, 1080), new Vector2(1920, 864), new Vector2(1440, 1080), new Vector2(1920, 1440) };
            foreach (var panel in panels)
            {
                float w = panel.x - 200f, h = panel.y - 120f;
                for (int i = 0; i < 4; i++)
                {
                    float left = w - (c[i].x + HexPx / 2f), right = w - (c[i].x - HexPx / 2f);
                    float bottom = c[i].y - HexPx / 2f, top = c[i].y + HexPx / 2f;
                    Assert.GreaterOrEqual(left, 0f, $"Hex {i + 1} leaves the left edge at {panel}.");
                    Assert.LessOrEqual(right, w, $"Hex {i + 1} leaves the right edge at {panel}.");
                    Assert.GreaterOrEqual(bottom, 0f, $"Hex {i + 1} leaves the bottom at {panel}.");
                    Assert.LessOrEqual(top + 60f, h, $"Hex {i + 1} (plus the hint line) leaves the top at {panel}.");
                    // The joystick base occupies x in [80, 360] from the left, y in [80, 360] from the bottom.
                    Assert.Greater(left, 360f + 20f, $"Hex {i + 1} runs into the joystick at {panel}.");
                }
            }
        }

        // ---- Settings ---------------------------------------------------------------------------------------------

        [Test]
        public void SettingsSheet_HasTheNewRowsAndSliders_AndScrolls()
        {
            var vta = TestAssets.Load<VisualTreeAsset>("Assets/UI/MainMenu.uxml");
            var root = vta.CloneTree();
            foreach (var row in new[] { "QuickMovesRow", "AutoPeckRow", "BuzzWhenHitRow", "MusicVolumeRow", "SfxVolumeRow" })
                Assert.IsNotNull(root.Q<VisualElement>(row), row);
            foreach (var toggle in new[] { "QuickMovesToggle", "AutoPeckToggle", "BuzzWhenHitToggle" })
                Assert.IsNotNull(root.Q<Toggle>(toggle), toggle);
            foreach (var slider in new[] { "MusicVolumeSlider", "SfxVolumeSlider" })
                Assert.IsNotNull(root.Q<Slider>(slider), slider);
            Assert.IsNotNull(root.Q<ScrollView>(className: "cw-sheet__body"), "Nine rows need to scroll on a phone.");
        }

        [Test]
        public void SettingsCopy_MatchesTheBrief()
        {
            UiText.Reset();
            Assert.AreEqual("Quick Moves", UiText.Get(UiKeys.SettingsQuickMoves));
            Assert.AreEqual("Moves fire the moment you tap.", UiText.Get(UiKeys.SettingsQuickMovesDesc));
            Assert.AreEqual("Auto-Peck", UiText.Get(UiKeys.SettingsAutoPeck));
            Assert.AreEqual("Peck by yourself when you stand by a pile.", UiText.Get(UiKeys.SettingsAutoPeckDesc));
            Assert.AreEqual("Buzz When Hit", UiText.Get(UiKeys.SettingsBuzzWhenHit));
            Assert.AreEqual("Your phone buzzes when a move hits you.", UiText.Get(UiKeys.SettingsBuzzWhenHitDesc));
            UiText.Reset();
        }

        [Test]
        public void SettingsController_BindsEveryNewRow_AndHidesBuzzOffPhone()
        {
            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            foreach (var row in new[] { "QuickMovesRow", "AutoPeckRow", "BuzzWhenHitRow", "MusicVolumeSlider", "SfxVolumeSlider" })
                StringAssert.Contains($"\"{row}\"", menu);
            StringAssert.Contains("Application.isMobilePlatform", menu, "Buzz When Hit is hidden where there is no vibrator.");
        }

        [Test]
        public void Volumes_DefaultToFullAndClamp()
        {
            bool hadM = PlayerPrefs.HasKey(PlayerPreferences.MusicVolumeKey), hadS = PlayerPrefs.HasKey(PlayerPreferences.SfxVolumeKey);
            float savedM = PlayerPrefs.GetFloat(PlayerPreferences.MusicVolumeKey, 1f), savedS = PlayerPrefs.GetFloat(PlayerPreferences.SfxVolumeKey, 1f);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.MusicVolumeKey);
                PlayerPrefs.DeleteKey(PlayerPreferences.SfxVolumeKey);
                PlayerPreferences.ResetCache();
                Assert.AreEqual(1f, PlayerPreferences.MusicVolume);
                Assert.AreEqual(1f, PlayerPreferences.SfxVolume);

                PlayerPreferences.MusicVolume = 7f;
                PlayerPreferences.SfxVolume = -3f;
                Assert.AreEqual(1f, PlayerPreferences.MusicVolume);
                Assert.AreEqual(0f, PlayerPreferences.SfxVolume);

                PlayerPreferences.SfxVolume = 0.4f;
                PlayerPreferences.FlushVolumes();
                PlayerPreferences.ResetCache();
                Assert.AreEqual(0.4f, PlayerPreferences.SfxVolume, 1e-5f, "Persisted.");
            }
            finally
            {
                if (hadM) PlayerPrefs.SetFloat(PlayerPreferences.MusicVolumeKey, savedM); else PlayerPrefs.DeleteKey(PlayerPreferences.MusicVolumeKey);
                if (hadS) PlayerPrefs.SetFloat(PlayerPreferences.SfxVolumeKey, savedS); else PlayerPrefs.DeleteKey(PlayerPreferences.SfxVolumeKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }
    }
}
