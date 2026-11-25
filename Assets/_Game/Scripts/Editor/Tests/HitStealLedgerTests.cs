using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Bully's per-victim lock in the game (design 2026-11-25, 16.2): one lock per victim, whoever hit, given back when the victim reports 0.</summary>
    public sealed class HitStealLedgerTests
    {
        private const int Victim = 7, Other = 8;

        [Test]
        public void AskLocks_EveryHitterOfTheCaster()
        {
            var ledger = new HitStealLedger<int>();
            Assert.IsFalse(ledger.Locked(Victim, 0f), "nothing asked, nothing locked");
            ledger.Ask(Victim, 1, 10f, 3f);
            Assert.IsTrue(ledger.Locked(Victim, 10f), "locked at the ask, for the Stun and for the Shove alike: the key is the victim, not the hitter");
            Assert.IsTrue(ledger.Locked(Victim, 12.9f));
            Assert.IsFalse(ledger.Locked(Other, 10f), "another victim is free");
            Assert.AreEqual(2f, ledger.LockLeft(Victim, 11f), 1e-5f);
            Assert.AreEqual(0f, ledger.LockLeft(Other, 11f));
        }

        [Test]
        public void ZeroSettle_ReleasesTheLock_PositiveSettleKeepsIt()
        {
            var ledger = new HitStealLedger<int>();
            ledger.Ask(Victim, 4, 0f, 3f);
            Assert.IsTrue(ledger.Settled(Victim, 4, 0f), "the ledger recognises its own request");
            Assert.IsFalse(ledger.Locked(Victim, 1f), "a take of 0 gives the lock back: nothing was taken, so nothing is locked");

            ledger.Ask(Victim, 5, 0f, 3f);
            Assert.IsTrue(ledger.Settled(Victim, 5, 2f));
            Assert.IsTrue(ledger.Locked(Victim, 1f), "a take keeps the lock");

            ledger.Ask(Other, 6, 0f, 3f);
            Assert.IsFalse(ledger.Settled(Other, 99, 0f), "an answer to some other steal is not this ledger's");
            Assert.IsTrue(ledger.Locked(Other, 1f), "and releases nothing");
            Assert.IsFalse(ledger.Settled(Victim, 6, 0f), "a request id of another victim does not match");
            Assert.IsTrue(ledger.Locked(Victim, 1f));
        }

        [Test]
        public void Lock_EndsAfterItsSeconds()
        {
            var ledger = new HitStealLedger<int>();
            ledger.Ask(Victim, 1, 5f, 3f);
            Assert.IsTrue(ledger.Locked(Victim, 7.99f));
            Assert.IsFalse(ledger.Locked(Victim, 8f), "the lock ends at its time (HitStealRules.Locked)");
            Assert.AreEqual(0f, ledger.LockLeft(Victim, 9f));

            ledger.Ask(Victim, 2, 9f, 3f);
            Assert.IsTrue(ledger.Locked(Victim, 9.5f), "the next take locks again");
            Assert.AreEqual(1, ledger.Count, "one lock per victim, the ended one replaced");
        }

        [Test]
        public void Clear_EmptiesEveryLock()
        {
            var ledger = new HitStealLedger<int>();
            ledger.Ask(Victim, 1, 0f, 30f);
            ledger.Ask(Other, 2, 0f, 30f);
            ledger.Clear();
            Assert.AreEqual(0, ledger.Count);
            Assert.IsFalse(ledger.Locked(Victim, 1f));
            Assert.IsFalse(ledger.Locked(Other, 1f));
        }
    }
}
