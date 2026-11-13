using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A19 (architecture audit 2026-11-12): one "the sender speaks for the named object" rule and one rejection rate
    /// limit, instead of a copy per RPC.
    /// </summary>
    public sealed class RpcClaimTests
    {
        [TestCase(true, true, false, true, TestName = "Local invoke, simulated here: accepted (solo, bots, own chicken)")]
        [TestCase(true, true, true, true, TestName = "Local invoke, simulated here, sender flag irrelevant")]
        [TestCase(true, false, true, false, TestName = "Local invoke on an object this peer does not simulate: refused")]
        [TestCase(true, false, false, false, TestName = "Local invoke, nothing holds: refused")]
        [TestCase(false, false, true, true, TestName = "Off the wire from the object's state authority: accepted (a bot's is the master)")]
        [TestCase(false, true, false, false, TestName = "Off the wire from anyone else: refused, even for an object simulated here")]
        [TestCase(false, false, false, false, TestName = "Off the wire, nothing holds: refused")]
        public void SpeaksFor_TruthTable(bool local, bool simulatedHere, bool senderIsAuthority, bool expected) =>
            Assert.AreEqual(expected, RpcClaim.SpeaksFor(local, simulatedHere, senderIsAuthority));

        [Test]
        public void ANullObject_SpeaksForNothing()
        {
            Assert.IsFalse(RpcClaim.SenderSpeaksFor((Fusion.NetworkObject)null, default));
            Assert.IsFalse(RpcClaim.SenderSpeaksFor((Fusion.NetworkBehaviour)null, default));
        }

        [Test]
        public void RejectionLimiter_OneLinePerSenderPerInterval_AndCountsWhatItHeld()
        {
            var limiter = new RpcRejectionLimiter();
            Assert.IsTrue(limiter.Allow(7, 10f, out string first));
            Assert.AreEqual(string.Empty, first);
            Assert.IsFalse(limiter.Allow(7, 10.5f, out _));
            Assert.IsFalse(limiter.Allow(7, 11.9f, out _));
            Assert.IsTrue(limiter.Allow(8, 11f, out string other), "another sender has its own budget");
            Assert.AreEqual(string.Empty, other);
            Assert.IsTrue(limiter.Allow(7, 10f + RpcRejectionLimiter.IntervalSeconds, out string next));
            Assert.AreEqual(" (+2 similar suppressed)", next);
            Assert.AreEqual(string.Empty, RpcRejectionLimiter.SuppressedNote(0));
        }

        // ---- source guards: the copies must not come back ---------------------------------------------------------

        private const string HelperFile = "Assets/_Game/Scripts/Gameplay/RpcClaim.cs";

        private static List<string> Hits(Regex pattern, params string[] exempt)
        {
            var hits = new List<string>();
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles())
            {
                if (exempt.Contains(path)) continue;
                if (pattern.IsMatch(RuntimeSourceGuardTests.Code(path))) hits.Add(path);
            }
            return hits;
        }

        [Test]
        public void NoFile_ComparesTheSenderWithAnAuthority_OutsideTheHelper()
        {
            // "<object>.StateAuthority == info.Source", either way round. The match
            // authority check (ChickenController.IsFromMatchAuthority) compares with "!=" and is a different rule.
            var copy = new Regex(@"StateAuthority\s*==\s*info\.Source|info\.Source\s*==\s*\w+(\.\w+)*\.StateAuthority");
            Assert.IsEmpty(Hits(copy, HelperFile),
                "A hand-written 'sender speaks for X' check. Use RpcClaim.SenderSpeaksFor(named, info) (docs/CONVENTIONS.md):\n" +
                string.Join("\n", Hits(copy, HelperFile)));
        }

        [Test]
        public void NoGameplayFile_BuildsItsOwnRejectionRateLimiter()
        {
            var own = new Regex(@"new\s+(CluckWars\.Diagnostics\.)?PerKeyRateLimiter\s*\(");
            var hits = Hits(own, HelperFile)
                .Where(p => p.Contains("/Gameplay/") || p.Contains("/Abilities/") || p.Contains("/Networking/"))
                // Not an RPC rejection: one line per throwing listener.
                .Where(p => !p.EndsWith("/Gameplay/ListenerList.cs"))
                .ToList();
            Assert.IsEmpty(hits, "Use RpcRejectionLimiter for 'rejected RPC' lines:\n" + string.Join("\n", hits));
        }

        [Test]
        public void TheGuards_SeeTheHelper()
        {
            // The two patterns above must match the helper itself, or they prove nothing.
            string helper = RuntimeSourceGuardTests.Code(HelperFile);
            StringAssert.IsMatch(@"StateAuthority\s*==\s*info\.Source", helper);
            StringAssert.IsMatch(@"new\s+PerKeyRateLimiter\s*\(", helper);
        }
    }
}
