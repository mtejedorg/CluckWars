using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Balance
{
    /// <summary>
    /// One chicken of the match simulator: its farming numbers (part 1's <see cref="OracleKit"/>), its class, its four abilities in
    /// fixed-kit order, its passive's hooks as plain factors and the game bot's profile for its class. Built by <see cref="MatchKits"/>.
    /// </summary>
    public sealed class MatchKit
    {
        public string Label;
        public ChickenClass Class;
        public OracleKit Farm;
        public MatchAbility[] Abilities;
        public MatchPassive Passive;
        public BotProfile Bot;
    }

    /// <summary>
    /// One ability as numbers. A field is zero where the ability does not own it. <see cref="Cooldown"/> already went through the
    /// owner's passive (<c>ModifyCooldown</c>); the Peck's cooldown and cast seconds are the class stats of <see cref="MatchKit.Farm"/>.
    /// </summary>
    public struct MatchAbility
    {
        public AbilityJob Job;
        /// <summary>The asset's name, for messages and for naming the asset a solver value belongs to.</summary>
        public string Asset;
        public float Cooldown;
        public float RoundStartCooldown;
        public float Duration;
        /// <summary>The ability's <c>AimRadius</c>: how far it reaches around the caster.</summary>
        public float Radius;
        public ControlEmission Control;
        public float Knockback;
        public float StealAmount;
        /// <summary>The ability's lasting area is an aura centred on its caster (Slow Area).</summary>
        public bool AreaAura;
        public float AreaRadius;
        public float AreaSlowFactor;
        public float SpeedMultiplier;
        public float HideOpacity;
        public float BlinkMetres;
        public float ShieldWalkSeconds;
        public float ShieldWalkSpeedMultiplier;
        public float ShieldEggSeconds;
        public bool StealProof;
        public bool EndsOnNextMove;
        public float MarkArmSeconds;
        public float MarkWindowSeconds;
        public float MarkKillRange;
        public float MarkSuccessCooldown;
        public float MarkFailCooldown;
        public float MarkRemovalSeconds;
        public float MarkRoundStartCooldown;
        public float MarkLeaderPickWeight;
        public BotRole Role;
        public AbilityCategory Category;
        public bool IsStealer;
        public bool IsStealShield;
    }

    /// <summary>
    /// A passive's hooks read once with no owner and a unit input, so the simulation multiplies plain factors. A hook that is not
    /// linear in its input cannot be a factor and is refused by <see cref="MatchKits"/>.
    /// </summary>
    public struct MatchPassive
    {
        public string Asset;
        /// <summary>Factor on the seconds of a stun the owner receives.</summary>
        public float StunInFactor;
        /// <summary>Factor on the seconds of a slow the owner receives (the slow's strength is <see cref="ReceivedSlowFactor"/>).</summary>
        public float SlowInDurationFactor;
        /// <summary>Factor on the seconds of every control this owner sends.</summary>
        public float OutgoingControlFactor;
        public float KnockbackFactor;
        public float StealFactor;
        public float MaxStealMultiplier;
        public float MatchEndBonusFood;
        /// <summary>Every slow factor the shipped kits emit, and what this passive turns each into when it is the victim (parallel arrays).</summary>
        public float[] SlowFactorsEmitted;
        public float[] SlowFactorsReceived;

        /// <summary>A passive that changes nothing: every factor 1. <paramref name="slowFactors"/> are the slow strengths it answers (each unchanged).</summary>
        public static MatchPassive Identity(params float[] slowFactors) => new MatchPassive
        {
            Asset = "identity",
            StunInFactor = 1f,
            SlowInDurationFactor = 1f,
            OutgoingControlFactor = 1f,
            KnockbackFactor = 1f,
            StealFactor = 1f,
            MaxStealMultiplier = 1f,
            MatchEndBonusFood = 0f,
            SlowFactorsEmitted = (float[])slowFactors.Clone(),
            SlowFactorsReceived = (float[])slowFactors.Clone(),
        };

        /// <summary>The slow factor this owner moves at when a slow of <paramref name="emitted"/> lands. Throws for a factor no kit emits.</summary>
        public float ReceivedSlowFactor(float emitted)
        {
            if (SlowFactorsEmitted != null && SlowFactorsReceived != null)
                for (int i = 0; i < SlowFactorsEmitted.Length; i++)
                    if (SlowFactorsEmitted[i] == emitted) return SlowFactorsReceived[i];
            throw new InvalidOperationException($"{Asset}: no kit emits a slow of factor {emitted}, so the passive's answer to it was never read.");
        }
    }

    /// <summary>The rules of one simulated match.</summary>
    public struct MatchRules
    {
        /// <summary>Seconds between two thinks of a policy when nothing happened; used when <see cref="ThinkSeconds"/> is not positive.</summary>
        public const float DefaultThinkSeconds = 0.25f;

        /// <summary>The farm rules of part 1: win target, clock, tick, pile slow. <see cref="FarmRules.StopAtTarget"/> is ignored: a match always ends at the target.</summary>
        public FarmRules Farm;
        /// <summary>Half the side of the box a chicken's centre may use (<c>OracleMapBaker.ArenaHalf()</c>).</summary>
        public float ArenaHalf;
        public float ThinkSeconds;
        /// <summary>Radius of the circle round each base that keeps rivals out (<c>MatchConfig.BaseKeepOutRadius</c>).</summary>
        public float KeepOutRadius;
        /// <summary>Food an execute adds to the Spoiler's bag on top of the victim's cargo (<c>MatchConfig.ExecuteBounty</c>).</summary>
        public float ExecuteBounty;
        /// <summary>Per second decay of a shove (<c>ChickenMovement.KnockbackDecayRate</c>).</summary>
        public float KnockbackDecayRate;
        /// <summary>Speed factor of an Underdog Surge (<c>ChickenMovement.UnderdogSurgeSpeedFactor</c>).</summary>
        public float SurgeSpeedFactor;
        /// <summary>The bot's arrival radius, the distance at which it drops a hidden rival it walked to (<c>OracleMenu.BotArrivalRadius()</c>).</summary>
        public float SightArrivalRadius;
        /// <summary>The comeback event; default (zero seconds) switches it off.</summary>
        public ComebackSettings Comeback;
    }
}
