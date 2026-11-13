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

        // ---- A19: the impulse is bounded, and no knockback is sent without a caster --------------------------------

        private static ControlClaimRules.Ceiling Knock(float maxImpulse, bool known = true) =>
            new ControlClaimRules.Ceiling(known, 0f, 1f, 6f, maxImpulse: maxImpulse);

        [Test]
        public void Impulse_UpToTheStrongestShoveInThePool_IsPlausible_AnythingStrongerIsNot()
        {
            var c = Knock(16.2f);
            Assert.IsTrue(ControlClaimRules.IsPlausibleImpulse(new Vector3(16.2f, 0f, 0f), c), "exactly the authored strength");
            Assert.IsTrue(ControlClaimRules.IsPlausibleImpulse(new Vector3(1f, 0f, 1f).normalized * 16.2f, c), "a normalized direction times the strength");
            Assert.IsTrue(ControlClaimRules.IsPlausibleImpulse(new Vector3(0f, 0f, -7.5f), c), "a weaker shove of the same pool");
            Assert.IsTrue(ControlClaimRules.IsPlausibleImpulse(Vector3.zero, c));
            Assert.IsFalse(ControlClaimRules.IsPlausibleImpulse(new Vector3(16.3f, 0f, 0f), c), "0.6% over is outside the float tolerance");
            Assert.IsFalse(ControlClaimRules.IsPlausibleImpulse(new Vector3(0f, 500f, 0f), c), "a launch into the sky");
            Assert.IsFalse(ControlClaimRules.IsPlausibleImpulse(new Vector3(float.NaN, 0f, 0f), c));
            Assert.IsFalse(ControlClaimRules.IsPlausibleImpulse(new Vector3(float.PositiveInfinity, 0f, 0f), c));
        }

        [Test]
        public void Impulse_WithNoShoveInThePool_IsAlwaysRefused_AndWithNoPool_OnlyFinitenessIsChecked()
        {
            Assert.IsFalse(ControlClaimRules.IsPlausibleImpulse(new Vector3(1f, 0f, 0f), Knock(0f)), "nothing in the pool shoves");
            Assert.IsTrue(ControlClaimRules.IsPlausibleImpulse(new Vector3(900f, 0f, 0f), Knock(0f, known: false)), "no honest bound");
            Assert.IsFalse(ControlClaimRules.IsPlausibleImpulse(new Vector3(float.NaN, 0f, 0f), Knock(0f, known: false)));
        }

        [Test]
        public void EvaluateKnockback_ChecksTheImpulseFirst_ThenTheSource()
        {
            var c = Knock(16.2f);
            var honest = new Vector3(16.2f, 0f, 0f);
            Assert.AreEqual(ControlClaimRules.Verdict.Accepted, ControlClaimRules.EvaluateKnockback(Source(), honest, c));
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleImpulse,
                ControlClaimRules.EvaluateKnockback(Source(), honest * 2f, c));
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleImpulse,
                ControlClaimRules.EvaluateKnockback(Source(speaksFor: false), honest * 2f, c), "magnitude first, as Evaluate does");
            // With a plausible impulse the verdict is the source's, fact for fact.
            foreach (bool resolved in new[] { true, false })
            foreach (bool self in new[] { true, false })
            foreach (bool decoy in new[] { true, false })
            foreach (bool speaksFor in new[] { true, false })
            foreach (float distance in new[] { 0f, 5.9f, 40f })
            {
                var claim = Source(resolved, self, decoy, speaksFor, distance);
                Assert.AreEqual(ControlClaimRules.EvaluateSource(claim, c), ControlClaimRules.EvaluateKnockback(claim, honest, c));
            }
            StringAssert.Contains(16.2f.ToString("0.00"), ControlClaimRules.Describe(ControlClaimRules.Verdict.ImplausibleImpulse, ControlKind.None, c));
        }

        [Test]
        public void KnockbackCeiling_IsTheStrongestDeclaredShove_OfTheShippedPool()
        {
            var pool = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath).All;
            var knockback = ControlClaimRules.KnockbackCeiling(pool);
            float strongest = pool.Where(a => a != null).Max(a => a.EmittedKnockback);
            Assert.AreEqual(strongest, knockback.MaxImpulse, 0f, "derived from the registry, no tunable of its own");
            Assert.Greater(knockback.MaxImpulse, 0f, "the shipped pool has a Shove; a zero bound would refuse it");
            // Every shove a registered ability can send passes its own pool's bound.
            foreach (var ability in pool.Where(a => a != null && a.EmittedKnockback > 0f))
                Assert.IsTrue(ControlClaimRules.IsPlausibleImpulse(new Vector3(0.6f, 0f, 0.8f) * ability.EmittedKnockback, knockback), ability.name);
        }

        [Test]
        public void EveryAbilityThatSendsAKnockback_DeclaresItsStrength_FromItsOwnField()
        {
            var senders = Directory.GetFiles(AbilityDir, "*AbilitySO.cs")
                .Where(p => Code(p).Contains(".KnockAway("))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "BellyFlopAbilitySO", "CluckShockAbilitySO", "HeadbuttAbilitySO", "RollPushAbilitySO", "SnatchAbilitySO" }, senders,
                "a new knockback sender: add its EmittedKnockback override and list it here");

            // The contact shove is sent by the controller for the Spine Coat's wearer.
            var declared = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                .Where(a => a.GetType().GetProperty(nameof(AbilityBaseSO.EmittedKnockback)).DeclaringType == a.GetType())
                .Select(a => a.GetType().Name).Distinct().OrderBy(n => n).ToArray();
            CollectionAssert.AreEquivalent(senders.Concat(new[] { "SpineCoatAbilitySO" }), declared);

            foreach (var a in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
            {
                float expected = a switch
                {
                    CluckShockAbilitySO s => s.KnockbackForce,
                    RollPushAbilitySO s   => s.PushStrength,
                    HeadbuttAbilitySO s   => s.KnockbackForce,
                    BellyFlopAbilitySO s  => s.KnockbackForce,
                    SnatchAbilitySO s     => s.KnockbackStrength,
                    SpineCoatAbilitySO _  => ChickenController.StealBackKnockback,
                    _                     => 0f,
                };
                Assert.AreEqual(expected, a.EmittedKnockback, 0f, a.name);
            }
        }

        [Test]
        public void TheUnvalidatedKnockbackRpc_IsGone()
        {
            Assert.IsNull(typeof(ChickenController).GetMethod("RPC_ApplyKnockback"), "any peer could send any chicken any impulse through it");
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles())
                StringAssert.DoesNotContain(".RPC_ApplyKnockback(", RuntimeSourceGuardTests.Code(path), path);
            string body = AssassinExecuteCreditTests.MethodBody(ControllerPath, "public void RPC_ApplyKnockbackFrom(");
            StringAssert.Contains("impulse: impulse", body, "the impulse reaches the bound check");
            StringAssert.Contains("ControlClaimRules.EvaluateKnockback(claim, impulse, ceiling)",
                AssassinExecuteCreditTests.MethodBody(ControllerPath, "private ControlClaimRules.Verdict JudgeControl("));
        }

        [TestCase("HeadbuttAbilitySO.cs")]
        [TestCase("BellyFlopAbilitySO.cs")]
        [TestCase("SnatchAbilitySO.cs")]
        [TestCase("CluckShockAbilitySO.cs")]
        [TestCase("RollPushAbilitySO.cs")]
        public void PairedKnockback_NamesItsCaster(string file)
        {
            string src = Code(Path.Combine(AbilityDir, file));
            StringAssert.Contains(".KnockAway(", src, $"{file}: its knockback goes through the caster's one sender (A20), which names the caster");
            StringAssert.DoesNotContain(".RPC_ApplyKnockbackFrom(", src, $"{file}: the direction and the caster id are the sender's job");
            StringAssert.DoesNotContain(".RPC_ApplyKnockback(", src, $"{file}: the unvalidated knockback lands across a base wall its other half is refused at");
        }

        [Test]
        public void SpineCoatContact_NamesItsWearer()
        {
            string body = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void CheckCollisionSlow()");
            StringAssert.Contains("KnockAway(other, StealBackKnockback);", body);
            StringAssert.DoesNotContain("other.RPC_ApplyKnockback", body);

            // The one sender: away from this chicken, named with its own id; nothing for a strength of 0.
            string knock = AssassinExecuteCreditTests.MethodBody(ControllerPath, "public void KnockAway(");
            StringAssert.Contains("if (victim == null || !(strength > 0f)) return;", knock);
            StringAssert.Contains("Planar.DirectionOr(transform.position, victim.transform.position, transform.forward)", knock);
            StringAssert.Contains("victim.RPC_ApplyKnockbackFrom(away * strength, Id);", knock);
            int senders = RuntimeSourceGuardTests.RuntimeFiles().Count(p => RuntimeSourceGuardTests.Code(p).Contains(".RPC_ApplyKnockbackFrom("));
            Assert.AreEqual(1, senders, "only ChickenController.KnockAway sends a knockback");
        }
    }
}
