using System;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// A16, part 1 (architecture audit 2026-11-12): one implementation of a timed effect window
    /// (<see cref="EffectWindow"/>) instead of five copied bodies in <c>ChickenController</c>, and ordinal pins for the
    /// networked / serialized effect enums that had none. No networked property, type or order changed.
    /// </summary>
    public sealed class EffectWindowTests
    {
        private const string Controller = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";

        [Test]
        public void WithoutARunner_NothingIsOpen_AndNothingIsGranted()
        {
            // An unspawned chicken (EditMode has no runner): the guards every window carried.
            Assert.IsFalse(EffectWindow.IsOpen(default, null));
            Assert.IsFalse(EffectWindow.TryGrant(default, null, 2f, out var granted));
            Assert.AreEqual(default(TickTimer), granted, "the window is left as it was");
        }

        [Test]
        public void TheGrantRule_IsTheOneControlImmunityRulesAlreadyPinned()
        {
            string grant = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/EffectWindow.cs", "public static bool TryGrant(");
            int guard = grant.IndexOf("if (runner == null || seconds <= 0f) return false;", StringComparison.Ordinal);
            int rule = grant.IndexOf("if (!ControlImmunityRules.ShouldReplace(Remaining(current, runner), seconds)) return false;", StringComparison.Ordinal);
            int write = grant.IndexOf("granted = TickTimer.CreateFromSeconds(runner, seconds);", StringComparison.Ordinal);
            Assert.Greater(guard, 0);
            Assert.Greater(rule, guard, "a grant never cuts a running window short");
            Assert.Greater(write, rule);
        }

        [TestCase("StunRemaining", "EffectWindow.Remaining(StunTimer, Runner)")]
        [TestCase("RootRemaining", "EffectWindow.Remaining(RootTimer, Runner)")]
        [TestCase("IsControlImmune", "EffectWindow.IsOpen(ControlImmuneTimer, Runner)")]
        [TestCase("IsSlowImmune", "IsControlImmune || EffectWindow.IsOpen(SlowImmuneTimer, Runner)")]
        [TestCase("IsStealProof", "EffectWindow.IsOpen(StealProofTimer, Runner)")]
        public void EveryWindowRead_IsOneCall(string property, string body)
        {
            string code = RuntimeSourceGuardTests.Code(Controller);
            StringAssert.IsMatch(@"public (float|bool) " + property + @" => " + Regex.Escape(body) + ";", code);
        }

        [TestCase("GrantSlowImmunity", "SlowImmuneTimer")]
        [TestCase("GrantControlImmunity", "ControlImmuneTimer")]
        [TestCase("GrantStealProof", "StealProofTimer")]
        public void EveryGrant_IsOneCall(string method, string timer)
        {
            string body = AssassinExecuteCreditTests.MethodBody(Controller, $"public void {method}(float seconds)");
            StringAssert.Contains($"if (EffectWindow.TryGrant({timer}, Runner, seconds, out var granted)) {timer} = granted;", body);
        }

        [Test]
        public void NoWindowBody_IsWrittenOutInTheController()
        {
            string code = RuntimeSourceGuardTests.Code(Controller);
            StringAssert.DoesNotMatch(@"ExpiredOrNotRunning\(Runner\)\s*\?\s*0f", code, "a hand-written 'remaining' body: use EffectWindow.Remaining");
            StringAssert.DoesNotContain("ControlImmunityRules.ShouldReplace(", code, "a hand-written grant: use EffectWindow.TryGrant");
        }

        [Test]
        public void TheNetworkedWindows_KeepTheirTypeAndOrder()
        {
            // The layout is the declaration order of the [Networked] properties: same five timers, same order.
            string code = RuntimeSourceGuardTests.Code(Controller);
            int last = -1;
            foreach (string timer in new[] { "StunTimer", "RootTimer", "ControlImmuneTimer", "SlowImmuneTimer", "StealProofTimer" })
            {
                var m = Regex.Match(code, @"\[Networked\] private TickTimer " + timer + @" \{ get; set; \}");
                Assert.IsTrue(m.Success, $"{timer} is no longer a [Networked] private TickTimer");
                Assert.Greater(m.Index, last, $"{timer} moved: the networked layout follows declaration order");
                last = m.Index;
            }
        }

        // ---- ordinal pins (networked or serialized enums that had none) -------------------------------------------

        private static void Pin<T>(params (T Value, int Ordinal)[] expected) where T : Enum
        {
            foreach (var (value, ordinal) in expected)
                Assert.AreEqual(ordinal, Convert.ToInt32(value), $"{typeof(T).Name}.{value}");
            Assert.AreEqual(expected.Length, Enum.GetValues(typeof(T)).Length, $"{typeof(T).Name}: append only, and pin the new value here");
        }

        [Test]
        public void ZoneEffect_IsPinned() => Pin((ZoneEffect.Slow, 0), (ZoneEffect.Root, 1));   // [Networked] on AbilityZone

        [Test]
        public void ControlVfx_IsPinned() =>   // [Networked] flags on ChickenController
            Pin((ControlVfx.None, 0), (ControlVfx.Slowed, 1), (ControlVfx.Rooted, 2), (ControlVfx.Stunned, 4), (ControlVfx.Snared, 8));

        [Test]
        public void ControlKind_AndControlState_ArePinned()
        {
            Pin((ControlKind.None, 0), (ControlKind.Stun, 1), (ControlKind.Root, 2), (ControlKind.Slow, 3));
            Pin((ControlState.Free, 0), (ControlState.Slowed, 1), (ControlState.Rooted, 2), (ControlState.Stunned, 3));
        }

        [Test]
        public void AbilityCategory_IsPinned() =>   // serialized in every ability asset
            Pin((AbilityCategory.Steal, 0), (AbilityCategory.Control, 1), (AbilityCategory.Defense, 2), (AbilityCategory.Utility, 3));
    }
}
