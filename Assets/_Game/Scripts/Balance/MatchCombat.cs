using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>What an incoming effect is, for the one question "does this state refuse it" (<see cref="MatchCombat.Refused"/>).</summary>
    public enum IncomingEffect : byte { Stun, Slow, Shove, Steal, Execute }

    /// <summary>
    /// The pure combat rules of the match simulator. Where the game has a pure rule, this calls it (<see cref="AbilityAim"/> for reach,
    /// <see cref="BaseWallRules"/> for the base wall, <see cref="StealMath"/>, <see cref="StealRules"/>, <see cref="PeckCastRules"/>,
    /// <see cref="ControlReapplyRules"/>); what the game decides inline is mirrored here and pinned by a test.
    /// </summary>
    public static class MatchCombat
    {
        /// <summary>
        /// Is <paramref name="target"/> inside the circle of <paramref name="radius"/> round <paramref name="caster"/>? The game's own
        /// range-only test (<c>AbilityAim.InShape</c>, a planar distance not above the radius). Arms of the pinwheel do not block an
        /// ability (spec 2.2: the only wall that does is a base's, <see cref="KeepOutBlocks"/>).
        /// </summary>
        public static bool InRange(Vector2 caster, Vector2 target, float radius) =>
            AbilityAim.InShape(AbilityAimShape.SelfCircle, new Vector3(caster.x, 0f, caster.y), Vector3.forward,
                new Vector3(target.x, 0f, target.y), radius, 0f, 0f);

        /// <summary>
        /// True when a base's wall stands between the two points: the game's <see cref="BaseWallRules.Blocks"/> over every claimed base
        /// (what <c>PlayerBase.WallBetween</c> gathers). No ability crosses it, in either direction.
        /// </summary>
        public static bool KeepOutBlocks(Vector2 caster, Vector2 target, Vector2[] baseCentres, float keepOutRadius)
        {
            if (baseCentres == null) return false;
            var a = new Vector3(caster.x, 0f, caster.y);
            var b = new Vector3(target.x, 0f, target.y);
            for (int i = 0; i < baseCentres.Length; i++)
                if (BaseWallRules.Blocks(a, b, baseCentres[i], keepOutRadius)) return true;
            return false;
        }

        /// <summary>
        /// Seconds a control lasts on its victim: the emission's seconds times the caster's outgoing factor (Relentless), then the victim's
        /// passive for that kind of control (<c>ControlClaimRules.OutgoingSeconds</c> then <c>ModifyControlDuration</c>). A root, or a
        /// control of no kind, is not modelled and gives 0.
        /// </summary>
        public static float ControlSeconds(ControlEmission e, MatchPassive caster, MatchPassive victim)
        {
            float outgoing = e.Seconds * caster.OutgoingControlFactor;
            switch (e.Kind)
            {
                case ControlKind.Stun: return outgoing * victim.StunInFactor;
                case ControlKind.Slow: return outgoing * victim.SlowInDurationFactor;
                default: return 0f;
            }
        }

        /// <summary>
        /// Metres a shove moves its victim in all: the impulse (after the victim's passive) decays by <paramref name="decayRate"/> per
        /// second, so its integral is impulse over rate (<c>ChickenMovement</c> applies the velocity, then lerps it to zero).
        /// </summary>
        public static float ShoveTravel(float knockback, float victimFactor, float decayRate) =>
            decayRate > 0f ? Mathf.Max(0f, knockback * victimFactor) / decayRate : 0f;

        /// <summary>
        /// Does a chicken in this state refuse the effect? The egg of Egg Shell refuses every control, the shove, a steal and an execute;
        /// the shield's walk phase refuses slows only (<c>TurtleModeAbilitySO</c>: stun, shove, theft and execution still land).
        /// </summary>
        public static bool Refused(IncomingEffect effect, bool egg, bool shieldWalk)
        {
            if (egg) return true;
            return shieldWalk && effect == IncomingEffect.Slow;
        }

        /// <summary>
        /// The speed factor of the slows on one chicken: slows do not stack, only the strongest applies. Mirrors <c>ChickenController.ApplySlow</c>
        /// (<c>SlowMultiplier = Mathf.Min(SlowMultiplier, factor)</c> over the pile slow, the timed ability slow and the zone or aura slow, each 1 when
        /// absent), which sits on a <c>NetworkBehaviour</c> and cannot be called here; a source test pins it.
        /// </summary>
        public static float CombineSlows(float pile, float timed, float aura) => Mathf.Min(pile, Mathf.Min(timed, aura));

        /// <summary>
        /// Food a steal takes from one victim: the nominal amount times the thief's factor, clamped to the thief's free room and to what the
        /// victim holds (<c>ChickenCargo.StealFrom</c>, through <see cref="StealMath.Clamp"/> and <see cref="StealRules.Stealable"/>).
        /// </summary>
        public static float StealTake(float nominal, float thiefFactor, float thiefFreeRoom, float victimCargo) =>
            StealMath.Clamp(nominal * thiefFactor, thiefFreeRoom, StealRules.Stealable(victimCargo));

        /// <summary>
        /// The timer's winner: most banked, then most kills, then the lowest corner, over the seats in <paramref name="present"/>. Mirrors
        /// <c>GameManager.EndOnTimerExpiry</c>, which decides it inline over <c>PlayerBase</c> objects and so cannot be called here; a
        /// source test pins the three comparisons. Returns -1 when nobody plays. <paramref name="cornerDecided"/> is true when the winner is
        /// level with another seat on banked food and kills, so only the corner decided it.
        /// </summary>
        public static int PickTimerWinner(float[] banked, int[] kills, bool[] present, out bool cornerDecided)
        {
            cornerDecided = false;
            int best = -1;
            for (int s = 0; s < banked.Length; s++)
            {
                if (!present[s]) continue;
                if (best < 0) { best = s; continue; }
                if (banked[s] > banked[best]) best = s;
                else if (Mathf.Approximately(banked[s], banked[best]))
                {
                    if (kills[s] > kills[best]) best = s;
                    else if (kills[s] == kills[best] && s < best) best = s;
                }
            }
            if (best < 0) return -1;
            for (int s = 0; s < banked.Length; s++)
                if (s != best && present[s] && Mathf.Approximately(banked[s], banked[best]) && kills[s] == kills[best]) cornerDecided = true;
            return best;
        }
    }

    /// <summary>
    /// The most that sums inside any window of <c>window</c> seconds (half open: an amount at time t counts for a later add until t + window is reached).
    /// Amounts are added in time order.
    /// </summary>
    public sealed class SlidingWindowSum
    {
        private readonly float _window;
        private readonly System.Collections.Generic.Queue<(float time, float amount)> _items = new System.Collections.Generic.Queue<(float, float)>();
        private float _sum;

        public SlidingWindowSum(float window) { _window = window; }

        /// <summary>The largest sum seen in any window so far.</summary>
        public float Max { get; private set; }

        public void Add(float time, float amount)
        {
            _items.Enqueue((time, amount));
            _sum += amount;
            while (_items.Count > 0 && _items.Peek().time + _window <= time) _sum -= _items.Dequeue().amount;
            if (_sum > Max) Max = _sum;
        }
    }

    /// <summary>The comeback event's numbers: when it fires and what its outcomes do. Filled from the game's constants by <see cref="MatchKits.Rules"/>.</summary>
    public struct ComebackSettings
    {
        /// <summary>The event fires once when this many seconds or fewer are left; zero or less switches it off.</summary>
        public float SecondsLeft;
        public float GoldenPileFood;
        public float RestockFood;
        public float GoldenRingMin;
        public float GoldenRingMax;
    }

    /// <summary>What happened to one seat in one match. <c>FirstContact</c> is -1 when it never happened.</summary>
    public struct SeatCounters
    {
        public float Foraged;
        public float Stolen;
        public float LostToSteals;
        public float Bounty;
        /// <summary>Loot marks drawn on a rival (the Thief's mark).</summary>
        public int LootOffered;
        /// <summary>Loot marks consumed by a marked steal: the Thief's marked steals.</summary>
        public int LootTaken;
        /// <summary>Marks that moved on without a take (the timeout) or ended without pay (the marked bird was removed).</summary>
        public int LootTimeouts;
        /// <summary>Steal casts that covered the marked rival in range but were refused by a base wall or a shell.</summary>
        public int LootRefused;
        /// <summary>Food the marked steals took from the marked rivals (into the Thief's cargo).</summary>
        public float LootTakeFood;
        /// <summary>New bounty food the marked steals put into the Thief's cargo.</summary>
        public float LootBountyFood;
        /// <summary>What marked steals would have gained with an unlimited beak, minus what they did: stolen food and bounty that did not fit.</summary>
        public float LootLostToRoom;
        /// <summary>Loot food (marked take plus bounty) this seat banked; a Thief's cargo mixes it with plain stolen food, so a deposit or a theft takes the loot share pro rata.</summary>
        public float LootBankedFood;
        /// <summary>Loot food rivals' steals took from this seat (pro rata of the cargo they drained).</summary>
        public float LootLostToSteals;
        /// <summary>Deposit visits that emptied this seat's cargo (trips home).</summary>
        public int Trips;
        /// <summary>Most marked steals this seat made between two of its trips home (the load before the first, and the one carried when the clock ended, count too).</summary>
        public int MaxMarksPerTrip;
        /// <summary>Seconds a Thief's loot mark sat on the same bird as a Spoiler's Hunt mark (counted on the Thief's seat).</summary>
        public float LootHuntOverlapSeconds;
        public int Casts;
        public int Steals;
        /// <summary>Hits of this seat's Stun, Shove or Slow that took food (the Bully's hit-steal).</summary>
        public int HitTakes;
        /// <summary>Food those hits took into this seat's cargo (also counted in <see cref="Stolen"/>, not in <see cref="Steals"/>).</summary>
        public float HitFood;
        /// <summary>Hits that affected a rival but took nothing because that rival was locked for this seat.</summary>
        public int HitRefusedLock;
        /// <summary>Hits that took nothing because the rival held no food.</summary>
        public int HitRefusedNoFood;
        /// <summary>Hits that took nothing because this seat's beak was full.</summary>
        public int HitRefusedRoom;
        /// <summary>Food hit-steals took from this seat (victim side; also in <see cref="LostToSteals"/>).</summary>
        public float HitLostFood;
        /// <summary>Peck breaks this seat landed (the Trophy Hunter's).</summary>
        public int Breaks;
        /// <summary>Food breaks took from the victims' piles into this seat's cargo (not counted as foraged).</summary>
        public float BreakFood;
        /// <summary>Food of the victim's bite that did not fit this seat's beak and stayed in the pile.</summary>
        public float BreakLeftInPile;
        /// <summary>Break presses whose cast was already over and that became an ordinary pile Peck.</summary>
        public int BreakFallbacks;
        /// <summary>Break presses with no cast to break and no pile Peck to fall back to: a visible cancel that spent nothing.</summary>
        public int BreakCancels;
        /// <summary>Break presses that could not be made (stunned, dashing, mid Peck, break not ready) or met a wall, an egg or an immune rival.</summary>
        public int BreakRefused;
        /// <summary>This seat's Peck casts that were broken by a rival (victim side; also in <see cref="CutsByCause"/>).</summary>
        public int PecksBroken;
        public int TrophiesEarned;
        /// <summary>Trophies a rival's take or an execution cost this seat.</summary>
        public int TrophiesLost;
        /// <summary>Trophies cleared because this seat banked (<c>TrophiesClearOnBank</c>).</summary>
        public int TrophiesCleared;
        public int TrophyPeak;
        /// <summary>Trophy seconds: the trophies held, summed over every tick and multiplied by the tick, for the mean held.</summary>
        public float TrophySeconds;
        public int TrophiesAtEnd;
        /// <summary>Beak room the held trophies added at each trip home that began with trophies held, summed.</summary>
        public float TrophyRoomOffered;
        /// <summary>Of that room, the food the trip carried above the class capacity (the room actually used), summed.</summary>
        public float TrophyRoomFilled;
        /// <summary>Food that left this seat's beak for the centre pile because its capacity fell below its cargo.</summary>
        public float OverflowToCentre;
        /// <summary>The most one victim lost to this seat's hit-steals, Steals and breaks inside any <see cref="MatchReports.LossWindowSeconds"/> window.</summary>
        public float WorstVictimLoss5s;
        public float FirstContact;
        /// <summary>Food this seat took from each seat (index = victim).</summary>
        public float[] StolenFromSeat;
        /// <summary>Executions this seat made on each seat (index = victim).</summary>
        public int[] KillsOnSeat;
        /// <summary>Cargo the executions drained from each seat (the victim's whole beak; 0 for a shelled victim).</summary>
        public float[] KillCargoFromSeat;
        /// <summary>Food the executions on each seat really put into this seat's cargo (victim cargo first, then the bounty, held to the free room).</summary>
        public float[] KillGainedFromSeat;
        /// <summary>Food the executions on each seat paid in name only: victim cargo plus bounty, less what fitted.</summary>
        public float[] KillUnfitFromSeat;
        /// <summary>Pecks of this seat that were cut, by <see cref="CutCause"/>.</summary>
        public int[] CutsByCause;
        /// <summary>Peck casts of this seat that began with a rival holding a ready cutter of that cause in range.</summary>
        public int[] ExposedByCause;
        /// <summary>Of those exposed casts, how many were then cut by that cause.</summary>
        public int[] CutWhileExposedByCause;
    }
}
