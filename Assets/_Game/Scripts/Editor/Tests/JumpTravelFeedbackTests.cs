using System;
using System.IO;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the jump travel catch-up and its landing impact — the cosmetic layer that makes a
    /// length-based teleport jump (Dive Bomb, Belly Flop, Doppelganger) read as travel with weight
    /// instead of as a one-frame pop.
    /// </summary>
    /// <remarks>
    /// The effect itself cannot be exercised here: it needs a live <c>NetworkRunner</c>, an
    /// interpolating <c>NetworkTransform</c> and a rendered frame, none of which EditMode has.
    /// What EditMode <em>can</em> hold onto are the four structural claims the whole thing rests
    /// on, each of which fails silently if it is broken:
    ///
    /// <list type="number">
    ///   <item><b>The discriminator is intact.</b> The catch-up is measured from a position
    ///   discontinuity, and <c>RPC_TeleportTo</c> produces one on every chicken at every round
    ///   reset. If anything ever bumps <c>JumpEventId</c> from the teleport path, chickens skate
    ///   across the arena at round boundaries — a bug that surfaces at the one moment nobody is
    ///   watching closely.</item>
    ///   <item><b>The settle is one number.</b> The model's lead-in and the landing ring's delay
    ///   are the same duration by design; copied literals let them drift and the ring starts
    ///   expanding under a chicken that has not arrived.</item>
    ///   <item><b>Reduced motion is honoured on both halves.</b> The motion-sensitive audience
    ///   plays fine on today's teleport; both new beats must be suppressible.</item>
    ///   <item><b>The single-write rule survives.</b> The travel is composed into the two existing
    ///   writes in <c>ChickenAnimator</c>. A third assignment anywhere means two effects fight and
    ///   the last one silently wins.</item>
    /// </list>
    ///
    /// Source-scanned rather than reflected, for the same reason
    /// <c>RpcHardeningTests.VerticalVelocity_IsNetworkedOnTheController</c> is: Fusion's ILWeaver
    /// rewrites <c>[Networked]</c> property bodies, so the declaration in source is the honest
    /// thing to assert on.
    /// </remarks>
    public sealed class JumpTravelFeedbackTests
    {
        private const string ChickenControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const string AbilityControllerPath = "Assets/_Game/Scripts/Gameplay/AbilityController.cs";
        private const string ChickenAnimatorPath   = "Assets/_Game/Scripts/Visuals/ChickenAnimator.cs";
        private const string ControlStateVfxPath   = "Assets/_Game/Scripts/Visuals/ControlStateVFX.cs";

        private static string ReadSource(string path)
        {
            Assert.IsTrue(File.Exists(path), $"{path} not found on disk.");
            return File.ReadAllText(path);
        }

        private static int CountOf(string haystack, string needle)
        {
            int count = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal);
                 i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }

        // ---- 1. The discriminator -------------------------------------------------

        [Test]
        public void JumpEventId_IsANetworkedByteOnTheController()
        {
            string source = ReadSource(ChickenControllerPath);

            Assert.IsTrue(source.Contains("[Networked] public byte JumpEventId"),
                "ChickenController.JumpEventId is no longer a [Networked] byte. It has to cross " +
                "the wire: the travel catch-up and the landing ring are local VFX played on every " +
                "peer, and this id is the only thing that tells a proxy the jump happened. As a " +
                "plain field it would fire on the caster's machine alone.");
        }

        [Test]
        public void JumpEventId_IsBumpedOnlyByTheJumpPath()
        {
            // The teleport path lives in ChickenController alongside the declaration, so proving
            // the file bumps nothing proves RPC_TeleportTo bumps nothing.
            string controller = ReadSource(ChickenControllerPath);
            Assert.AreEqual(0, CountOf(controller, "JumpEventId++"),
                "ChickenController now bumps JumpEventId itself. RPC_TeleportTo runs on every " +
                "chicken at every round reset (GameManager.RestartMatch); a bump there makes the " +
                "model ease back toward the corner it just left, which reads as the chicken " +
                "skating across the arena. Only AbilityController.ExecuteJumpIfAny may publish it.");

            string abilities = ReadSource(AbilityControllerPath);
            Assert.AreEqual(1, CountOf(abilities, "JumpEventId++"),
                "AbilityController must bump JumpEventId exactly once, in ExecuteJumpIfAny — the " +
                "sole publisher. A second site means some other movement is masquerading as a jump.");
        }

        [Test]
        public void JumpEventId_IsNotBumpedForAJumpThatWentNowhere()
        {
            string source = ReadSource(AbilityControllerPath);

            Assert.IsTrue(source.Contains("if (jump.EffectiveDistance > 0f) _controller.JumpEventId++;"),
                "ExecuteJumpIfAny no longer gates the JumpEventId bump on the resolved distance. " +
                "A jump pressed flat against a wall resolves to zero travel; announcing a landing " +
                "impact for it advertises a movement that did not happen.");
        }

        // ---- 2. One settle, shared ------------------------------------------------

        [Test]
        public void TheTravelSettle_IsReadFromTuning_ByBothHalves()
        {
            Assert.IsTrue(ReadSource(ChickenAnimatorPath).Contains("FeedbackTuning.JumpTravelSettleSeconds"),
                "ChickenAnimator no longer reads the shared settle constant. The model's lead-in " +
                "and the landing ring's delay are the same duration by design.");

            Assert.IsTrue(ReadSource(ControlStateVfxPath).Contains("FeedbackTuning.JumpTravelSettleSeconds"),
                "ControlStateVFX no longer delays the jump landing ring by the shared settle. " +
                "Fired on the event edge, the ring expands under a chicken still mid-travel and " +
                "reads as belonging to something else.");
        }

        [Test]
        public void TheFullArcDistance_TracksTheShortestJumpInTheLadder()
        {
            // Derived, not copied — see FeedbackTuning.JumpTravelFullArcDistance. The traversal
            // tiers have silently decoupled from the map once already (JumpResolver's x0.65
            // rescale of 2026-08-20); the arc must not be able to repeat that.
            Assert.AreEqual(JumpResolver.ShortDistance, FeedbackTuning.JumpTravelFullArcDistance, 1e-6f,
                "FeedbackTuning.JumpTravelFullArcDistance has drifted from JumpResolver.ShortDistance. " +
                "The shortest jump in the game is the one that must earn a full-strength arc; a " +
                "hardcoded copy here goes stale the next time the ladder is rescaled.");

            Assert.Greater(FeedbackTuning.JumpTravelMaxResidual, JumpResolver.BigDistance,
                "The residual clamp is now below the longest jump, so a Big-tier travel would be " +
                "clipped. It is a safety rail against resimulation snaps, not a limit on real jumps.");

            Assert.Greater(FeedbackTuning.JumpTravelArcHeight, 0f,
                "A zero arc height silently removes the vertical half of the travel while leaving " +
                "every other moving part in place, which looks like the effect is broken rather " +
                "than switched off.");
        }

        // ---- 3. Reduced motion, both halves ---------------------------------------

        [Test]
        public void BothNewMotionBeats_HonourReducedMotion()
        {
            Assert.IsTrue(ReadSource(ChickenAnimatorPath).Contains("PlayerPreferences.ReducedMotionEnabled"),
                "ChickenAnimator's jump travel no longer checks ReducedMotionEnabled. " +
                "PlayerPreferences' own remarks name travel arcs and landing impacts as the case " +
                "this preference is expected to cover; an unconditional arc regresses players who " +
                "are fine on the shipped teleport.");

            Assert.IsTrue(ReadSource(ControlStateVfxPath).Contains("PlayerPreferences.ReducedMotionEnabled"),
                "ControlStateVFX's jump landing ring no longer checks ReducedMotionEnabled.");
        }

        // ---- 4. The single-write rule ---------------------------------------------

        [Test]
        public void ChickenAnimator_StillWritesTheModelPositionExactlyTwice()
        {
            string source = ReadSource(ChickenAnimatorPath);

            Assert.AreEqual(2, CountOf(source, "_modelRoot.localPosition ="),
                "ChickenAnimator must assign the model's localPosition exactly twice — once at " +
                "the bottom of the skeletal path, once at the bottom of the legacy path. The " +
                "travel offset is composed INTO those writes, not assigned separately: the moment " +
                "two effects each assign, they fight and the last one silently wins.");

            // The call sites, not the declaration — hence the leading '+ '.
            Assert.AreEqual(2, CountOf(source, "+ CurrentTravelOffset();"),
                "The travel offset must be composed into both write paths. Applying it only on " +
                "the skeletal path leaves a chicken whose rig failed to import teleporting while " +
                "every other chicken travels — the degraded mode should degrade articulation, not " +
                "silently change how movement reads.");
        }
    }
}
