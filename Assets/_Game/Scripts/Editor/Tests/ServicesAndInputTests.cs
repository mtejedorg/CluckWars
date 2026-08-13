using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Services;

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
            public bool A1, A2, A3, A4, Cancel;
            public bool[] Held = new bool[AbilityController.SlotCount];
            public int A1Reads, A2Reads, A3Reads, A4Reads, CancelReads;

            public Vector2 GetMovement() => Movement;
            public bool GetAbility1Pressed() { A1Reads++; return A1; }
            public bool GetAbility2Pressed() { A2Reads++; return A2; }
            public bool GetAbility3Pressed() { A3Reads++; return A3; }
            public bool GetAbility4Pressed() { A4Reads++; return A4; }
            public bool GetAbilityHeld(int slot) =>
                slot >= 0 && slot < AbilityController.SlotCount && Held[slot];
            public bool GetAbilityCancelPressed() { CancelReads++; return Cancel; }
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
            // AbilityController's slot indices are always 0..2, but a defensive caller
            // (or a future slot-count change) must not crash the whole input tick.
            var kb = new KeyboardInputProvider();
            Assert.DoesNotThrow(() => kb.GetAbilityHeld(3));
            Assert.IsFalse(kb.GetAbilityHeld(3));
            Assert.IsFalse(kb.GetAbilityHeld(-1));
        }
    }
}
