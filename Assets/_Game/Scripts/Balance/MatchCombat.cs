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
        public static float StealTake(float nominal, float thiefFactor, float thiefFreeRoom, float victimCargo, float victimBag) =>
            StealMath.Clamp(nominal * thiefFactor, thiefFreeRoom, StealRules.Stealable(victimCargo, victimBag));

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
        public int Casts;
        public int Steals;
        public float FirstContact;
        /// <summary>Food this seat took from each seat (index = victim).</summary>
        public float[] StolenFromSeat;
        /// <summary>Pecks of this seat that were cut, by <see cref="CutCause"/>.</summary>
        public int[] CutsByCause;
        /// <summary>Peck casts of this seat that began with a rival holding a ready cutter of that cause in range.</summary>
        public int[] ExposedByCause;
        /// <summary>Of those exposed casts, how many were then cut by that cause.</summary>
        public int[] CutWhileExposedByCause;
    }
}
