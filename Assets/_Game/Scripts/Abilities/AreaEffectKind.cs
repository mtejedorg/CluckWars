namespace CluckWars.Abilities
{
    /// <summary>
    /// What kind of lasting area an ability leaves behind (decisions 2026-10-21, B1-B3). Authored per ability asset as
    /// <see cref="AbilityBaseSO.AreaKind"/>: ability effects have TYPES, not a per-ability "follows caster" flag.
    /// </summary>
    /// <remarks>
    /// Byte-backed with explicit values and <b>append-only</b>: persisted by ordinal in <c>.asset</c> files (the same rule
    /// as <see cref="CastArchetype"/>). <c>AreaEffectTypeTests.AreaEffectKind_Ordinals_ArePinned</c> locks them.
    /// <para>
    /// The kind answers whether the area MOVES; <c>ZoneEffect</c> answers what it DOES. A future "root aura" is a new
    /// <c>ZoneEffect</c> member, never a new kind.
    /// </para>
    /// </remarks>
    public enum AreaEffectKind : byte
    {
        /// <summary>No lasting area: self-buffs, bursts resolved at cast (Cluck Shock, Dust Kick...), Doppelganger.</summary>
        None = 0,
        /// <summary>Placed once, stays where it was put (Feather Trap, Root Egg, Smoke Roost's cloud).</summary>
        StaticAoE = 1,
        /// <summary>Centred on its caster for its whole life (Feather Aura; future auras).</summary>
        Aura = 2,
    }
}
