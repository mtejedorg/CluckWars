namespace CluckWars.Abilities
{
    /// <summary>
    /// How an area effect applies over time (decision B3, 2026-10-21). One area may combine channels.
    /// </summary>
    /// <remarks>
    /// <b>[Flags], byte, explicit and append-only:</b> stamped on <c>AbilityZone.Channels</c> as a <c>[Networked]</c> byte,
    /// so a value's meaning is part of the wire format.
    /// <list type="bullet">
    /// <item><see cref="OnEnter"/>: once per chicken per zone life, including the chickens already inside when the zone
    /// appears (Maestro: "it should have effect on the chickens that were in the zone when it is cast"). Evaluated by the
    /// ZONE's state authority, which sends the effect by RPC (Root Egg: <c>RPC_ApplyRoot</c>).</item>
    /// <item><see cref="WhileInside"/>: state re-evaluated every tick by each VICTIM's own authority, zero RPC (the slows,
    /// <c>ChickenController.CheckAbilityZoneSlow</c>). B2's hybrid authority: continuous effects are victim-evaluated.</item>
    /// <item><see cref="Periodic"/>: every <c>AreaEffectSpec.PeriodicInterval</c> seconds (&gt;= 0.25) per victim, amount
    /// scaled by the interval. <b>Spec only (F3, WP5 deferred):</b> no RPC and no receiver exists yet, so
    /// <c>AreaEffectSpawner</c> refuses a spec that asks for it rather than spawning a zone that silently does nothing.</item>
    /// </list>
    /// </remarks>
    [System.Flags]
    public enum AreaEffectChannel : byte
    {
        None        = 0,
        OnEnter     = 1 << 0, // 1
        WhileInside = 1 << 1, // 2
        Periodic    = 1 << 2, // 4
    }
}
