using System.Collections.Generic;
using CluckWars.Logging;
using CluckWars.Visuals;
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

        // ---- Local trigger observation (FEEDBACK.md §3.2 for placed zones) ----
        //
        // Purely observational, purely local, and deliberately no new networked state and
        // no RPC: the zone's own Consumed flag and every chicken's position are already
        // replicated, so each peer's Render() reaches the same conclusion independently —
        // the same reasoning ChickenCargo uses for its Cargo detector.
        private ChangeDetector _changes;
        private PropertyReader<bool> _consumedReader;

        /// <summary>Slow-zone occupancy from the previous frame, so the caster's
        /// confirmation edge-triggers on entry instead of firing every frame someone stands
        /// in the cloud. Slow zones have no <see cref="Consumed"/> flip to watch.</summary>
        private bool _slowZoneOccupied;

        /// <summary>Root zones confirm exactly once. Shared between the
        /// <see cref="Render"/> detector and the <see cref="Despawned"/> fallback so the two
        /// can never both fire for one trigger.</summary>
        private bool _rootConfirmSent;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        // ---- Fusion lifecycle -------------------------------------------------

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            ActiveZones.Add(this);

            _changes        = GetChangeDetector(ChangeDetector.Source.SimulationState);
            _consumedReader = GetPropertyReader<bool>(nameof(Consumed));

            // Fusion pools NetworkObjects, so both observation flags must be re-armed per
            // spawn — a reused instance would otherwise inherit "already confirmed" (root)
            // or "already occupied" (slow) from the previous zone and stay silent.
            _rootConfirmSent  = false;
            _slowZoneOccupied = false;

            _log?.Debug(Source, $"Spawned. Effect={Effect}, Radius={TriggerRadius}, " +
                $"Slow={SlowFactor:P0}, RootDuration={RootDuration:0.0}s.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveZones.Remove(this);

            // Fallback for the one case the Render() detector structurally cannot see: a
            // root zone flips Consumed on tick N and despawns on tick N+1, and at the 30 fps
            // Android floor against a 32 Hz tick rate a frame occasionally runs both ticks
            // back to back — so no Render() ever happens while Consumed is true and visible.
            // Despawned always runs, on every peer, which closes that hole. Guarded by
            // _rootConfirmSent so the normal path can never double-confirm.
            if (hasState && Effect == ZoneEffect.Root && Consumed) ConfirmRootTriggerOnce(runner);
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
                TryTriggerRoot();
            }
            // Slow zones: handled passively by ChickenController.CheckAbilityZoneSlow.
        }

        /// <summary>
        /// The root trigger. Iterates <see cref="ChickenController.ActiveControllers"/>
        /// (at most 4 entries) with a planar-XZ distance test, exactly like
        /// <c>AbilityBaseSO.GatherTargets</c> and <c>ChickenController.CheckAbilityZoneSlow</c>
        /// — this zone was the last direct-hit path still resolving against collider bounds
        /// via <c>Physics.OverlapSphere</c>, which meant the edge ring
        /// <c>AbilityZoneVisuals</c> draws at exactly <see cref="TriggerRadius"/> was not
        /// quite the thing that triggered. Locked by
        /// <c>AbilityAimTests.NoAbilityScript_CallsPhysicsOverlapDirectly</c>.
        ///
        /// <b>Balance note:</b> the effective trigger area is now measured to a chicken's
        /// transform pivot rather than to the nearest point on its capsule, so the egg is
        /// very slightly harder to set off (by roughly the chicken's collider radius). The
        /// drawn ring is now the truth.
        ///
        /// <b>A decoy does not set the egg off, even though decoys are targetable now.</b>
        /// Being a valid target for a cast someone deliberately aimed is one thing; a placed
        /// trap is another — nobody aimed this at the decoy, it just walked over it. And a
        /// decoy cannot actually be rooted: <c>ChickenController.FixedUpdateNetwork</c>
        /// early-returns on <c>IsDecoy</c> before root is re-derived, so the egg would be
        /// consumed and despawned with no visible effect on anyone, while still firing a
        /// hit-confirm at its caster for a catch that did nothing. That is the same
        /// reasoning <see cref="AnyRivalInside"/> already applies to the slow half of this
        /// class — the two halves of one file must not disagree about what counts as a catch.
        /// </summary>
        private void TryTriggerRoot()
        {
            float radius = TriggerRadius;
            float radiusSqr = radius * radius;
            Vector3 center = transform.position;

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var chicken = all[i];
                if (chicken == null) continue;
                if (chicken.Id == OwnerChicken) continue; // zones never affect their caster
                if (chicken.IsDecoy) continue;            // see the remarks above
                if (chicken.Combat != null && chicken.Combat.IsRemoved) continue;
                if (PlanarSqrDistance(center, chicken.transform.position) > radiusSqr) continue;

                chicken.RPC_ApplyRoot(RootDuration);
                _log?.Debug(Source, $"Root applied to {chicken.name}.");

                // Root Egg: consumed on first trigger. Every peer watches this flip in
                // Render() and confirms the cast to the caster — see ObserveRootConsumed.
                Consumed = true;
                return;
            }
        }

        // ---- Local confirmation to the caster (no RPC) ------------------------

        /// <summary>
        /// Gives the caster of a placed zone the hit-confirm their cast could not give them.
        /// A Root Egg / Feather Trap reports 0 targets at cast time by construction (see
        /// <c>AbilityBaseSO.ReportsCastHits</c>), so without this the ability that worked
        /// perfectly is also the ability that never tells you it worked.
        /// </summary>
        public override void Render()
        {
            if (Effect == ZoneEffect.Root) ObserveRootConsumed();
            else                           ObserveSlowZoneEntry();
        }

        /// <summary>Root zones flip the replicated <see cref="Consumed"/> flag on the exact
        /// tick they catch someone, so the false→true edge is the trigger moment on every
        /// peer — a real <c>ChangeDetector</c> works here because the property is declared on
        /// this behaviour.</summary>
        private void ObserveRootConsumed()
        {
            if (_changes == null || _rootConfirmSent) return;

            foreach (var changed in _changes.DetectChanges(this, out var previous, out var current))
            {
                if (changed != nameof(Consumed)) continue;
                var (before, after) = _consumedReader.Read(previous, current);
                if (!before && after) ConfirmRootTriggerOnce(Runner);
            }
        }

        /// <summary>Idempotent root confirmation — whichever of <see cref="Render"/> or
        /// <see cref="Despawned"/> observes the trigger first wins, the other becomes a
        /// no-op.</summary>
        private void ConfirmRootTriggerOnce(NetworkRunner runner)
        {
            if (_rootConfirmSent) return;
            _rootConfirmSent = true;
            NotifyOwnerZoneTriggered(runner);
        }

        /// <summary>
        /// Slow zones never flip a flag — they just sit there slowing whoever is inside — so
        /// the "it caught someone" edge is derived from replicated positions instead. Rising
        /// edge only: a rival standing in the cloud must not re-confirm every frame.
        /// </summary>
        private void ObserveSlowZoneEntry()
        {
            bool occupied = AnyRivalInside();
            if (occupied && !_slowZoneOccupied) NotifyOwnerZoneTriggered(Runner);
            _slowZoneOccupied = occupied;
        }

        /// <summary>Same registry + planar-XZ test as <see cref="TryTriggerRoot"/>, so the
        /// confirmation and the effect agree on what "inside" means. At most 4 entries —
        /// no <c>Physics.Overlap</c>.</summary>
        private bool AnyRivalInside()
        {
            float radius = TriggerRadius;
            float radiusSqr = radius * radius;
            Vector3 center = transform.position;

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var chicken = all[i];
                if (chicken == null) continue;
                if (chicken.Id == OwnerChicken) continue;
                if (chicken.IsDecoy) continue; // a phantom is not a catch
                if (chicken.Combat != null && chicken.Combat.IsRemoved) continue;

                var obj = chicken.Object;
                if (obj == null || !obj.IsValid) continue;

                if (PlanarSqrDistance(center, chicken.transform.position) <= radiusSqr) return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the caster from the replicated <see cref="OwnerChicken"/> id and hands
        /// them a plain local method call — never an RPC. Every peer runs this and reaches
        /// the same answer; the shake inside <c>NotifyZoneTriggered</c> is
        /// <c>HasInputAuthority</c>-gated so only the caster's own client feels it.
        /// </summary>
        private void NotifyOwnerZoneTriggered(NetworkRunner runner)
        {
            if (runner == null) return;

            if (!runner.TryFindBehaviour(OwnerChicken, out ChickenController owner) || owner == null)
            {
                // Legitimate: the caster can be removed or have left while their zone is
                // still armed. The zone keeps working — there is just nobody to confirm to.
                // Logged rather than swallowed so a systematic failure to resolve owners
                // shows up instead of quietly costing every placed zone its feedback.
                _log?.Debug(Source, "Zone triggered but its owner could not be resolved — no cast confirmation sent.");
                return;
            }

            var feedback = owner.GetComponent<HitFeedback>();
            if (feedback == null)
            {
                _log?.Warn(Source, $"Zone owner '{owner.name}' has no HitFeedback component — " +
                    "placed-zone cast confirmation dropped. Check the Chicken prefab.");
                return;
            }

            feedback.NotifyZoneTriggered();
        }

        private static float PlanarSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
