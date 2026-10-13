using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Security hardening of the steal-proof refund (<c>RPC_RefundRefusedSteal</c>) and the
    /// Death Mark transfer (<c>RPC_TransferAllToBountyBag</c>): neither may be driven by a peer
    /// without a real steal / mark behind it. The ledger is pure and asserted for real; the
    /// Fusion-side wiring is pinned from comment-stripped source.
    /// </summary>
    public sealed class PendingStealLedgerTests
    {
        private const string Dir = "Assets/_Game/Scripts/";
        private const float W = PendingStealLedger<int>.WindowSeconds;

        private static string Src(string rel)
        {
            string path = Dir + rel;
            Assert.IsTrue(File.Exists(path), $"Expected source file not found: {path}");
            string src = Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var sb = new StringBuilder(src.Length);
            foreach (var line in src.Split('\n'))
            {
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(slash >= 0 ? line.Substring(0, slash) : line).Append('\n');
            }
            return sb.ToString();
        }

        private static string Body(string src, string signature, int span)
        {
            int i = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.Greater(i, 0, $"'{signature}' not found - the test anchor moved.");
            return src.Substring(i, Math.Min(span, src.Length - i));
        }

        // ---- Ledger -------------------------------------------------------------

        [Test]
        public void UnsolicitedRefund_IsRejected()
        {
            var ledger = new PendingStealLedger<int>();
            Assert.IsFalse(ledger.TryConsume(7, 4f, 0f, out float allowed));
            Assert.AreEqual(0f, allowed);
        }

        [Test]
        public void RefundForAnotherVictim_IsRejected()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 4f, 0f);
            Assert.IsFalse(ledger.TryConsume(2, 4f, 0.1f, out _));
            Assert.IsTrue(ledger.TryConsume(1, 4f, 0.1f, out _), "the real record is untouched");
        }

        [Test]
        public void MatchedRefund_IsAcceptedOnce()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 4f, 0f);
            Assert.IsTrue(ledger.TryConsume(1, 4f, 0.3f, out float allowed));
            Assert.AreEqual(4f, allowed, 1e-5f);
        }

        [Test]
        public void DoubleRefund_IsRejected()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 4f, 0f);
            Assert.IsTrue(ledger.TryConsume(1, 4f, 0.2f, out _));
            Assert.IsFalse(ledger.TryConsume(1, 4f, 0.3f, out float again));
            Assert.AreEqual(0f, again);
        }

        [Test]
        public void ExpiredRecord_IsRejected_AndDropped()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 4f, 0f);
            Assert.IsFalse(ledger.TryConsume(1, 4f, W + 0.01f, out _));
            Assert.IsFalse(ledger.TryConsume(1, 4f, 0.1f, out _), "an expired record stays gone");
        }

        [Test]
        public void RecordAtTheEdgeOfTheWindow_IsStillHonoured()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 4f, 0f);
            Assert.IsTrue(ledger.TryConsume(1, 4f, W, out _));
        }

        [Test]
        public void Refund_IsCappedAtTheRecordedAmount()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 3f, 0f);
            Assert.IsTrue(ledger.TryConsume(1, 11f, 0.1f, out float allowed));
            Assert.AreEqual(3f, allowed, 1e-5f, "asking for more than was credited returns only the credit");
        }

        [Test]
        public void SmallerRequest_ConsumesTheWholeRecord()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 5f, 0f);
            Assert.IsTrue(ledger.TryConsume(1, 2f, 0.1f, out float allowed));
            Assert.AreEqual(2f, allowed, 1e-5f);
            Assert.IsFalse(ledger.TryConsume(1, 3f, 0.2f, out _), "the remainder is not a second refund");
        }

        [Test]
        public void TwoRecordsFromOneVictim_EachMatchedOnce_PreferringTheOneThatCoversTheRequest()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 2f, 0f);
            ledger.Record(1, 5f, 0.1f);
            Assert.IsTrue(ledger.TryConsume(1, 5f, 0.3f, out float a));
            Assert.AreEqual(5f, a, 1e-5f, "the 5-credit is refunded in full, not the older 2-credit");
            Assert.IsTrue(ledger.TryConsume(1, 2f, 0.4f, out float b));
            Assert.AreEqual(2f, b, 1e-5f);
            Assert.IsFalse(ledger.TryConsume(1, 2f, 0.5f, out _));
        }

        [Test]
        public void NonPositiveRequestsAndCredits_AreIgnored()
        {
            var ledger = new PendingStealLedger<int>();
            ledger.Record(1, 0f, 0f);
            ledger.Record(1, -3f, 0f);
            Assert.IsFalse(ledger.TryConsume(1, 1f, 0.1f, out _));
            ledger.Record(1, 3f, 0f);
            Assert.IsFalse(ledger.TryConsume(1, 0f, 0.1f, out _));
            Assert.IsFalse(ledger.TryConsume(1, -1f, 0.1f, out _));
        }

        [Test]
        public void FullRing_OverwritesTheOldest_AndClearEmptiesIt()
        {
            var ledger = new PendingStealLedger<int>();
            for (int i = 0; i < PendingStealLedger<int>.Capacity + 1; i++)
                ledger.Record(i, 1f, 0f);

            Assert.IsFalse(ledger.TryConsume(0, 1f, 0.1f, out _), "the oldest was overwritten");
            Assert.IsTrue(ledger.TryConsume(PendingStealLedger<int>.Capacity, 1f, 0.1f, out _));

            ledger.Clear();
            Assert.IsFalse(ledger.TryConsume(1, 1f, 0.1f, out _));
        }

        [Test]
        public void Window_CoversAQuarterSecondRoundTrip_WithMargin()
        {
            // credit -> drain RPC -> refund RPC is one RTT plus tick boundaries.
            const float rtt = 0.25f, tickBoundaries = 2f / 32f;
            Assert.GreaterOrEqual(W, 2f * (rtt + tickBoundaries));
            Assert.LessOrEqual(W, 1.5f, "keep the forgery window short");
        }

        // ---- Wiring (source guards) ---------------------------------------------

        [Test]
        public void RefundRpc_ConsumesAPendingRecord_BeforeTouchingCargo()
        {
            string body = Body(Src("Gameplay/ChickenCargo.cs"), "public void RPC_RefundRefusedSteal(", 3200);
            int consume = body.IndexOf("_pendingSteals.TryConsume(victimId", StringComparison.Ordinal);
            int cargo = body.IndexOf("Cargo -= refund", StringComparison.Ordinal);
            Assert.Greater(consume, 0, "the refund must be matched against a pending record");
            Assert.Greater(cargo, consume);
            StringAssert.Contains("return;", body.Substring(consume, cargo - consume), "no record -> reject");
            StringAssert.Contains("_log?.Warn", body.Substring(consume, cargo - consume));
        }

        [Test]
        public void HelperRecordsBeforeItSendsTheDrain_AndEveryDrainGoesThroughIt()
        {
            string cargo = Src("Gameplay/ChickenCargo.cs");
            string helper = Body(cargo, "public void CreditStealAndDrain(", 1100);
            int credit = helper.IndexOf("Cargo += amount", StringComparison.Ordinal);
            int record = helper.IndexOf("_pendingSteals.Record(", StringComparison.Ordinal);
            int rpc = helper.IndexOf("RPC_DrainStolen(amount", StringComparison.Ordinal);
            Assert.Greater(credit, 0);
            Assert.Greater(record, credit);
            Assert.Greater(rpc, record, "a local invoke can refund synchronously, so the record must exist first");

            foreach (var path in Directory.GetFiles(Dir, "*.cs", SearchOption.AllDirectories))
            {
                string norm = path.Replace('\\', '/');
                if (norm.Contains("/Editor/") || Path.GetFileName(path) == "ChickenCargo.cs") continue;
                string src = Src(norm.Substring(Dir.Length));
                StringAssert.DoesNotContain(".RPC_DrainStolen(", src,
                    $"{Path.GetFileName(path)} calls RPC_DrainStolen directly: no pending record, so a steal-proof refusal could never be refunded.");
            }
        }

        [TestCase("Abilities/SnatchAbilitySO.cs")]
        [TestCase("Abilities/SneakyStealAbilitySO.cs")]
        [TestCase("Abilities/ScrapAbilitySO.cs")]
        [TestCase("Abilities/RollTrampleAbilitySO.cs")]
        [TestCase("Gameplay/ChickenController.cs")]
        public void DrainSite_UsesTheHelper_AndNoLongerSelfCreditsByHand(string file)
        {
            string src = Src(file);
            StringAssert.Contains(".CreditStealAndDrain(", src);
            Assert.IsFalse(Regex.IsMatch(src, @"Cargo\s*\+=\s*(stolen|take)\b"),
                $"{file} still credits itself by hand; the pending record would be missing.");
        }

        [Test]
        public void TransferAllToBountyBag_RequiresTheAssassinsAuthority_AndAMarkOnThisChicken()
        {
            string cargo = Src("Gameplay/ChickenCargo.cs");
            string rpc = Body(cargo, "public void RPC_TransferAllToBountyBag(", 1400);
            StringAssert.Contains("RpcInfo info = default", rpc);
            int resolve = rpc.IndexOf("TryResolveExecutor(", StringComparison.Ordinal);
            int zero = rpc.IndexOf("Cargo = 0f", StringComparison.Ordinal);
            Assert.Greater(resolve, 0);
            Assert.Greater(zero, resolve, "validation must run before the cargo is touched");
            StringAssert.Contains("_log?.Warn", rpc.Substring(resolve, zero - resolve));

            string guard = Body(cargo, "private bool TryResolveExecutor(", 1800);
            StringAssert.Contains("execute.Object.HasStateAuthority", guard);
            StringAssert.Contains("execute.Object.StateAuthority == info.Source", guard);
            StringAssert.Contains("execute.MarkedTarget != _controller.Id && !_controller.IsStunned", guard);
        }
    }
}
