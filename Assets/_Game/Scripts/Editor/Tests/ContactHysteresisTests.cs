using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A11 (collision slow enters at 1.2 m, leaves past 1.3 m) and A6 (Featherfoot ignores the
    /// collision slow through one pure hook), both landing in <c>ChickenController.CheckCollisionSlow</c>.
    /// </summary>
    public sealed class ContactHysteresisTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";

        // ---- A11: the band ----------------------------------------------------------------

        [Test]
        public void Band_IsTheDecidedOne()
        {
            Assert.AreEqual(1.2f, ContactHysteresis.EnterMeters, 1e-6f);
            Assert.AreEqual(1.3f, ContactHysteresis.ExitMeters, 1e-6f);
        }

        [Test]
        public void OutOfContact_EntersAtOrInsideEnterMeters_Only()
        {
            Assert.IsTrue(ContactHysteresis.Update(false, 1.2f));
            Assert.IsTrue(ContactHysteresis.Update(false, 0.5f));
            Assert.IsFalse(ContactHysteresis.Update(false, 1.25f), "inside the band does not START contact");
        }

        [Test]
        public void InContact_StaysThroughTheBand_LeavesPastExitMeters()
        {
            Assert.IsTrue(ContactHysteresis.Update(true, 1.25f), "inside the band KEEPS contact");
            Assert.IsTrue(ContactHysteresis.Update(true, 1.3f));
            Assert.IsFalse(ContactHysteresis.Update(true, 1.31f));
        }

        [Test]
        public void NobodyNear_IsNoContact()
        {
            Assert.IsFalse(ContactHysteresis.Update(true, float.PositiveInfinity));
            Assert.IsFalse(ContactHysteresis.Update(true, float.NaN));
        }

        [Test]
        public void TwoBirdsAtTheEdge_DoNotFlicker()
        {
            // CW-226's "contact edge": side by side at ~1.2 m, jitter of a couple of cm each tick.
            float[] distances = { 1.19f, 1.21f, 1.19f, 1.22f, 1.18f, 1.24f, 1.21f };
            int oldToggles = 0, newToggles = 0;
            bool oldState = false, newState = false;
            foreach (float d in distances)
            {
                bool o = d <= 1.2f;                                   // the pre-A11 test
                bool n = ContactHysteresis.Update(newState, d);
                if (o != oldState) oldToggles++;
                if (n != newState) newToggles++;
                oldState = o;
                newState = n;
            }
            Assert.Greater(oldToggles, 4, "fixture sanity: the plain threshold flickers on this sequence");
            Assert.AreEqual(1, newToggles, "with the band the slow switches on once and stays on");
        }

        [Test]
        public void CheckCollisionSlow_SlowsOnTheHysteresisState_StealBackKeepsThePlainTest()
        {
            string body = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void CheckCollisionSlow(");
            int steal    = body.IndexOf("CreditStealAndDrain(", StringComparison.Ordinal);
            int plain    = body.IndexOf("sqr > CollisionSlowRadius * CollisionSlowRadius", StringComparison.Ordinal);
            int band     = body.IndexOf("_inContact = ContactHysteresis.Update(_inContact,", StringComparison.Ordinal);
            int hook     = body.IndexOf("IgnoresCollisionSlow(this)", StringComparison.Ordinal);
            int slow     = body.IndexOf("ApplySlow(SlowSource.Collision, CollisionSlowFactor)", StringComparison.Ordinal);
            Assert.Greater(plain, 0, "the steal-back must keep its 1.2 m test (A11 touches only the slow)");
            Assert.Greater(steal, plain);
            Assert.Greater(band, steal, "the hysteresis state is updated after the scan");
            Assert.Greater(hook, band, "Featherfoot's hook must come after contact tracking and the steal-back (A6)");
            Assert.Greater(slow, hook, "the collision slow is the only line the hook skips");
            StringAssert.DoesNotContain("break;", body,
                "the scan must see every chicken to find the nearest one for the band");
        }

        // ---- A6: Featherfoot ----------------------------------------------------------------

        [Test]
        public void Featherfoot_IgnoresPileAndCollisionSlow_OtherPassivesIgnoreNeither()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            bool sawFeatherfoot = false;
            foreach (var passive in registry.Passives)
            {
                // The hooks are declared pure: a null self must be fine (A8 relies on it too).
                bool collision = passive.IgnoresCollisionSlow(null);
                bool pile = passive.IgnoresPileSlow(null);
                if (passive is FeatherfootPassiveSO)
                {
                    sawFeatherfoot = true;
                    Assert.IsTrue(collision, "Featherfoot ignores the collision slow (A6)");
                    Assert.IsTrue(pile, "Featherfoot still ignores the pile slow");
                }
                else
                {
                    Assert.IsFalse(collision, $"{passive.name} must not ignore the collision slow");
                }
            }
            Assert.IsTrue(sawFeatherfoot, "no Featherfoot in the shipped registry");
        }

        [Test]
        public void IgnoresCollisionSlow_HasExactlyOneProductionCallSite()
        {
            int calls = 0;
            string where = null;
            foreach (var path in System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string p = path.Replace('\\', '/');
                if (p.Contains("/Editor/")) continue;
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("///")) continue;
                    if (t.Contains(".IgnoresCollisionSlow(")) { calls++; where = p; }
                }
            }
            Assert.AreEqual(1, calls, "PassiveAbilitySO hooks have exactly one chokepoint each");
            StringAssert.EndsWith("Gameplay/ChickenController.cs", where);
        }

        [Test]
        public void ToughDamageBonus_IsGone()
        {
            // A12: a damage bonus in a game without damage; nothing read it.
            StringAssert.DoesNotContain("ToughDamageBonus", System.IO.File.ReadAllText(ControllerPath));
        }
    }
}
