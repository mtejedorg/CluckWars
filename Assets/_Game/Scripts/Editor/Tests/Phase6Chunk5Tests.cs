using System;
using System.Collections.Generic;
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
    /// Phase 6 chunk 5: the aim byte (A7), right-stick shaping, touch / pad magnetism, the gamepad binding table and
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

        // ---- Aim quantiser ---------------------------------------------------------------------

        [Test]
        public void Aim_ZeroIsReserved_NoDirectionEverEncodesToIt()
        {
            Assert.AreEqual(0, AimQuantizer.Encode(Vector2.zero), "No aim encodes as 0.");
            Assert.AreEqual(Vector2.zero, AimQuantizer.Decode(0));

            for (int deg = 0; deg < 3600; deg++)
            {
                float rad = deg * 0.1f * Mathf.Deg2Rad;
                byte b = AimQuantizer.Encode(new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)));
                Assert.GreaterOrEqual(b, 1, $"{deg * 0.1f} degrees encoded as the reserved 0.");
            }
        }

        [Test]
        public void Aim_RoundTrip_ErrorIsAtMostHalfAStep()
        {
            float worst = 0f;
            for (int deg = 0; deg < 7200; deg++)
            {
                float rad = deg * 0.05f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                float err = Vector2.Angle(dir, AimQuantizer.Decode(AimQuantizer.Encode(dir)));
                worst = Mathf.Max(worst, err);
            }
            Assert.LessOrEqual(worst, AimQuantizer.StepDegrees * 0.5f + 0.01f,
                $"Worst round-trip error {worst:0.000} deg exceeds half a step ({AimQuantizer.StepDegrees * 0.5f:0.000}).");
        }

        [Test]
        public void Aim_CardinalDirections_AreExactAndSnapIsIdempotent()
        {
            // 0 deg is +Z (north), 90 deg is +X: the same convention the movement vector uses (x, z).
            Assert.AreEqual(1, AimQuantizer.Encode(new Vector2(0f, 1f)));
            Vector2 north = AimQuantizer.Decode(1);
            Assert.AreEqual(0f, north.x, 1e-5f);
            Assert.AreEqual(1f, north.y, 1e-5f);

            var d = new Vector2(0.3f, -0.8f).normalized;
            var once = AimQuantizer.Snap(d);
            var twice = AimQuantizer.Snap(once);
            Assert.AreEqual(AimQuantizer.Encode(once), AimQuantizer.Encode(twice));
            Assert.AreEqual(1f, once.magnitude, 1e-4f);
        }

        [Test]
        public void Aim_EveryByteDecodesToADistinctUnitVector()
        {
            var seen = new HashSet<byte>();
            for (int b = 1; b <= 255; b++)
            {
                var v = AimQuantizer.Decode((byte)b);
                Assert.AreEqual(1f, v.magnitude, 1e-4f);
                Assert.AreEqual((byte)b, AimQuantizer.Encode(v), $"Byte {b} must be its own fixed point.");
                Assert.IsTrue(seen.Add(AimQuantizer.Encode(v)));
            }
            Assert.AreEqual(255, seen.Count);
        }

        // ---- Right stick -----------------------------------------------------------------------

        [Test]
        public void RightStick_InsideDeadZone_IsNoAim()
        {
            var f = new StickAimFilter();
            Assert.AreEqual(Vector2.zero, f.Update(new Vector2(0.24f, 0f), 1f));
            Assert.AreEqual(Vector2.zero, f.Update(new Vector2(0f, -0.25f), 2f), "The dead zone edge itself is still dead.");
        }

        [Test]
        public void RightStick_OuterZone_RescalesToFullAtNinetyFivePercent()
        {
            var f = new StickAimFilter();
            Assert.AreEqual(1f, f.Update(new Vector2(0.95f, 0f), 1f).magnitude, 1e-4f, "0.95 reads as full deflection.");
            Assert.AreEqual(1f, f.Update(new Vector2(1f, 0f), 1.1f).magnitude, 1e-4f, "Past 0.95 is clamped.");
            // Halfway up the ramp: (0.6 - 0.25) / (0.95 - 0.25) = 0.5.
            Assert.AreEqual(0.5f, f.Update(new Vector2(0f, 0.6f), 1.2f).magnitude, 1e-4f);
            // Direction is preserved.
            var v = f.Update(new Vector2(-0.5f, 0f), 1.3f);
            Assert.Less(v.x, 0f);
            Assert.AreEqual(0f, v.y, 1e-5f);
        }

        [Test]
        public void RightStick_HoldsLastAimFor150ms_ThenDrops()
        {
            var f = new StickAimFilter();
            var live = f.Update(new Vector2(0f, 1f), 10f);
            Assert.AreNotEqual(Vector2.zero, live);

            Assert.AreEqual(live, f.Update(Vector2.zero, 10.10f), "100 ms after release the aim is still held.");
            Assert.AreEqual(live, f.Update(Vector2.zero, 10.15f), "Exactly 150 ms is still inside the hold.");
            Assert.AreEqual(Vector2.zero, f.Update(Vector2.zero, 10.16f), "After 150 ms it drops to no aim.");
            Assert.AreEqual(Vector2.zero, f.Update(Vector2.zero, 10.10f + 5f), "And stays dropped.");
        }

        [Test]
        public void RightStick_HoldRestartsWhenTheStickMovesAgain_AndIsIdempotentPerTime()
        {
            var f = new StickAimFilter();
            f.Update(new Vector2(1f, 0f), 1f);
            var held = f.Update(Vector2.zero, 1.05f);
            Assert.AreEqual(held, f.Update(Vector2.zero, 1.05f), "Reading twice at the same time changes nothing.");

            f.Update(new Vector2(0f, 1f), 1.10f);
            Assert.AreNotEqual(Vector2.zero, f.Update(Vector2.zero, 1.20f), "A fresh deflection restarts the 150 ms.");
            Assert.AreEqual(Vector2.zero, f.Update(Vector2.zero, 1.30f));
        }

        // ---- Mouse and stick resolution -------------------------------------------------------------

        [Test]
        public void Mouse_UnderHalfAMetreFromTheBird_IsFacing()
        {
            var bird = new Vector3(3f, 0f, 4f);
            Assert.AreEqual(Vector2.zero, AimRules.Resolve(AimInput.FromGroundPoint(bird + new Vector3(0.49f, 0f, 0f)), bird, 0f));
            var far = AimRules.Resolve(AimInput.FromGroundPoint(bird + new Vector3(0f, 0f, 2f)), bird, 0f);
            Assert.AreEqual(0f, far.x, 1e-5f);
            Assert.AreEqual(1f, far.y, 1e-5f);
        }

        [Test]
        public void Aim_None_ResolvesToFacing()
        {
            Assert.AreEqual(Vector2.zero, AimRules.Resolve(AimInput.None, Vector3.zero, 45f));
            Assert.AreEqual(Vector2.zero, AimRules.Resolve(AimInput.FromStick(Vector2.zero), Vector3.zero, 45f));
        }

        [Test]
        public void Stick_AimUsesTheSameCameraRotationAsMovement()
        {
            // Camera yaw 0: stick up is world +Z.
            var up = AimRules.Resolve(AimInput.FromStick(new Vector2(0f, 1f)), Vector3.zero, 0f);
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

        [Test]
        public void Ray_HitsTheGroundPlane_AtTheBirdsHeight()
        {
            var ray = new Ray(new Vector3(0f, 10f, 0f), new Vector3(0f, -1f, 1f).normalized);
            Assert.IsTrue(AimRules.TryRayToGround(ray, 2f, out var hit));
            Assert.AreEqual(2f, hit.y, 1e-4f);
            Assert.AreEqual(8f, hit.z, 1e-3f);

            Assert.IsFalse(AimRules.TryRayToGround(new Ray(Vector3.zero, Vector3.right), 0f, out _), "A level ray never hits.");
            Assert.IsFalse(AimRules.TryRayToGround(new Ray(Vector3.zero, Vector3.up), -5f, out _), "A plane behind the ray is a miss.");
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
            foreach (AbilityBaseSO single in new AbilityBaseSO[] { Make<MarkKillAbilitySO>(), Make<SneakyStealAbilitySO>() })
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
        public void Gamepad_AbilityTable_IsTotal_RB_RT_LB_LT()
        {
            CollectionAssert.AreEqual(
                new[] { GamepadButton.RightShoulder, GamepadButton.RightTrigger, GamepadButton.LeftShoulder, GamepadButton.LeftTrigger },
                GamepadInputProvider.AbilityButtons);
            Assert.AreEqual(AbilityController.SlotCount, GamepadInputProvider.AbilityButtons.Length, "One button per slot.");
        }

        [Test]
        public void Gamepad_NoButtonDrivesTwoThings()
        {
            var seen = new HashSet<GamepadButton>();
            foreach (var b in GamepadInputProvider.AbilityButtons)
                Assert.IsTrue(seen.Add(b), $"{b} is bound to two ability slots.");

            Assert.IsFalse(seen.Contains(GamepadInputProvider.CancelButton), "B must not also fire an ability.");
            Assert.IsFalse(seen.Contains(GamepadInputProvider.BackButton), "Start must not also fire an ability.");
            Assert.AreNotEqual(GamepadInputProvider.CancelButton, GamepadInputProvider.BackButton);
            Assert.AreEqual(GamepadButton.East, GamepadInputProvider.CancelButton, "B is East.");
            Assert.AreEqual(GamepadButton.Start, GamepadInputProvider.BackButton);

            foreach (var face in new[] { GamepadButton.South, GamepadButton.West, GamepadButton.North })
                Assert.IsFalse(seen.Contains(face), $"{face} fights the right stick and must stay unbound.");
        }

        // ---- Composite -----------------------------------------------------------------------------------

        private sealed class FakeDevice : IInputProvider
        {
            public InputDeviceKind Kind;
            public float Active = float.NegativeInfinity;
            public AimInput Aim = AimInput.None;
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
            public AimInput GetAim(float groundY) => Aim;
        }

        [Test]
        public void Composite_AimComesFromTheMostRecentlyUsedDeviceThatHasOne()
        {
            var mouse = new FakeDevice { Kind = InputDeviceKind.KeyboardMouse, Active = 5f, Aim = AimInput.FromGroundPoint(new Vector3(1, 0, 1)) };
            var pad = new FakeDevice { Kind = InputDeviceKind.Gamepad, Active = 9f, Aim = AimInput.FromStick(Vector2.up) };
            Assert.AreEqual(AimInputKind.Direction, new CompositeInputProvider(mouse, pad).GetAim(0f).Kind, "The pad was used last.");

            pad.Active = 2f;
            Assert.AreEqual(AimInputKind.GroundPoint, new CompositeInputProvider(mouse, pad).GetAim(0f).Kind, "Now the mouse was.");

            pad.Active = 99f;
            pad.Aim = AimInput.None;
            Assert.AreEqual(AimInputKind.GroundPoint, new CompositeInputProvider(mouse, pad).GetAim(0f).Kind,
                "A device with no aim never hides another's.");

            mouse.Aim = AimInput.None;
            Assert.AreEqual(AimInputKind.None, new CompositeInputProvider(mouse, pad).GetAim(0f).Kind);
            Assert.AreEqual(AimInputKind.None, new CompositeInputProvider().GetAim(0f).Kind);
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
        public void ProvidersWithoutAim_NeedNoCode_DefaultMembersMeanNoAimNoDevice()
        {
            IInputProvider bare = new BareProvider();
            Assert.AreEqual(AimInputKind.None, bare.GetAim(0f).Kind);
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

            string net = Read("Assets/_Game/Scripts/Networking/FusionNetworkService.cs");
            StringAssert.Contains("Aim = aim", net);
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
    }
}
