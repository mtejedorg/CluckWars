using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The shared "affected" test of the hit-steal and the debts (design 2026-11-25, 16.2): the game predicts it on the caster, the simulator
    /// calls it in one tick, and both must agree with what the victim's own RPC will decide.
    /// </summary>
    public sealed class HitAffectRulesTests
    {
        [Test]
        public void Stun_AffectsOnlyWhenItExtends_NeverAnImmuneVictim()
        {
            Assert.IsTrue(HitAffectRules.StunAffects(false, 0f, 1f), "a free victim is stunned");
            Assert.IsTrue(HitAffectRules.StunAffects(false, 0.5f, 1f), "a longer stun extends a shorter one");
            Assert.IsFalse(HitAffectRules.StunAffects(false, 1f, 1f), "an equal stun does not extend");
            Assert.IsFalse(HitAffectRules.StunAffects(false, 2f, 1f), "a shorter stun does not cut a longer one");
            Assert.IsFalse(HitAffectRules.StunAffects(false, 0f, 0f), "a stun the victim's passive shortened to nothing did not affect him");
            Assert.IsFalse(HitAffectRules.StunAffects(false, 0f, -1f), "never a negative stun");
            Assert.IsFalse(HitAffectRules.StunAffects(false, 0f, float.NaN), "NaN is no stun");
            Assert.IsFalse(HitAffectRules.StunAffects(true, 0f, 5f), "an immune victim is never affected");
        }

        [Test]
        public void Shove_MovesOnlyAboveZeroAfterTheFactor()
        {
            Assert.IsTrue(HitAffectRules.ShoveMoves(false, 5f, 1f));
            Assert.IsTrue(HitAffectRules.ShoveMoves(false, 5f, 0.1f), "a heavy victim still moves");
            Assert.IsFalse(HitAffectRules.ShoveMoves(false, 5f, 0f), "an immovable victim");
            Assert.IsFalse(HitAffectRules.ShoveMoves(false, 5f, -1f), "a negative factor reads as 0");
            Assert.IsFalse(HitAffectRules.ShoveMoves(false, 0f, 1f), "no strength moves nobody");
            Assert.IsFalse(HitAffectRules.ShoveMoves(true, 5f, 1f), "an immune victim is never moved");
        }

        [Test]
        public void AgreesWithTheVictimsRpcPrimitives()
        {
            // RPC_ApplyStun: seconds -> ApplyPassiveControlDuration (0 when immune, else the passive's factor) -> IncomingExtends(StunRemaining, seconds).
            foreach (bool immune in new[] { false, true })
            foreach (float left in new[] { 0f, 0.4f, 1f, 3f })
            foreach (float sent in new[] { 0f, 0.4f, 1f, 1.3f, 3f })
            foreach (float factor in new[] { 0f, 0.5f, 1f, 1.5f })
            {
                float afterPassive = immune ? 0f : sent * factor;
                bool rpc = ControlReapplyRules.IncomingExtends(left, afterPassive);
                // The caller passes the immune flag and the seconds after the passive had its say (not the immune-zeroed ones).
                Assert.AreEqual(rpc, HitAffectRules.StunAffects(immune, left, sent * factor),
                    $"stun: immune={immune}, left={left}, sent={sent}, factor={factor}");
            }

            // ApplyKnockback: an immune victim returns at once, else the impulse is scaled by max(0, factor); it moves when something is left.
            foreach (bool immune in new[] { false, true })
            foreach (float strength in new[] { 0f, 1f, 5f })
            foreach (float factor in new[] { -1f, 0f, 0.5f, 1f })
            {
                float impulse = immune ? 0f : strength * Mathf.Max(0f, factor);
                Assert.AreEqual(impulse > 0f, HitAffectRules.ShoveMoves(immune, strength, factor),
                    $"shove: immune={immune}, strength={strength}, factor={factor}");
            }
        }

        [Test]
        public void StunAffects_GoesThroughTheSharedReapplyRule()
        {
            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_Game/Scripts/Gameplay/HitAffectRules.cs"));
            StringAssert.Contains("ControlReapplyRules.IncomingExtends(", source, "the prediction must call the rule the victim's RPC calls, not restate it.");
        }
    }
}
