using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Logging;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Tests
{
    /// <summary>
    /// B10 / F5 (2026-10-22): PLAY AGAIN despawns every live ability zone and Doppelganger decoy, by a per-peer event, and
    /// the round-start invariants (in-game probe + auto-test hook) prove it. Before this, a Root Egg placed in the last
    /// seconds of a round lived on into the next one on every peer (session-20261021-213935: the same zone hash at round 2's
    /// GO on all four clients, while the probe printed "Invariant ok").
    /// </summary>
    public sealed class RoundResetSignalTests
    {
        private const string GameManagerPath = "Assets/_Game/Scripts/Gameplay/GameManager.cs";
        private const string ZonePath = "Assets/_Game/Scripts/Gameplay/AbilityZone.cs";
        private const string DecoyPath = "Assets/_Game/Scripts/Gameplay/Doppelganger.cs";

        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Errors = new();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) => Errors.Add(message);
        }

        private static string Src(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        /// <summary>The body of the first member whose declaration contains <paramref name="signature"/> (up to its closing brace at 8 spaces).</summary>
        private static string Body(string src, string signature)
        {
            int start = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, $"'{signature}' not found - renamed? update this test");
            int end = src.IndexOf("\n        }", start, StringComparison.Ordinal);
            return src.Substring(start, end - start);
        }

        // ---- the signal ------------------------------------------------------------------------------------------

        [Test]
        public void RaiseRoundReset_RunsEverySubscriberOnce_WithTheResetTick_AndAThrowingOneDoesNotStopTheReset()
        {
            int a = 0, b = 0, seen = 0;
            Action<int> first = t => { a++; seen = t; };
            Action<int> broken = _ => throw new InvalidOperationException("boom");
            Action<int> last = _ => b++;
            var log = new RecordingLog();
            MatchSignals.RoundReset += first;
            MatchSignals.RoundReset += broken;
            MatchSignals.RoundReset += last;
            try
            {
                int ok = MatchSignals.RaiseRoundReset(1234, log);
                Assert.AreEqual(2, ok, "the two healthy subscribers ran");
                Assert.AreEqual(1, a);
                Assert.AreEqual(1234, seen, "subscribers receive the reset tick (their despawn cut-off)");
                Assert.AreEqual(1, b, "a subscriber after the throwing one still ran");
                Assert.AreEqual(1, log.Errors.Count, "the throwing subscriber is reported, not swallowed");
                StringAssert.Contains("boom", log.Errors[0]);
            }
            finally
            {
                MatchSignals.RoundReset -= first;
                MatchSignals.RoundReset -= broken;
                MatchSignals.RoundReset -= last;
            }
            Assert.AreEqual(0, MatchSignals.RaiseRoundReset(1, log), "no subscribers: nothing runs, nothing breaks");
        }

        [Test]
        public void Observation_OnlyNonAuthorityPeersRaise_OncePerResetTickChange()
        {
            Assert.IsTrue(RoundResetObservation.ShouldRaiseOnChange(false, 0, 1500), "a joiner sees the host's PLAY AGAIN");
            Assert.IsTrue(RoundResetObservation.ShouldRaiseOnChange(false, 1500, 3200), "the next PLAY AGAIN");
            Assert.IsFalse(RoundResetObservation.ShouldRaiseOnChange(true, 0, 1500), "the authority already raised it inside the reset");
            Assert.IsFalse(RoundResetObservation.ShouldRaiseOnChange(false, 1500, 1500), "no change, no signal");
            Assert.IsFalse(RoundResetObservation.ShouldRaiseOnChange(false, 1500, 0), "a cleared tick is no reset");

            Assert.IsTrue(RoundResetObservation.ShouldRaiseOnSpawn(false, 1500),
                "a manager that spawned after a reset (fresh manager over a dirty world, or a late join)");
            Assert.IsFalse(RoundResetObservation.ShouldRaiseOnSpawn(false, 0), "a fresh manager: no reset happened");
            Assert.IsFalse(RoundResetObservation.ShouldRaiseOnSpawn(true, 1500));
        }

        [Test]
        public void OutlivedReset_TakesOnlyObjectsOfTheRoundThatEnded()
        {
            Assert.IsTrue(RoundResetObservation.OutlivedReset(spawnTick: 1400, resetTick: 1500), "a Root Egg cast before PLAY AGAIN");
            Assert.IsTrue(RoundResetObservation.OutlivedReset(1500, 1500), "spawned earlier in the very tick the reset ran");
            Assert.IsFalse(RoundResetObservation.OutlivedReset(1620, 1500),
                "cast in the NEW round on a stalled peer whose Render raised the signal late: it must live");
            Assert.IsFalse(RoundResetObservation.OutlivedReset(1400, 0), "no reset recorded");
            // A late joiner's spawn rule delivers an old reset tick: everything it owns spawned after it.
            Assert.IsFalse(RoundResetObservation.OutlivedReset(9000, 1500));
        }

        [Test]
        public void WorldDirt_CountsLiveZonesDecoysAndAuras()
        {
            // A round can end (host lost) before any food moved and still leave a zone behind.
            Assert.IsFalse(MatchFlowRules.IsWorldDirty(new WorldDirt(0f, false, false, MatchFlowRules.Leftovers(0, 0, 0))));
            Assert.IsTrue(MatchFlowRules.IsWorldDirty(new WorldDirt(0f, false, false, MatchFlowRules.Leftovers(1, 0, 0))), "a zone");
            Assert.IsTrue(MatchFlowRules.IsWorldDirty(new WorldDirt(0f, false, false, MatchFlowRules.Leftovers(0, 1, 0))), "a decoy");
            Assert.IsTrue(MatchFlowRules.IsWorldDirty(new WorldDirt(0f, false, false, MatchFlowRules.Leftovers(0, 0, 1))), "an aura");
            Assert.AreEqual(3, MatchFlowRules.Leftovers(1, 1, 1));
            Assert.AreEqual(0, MatchFlowRules.Leftovers(-1, 0, 0), "never negative");

            var scan = Body(Src(GameManagerPath), "private static WorldDirt ScanWorldDirt()");
            StringAssert.Contains("AbilityZone.ActiveZones", scan);
            StringAssert.Contains("c.IsDecoy", scan);
            StringAssert.Contains("c.AuraSlowActive", scan);
            StringAssert.Contains("MatchFlowRules.Leftovers(liveZones, decoys, auras)", scan);
        }

        [Test]
        public void GameManager_RaisesOnTheAuthorityBeforeTheRpcFanOut_AndObservesTheResetTickElsewhere()
        {
            var gm = Src(GameManagerPath);
            StringAssert.Contains("[Networked] public int ResetTickRaw", gm);
            StringAssert.DoesNotContain("[Networked] public Tick ", gm, "the Fusion weaver rejects a networked Tick (not an INetworkStruct)");
            StringAssert.DoesNotContain("ResetOrdinal", gm, "one networked field for B10, not two");

            var reset = Body(gm, "private void ResetWorldForNewRound()");
            int stamp = reset.IndexOf("ResetTickRaw = Runner.Tick.Raw;", StringComparison.Ordinal);
            int raise = reset.IndexOf("MatchSignals.RaiseRoundReset(ResetTickRaw, _log)", StringComparison.Ordinal);
            int fanOut = reset.IndexOf("RPC_ResetForNewMatch", StringComparison.Ordinal);
            Assert.GreaterOrEqual(stamp, 0, "the reset stamps the replicated tick the other peers observe");
            Assert.GreaterOrEqual(raise, 0, "the authority raises the signal itself");
            Assert.Greater(fanOut, raise, "raised before the RPC fan-out");
            Assert.Less(stamp, raise);

            StringAssert.Contains("ObserveResetTick();", Body(gm, "public override void Render()"));
            var observe = Body(gm, "private void ObserveResetTick()");
            StringAssert.Contains("RoundResetObservation.ShouldRaiseOnChange(HasStateAuthority, before, after)", observe);
            StringAssert.Contains("RoundResetObservation.ShouldRaiseOnSpawn(HasStateAuthority, ResetTickRaw)",
                Body(gm, "public override void Spawned()"));
        }

        [TestCase(ZonePath)]
        [TestCase(DecoyPath)]
        public void ZoneAndDecoy_SubscribePerSpawn_AndDespawnOnlyInTheirOwnAuthorityTick(string path)
        {
            var src = Src(path);
            var spawned = Body(src, "public override void Spawned()");
            StringAssert.Contains("MatchSignals.RoundReset += OnRoundReset;", spawned, "subscribe in Spawned: pooled objects re-arm per spawn");
            StringAssert.Contains("_resetTick = 0;", spawned, "a reused instance starts clean");
            StringAssert.Contains("_spawnTick = Runner.Tick.Raw;", spawned, "the cut-off needs this life's spawn tick");
            StringAssert.Contains("MatchSignals.RoundReset -= OnRoundReset;", Body(src, "public override void Despawned("));

            // The handler only records; despawning inside it could run inside another object's tick or a UI press.
            var handler = Regex.Match(src, @"private void OnRoundReset\(int resetTick\)\s*=>\s*([^;]+);");
            Assert.IsTrue(handler.Success, "OnRoundReset must be a one-line recorder");
            Assert.AreEqual("_resetTick = resetTick", handler.Groups[1].Value.Trim());

            var fun = Body(src, "public override void FixedUpdateNetwork()");
            int gate = fun.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal);
            int rule = fun.IndexOf("if (RoundResetObservation.OutlivedReset(_spawnTick, _resetTick))", StringComparison.Ordinal);
            Assert.GreaterOrEqual(gate, 0, "authority gate at the top");
            Assert.Greater(rule, gate, "the reset despawn sits behind the authority gate");
            StringAssert.Contains("Runner.Despawn(Object);", fun.Substring(rule));
        }

        /// <summary>
        /// Hostleave (B10 audit): the zone's and the decoy's NetworkObject flags decide whether a peer that leaves takes its
        /// zone with it or leaves an orphan nobody can despawn. Both carry <c>DestroyWhenStateAuthorityLeaves</c> and not
        /// <c>MasterClientObject</c> (which would hand them to the new master instead).
        /// </summary>
        [TestCase("Assets/_Game/Prefabs/Abilities/AbilityZone.prefab")]
        [TestCase("Assets/_Game/Prefabs/Chicken.prefab")]   // Doppelganger.prefab is a variant with no Flags override
        public void ZoneAndDecoyPrefabs_AreDestroyedWithTheirAuthority(string prefab)
        {
            var m = Regex.Match(File.ReadAllText(prefab), @"\n  Flags: (\d+)");
            Assert.IsTrue(m.Success, $"{prefab} has no NetworkObject Flags line");
            var flags = (NetworkObjectFlags)int.Parse(m.Groups[1].Value);
            Assert.IsTrue((flags & NetworkObjectFlags.DestroyWhenStateAuthorityLeaves) != 0, $"{prefab}: {flags}");
            Assert.IsTrue((flags & NetworkObjectFlags.MasterClientObject) == 0, $"{prefab}: {flags}");
            StringAssert.DoesNotContain("propertyPath: Flags", File.ReadAllText("Assets/_Game/Prefabs/Doppelganger.prefab"),
                "the decoy variant must keep the Chicken prefab's flags");
        }

        /// <summary>
        /// The manager is the opposite: a MasterClientObject, so a host change hands it to the new master (the round goes on,
        /// the next PLAY AGAIN resets as usual; hostleave sessions 20261022-100545 / -101459 show no second "Spawned"). If
        /// this flag ever goes, a host change destroys the manager and only the dirty-world fallback in Spawned is left.
        /// </summary>
        [Test]
        public void GameManagerPrefab_TransfersToTheNewMaster()
        {
            var m = Regex.Match(File.ReadAllText("Assets/_Game/Prefabs/GameManager.prefab"), @"\n  Flags: (\d+)");
            Assert.IsTrue(m.Success);
            var flags = (NetworkObjectFlags)int.Parse(m.Groups[1].Value);
            Assert.IsTrue((flags & NetworkObjectFlags.MasterClientObject) != 0, flags.ToString());
        }

        // ---- "base already claimed" warning: once per collision, not per tick (session-20261022-100224: 5248 lines) ----

        [Test]
        public void BaseClaimWarnings_OncePerCollision_RearmedOnBaseLeaveOrReset()
        {
            var p2 = PlayerRef.FromIndex(2);
            var p3 = PlayerRef.FromIndex(3);
            var w = new BaseClaimWarnings();
            Assert.IsTrue(w.ShouldWarn(p2, 1), "the first tick of a collision warns");
            for (int tick = 0; tick < 100; tick++) Assert.IsFalse(w.ShouldWarn(p2, 1), "...and the next ticks stay quiet");
            Assert.IsTrue(w.ShouldWarn(p2, 2), "a different corner is a different collision");
            Assert.IsTrue(w.ShouldWarn(p3, 1), "another player colliding on the same corner warns");

            w.Forget(p2);   // got a base
            Assert.IsTrue(w.ShouldWarn(p2, 1), "a new collision after the player got a base warns again");
            Assert.IsFalse(w.ShouldWarn(p3, 1), "forgetting one player keeps the others");

            w.KeepOnly(new[] { p2 });   // p3 left
            Assert.IsTrue(w.ShouldWarn(p3, 1), "a player who left and came back warns again");

            w.Clear();      // round reset
            Assert.AreEqual(0, w.Count);
            Assert.IsTrue(w.ShouldWarn(p2, 1));
        }

        [Test]
        public void GameManager_RateLimitsTheBaseClaimWarning_KeepingItsText()
        {
            var gm = Src(GameManagerPath);
            var assign = Body(gm, "private void AssignBasesToPlayers()");
            StringAssert.Contains("_baseClaimWarnings.ShouldWarn(player, b.CornerIndex)", assign);
            StringAssert.Contains("_baseClaimWarnings.KeepOnly(Runner.ActivePlayers);", assign);
            StringAssert.Contains("_baseClaimWarnings.Forget(player);", assign);
            StringAssert.Contains("_log?.Warn(Source, $\"Invariant violation: base {b.name} (corner {b.CornerIndex}) is already claimed, " +
                "but player {player} has it as home corner.\");", assign, "same text and level: merge.ps1 / triage still see it");
            StringAssert.Contains("_baseClaimWarnings.Clear();", Body(gm, "private void ResetWorldForNewRound()"));
        }

        // ---- the round-start rule --------------------------------------------------------------------------------

        [Test]
        public void RoundStart_ZonesAurasDecoysLeftFromTheLastRound_Fail()
        {
            var spawns = new[] { new Vector3(21.8f, 0f, 21.8f) };
            var bird = new InvariantChicken { Id = "P1", Home = 0, Position = new Vector2(21.8f, 21.8f) };
            var fails = new List<InvariantFailure>();
            RoundInvariants.CheckRoundStart(new List<InvariantChicken> { bird }, spawns, new List<(int, float)> { (0, 0f) },
                new List<(float, float)>(), 45f, 45f, false, fails, zones: 1, auras: 1, decoys: 2);
            CollectionAssert.AreEquivalent(new[] { "zones-zero", "auras-zero", "decoys-zero" }, fails.ConvertAll(f => f.Name));
            Assert.AreEqual("Invariant FAIL zones-zero expected=0 actual=1 who=world", fails.Find(f => f.Name == "zones-zero").Line());

            fails.Clear();
            RoundInvariants.CheckRoundStart(new List<InvariantChicken> { bird }, spawns, new List<(int, float)> { (0, 0f) },
                new List<(float, float)>(), 45f, 45f, false, fails, 0, 0, 0);
            CollectionAssert.IsEmpty(fails, string.Join(" | ", fails.ConvertAll(f => f.Line())));
        }

        [Test]
        public void RoundStart_CountsSuffix_IsWhatMergeParses()
        {
            // tools/test-session/merge.ps1 ($rxStartCounts) reads exactly " zn=<n> au=<n> dc=<n>" at the end of the line.
            Assert.AreEqual(" zn=1 au=0 dc=2", RoundInvariants.StartCounts(1, 0, 2));
            StringAssert.Contains(@"\bzn=(\d+) au=(\d+) dc=(\d+)", File.ReadAllText("tools/test-session/merge.ps1"));
        }

        // ---- the auto-test hook (cw_autozone) --------------------------------------------------------------------

        private readonly List<UnityEngine.Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _made.Clear();
        }

        private T Make<T>() where T : ScriptableObject
        {
            var so = ScriptableObject.CreateInstance<T>();
            _made.Add(so);
            return so;
        }

        [Test]
        public void AutoZone_PicksTheRootEggFirst_ThenAnyZone_ThenADecoy()
        {
            var peck = Make<PeckAbilitySO>();
            var trap = Make<FeatherTrapAbilitySO>();
            var egg = Make<RootEggAbilitySO>();
            var decoy = Make<DoppelgangerAbilitySO>();
            Assert.AreEqual(3, AutoTestRules.PickZoneSlot(new AbilityBaseSO[] { peck, trap, decoy, egg }), "Root Egg lives longest (8 s)");
            Assert.AreEqual(1, AutoTestRules.PickZoneSlot(new AbilityBaseSO[] { peck, trap, decoy, null }));
            Assert.AreEqual(2, AutoTestRules.PickZoneSlot(new AbilityBaseSO[] { peck, null, decoy, null }));
            Assert.AreEqual(-1, AutoTestRules.PickZoneSlot(new AbilityBaseSO[] { peck, null, null, null }), "a Warrior's loadout places nothing");
            Assert.AreEqual(-1, AutoTestRules.PickZoneSlot(null));
        }

        [Test]
        public void AutoZone_CastsOnceAtTheMark_AndPlayAgainComesSoonEnoughForAnEightSecondEgg()
        {
            Assert.IsFalse(AutoTestRules.ShouldCastZone(true, false, 10f));
            Assert.IsTrue(AutoTestRules.ShouldCastZone(true, false, AutoTestRules.ZoneLeadSeconds));
            Assert.IsFalse(AutoTestRules.ShouldCastZone(true, true, 1f), "once per round");
            Assert.IsFalse(AutoTestRules.ShouldCastZone(false, false, 1f), "never during the intro / results");

            Assert.AreEqual(AutoTestRules.PlayAgainDelaySeconds, AutoTestRules.PlayAgainDelay(false), "the normal pace is unchanged");
            // Egg cast one hold after the press; the probe checks 0.5 s after Active; GET READY takes ~1-2 s.
            float worstCase = AutoTestRules.ZoneLeadSeconds + AutoTestRules.PlayAgainDelay(true) + 2.5f + 0.5f;
            Assert.Less(worstCase, 8f, "the Root Egg (EggLifetime 8 s) must still be alive at the next round-start check");
        }

        [Test]
        public void AutoZone_Key_ParsesAndNeedsAutoPlay()
        {
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.AutoZoneKey);
            var cfg = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autosolo", "-cw_autoplay", "-cw_autozone" });
            Assert.IsTrue(cfg.AutoZone);
            CollectionAssert.IsEmpty(cfg.Problems);

            var lone = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autosolo", "-cw_autozone" });
            Assert.IsFalse(lone.AutoZone, "without the fake player nobody presses the zone slot");
            Assert.AreEqual(1, lone.Problems.Count);
        }
    }
}
