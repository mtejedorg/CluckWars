using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A09 (architecture audit 2026-11-12): a knockback that belongs to a stun or a steal of the same cast names its
    /// caster and is judged like that effect (sender, reach, base wall), so both land or neither does.
    /// </summary>
    public sealed class KnockbackClaimTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const string AbilityDir = "Assets/_Game/Scripts/Abilities";

        private static ControlClaimRules.Claim Source(bool resolved = true, bool self = false, bool decoy = false,
            bool speaksFor = true, float distance = 2f) =>
            new ControlClaimRules.Claim
            {
                Kind = ControlKind.Stun,
                Seconds = 0.35f,
                SlowFactor = 1f,
                CasterResolved = resolved,
                CasterIsVictim = self,
                CasterIsDecoy = decoy,
                SenderSpeaksForCaster = speaksFor,
                PlanarDistance = distance,
            };

        /// <summary>The file without its comment lines (a doc comment may name the old RPC).</summary>
        private static string Code(string path) =>
            string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//")));

        private static readonly ControlClaimRules.Ceiling Stun = new ControlClaimRules.Ceiling(true, 2f, 1f, 4f);

        [Test]
        public void EvaluateSource_IgnoresTheMagnitude_ItHasNone()
        {
            var claim = Source();
            claim.Seconds = 0f;   // a knockback carries no duration
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleDuration, ControlClaimRules.Evaluate(claim, Stun));
            Assert.AreEqual(ControlClaimRules.Verdict.Accepted, ControlClaimRules.EvaluateSource(claim, Stun));
        }

        [Test]
        public void EvaluateSource_RefusesWhatEvaluateRefuses_ForTheSameReason()
        {
            Assert.AreEqual(ControlClaimRules.Verdict.CasterUnresolved, ControlClaimRules.EvaluateSource(Source(resolved: false), Stun));
            Assert.AreEqual(ControlClaimRules.Verdict.SelfApplied, ControlClaimRules.EvaluateSource(Source(self: true), Stun));
            Assert.AreEqual(ControlClaimRules.Verdict.DecoyCaster, ControlClaimRules.EvaluateSource(Source(decoy: true), Stun));
            Assert.AreEqual(ControlClaimRules.Verdict.NotAttributable, ControlClaimRules.EvaluateSource(Source(speaksFor: false), Stun));
            Assert.AreEqual(ControlClaimRules.Verdict.OutOfRange, ControlClaimRules.EvaluateSource(Source(distance: 40f), Stun));
        }

        [Test]
        public void AnHonestStun_AndItsKnockback_GetTheSameVerdict_OnEverySourceFact()
        {
            // Both or neither: with a plausible duration the stun's verdict is the knockback's, whatever the source facts.
            foreach (bool resolved in new[] { true, false })
            foreach (bool self in new[] { true, false })
            foreach (bool decoy in new[] { true, false })
            foreach (bool speaksFor in new[] { true, false })
            foreach (float distance in new[] { 0f, 3.9f, 4f, 6f, 40f })
            {
                var claim = Source(resolved, self, decoy, speaksFor, distance);
                Assert.AreEqual(ControlClaimRules.Evaluate(claim, Stun), ControlClaimRules.EvaluateSource(claim, Stun),
                    $"resolved={resolved} self={self} decoy={decoy} speaksFor={speaksFor} distance={distance}");
            }
        }

        [Test]
        public void KnockbackCeiling_ReachesAsFarAsTheLongestAbility_AndNeverLessThanTheStun()
        {
            var pool = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath).All;
            var knockback = ControlClaimRules.KnockbackCeiling(pool);
            Assert.IsTrue(knockback.Known);
            Assert.AreEqual(pool.Where(a => a != null).Max(ControlClaimRules.Reach), knockback.MaxReach, 1e-4f);
            Assert.GreaterOrEqual(knockback.MaxReach, ControlClaimRules.CeilingFor(pool, ControlKind.Stun).MaxReach,
                "a knockback bound tighter than the stun's would refuse the shove of a stun that landed");
            Assert.GreaterOrEqual(knockback.MaxReach, StealRules.MaxReach(pool),
                "a knockback bound tighter than the steal's would refuse the shove of a steal that landed");
            Assert.IsFalse(ControlClaimRules.KnockbackCeiling(null).Known, "no pool, no bound: sender check only");
        }

        [Test]
        public void TheValidatedRpc_JudgesBeforeItShoves()
        {
            string body = AssassinExecuteCreditTests.MethodBody(ControllerPath, "public void RPC_ApplyKnockbackFrom(");
            int judge = body.IndexOf("AcceptControlClaim(nameof(RPC_ApplyKnockbackFrom)", System.StringComparison.Ordinal);
            int shove = body.IndexOf("ApplyKnockback(impulse)", System.StringComparison.Ordinal);
            Assert.Greater(judge, 0, "the knockback is no longer judged");
            Assert.Greater(shove, judge, "the claim is judged before the impulse is applied");
            StringAssert.Contains("RpcInfo info", body);

            string accept = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private bool AcceptControlClaim(");
            StringAssert.Contains("CrossedBaseWall(kind, caster)", accept, "the same wall answer the stun gets");
        }

        [TestCase("HeadbuttAbilitySO.cs")]
        [TestCase("BellyFlopAbilitySO.cs")]
        [TestCase("SnatchAbilitySO.cs")]
        public void PairedKnockback_NamesItsCaster(string file)
        {
            string src = Code(Path.Combine(AbilityDir, file));
            StringAssert.Contains(".RPC_ApplyKnockbackFrom(", src, $"{file}: its knockback rides another effect and must be judged with it");
            StringAssert.DoesNotContain(".RPC_ApplyKnockback(", src, $"{file}: the unvalidated knockback lands across a base wall its other half is refused at");
        }

        [Test]
        public void SpineCoatContact_NamesItsWearer()
        {
            string body = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void CheckCollisionSlow()");
            StringAssert.Contains("other.RPC_ApplyKnockbackFrom(", body);
            StringAssert.DoesNotContain("other.RPC_ApplyKnockback(", body);
        }
    }
}
