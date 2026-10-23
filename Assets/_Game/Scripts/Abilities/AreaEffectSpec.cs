using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// The lasting area an ability creates, built in code from the fields the ability already serializes
    /// (<see cref="AbilityBaseSO.TryGetAreaEffect"/>). Not serialized itself: no tuned number moves into a new field, so
    /// the <c>.asset</c> YAML, the Balance Editor, the description templates and the oracle are untouched (phase 1, WP3).
    /// </summary>
    /// <remarks>
    /// Read by <see cref="AreaEffectSpawner"/> (what to stamp on the zone) and by the tests that pin each ability's spec
    /// to its authored fields (<c>AreaEffectTypeTests.AreaAbilities_TryGetAreaEffect_MatchesTheirFields</c>).
    /// </remarks>
    public readonly struct AreaEffectSpec
    {
        /// <summary>Floor on <see cref="PeriodicInterval"/> (B3): a periodic effect never ticks faster than 4 Hz, whatever the tick rate.</summary>
        public const float MinPeriodicInterval = 0.25f;

        public readonly AreaEffectKind Kind;
        public readonly AreaEffectChannel Channels;
        public readonly ZoneEffect Effect;
        public readonly float Radius;
        /// <summary>Seconds the zone lives (an Aura's backstop when <see cref="EndsWithAbility"/>).</summary>
        public readonly float Lifetime;
        /// <summary>Slow zones: speed multiplier (&lt; 1).</summary>
        public readonly float SlowFactor;
        /// <summary>Root zones: root seconds before the victim's passive shortens it.</summary>
        public readonly float RootDuration;
        /// <summary>Static AoE: metres ahead of the caster the centre is placed (0 = at the feet). Ignored by an Aura.</summary>
        public readonly float ForwardOffset;
        /// <summary>The zone is spent on its first trigger (Root Egg).</summary>
        public readonly bool ConsumeOnTrigger;
        /// <summary>Aura only: the zone despawns when the casting slot stops being active (Feather Aura, WP4).</summary>
        public readonly bool EndsWithAbility;
        /// <summary>Periodic channel only: seconds between applications per victim, &gt;= <see cref="MinPeriodicInterval"/>; 0 otherwise.</summary>
        public readonly float PeriodicInterval;
        /// <summary>Periodic channel only: amount per second; each application carries this x <see cref="PeriodicInterval"/>.</summary>
        public readonly float PeriodicAmountPerSecond;

        public AreaEffectSpec(AreaEffectKind kind, AreaEffectChannel channels, ZoneEffect effect, float radius, float lifetime,
            float slowFactor = 1f, float rootDuration = 0f, float forwardOffset = 0f, bool consumeOnTrigger = false,
            bool endsWithAbility = false, float periodicInterval = 0f, float periodicAmountPerSecond = 0f)
        {
            bool periodic = (channels & AreaEffectChannel.Periodic) != 0;
            Kind = kind;
            Channels = channels;
            Effect = effect;
            Radius = radius;
            Lifetime = lifetime;
            SlowFactor = slowFactor;
            RootDuration = rootDuration;
            ForwardOffset = forwardOffset;
            ConsumeOnTrigger = consumeOnTrigger;
            EndsWithAbility = endsWithAbility;
            PeriodicInterval = periodic ? Mathf.Max(MinPeriodicInterval, periodicInterval) : 0f;
            PeriodicAmountPerSecond = periodic ? periodicAmountPerSecond : 0f;
        }

        /// <summary>A placed slow cloud (Feather Trap, Smoke Roost): slows while inside.</summary>
        public static AreaEffectSpec StaticSlow(float radius, float lifetime, float slowFactor, float forwardOffset) =>
            new AreaEffectSpec(AreaEffectKind.StaticAoE, AreaEffectChannel.WhileInside, ZoneEffect.Slow, radius, lifetime,
                slowFactor: slowFactor, forwardOffset: forwardOffset);

        /// <summary>A placed one-shot root (Root Egg): roots on entry, spent on its first catch.</summary>
        public static AreaEffectSpec StaticRoot(float radius, float lifetime, float rootDuration, float forwardOffset) =>
            new AreaEffectSpec(AreaEffectKind.StaticAoE, AreaEffectChannel.OnEnter, ZoneEffect.Root, radius, lifetime,
                rootDuration: rootDuration, forwardOffset: forwardOffset, consumeOnTrigger: true);

        /// <summary>
        /// A slow centred on the caster that ends with the casting slot (Feather Aura). WhileInside carries the slow;
        /// OnEnter is the once-per-chicken entry confirmation, which also covers the chickens already inside at the cast
        /// (Maestro: "it should have effect on the chickens that were in the zone when it is cast"; WP4).
        /// </summary>
        public static AreaEffectSpec AuraSlow(float radius, float lifetime, float slowFactor) =>
            new AreaEffectSpec(AreaEffectKind.Aura, AreaEffectChannel.WhileInside | AreaEffectChannel.OnEnter, ZoneEffect.Slow,
                radius, lifetime, slowFactor: slowFactor, endsWithAbility: true);

        /// <summary>How the zone is anchored: a Static AoE to the world, an Aura to its caster.</summary>
        public ZoneAnchor Anchor => Kind == AreaEffectKind.Aura ? ZoneAnchor.Caster : ZoneAnchor.World;
    }
}
