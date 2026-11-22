namespace CluckWars.Abilities
{
    /// <summary>
    /// The numbers the balance oracle reads off an ability, declared by the ability that owns them
    /// (<see cref="AbilityBaseSO.OracleFacts"/>) so the oracle never asks which class an ability is.
    /// A field is zero where the ability does not own it.
    /// </summary>
    public struct AbilityOracleFacts
    {
        public float SpeedMultiplier;
        public float SpeedSeconds;
        public float StealAmount;
        public float MarkRoundStartCooldown;
        public float MarkWindowSeconds;
        public float MarkSuccessCooldown;
        public float MarkRemovalSeconds;
        /// <summary>Hide: the authored opacity while it runs (what the bot sight rule reads).</summary>
        public float HideOpacity;
        /// <summary>Jump: how far the blink goes, in metres.</summary>
        public float BlinkMetres;
        /// <summary>Shield: seconds of the slow-immune walk, the walk's speed scalar, and seconds of the egg that follows.</summary>
        public float ShieldWalkSeconds;
        public float ShieldWalkSpeedMultiplier;
        public float ShieldEggSeconds;
        /// <summary>Death Mark: seconds before a Kill can qualify, the Kill's reach, the cooldown after a miss, and the pick's pull toward the leader.</summary>
        public float MarkArmSeconds;
        public float MarkKillRange;
        public float MarkFailCooldown;
        public float MarkLeaderPickWeight;
    }
}
