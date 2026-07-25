using System.Collections.Generic;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Distinguishes what effect an <see cref="AbilityZone"/> applies to chickens
    /// that enter its radius.
    /// </summary>
    public enum ZoneEffect : byte
    {
        Slow = 0, // Applies <see cref="SlowSource.Ability"/> via ChickenController.
        Root = 1, // Applies root via <see cref="ChickenController.RPC_ApplyRoot"/>.
    }

    /// <summary>
    /// Networked placed-effect zone used by Feather Trap (slow) and Root Egg (root).
    /// Spawned by the respective ability SOs via
    /// <see cref="NetworkRunner.Spawn"/>; parameters are stamped in
    /// <c>onBeforeSpawned</c> from tick zero.
    /// </summary>
    /// <remarks>
    /// <b>Slow zones</b> are handled without per-tick RPCs: each chicken's authority
    /// reads <see cref="ActiveZones"/> in its own <c>FixedUpdateNetwork</c> and
    /// self-applies the slow (<see cref="ChickenController.CheckAbilityZoneSlow"/>
    /// calls this list). The list is maintained via <see cref="Spawned"/> /
    /// <see cref="Despawned"/>.
    ///
    /// <b>Root zones</b> are consumed on first trigger and send
    /// <see cref="ChickenController.RPC_ApplyRoot"/> once to the target's authority.
    ///
    /// <b>Maestro:</b> Create one <c>AbilityZone</c> prefab (NetworkObject + this
    /// script + a trigger sphere collider at the desired radius). Assign it to
    /// <c>PrefabRegistrySO.AbilityZone</c>.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AbilityZone : NetworkBehaviour
    {
        private const string Source = "AbilityZone";

        // ---- Static registry — all active zones on this peer ------------------

        /// <summary>
        /// All currently alive AbilityZones on this peer. Read by
        /// <see cref="ChickenController"/> to apply slow sources without RPCs.
        /// </summary>
        public static readonly List<AbilityZone> ActiveZones = new List<AbilityZone>();

        // Static array for broadphase overlaps to prevent per-tick allocation (Stage G)
        private static readonly Collider[] _overlapHits = new Collider[32];

        // ---- Serialized tunables ----------------------------------------------

        [Tooltip("Radius within which the zone effect triggers. Must match the visual / collider size.")]
        [Min(0.5f)]
        [SerializeField] private float _triggerRadius = 1.5f;

        // ---- Networked parameters (stamped by spawner in onBeforeSpawned) -----

        [Networked] public TickTimer  LifetimeTimer    { get; set; }
        [Networked] public bool       Consumed         { get; set; }
        [Networked] public ZoneEffect Effect           { get; set; }

        /// <summary>Speed multiplier for Slow zones (e.g. 0.50 = 50% speed).</summary>
        [Networked] public float SlowFactor    { get; set; }

        /// <summary>How long the root lasts on the triggered chicken (Root zones only).</summary>
        [Networked] public float RootDuration  { get; set; }

        /// <summary>
        /// Per-spawn radius override. When set to a value &gt; 0 in
        /// <c>onBeforeSpawned</c> by ability SOs, this takes precedence over
        /// the prefab's serialized <c>_triggerRadius</c>.
        /// </summary>
        [Networked] public float NetworkedRadius { get; set; }

        /// <summary>
        /// The caster's <see cref="ChickenController"/> behaviour id, stamped in
        /// <c>onBeforeSpawned</c>. Zones never affect their own caster (a Root
        /// Egg placed at the caster's feet would otherwise self-root next tick).
        /// </summary>
        [Networked] public NetworkBehaviourId OwnerChicken { get; set; }

        // ---- Read-only accessor (ChickenController uses this) -----------------

        /// <summary>
        /// Returns <see cref="NetworkedRadius"/> if set, else the prefab's
        /// serialized default <c>_triggerRadius</c>.
        /// </summary>
        public float TriggerRadius => NetworkedRadius > 0f ? NetworkedRadius : _triggerRadius;

        private ILogService _log;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        // ---- Fusion lifecycle -------------------------------------------------

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            ActiveZones.Add(this);
            _log?.Debug(Source, $"Spawned. Effect={Effect}, Radius={TriggerRadius}, " +
                $"Slow={SlowFactor:P0}, RootDuration={RootDuration:0.0}s.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveZones.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            // Despawn when lifetime expires or zone was consumed.
            if (LifetimeTimer.Expired(Runner) || Consumed)
            {
                Runner.Despawn(Object);
                return;
            }

            // Root zones: scan for chickens and apply root once (consumed = true
            // for Root Egg; persistent for a future multi-trigger root variant).
            if (Effect == ZoneEffect.Root)
            {
                int hitCount = Physics.OverlapSphereNonAlloc(
                    transform.position, TriggerRadius, _overlapHits, 1 << 8,
                    QueryTriggerInteraction.Ignore);

                for (int i = 0; i < hitCount; i++)
                {
                    var chicken = _overlapHits[i].GetComponentInParent<ChickenController>();
                    if (chicken == null) continue;
                    if (chicken.Id == OwnerChicken) continue; // zones never affect their caster
                    if (chicken.Combat != null && chicken.Combat.IsRemoved) continue;

                    chicken.RPC_ApplyRoot(RootDuration);
                    _log?.Debug(Source, $"Root applied to {chicken.name}.");

                    // Root Egg: consumed on first trigger.
                    Consumed = true;
                    return;
                }
            }
            // Slow zones: handled passively by ChickenController.CheckAbilityZoneSlow.
        }
    }
}
