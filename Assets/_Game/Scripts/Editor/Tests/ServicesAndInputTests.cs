using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Services;
using CluckWars.Settings;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the plain-C# service and input layer — the parts of the architecture
    /// that sit behind an interface and therefore never get exercised by a compile.
    /// </summary>
    /// <remarks>
    /// Everything here is constructible without Unity's runtime: the Null* services
    /// (which is what a demo / offline build actually runs on),
    /// <c>SessionSelectionService</c> (the menu's whole state), and
    /// <c>CompositeInputProvider</c> (the keyboard+touch merge that every local input
    /// tick goes through). A defect in any of these is invisible until a human plays.
    ///
    /// <c>FusionNetworkService</c> and the real <c>UGSService</c> are NOT covered —
    /// they wrap live SDKs and need a network.
    /// </remarks>
    public sealed class ServicesAndInputTests
    {
        // ---- SessionSelectionService --------------------------------------------

        [Test]
        public void SessionSelection_DefaultsAreTheSafeColdStartValues()
        {
            var svc = new SessionSelectionService();

            Assert.AreEqual(ChickenClass.Warrior, svc.SelectedClass,
                "Cold start must select a class that definitely exists in the registry — otherwise " +
                "ChickenController.Spawned resolves nothing and falls back silently.");
            Assert.AreEqual(SessionMode.Solo, svc.Mode,
                "Defaulting to anything but Solo would have a cold-started client try to reach Photon " +
                "before the player picked Host or Join.");
            Assert.AreEqual("cluck-lan", svc.SessionName,
                "The offline/LAN demo flow hardcodes this session name on both the Host and Join side.");
            Assert.IsNull(svc.Ability0, "Unset slots must be null so the prefab default is used.");
            Assert.IsNull(svc.Ability1);
            Assert.IsNull(svc.Ability2);
        }

        [Test]
        public void SessionSelection_RaisesOnSelectionChanged_OnlyOnARealChange()
        {
            // MenuUiController rebuilds the whole character-select panel from this
            // event. Firing on a redundant set would rebuild the UI on every click of
            // the already-selected chip.
            var svc = new SessionSelectionService();
            var seen = new List<ChickenClass>();
            svc.OnSelectionChanged += c => seen.Add(c);

            svc.SelectedClass = ChickenClass.Warrior; // same as the default
            CollectionAssert.IsEmpty(seen, "Setting the already-selected class must not raise the event.");

            svc.SelectedClass = ChickenClass.Fatty;
            svc.SelectedClass = ChickenClass.Fatty;
            CollectionAssert.AreEqual(new[] { ChickenClass.Fatty }, seen,
                "The event should fire exactly once per actual change.");
            Assert.AreEqual(ChickenClass.Fatty, svc.SelectedClass);
        }

        [Test]
        public void SessionSelection_WithNoSubscribers_DoesNotThrow()
        {
            // The setter invokes the event; the menu is not always alive when the
            // selection changes (e.g. reset on returning to Bootstrap).
            var svc = new SessionSelectionService();
            Assert.DoesNotThrow(() => svc.SelectedClass = ChickenClass.Assassin);
        }

        // ---- NullUGSService — what the offline demo actually runs on -------------

        [Test]
        public void NullUgs_ReportsItselfUnavailable_SoCallersTakeTheOfflinePath()
        {
            var ugs = new NullUGSService();
            Assert.IsFalse(ugs.IsAvailable);
            Assert.IsFalse(ugs.IsSignedIn);
            Assert.IsNull(ugs.CurrentLobby);
        }

        [Test]
        public void NullUgs_CreateLobby_ReturnsTheLegacyLanSessionName()
        {
            // The offline Host flow feeds this JoinCode straight into Fusion as the
            // session name. If it stopped matching the Join side's "cluck-lan"
            // default, two local clients would land in different sessions.
            var lobby = new NullUGSService().CreateLobbyAsync("whatever", 4).Result;

            Assert.IsNotNull(lobby);
            Assert.AreEqual("cluck-lan", lobby.JoinCode);
        }

        [Test]
        public void NullUgs_JoinByCode_PreservesTheCode_AndTrimsPastedWhitespace()
        {
            // Join codes get typed and pasted; a trailing newline would become part
            // of the Fusion session name and silently create a second empty room.
            var lobby = new NullUGSService().JoinLobbyByCodeAsync("  ABC123 \n").Result;

            Assert.AreEqual("ABC123", lobby.JoinCode);
        }

        [Test]
        public void NullUgs_QueryLobbies_ReturnsAnEmptyList_NotNull()
        {
            // The lobby browser enumerates the result directly.
            var lobbies = new NullUGSService().QueryLobbiesAsync().Result;

            Assert.IsNotNull(lobbies, "A null list would NRE the lobby browser instead of showing 'no games'.");
            CollectionAssert.IsEmpty(lobbies);
        }

        [Test]
        public void NullUgs_LifecycleCalls_CompleteInsteadOfHanging()
        {
            // MenuUiController awaits these on the offline path. A task that never
            // completes would freeze the menu with no error.
            var ugs = new NullUGSService();

            Assert.IsTrue(ugs.InitializeAsync().IsCompleted);
            Assert.IsTrue(ugs.SignInAnonymouslyAsync().IsCompleted);
            Assert.IsTrue(ugs.SignOutAsync().IsCompleted);
            Assert.IsTrue(ugs.LeaveLobbyAsync().IsCompleted);
        }

        [Test]
        public void LobbyInfo_ToString_ShowsTheJoinCodeAndOccupancy()
        {
            // Rendered verbatim in the lobby list; the join code is the one thing a
            // player has to read off the screen and type on another device.
            var info = new LobbyInfo("id-1", "Marco's Game", "ABC123", 2, 4);

            Assert.AreEqual("Marco's Game [ABC123] 2/4", info.ToString());
        }

        // ---- NullAudioService ---------------------------------------------------

        [Test]
        public void NullAudio_SwallowsEveryCall_IncludingNullClips()
        {
            // Bound during headless / Phase-1 runs. Gameplay calls it unconditionally.
            var audio = new NullAudioService();

            Assert.DoesNotThrow(() =>
            {
                audio.PlaySFX(null);
                audio.PlayMusic(null);
                audio.StopMusic();
                audio.SetMasterVolume(0.5f);
            });
        }

        // ---- CompositeInputProvider — every local input tick goes through it -----

        /// <summary>Scriptable stand-in that records how many times each getter was read.</summary>
        private sealed class FakeInput : IInputProvider
        {
            public Vector2 Movement;
            public bool A1, A2, A3, A4, Cancel, Back, CancelArmed;
            public bool[] Held = new bool[AbilityController.SlotCount];
            public int A1Reads, A2Reads, A3Reads, A4Reads, CancelReads, BackReads;

            public Vector2 GetMovement() => Movement;
            public bool GetAbility1Pressed() { A1Reads++; return A1; }
            public bool GetAbility2Pressed() { A2Reads++; return A2; }
            public bool GetAbility3Pressed() { A3Reads++; return A3; }
            public bool GetAbility4Pressed() { A4Reads++; return A4; }
            public bool GetAbilityHeld(int slot) =>
                slot >= 0 && slot < AbilityController.SlotCount && Held[slot];
            public bool GetAbilityCancelPressed() { CancelReads++; return Cancel; }
            public bool GetBackPressed() { BackReads++; return Back; }
            public bool IsAbilityCancelArmed() => CancelArmed;
        }

        [Test]
        public void Composite_WithNoProviders_IsInert()
        {
            var composite = new CompositeInputProvider(null);

            Assert.AreEqual(Vector2.zero, composite.GetMovement());
            Assert.IsFalse(composite.GetAbility1Pressed());
            Assert.IsFalse(composite.GetAbility2Pressed());
            Assert.IsFalse(composite.GetAbility3Pressed());
        }

        [Test]
        public void Composite_Movement_TakesTheLargestMagnitude_NotTheFirstNonZero()
        {
            // Keyboard and touch are both live on Windows. A player nudging the
            // on-screen stick while holding W must not have their WASD input
            // downgraded to the stick's small vector.
            var keyboard = new FakeInput { Movement = new Vector2(0f, 1f) };
            var touch    = new FakeInput { Movement = new Vector2(0.2f, 0.1f) };

            var composite = new CompositeInputProvider(touch, keyboard);

            Assert.AreEqual(new Vector2(0f, 1f), composite.GetMovement());
        }

        [Test]
        public void Composite_Movement_IsZero_WhenEveryProviderIsIdle()
        {
            var composite = new CompositeInputProvider(new FakeInput(), new FakeInput());
            Assert.AreEqual(Vector2.zero, composite.GetMovement());
        }

        [Test]
        public void Composite_AbilityPress_OrsAcrossProviders()
        {
            var keyboard = new FakeInput { A1 = false, A2 = true,  A3 = false };
            var touch    = new FakeInput { A1 = true,  A2 = false, A3 = false };

            var composite = new CompositeInputProvider(keyboard, touch);

            Assert.IsTrue(composite.GetAbility1Pressed(),  "Touch pressed ability 1.");
            Assert.IsTrue(composite.GetAbility2Pressed(),  "Keyboard pressed ability 2.");
            Assert.IsFalse(composite.GetAbility3Pressed(), "Nobody pressed ability 3.");
        }

        [Test]
        public void Composite_ReadsEveryProvider_EvenAfterOneAlreadyReturnedTrue()
        {
            // Documented contract on the class: the getters are edge-triggered and
            // one-shot, so every provider must be READ each tick to consume its
            // latch. A `||` short-circuit here would leave the second provider's
            // press queued and fire the ability again on the following tick.
            var first  = new FakeInput { A1 = true,  A2 = true,  A3 = true };
            var second = new FakeInput { A1 = false, A2 = false, A3 = false };

            var composite = new CompositeInputProvider(first, second);
            composite.GetAbility1Pressed();
            composite.GetAbility2Pressed();
            composite.GetAbility3Pressed();

            Assert.AreEqual(1, second.A1Reads,
                "The second provider's ability-1 latch was never read — its press leaks into the next tick.");
            Assert.AreEqual(1, second.A2Reads, "The second provider's ability-2 latch was never read.");
            Assert.AreEqual(1, second.A3Reads, "The second provider's ability-3 latch was never read.");
        }

        // ---- v0.6 hold-to-aim: GetAbilityHeld / GetAbilityCancelPressed ----------

        [Test]
        public void Composite_AbilityHeld_OrsAcrossProviders_PerSlot()
        {
            var keyboard = new FakeInput { Held = new[] { false, true, false } };
            var touch    = new FakeInput { Held = new[] { true, false, false } };

            var composite = new CompositeInputProvider(keyboard, touch);

            Assert.IsTrue(composite.GetAbilityHeld(0), "Touch is holding slot 0.");
            Assert.IsTrue(composite.GetAbilityHeld(1), "Keyboard is holding slot 1.");
            Assert.IsFalse(composite.GetAbilityHeld(2), "Nobody is holding slot 2.");
        }

        [Test]
        public void Composite_AbilityCancel_OrsAcrossProviders_AndConsumesEveryLatch()
        {
            var first  = new FakeInput { Cancel = true };
            var second = new FakeInput { Cancel = false };

            var composite = new CompositeInputProvider(first, second);

            Assert.IsTrue(composite.GetAbilityCancelPressed());
            Assert.AreEqual(1, second.CancelReads,
                "The second provider's cancel latch was never read — same one-shot leak risk as the ability presses.");
        }

        [Test]
        public void Composite_Back_OrsAcrossProviders_AndReadsEveryProvider()
        {
            var first  = new FakeInput { Back = true };
            var second = new FakeInput { Back = false };

            var composite = new CompositeInputProvider(first, second);

            Assert.IsTrue(composite.GetBackPressed());
            Assert.AreEqual(1, second.BackReads, "Every provider's back edge is read once per poll.");
            Assert.IsFalse(new CompositeInputProvider(new FakeInput(), new FakeInput()).GetBackPressed());
        }

        [Test]
        public void Back_IsNotTheAbilityCancel_SoTheMenuPollCannotStealIt()
        {
            // The menu polls GetBackPressed, the match polls GetAbilityCancelPressed; neither getter
            // may read through the other (a composite that did would consume the other's latch).
            var p = new FakeInput { Back = true };
            var composite = new CompositeInputProvider(p);
            composite.GetBackPressed();
            Assert.AreEqual(0, p.CancelReads);
            composite.GetAbilityCancelPressed();
            Assert.AreEqual(1, p.BackReads);
        }

        [Test]
        public void Touch_HasNoBackButton()
        {
            Assert.IsFalse(new TouchInputProvider().GetBackPressed());
        }

        [Test]
        public void Keyboard_WithNoDeviceAttached_IsInertRatherThanThrowing()
        {
            // Keyboard.current is null on a headless / device-less run (CI, an
            // Android build with no keyboard). FusionNetworkService.OnInput calls
            // these every simulation tick — an NRE here kills the whole input path.
            var kb = new KeyboardInputProvider();

            Assert.DoesNotThrow(() =>
            {
                kb.GetMovement();
                kb.GetAbility1Pressed();
                kb.GetAbility2Pressed();
                kb.GetAbility3Pressed();
                kb.GetAbilityHeld(0);
                kb.GetAbilityHeld(1);
                kb.GetAbilityHeld(2);
                kb.GetAbilityCancelPressed();
                kb.GetBackPressed();
            });
        }

        [Test]
        public void Keyboard_WithNoDeviceAttached_ReportsNoHoldsAndNoCancel()
        {
            // Distinguishes "inert" from merely "doesn't crash" — a null Keyboard.current
            // must resolve to false everywhere, not throw AND not silently report true.
            var kb = new KeyboardInputProvider();

            Assert.IsFalse(kb.GetAbilityHeld(0));
            Assert.IsFalse(kb.GetAbilityHeld(1));
            Assert.IsFalse(kb.GetAbilityHeld(2));
            Assert.IsFalse(kb.GetAbilityCancelPressed());
        }

        [Test]
        public void Keyboard_AbilityHeld_OutOfRangeSlot_ReturnsFalse_NotThrow()
        {
            // Slot indices are 0..3 since every class gained a fourth slot (b3bf52f); this
            // test used to call 3 "out of range", which stopped being true then and only
            // kept passing because Keyboard.current is null in an EditMode run. The genuinely
            // out-of-range indices are 4 and -1, and they must be inert rather than throwing:
            // this runs on every simulation tick, so an exception kills the whole input path.
            var kb = new KeyboardInputProvider();
            Assert.DoesNotThrow(() => kb.GetAbilityHeld(4));
            Assert.IsFalse(kb.GetAbilityHeld(4));
            Assert.IsFalse(kb.GetAbilityHeld(-1));
        }

        // ---- Ability key bindings: two schemes (Maestro, 2026-11-20) -----------------------------
        // Asserted against the Key-enum tables rather than by pressing keys, because
        // Keyboard.current is null in EditMode: an enum table is what makes the mapping verifiable at all.

        private static readonly KeyboardScheme[] Schemes = { KeyboardScheme.WasdArrows, KeyboardScheme.ArrowsQwer };

        [Test]
        public void Keyboard_WasdArrows_PinsTheExactTable()
        {
            var table = KeyboardInputProvider.AbilityKeysFor(KeyboardScheme.WasdArrows);
            Assert.AreEqual(4, table.Length, "One row per ability slot.");
            CollectionAssert.AreEqual(new[] { Key.DownArrow,  Key.Digit1 }, table[0], "slot 1 = Down");
            CollectionAssert.AreEqual(new[] { Key.UpArrow,    Key.Digit2 }, table[1], "slot 2 = Up");
            CollectionAssert.AreEqual(new[] { Key.RightArrow, Key.Digit3 }, table[2], "slot 3 = Right");
            CollectionAssert.AreEqual(new[] { Key.LeftArrow,  Key.Digit4 }, table[3], "slot 4 = Left");
            CollectionAssert.AreEqual(new[] { Key.W, Key.S, Key.A, Key.D }, KeyboardInputProvider.MovementKeysFor(KeyboardScheme.WasdArrows), "up, down, left, right");
        }

        [Test]
        public void Keyboard_ArrowsQwer_PinsTheExactTable()
        {
            var table = KeyboardInputProvider.AbilityKeysFor(KeyboardScheme.ArrowsQwer);
            Assert.AreEqual(4, table.Length, "One row per ability slot.");
            CollectionAssert.AreEqual(new[] { Key.Q, Key.Digit1 }, table[0], "slot 1 = Q");
            CollectionAssert.AreEqual(new[] { Key.W, Key.Digit2 }, table[1], "slot 2 = W");
            CollectionAssert.AreEqual(new[] { Key.R, Key.Digit3 }, table[2], "slot 3 = R (the flex slot sits last, like an ultimate)");
            CollectionAssert.AreEqual(new[] { Key.E, Key.Digit4 }, table[3], "slot 4 = E");
            CollectionAssert.AreEqual(new[] { Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow },
                KeyboardInputProvider.MovementKeysFor(KeyboardScheme.ArrowsQwer), "up, down, left, right");
        }

        [Test]
        public void Keyboard_EverySlotOfEveryScheme_HasItsKeyAndItsDigit()
        {
            foreach (var scheme in Schemes)
            {
                var table = KeyboardInputProvider.AbilityKeysFor(scheme);
                Assert.AreEqual(4, table.Length, $"{scheme}: four slots");
                for (int slot = 0; slot < 4; slot++)
                {
                    Assert.AreEqual(2, table[slot].Length, $"{scheme} slot {slot}: the scheme's key plus a digit");
                    Assert.AreEqual(Key.Digit1 + slot, table[slot][1], $"{scheme} slot {slot}: the digit printed on the HUD hex");
                    CollectionAssert.AllItemsAreUnique(table[slot], $"{scheme} slot {slot}: no duplicate key inside a slot");
                }
            }
        }

        [Test]
        public void Keyboard_NoKeyDrivesTwoDifferentAbilities_InEitherScheme()
        {
            // A key appearing in two rows would fire both abilities from one press.
            foreach (var scheme in Schemes)
            {
                var seen = new Dictionary<Key, int>();
                var table = KeyboardInputProvider.AbilityKeysFor(scheme);
                for (int slot = 0; slot < table.Length; slot++)
                    foreach (var key in table[slot])
                    {
                        Assert.IsFalse(seen.TryGetValue(key, out int firstSlot), $"{scheme}: {key} is bound to both slot {firstSlot} and slot {slot}.");
                        seen[key] = slot;
                    }
            }
        }

        [Test]
        public void Keyboard_NoAbilityKeyCollidesWithThatSchemesMovement_AndEscIsInNeither()
        {
            // Walking must never fire an ability, and Esc stays cancel / back / Leave sheet.
            foreach (var scheme in Schemes)
            {
                var movement = new HashSet<Key>(KeyboardInputProvider.MovementKeysFor(scheme));
                Assert.AreEqual(4, movement.Count, $"{scheme}: four distinct movement keys");
                Assert.IsFalse(movement.Contains(Key.Escape), $"{scheme}: Esc is not a movement key");
                foreach (var row in KeyboardInputProvider.AbilityKeysFor(scheme))
                    foreach (var key in row)
                    {
                        Assert.IsFalse(movement.Contains(key), $"{scheme}: {key} is both a movement key and an ability key.");
                        Assert.AreNotEqual(Key.Escape, key, $"{scheme}: Esc is not an ability key.");
                    }
            }
        }

        [Test]
        public void Keyboard_TheTwoSchemesShareOnlyTheDigits()
        {
            var a = new HashSet<Key>(KeyboardInputProvider.AbilityKeysFor(KeyboardScheme.WasdArrows).SelectMany(r => r));
            var b = KeyboardInputProvider.AbilityKeysFor(KeyboardScheme.ArrowsQwer).SelectMany(r => r);
            CollectionAssert.AreEquivalent(new[] { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 }, b.Where(a.Contains).ToArray());
        }

        // ---- KeyboardScheme preference ------------------------------------------------------------

        [Test]
        public void KeyboardScheme_DefaultsToWasdArrows_RoundTrips_AndGarbageFallsBack()
        {
            bool had = PlayerPrefs.HasKey(PlayerPreferences.KeyboardSchemeKey);
            int saved = PlayerPrefs.GetInt(PlayerPreferences.KeyboardSchemeKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.KeyboardSchemeKey);
                PlayerPreferences.ResetCache();
                Assert.AreEqual(KeyboardScheme.WasdArrows, PlayerPreferences.KeyboardSchemeChoice, "default");

                PlayerPreferences.KeyboardSchemeChoice = KeyboardScheme.ArrowsQwer;
                Assert.AreEqual(KeyboardScheme.ArrowsQwer, PlayerPreferences.KeyboardSchemeChoice, "live, no reload");
                PlayerPreferences.ResetCache();
                Assert.AreEqual(KeyboardScheme.ArrowsQwer, PlayerPreferences.KeyboardSchemeChoice, "persisted");
                StringAssert.StartsWith("CluckWars.", PlayerPreferences.KeyboardSchemeKey);

                PlayerPrefs.SetInt(PlayerPreferences.KeyboardSchemeKey, 99);
                PlayerPreferences.ResetCache();
                Assert.AreEqual(KeyboardScheme.WasdArrows, PlayerPreferences.KeyboardSchemeChoice, "an unknown stored value falls back to the default");
                Assert.AreEqual(KeyboardScheme.WasdArrows, PlayerPreferences.SchemeFromStored(-1));
                Assert.AreEqual(KeyboardScheme.ArrowsQwer, PlayerPreferences.SchemeFromStored(1));
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(PlayerPreferences.KeyboardSchemeKey, saved);
                else PlayerPrefs.DeleteKey(PlayerPreferences.KeyboardSchemeKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        // ---- Badges and strip arrangement ---------------------------------------------------------

        [Test]
        public void Badge_WasdArrows_DrawsArrowsDownUpRightLeft_WithNoText()
        {
            var arrows = new[] { BadgeArrow.Down, BadgeArrow.Up, BadgeArrow.Right, BadgeArrow.Left };
            for (int slot = 0; slot < 4; slot++)
            {
                Assert.AreEqual(arrows[slot], HudLayoutRules.ArrowFor(HudDeviceMode.KeyboardMouse, slot, KeyboardScheme.WasdArrows), $"slot {slot}");
                Assert.AreEqual(string.Empty, HudLayoutRules.BadgeLabel(HudDeviceMode.KeyboardMouse, slot, KeyboardScheme.WasdArrows), "an arrow badge prints no text");
            }
        }

        [Test]
        public void Badge_ArrowsQwer_PrintsQWRE_AndNoArrow()
        {
            var letters = new[] { "Q", "W", "R", "E" };
            for (int slot = 0; slot < 4; slot++)
            {
                Assert.AreEqual(letters[slot], HudLayoutRules.BadgeLabel(HudDeviceMode.KeyboardMouse, slot, KeyboardScheme.ArrowsQwer), $"slot {slot}");
                Assert.AreEqual(BadgeArrow.None, HudLayoutRules.ArrowFor(HudDeviceMode.KeyboardMouse, slot, KeyboardScheme.ArrowsQwer));
            }
        }

        [Test]
        public void Badge_OnTouchAndPad_IgnoresTheKeyboardScheme()
        {
            foreach (var scheme in Schemes)
            {
                Assert.AreEqual("1", HudLayoutRules.BadgeLabel(HudDeviceMode.Touch, 0, scheme));
                Assert.AreEqual("A", HudLayoutRules.BadgeLabel(HudDeviceMode.Gamepad, 0, scheme));
                Assert.AreEqual(BadgeArrow.None, HudLayoutRules.ArrowFor(HudDeviceMode.Touch, 0, scheme));
                Assert.AreEqual(BadgeArrow.None, HudLayoutRules.ArrowFor(HudDeviceMode.Gamepad, 0, scheme));
            }
            Assert.AreEqual(BadgeArrow.None, HudLayoutRules.ArrowFor(HudDeviceMode.KeyboardMouse, 4, KeyboardScheme.WasdArrows), "out of range is inert");
        }

        [Test]
        public void BadgeArrow_Classes_AreOnePerDirection_EachWithOneRotateRule()
        {
            string uss = System.IO.File.ReadAllText(System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName, "Assets/UI/Styles/TouchControls.uss"));
            var expectedDegrees = new Dictionary<BadgeArrow, int> { { BadgeArrow.Up, 0 }, { BadgeArrow.Right, 90 }, { BadgeArrow.Down, 180 }, { BadgeArrow.Left, 270 } };
            foreach (var kv in expectedDegrees)
            {
                string cls = HudLayoutRules.ArrowClass(kv.Key);
                Assert.IsNotNull(cls, kv.Key.ToString());
                var m = System.Text.RegularExpressions.Regex.Match(uss,
                    @"\." + System.Text.RegularExpressions.Regex.Escape(cls) + @"\s*\{\s*rotate:\s*(\d+)deg");
                Assert.IsTrue(m.Success, $"{cls}: no rotate rule");
                Assert.AreEqual(kv.Value, int.Parse(m.Groups[1].Value), $"{cls} rotation");
            }
            Assert.IsNull(HudLayoutRules.ArrowClass(BadgeArrow.None));
        }

        [Test]
        public void StripArrangement_EveryDeviceAndScheme()
        {
            Assert.AreEqual(HudStripArrangement.Tee, HudLayoutRules.StripArrangement(HudDeviceMode.KeyboardMouse, KeyboardScheme.WasdArrows));
            Assert.AreEqual(HudStripArrangement.Row, HudLayoutRules.StripArrangement(HudDeviceMode.KeyboardMouse, KeyboardScheme.ArrowsQwer));
            Assert.AreEqual(HudStripArrangement.Tee, HudLayoutRules.StripArrangement(HudDeviceMode.Gamepad, KeyboardScheme.WasdArrows), "pad shares the T");
            Assert.AreEqual(HudStripArrangement.Tee, HudLayoutRules.StripArrangement(HudDeviceMode.Gamepad, KeyboardScheme.ArrowsQwer), "the keyboard scheme never changes the pad");
            Assert.AreEqual(HudStripArrangement.None, HudLayoutRules.StripArrangement(HudDeviceMode.Touch, KeyboardScheme.WasdArrows));
            Assert.AreEqual(HudStripArrangement.None, HudLayoutRules.StripArrangement(HudDeviceMode.Touch, KeyboardScheme.ArrowsQwer));
            Assert.AreEqual("cw-hud--strip-row", HudLayoutRules.ArrangementClass(HudStripArrangement.Row));
            Assert.AreEqual("cw-hud--strip-tee", HudLayoutRules.ArrangementClass(HudStripArrangement.Tee));
        }
    }
}
