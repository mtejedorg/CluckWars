using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Stage 1 of the ability-feedback system (docs/FEEDBACK.md §4/§8): the aim
    /// descriptor is the single source of truth for "what does this ability cover
    /// and who does it hit," used by the preview, the usability gate, and
    /// <c>OnActivate</c> alike. These tests lock the pure geometry
    /// (<see cref="AbilityAim"/>), the descriptor↔tuned-field parity on the real
    /// shipped assets, the completeness of the per-ability declarations, and the
    /// "no ability re-implements its own scan" regression the whole stage exists to
    /// fix.
    /// </summary>
    public sealed class AbilityAimTests
    {
        // ---- Shared helpers ------------------------------------------------

        /// <summary>Every concrete ability subclass compiled into the game assembly. Mirrors AbilitySystemTests' helper — kept local so this file has no cross-file coupling.</summary>
        private static List<Type> ConcreteAbilityTypes() =>
            typeof(AbilityBaseSO).Assembly
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(AbilityBaseSO)) && !t.IsAbstract)
                .ToList();

        /// <summary>
        /// A bare, never-<c>Spawned()</c> <see cref="ChickenController"/> — enough to
        /// read <c>transform</c>, <c>IsDecoy</c> (defaults false) and <c>Combat</c>
        /// (defaults null, which <c>WouldAffect</c> treats as "alive"). Verified
        /// empirically via Unity MCP script-execute: AddComponent alone (its
        /// RequireComponent chain pulls in NetworkObject + CharacterController) does
        /// not throw and logs nothing outside a NetworkRunner, because Spawned()
        /// never runs. This deliberately stays inside AbilityAimTests rather than
        /// TestAssets — AbilitySystemTests' own doc comment notes real activation
        /// (physics scans, RPCs) needs a NetworkRunner and stays out of EditMode; this
        /// probe only reads plain properties, never activates anything.
        /// </summary>
        private static ChickenController NewProbeChicken(Vector3 position, Vector3 forward)
        {
            var go = new GameObject("AbilityAimTests_ProbeChicken");
            go.transform.position = position;
            go.transform.rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
            return go.AddComponent<ChickenController>();
        }

        private static void Destroy(ChickenController c)
        {
            if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        /// <summary>
        /// Publishes probe chickens to <see cref="ChickenController.ActiveControllers"/> —
        /// the registry <c>GatherTargets</c> scans. Normally filled by <c>Spawned()</c>,
        /// which never runs here, so a test that needs <c>GatherTargets</c> to see anything
        /// has to populate it by hand. ALWAYS paired with <see cref="Unregister"/> in a
        /// <c>finally</c>: the list is a static, so a leaked entry would poison every later
        /// test in the run.
        /// </summary>
        private static void Register(params ChickenController[] chickens)
        {
            foreach (var c in chickens) ChickenController.ActiveControllers.Add(c);
        }

        private static void Unregister(params ChickenController[] chickens)
        {
            foreach (var c in chickens) ChickenController.ActiveControllers.Remove(c);
        }

        // ---- Pure geometry: AbilityAim.InShape -----------------------------

        [Test]
        public void None_AlwaysFalse()
        {
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.None, Vector3.zero, Vector3.forward, Vector3.zero, 100f, 0f, 360f),
                "None must never report a candidate as inside, even at the caster's own position with a huge radius.");
        }

        [Test]
        public void SelfCircle_Inside_OnBoundary_Outside()
        {
            var caster = new Vector3(0f, 1f, 0f);
            const float radius = 3f;

            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.SelfCircle, caster, Vector3.forward, caster + new Vector3(2f, 0f, 0f), radius, 0f, 360f), "inside");
            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.SelfCircle, caster, Vector3.forward, caster + new Vector3(radius, 0f, 0f), radius, 0f, 360f), "exactly on the boundary must count as inside (<=)");
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.SelfCircle, caster, Vector3.forward, caster + new Vector3(radius + 0.01f, 0f, 0f), radius, 0f, 360f), "outside");
        }

        [Test]
        public void Aura_And_SingleTarget_MatchSelfCircleContainment()
        {
            var caster = Vector3.zero;
            var forward = Vector3.forward;
            const float radius = 4f;

            foreach (var candidate in new[] { new Vector3(1f, 0f, 1f), new Vector3(4f, 0f, 0f), new Vector3(5f, 0f, 0f) })
            {
                bool selfCircle = AbilityAim.InShape(AbilityAimShape.SelfCircle, caster, forward, candidate, radius, 0f, 360f);
                bool aura = AbilityAim.InShape(AbilityAimShape.Aura, caster, forward, candidate, radius, 0f, 360f);
                bool single = AbilityAim.InShape(AbilityAimShape.SingleTarget, caster, forward, candidate, radius, 0f, 360f);

                Assert.AreEqual(selfCircle, aura, $"Aura must have the same containment as SelfCircle at {candidate}.");
                Assert.AreEqual(selfCircle, single, $"SingleTarget must have the same containment as SelfCircle at {candidate}.");
            }
        }

        [Test]
        public void ForwardCircle_Inside_OnBoundary_Outside_CentreOffsetAlongForward()
        {
            var caster = Vector3.zero;
            var forward = Vector3.forward;
            const float offset = 2f;
            const float radius = 1.5f;
            var center = caster + forward * offset; // (0,0,2)

            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.ForwardCircle, caster, forward, center, radius, offset, 360f), "dead centre of the offset circle");
            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.ForwardCircle, caster, forward, center + new Vector3(radius, 0f, 0f), radius, offset, 360f), "on the boundary");
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.ForwardCircle, caster, forward, center + new Vector3(radius + 0.01f, 0f, 0f), radius, offset, 360f), "just outside");

            // The caster's own position is NOT inside a forward-offset circle whose
            // offset exceeds its radius — this is what makes ForwardCircle directional.
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.ForwardCircle, caster, forward, caster, radius, offset, 360f),
                "caster stands outside their own forward-offset zone when offset > radius");
        }

        [Test]
        public void Jump_MatchesForwardCircleContainment()
        {
            var caster = Vector3.zero;
            var forward = Vector3.forward;
            const float offset = 5f;
            const float radius = 1f;

            foreach (var candidate in new[] { caster + forward * offset, caster + forward * offset + new Vector3(0.9f, 0f, 0f), caster })
            {
                bool forwardCircle = AbilityAim.InShape(AbilityAimShape.ForwardCircle, caster, forward, candidate, radius, offset, 360f);
                bool jump = AbilityAim.InShape(AbilityAimShape.Jump, caster, forward, candidate, radius, offset, 360f);
                Assert.AreEqual(forwardCircle, jump, $"Jump must share ForwardCircle's containment at {candidate}.");
            }
        }

        [Test]
        public void Cone120_AcceptsDeadAhead_RejectsBehind_RespectsRadius()
        {
            var caster = Vector3.zero;
            var forward = Vector3.forward; // +Z
            const float radius = 5f;
            const float coneAngle = 120f; // half-angle 60°

            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.Cone, caster, forward, new Vector3(0f, 0f, 3f), radius, 0f, coneAngle), "dead ahead, in range");
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.Cone, caster, forward, new Vector3(0f, 0f, -3f), radius, 0f, coneAngle), "directly behind — outside a 120° forward cone");

            // Just inside the half-angle boundary (60° off dead-ahead) must count as
            // inside. Placed a hair under 60° rather than exactly on it — Vector3.Angle
            // round-trips through acos/cos and can land a float epsilon over 60° for
            // an angle constructed via sin/cos, which InShape's <= would (correctly)
            // reject; that is a floating-point artifact of the test's own construction,
            // not a boundary-inclusiveness bug in InShape (SelfCircle's <= boundary
            // case is covered exactly, with exact arithmetic, in another test).
            float justInside = (coneAngle * 0.5f - 0.01f) * Mathf.Deg2Rad;
            var onBoundary = caster + new Vector3(Mathf.Sin(justInside), 0f, Mathf.Cos(justInside)) * 3f;
            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.Cone, caster, forward, onBoundary, radius, 0f, coneAngle), "just inside the 60° half-angle boundary");

            // Just past the boundary is rejected.
            float justPast = (coneAngle * 0.5f + 5f) * Mathf.Deg2Rad;
            var pastBoundary = caster + new Vector3(Mathf.Sin(justPast), 0f, Mathf.Cos(justPast)) * 3f;
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.Cone, caster, forward, pastBoundary, radius, 0f, coneAngle), "just past the half-angle boundary");

            // In the right direction but past the radius.
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.Cone, caster, forward, new Vector3(0f, 0f, radius + 0.5f), radius, 0f, coneAngle), "dead ahead but outside the radius");
        }

        [Test]
        public void Cone360_DegeneratesExactlyToSelfCircle()
        {
            // This is the invariant Ambush relies on: it inherits Cone from
            // StunBurstAbilitySO and never narrows AimConeAngle away from 360.
            var caster = new Vector3(1f, 0f, -2f);
            var forward = new Vector3(1f, 0f, 1f);
            const float radius = 3f;

            var candidates = new[]
            {
                caster + new Vector3(0.5f, 0f, 0.5f),
                caster - forward.normalized * radius, // directly behind — must still be inside at 360°
                caster + new Vector3(radius, 0f, 0f),
                caster + new Vector3(radius + 0.01f, 0f, 0f),
            };

            foreach (var c in candidates)
            {
                bool cone360 = AbilityAim.InShape(AbilityAimShape.Cone, caster, forward, c, radius, 0f, 360f);
                bool selfCircle = AbilityAim.InShape(AbilityAimShape.SelfCircle, caster, forward, c, radius, 0f, 360f);
                Assert.AreEqual(selfCircle, cone360, $"360° cone must equal SelfCircle at {c}.");
            }
        }

        // ---- Capsule (Roll Push, Flying Peck) -------------------------------

        [Test]
        public void Capsule_Inside_OnBoundary_Outside()
        {
            var caster = new Vector3(0f, 1f, 0f);
            var forward = Vector3.forward;   // +Z
            const float length = 5f;         // AimForwardOffset = axis length
            const float radius = 1.15f;

            // Probe perpendicular containment at the near cap, the middle, and the far cap.
            foreach (float t in new[] { 0f, length * 0.5f, length })
            {
                var onAxis = caster + forward * t;

                Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, onAxis, radius, length, 360f),
                    $"dead on the axis at t={t}");
                Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, onAxis + new Vector3(radius, 0f, 0f), radius, length, 360f),
                    $"exactly on the boundary at t={t} must count as inside (<=)");
                Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, onAxis + new Vector3(radius + 0.01f, 0f, 0f), radius, length, 360f),
                    $"just outside the sweep radius at t={t}");
            }

            // Past the far cap along the axis: reach is length + radius, no further.
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, caster + forward * (length + radius + 0.01f), radius, length, 360f),
                "beyond the far cap");
        }

        [Test]
        public void Capsule_NearCap_IsHemispherical()
        {
            // The deliberate difference from ForwardCircle, and the reason Roll Push stops
            // whiffing point-blank: the near cap is round, so it reaches slightly BEHIND the
            // caster. A ForwardCircle centred 4 m ahead leaves a hole everywhere closer.
            var caster = Vector3.zero;
            var forward = Vector3.forward;
            const float length = 4f;
            const float radius = 1.05f;

            var behind = caster - forward * (radius * 0.5f);
            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, behind, radius, length, 360f),
                "a chicken standing just behind the caster is inside the near cap");

            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.ForwardCircle, caster, forward, behind, radius, length, 360f),
                "sanity: the same point is NOT inside the equivalent ForwardCircle — that gap is the bug the capsule fixes");
        }

        [Test]
        public void Capsule_ZeroLength_DegeneratesExactlyToSelfCircle()
        {
            // Mirrors Cone360_DegeneratesExactlyToSelfCircle across the same probe set: a
            // zero-length axis collapses to the caster's own position.
            var caster = new Vector3(1f, 0f, -2f);
            var forward = new Vector3(1f, 0f, 1f);
            const float radius = 3f;

            var candidates = new[]
            {
                caster + new Vector3(0.5f, 0f, 0.5f),
                caster - forward.normalized * radius, // directly behind — still inside a circle
                caster + new Vector3(radius, 0f, 0f),
                caster + new Vector3(radius + 0.01f, 0f, 0f),
            };

            foreach (var c in candidates)
            {
                bool capsule    = AbilityAim.InShape(AbilityAimShape.Capsule,    caster, forward, c, radius, 0f, 360f);
                bool selfCircle = AbilityAim.InShape(AbilityAimShape.SelfCircle, caster, forward, c, radius, 0f, 360f);
                Assert.AreEqual(selfCircle, capsule, $"zero-length capsule must equal SelfCircle at {c}.");
            }
        }

        [Test]
        public void Capsule_NegativeForwardOffset_ClampsToZero()
        {
            // Clamps, never mirrors: a backward capsule is never what an author meant, and
            // silently drawing one would put the preview behind the player.
            var caster = Vector3.zero;
            var forward = Vector3.forward;
            const float radius = 1f;

            var ahead  = caster + forward * 3f;
            var behind = caster - forward * 3f;

            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, behind, radius, -5f, 360f),
                "a negative offset must NOT mirror the capsule backwards");
            Assert.IsFalse(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, ahead, radius, -5f, 360f),
                "nor extend it forwards");
            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.Capsule, caster, forward, caster, radius, -5f, 360f),
                "it collapses to the zero-length case, i.e. a circle on the caster");
        }

        [Test]
        public void ShapeCenter_Capsule_IsAxisMidpoint()
        {
            var caster = new Vector3(2f, 1f, -1f);
            var forward = Vector3.forward;
            const float length = 5f;

            Assert.AreEqual(caster + forward * (length * 0.5f),
                AbilityAim.ShapeCenter(AbilityAimShape.Capsule, caster, forward, length),
                "the capsule's decal centre AND GatherTargets' distance-sort origin is the axis midpoint, not the far end.");

            Assert.AreEqual(caster, AbilityAim.ShapeCenter(AbilityAimShape.Capsule, caster, forward, -5f),
                "a negative offset clamps here exactly as it does in InShape.");
        }

        [Test]
        public void Cone_CandidateExactlyAtCasterPosition_CountsAsInside()
        {
            var caster = new Vector3(4f, 0f, 4f);
            Assert.IsTrue(AbilityAim.InShape(AbilityAimShape.Cone, caster, Vector3.forward, caster, 5f, 0f, 30f),
                "a candidate with no direction to measure an angle against must not be silently excluded (or NaN out).");
        }

        [Test]
        public void YOffset_NeverAffectsTheResult()
        {
            var caster = new Vector3(0f, 0f, 0f);
            var forward = Vector3.forward;
            const float radius = 2f;
            var candidateGroundLevel = new Vector3(1f, 0f, 1f);

            foreach (float y in new[] { -50f, -1f, 0f, 1f, 50f })
            {
                var candidate = new Vector3(candidateGroundLevel.x, y, candidateGroundLevel.z);
                foreach (var shape in new[] { AbilityAimShape.SelfCircle, AbilityAimShape.ForwardCircle, AbilityAimShape.Cone, AbilityAimShape.Aura, AbilityAimShape.Jump, AbilityAimShape.SingleTarget, AbilityAimShape.Capsule })
                {
                    bool atGround = AbilityAim.InShape(shape, caster, forward, candidateGroundLevel, radius, 1f, 90f);
                    bool atY = AbilityAim.InShape(shape, caster, forward, candidate, radius, 1f, 90f);
                    Assert.AreEqual(atGround, atY, $"{shape}: Y={y} changed the result — distance tests must be planar (XZ only).");
                }
            }
        }

        [Test]
        public void ZeroLengthForward_FallsBackToWorldForward_NoThrowNoNaN()
        {
            var caster = Vector3.zero;
            bool result = false;
            Assert.DoesNotThrow(() =>
                result = AbilityAim.InShape(AbilityAimShape.Cone, caster, Vector3.zero, new Vector3(0f, 0f, 2f), 5f, 0f, 90f));
            Assert.IsTrue(result, "a zero-length caster forward should fall back to Vector3.forward, which puts (0,0,2) dead ahead.");

            var center = AbilityAim.ShapeCenter(AbilityAimShape.ForwardCircle, caster, Vector3.zero, 3f);
            Assert.IsFalse(float.IsNaN(center.x) || float.IsNaN(center.y) || float.IsNaN(center.z), "ShapeCenter must never NaN on a zero-length forward.");
            Assert.AreEqual(new Vector3(0f, 0f, 3f), center, "falls back to world forward (0,0,1) * offset.");
        }

        [Test]
        public void NegativeRadius_ReturnsFalse()
        {
            foreach (var shape in (AbilityAimShape[])Enum.GetValues(typeof(AbilityAimShape)))
            {
                if (shape == AbilityAimShape.None) continue;
                Assert.IsFalse(AbilityAim.InShape(shape, Vector3.zero, Vector3.forward, Vector3.zero, -1f, 0f, 90f),
                    $"{shape}: negative radius must return false, not throw or match everything.");
            }
        }

        [Test]
        public void ShapeCenter_ForwardCircleAndJump_OffsetAlongForward()
        {
            var caster = new Vector3(2f, 1f, -1f);
            var forward = new Vector3(0f, 0f, 1f);
            const float offset = 4f;

            var expected = caster + forward * offset;
            Assert.AreEqual(expected, AbilityAim.ShapeCenter(AbilityAimShape.ForwardCircle, caster, forward, offset));
            Assert.AreEqual(expected, AbilityAim.ShapeCenter(AbilityAimShape.Jump, caster, forward, offset));
        }

        [Test]
        public void ShapeCenter_NonOffsetShapes_EqualCasterPosition()
        {
            var caster = new Vector3(-3f, 0.5f, 7f);
            var forward = Vector3.right;
            const float offset = 10f; // must be ignored for these shapes

            // Capsule is deliberately absent: its centre IS offset-dependent (the axis
            // midpoint) — see ShapeCenter_Capsule_IsAxisMidpoint.
            foreach (var shape in new[] { AbilityAimShape.None, AbilityAimShape.SelfCircle, AbilityAimShape.Aura, AbilityAimShape.Cone, AbilityAimShape.SingleTarget })
            {
                Assert.AreEqual(caster, AbilityAim.ShapeCenter(shape, caster, forward, offset), $"{shape}: centre must be the caster's own position, ignoring forward offset.");
            }
        }

        // ---- Descriptor↔tuned-field parity (real shipped assets) ----------

        [Test]
        public void AimRadius_MatchesTheAbilitysOwnTunedRadiusField()
        {
            const float tol = 0.0001f;
            foreach (var a in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
            {
                switch (a)
                {
                    case CluckShockAbilitySO x: Assert.AreEqual(x.ShockRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror ShockRadius."); break;
                    // Covers Ambush and Wing Slam too — both are StunBurstAbilitySO subclasses.
                    case StunBurstAbilitySO x: Assert.AreEqual(x.StunRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror StunRadius."); break;
                    case SnatchAbilitySO x: Assert.AreEqual(x.SnatchRange, x.AimRadius, tol, $"{a.name}: AimRadius must mirror SnatchRange."); break;
                    case SneakyStealAbilitySO x: Assert.AreEqual(x.StealRange, x.AimRadius, tol, $"{a.name}: AimRadius must mirror StealRange."); break;
                    case RollPushAbilitySO x: Assert.AreEqual(x.PushRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror PushRadius."); break;
                    case RollTrampleAbilitySO x: Assert.AreEqual(x.SweepRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror SweepRadius."); break;
                    case FeatherTrapAbilitySO x: Assert.AreEqual(x.ZoneRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror ZoneRadius."); break;
                    case RootEggAbilitySO x: Assert.AreEqual(x.EggRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror EggRadius."); break;
                    case FeatherAuraAbilitySO x: Assert.AreEqual(x.AuraRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror AuraRadius."); break;
                    case MarkKillAbilitySO x: Assert.AreEqual(AssassinExecute.MaxMarkRange, x.AimRadius, tol, $"{a.name}: AimRadius must mirror AssassinExecute.MaxMarkRange."); break;
                    case ShadowstepAbilitySO x: Assert.AreEqual(AbilityBaseSO.JumpLandingRadius, x.AimRadius, tol, $"{a.name}: AimRadius must mirror the shared JumpLandingRadius."); break;
                }

                if (a.AimShape != AbilityAimShape.None)
                {
                    Assert.Greater(a.AimRadius, 0f,
                        $"{a.name}: AimShape is {a.AimShape} but AimRadius is {a.AimRadius} — the preview would draw a zero-size (invisible) area.");
                }
            }
        }

        [Test]
        public void AimForwardOffset_MirrorsTheAbilitysOwnTunedOffsetField()
        {
            const float tol = 0.0001f;
            foreach (var a in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
            {
                switch (a)
                {
                    case RollPushAbilitySO x: Assert.AreEqual(x.ForwardOffset, x.AimForwardOffset, tol, $"{a.name}"); break;
                    case RollTrampleAbilitySO x: Assert.AreEqual(x.ForwardOffset, x.AimForwardOffset, tol, $"{a.name}"); break;
                    case FeatherTrapAbilitySO x: Assert.AreEqual(x.ForwardOffset, x.AimForwardOffset, tol, $"{a.name}"); break;
                    case RootEggAbilitySO x: Assert.AreEqual(0f, x.AimForwardOffset, tol, $"{a.name}: Root Egg spawns at the caster's own feet — no offset."); break;
                }

                // A capsule reads AimForwardOffset as its AXIS LENGTH, not as a detached
                // centre. A negative length would clamp to 0 and silently collapse the lane
                // into a circle on the caster; a zero radius would make it a zero-width line.
                if (a.AimShape == AbilityAimShape.Capsule)
                {
                    Assert.GreaterOrEqual(a.AimForwardOffset, 0f,
                        $"{a.name}: a Capsule's AimForwardOffset is its axis length and must not be negative — " +
                        "it would clamp to 0 and degenerate the lane to a SelfCircle.");
                    Assert.Greater(a.AimRadius, 0f,
                        $"{a.name}: a Capsule's AimRadius is its sweep half-width and must be positive.");
                }
            }
        }

        // ---- Single-target resolution (GatherTargets, not WouldAffect) ------

        [Test]
        public void SingleTarget_GatherTargets_ResolvesToExactlyTheNearestCandidate()
        {
            // The bug: WouldAffect returned true for every eligible rival in range, so the
            // telegraph marked three and OnActivate acted on _scratch[0] — one. GatherTargets
            // now owns the resolution, so the marks and the hit agree.
            //
            // Mark/Kill is the subject because it is the SingleTarget ability whose filter can
            // be satisfied by bare probe chickens. The positions are chosen so BOTH candidates
            // stay isolated: they are 9 m apart (> the 8 m IsolationRadius) while both sit
            // inside the caster's 8 m mark range.
            var probe = ScriptableObject.CreateInstance<MarkKillAbilitySO>();
            var caster = NewProbeChicken(Vector3.zero, Vector3.forward);
            var near   = NewProbeChicken(new Vector3(4f, 0f, 0f), Vector3.forward);
            var far    = NewProbeChicken(new Vector3(-5f, 0f, 0f), Vector3.forward);
            var buffer = new List<ChickenController>();

            try
            {
                Register(caster, near, far);

                Assert.IsTrue(probe.WouldAffect(caster, near), "precondition: the near candidate is eligible");
                Assert.IsTrue(probe.WouldAffect(caster, far),  "precondition: the far candidate is eligible too — eligibility is not selection");

                int count = probe.GatherTargets(caster, buffer);
                Assert.AreEqual(1, count, "a SingleTarget ability must resolve to exactly one target even with several eligible candidates.");
                Assert.AreSame(near, buffer[0], "and it must be the nearest one.");

                Assert.IsTrue(probe.HasAnyTarget(caster),
                    "HasAnyTarget stays an eligibility question — the button is usable whenever ANY candidate qualifies.");
            }
            finally
            {
                Unregister(caster, near, far);
                Destroy(near); Destroy(far); Destroy(caster);
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void EverySingleTargetAbility_NeverResolvesMoreThanOneTarget()
        {
            // Reflective sweep, same shape as the completeness test: whatever an ability's
            // ExtraTargetFilter does, declaring SingleTarget is a promise that at most one
            // chicken comes back — which is what the telegraph now marks Valid.
            var caster = NewProbeChicken(Vector3.zero, Vector3.forward);
            var candidates = new List<ChickenController>();
            var buffer = new List<ChickenController>();

            try
            {
                foreach (var pos in new[] { new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 1.5f) })
                    candidates.Add(NewProbeChicken(pos, Vector3.forward));

                Register(caster);
                Register(candidates.ToArray());

                foreach (var type in ConcreteAbilityTypes())
                {
                    var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                    try
                    {
                        if (probe.AimShape != AbilityAimShape.SingleTarget) continue;

                        int count = probe.GatherTargets(caster, buffer);
                        Assert.LessOrEqual(count, 1,
                            $"{type.Name} declares AimShape.SingleTarget but GatherTargets returned {count} targets. " +
                            "The telegraph marks every gathered chicken Valid, so this would light up rivals the cast cannot hit.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(probe); }
                }
            }
            finally
            {
                Unregister(candidates.ToArray());
                Unregister(caster);
                foreach (var c in candidates) Destroy(c);
                Destroy(caster);
            }
        }

        // ---- Decoys are valid targets (Maestro's call) ----------------------

        [Test]
        public void Decoys_AreValidTargets()
        {
            // Reversal of the old "a decoy is never a valid target" rule (docs/STATE.md,
            // Stage 1 behaviour change #2). A Doppelganger whose decoy could not be marked or
            // hit had no reason to exist — the attacker has to be able to commit, burn the
            // cooldown, and get nothing of value. Nothing about match state depends on this
            // guard: kill credit, scoring, cargo, deposits and player counts all keep their
            // own IsDecoy checks at the systems that own them.
            var probe = ScriptableObject.CreateInstance<CluckShockAbilitySO>();
            var caster = NewProbeChicken(Vector3.zero, Vector3.forward);
            var decoy  = NewProbeChicken(new Vector3(0.5f, 0f, 0.5f), Vector3.forward);

            try
            {
                decoy.IsDecoy = true;

                Assert.IsTrue(probe.IsInAimShape(caster, decoy), "precondition: the decoy is inside the shape");
                Assert.IsTrue(probe.WouldAffect(caster, decoy),
                    "a decoy standing inside an ability's aim shape must be a valid target — that is the whole point of the deception.");
            }
            finally
            {
                Destroy(decoy); Destroy(caster);
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        // ---- Shadowstep's impact styling ------------------------------------

        [Test]
        public void Shadowstep_AlwaysReportsItsOwnCasterAsTheHit()
        {
            // Shadowstep is a pure mobility cast but declares a real Jump aim shape (the
            // landing ring the player needs to see), so ReportsCastHits is true and it WILL be
            // styled as a hit or a whiff either way. It used to inherit AffectsEnemies = true
            // and count whoever happened to stand in the ring — a phantom hit. Flipping only
            // that would have produced a phantom MISS on every cast instead. Marking the
            // caster makes the count honest at exactly 1.
            //
            // This is also the canary for the asset: at JumpTier None the landing ring is
            // centred on the caster, so they are inside their own shape. Raising JumpTier in
            // the Inspector would push the ring away and silently drop the count to 0.
            var shadowstep = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                .OfType<ShadowstepAbilitySO>()
                .FirstOrDefault();
            Assert.IsNotNull(shadowstep, "Shadowstep.asset not found — this test pins the shipped asset, not just the class.");

            Assert.IsFalse(shadowstep.AffectsEnemies,
                "Shadowstep touches nobody; counting rivals in its landing ring styles a blink as a landed attack.");
            Assert.IsTrue(shadowstep.AffectsSelf,
                "…and with AffectsSelf false as well it would report 0 on every cast and be whiff-styled forever.");
            Assert.IsTrue(shadowstep.ReportsCastHits,
                "precondition: a Jump shape is not exempt from hit/whiff styling, which is why the count has to be honest.");

            var caster = NewProbeChicken(Vector3.zero, Vector3.forward);
            var rival  = NewProbeChicken(new Vector3(0.4f, 0f, 0.4f), Vector3.forward); // inside the 1 m landing ring
            var buffer = new List<ChickenController>();
            try
            {
                Register(caster, rival);

                Assert.AreEqual(1, shadowstep.GatherTargets(caster, buffer),
                    "Shadowstep must report exactly its own caster. 0 means the landing ring no longer covers the " +
                    "caster (check the asset's JumpTier); 2 means AffectsEnemies came back.");
                Assert.AreSame(caster, buffer[0], "and the one target must be the caster, not the rival standing in the ring.");
            }
            finally
            {
                Unregister(caster, rival);
                Destroy(rival); Destroy(caster);
            }
        }

        // ---- Completeness ---------------------------------------------------

        [Test]
        public void EveryOffensiveAbility_DeclaresItsOwnAimShape()
        {
            // Self-buffs (FEEDBACK.md §4's "None" row) mark the caster's own ring
            // instead of an area, so they are allowed to keep AimShape.None. Passives
            // (PassiveAbilitySO) are a different subsystem entirely — permanent,
            // never OnActivate-target-scanning, driven by ModifyOutgoingDamage /
            // ModifyIncomingDamage / OnTraversalTick instead (see PassiveAbilitySO's
            // own doc comment) — so they are allow-listed by base type rather than by
            // name. Everything else declaring an area must say so explicitly, or the
            // hold-to-aim preview (FEEDBACK.md §2) silently draws nothing for it.
            var selfBuffAllowList = new HashSet<string>
            {
                nameof(SpeedBurstAbilitySO),
                nameof(TurtleModeAbilitySO),
                nameof(InvisibilityAbilitySO),
                nameof(SpineCoatAbilitySO),
                nameof(EggShellAbilitySO),
                nameof(DoppelgangerAbilitySO),
                // Peck is not a self-buff, but it belongs here for the same reason: its
                // target is a FoodPile, and the aim-shape system describes areas that
                // contain CHICKENS. There is no rival to preview, mark, or whiff against,
                // so AimShape.None is the honest answer rather than a missing override.
                nameof(PeckAbilitySO),
            };

            var offenders = new List<string>();
            foreach (var type in ConcreteAbilityTypes())
            {
                if (selfBuffAllowList.Contains(type.Name)) continue;
                if (typeof(PassiveAbilitySO).IsAssignableFrom(type)) continue;

                var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                try
                {
                    if (probe.AimShape == AbilityAimShape.None) offenders.Add(type.Name);
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }

            Assert.IsEmpty(offenders,
                "These ability types silently inherit AimShape.None. If genuinely a self-buff with no " +
                "target area, add it to the allow-list in this test; otherwise give it a real AimShape " +
                "override or the hold-to-aim preview will draw nothing for it.");
        }

        // ---- Hit/whiff eligibility (AbilityBaseSO.ReportsCastHits) ----------

        [Test]
        public void ReportsCastHits_IsFalse_ForExactlySelfBuffsAndPlacedZones()
        {
            // AbilityController.TryActivate snapshots LastCastHitCount via GatherTargets at
            // the instant of the cast, and two families always come back 0 there for reasons
            // that have nothing to do with missing:
            //   * self-buffs — GatherTargets only returns candidates inside an aim shape,
            //     and AimShape.None has no shape;
            //   * placed zones — Root Egg / Feather Trap put a zone down and the real hit
            //     happens later, in AbilityZone's own tick. Root Egg is the sharp case: it
            //     spawns at the caster's feet with AffectsSelf false, so on open ground it
            //     reported 0 on EVERY cast and got whiff-styled every time.
            // ReportsCastHits is the single predicate every whiff-vs-hit consumer gates on,
            // so this test pins the exact membership of both buckets. A new ability that
            // silently lands in the wrong one fails here rather than in a live session.
            var expectedExempt = new HashSet<string>
            {
                // Self-buffs (mirrors EveryOffensiveAbility_DeclaresItsOwnAimShape's list).
                nameof(SpeedBurstAbilitySO),
                nameof(TurtleModeAbilitySO),
                nameof(InvisibilityAbilitySO),
                nameof(SpineCoatAbilitySO),
                nameof(EggShellAbilitySO),
                nameof(DoppelgangerAbilitySO),
                // Placed zones.
                nameof(FeatherTrapAbilitySO),
                nameof(RootEggAbilitySO),
                // Peck resolves against a pile, never a chicken, so GatherTargets is
                // always 0 for it. Whiff styling would mark every successful forage as a
                // miss - the same failure Root Egg had before it was exempted.
                nameof(PeckAbilitySO),
            };

            var wronglyExempt = new List<string>();
            var wronglyReporting = new List<string>();

            foreach (var type in ConcreteAbilityTypes())
            {
                // Passives are a different subsystem — never activated, never target-scanned.
                if (typeof(PassiveAbilitySO).IsAssignableFrom(type)) continue;

                var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                try
                {
                    bool shouldBeExempt = expectedExempt.Contains(type.Name);
                    if (shouldBeExempt && probe.ReportsCastHits) wronglyReporting.Add(type.Name);
                    if (!shouldBeExempt && !probe.ReportsCastHits) wronglyExempt.Add(type.Name);
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }

            Assert.IsEmpty(wronglyReporting,
                "These abilities cannot produce a meaningful cast-time hit count (self-buff or placed zone) " +
                "but ReportsCastHits says they can — they will be styled as a whiff on every successful use.");
            Assert.IsEmpty(wronglyExempt,
                "These abilities resolve a real hit at cast time but ReportsCastHits exempts them from " +
                "whiff styling — a genuine miss would be silently dressed up as a hit. If one of them really " +
                "did become a self-buff or a placed zone, add it to this test's expected-exempt set.");
        }

        [Test]
        public void PlacedZoneAbilities_DeclarePlacesZone()
        {
            // The two halves of ReportsCastHits must stay independently true: Root Egg and
            // Feather Trap are exempt because they PLACE something, not because they have no
            // shape (they both declare a real ForwardCircle, which is why they never hit the
            // AimShape.None exemption in the first place — the original bug).
            foreach (var type in new[] { typeof(FeatherTrapAbilitySO), typeof(RootEggAbilitySO) })
            {
                var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                try
                {
                    Assert.IsTrue(probe.PlacesZone, $"{type.Name} must declare PlacesZone.");
                    Assert.AreNotEqual(AbilityAimShape.None, probe.AimShape,
                        $"{type.Name} draws a real zone footprint, so it must keep a real AimShape — " +
                        "its whiff exemption has to come from PlacesZone, not from having no shape.");
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }
        }

        // ---- WouldAffect / IsInAimShape invariant ---------------------------

        [Test]
        public void WouldAffect_AlwaysImplies_IsInAimShape()
        {
            // Stage 3's telegraph relies on this: it never marks a "will be hit"
            // bracket on a candidate that isn't even geometrically inside the shape.
            // WouldAffect is implemented as IsInAimShape AND (alive/decoy/side/extra
            // filters), so the implication holds by construction — this test is the
            // regression lock, not the only proof.
            var caster = NewProbeChicken(Vector3.zero, Vector3.forward);
            var candidates = new List<ChickenController>();
            try
            {
                foreach (var pos in new[]
                         {
                             Vector3.zero,
                             new Vector3(1f, 0f, 0f),
                             new Vector3(0f, 0f, 5f),
                             new Vector3(-3f, 2f, -3f), // off-axis + Y offset
                             new Vector3(20f, 0f, 20f), // far outside every shape
                         })
                {
                    candidates.Add(NewProbeChicken(pos, Vector3.forward));
                }

                foreach (var type in ConcreteAbilityTypes())
                {
                    var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                    try
                    {
                        foreach (var candidate in candidates)
                        {
                            bool would = probe.WouldAffect(caster, candidate);
                            bool inShape = probe.IsInAimShape(caster, candidate);
                            if (would)
                            {
                                Assert.IsTrue(inShape,
                                    $"{type.Name}: WouldAffect(caster, candidate@{candidate.transform.position}) was true " +
                                    "but IsInAimShape was false — the preview could mark a target that OnActivate can't reach.");
                            }
                        }

                        bool wouldSelf = probe.WouldAffect(caster, caster);
                        if (wouldSelf)
                        {
                            Assert.IsTrue(probe.IsInAimShape(caster, caster),
                                $"{type.Name}: WouldAffect(caster, caster) was true but IsInAimShape(caster, caster) was false.");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(probe); }
                }
            }
            finally
            {
                foreach (var c in candidates) Destroy(c);
                Destroy(caster);
            }
        }

        // ---- No stray scans (regression lock for the whole stage) ---------

        /// <summary>
        /// Scope is deliberately the set of files that resolve "which chicken does this
        /// ability touch": everything under <c>Scripts/Abilities/</c>, plus two
        /// <c>Gameplay/</c> files that belong to the same contract even though they live
        /// elsewhere —
        /// <list type="bullet">
        ///   <item><c>AbilityZone.cs</c>, a placed ability's delayed hit. It was invisible to
        ///   this lock until Stage 1's follow-up, and was indeed still scanning collider
        ///   bounds while <c>AbilityZoneVisuals</c> drew its edge ring at
        ///   <c>TriggerRadius</c>.</item>
        ///   <item><c>AssassinExecute.cs</c>, Mark/Kill's execute state machine. It ran two
        ///   separate <c>Physics.OverlapSphereNonAlloc</c> queries — a 3D candidate search
        ///   and a 3D isolation check — against a preview that was planar and knew nothing
        ///   about isolation, so the telegraph could mark a target the press then refused.
        ///   Both now route through <c>ChickenController.ActiveControllers</c>, and
        ///   <c>MarkKillAbilitySO.ExtraTargetFilter</c> applies the identical rules via the
        ///   shared static <c>AssassinExecute.IsIsolated</c>.</item>
        /// </list>
        ///
        /// NOT widened to all of <c>Gameplay/</c> on purpose. <c>ChickenCargo</c> (food
        /// piles, bases), <c>ChickenController</c> (body collision) and
        /// <c>ChickenTraversal</c> query real colliders for non-chicken-targeting reasons and
        /// are legitimately outside this contract. Blanket-failing them would turn this lock
        /// into noise.
        /// </summary>
        [Test]
        public void NoAbilityScript_CallsPhysicsOverlapDirectly()
        {
            const string abilitiesDir = "Assets/_Game/Scripts/Abilities";
            var gameplayFiles = new[]
            {
                "Assets/_Game/Scripts/Gameplay/AbilityZone.cs",
                "Assets/_Game/Scripts/Gameplay/AssassinExecute.cs",
            };

            Assert.IsTrue(Directory.Exists(abilitiesDir), $"{abilitiesDir} not found on disk.");
            foreach (var f in gameplayFiles)
            {
                Assert.IsTrue(File.Exists(f),
                    $"{f} not found on disk — if it moved, move this lock with it rather than dropping it.");
            }

            // Matches an actual call site: Physics.OverlapSphere(...), OverlapBox(...),
            // OverlapSphereNonAlloc(...), and so on.
            var callSite = new System.Text.RegularExpressions.Regex(@"Physics\.Overlap\w*\s*\(");

            // Comment lines are skipped before matching. Every file covered here
            // deliberately explains in prose which physics scan it replaced and why, and
            // that prose names the API — an earlier version of this lock tried to tell the
            // two apart by requiring a trailing '(', which held right up until a comment
            // wrote "...its own Physics.OverlapSphere (in 3D, against a preview that was
            // planar...)". A parenthetical is ordinary English, so the heuristic was always
            // going to break; matching only real code is the honest fix. Line-level
            // stripping is enough because this codebase's explanatory prose is all '//' or
            // '///' doc comments — no call site has ever been written inside a /* */ block.
            static bool IsCommentLine(string line)
            {
                string t = line.TrimStart();
                return t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*");
            }

            var covered = new List<string>(Directory.GetFiles(abilitiesDir, "*.cs", SearchOption.AllDirectories));
            covered.AddRange(gameplayFiles);

            var offenders = new List<string>();
            foreach (var file in covered)
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (IsCommentLine(line) || !callSite.IsMatch(line)) continue;
                    offenders.Add(Path.GetFileName(file));
                    break;
                }
            }

            Assert.IsEmpty(offenders,
                "These scripts still call Physics.Overlap* directly instead of routing through the " +
                "ChickenController.ActiveControllers registry with a planar-XZ distance test — the single " +
                "target scan the preview, the usability gate, OnActivate, a placed zone's delayed hit and " +
                "Mark/Kill's execute must all agree on (FEEDBACK.md §4/§8). Covered here: everything under " +
                "Assets/_Game/Scripts/Abilities/ plus Gameplay/AbilityZone.cs and Gameplay/AssassinExecute.cs. " +
                "Other Gameplay/ files (ChickenCargo, ChickenController, ChickenTraversal) are out of scope " +
                "by design — see this test's doc comment.");
        }
    }
}
