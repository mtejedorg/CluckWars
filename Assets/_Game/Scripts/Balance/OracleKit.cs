using System;

namespace CluckWars.Balance
{
    /// <summary>
    /// One chicken as the farming simulation sees it: class stats, the passive's hooks and the farming-relevant
    /// abilities, already resolved to numbers. Built from the shipped assets by <c>OracleKits</c>; the simulation never reads an asset.
    /// </summary>
    [Serializable]
    public struct OracleKit
    {
        public string Label;
        /// <summary>False for a kit that never collects food (both Sneaky kits): the run reports nothing for it.</summary>
        public bool Forages;
        public float MoveSpeed;
        public int CargoCapacity;
        public float PeckAmount;
        public float PeckCooldown;
        /// <summary>Seconds from a peck starting to its food arriving. The cycle between two pecks is max(PeckCooldown, PeckCastSeconds).</summary>
        public float PeckCastSeconds;
        /// <summary>Food per second banked while standing at the base.</summary>
        public float DepositRate;
        /// <summary>True when the kit holds a Jump ability: it walks the jump leg table.</summary>
        public bool Jumps;
        public bool IgnoresPileSlow;
        /// <summary>The Speed ability: move speed multiplier while travelling, used whenever ready. Multiplier of 1 or less, or zero seconds, means no burst.</summary>
        public float BurstMultiplier;
        public float BurstSeconds;
        public float BurstCooldown;
    }

    /// <summary>Starting food of each pile kind.</summary>
    [Serializable]
    public struct OracleFood
    {
        public float Personal;
        public float Contested;
        public float Centre;
    }

    public enum FarmRoute : byte { Nearest, SidesFirst, CentreFirst, EasySideThenCentre }

    public enum FarmFoodEvent : byte { None, GoldenPile, Restock }

    /// <summary>The match rules of one run.</summary>
    [Serializable]
    public struct FarmRules
    {
        /// <summary>The game's tick rate; used when <see cref="TickSeconds"/> is not positive.</summary>
        public const float DefaultTickSeconds = 1f / 30f;

        public float WinTarget;
        public float ClockSeconds;
        /// <summary>True: a seat stops once it has banked <see cref="WinTarget"/> (and banks exactly that). False: it keeps farming to the clock.</summary>
        public bool StopAtTarget;
        public float TickSeconds;
        public FarmFoodEvent Event;
        /// <summary>The event fires when this many seconds are left on the clock.</summary>
        public float EventSecondsLeft;
        /// <summary>Added to the centre pile.</summary>
        public float GoldenPileFood;
        /// <summary>Added to every pile.</summary>
        public float RestockFood;
        /// <summary>Move-speed factor while standing in reach of a non-empty pile; zero or less means 1 (no slow).</summary>
        public float PileSlowFactor;
    }

    [Serializable]
    public struct FarmSeat
    {
        public bool Present;
        public OracleKit Kit;
        public FarmRoute Route;
    }

    [Serializable]
    public struct FarmSeatResult
    {
        public float Banked;
        /// <summary>Clock time the seat first banked <see cref="FarmRules.WinTarget"/>; -1 when it never did.</summary>
        public float SecondsToTarget;
        public int Trips;
        /// <summary>Food still in the cargo when the clock ended.</summary>
        public float Carried;
        /// <summary>Clock time at which the food in all nine piles first reached zero in this run (the same value in every seat's result); -1 when the piles never ran dry.</summary>
        public float PilesEmptyAt;
    }
}
