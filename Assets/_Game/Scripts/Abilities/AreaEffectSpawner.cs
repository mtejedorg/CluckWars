using CluckWars.Gameplay;
using Fusion;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// The one <c>Runner.Spawn</c> + <c>onBeforeSpawned</c> stamp of an <see cref="AbilityZone"/> (WP3). Feather Trap,
    /// Root Egg and Smoke Roost each carried a copy of this block; they now compute the position and call
    /// <see cref="Spawn"/>, and so does Feather Aura (WP4, a Caster-anchored zone).
    /// </summary>
    /// <remarks>
    /// <b>Stamp order and values are the copies' own, byte for byte:</b> <c>SpawnPosition</c>, <c>HasSpawnPosition</c>,
    /// <c>Effect</c>, <c>OwnerChicken</c>, then <c>SlowFactor</c> (slow zones) or <c>RootDuration</c> (root zones), then
    /// <c>NetworkedRadius</c> and <c>LifetimeTimer</c>. The three WP3 fields (<c>Anchor</c>, <c>SourceSlot</c>,
    /// <c>Channels</c>) are appended after them. The stamp is the only way the zone's position reaches other peers: the
    /// prefab has no NetworkTransform (CW-217, <c>SpawnPlacementTests</c>).
    /// <para>
    /// Callers gate on <c>ctx.CanSpawnZone</c> in their <c>CanActivate</c> (the pre-commitment wiring gate); the refusals
    /// below are for a malformed SPEC, which is a code bug, and are reported as Errors before anything is spawned.
    /// </para>
    /// </remarks>
    public static class AreaEffectSpawner
    {
        /// <summary>
        /// Why <paramref name="spec"/> cannot be spawned today, or null when it can. Pure, so the refusals are tested
        /// without a runner.
        /// </summary>
        public static string Refusal(in AreaEffectSpec spec, int slot)
        {
            if (spec.Kind == AreaEffectKind.None) return "its AreaEffectSpec has Kind None (no lasting area to spawn)";
            if ((spec.Channels & AreaEffectChannel.Periodic) != 0)
                return "it asks for the Periodic channel, which has no RPC or receiver yet (WP5 is deferred to the first Periodic aura)";
            // B9 (Slow only, plus Root Egg's root): AbilityZone applies a root on the OnEnter channel and a slow on the
            // WhileInside channel, nothing else. A slow zone may add OnEnter (WP4, Feather Aura): it then confirms each
            // chicken's first entry, including the ones inside at the cast; the slow itself stays WhileInside. Any other
            // pairing would spawn a zone that affects nobody.
            bool channelsOk = spec.Effect == ZoneEffect.Root
                ? spec.Channels == AreaEffectChannel.OnEnter
                : spec.Channels == AreaEffectChannel.WhileInside
                  || spec.Channels == (AreaEffectChannel.WhileInside | AreaEffectChannel.OnEnter);
            if (!channelsOk)
            {
                string allowed = spec.Effect == ZoneEffect.Root ? "OnEnter" : "WhileInside (optionally with OnEnter)";
                return $"its channels ({spec.Channels}) are not the {allowed} channel AbilityZone applies a {spec.Effect} on";
            }
            // AbilityZone consumes Root zones on their first trigger and never consumes Slow zones (no networked flag for it).
            if (spec.ConsumeOnTrigger != (spec.Effect == ZoneEffect.Root))
                return $"ConsumeOnTrigger={spec.ConsumeOnTrigger} disagrees with what AbilityZone does for {spec.Effect} zones";
            if (spec.EndsWithAbility && spec.Kind != AreaEffectKind.Aura)
                return "EndsWithAbility is an Aura's rule; a Static AoE outlives its cast by design";
            if (spec.EndsWithAbility && (slot < 0 || slot >= AreaEffectRules.NoSourceSlot))
                return $"it ends with its ability but the cast carries no valid slot ({slot}), so it would despawn on its first tick";
            return null;
        }

        /// <summary>
        /// Where a Static AoE is placed: <see cref="AreaEffectSpec.ForwardOffset"/> metres along the caster's forward, on
        /// the caster's ground height (offset 0 = at the feet: Root Egg, Smoke Roost). An Aura's position only seeds its
        /// transform. The one reader of <c>ForwardOffset</c>, so the spec and the cast cannot disagree.
        /// </summary>
        public static Vector3 Placement(Vector3 casterPosition, Vector3 casterForward, in AreaEffectSpec spec)
        {
            var pos = casterPosition + casterForward * spec.ForwardOffset;
            pos.y = casterPosition.y; // keep on ground plane
            return pos;
        }

        /// <summary>
        /// Spawns <paramref name="spec"/>'s zone at <paramref name="pos"/> (an Aura's position only seeds its transform;
        /// its centre is the caster). False, with an Error naming the ability, when the spec is refused.
        /// </summary>
        public static bool Spawn(AbilityContext ctx, in AreaEffectSpec spec, Vector3 pos, string source, string abilityName)
        {
            string refusal = Refusal(spec, ctx.Slot);
            if (refusal != null)
            {
                ctx.Log?.Error(source, $"{abilityName} placed no zone: {refusal}. Fix the ability's TryGetAreaEffect.");
                return false;
            }

            // Captured into locals: a closure over 'this' or over ctx can go stale (the copies' reason, kept).
            var   capSpec  = spec;
            var   capOwner = ctx.Controller.Id;
            byte  capSlot  = spec.EndsWithAbility ? (byte)ctx.Slot : AreaEffectRules.NoSourceSlot;
            var   capLog   = ctx.Log;
            var   capPos   = pos;

            ctx.Runner.Spawn(
                ctx.PrefabRegistry.AbilityZone,
                capPos,
                Quaternion.identity,
                inputAuthority: null,
                onBeforeSpawned: (runner, networkObject) =>
                {
                    var zone = networkObject.GetComponent<AbilityZone>();
                    if (zone == null)
                    {
                        capLog?.Error(source, abilityName + " leaked a zone: the prefab wired to "
                            + "PrefabRegistrySO.AbilityZone has no AbilityZone component, so its lifetime "
                            + "timer was never set. The NetworkObject has ALREADY spawned and will now "
                            + "persist for the rest of the match doing nothing — fix the prefab.");
                        return;
                    }
                    // No NetworkTransform on the zone: its position reaches the other peers only through this stamp.
                    zone.SpawnPosition    = capPos;
                    zone.HasSpawnPosition = true;
                    zone.Effect          = capSpec.Effect;
                    zone.OwnerChicken    = capOwner;
                    if (capSpec.Effect == ZoneEffect.Slow) zone.SlowFactor   = capSpec.SlowFactor;
                    else                                   zone.RootDuration = capSpec.RootDuration;
                    zone.NetworkedRadius = capSpec.Radius;
                    zone.LifetimeTimer   = TickTimer.CreateFromSeconds(runner, capSpec.Lifetime);
                    // WP3: appended, after the original stamp.
                    zone.Anchor          = capSpec.Anchor;
                    zone.SourceSlot      = capSlot;
                    zone.Channels        = capSpec.Channels;
                    // Consumed starts false — AbilityZone.FUN sets it true on a Root zone's first trigger.
                });
            return true;
        }
    }
}
