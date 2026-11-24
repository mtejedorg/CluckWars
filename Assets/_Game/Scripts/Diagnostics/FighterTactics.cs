using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>A rival as the fighter sees it (world XZ).</summary>
    public struct FighterRival
    {
        public Vector2 Position;
        public float Cargo;
        /// <summary>A Peck cast is running on it (its food has not landed yet).</summary>
        public bool CastingPeck;
        public bool Stunned;
        /// <summary>Nothing reaches it right now: steal-proof (a shell) or behind a base wall.</summary>
        public bool Untouchable;
        /// <summary>It is this seat's Death Mark target.</summary>
        public bool Marked;
        /// <summary>It carries this seat's loot mark (a Thief's, BL-217): a Steal that reaches it pays the bounty even with nothing to take.</summary>
        public bool LootMarked;
    }

    /// <summary>One equipped attack. <see cref="Reach"/> is its range-only radius; <see cref="Ready"/> = it can be pressed right now (off cooldown, usable).</summary>
    public struct FighterAttack
    {
        public int Slot;
        public bool Ready;
        public AbilityJob Job;
        public float Reach;
        /// <summary>Seconds of control it emits (a Stun's length); 0 for the others.</summary>
        public float ControlSeconds;
    }

    public enum FighterMarkPhase : byte { None, Ready, Marked }

    public struct FighterView
    {
        public Vector2 Self;
        public FighterRival[] Rivals;
        public int RivalCount;
        public FighterAttack[] Attacks;
        public int AttackCount;
        /// <summary>The Death Mark slot (-1 when the kit has none). Pressing it marks while <see cref="FighterMarkPhase.Ready"/>, kills once <see cref="CanKillNow"/>.</summary>
        public int MarkSlot;
        public FighterMarkPhase Mark;
        public bool CanKillNow;
        public float ArmRemaining, WindowRemaining;
    }

    /// <summary>What the fighter does this decision: press <see cref="Slot"/> (-1 = none) and/or walk at rival <see cref="Chase"/> (-1 = keep playing the economy).</summary>
    public struct FighterDecision
    {
        public int Slot;
        public int Chase;
        public static FighterDecision None => new FighterDecision { Slot = -1, Chase = -1 };
    }

    /// <summary>
    /// The fighter personality's target choice (<c>cw_personality fighter</c>, BL-208), pure so EditMode tests pin it. It
    /// plays the kit's attacks at a legal target and nothing else: Stun and Shove at the nearest rival in reach, a rival
    /// with a Peck cast running first; Steal at a loaded rival in reach, or at the rival carrying this seat's loot mark whatever it carries
    /// (and a walk at that rival from any distance, as the game's bot does); for a Death Mark kit, mark, chase the marked rival,
    /// stun it so the stun is still running when the arm ends, and press Kill the moment it lands. The caller turns a decision
    /// into a press on the normal input path.
    /// </summary>
    public static class FighterTactics
    {
        /// <summary>Hunt a rival within this many metres when no attack is in reach (the brain keeps the economy beyond it).</summary>
        public const float ChaseMetres = 14f;
        /// <summary>Cast the stun on the marked rival once the arm has at most the stun's length minus this left, so the stun outlives the arm.</summary>
        public const float StunLeadMarginSeconds = 0.6f;
        /// <summary>No stun on the marked rival with less than this much of the window left (it would end before the Kill could follow).</summary>
        public const float MinWindowForStunSeconds = 0.4f;

        // Scores: a rival casting Peck outranks everything (the cut is the point), then a loaded Steal, then the nearest.
        private const float PeckBonus = 1000f, StealBonus = 500f, StunPreference = 0.5f, Reachable = 100f;

        public static FighterDecision Choose(in FighterView v)
        {
            var d = FighterDecision.None;

            if (v.Mark == FighterMarkPhase.Marked)
            {
                int m = MarkedRival(v);
                if (m >= 0)
                {
                    d.Chase = m;
                    if (v.MarkSlot >= 0 && v.CanKillNow) { d.Slot = v.MarkSlot; return d; }
                    var r = v.Rivals[m];
                    if (!r.Stunned && !r.Untouchable && v.WindowRemaining > MinWindowForStunSeconds)
                        for (int i = 0; i < v.AttackCount; i++)
                        {
                            var a = v.Attacks[i];
                            if (!a.Ready || a.Job != AbilityJob.Stun || (r.Position - v.Self).magnitude > a.Reach) continue;
                            if (v.ArmRemaining <= a.ControlSeconds - StunLeadMarginSeconds) { d.Slot = a.Slot; return d; }
                        }
                    return d;       // keep closing in: the stun waits for the arm
                }
            }
            else if (v.Mark == FighterMarkPhase.Ready && v.MarkSlot >= 0 && v.RivalCount > 0)
            {
                d.Slot = v.MarkSlot;
                return d;
            }

            int loot = LootMarkedRival(v);
            if (loot >= 0)
            {
                var r = v.Rivals[loot];
                d.Chase = loot;
                float dist = (r.Position - v.Self).magnitude;
                for (int i = 0; i < v.AttackCount; i++)
                {
                    var a = v.Attacks[i];
                    if (a.Ready && a.Job == AbilityJob.Steal && dist <= a.Reach) { d.Slot = a.Slot; return d; }
                }
                return d;       // keep closing in: Steal is on cooldown or the rival is out of reach
            }

            float best = float.NegativeInfinity;
            for (int i = 0; i < v.AttackCount; i++)
            {
                var a = v.Attacks[i];
                if (!a.Ready || (a.Job != AbilityJob.Stun && a.Job != AbilityJob.Shove && a.Job != AbilityJob.Steal)) continue;
                for (int j = 0; j < v.RivalCount; j++)
                {
                    var r = v.Rivals[j];
                    float dist = (r.Position - v.Self).magnitude;
                    if (r.Untouchable || dist > a.Reach) continue;
                    float score;
                    if (a.Job == AbilityJob.Steal)
                    {
                        if (r.Cargo <= 0f) continue;
                        score = StealBonus - dist;
                    }
                    else
                    {
                        if (r.Stunned) continue;        // already out of the Peck and out of the fight: do not spend the move
                        score = (r.CastingPeck ? PeckBonus : Reachable) - dist + (a.Job == AbilityJob.Stun ? StunPreference : 0f);
                    }
                    if (score > best) { best = score; d.Slot = a.Slot; }
                }
            }
            if (d.Slot >= 0) return d;

            d.Chase = ChaseTarget(v);
            return d;
        }

        /// <summary>The rival carrying this seat's loot mark, unless nothing reaches it right now (a shell, a base wall): the bot abandons it the same way.</summary>
        private static int LootMarkedRival(in FighterView v)
        {
            for (int i = 0; i < v.RivalCount; i++) if (v.Rivals[i].LootMarked && !v.Rivals[i].Untouchable) return i;
            return -1;
        }

        private static int MarkedRival(in FighterView v)
        {
            for (int i = 0; i < v.RivalCount; i++) if (v.Rivals[i].Marked) return i;
            return -1;
        }

        /// <summary>The rival to walk at: one casting Peck, else a loaded one when the kit steals, else the nearest, within <see cref="ChaseMetres"/>.</summary>
        private static int ChaseTarget(in FighterView v)
        {
            bool steals = false, attacks = false;
            // Chase with what the kit has, ready or not: a move on cooldown is back by the time the fighter arrives.
            for (int i = 0; i < v.AttackCount; i++)
            {
                var j = v.Attacks[i].Job;
                if (j == AbilityJob.Steal) steals = true;
                if (j == AbilityJob.Stun || j == AbilityJob.Shove || j == AbilityJob.Steal) attacks = true;
            }
            if (!attacks) return -1;

            int pick = -1;
            float best = float.NegativeInfinity;
            for (int j = 0; j < v.RivalCount; j++)
            {
                var r = v.Rivals[j];
                float dist = (r.Position - v.Self).magnitude;
                if (r.Untouchable || dist > ChaseMetres) continue;
                float score = -dist + (r.CastingPeck ? PeckBonus : 0f) + (steals && r.Cargo > 0f ? StealBonus : 0f);
                if (score > best) { best = score; pick = j; }
            }
            return pick;
        }
    }
}
