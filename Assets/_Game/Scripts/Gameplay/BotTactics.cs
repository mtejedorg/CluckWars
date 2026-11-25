using System.Collections.Generic;
using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// What a bot is fundamentally trying to do all match. One per class, and the
    /// difference between them is behavioural, not numeric — each strategy visits a
    /// different set of <see cref="BotSituation"/>s and answers "what should I cast"
    /// differently in the ones they share.
    /// </summary>
    /// <remarks>
    /// This exists because per-class personality used to be six serialized floats
    /// (hunt radius, protect threshold, …) with one shared code path. Numbers alone
    /// cannot express "the Fatty defends its base" or "the Assassin robs whoever is
    /// winning" — and worse, the Fatty's tuning (<c>huntRadius = 0</c>) meant it never
    /// entered Hunt, so Belly Flop, Ground Quake and Roll Push were unreachable for the
    /// entire class. Every ability a class can equip must be reachable by some situation
    /// its strategy actually enters; <c>BotTacticsTests</c> pins that.
    /// </remarks>
    public enum BotStrategy : byte
    {
        /// <summary>Warrior. Taxes the map: holds the pile it is working and robs whoever walks in.</summary>
        Brawler  = 0,
        /// <summary>Speedy. Optimises trips: short round-trips, refuses fights, banks constantly.</summary>
        Runner   = 1,
        /// <summary>Fatty. Holds ground: big hauls, and everything entering its threat radius is punished.</summary>
        Bunker   = 2,
        /// <summary>Assassin. Hunts value: stalks the richest / leading rival, robs, disengages, banks.</summary>
        Predator = 3,
    }

    /// <summary>
    /// The tactical context a cast decision is made in. Deliberately distinct from
    /// <c>BotController</c>'s FSM state: several states share a situation (Hunt and Stalk
    /// are both <see cref="Engaging"/> once in reach) and one state produces several
    /// (ReturnToBase is <see cref="Banking"/> at the base and <see cref="Retreating"/>
    /// while a rival is on its tail).
    /// </summary>
    public enum BotSituation : byte
    {
        /// <summary>Loaded, being chased, running home. Shed the pursuer, protect the cargo.</summary>
        Retreating = 0,
        /// <summary>Standing on the base dumping cargo. Only deposit accelerators matter here.</summary>
        Banking    = 1,
        /// <summary>Closing on / fighting a rival worth attacking.</summary>
        Engaging   = 2,
        /// <summary>A rival is standing on the pile this bot wants. Displace them.</summary>
        Contesting = 3,
        /// <summary>Walking somewhere with nobody relevant nearby. Mobility only.</summary>
        Transiting = 4,
        /// <summary>Holding a position (own base / the pile being worked). Punish anything close.</summary>
        Guarding   = 5,
    }

    /// <summary>How far into the round we are. Drives the bank-it-or-lose-it endgame rules.</summary>
    public enum MatchPhase : byte
    {
        Opening = 0,
        Mid     = 1,
        Endgame = 2,
    }

    /// <summary>What a bot holding a Death Mark does next (<see cref="BotTactics.NextMarkStep"/>).</summary>
    public enum BotMarkStep : byte
    {
        /// <summary>No mark up: the ordinary FSM decides.</summary>
        None = 0,
        /// <summary>Hunt the target across the map, keeping the stun back: a stun that ends before the arm is wasted.</summary>
        Hunt = 1,
        /// <summary>Target in reach, and a stun cast now still runs when the arm ends: land it.</summary>
        Stun = 2,
        /// <summary>Stunned and in kill range: press again.</summary>
        Kill = 3,
    }

    /// <summary>
    /// The per-class numeric envelope a <see cref="BotStrategy"/> runs inside. Kept
    /// separate from the strategy itself so the <i>shape</i> of the behaviour and its
    /// <i>aggression dial</i> can be tuned independently.
    /// </summary>
    public readonly struct BotProfile
    {
        public readonly BotStrategy Strategy;

        /// <summary>Cargo fraction at which the bot heads home under normal conditions.</summary>
        public readonly float ReturnThreshold;

        /// <summary>
        /// Food carried at which the bot heads home whatever its capacity (0 = none: the fraction alone decides). Read through
        /// <see cref="BotTactics.ReturnThreshold(in BotProfile, MatchPhase, bool, float)"/>; it applies only to a beak at least this big.
        /// </summary>
        public readonly float ReturnAmount;

        /// <summary>Cargo fraction above which a nearby rival is treated as a threat to the haul.</summary>
        public readonly float ProtectCargoThreshold;

        /// <summary>A rival this close while loaded triggers the retreat.</summary>
        public readonly float DangerRadius;

        /// <summary>How far the bot will travel to attack a rival worth attacking. 0 = never chases.</summary>
        public readonly float HuntRadius;

        /// <summary>Rival cargo fraction that makes them worth chasing at all.</summary>
        public readonly float HuntCargoThreshold;

        /// <summary>Radius inside which an empty-handed rival still gets attacked. 0 = never.</summary>
        public readonly float EngageRadius;

        /// <summary>Radius around the bot's anchor (own base, or the pile it works) that intruders are punished in. 0 = does not guard.</summary>
        public readonly float GuardRadius;

        /// <summary>How strongly pile choice weights the walk home. 0 = nearest pile wins; 1 = full round-trip cost.</summary>
        public readonly float RoundTripBias;

        /// <summary>Extra weight on a rival's banked score when choosing whom to attack. 0 = ignore the scoreboard.</summary>
        public readonly float LeaderFocus;

        /// <summary>Seconds between FSM re-evaluations.</summary>
        public readonly float ThinkInterval;

        public BotProfile(BotStrategy strategy, float returnThreshold, float protectCargoThreshold,
            float dangerRadius, float huntRadius, float huntCargoThreshold, float engageRadius,
            float guardRadius, float roundTripBias, float leaderFocus, float thinkInterval, float returnAmount = 0f)
        {
            Strategy              = strategy;
            ReturnThreshold       = returnThreshold;
            ProtectCargoThreshold = protectCargoThreshold;
            DangerRadius          = dangerRadius;
            HuntRadius            = huntRadius;
            HuntCargoThreshold    = huntCargoThreshold;
            EngageRadius          = engageRadius;
            GuardRadius           = guardRadius;
            RoundTripBias         = roundTripBias;
            LeaderFocus           = leaderFocus;
            ThinkInterval         = thinkInterval;
            ReturnAmount          = returnAmount;
        }

        /// <summary>Can this profile chase a rival down? False for the two classes that hold position.</summary>
        public bool Chases => HuntRadius > 0f || EngageRadius > 0f;

        /// <summary>Does this profile punish intruders near whatever it is doing?</summary>
        public bool Guards => GuardRadius > 0f;
    }

    /// <summary>
    /// Pure, MonoBehaviour-free bot decision logic — the same split
    /// <see cref="AbilityAim"/> and <c>FoodPileMath</c> already make. Everything here is
    /// a function of numbers, so <c>BotTacticsTests</c> can exercise a bot's judgement
    /// without a <c>NetworkRunner</c>, a scene, or a Play Mode session.
    /// </summary>
    /// <remarks>
    /// <c>BotController</c> is the glue: it perceives, calls in here to decide, and acts.
    /// Nothing in this class touches Unity lifecycle, physics, or networked state.
    /// </remarks>
    public static class BotTactics
    {
        // ---- Class table ----------------------------------------------------

        /// <summary>
        /// The strategy and envelope for <paramref name="cls"/>, from the one class table
        /// (<see cref="ChickenClasses"/>, A17); <c>BotController</c> reads it and never
        /// switches on <see cref="ChickenClass"/> itself. An unknown class gets the Warrior's.
        /// </summary>
        public static BotProfile ProfileFor(ChickenClass cls) => ChickenClasses.Of(cls).Bot;

        /// <summary>
        /// Identity prefix for <c>[Bot]</c> log lines: class and home corner, e.g.
        /// <c>"Warrior@c2"</c>; <c>"c?"</c> while the corner is unassigned (-1).
        /// </summary>
        /// <remarks>
        /// Without it the FSM transition lines of four bots were indistinguishable in a merged
        /// log, so per-bot Flee/Hunt flip rates could not be measured (decision A8). Format
        /// is pinned by <c>BotTacticsTests</c>; log parsers may rely on it.
        /// </remarks>
        public static string LogIdentity(ChickenClass cls, int homeCornerIndex) =>
            homeCornerIndex >= 0 ? $"{cls}@c{homeCornerIndex}" : $"{cls}@c?";

        // ---- Cast plans -----------------------------------------------------
        // Static arrays, never built per call: this is read inside the think tick and a
        // fresh BotRole[] per bot per tick is hundreds of allocations a second.

        private static readonly BotRole[] RetreatDefault  = { BotRole.Escape, BotRole.Defense, BotRole.Control };
        // Escape appended LAST, deliberately below Control/Offense/Defense: the Brawler
        // turns and fights first, per its own essence ("does not run" — see CastPlan's
        // Retreating case below). But priority is a strict ladder (ScoreCastCandidate's
        // cooldown tiebreak is bounded below one whole role step, so it can never let a
        // later role beat an earlier one) — so this ONLY fires when every Control/Offense/
        // Defense ability the Warrior holds is on cooldown. Without it, Ruffle
        // (RuffleAbilitySO, the Warrior's only Escape-role ability) had no CastPlan branch
        // that ever reached BotRole.Escape and was structurally dead on every Brawler bot,
        // the exact "authored, gated, unreachable" bug class this file exists to prevent —
        // found in the 2026-08-24 multi-agent review, BotTacticsTests pins it.
        private static readonly BotRole[] RetreatBrawler  = { BotRole.Control, BotRole.Offense, BotRole.Defense, BotRole.Escape };
        private static readonly BotRole[] RetreatBunker   = { BotRole.Defense, BotRole.Control, BotRole.Offense };
        private static readonly BotRole[] RetreatPredator = { BotRole.Escape, BotRole.Control, BotRole.Defense };

        private static readonly BotRole[] BankPlan        = { BotRole.Bank };

        private static readonly BotRole[] EngageLoaded    = { BotRole.Steal, BotRole.Control, BotRole.Offense };
        private static readonly BotRole[] EngageEmpty     = { BotRole.Control, BotRole.Offense };
        private static readonly BotRole[] EngagePredator  = { BotRole.Steal, BotRole.Offense, BotRole.Control };

        private static readonly BotRole[] ContestPlan     = { BotRole.Control, BotRole.Offense, BotRole.Steal };
        // Defense inserted after Control, ahead of Offense/Steal: Guarding is the one
        // genuinely ANTICIPATORY situation (a rival has entered the guard radius, nothing
        // has landed yet — see BotController's Priority 4 gate), so it is where a
        // pre-emptive Defense ability belongs. Without this, ImmovableAbilitySO's own doc
        // comment ("spent in anticipation... a read rather than a reflex") was contradicted
        // in practice: the only situation carrying BotRole.Defense for the Bunker was
        // Retreating, which by construction requires a rival ALREADY inside DangerRadius
        // with cargo loaded — i.e. Immovable could only ever be cast reactively, backwards
        // from its design. Found in the 2026-08-24 multi-agent review. Safe for the
        // Brawler, which shares this array via CastPlan's default case but has no
        // Defense-role ability in its kit to match — IndexInPlan simply never finds one.
        private static readonly BotRole[] GuardPlan       = { BotRole.Control, BotRole.Defense, BotRole.Offense, BotRole.Steal };

        private static readonly BotRole[] TransitRunner   = { BotRole.Escape };
        private static readonly BotRole[] TransitNone     = { };

        /// <summary>
        /// Which <see cref="BotRole"/>s to try, in order, for this strategy in this
        /// situation. The best ready-and-able match wins; see
        /// <see cref="ScoreCastCandidate"/> for how ties inside one role are broken.
        /// </summary>
        /// <param name="targetIsLoaded">
        /// Only read in <see cref="BotSituation.Engaging"/>. Robbing an empty rival does
        /// nothing, so a Steal-first plan against one would burn the cooldown for zero —
        /// the empty-target plan leads with Control instead.
        /// </param>
        public static BotRole[] CastPlan(BotStrategy strategy, BotSituation situation, bool targetIsLoaded)
        {
            switch (situation)
            {
                case BotSituation.Banking:
                    return BankPlan;

                case BotSituation.Retreating:
                    // The Brawler does not run: being chased while loaded is the fight it
                    // wanted, so it turns and controls. The Bunker cannot outrun anyone, so
                    // it turtles. Only the two fast classes actually disengage.
                    return strategy switch
                    {
                        BotStrategy.Brawler  => RetreatBrawler,
                        BotStrategy.Bunker   => RetreatBunker,
                        BotStrategy.Predator => RetreatPredator,
                        _                    => RetreatDefault,
                    };

                case BotSituation.Engaging:
                    if (!targetIsLoaded) return EngageEmpty;
                    return strategy == BotStrategy.Predator ? EngagePredator : EngageLoaded;

                case BotSituation.Contesting:
                    return ContestPlan;

                case BotSituation.Guarding:
                    return GuardPlan;

                case BotSituation.Transiting:
                    // Mobility on the open road is the Runner's identity — Speed Burst is
                    // for making the trip shorter, not only for panicking. Every other
                    // strategy saves its escape for the moment it is actually needed.
                    return strategy == BotStrategy.Runner ? TransitRunner : TransitNone;

                default:
                    return TransitNone;
            }
        }

        /// <summary>
        /// Every <see cref="BotSituation"/> a profile can actually reach, so a caller can ask
        /// "is there any path from this profile to a cast of role X" without re-deriving
        /// <c>BotController</c>'s branch conditions.
        /// </summary>
        /// <remarks>
        /// <b>Getting this wrong is the Fatty bug.</b> That class could equip Belly Flop,
        /// Ground Quake and Roll Push and reach none of them, because the only situation
        /// producing an offensive cast was gated behind a hunt radius it deliberately set to
        /// zero. The check that catches it has to model reachability honestly, and an
        /// earlier version of this — three radii OR-ed together — was too coarse the other
        /// way: it declared the Speedy dead-kitted when in fact it reaches its Feather Trap
        /// and Feather Aura through Retreating and Contesting.
        ///
        /// Four situations are unconditional. Every bot banks, transits, is chased while
        /// loaded (<see cref="BotProfile.DangerRadius"/> is never zero) and can find a rival
        /// standing on the pile it wants. Only Engaging and Guarding are gated.
        /// </remarks>
        public static BotSituation[] ReachableSituations(in BotProfile profile)
        {
            var reachable = new List<BotSituation>(6)
            {
                BotSituation.Banking,
                BotSituation.Transiting,
                BotSituation.Contesting,
            };
            if (profile.DangerRadius > 0f) reachable.Add(BotSituation.Retreating);
            if (profile.Chases)            reachable.Add(BotSituation.Engaging);
            if (profile.Guards)            reachable.Add(BotSituation.Guarding);
            return reachable.ToArray();
        }

        // ---- Reach ----------------------------------------------------------

        /// <summary>
        /// Sentinel reach for an ability that needs no target. Returning 0 would read
        /// identically to "cannot reach anything" at the call site and would make every
        /// self-buff in the game uncastable.
        /// </summary>
        public const float SelfCastReach = float.PositiveInfinity;

        /// <summary>
        /// How far from the caster this ability can actually reach, in metres, derived
        /// from its declared aim shape.
        /// </summary>
        /// <remarks>
        /// This is the bot's cast gate. It replaces a single flat <c>_abilityRange = 4</c>
        /// that was wrong for nearly the whole roster in both directions — Root Egg reaches
        /// 1.45 m and was being fired from 4 m away every time, while Roll Push (9.1 m) and
        /// Mark Kill (8.0 m) were never fired until the target was less than half their
        /// reach away.
        ///
        /// Mirrors <see cref="AbilityBaseSO.AimForwardOffset"/>'s documented contract:
        /// total forward reach is offset + radius for the shapes that project forward, and
        /// radius alone for the caster-centred ones. Derived from the shape rather than
        /// read off <see cref="AbilityBaseSO.IndicatorRange"/>, because a subclass that
        /// forgets to override IndicatorRange would otherwise hand the bot a reach of 0.
        /// </remarks>
        public static float EffectiveReach(AbilityAimShape shape, float radius, float forwardOffset)
        {
            switch (shape)
            {
                case AbilityAimShape.None:
                    return SelfCastReach;

                case AbilityAimShape.SelfCircle:
                case AbilityAimShape.Aura:
                case AbilityAimShape.Cone:
                case AbilityAimShape.SingleTarget:
                    return Mathf.Max(0f, radius);

                case AbilityAimShape.ForwardCircle:
                case AbilityAimShape.Jump:
                case AbilityAimShape.Capsule:
                    return Mathf.Max(0f, forwardOffset) + Mathf.Max(0f, radius);

                default:
                    return Mathf.Max(0f, radius);
            }
        }

        /// <summary>Convenience overload reading the shape straight off the ability.</summary>
        public static float EffectiveReach(AbilityBaseSO ability) =>
            ability == null
                ? 0f
                : EffectiveReach(ability.AimShape, ability.AimRadius, ability.AimForwardOffset);

        // ---- Aim ------------------------------------------------------------

        /// <summary>
        /// Fraction of an ability's reach at which the bot commits to the cast. Firing at
        /// the very edge means the target walks out of the shape during the wind-up; firing
        /// only at point blank throws away most of a long-reach ability.
        /// </summary>
        public const float CommitReachFraction = 0.85f;

        /// <summary>
        /// How far off-axis the bot may be and still land the cast, in degrees. Radial
        /// shapes do not care about facing at all; a cone gets half its own arc minus a
        /// margin; the forward-projected shapes get a tolerance that narrows as they get
        /// longer, because at 9 m a 20 degree error is a 3 m miss.
        /// </summary>
        public static float AimToleranceDeg(AbilityAimShape shape, float coneAngleDeg, float radius, float forwardOffset)
        {
            switch (shape)
            {
                case AbilityAimShape.None:
                case AbilityAimShape.SelfCircle:
                case AbilityAimShape.Aura:
                case AbilityAimShape.SingleTarget:
                    return 180f; // rotation-invariant — never block the cast on facing

                case AbilityAimShape.Cone:
                    // Half-arc, minus a margin so a target on the rim isn't a coin flip.
                    return Mathf.Clamp(coneAngleDeg * 0.5f - 8f, 5f, 180f);

                case AbilityAimShape.ForwardCircle:
                case AbilityAimShape.Jump:
                case AbilityAimShape.Capsule:
                {
                    // The angle at which a target at `forwardOffset` metres sits exactly
                    // `radius` off the axis — i.e. still just inside the shape.
                    float len = Mathf.Max(0.01f, forwardOffset);
                    float r   = Mathf.Max(0f, radius);
                    return Mathf.Clamp(Mathf.Atan2(r, len) * Mathf.Rad2Deg, 4f, 90f);
                }

                default:
                    return 180f;
            }
        }

        /// <summary>Convenience overload reading the shape straight off the ability.</summary>
        public static float AimToleranceDeg(AbilityBaseSO ability) =>
            ability == null
                ? 180f
                : AimToleranceDeg(ability.AimShape, ability.AimConeAngle, ability.AimRadius, ability.AimForwardOffset);

        /// <summary>
        /// Planar (XZ) angle in degrees between where the caster is looking and where the
        /// target is. Y is dropped for the same reason <see cref="AbilityAim"/> drops it:
        /// every aim shape in the game is a flat footprint.
        /// </summary>
        public static float FacingErrorDeg(Vector3 forward, Vector3 toTarget)
        {
            forward = Planar.Flat(forward);
            toTarget = Planar.Flat(toTarget);
            if (forward.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(forward, toTarget);
        }

        /// <summary>
        /// Is the bot pointed close enough at <paramref name="toTarget"/> to fire
        /// <paramref name="ability"/> and have it land? False means "turn first, cast next
        /// tick" — not "give up".
        /// </summary>
        /// <remarks>
        /// This is the single biggest reason bots looked stupid. They only ever faced their
        /// movement direction and cast the instant a rival came within a flat 4 m, so every
        /// Cone and Capsule ability in the game — Headbutt, Wing Slam, Snatch, Roll Push,
        /// Roll Trample — fired at whatever heading the navmesh happened to leave them on.
        /// </remarks>
        public static bool IsAimedWellEnough(AbilityBaseSO ability, Vector3 forward, Vector3 toTarget) =>
            FacingErrorDeg(forward, toTarget) <= AimToleranceDeg(ability);

        // ---- Cast candidate scoring -----------------------------------------

        /// <summary>
        /// Rank for one castable slot. <b>Lower wins.</b> Role preference dominates — a
        /// plan that asks for Steal first means Steal first — and within a single role the
        /// cheaper cooldown wins.
        /// </summary>
        /// <remarks>
        /// The cooldown tiebreak is what stops a Warrior opening every skirmish with Wing
        /// Slam (12 s) when Headbutt (4 s) would have done the same job, then standing
        /// there with nothing for the next ten seconds. Bounded below one whole role step
        /// so it can never reorder the plan itself.
        /// </remarks>
        public static float ScoreCastCandidate(int rolePriorityIndex, float cooldownSeconds)
        {
            float cdPenalty = Mathf.Clamp01(cooldownSeconds / 30f) * 0.9f;
            return rolePriorityIndex + cdPenalty;
        }

        // ---- Pile selection --------------------------------------------------

        /// <summary>
        /// Cost of working a pile <paramref name="distToPile"/> metres away and hauling the
        /// result home. <b>Lower wins.</b>
        /// </summary>
        /// <remarks>
        /// <paramref name="roundTripBias"/> is the whole per-class difference. At 0 this
        /// degenerates to "nearest pile", which is what every bot used to do and is why they
        /// all converged on the centre island and then walked the full diagonal home. At 1
        /// (Speedy) the walk home counts in full, so a slightly further pile that sits on the
        /// way back to base beats a close one behind enemy lines.
        ///
        /// <paramref name="available"/> discounts a rich pile: a pile with two pecks left in
        /// it is not worth a trip even when it is underfoot.
        /// </remarks>
        public static float PileCost(float distToPile, float pileToBase, float available, float roundTripBias)
        {
            float travel = Mathf.Max(0f, distToPile) + Mathf.Max(0f, roundTripBias) * Mathf.Max(0f, pileToBase);

            // Richness discount, bounded: a full pile is worth up to 30% off the trip, and
            // past ~15 units more food stops mattering — a beak only holds so much per visit.
            float richness = Mathf.Clamp01(Mathf.Max(0f, available) / 15f);
            return travel * (1f - 0.30f * richness);
        }

        // ---- Target selection ------------------------------------------------

        /// <summary>
        /// How badly this bot wants to attack a given rival. <b>Higher wins.</b> Returns 0
        /// for a rival not worth attacking at all.
        /// </summary>
        /// <remarks>
        /// Proximity used to be the entire rule, which made every hunt a race to whoever
        /// happened to be closest — usually the bot farming quietly next door rather than
        /// the human about to win. <paramref name="leaderFocus"/> is what lets the Assassin
        /// ignore an easy empty-handed target and cross the map for the leader.
        /// </remarks>
        /// <param name="cargoFraction">Rival's cargo as a fraction of their capacity.</param>
        /// <param name="bankedShare">Rival's banked food as a fraction of the win target, clamped [0,1].</param>
        /// <param name="reachRadius">The bot's own hunt radius; beyond it the rival scores 0.</param>
        public static float TargetPriority(float dist, float cargoFraction, float bankedShare,
                                           float reachRadius, float leaderFocus)
        {
            if (reachRadius <= 0f || dist > reachRadius) return 0f;

            // Closeness in [0,1]: 1 underfoot, 0 at the edge of reach.
            float closeness = 1f - Mathf.Clamp01(dist / reachRadius);

            float value = 1f
                        + 2.0f * Mathf.Clamp01(cargoFraction)
                        + Mathf.Max(0f, leaderFocus) * 2.5f * Mathf.Clamp01(bankedShare);

            // Closeness matters, but never enough to let an empty neighbour outrank a loaded
            // leader across the arena — that inversion is the bug this exists to fix.
            return value * (0.45f + 0.55f * closeness);
        }

        // ---- Match phase and the bank-it-or-lose-it rule ----------------------

        /// <summary>Fraction of the match remaining at or below which the endgame rules apply.</summary>
        public const float EndgameFraction = 0.28f;

        /// <summary>Fraction remaining at or above which the opening rules still apply.</summary>
        public const float OpeningFraction = 0.75f;

        public static MatchPhase ResolvePhase(float timeRemaining, float matchDuration)
        {
            if (matchDuration <= 0f) return MatchPhase.Mid;
            float f = Mathf.Clamp01(timeRemaining / matchDuration);
            if (f <= EndgameFraction) return MatchPhase.Endgame;
            if (f >= OpeningFraction) return MatchPhase.Opening;
            return MatchPhase.Mid;
        }

        /// <summary>Safety margin on the walk home, as a multiplier on the straight-line travel estimate.</summary>
        public const float BankTravelSafetyFactor = 1.45f;

        /// <summary>
        /// Must the bot abandon what it is doing and bank <i>right now</i> to avoid holding
        /// unbanked cargo when the clock hits zero?
        /// </summary>
        /// <remarks>
        /// Cargo in the beak scores nothing. A 45-second match (<c>MatchConfig.asset</c>)
        /// leaves very little room to be wrong about this, and bots previously had no concept
        /// of the clock at all — they would start a fresh trip to the far pile with four
        /// seconds left and finish the match holding a full load worth zero.
        ///
        /// The safety factor covers what a straight-line estimate cannot: navmesh detours
        /// around walls and piles, the pile slow, and the deposit itself not being
        /// instantaneous. Being early costs a couple of seconds of foraging; being late costs
        /// the entire haul.
        /// </remarks>
        public static bool MustBankNow(float cargo, float distToBase, float moveSpeed,
                                       float timeRemaining, float depositSeconds)
        {
            if (cargo <= 0f) return false;
            if (timeRemaining <= 0f) return false;
            if (moveSpeed <= 0.01f) return true; // cannot estimate — assume the worst and go

            float travel = (Mathf.Max(0f, distToBase) / moveSpeed) * BankTravelSafetyFactor;
            return timeRemaining <= travel + Mathf.Max(0f, depositSeconds);
        }

        /// <summary>
        /// The cargo fraction this bot heads home at, after the clock and the scoreboard have
        /// had their say.
        /// </summary>
        /// <remarks>
        /// Endgame while trailing pushes the threshold up: a bot that cannot win by banking
        /// neatly should be taking risks, holding a bigger load and picking fights. Endgame
        /// while leading pulls it down hard: bank early, bank often, and stop giving the
        /// human something to rob.
        /// </remarks>
        public static float ReturnThreshold(in BotProfile profile, MatchPhase phase, bool leading)
        {
            float t = profile.ReturnThreshold;
            if (phase != MatchPhase.Endgame) return Mathf.Clamp01(t);
            return Mathf.Clamp01(leading ? t * 0.55f : Mathf.Min(1f, t * 1.20f));
        }

        /// <summary>
        /// The cargo fraction this bot heads home at, for a beak of <paramref name="capacity"/> food: <see cref="ReturnThreshold(in BotProfile, MatchPhase, bool)"/>,
        /// or the share of the beak that <see cref="BotProfile.ReturnAmount"/> food is, whichever is lower. A profile without an amount, or a beak
        /// smaller than it, keeps the fraction (Maestro, 2026-11-25: the Warrior banks at 12 food however big its beak, so a bigger beak is
        /// not more time carrying food that a rival can take).
        /// </summary>
        public static float ReturnThreshold(in BotProfile profile, MatchPhase phase, bool leading, float capacity)
        {
            float fraction = ReturnThreshold(profile, phase, leading);
            if (profile.ReturnAmount <= 0f || capacity < profile.ReturnAmount) return fraction;
            return Mathf.Min(fraction, profile.ReturnAmount / capacity);
        }

        // ---- Steal in passing ---------------------------------------------------

        /// <summary>
        /// Does a bot on its way home cast Steal now, where it stands? Only the Brawler (the Warrior) does; the rival must carry something to take, the
        /// bot must have room for it, and the rival must be inside Steal's commit reach: the cast never moves the bot off its path (no detour, no chase).
        /// </summary>
        public static bool ShouldStealInPassing(in BotProfile profile, float ownRoom, float rivalStealable, float rivalDist, float stealReach) =>
            profile.Strategy == BotStrategy.Brawler && ownRoom >= StealInPassingMinRoom && rivalStealable > 0f
            && rivalDist <= stealReach * CommitReachFraction;

        /// <summary>Free room below which a steal on the way home is not worth the cooldown (a steal moves whole food).</summary>
        public const float StealInPassingMinRoom = 1f;

        // ---- Death Mark follow-through ------------------------------------------

        /// <summary>
        /// True while a bot must bank before it may press a new Death Mark: it is loaded to its return threshold, or it is already
        /// heading home with food in the beak (the return latch keeps banking until empty, so a drop below the threshold mid-deposit
        /// is no licence to leave with food still carried).
        /// </summary>
        public static bool LoadedToBank(float cargo, float cargoFraction, float returnThreshold, bool returning) =>
            cargoFraction >= returnThreshold || (returning && cargo > 0f);

        /// <summary>How much of the stun a bot keeps beyond the arm: room for a tick of replication and the Kill press.</summary>
        public const float StunLeadMargin = 0.25f;

        /// <summary>
        /// The Spoiler bot's hunt. A mark lands anywhere on the map, and the CastPlan ladder knows none of that: left to it, a
        /// bot fires Ambush at whoever is near and walks where the FSM says, so the mark expires unpressed. The Kill needs the
        /// stun live at the arm (3 s into a 5 s mark), so the stun goes out only once it will still run
        /// <see cref="StunLeadMargin"/> past the arm, and while the window has more than that margin left.
        /// </summary>
        /// <param name="armRemaining">Seconds until the mark arms (0 once armed).</param>
        /// <param name="windowRemaining">Seconds until the mark expires.</param>
        /// <param name="stunInReach">A ready stun would land on the target now (and the target is neither shelled nor control-immune).</param>
        /// <param name="stunSeconds">That stun's length on this target, after its passive (Bulwark 0.65, Relentless 0.75; Slippery leaves stuns whole since 2026-11-05).</param>
        public static BotMarkStep NextMarkStep(bool marked, bool canKill, float armRemaining, float windowRemaining,
            bool stunInReach, float stunSeconds)
        {
            if (!marked) return BotMarkStep.None;
            if (canKill) return BotMarkStep.Kill;
            bool lastsPastTheArm = armRemaining <= stunSeconds - StunLeadMargin;
            return stunInReach && lastsPastTheArm && windowRemaining > StunLeadMargin ? BotMarkStep.Stun : BotMarkStep.Hunt;
        }

        // ---- Steal guard (Phase 7 chunk D) ------------------------------------

        /// <summary>A rival with a steal ability this close is about to rob a loaded bot (Sneaky Steal's reach).</summary>
        public const float StealGuardRadius = 5.4f;

        /// <summary>Below this much cargo there is nothing worth hiding.</summary>
        public const float StealGuardMinCargo = 5f;

        /// <summary>
        /// Does <paramref name="ability"/> take cargo off a rival? Snatch, Sneaky Steal, Scrap, Dive Bomb
        /// and Spine Coat declare a nominal steal amount; Death Mark's execute moves the whole load
        /// (<c>RPC_TransferAllToExecutor</c>), which has no nominal amount.
        /// </summary>
        public static bool IsStealer(AbilityBaseSO ability) =>
            ability != null && (ability.NominalStealAmount > 0f || ability.Is(AbilityJob.DeathMark));

        /// <summary>Is <paramref name="ability"/> a steal-proof shell (Egg Shell, Turtle Mode)?</summary>
        public static bool IsStealShield(AbilityBaseSO ability) =>
            ability != null && ability.GrantsStealProof;

        /// <summary>
        /// Cast a shell when a loaded bot has a stealer in reach. <paramref name="nearestStealerDist"/>
        /// is the planar distance to the closest rival owning a steal ability
        /// (<see cref="float.MaxValue"/> when none).
        /// </summary>
        public static bool ShouldStealGuard(float ownCargo, float nearestStealerDist, bool alreadyProof) =>
            !alreadyProof && ownCargo >= StealGuardMinCargo && nearestStealerDist <= StealGuardRadius;
    }
}
