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
    }
}
