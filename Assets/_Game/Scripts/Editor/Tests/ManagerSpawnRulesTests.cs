using CluckWars.Gameplay;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 5: the Host start race. <c>MatchBootstrapper</c>'s master-promotion poll spawned the GameManager
    /// while <c>StartGame</c> was still connecting (runner already <c>IsRunning</c> + master); the Spawn threw
    /// inside Fusion, left a never-spawned manager, and that dead instance then blocked the real spawn. The
    /// spawn now waits for the session to be ready, a dead instance does not count, and the first master
    /// observation after the start is not a promotion.
    /// </summary>
    public sealed class ManagerSpawnRulesTests
    {
        [Test]
        public void NotReady_NeverSpawns_EvenAsMaster()
        {
            Assert.IsFalse(MatchFlowRules.ShouldSpawnManager(false, false, true, false));
            Assert.IsFalse(MatchFlowRules.ShouldSpawnManager(false, true, false, false));
        }

        [Test]
        public void Ready_MasterWithoutLiveManager_Spawns() =>
            Assert.IsTrue(MatchFlowRules.ShouldSpawnManager(true, false, true, false));

        [Test]
        public void Ready_Solo_Spawns_WithoutBeingMaster() =>
            Assert.IsTrue(MatchFlowRules.ShouldSpawnManager(true, true, false, false));

        [Test]
        public void Ready_NonMasterClient_NeverSpawns() =>
            Assert.IsFalse(MatchFlowRules.ShouldSpawnManager(true, false, false, false));

        [Test]
        public void LiveManager_BlocksSpawn()
        {
            Assert.IsFalse(MatchFlowRules.ShouldSpawnManager(true, false, true, true));
            Assert.IsFalse(MatchFlowRules.ShouldSpawnManager(true, true, false, true));
        }

        [Test]
        public void FirstObservation_IsNotAPromotion()
        {
            Assert.IsFalse(MatchFlowRules.IsMasterPromotion(null, true), "host that started as master");
            Assert.IsFalse(MatchFlowRules.IsMasterPromotion(null, false));
        }

        [Test]
        public void NonMasterBecomingMaster_IsAPromotion() =>
            Assert.IsTrue(MatchFlowRules.IsMasterPromotion(false, true), "joiner after the host left");

        [Test]
        public void StayingMaster_OrLosingIt_IsNotAPromotion()
        {
            Assert.IsFalse(MatchFlowRules.IsMasterPromotion(true, true));
            Assert.IsFalse(MatchFlowRules.IsMasterPromotion(true, false));
            Assert.IsFalse(MatchFlowRules.IsMasterPromotion(false, false));
        }
    }
}
