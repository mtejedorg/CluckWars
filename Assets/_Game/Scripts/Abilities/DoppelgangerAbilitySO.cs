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
        [Tooltip("Decoy NetworkObject prefab (Chicken-prefab variant with ChickenCargo stripped + Doppelganger added).")]
        [SerializeField] private NetworkObject _decoyPrefab;

        [Tooltip("How long the decoy persists after spawn. Independent of the ability's Duration (which only drives the cooldown UI).")]
        [Min(0.5f)] public float DecoyLifetimeSeconds = 4f;

        [Tooltip("Lateral offset from the caster where the decoy spawns. Side-step so it isn't sitting on top of the caster.")]
        [Min(0f)] public float SideOffset = 1.2f;

        public override void OnActivate(AbilityContext ctx)
        {
            if (_decoyPrefab == null) return;

            var caster = ctx.Controller;
            var runner = caster.Runner;
            if (runner == null) return;

            var spawnPos = caster.transform.position + caster.transform.right * SideOffset;
            var spawnRot = caster.transform.rotation;
            var mimickedClass = caster.Class;
            var lifetime = DecoyLifetimeSeconds;

            runner.Spawn(
                _decoyPrefab,
                spawnPos,
                spawnRot,
                caster.Object.InputAuthority,
                onBeforeSpawned: (_, networkObject) =>
                {
                    // Set Class on the decoy's ChickenController so its existing
                    // Spawned() pulls the right stats + tint from the registry.
                    var decoyController = networkObject.GetComponent<ChickenController>();
                    if (decoyController != null) decoyController.Class = mimickedClass;

                    var doppel = networkObject.GetComponent<Doppelganger>();
                    if (doppel != null)
                    {
                        doppel.LifetimeTimer = TickTimer.CreateFromSeconds(runner, lifetime);
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
