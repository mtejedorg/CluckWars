namespace CluckWars.Abilities
{
    /// <summary>
    /// The job an ability does, as the ability itself declares it (<see cref="AbilityBaseSO.Job"/>, A15). One ability per
    /// job in the shipped pool (2026-11-05); a retired ability declares None unless it shares a job's presentation.
    /// </summary>
    /// <remarks>
    /// This is what code outside <c>Scripts/Abilities</c> asks instead of <c>is XxxAbilitySO</c>: the shell visuals ask
    /// for <see cref="Shield"/>, the grab lines for <see cref="Steal"/>, a kit rule for <see cref="Peck"/>. Not
    /// networked and not serialized (it is a property of the class), but append only all the same: tests pin the values.
    /// </remarks>
    public enum AbilityJob : byte
    {
        None      = 0,
        Peck      = 1,   // forage from a pile
        Steal     = 2,
        Stun      = 3,
        Shove     = 4,
        Slow      = 5,   // the instant timed slow (Slow)
        Speed     = 6,
        Jump      = 7,
        Shield    = 8,   // walk, then the egg
        Hide      = 9,
        Bank      = 10,
        DeathMark = 11,  // mark, then the Kill
        SlowArea  = 12,  // the lasting slow area (Slow Area: an aura or a placed zone). Appended: the instant Slow keeps Slow.
    }

    public static class AbilityJobs
    {
        /// <summary>Does <paramref name="ability"/> do <paramref name="job"/>? False for null, like the <c>is</c> check it replaces.</summary>
        public static bool Is(this AbilityBaseSO ability, AbilityJob job) => ability != null && ability.Job == job;
    }
}
