using CluckWars.Gameplay;
using Fusion;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Spawns a brief decoy copy of the caster (<see cref="Doppelganger"/>) just
    /// to one side. The decoy wears the caster's class tint (resolved by
    /// <see cref="ChickenController"/>'s usual <c>Spawned</c> path) and reacts to
    /// hits with the regular hit animation, since the prefab keeps
    /// <c>ChickenController</c> + <c>ChickenCombat</c> + animator. Purpose: break
    /// enemy targeting / waste their attacks. One-shot at activation;
    /// <see cref="OnDeactivate"/> is a no-op since the decoy outlives the
    /// ability's "active" window and self-despawns.
    /// </summary>
    [CreateAssetMenu(fileName = "Doppelganger", menuName = "Cluck Wars/Ability/Doppelganger", order = 7)]
    public sealed class DoppelgangerAbilitySO : AbilityBaseSO
    {
        private const string Source = "Doppelganger";

        public DoppelgangerAbilitySO()
        {
            TerrainTraversal = TerrainTraversal.Blink;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        [Tooltip("Decoy NetworkObject prefab (Chicken-prefab variant with ChickenCargo stripped + Doppelganger added).")]
        [SerializeField] private NetworkObject _decoyPrefab;

        [Tooltip("How long the decoy persists after spawn. Independent of the ability's Duration (which only drives the cooldown UI).")]
        [Min(0.5f)] public float DecoyLifetimeSeconds = 4f;

        [Tooltip("Lateral offset from the caster where the decoy spawns. Side-step so it isn't sitting on top of the caster.")]
        [Min(0f)] public float SideOffset = 1.2f;

        protected override string DefaultIcon => "👥";

        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md
        // §2.2). Stated explicitly (rather than relying on the inherited default)
        // because the asset also carries a JumpTier (its short Blink hop) — a future
        // reader skimming JumpTier=Big should not have to wonder whether that implies
        // an offensive Jump shape; it does not.
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        /// <summary>
        /// Refuses the press when the decoy could not be spawned, before the cast is
        /// committed. Same shape and the same reason as the zone abilities' gate: this is
        /// unassigned wiring, which will never fix itself at runtime, so it is an Error and
        /// it belongs ahead of <c>ActiveSlot</c> / the activation timer / the cooldown that
        /// <c>AbilityController.TryActivate</c> writes immediately after this returns true.
        /// </summary>
        /// <remarks>
        /// Doppelganger spawns a NetworkObject without placing an <c>AbilityZone</c>, so it
        /// cannot reuse <see cref="AbilityContext.CanSpawnZone"/> — it needs
        /// <c>ctx.Runner</c> and its own serialized prefab, not the shared registry.
        /// The <c>ctx.Runner</c> arm is unreachable for an actively-ticking
        /// <c>AbilityController</c> (the runner is what ticks it); it is kept for symmetry
        /// with <c>CanSpawnZone</c>, and to keep the caller's contract "false means an Error
        /// naming the missing reference has already been logged" true on every path.
        /// </remarks>
        public override bool CanActivate(AbilityContext ctx)
        {
            if (_decoyPrefab == null)
            {
                ctx.Log?.Error(Source, "Doppelganger cast refused: DoppelgangerAbilitySO._decoyPrefab is " +
                    "not assigned — wire the Doppelganger decoy prefab onto the Doppelganger ability asset. " +
                    "The Assassin gets no decoy; the press was refused before any cooldown was charged.");
                return false;
            }

            if (ctx.Runner == null)
            {
                ctx.Log?.Error(Source, "Doppelganger cast refused: the ability context has no " +
                    "NetworkRunner, so there is nothing to Spawn the decoy into. The Assassin gets no " +
                    "decoy; the press was refused before any cooldown was charged.");
                return false;
            }

            return true;
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var runner = ctx.Runner;

            var spawnPos = caster.transform.position + caster.transform.right * SideOffset;
            var spawnRot = caster.transform.rotation;
            var mimickedClass = caster.Class;
            var lifetime = DecoyLifetimeSeconds;
            // Captured like the rest so the abandoned-spawn reports below close over the
            // logger rather than over ctx (mirrors RootEggAbilitySO).
            var capLog = ctx.Log;

            runner.Spawn(
                _decoyPrefab,
                spawnPos,
                spawnRot,
                caster.Object.InputAuthority,
                onBeforeSpawned: (_, networkObject) =>
                {
                    // Nothing here may despawn on failure: onBeforeSpawned runs mid-Spawn,
                    // where despawning the object being spawned is not safe. Surfacing the
                    // broken prefab is the whole available fix (docs/CONVENTIONS.md,
                    // silent-failure corollary 2).

                    // Set Class on the decoy's ChickenController so its existing
                    // Spawned() pulls the right stats + tint from the registry.
                    var decoyController = networkObject.GetComponent<ChickenController>();
                    if (decoyController != null)
                    {
                        decoyController.Class = mimickedClass;
                    }
                    else
                    {
                        capLog?.Error(Source, "Doppelganger decoy has no ChickenController: the prefab "
                            + "wired to DoppelgangerAbilitySO._decoyPrefab is not a Chicken variant. The "
                            + "NetworkObject has ALREADY spawned, so it will wear the default class's stats "
                            + "and tint instead of the caster's — fix the prefab.");
                    }

                    var doppel = networkObject.GetComponent<Doppelganger>();
                    if (doppel != null)
                    {
                        doppel.LifetimeTimer = TickTimer.CreateFromSeconds(runner, lifetime);
                    }
                    else
                    {
                        capLog?.Error(Source, "Doppelganger decoy has no Doppelganger component: the prefab "
                            + "wired to DoppelgangerAbilitySO._decoyPrefab is missing it. The NetworkObject "
                            + "has ALREADY spawned and its LifetimeTimer was never set, so it persists for "
                            + "the rest of the match; and because Doppelganger is what sets "
                            + "ChickenController.IsDecoy, that flag is never set either — the caster's input "
                            + "drives their real chicken AND this decoy in lockstep. A permanent "
                            + "input-mirroring phantom. Fix the prefab.");
                    }
                });
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            // No persistent state to clear — the decoy is independent and self-despawns
            // (lifetime expiry or OnDeath, whichever comes first).
        }
    }
}
