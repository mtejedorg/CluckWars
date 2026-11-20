using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Networking;
using CluckWars.Settings;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 5: the aim byte decode (A7), touch / pad magnetism, the gamepad binding table and
    /// "Quick Moves" (A8). Pure layer plus source guards for the wiring that needs a runner.
    /// </summary>
    public sealed class Phase6Chunk5Tests
    {
        private const float Tick = 1f / 32f;
        private const float Threshold = FeedbackTuning.TapHoldThresholdSeconds;

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private static bool[] Slots(params int[] on)
        {
            var a = new bool[4];
            foreach (int i in on) a[i] = true;
            return a;
        }

        private static readonly bool[] All = { true, true, true, true };

        // ---- Aim byte --------------------------------------------------------------------------------

        [Test]
        public void Aim_ZeroIsReserved_AndDecodesToNoDirection()
        {
            Assert.AreEqual(Vector2.zero, AimQuantizer.Decode(0));
        }

        [Test]
        public void Aim_EveryNonZeroByteDecodesToADistinctUnitVector_ByteOneIsNorth()
        {
            // 0 deg is +Z (north), 90 deg is +X: the same convention the movement vector uses (x, z).
            Vector2 north = AimQuantizer.Decode(1);
            Assert.AreEqual(0f, north.x, 1e-5f);
            Assert.AreEqual(1f, north.y, 1e-5f);

            var seen = new HashSet<Vector2>();
            for (int b = 1; b <= 255; b++)
            {
                var v = AimQuantizer.Decode((byte)b);
                Assert.AreEqual(1f, v.magnitude, 1e-4f);
                Assert.IsTrue(seen.Add(new Vector2(Mathf.Round(v.x * 1e4f), Mathf.Round(v.y * 1e4f))), $"Byte {b} repeats a direction.");
            }
            Assert.AreEqual(255, seen.Count);
        }

        // ---- No device aims (item 5, 2026-11-20) -----------------------------------------------------

        [Test]
        public void NoDeviceAims_PcAndPadActLikeTouch_TheWireAimByteIsAlwaysZero()
        {
            Assert.IsNull(typeof(IInputProvider).GetMethod("GetAim"), "IInputProvider has no aim member.");
            Assert.IsNull(typeof(CompositeInputProvider).GetMethod("GetAim"));
            Assert.IsNull(typeof(KeyboardInputProvider).GetMethod("GetAim"));
            Assert.IsNull(typeof(GamepadInputProvider).GetMethod("GetAim"));

            string net = Read("Assets/_Game/Scripts/App/FusionNetworkService.cs");
            StringAssert.DoesNotContain("Aim =", net, "FusionNetworkService leaves the aim byte at 0, as a touch client always did.");
            StringAssert.DoesNotContain("AimQuantizer", net);
            StringAssert.DoesNotContain("GetAim", net);

            string pad = Read("Assets/_Game/Scripts/Input/GamepadInputProvider.cs");
            StringAssert.DoesNotContain("rightStick", pad, "The right stick does nothing.");
            StringAssert.DoesNotContain("GetAim", pad);
        }

        [Test]
        public void Stick_ToWorld_UsesTheSameCameraRotationAsMovement()
        {
            // Camera yaw 0: stick up is world +Z.
            var up = AimRules.StickToWorld(new Vector2(0f, 1f), 0f);
            Assert.AreEqual(0f, up.x, 1e-5f);
            Assert.AreEqual(1f, up.y, 1e-5f);

            // The helper is the one FusionNetworkService applies to movement: identical to the inline maths it replaced.
            foreach (float yawDeg in new[] { 0f, 37f, 90f, 180f, 271f })
            {
                float yaw = yawDeg * Mathf.Deg2Rad;
                var stick = new Vector2(0.4f, -0.9f);
                var legacy = new Vector2(
                    stick.x * Mathf.Cos(yaw) + stick.y * Mathf.Sin(yaw),
                    -stick.x * Mathf.Sin(yaw) + stick.y * Mathf.Cos(yaw));
                Assert.AreEqual(legacy.x, AimRules.StickToWorld(stick, yawDeg).x, 1e-6f);
                Assert.AreEqual(legacy.y, AimRules.StickToWorld(stick, yawDeg).y, 1e-6f);
            }
        }

        // ---- Magnetism and angle picks ----------------------------------------------------------------

        [Test]
        public void SoftLock_PicksTheClosestAngleInsideTheLimit_AndNothingOutside()
        {
            var facing = new Vector2(0f, 1f);
            var offsets = new[]
            {
                new Vector2(Mathf.Sin(25f * Mathf.Deg2Rad), Mathf.Cos(25f * Mathf.Deg2Rad)) * 4f,   // 25 deg
                new Vector2(Mathf.Sin(-10f * Mathf.Deg2Rad), Mathf.Cos(-10f * Mathf.Deg2Rad)) * 9f, // 10 deg, farther
                new Vector2(Mathf.Sin(50f * Mathf.Deg2Rad), Mathf.Cos(50f * Mathf.Deg2Rad)) * 2f,   // 50 deg
            };

            Assert.AreEqual(1, AimSoftLock.PickClosestAngle(facing, offsets, 3, AimSoftLock.TouchHalfAngleDegrees),
                "Touch picks the 10 degree rival, not the nearer 25 degree one.");
            Assert.AreEqual(1, AimSoftLock.PickClosestAngle(facing, offsets, 3, AimSoftLock.PadHalfAngleDegrees),
                "10 degrees is inside the pad's 12.");

            var outside = new[] { offsets[0], offsets[2] };
            Assert.AreEqual(-1, AimSoftLock.PickClosestAngle(facing, outside, 2, AimSoftLock.PadHalfAngleDegrees),
                "Nothing within 12 degrees: no magnetism.");
            Assert.AreEqual(0, AimSoftLock.PickClosestAngle(facing, outside, 2, AimSoftLock.TouchHalfAngleDegrees));
            Assert.AreEqual(-1, AimSoftLock.PickClosestAngle(facing, new[] { offsets[2] }, 1, AimSoftLock.TouchHalfAngleDegrees),
                "50 degrees is outside the touch 30.");
        }

        [Test]
        public void SoftLock_Limits_AreThirtyForTouchAndTwelveForPad()
        {
            Assert.AreEqual(30f, AimSoftLock.TouchHalfAngleDegrees);
            Assert.AreEqual(12f, AimSoftLock.PadHalfAngleDegrees);
        }

        [Test]
        public void SoftLock_TieKeepsTheFirstCandidate_AndZeroOffsetsAreSkipped()
        {
            var facing = Vector2.up;
            var tie = new[] { Vector2.zero, new Vector2(0f, 3f), new Vector2(0f, 7f) };
            Assert.AreEqual(1, AimSoftLock.PickClosestAngle(facing, tie, 3, 30f), "Zero offset skipped; first of a tie wins (callers sort nearest-first).");
            Assert.AreEqual(-1, AimSoftLock.PickClosestAngle(Vector2.zero, tie, 3, 30f), "No facing, no pick.");
            Assert.AreEqual(-1, AimSoftLock.PickClosestAngle(facing, tie, 0, 30f));
        }

        [Test]
        public void SingleTarget_SmallestAngleToTheAim_BeatsTheNearest()
        {
            // The nearest rival is 80 degrees off the aim; the farther one is 5 degrees off. Limit 180 = plain smallest angle.
            var aim = new Vector2(1f, 0f);
            var rivals = new[]
            {
                new Vector2(0.17f, 1f), // nearest, about 80 degrees off the aim
                new Vector2(8f, 0.7f),  // farther, about 5 degrees off
            };
            Assert.AreEqual(1, AimSoftLock.PickClosestAngle(aim, rivals, 2, 180f));
        }

        // ---- Which moves use the aim --------------------------------------------------------------------

        private static T Make<T>() where T : AbilityBaseSO => ScriptableObject.CreateInstance<T>();

        [Test]
        public void Dashes_FollowMovement_NeverTheAim_OnEveryDevice()
        {
            foreach (AbilityBaseSO dash in new AbilityBaseSO[]
                     { Make<SpeedBurstAbilitySO>(), Make<ShadowstepAbilitySO>(), Make<RollPushAbilitySO>(), Make<RollTrampleAbilitySO>() })
            {
                Assert.IsTrue(dash.FollowsMovementNotAim, $"{dash.GetType().Name} is declared a movement move.");
                Assert.IsFalse(dash.UsesAim, $"{dash.GetType().Name} must ignore aim.");
                Assert.IsFalse(dash.RotatesToAim, $"{dash.GetType().Name} must not rotate to aim.");
            }
        }

        [Test]
        public void SelfShapes_IgnoreAim()
        {
            Assert.IsFalse(Make<CluckShockAbilitySO>().UsesAim, "SelfCircle");
            Assert.IsFalse(Make<PeckAbilitySO>().UsesAim, "AimShape.None");
            Assert.IsFalse(Make<TurtleModeAbilitySO>().UsesAim, "AimShape.None");
            Assert.IsFalse(Make<FeatherAuraAbilitySO>().UsesAim, "Aura");
        }

        [Test]
        public void DirectionalShapes_UseAndRotateToAim_SingleTargetOnlyPicks()
        {
            // The authored shapes, so Shaped (2026-11-05: RangeOnly turns these into caster circles; SimplifiedAbilitiesTests).
            var savedMode = AbilityTargeting.Mode;
            AbilityTargeting.Mode = TargetingMode.Shaped;
            try { DirectionalShapesUnderShaped(); }
            finally { AbilityTargeting.Mode = savedMode; }
        }

        private void DirectionalShapesUnderShaped()
        {
            foreach (AbilityBaseSO cone in new AbilityBaseSO[] { Make<WingSlamAbilitySO>(), Make<HeadbuttAbilitySO>(), Make<SnatchAbilitySO>() })
            {
                Assert.AreEqual(AbilityAimShape.Cone, cone.AimShape);
                Assert.IsTrue(cone.UsesAim);
                Assert.IsTrue(cone.RotatesToAim);
            }
            foreach (AbilityBaseSO circle in new AbilityBaseSO[] { Make<FeatherTrapAbilitySO>(), Make<RootEggAbilitySO>() })
            {
                Assert.AreEqual(AbilityAimShape.ForwardCircle, circle.AimShape);
                Assert.IsTrue(circle.RotatesToAim);
            }
            foreach (AbilityBaseSO single in new AbilityBaseSO[] { Make<SneakyStealAbilitySO>() })
            {
                Assert.AreEqual(AbilityAimShape.SingleTarget, single.AimShape);
                Assert.IsTrue(single.UsesAim, "Single-target picks by angle to the aim.");
                Assert.IsFalse(single.RotatesToAim, "But the bird keeps its heading.");
            }
        }

        // ---- Quick Moves -------------------------------------------------------------------------------

        private static ChargeDecision Quick(bool[] hold, bool[] press, bool[] canBegin, bool quick = true) =>
            AbilityHoldStateMachine.Decide(0, AbilityHoldStateMachine.NoPendingSlot, 0f, Threshold,
                canCast: true, cancelPressed: false, hold, press, canBegin, quickCast: quick);

        [Test]
        public void QuickMoves_PressFiresOnThePressTick_WithNoPendingHold()
        {
            var d = Quick(Slots(1), Slots(1), All);
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(1, d.Slot);
        }

        [Test]
        public void QuickMoves_SubTickTapAndHoldAlone_AlsoFireImmediately()
        {
            Assert.AreEqual(ChargeAction.Fire, Quick(Slots(), Slots(2), All).Action, "Press edge only (tap between ticks).");
            Assert.AreEqual(ChargeAction.Fire, Quick(Slots(3), Slots(), All).Action, "Hold bit only.");
        }

        [Test]
        public void QuickMoves_Off_StillWaitsForReleaseExactlyAsBefore()
        {
            Assert.AreEqual(ChargeAction.BeginPendingHold, Quick(Slots(1), Slots(1), All, quick: false).Action);
            Assert.AreEqual(ChargeAction.Fire, Quick(Slots(), Slots(1), All, quick: false).Action, "The sub-tick tap fallback is unchanged.");
        }

        [Test]
        public void QuickMoves_ADeadSlot_StillRefusesOnAPress_AndStaysQuietWhenMerelyHeld()
        {
            var dead = new[] { true, false, true, true };
            var refuse = Quick(Slots(1), Slots(1), dead);
            Assert.AreEqual(ChargeAction.RefuseAttempt, refuse.Action);
            Assert.AreEqual(1, refuse.Slot);
            Assert.AreEqual(ChargeAction.None, Quick(Slots(1), Slots(), dead).Action, "A held dead slot does not spam refusals.");
        }

        [Test]
        public void QuickMoves_FizzleStillApplies_AndAFiredSlotIsSuppressedUntilReleased()
        {
            // A quick fire ends in TryActivate, so a target-gated move with nobody in range fizzles: no cooldown, re-arm.
            Assert.IsTrue(AbilityFizzleRules.IsFizzle(AbilityRefusal.NoTarget));
            Assert.AreEqual(AbilityFizzleRules.RearmSeconds, AbilityFizzleRules.Begin());

            // After the fire the controller suppresses the slot: a still-down hold reads as released, so it cannot refire.
            var hold = Slots(1);
            var press = Slots();
            var suppressed = new[] { false, true, false, false };
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.AreEqual(ChargeAction.None, Quick(hold, press, All).Action);
            Assert.IsTrue(suppressed[1], "Still suppressed while the key is down.");

            hold = Slots();
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.IsFalse(suppressed[1], "Released: the slot is armed again.");
        }

        [Test]
        public void QuickMoves_PreferenceDefaultsOffAndPersists()
        {
            bool had = PlayerPrefs.HasKey(PlayerPreferences.QuickMovesKey);
            int saved = PlayerPrefs.GetInt(PlayerPreferences.QuickMovesKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.QuickMovesKey);
                PlayerPreferences.ResetCache();
                Assert.IsFalse(PlayerPreferences.QuickMovesEnabled, "Quick Moves defaults to off.");

                PlayerPreferences.QuickMovesEnabled = true;
                PlayerPreferences.ResetCache();
                Assert.IsTrue(PlayerPreferences.QuickMovesEnabled, "The choice reached PlayerPrefs.");
                StringAssert.StartsWith("CluckWars.", PlayerPreferences.QuickMovesKey);
                Assert.AreNotEqual(PlayerPreferences.QuickMovesKey, PlayerPreferences.AutoPeckKey);
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(PlayerPreferences.QuickMovesKey, saved);
                else PlayerPrefs.DeleteKey(PlayerPreferences.QuickMovesKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        // ---- Gamepad table ----------------------------------------------------------------------------

        [Test]
        public void Gamepad_AbilityTable_IsTotal_ShoulderAndFaceButtonPerSlot()
        {
            Assert.AreEqual(AbilityController.SlotCount, GamepadInputProvider.AbilityButtons.Length, "One row per slot.");
            // Face buttons: A, Y, B, X = slots 1-4 (the diamond mirrors the slot roles).
            CollectionAssert.AreEqual(
                new[] { GamepadButton.South, GamepadButton.North, GamepadButton.East, GamepadButton.West },
                GamepadInputProvider.AbilityButtons.Select(r => r[0]).ToArray());
            // Shoulders and triggers: RB, RT, LB, LT = slots 1-4.
            CollectionAssert.AreEqual(
                new[] { GamepadButton.RightShoulder, GamepadButton.RightTrigger, GamepadButton.LeftShoulder, GamepadButton.LeftTrigger },
                GamepadInputProvider.AbilityButtons.Select(r => r[1]).ToArray());
            foreach (var row in GamepadInputProvider.AbilityButtons) Assert.AreEqual(2, row.Length);
        }

        [Test]
        public void Gamepad_NoButtonDrivesTwoThings()
        {
            var seen = new HashSet<GamepadButton>();
            foreach (var row in GamepadInputProvider.AbilityButtons)
                foreach (var b in row)
                    Assert.IsTrue(seen.Add(b), $"{b} is bound to two ability slots.");

            Assert.IsFalse(seen.Contains(GamepadInputProvider.CancelButton), "L3 must not also fire an ability.");
            Assert.IsFalse(seen.Contains(GamepadInputProvider.BackButton), "Start must not also fire an ability.");
            Assert.AreEqual(GamepadButton.LeftStick, GamepadInputProvider.CancelButton, "The hold cancel is L3.");
            Assert.AreEqual(GamepadButton.Start, GamepadInputProvider.BackButton);
            Assert.AreNotEqual(GamepadInputProvider.CancelButton, GamepadInputProvider.BackButton, "cancel != pause");

            // The menu back button is B and is the ONE shared button: it is slot 3 by design.
            Assert.AreEqual(GamepadButton.East, GamepadInputProvider.MenuBackButton);
            CollectionAssert.Contains(GamepadInputProvider.AbilityButtons[2], GamepadInputProvider.MenuBackButton);
            for (int slot = 0; slot < 4; slot++)
                if (slot != 2) CollectionAssert.DoesNotContain(GamepadInputProvider.AbilityButtons[slot], GamepadInputProvider.MenuBackButton);
        }

        // ---- Composite -----------------------------------------------------------------------------------

        private sealed class FakeDevice : IInputProvider
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

        [Test]
        public void Composite_DeviceIsTheMostRecentlyUsed_AndNoneBeforeAnyUse()
        {
            var kb = new FakeDevice { Kind = InputDeviceKind.KeyboardMouse };
            var touch = new FakeDevice { Kind = InputDeviceKind.Touch };
            Assert.AreEqual(InputDeviceKind.None, new CompositeInputProvider(kb, touch).Device);

            touch.Active = 3f;
            Assert.AreEqual(InputDeviceKind.Touch, new CompositeInputProvider(kb, touch).Device);
            kb.Active = 4f;
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, new CompositeInputProvider(kb, touch).Device);
            Assert.AreEqual(4f, new CompositeInputProvider(kb, touch).LastActiveTime);
        }

        [Test]
        public void ProvidersWithoutADevice_NeedNoCode_DefaultMembersMeanNoDevice()
        {
            IInputProvider bare = new BareProvider();
            Assert.AreEqual(InputDeviceKind.None, bare.Device);
            Assert.IsTrue(float.IsNegativeInfinity(bare.LastActiveTime));
        }

        private sealed class BareProvider : IInputProvider
        {
            public Vector2 GetMovement() => Vector2.zero;
            public bool GetAbility1Pressed() => false;
            public bool GetAbility2Pressed() => false;
            public bool GetAbility3Pressed() => false;
            public bool GetAbility4Pressed() => false;
            public bool GetAbilityHeld(int slot) => false;
            public bool GetAbilityCancelPressed() => false;
            public bool GetBackPressed() => false;
            public bool IsAbilityCancelArmed() => false;
        }

        // ---- Wire format and wiring guards -----------------------------------------------------------------

        [Test]
        public void InputButtons_AreAppendOnly_AndUnique()
        {
            Assert.AreEqual(9, (int)InputButton.AutoPeck, "Existing bits must never be renumbered.");
            Assert.AreEqual(10, (int)InputButton.SoftLockTouch);
            Assert.AreEqual(11, (int)InputButton.SoftLockPad);
            Assert.AreEqual(12, (int)InputButton.QuickMoves);

            var seen = new HashSet<int>();
            foreach (InputButton b in Enum.GetValues(typeof(InputButton)))
                Assert.IsTrue(seen.Add((int)b), $"{b} reuses a bit.");
        }

        [Test]
        public void GameplayAndTelegraph_NeverReadDevices_TheProvidersDo()
        {
            foreach (string file in new[]
            {
                "Assets/_Game/Scripts/Gameplay/AbilityController.cs",
                "Assets/_Game/Scripts/Gameplay/ChickenController.cs",
                "Assets/_Game/Scripts/Gameplay/ChickenMovement.cs",
                "Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs",
                "Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs",
            })
            {
                string src = Read(file);
                StringAssert.DoesNotContain("Mouse.current", src, file);
                StringAssert.DoesNotContain("Gamepad.current", src, file);
                StringAssert.DoesNotContain("Camera.main", src, file);
                StringAssert.DoesNotContain("Keyboard.current", src, file);
            }
        }

        [Test]
        public void FireTick_AppliesAimBeforeTargetsResolve_AndComposeIsWired()
        {
            string ctl = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int aim = ctl.IndexOf("ApplyAimForFire(decision.Slot, input);", StringComparison.Ordinal);
            int cast = ctl.IndexOf("TryActivate(decision.Slot);", aim, StringComparison.Ordinal);
            Assert.Greater(aim, 0, "The Fire case must apply the aim.");
            Assert.Greater(cast, aim, "Aim is applied before TryActivate resolves targets.");
            StringAssert.Contains("quickCast", ctl);

            StringAssert.Contains("new GamepadInputProvider()", Read("Assets/_Game/Scripts/Installers/ProjectInstaller.cs"));

            string net = Read("Assets/_Game/Scripts/App/FusionNetworkService.cs");
                        StringAssert.Contains("InputButton.QuickMoves", net);
            StringAssert.Contains("InputButton.SoftLockTouch", net);
            StringAssert.Contains("InputButton.SoftLockPad", net);
        }

        [Test]
        public void QuickMovesCopy_IsInUiTextAndKeys()
        {
            string csv = Read("Assets/_Game/Resources/Text/UiText.csv");
            StringAssert.Contains("settings.quickMoves,Quick Moves", csv);
            StringAssert.Contains("settings.quickMoves.desc,Moves fire the moment you tap.", csv);
            Assert.AreEqual("settings.quickMoves", CluckWars.Localization.UiKeys.SettingsQuickMoves);
            Assert.AreEqual("settings.quickMoves.desc", CluckWars.Localization.UiKeys.SettingsQuickMovesDesc);
        }

        /// <summary>
        /// Maestro: holding an ability previews its range and the chicken keeps moving. The hold used to turn the stick into
        /// facing for directional abilities, so the bird stopped dead. Only bots' own turn-in-place may suppress translation.
        /// </summary>
        [Test]
        public void HoldingAnAbility_NeverStopsThePlayersMovement_SourceGuard()
        {
            string fun = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/ChickenController.cs", "public override void FixedUpdateNetwork()");
            StringAssert.DoesNotContain("aiming != null, lockFacing", fun, "a charge must not suppress translation");
            StringAssert.Contains("_movement.Tick(input.Movement, Runner.DeltaTime,", fun, "the stick always walks the bird");
            StringAssert.Contains("faceOverride:", fun, "an explicit aim turns the bird without moving it");
        }
    }
}
