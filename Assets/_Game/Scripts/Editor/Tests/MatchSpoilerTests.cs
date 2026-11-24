using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MatchEventKind = CluckWars.Balance.MatchEventKind;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Spoiler's Death Mark, the execution, the removal, the kill loot, the passive's bonus, the Thief's steal multiplier and the
    /// forced-kills design, on synthetic maps and kits. Every number is the test's own (the one shipped-asset test reads the passives'
    /// hooks); no test asserts a win rate.
    /// </summary>
    public sealed class MatchSpoilerTests
    {
        private const float Half = 40f;
        private const float Tick = 1f / 30f;
        private const float Big = 1000f;
        private const float Bounty = 5f;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        // ---- scripted seats ----

        private delegate void Watch(MatchView view, int seat);

        // Stands at `goal` (where it is when null) and casts each (time, slot) once, on the first think at or after its time.
        private static PolicyFactory Plan(Vector2? goal, Watch watch, params (float time, int slot)[] casts)
        {
            return (seat, p) =>
            {
                int next = 0;
                return MatchTestKit.Policy((v, s) =>
                {
                    watch?.Invoke(v, s);
                    var intent = goal.HasValue
                        ? new MatchIntent { Goal = goal.Value, PeckPile = -1, CastSlot = -1, CastTarget = -1 }
                        : MatchTestKit.StandAt(v, s);
                    if (next < casts.Length && v.Time + 1e-4f >= casts[next].time) intent.CastSlot = casts[next++].slot;
                    return intent;
                })(seat, p);
            };
        }

        private static MatchAbility Mark(float arm = 1f, float window = 4f, float range = Big, float success = 8f, float fail = 4f,
            float removal = 3f, float leader = 2.5f) =>
            MatchTestKit.DeathMark(arm, window, range, success, fail, removal, leader);

        private static MatchKit Victim(string label = "victim", params MatchAbility[] abilities) =>
            MatchTestKit.Fighter(label, 100f, abilities);

        private static List<MatchEvent> Of(MatchResult r, MatchEventKind kind) => r.Events.Where(e => e.Kind == kind).ToList();

        // ---- the legality arena: a Spoiler (seat 0) and three rivals; rivals 2 and 3 stun everyone, so whichever rival is marked is stunned ----

        private struct Arena
        {
            public float Arm, Window, Range, MarkAt, StunAt, KillAt, KeepOut, Success, Fail, Removal;
            public bool Shell, SpoilerStunnable, Gather;
            public Watch Watch;

            public static Arena Standard => new Arena
            {
                Arm = 1f, Window = 4f, Range = Big, MarkAt = 0.5f, StunAt = 0.6f, KillAt = 3f, Success = 8f, Fail = 4f, Removal = 3f,
            };
        }

        private static MatchResult RunArena(Arena a, ulong seed = 5)
        {
            var map = MatchTestKit.OpenMap(Half);
            var stun = MatchTestKit.Stun(Big, 8f, 30f);
            var shield = MatchTestKit.Shield(0.5f, 1f, 6f, 30f);
            var spoilerPassive = MatchPassive.Identity();
            if (!a.SpoilerStunnable) spoilerPassive.StunInFactor = 0f;   // the Spoiler shrugs the stun off, so only the rivals are stunned
            // A rival that casts anything after its Shield loses the egg (the game's follow-up rule), so in the shelled variant the stuns come from
            // the Spoiler: the rivals shell first, and the stun lands in their walk, which only refuses slows.
            var spoilerAbilities = a.Shell && !float.IsNaN(a.StunAt)
                ? new[] { Mark(a.Arm, a.Window, a.Range, a.Success, a.Fail, a.Removal), stun }
                : new[] { Mark(a.Arm, a.Window, a.Range, a.Success, a.Fail, a.Removal) };
            var kits = new[]
            {
                MatchTestKit.Fighter("spoiler", 100f, spoilerAbilities, spoilerPassive),
                Victim("r1", stun, shield), Victim("r2", stun, shield), Victim("r3", stun, shield),
            };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.RecordEvents = true;
            setup.Rules.Farm.ClockSeconds = 10f;
            setup.Rules.KeepOutRadius = a.KeepOut;
            setup.Rules.ExecuteBounty = Bounty;

            Vector2? meet = a.Gather ? map.Places[OracleMapSO.BaseIndex(1)].Pos : (Vector2?)null;
            var shell = a.Shell ? new[] { (0.4f, 1) } : new (float, int)[0];
            var stuns = float.IsNaN(a.StunAt) ? new (float, int)[0] : new[] { (a.StunAt, 0) };
            var spoilerCasts = a.Shell && !float.IsNaN(a.StunAt)
                ? new[] { (a.MarkAt, 0), (a.StunAt, 1), (a.KillAt, 0) }
                : new[] { (a.MarkAt, 0), (a.KillAt, 0) };
            setup.Policies[0] = Plan(null, a.Watch, spoilerCasts);
            setup.Policies[1] = Plan(null, null, shell);
            setup.Policies[2] = setup.Policies[3] = Plan(meet, null, a.Shell ? shell : stuns);
            if (a.Gather) setup.Policies[1] = Plan(meet, null, shell);
            return MatchSim.Run(setup, seed);
        }

        private static int KillsOf(Arena a) => RunArena(a).Kills[0];

        // ---- the mark ----

        [Test]
        public void Mark_PicksThroughMarkPickRules_WithTheMarkStream()
        {
            const float leader = 2.5f;
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var map = MatchTestKit.OpenMap(Half);
                int picked = -1;
                bool seenByVictim = false;
                var kits = new[] { MatchTestKit.Fighter("spoiler", 100f, new[] { Mark(leader: leader) }), Victim("a"), Victim("b"), Victim("c") };
                var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
                setup.Rules.Farm.ClockSeconds = 2f;
                setup.Policies[0] = Plan(null, (v, s) =>
                {
                    int target = v.Seats[s].MarkTarget;
                    if (target < 0 || picked >= 0) return;
                    picked = target;
                    seenByVictim = v.Seats[target].MarkedBy == s && v.Seats[s].WindowLeft > 0f && v.Seats[s].ArmLeft > 0f;
                }, (0.5f, 0));
                MatchSim.Run(setup, seed);

                // The game's pick, rebuilt from the rules: living rivals in seat order, one roll each, then the fallback roll.
                var centres = Enumerable.Range(0, 4).Select(s => map.Places[OracleMapSO.BaseIndex(s)].Pos).ToArray();
                float span = 0f;
                for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++) span = Mathf.Max(span, Vector2.Distance(centres[i], centres[j]));
                var rng = MatchRng.Create(seed, MatchRng.StreamMark);
                var rivals = new List<int> { 1, 2, 3 };
                var home = new List<float>(); var near = new List<float>(); var lead = new List<float>(); var rolls = new List<float>();
                foreach (int t in rivals)
                {
                    home.Add(MarkPickRules.HomeFraction(Vector2.Distance(MatchSim.Spawn(map, t), centres[t]), span));
                    near.Add(MarkPickRules.AssassinFraction(Vector2.Distance(MatchSim.Spawn(map, 0), MatchSim.Spawn(map, t)), span));
                    lead.Add(MarkPickRules.LeaderWeight(0f, 0f, leader));
                    rolls.Add(rng.NextFloat());
                }
                int expected = rivals[MarkPickRules.PickWeighted(home, near, lead, rolls, rng.NextFloat())];

                Assert.AreEqual(expected, picked, $"seed {seed}");
                Assert.IsTrue(seenByVictim, $"seed {seed}: the marked seat sees who marked it, and the mark's arm and window are counting");
            }
        }

        [Test]
        public void Mark_NeverRepeatsTheLastMark_WhileAnotherRivalLives()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var map = MatchTestKit.OpenMap(Half);
                var marks = new List<int>();
                var kits = new[] { MatchTestKit.Fighter("spoiler", 100f, new[] { Mark(window: 2f, fail: 1f) }), Victim("a"), Victim("b"), Victim("c") };
                var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
                setup.Rules.Farm.ClockSeconds = 8f;
                int last = -1;
                setup.Policies[0] = Plan(null, (v, s) =>
                {
                    int t = v.Seats[s].MarkTarget;
                    if (t >= 0 && t != last) { marks.Add(t); }
                    last = t;
                }, (0.5f, 0), (5f, 0));   // the first mark runs out at 2.5 s, the cooldown ends at 3.5 s
                MatchSim.Run(setup, seed);
                Assert.AreEqual(2, marks.Count, $"seed {seed}: two marks");
                Assert.AreNotEqual(marks[0], marks[1], $"seed {seed}: the last mark is filtered out");
            }
        }

        // ---- the kill ----

        [Test]
        public void Kill_BeforeTheArmEnds_DoesNotLand()
        {
            var early = Arena.Standard; early.Arm = 2f; early.KillAt = 1.5f;   // arm ends at 2.5 s, the press comes at 1.5 s
            var late = early; late.KillAt = 2.6f;
            Assert.AreEqual(0, KillsOf(early), "armed only after 2.5 s");
            Assert.AreEqual(1, KillsOf(late), "the same mark, pressed once armed");
        }

        [Test]
        public void Kill_NeedsStunnedAndInRange_InsideTheWindow()
        {
            var control = Arena.Standard;
            var run = RunArena(control);
            Assert.AreEqual(1, run.Kills[0], "the control run kills");
            var kill = Of(run, MatchEventKind.Kill).Single();
            Assert.AreEqual(0, kill.Seat);
            Assert.That(kill.Time, Is.InRange(control.KillAt - 1e-3f, control.KillAt + 2 * Tick), "the Kill lands on the press");

            var notStunned = control; notStunned.StunAt = float.NaN;
            Assert.AreEqual(0, KillsOf(notStunned), "the target is not stunned");

            var outOfRange = control; outOfRange.Range = 1f;
            Assert.AreEqual(0, KillsOf(outOfRange), "the target is beyond the kill range");

            var windowOver = control; windowOver.KillAt = control.MarkAt + control.Window + 0.3f;   // the stun still runs, the mark does not
            Assert.AreEqual(0, KillsOf(windowOver), "the window is over");
        }

        [Test]
        public void Kill_IsRefusedOnAShelledTarget_ByASpoilerWhoIsStunned_AndAcrossABaseWall()
        {
            var shelled = Arena.Standard; shelled.Shell = true;
            Assert.AreEqual(0, KillsOf(shelled), "an egg cannot be executed");

            var stunnedSpoiler = Arena.Standard; stunnedSpoiler.SpoilerStunnable = true;
            Assert.AreEqual(0, KillsOf(stunnedSpoiler), "the Spoiler is stunned and cannot cast");

            // The rivals meet inside one base's wall (so their stuns land) while the Spoiler stays in its own.
            var walled = Arena.Standard; walled.Gather = true; walled.StunAt = 1f; walled.KeepOut = 3f;
            var open = walled; open.KeepOut = 0f;
            Assert.AreEqual(1, KillsOf(open), "without a wall the same set-up kills");
            Assert.AreEqual(0, KillsOf(walled), "a base wall stands between the Spoiler and its target");
        }

        [Test]
        public void Kill_SetsTheSuccessCooldown_AFailSetsTheFailCooldown()
        {
            var trace = new List<(float time, float left)>();
            var win = Arena.Standard;
            win.Watch = (v, s) => trace.Add((v.Time, v.Seats[s].CooldownLeft[1]));
            var killed = RunArena(win);
            float killTime = Of(killed, MatchEventKind.Kill).Single().Time;
            float afterKill = trace.First(t => t.time >= killTime + 0.5f).left;
            Assert.AreEqual(win.Success - 0.5f, afterKill, 0.1f, "a kill starts the success cooldown");

            trace.Clear();
            var fail = Arena.Standard;
            fail.KillAt = 99f;   // never pressed: the window runs out
            fail.Watch = (v, s) => trace.Add((v.Time, v.Seats[s].CooldownLeft[1]));
            var lapsed = RunArena(fail);
            Assert.AreEqual(0, lapsed.Kills[0]);
            float windowEnd = fail.MarkAt + fail.Window;
            float afterFail = trace.First(t => t.time >= windowEnd + 0.5f).left;
            Assert.AreEqual(fail.Fail - 0.5f, afterFail, 0.1f, "a lapsed mark starts the fail cooldown");
            Assert.AreEqual(0f, trace.First(t => t.time >= fail.MarkAt + 0.5f).left, "the slot is free while the mark runs");
        }

        // ---- payout, removal, respawn ----

        private sealed class Glimpse
        {
            public float Time;
            public bool Removed, Visible, Known;
            public Vector2 Pos;
            public float Cargo;
        }

        // Duel: seat 0 the Spoiler, seat 1 the victim. Returns the victim as the Spoiler sees it on every think.
        private static (MatchResult result, OracleMapSO map) KillDuel(MatchKit spoiler, MatchKit victim, PolicyFactory spoilerPolicy,
            PolicyFactory victimPolicy, Action<MatchSetup> tweak = null, float clock = 12f)
        {
            var map = MatchTestKit.OpenMap(Half);
            var setup = MatchTestKit.Duel(map, spoiler, victim, spoilerPolicy, victimPolicy, Half, clock);
            setup.Rules.ExecuteBounty = Bounty;
            tweak?.Invoke(setup);
            return (MatchSim.Run(setup, 3), map);
        }

        private static Watch Trace(List<Glimpse> into) => (v, s) =>
        {
            var o = v.Seats[1 - s];
            into.Add(new Glimpse { Time = v.Time, Removed = o.Removed, Visible = o.Seen, Known = o.Known, Pos = o.Pos, Cargo = o.Cargo });
        };

        [Test]
        public void Kill_PaysCargoPlusBountyIntoTheCargo_AndRemovesTheVictim()
        {
            const float amount = 6f, removal = 3f;
            var trace = new List<Glimpse>();
            var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { MatchTestKit.Stun(Big, 8f, 30f), Mark(removal: removal) });
            var victim = MatchTestKit.Fighter("victim", 100f, new MatchAbility[0], peckAmount: amount, peckCooldown: 50f, castSeconds: 0.2f);
            var (r, map) = KillDuel(spoiler, victim,
                Plan(null, Trace(trace), (1.5f, 1), (1.6f, 0), (3f, 1)),
                MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1)));

            Assert.AreEqual(1, r.Kills[0]);
            var kill = Of(r, MatchEventKind.Kill).Single();
            Assert.AreEqual(1, kill.Other);
            Assert.AreEqual(amount + Bounty, kill.Amount, 1e-4f, "its beak plus the flat bounty");
            Assert.AreEqual(amount + Bounty, r.Counters[0].Bounty, 1e-4f);
            Assert.AreEqual(amount + Bounty, r.Banked[0], 1e-3f, "the loot banks at the Spoiler's own base");
            Assert.AreEqual(0f, r.Banked[1], "the victim lost what it carried");

            Assert.IsTrue(trace.First(t => t.Time >= kill.Time + Tick).Removed, "out of play after the kill");
            var back = trace.First(t => t.Time > kill.Time + 0.5f && !t.Removed);
            Assert.AreEqual(removal, back.Time - kill.Time, 2 * Tick, "out for the removal seconds");
            Assert.AreEqual(0f, back.Cargo, "back with an empty beak");
            Assert.AreEqual(MatchSim.Spawn(map, 1).x, back.Pos.x, 1e-3f);
            Assert.AreEqual(MatchSim.Spawn(map, 1).y, back.Pos.y, 1e-3f);
        }

        [Test]
        public void Executed_WhilePecking_LandsNothing_AndDropsQueuedIntents()
        {
            const float killAt = 1f, removal = 2f;
            var trace = new List<Glimpse>();
            var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { Mark(removal: removal) });
            var victim = MatchTestKit.Fighter("victim", 100f, new MatchAbility[0], peckAmount: 3f, peckCooldown: 50f, castSeconds: 1f);
            int pile = OracleMapSO.PersonalIndex(1);
            // The victim pecks until just after the kill, then stands; its intents reach it half a second after it thinks them.
            var victimPolicy = MatchTestKit.Policy((v, s) => v.Time < killAt + 0.1f ? MatchTestKit.PeckAt(v, s, pile) : MatchTestKit.StandAt(v, s));
            var (r, map) = KillDuel(spoiler, victim, Plan(null, Trace(trace)), victimPolicy, setup =>
            {
                setup.Params[1].ReactionDelay = 0.5f;
                setup.Forced = new ForcedKills { Spoiler = 0, Kills = 1, Victims = ForcedVictims.Loaded, Times = new[] { killAt } };
            }, clock: 6f);

            Assert.AreEqual(1, r.Kills[0]);
            var starts = r.Events.Where(e => e.Kind == MatchEventKind.CastStart && e.Seat == 1 && e.Other >= 0).ToList();
            Assert.AreEqual(1, starts.Count, "one Peck started, before the kill, and no stale intent started another after it");
            Assert.Less(starts[0].Time, killAt, "the kill found it mid-cast");
            var cut = Of(r, MatchEventKind.PeckCut).Single();
            Assert.AreEqual(1, cut.Seat);
            Assert.AreEqual(CutCause.Execution, cut.Cause);
            Assert.AreEqual(0f, r.Counters[1].Foraged, "nothing landed");
            Assert.AreEqual(1, r.Counters[1].CutsByCause[(int)CutCause.Execution]);

            float up = killAt + removal;
            var later = trace.First(t => t.Time >= up + 0.3f);
            Assert.IsFalse(later.Removed);
            Assert.AreEqual(MatchSim.Spawn(map, 1).x, later.Pos.x, 1e-3f, "the queued Peck intent died with the removal: it has not moved");
            Assert.AreEqual(MatchSim.Spawn(map, 1).y, later.Pos.y, 1e-3f);
        }

        [Test]
        public void Respawned_RivalIsSeenAtItsBase()
        {
            const float killAt = 1f, removal = 2f;
            var trace = new List<Glimpse>();
            var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { Mark(removal: removal) });
            var victim = MatchTestKit.Fighter("victim", 100f, new MatchAbility[0]);
            // The victim walks to the middle, so the place it dies is far from its base.
            var walkToCentre = MatchTestKit.Policy((v, s) => MatchIntent.Idle(Vector2.zero));
            var (r, map) = KillDuel(spoiler, victim, Plan(null, Trace(trace)), walkToCentre,
                setup => setup.Forced = new ForcedKills { Spoiler = 0, Kills = 1, Victims = ForcedVictims.Empty, Times = new[] { killAt } }, clock: 6f);

            Assert.AreEqual(1, r.Kills[0]);
            var during = trace.First(t => t.Time >= killAt + 0.5f);
            Assert.IsTrue(during.Removed, "flagged as out of play");
            Assert.Greater(Vector2.Distance(during.Pos, MatchSim.Spawn(map, 1)), 5f, "its body lies where it fell");

            var back = trace.First(t => t.Time > killAt && !t.Removed);
            Assert.AreEqual(killAt + removal, back.Time, 2 * Tick);
            Assert.IsTrue(back.Visible && back.Known, "visible and known again");
            Assert.AreEqual(MatchSim.Spawn(map, 1).x, back.Pos.x, 1e-3f, "on the very think it is back, the observer sees it at its base, not where it fell");
            Assert.AreEqual(MatchSim.Spawn(map, 1).y, back.Pos.y, 1e-3f);
        }

        // ---- the Spoiler's bonus, the kill loot, the Thief ----

        private static PolicyFactory Farm() => MatchTestKit.Policy((v, s) =>
            v.Seats[s].Cargo > 0f
                ? MatchIntent.Idle(v.Map.Places[OracleMapSO.BaseIndex(s)].Pos)
                : MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(s)));

        private static MatchResult BonusDuel(float winTarget, float bonus)
        {
            var passive = MatchPassive.Identity();
            passive.MatchEndBonusFood = bonus;
            var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { Mark() }, passive);
            var farmer = MatchTestKit.Fighter("farmer", 100f, new MatchAbility[0], peckAmount: 3f, peckCooldown: 0.5f, castSeconds: 0.2f);
            var (r, _) = KillDuel(spoiler, farmer, MatchTestKit.Stand, Farm(), setup => setup.Rules.Farm.WinTarget = winTarget, clock: 30f);
            return r;
        }

        [Test]
        public void SpoilerBonus_IsPaidBeforeTheWinnerIsPicked()
        {
            const float bonus = 15f;
            // The farmer banks its one pile (10 food in this set-up); the Spoiler banks nothing but is paid the bonus on the clock.
            var clock = BonusDuel(winTarget: 40f, bonus: bonus);
            Assert.IsTrue(clock.NobodyReachedTarget);
            Assert.AreEqual(1, clock.WinnerWithoutSpoilerBonus, "the farmer banked more food");
            Assert.AreEqual(0, clock.Winner, "the bonus lifts the Spoiler above it before the winner is picked");
            Assert.AreEqual(bonus, clock.Banked[0], 1e-4f, "the bonus counts in the final total");
            Assert.Greater(clock.Banked[1], 0f);
            Assert.Less(clock.Banked[1], bonus);

            // When the target ends the match first, no bonus is paid and both picks agree.
            var target = BonusDuel(winTarget: 5f, bonus: bonus);
            Assert.IsTrue(target.ReachedTarget);
            Assert.IsFalse(target.NobodyReachedTarget);
            Assert.AreEqual(1, target.Winner);
            Assert.AreEqual(target.Winner, target.WinnerWithoutSpoilerBonus);
            Assert.AreEqual(0f, target.Banked[0], "the bonus is a clock payout");
        }

        // Seat 0 Spoiler, seat 1 the victim of the forced kill, seat 2 an optional thief.
        private static MatchResult BagRun(Vector2? spoilerGoal, MatchAbility? thiefAbility, float thiefAt)
        {
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[]
            {
                MatchTestKit.Fighter("spoiler", 100f, new[] { Mark(removal: 1f) }),
                Victim(),
                thiefAbility.HasValue ? MatchTestKit.Fighter("thief", 100f, new[] { thiefAbility.Value }) : Victim("bystander"),
                null,
            };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.RecordEvents = true;
            setup.Rules.Farm.ClockSeconds = 5f;
            setup.Rules.ExecuteBounty = Bounty;
            setup.Forced = new ForcedKills { Spoiler = 0, Kills = 1, Victims = ForcedVictims.Empty, Times = new[] { 1f } };
            setup.Policies[0] = Plan(spoilerGoal, null);
            setup.Policies[2] = thiefAbility.HasValue ? Plan(null, null, (thiefAt, 0)) : MatchTestKit.Stand;
            return MatchSim.Run(setup, 11);
        }

        [Test]
        public void KillLoot_IsBankedAtTheBase_AndIsStealable_LikeAnyCargo()
        {
            // At home the loot banks, whole.
            var home = BagRun(null, null, 0f);
            Assert.AreEqual(Bounty, home.Banked[0], 1e-3f, "an empty rival pays only the flat bounty, and it banks");

            // Away from home the loot can be stolen: it is the Spoiler's cargo, nothing else.
            const float nominal = 3f;
            var robbed = BagRun(Vector2.zero, MatchTestKit.Steal(Big, nominal, 20f), 2f);
            float expected = StealMath.Clamp(nominal, 100f, StealRules.Stealable(Bounty));
            Assert.Greater(expected, 0f);
            Assert.AreEqual(expected, robbed.Counters[2].Stolen, 1e-4f, "taken from the cargo the kill filled");
            Assert.AreEqual(expected, robbed.Counters[0].LostToSteals, 1e-4f);
            Assert.AreEqual(expected, robbed.Counters[2].StolenFromSeat[0], 1e-4f);
        }

        // ---- the kill loot is ordinary cargo, held to the killer's capacity (Maestro, 2026-11-24: no capacity-free bag) ----

        // A Spoiler of the given capacity executes a victim whose beak holds `beak` when the Kill lands.
        private static MatchResult CapacityKill(int spoilerCapacity, float beak)
        {
            var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { MatchTestKit.Stun(Big, 8f, 30f), Mark(removal: 3f) }, capacity: spoilerCapacity);
            var victim = MatchTestKit.Fighter("victim", 100f, new MatchAbility[0], peckAmount: beak, peckCooldown: 50f, castSeconds: 0.2f);
            var (r, _) = KillDuel(spoiler, victim, Plan(null, null, (1.5f, 1), (1.6f, 0), (3f, 1)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1)));
            return r;
        }

        [Test]
        public void Kill_FillsTheBeakWithTheVictimsCargoFirst_ThenTheBountyIntoTheRoomLeft()
        {
            const int capacity = 10;
            const float beak = 6f;
            var r = CapacityKill(capacity, beak);
            Assert.AreEqual(1, r.Kills[0]);
            float room = capacity - beak;
            float bountyKept = Mathf.Min(Bounty, room);
            var c = r.Counters[0];
            Assert.AreEqual(beak, c.KillCargoFromSeat[1], 1e-4f, "the victim's whole beak was drained");
            Assert.AreEqual(beak + bountyKept, c.KillGainedFromSeat[1], 1e-4f, "its beak first, then the bounty into what room is left");
            Assert.AreEqual(Bounty - bountyKept, c.KillUnfitFromSeat[1], 1e-4f, "the part of the bounty that did not fit is not gained");
            Assert.AreEqual(beak + bountyKept, Of(r, MatchEventKind.Kill).Single().Amount, 1e-4f);
            Assert.AreEqual(beak + bountyKept, r.Banked[0], 1e-3f, "what fitted banks; nothing more was ever in the beak");
            Assert.LessOrEqual(r.Banked[0], capacity + 1e-3f, "the hold never exceeds its capacity");
        }

        [Test]
        public void Kill_OnAVictimWithABeakBiggerThanTheRoom_TakesWhatFits_AndLeavesNothingOnTheVictim()
        {
            // The Fatty case: a beak larger than the Spoiler's whole capacity. The bounty finds no room, the rest of the beak is gone with the victim.
            const int capacity = 4;
            const float beak = 8f;
            var r = CapacityKill(capacity, beak);
            Assert.AreEqual(1, r.Kills[0]);
            var c = r.Counters[0];
            Assert.AreEqual(beak, c.KillCargoFromSeat[1], 1e-4f, "the victim is emptied whatever fits");
            Assert.AreEqual(capacity, c.KillGainedFromSeat[1], 1e-4f, "the Spoiler's beak is full, no more");
            Assert.AreEqual(beak + Bounty - capacity, c.KillUnfitFromSeat[1], 1e-4f, "the bounty and the rest of the beak did not fit");
            Assert.AreEqual(capacity, r.Banked[0], 1e-3f);
            Assert.AreEqual(0f, r.Banked[1], "the victim carried nothing to bank");
        }

        [Test]
        public void Kill_WithATinyBeak_StillScoresTheKill_AndGainsOnlyWhatFits()
        {
            var r = CapacityKill(1, 8f);
            Assert.AreEqual(1, r.Kills[0], "the kill itself is not conditional on room");
            Assert.LessOrEqual(r.Counters[0].KillGainedFromSeat[1], 1f + 1e-4f);
        }

        [Test]
        public void SpoilerCapacity_ComesFromThePassiveFloor_NotFromTheSharedClassStat()
        {
            var spoiler = AssetDatabase.LoadAssetAtPath<SpoilerPassiveSO>("Assets/_Game/Data/Abilities/Passives/Spoiler.asset");
            var thief = AssetDatabase.LoadAssetAtPath<ThiefPassiveSO>("Assets/_Game/Data/Abilities/Passives/Thief.asset");
            var sneaky = AssetDatabase.LoadAssetAtPath<ChickenStatsSO>("Assets/_Game/Data/Classes/Assassin.asset");
            Assert.IsNotNull(spoiler);
            Assert.IsNotNull(thief);
            Assert.IsNotNull(sneaky);

            float stat = sneaky.CargoCapacity;
            Assert.AreEqual(Mathf.Max(stat, spoiler.MinimumCapacity), spoiler.ModifyCargoCapacity(stat, null), 1e-4f, "the floor, like the Hoarder's");
            Assert.Greater(spoiler.ModifyCargoCapacity(stat, null), stat, "the Spoiler carries more than the shared class stat");
            Assert.AreEqual(Mathf.Max(stat, thief.MinimumCapacity), thief.ModifyCargoCapacity(stat, null), 1e-4f, "the Thief reads the same stat through his own floor");

            var fresh = ScriptableObject.CreateInstance<SpoilerPassiveSO>();
            try { Assert.AreEqual(spoiler.MinimumCapacity, fresh.MinimumCapacity, 1e-6f, "the default is the shipped value"); }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }
        }

        private static MatchResult ThiefRun(MatchPassive passive, float nominal, float victimAmount)
        {
            var thief = MatchTestKit.Fighter("thief", 10f, new[] { MatchTestKit.Steal(Big, nominal, 20f) }, passive, capacity: 100);
            var victim = MatchTestKit.Fighter("victim", 100f, new MatchAbility[0], peckAmount: victimAmount, peckCooldown: 50f, castSeconds: 0.2f);
            var (r, _) = KillDuel(victim, thief, MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), MatchTestKit.CastAt(1.2f, 0), setup => { }, clock: 2f);
            return r;
        }

        [Test]
        public void Thief_StealIsScaled_AndCappedByMaxStealMultiplier()
        {
            const float nominal = 5f, factor = 1.6f, held = 9f;
            var passive = MatchPassive.Identity();
            passive.StealFactor = factor;
            passive.MaxStealMultiplier = factor;
            var r = ThiefRun(passive, nominal, held);
            float stolen = r.Counters[1].Stolen;
            Assert.AreEqual(StealMath.Clamp(nominal * factor, 100f, held), stolen, 1e-4f, "the nominal amount times the thief's factor");
            Assert.Greater(stolen, nominal, "more than the nominal amount");
            Assert.LessOrEqual(stolen, nominal * passive.MaxStealMultiplier + 1e-4f, "never above what the receiver would accept");
        }

        [Test]
        public void Thief_ShippedPassives_StealWithinTheirDeclaredMaximum()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var scalers = registry.Passives.Where(p => p.MaxStealMultiplier > 1f).ToList();
            Assert.IsNotEmpty(scalers, "no passive scales steals; the Thief is gone or its ceiling is not declared");
            foreach (var asset in scalers)
            {
                var read = MatchKits.ReadPassive(asset, new float[0]);
                Assert.LessOrEqual(read.StealFactor, read.MaxStealMultiplier + 1e-5f, $"{asset.name}: the hook multiplies above its declared ceiling");
                Assert.Greater(read.StealFactor, 1f, $"{asset.name} declares a ceiling above 1 but its hook does not scale");
                const float nominal = 5f, held = 9f;
                var r = ThiefRun(read, nominal, held);
                Assert.AreEqual(StealMath.Clamp(nominal * read.StealFactor, 100f, held), r.Counters[1].Stolen, 1e-4f, asset.name);
                Assert.LessOrEqual(r.Counters[1].Stolen, nominal * read.MaxStealMultiplier + 1e-4f, asset.name);
            }
        }

        // ---- forced kills ----

        private static MatchResult ForcedRun(int k, ForcedVictims victims, float[] amounts = null, ulong seed = 2)
        {
            var map = MatchTestKit.OpenMap(Half);
            var times = new[] { 2f, 4f, 6f, 8f };
            var kits = new List<MatchKit> { MatchTestKit.Fighter("spoiler", 100f, new[] { Mark(removal: 1f) }) };
            for (int s = 1; s < 4; s++)
            {
                float amount = amounts != null ? amounts[s - 1] : 3f;
                kits.Add(MatchTestKit.Fighter("rival" + s, 100f, new MatchAbility[0], peckAmount: amount, peckCooldown: 50f, castSeconds: 0.2f));
            }
            var setup = MatchTestKit.Setup(map, kits.ToArray(), MatchTestKit.Stand, Half);
            setup.RecordEvents = true;
            setup.Rules.Farm.ClockSeconds = 12f;
            setup.Rules.ExecuteBounty = Bounty;
            setup.Forced = new ForcedKills { Spoiler = 0, Kills = k, Victims = victims, Times = times };
            for (int s = 1; s < 4; s++)
            {
                int seat = s;
                // amounts of 0 mean "never peck" so the rival's beak stays empty
                setup.Policies[s] = amounts != null && amounts[s - 1] <= 0f
                    ? MatchTestKit.Stand
                    : MatchTestKit.Pecker(OracleMapSO.PersonalIndex(seat));
            }
            return MatchSim.Run(setup, seed);
        }

        [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4)]
        public void ForcedKills_GrantExactlyK_OnSchedule(int k)
        {
            var times = new[] { 2f, 4f, 6f, 8f };
            var r = ForcedRun(k, ForcedVictims.Empty);
            Assert.AreEqual(k, r.Kills[0]);
            Assert.AreEqual(0, r.Kills[1] + r.Kills[2] + r.Kills[3], "nobody else scores");
            var kills = Of(r, MatchEventKind.Kill);
            Assert.AreEqual(k, kills.Count, "one Kill event per scheduled time");
            for (int i = 0; i < k; i++)
                Assert.That(kills[i].Time, Is.InRange(times[i] - 1e-3f, times[i] + 2 * Tick), $"kill {i + 1}");
            Assert.IsTrue(kills.All(e => e.Seat == 0 && e.Other >= 1));
        }

        [Test]
        public void ForcedKills_LoadedVictims_PickTheRichestRival_AndEmptyTheLeast()
        {
            // Rival beaks after the first Peck: 4, 7 and 0 (the third never pecks); the pile has more than any of them.
            var amounts = new[] { 4f, 7f, 0f };
            var loaded = ForcedRun(1, ForcedVictims.Loaded, amounts);
            var empty = ForcedRun(1, ForcedVictims.Empty, amounts);
            Assert.AreEqual(2, Of(loaded, MatchEventKind.Kill).Single().Other, "the rival holding 7");
            Assert.AreEqual(7f + Bounty, Of(loaded, MatchEventKind.Kill).Single().Amount, 1e-4f);
            Assert.AreEqual(3, Of(empty, MatchEventKind.Kill).Single().Other, "the rival holding nothing");
            Assert.AreEqual(Bounty, Of(empty, MatchEventKind.Kill).Single().Amount, 1e-4f);
        }

        [Test]
        public void ForcedKills_WithNoLivingRival_FailsLoudly()
        {
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[] { MatchTestKit.Fighter("spoiler", 100f, new[] { Mark(removal: 50f) }), Victim(), null, null };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Forced = new ForcedKills { Spoiler = 0, Kills = 2, Victims = ForcedVictims.Empty, Times = new[] { 1f, 2f } };
            var ex = Assert.Throws<InvalidOperationException>(() => MatchSim.Run(setup, 1));
            StringAssert.Contains("no living rival", ex.Message);
        }

        [Test]
        public void ForcedKills_NeedsADeathMarkSpoiler_AndEnoughTimes()
        {
            var map = MatchTestKit.OpenMap(Half);
            var plain = new[] { Victim("no-mark"), Victim(), null, null };
            var setup = MatchTestKit.Setup(map, plain, MatchTestKit.Stand, Half);
            setup.Forced = new ForcedKills { Spoiler = 0, Kills = 1, Times = new[] { 1f } };
            Assert.Throws<ArgumentException>(() => MatchSim.Run(setup, 1), "no Death Mark ability to take the removal seconds from");

            var kits = new[] { MatchTestKit.Fighter("spoiler", 100f, new[] { Mark() }), Victim(), null, null };
            setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Forced = new ForcedKills { Spoiler = 0, Kills = 2, Times = new[] { 1f } };
            Assert.Throws<ArgumentException>(() => MatchSim.Run(setup, 1), "two kills need two times");
        }

        [Test]
        public void Determinism_SameSeed_SameMatch_WithMarksAndKills()
        {
            var a = RunArena(Arena.Standard, 9);
            var b = RunArena(Arena.Standard, 9);
            Assert.AreEqual(a.Kills[0], b.Kills[0]);
            CollectionAssert.AreEqual(a.Banked, b.Banked);
            Assert.AreEqual(a.Events.Count, b.Events.Count);
        }

        // ---- the kills-to-win table, from the shipped kits ----

        private static OracleInputs ShippedInputs()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            return new OracleInputs
            {
                Map = map,
                Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
        }

        private const float FirstKillSeconds = 5f;
        private const float KillGapSeconds = 3.5f;

        private static readonly int[][] RivalOrders =
            { new[] { 1, 2, 3 }, new[] { 1, 3, 2 }, new[] { 2, 1, 3 }, new[] { 2, 3, 1 }, new[] { 3, 1, 2 }, new[] { 3, 2, 1 } };

        /// <summary>
        /// A forced design with the shipped kits: the Spoiler in seat 0 against one kit of each other class (every combination), in all six
        /// orders of the rivals round the table, granted exactly k kills on schedule on empty or on loaded victims. Every seat farms like
        /// part 1's farmer and never fights, so this measures what kills are worth, not how a policy lands them. Row k takes the first k of the
        /// same times (<see cref="FirstKillSeconds"/>, then every <see cref="KillGapSeconds"/>), all before the fastest kit can bank the target. The number of replicates per cell comes from the environment variable
        /// <c>CW_FORCED_R</c> (default 1); the table is printed to the test output. No rate is asserted (D7).
        /// </summary>
        [Test]
        public void KillsToWin_ForcedDesign_WithShippedKits_BuildsTheTable()
        {
            int replicates = int.TryParse(Environment.GetEnvironmentVariable("CW_FORCED_R"), out int r) && r > 0 ? r : 1;
            var inputs = ShippedInputs();
            var kits = MatchKits.All(inputs);
            var rules = MatchKits.Rules(inputs, OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
            var reference = AssetDatabase.LoadAssetAtPath<OracleReferenceSO>(OracleMenu.ReferencePath);
            var nav = OracleNav.For(inputs.Map, rules.ArenaHalf);
            var food = OracleKits.Food(inputs);

            var spoiler = kits.Single(k => k.Passive.MatchEndBonusFood > 0f);
            Assert.IsTrue(spoiler.Abilities.Any(a => a.Job == AbilityJob.DeathMark), "the Spoiler kit holds Death Mark");
            var byClass = kits.Where(k => k.Class != spoiler.Class).GroupBy(k => k.Class).Select(g => g.ToArray()).ToArray();
            Assert.AreEqual(3, byClass.Length);
            var sets = from a in byClass[0] from b in byClass[1] from c in byClass[2] select new[] { a, b, c };

            var table = new List<SpoilerKillRow>();
            int matches = 0;
            foreach (var victims in new[] { ForcedVictims.Empty, ForcedVictims.Loaded })
                for (int k = 0; k <= 4; k++)
                {
                    var results = new List<MatchResult>();
                    var times = Enumerable.Range(0, k).Select(i => FirstKillSeconds + i * KillGapSeconds).ToArray();
                    int cell = 0;
                    foreach (var set in sets)
                        foreach (var order in RivalOrders)
                        {
                            var seats = new[] { spoiler, set[order[0] - 1], set[order[1] - 1], set[order[2] - 1] };
                            var setup = new MatchSetup
                            {
                                Map = inputs.Map, Nav = nav, Food = food, Rules = rules, Seats = seats,
                                Policies = new[] { FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory },
                                Forced = new ForcedKills { Spoiler = 0, Kills = k, Victims = victims, Times = times },
                            };
                            for (int rep = 0; rep < replicates; rep++, matches++)
                            {
                                var m = MatchSim.Run(setup, MatchRng.MatchSeed(1UL, cell, rep));
                                // All scheduled kills happen unless the match ends first, and the only way it ends before the last one is the Spoiler banking
                                // the target with the food those kills paid.
                                string who = $"({string.Join(", ", seats.Select(x => x.Label))}; {victims}; ended at {m.EndSeconds:0.0} s, cell {cell} rep {rep})";
                                if (m.Kills[0] != k)
                                {
                                    Assert.Less(m.Kills[0], k, "never more than scheduled " + who);
                                    Assert.IsTrue(m.ReachedTarget && m.Winner == 0, "a missed kill means the Spoiler had already won " + who);
                                    Assert.Less(m.EndSeconds, times[k - 1] + Tick * 2f, who);
                                }
                                Assert.AreEqual(0, m.RejectedIntents);
                                Assert.That(m.Winner, Is.InRange(0, 3));
                                results.Add(m);
                            }
                            cell++;
                        }
                    var row = MatchStats.KillsToWin(results, 0, k, victims, reference.KillTargets);
                    Assert.AreEqual(results.Count, row.Win.Trials);
                    table.Add(row);
                }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"FORCED-KILLS TABLE (scripted design, farm-only policy; {matches} matches, Spoiler = {spoiler.Label}, R = {replicates} per cell, 48 cells per row, kills at {FirstKillSeconds} s then every {KillGapSeconds} s)");
            sb.AppendLine("victims | kills | win without passive [95%] | win with passive [95%] | mean final | mean best rival | clock-ended | D6 label");
            foreach (var row in table)
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} | {1} | {2:P1} [{3:P1}, {4:P1}] | {5:P1} [{6:P1}, {7:P1}] | {8:0.0} | {9:0.0} | {10:P0} | {11}",
                    row.Victims, row.Kills, row.Win.Rate, row.Win.Low, row.Win.High, row.WinWithPassive.Rate, row.WinWithPassive.Low,
                    row.WinWithPassive.High, row.MeanFinal, row.MeanBestRival, row.NobodyReachedTarget, row.Label ?? "-"));
            TestContext.Out.WriteLine(sb.ToString());
            Assert.AreEqual(10, table.Count);
        }
    }
}
