namespace CluckWars.Gameplay
{
    /// <summary>
    /// Each class's unique passive mechanic (GDD v0.3 §5.2).
    /// Byte-backed to match the <see cref="ChickenClass"/> convention and keep
    /// networked enum overhead minimal.
    /// </summary>
    public enum ChickenPassive : byte
    {
        /// <summary>No passive (unused / placeholder).</summary>
        None = 0,

        /// <summary>
        /// <b>Fatty.</b> Knockback applied to this chicken is drastically reduced.
        /// </summary>
        Immovable = 1,

        /// <summary>
        /// <b>Speedy.</b> Slow and root durations (and magnitudes for continuous
        /// slow sources) are reduced.
        /// </summary>
        Slippery = 2,

        /// <summary>
        /// <b>Warrior.</b> This chicken's damage abilities deal increased damage.
        /// </summary>
        Tough = 3,

        /// <summary>
        /// <b>Assassin.</b> Grants a third ability slot — the Combo slot.
        /// </summary>
        Combo = 4,
    }
}
