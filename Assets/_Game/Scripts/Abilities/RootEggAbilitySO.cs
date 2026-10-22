using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Root Egg — Control ability. Places an egg zone at the caster's feet.
    /// The first chicken to step on it is rooted for <see cref="RootDuration"/>
    /// seconds; the zone then self-destructs (consumed on first trigger). If no
    /// chicken triggers it within <see cref="EggLifetime"/> seconds the egg
    /// despawns automatically.
    /// </summary>
    /// <remarks>
    /// Speedy's Slippery passive reduces the root duration applied to the target
    /// (handled inside <see cref="ChickenController.RPC_ApplyRoot"/>).
    ///
    /// Cooldown tier: Medium. Requires <see cref="PrefabRegistrySO.AbilityZone"/>
    /// to be assigned.
    /// </remarks>
    [CreateAssetMenu(fileName = "RootEgg",
        menuName = "Cluck Wars/Ability/Control/Root Egg", order = 11)]
    public sealed class RootEggAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityRootEggDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("secs", Num(RootDuration)) };

        private const string Source = "RootEgg";

        [Tooltip("Seconds before the egg despawns if no chicken triggers it.")]
        [Min(0.5f)] public float EggLifetime = 8.0f;

        [Tooltip("How long the triggered chicken is rooted (seconds, before Slippery reduction).")]
        [Min(0.1f)] public float RootDuration = 2.0f;

        [Tooltip("Trigger radius within which a chicken activates the root.")]
        [Min(0.3f)] public float EggRadius = 1.45f;

        // Placed zone, not a direct hit — no GatherTargets loop in OnActivate, same
        // as Feather Trap. The egg spawns at the caster's own feet (offset 0); the
        // descriptor describes that placement rather than inventing a tuned offset.
        public override AbilityAimShape AimShape => AbilityAimShape.ForwardCircle;
        public override float AimRadius => EggRadius;
        public override float AimForwardOffset => 0f;

        // The cast never hits anyone — the egg does, later, in AbilityZone's own tick. With
        // AimForwardOffset 0 and AffectsSelf false the caster is the only chicken inside the
        // shape at cast time and is excluded, so LastCastHitCount is 0 on EVERY cast on open
        // ground. PlacesZone (derived from AreaKind = StaticAoE, authored on the asset and
        // defaulted here for fresh instances) keeps the whiff styling off every correct egg.
        public RootEggAbilitySO() { AreaKind = AreaEffectKind.StaticAoE; }

        public override bool TryGetAreaEffect(out AreaEffectSpec spec)
        {
            spec = AreaEffectSpec.StaticRoot(EggRadius, EggLifetime, RootDuration, AimForwardOffset);
            return true;
        }

        // The egg's AbilityZone sends this root later (TryTriggerRoot, naming this caster), but the
        // receiver's bound comes from the ability pool, so the ability declares it (F4).
        public override ControlEmission EmittedControl => ControlEmission.Root(RootDuration);

        // Unassigned wiring, not a gameplay state — CanSpawnZone logs which reference is
        // missing. Sits in CanActivate rather than at the top of OnActivate so the refusal
        // lands before the cast and its cooldown are committed. The logger reaches an ability
        // through AbilityContext because SOs are assets Zenject cannot inject; read
        // AbilityContext.Log before reaching for anything shorter.
        public override bool CanActivate(AbilityContext ctx) => ctx.CanSpawnZone(Source, "Root Egg");

        public override void OnActivate(AbilityContext ctx)
        {
            var pos = ctx.Controller.transform.position;   // at the caster's feet (AimForwardOffset 0)

            TryGetAreaEffect(out var spec);
            AreaEffectSpawner.Spawn(ctx, spec, pos, Source, "Root Egg");
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
