using System.Collections.Generic;
using CluckWars.Abilities;
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
    /// What an <see cref="AbilityZone"/>'s centre is anchored to (WP3). Byte, explicit and append-only: a
    /// <c>[Networked]</c> field, so the values are part of the wire format.
    /// </summary>
    public enum ZoneAnchor : byte
    {
        /// <summary>Static AoE: the stamped spawn position, fixed for the zone's life.</summary>
        World = 0,
        /// <summary>Aura: the owner chicken's live position, as each peer sees it. Never networked as a position.</summary>
        Caster = 1,
    }

    /// <summary>
    /// The networked area-effect object for both effect types (WP3): a <b>Static AoE</b> (Feather Trap, Root Egg, Smoke
    /// Roost; <see cref="ZoneAnchor.World"/>) and an <b>Aura</b> (<see cref="ZoneAnchor.Caster"/>; Feather Aura since
    /// WP4). Spawned by <see cref="AreaEffectSpawner"/>; parameters are stamped in <c>onBeforeSpawned</c> from tick zero.
    /// </summary>
    /// <remarks>
    /// <b>One centre.</b> Every simulation and feedback test reads <see cref="TryGetCenter"/>, never the transform: a
    /// Caster zone's transform is moved to its centre in <see cref="Render"/> for <see cref="AbilityZoneVisuals"/> only,
    /// so a simulation read of it would be a frame stale (and FUN order would matter). Nothing is parented under the
    /// chicken and there is no NetworkTransform (decision F2): the aura costs zero bandwidth after its spawn.
    /// Locked by <c>AreaEffectTypeTests.NoSimulationRead_OfZoneTransform</c>.
    /// <para>
    /// <b>Channels</b> (<see cref="Channels"/>, B3): <see cref="AreaEffectChannel.OnEnter"/> fires once per chicken per
    /// zone life, including chickens already inside when the zone first runs. On a Root zone it is evaluated HERE, on the
    /// zone's authority (Root Egg: <see cref="ChickenController.RPC_ApplyRoot"/>, then consumed). On a Slow zone (Feather
    /// Aura, WP4) it changes no state - the slow is WhileInside - and is the per-chicken entry confirmation, evaluated in
    /// <see cref="Render"/> on every peer from replicated positions (<see cref="ObserveSlowZoneEntry"/>).
    /// <see cref="AreaEffectChannel.WhileInside"/> is evaluated by each VICTIM's own authority in
    /// <see cref="ChickenController.CheckAbilityZoneSlow"/> from <see cref="ActiveZones"/>, zero RPC (B2 hybrid
    /// authority). Periodic is spec-only (WP5).
    /// </para>
    /// <para>
    /// <b>Maestro:</b> one <c>AbilityZone</c> prefab (NetworkObject + this script + <c>AbilityZoneVisuals</c>), assigned
    /// to <c>PrefabRegistrySO.AbilityZone</c>. The WP3 fields need no prefab re-bake.
    /// </para>
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
        /// Egg placed at the caster's feet would otherwise self-root next tick). A Caster zone's anchor.
        /// </summary>
        [Networked] public NetworkBehaviourId OwnerChicken { get; set; }

        /// <summary>
        /// Where the caster placed the zone, stamped in <c>onBeforeSpawned</c> by the spawner: the prefab has no
        /// NetworkTransform and Fusion does not replicate a spawn position without one (<see cref="SpawnPlacement"/>), so
        /// every other peer stood the zone at the prefab root - and every OTHER chicken's own authority, which tests zone
        /// overlap in its own tick, never felt a zone placed by someone else (CW-217's differing World hashes).
        /// A Caster zone keeps it as "where it was cast"; its centre is the caster.
        /// </summary>
        [Networked] public Vector3 SpawnPosition { get; set; }

        /// <summary><see cref="SpawnPosition"/> was stamped.</summary>
        [Networked] public NetworkBool HasSpawnPosition { get; set; }

        // WP3 (protocol change): three byte-sized fields, stamped last by AreaEffectSpawner. Fusion stores each
        // networked field in its own 4-byte word, so the layout grows by 12 bytes. Byte enums / byte only (weaver).

        /// <summary>
        /// World (Static AoE) or Caster (Aura). Only <see cref="AreaEffectSpawner"/> creates zones and it always stamps
        /// this; an unstamped zone would also have <see cref="Channels"/> None and affect nobody.
        /// </summary>
        [Networked] public ZoneAnchor Anchor { get; set; }

        /// <summary>
        /// Caster zones that end with their ability: the casting slot (<c>AbilityController.IsSlotActive</c>).
        /// <see cref="AreaEffectRules.NoSourceSlot"/> for every zone that outlives its cast.
        /// </summary>
        [Networked] public byte SourceSlot { get; set; }

        /// <summary>How the zone applies its effect over time (B3). Root Egg: OnEnter; the slows: WhileInside.</summary>
        [Networked] public AreaEffectChannel Channels { get; set; }

        private bool _placed;

        /// <summary>OnEnter, on the authority: who this zone life has already fired on (cleared per spawn; pooled).</summary>
        private readonly HashSet<NetworkBehaviourId> _entered = new HashSet<NetworkBehaviourId>();

        // ---- Read-only accessors (ChickenController uses these) ---------------

        /// <summary>
        /// Returns <see cref="NetworkedRadius"/> if set, else the prefab's
        /// serialized default <c>_triggerRadius</c>.
        /// </summary>
        public float TriggerRadius => NetworkedRadius > 0f ? NetworkedRadius : _triggerRadius;

        /// <summary>True when the zone applies its effect on <paramref name="channel"/>.</summary>
        public bool HasChannel(AreaEffectChannel channel) => (Channels & channel) != 0;

        /// <summary>
        /// The one centre every overlap and feedback test uses (<see cref="AreaEffectRules.TryResolveCenter"/>). World:
        /// the stamped spawn position (the transform on the authority before its own stamp). Caster: the owner's
        /// transform as this peer sees it; false while the owner is unresolved or removed - the zone is then inert.
        /// </summary>
        public bool TryGetCenter(out Vector3 center)
        {
            ChickenController owner = null;
            bool available = Anchor == ZoneAnchor.Caster && TryGetAvailableOwner(out owner);
            return AreaEffectRules.TryResolveCenter(Anchor, HasSpawnPosition, SpawnPosition, transform.position,
                available, available ? owner.transform.position : default, out center);
        }

        /// <summary>
        /// A live aura of <paramref name="owner"/> that ends with <paramref name="slot"/> exists on this peer
        /// (<see cref="AreaEffectRules.BlocksRecast"/>). Read on the caster's authority, where its own zones are spawned.
        /// </summary>
        public static bool HasLiveAuraFrom(NetworkBehaviourId owner, int slot)
        {
            for (int i = 0; i < ActiveZones.Count; i++)
            {
                var z = ActiveZones[i];
                if (z == null || z.Object == null || !z.Object.IsValid) continue;
                if (AreaEffectRules.BlocksRecast(z.Anchor, z.OwnerChicken == owner, z.SourceSlot, slot)) return true;
            }
            return false;
        }

        /// <summary>The owner resolves on this peer, is live, and has not been removed from the match.</summary>
        private bool TryGetAvailableOwner(out ChickenController owner)
        {
            owner = null;
            if (Runner == null || !Runner.TryFindBehaviour(OwnerChicken, out owner) || owner == null) return false;
            if (owner.Object == null || !owner.Object.IsValid) return false;
            return owner.Combat == null || !owner.Combat.IsRemoved;
        }

        // ---- Root-claim evidence for the victim's authority (F4) --------------
        //
        // RPC_ApplyRoot names the CASTER, and the victim's authority accepts a root from a caster
        // out of direct reach only while it stands inside a Root zone that caster owns. A live zone
        // answers from ActiveZones. The record below covers the one honest case a live lookup
        // misses: the zone was consumed and its despawn replicated to this peer before the RPC it
        // sent in the same tick (an RPC is not ordered against state). Local, never networked; a
        // record lives RecentRootWindowSeconds of real time, longer than any latency the range
        // allowances accept (ExecuteRules.MaxLatencySeconds). A zone consumed in its first ticks
        // may never reach this peer at all; Root Egg spawns at its caster's feet, so that case is
        // inside the caster's direct reach instead.

        /// <summary>How long a despawned Root zone still counts as evidence for a root claim from its owner.</summary>
        public const float RecentRootWindowSeconds = 1.0f;

        // This life's root evidence (local, plain fields, never networked). Snapshotted in Spawned; Render refreshes only
        // the geometry, on every peer, so Despawned records it without reading networked state: a remote copy's
        // Despawned may get hasState == false, and a record gated on it would be lost exactly on the victim's peer (qa W1).
        // The owner, the zone's state authority and the caster's position are captured ONCE, when THIS peer first sees
        // the zone (security review 2026-10-22): a later change of owner or authority voids the evidence instead of
        // silently re-anchoring it, and the geometry is always checked against that fixed anchor.
        private struct RootSnapshot
        {
            public NetworkBehaviourId Owner;   // snapshot, never refreshed
            public PlayerRef Authority;        // the zone's state authority; snapshot once (None = not captured yet, qa W2)
            public bool IdentityChanged;       // owner or authority differed from the snapshot later: no evidence
            public Vector3 Center;
            public float Radius;               // the zone's own radius; clamped by ControlClaimRules
            public bool HasCasterAtSpawn;
            public Vector3 CasterAtSpawn;
            public float SpawnDrift;
            public float CasterDriftSpeed;     // m/s at first sight; prices the late-joiner extension (qa W1)
            public bool InJoinSnapshot;        // part of this peer's initial snapshot (local Spawned tick <= join tick)
            public float RemainingAtFirstSight;// the networked LifetimeTimer then (attacker-writable; -1 = unknown)
            public float FirstSeenAt;          // Time.realtimeSinceStartup
            public float DespawnedAt;          // records only
        }

        private bool _seenAsRoot;
        private RootSnapshot _seen;

        private static readonly List<RootSnapshot> _recentRoots = new List<RootSnapshot>();

        /// <summary>Spawned: the full snapshot, including who owns the zone and where the caster stands as this peer first sees it.</summary>
        private void CaptureRootEvidence()
        {
            // Placed root zones only. A future Caster-anchored root would move with its caster, which the fixed-anchor
            // credibility check cannot judge: it needs its own rule before it may vouch.
            _seenAsRoot = Effect == ZoneEffect.Root && Anchor == ZoneAnchor.World;
            if (!_seenAsRoot) return;
            _seen = default;
            _seen.FirstSeenAt = Time.realtimeSinceStartup;
            _seen.Owner       = OwnerChicken;
            _seen.Authority   = Object != null ? Object.StateAuthority : default;
            // Late joiner (qa W1): an egg in this peer's initial snapshot may be up to a lifetime old. The join tick and
            // this Spawned's tick are local; the remaining time is the zone's own claim, priced (and capped) by
            // ControlClaimRules.LateJoinDrift. _spawnTick is this peer's Spawned tick (set just above in Spawned).
            _seen.InJoinSnapshot = LocalJoinClock.InJoinSnapshot(Runner, _spawnTick);
            _seen.RemainingAtFirstSight = Runner != null ? (LifetimeTimer.RemainingTime(Runner) ?? -1f) : -1f;
            RefreshRootGeometry();
            if (Runner != null && Runner.TryFindBehaviour(OwnerChicken, out ChickenController caster) &&
                caster != null && caster.Object != null && caster.Object.IsValid)
            {
                // The zone and its caster reach this peer in the same snapshot, so the caster has moved at most its
                // drift over the zone authority's latency (0 on the authority, which spawns inside the caster's tick).
                float latency = HasStateAuthority ? 0f : ExecuteRules.RemoteLatency((float)Runner.GetPlayerRtt(Object.StateAuthority));
                _seen.HasCasterAtSpawn = true;
                _seen.CasterAtSpawn = caster.transform.position;
                _seen.CasterDriftSpeed = ChickenCargo.DriftSpeed(caster);
                _seen.SpawnDrift = _seen.CasterDriftSpeed * (latency + 2f * Runner.DeltaTime);
            }
            else
            {
                // Legitimate (late join, caster still in flight), so not an error - but this zone is then no evidence
                // here: a root its owner sends from out of direct reach is refused once on this peer and logged there.
                _log?.Debug(Source, "Root zone seen before its caster resolved on this peer: it will not vouch for far root claims here.");
            }
        }

        /// <summary>
        /// Render: refreshes the geometry only; a changed owner or authority voids the evidence instead. A stored None
        /// authority is "not captured yet" (qa W2): it is captured on the first frame the zone has a valid one.
        /// </summary>
        private void ObserveRootEvidence()
        {
            if (!_seenAsRoot || _seen.IdentityChanged) return;
            var authority = Object != null ? Object.StateAuthority : default;
            var seen = ControlClaimRules.ObserveIdentity(
                sameOwner: OwnerChicken == _seen.Owner,
                snapshotAuthorityKnown: _seen.Authority != PlayerRef.None,
                authorityKnown: authority != PlayerRef.None,
                sameAuthority: authority == _seen.Authority);
            if (seen == ControlClaimRules.IdentityObservation.CaptureAuthority)
            {
                _seen.Authority = authority;   // the one deferred capture; never overwritten after this
            }
            else if (seen == ControlClaimRules.IdentityObservation.Changed)
            {
                _seen.IdentityChanged = true;
                _log?.Warn(Source, $"Root zone changed hands after this peer first saw it (owner {_seen.Owner} -> {OwnerChicken}, " +
                    $"authority {_seen.Authority} -> {authority}): it no longer vouches for root claims.");
                return;
            }
            RefreshRootGeometry();
        }

        private void RefreshRootGeometry()
        {
            if (TryGetCenter(out Vector3 center)) _seen.Center = center;
            _seen.Radius = TriggerRadius;
        }

        private static void RememberDespawnedRoot(RootSnapshot snapshot)
        {
            float now = Time.realtimeSinceStartup;
            _recentRoots.RemoveAll(r => now - r.DespawnedAt > RecentRootWindowSeconds);
            snapshot.DespawnedAt = now;
            _recentRoots.Add(snapshot);
        }

        private static ControlClaimRules.RootZoneEvidence Evidence(in RootSnapshot s, PlayerRef casterAuthority, float now) =>
            new ControlClaimRules.RootZoneEvidence
            {
                SameAuthorityAsCaster = s.Authority == casterAuthority,
                IdentityChanged = s.IdentityChanged,
                Center = s.Center,
                Radius = s.Radius,
                HasCasterAtSpawn = s.HasCasterAtSpawn,
                CasterAtSpawn = s.CasterAtSpawn,
                SpawnDrift = s.SpawnDrift,
                SecondsSinceSeen = now - s.FirstSeenAt,
                InJoinSnapshot = s.InJoinSnapshot,
                RemainingAtFirstSight = s.RemainingAtFirstSight,
                CasterDriftSpeed = s.CasterDriftSpeed,
            };

        /// <summary>
        /// True when <paramref name="point"/> is inside (plus <paramref name="margin"/>) a Root zone owned by
        /// <paramref name="caster"/> that is alive on this peer or despawned within <see cref="RecentRootWindowSeconds"/>,
        /// judged by <see cref="ControlClaimRules.RootZoneVouches"/>: same, unchanged state authority as the caster, a
        /// centre where the caster stood when this peer first saw it, inside its authored life, the radius clamped to the
        /// pool's authored one. Read by <c>ChickenController</c>'s root-claim check.
        /// </summary>
        public static bool ContainsRootOf(ChickenController caster, Vector3 point, float margin, in ControlClaimRules.Ceiling ceiling)
        {
            if (caster == null || caster.Object == null || !caster.Object.IsValid) return false;
            var owner = caster.Id;
            var authority = caster.Object.StateAuthority;
            float now = Time.realtimeSinceStartup;

            for (int i = 0; i < ActiveZones.Count; i++)
            {
                var z = ActiveZones[i];
                if (z == null || !z._seenAsRoot || z._seen.Owner != owner) continue;
                if (ControlClaimRules.RootZoneVouches(Evidence(z._seen, authority, now), point, margin, ceiling))
                    return true;
            }

            for (int i = 0; i < _recentRoots.Count; i++)
            {
                var r = _recentRoots[i];
                if (r.Owner != owner || now - r.DespawnedAt > RecentRootWindowSeconds) continue;
                if (ControlClaimRules.RootZoneVouches(Evidence(r, authority, now), point, margin, ceiling))
                    return true;
            }
            return false;
        }

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

        /// <summary>Slow zones with the OnEnter channel (Feather Aura): who this zone life has already confirmed on THIS
        /// peer (local, re-armed per spawn).</summary>
        private readonly HashSet<NetworkBehaviourId> _confirmed = new HashSet<NetworkBehaviourId>();

        /// <summary>Root zones confirm exactly once. Shared between the
        /// <see cref="Render"/> detector and the <see cref="Despawned"/> fallback so the two
        /// can never both fire for one trigger.</summary>
        private bool _rootConfirmSent;

        /// <summary>
        /// The reset tick last delivered by <see cref="MatchSignals.RoundReset"/> (B10; 0 = none) and this life's spawn tick.
        /// The authority despawns at the top of its next <see cref="FixedUpdateNetwork"/> when
        /// <see cref="RoundResetObservation.OutlivedReset"/> says this zone belongs to the round that reset ended - never
        /// inside the handler (it can run inside another object's tick, a <c>Spawned</c> or a UI press). Re-armed per
        /// spawn: Fusion pools this object.
        /// </summary>
        /// <remarks>
        /// <b>When the caster's peer leaves (hostleave, B10 audit):</b> the prefab's <c>NetworkObject</c> is serialised with
        /// <c>Flags: 262145</c> = <c>V1 (1) | DestroyWhenStateAuthorityLeaves (262144)</c>, without
        /// <c>MasterClientObject (131072)</c> (values read from <c>Fusion.Runtime.dll</c>'s <c>NetworkObjectFlags</c>). In
        /// Shared Mode the zone's state authority is the peer that spawned it, so when that peer leaves Fusion destroys the
        /// zone on every remaining peer: it is never orphaned. A bot's zone has the master as authority and goes with it.
        /// <c>RoundResetSignalTests.ZoneAndDecoyPrefabs_AreDestroyedWithTheirAuthority</c> pins the flag; an orphan would
        /// show as <c>zones-zero</c> at the next round start.
        /// </remarks>
        private int _resetTick;
        private int _spawnTick;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        // ---- Fusion lifecycle -------------------------------------------------

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            ActiveZones.Add(this);
            LocalJoinClock.Note(Runner);   // a zone in a joiner's first snapshot can be the first thing it sees

            if (HasStateAuthority && !HasSpawnPosition)
            {
                SpawnPosition = transform.position;
                HasSpawnPosition = true;
            }
            ApplySpawnPosition();

            _changes        = GetChangeDetector(ChangeDetector.Source.SimulationState);
            _consumedReader = GetPropertyReader<bool>(nameof(Consumed));

            // Fusion pools NetworkObjects, so both observation flags must be re-armed per
            // spawn — a reused instance would otherwise inherit "already confirmed" (root)
            // or "already occupied" (slow) from the previous zone and stay silent.
            _rootConfirmSent  = false;
            _slowZoneOccupied = false;
            _entered.Clear();   // OnEnter fires once per chicken per zone LIFE
            _confirmed.Clear();

            _resetTick = 0;
            _spawnTick = Runner.Tick.Raw;
            MatchSignals.RoundReset -= OnRoundReset;   // never twice, whatever the pool did
            MatchSignals.RoundReset += OnRoundReset;

            CaptureRootEvidence();

            _log?.Debug(Source, $"Spawned. Effect={Effect}, Anchor={Anchor}, Channels={Channels}, Radius={TriggerRadius}, " +
                $"Slow={SlowFactor:P0}, RootDuration={RootDuration:0.0}s.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            MatchSignals.RoundReset -= OnRoundReset;
            ActiveZones.Remove(this);
            // From the local snapshot, never gated on hasState (W1): see ObserveRootEvidence.
            if (_seenAsRoot) RememberDespawnedRoot(_seen);
            _seenAsRoot = false;   // pooled: the next life observes its own
            _placed = false;   // pooled: the next life re-applies its own stamp

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

            // The round was reset (B10): no zone outlives the round it was placed in.
            if (RoundResetObservation.OutlivedReset(_spawnTick, _resetTick))
            {
                _log?.Info(Source, $"Despawned by the round reset (effect={Effect}, consumed={Consumed}, spawned tick {_spawnTick} <= reset tick {_resetTick}).");
                Runner.Despawn(Object);
                return;
            }

            // Lifetime, consumption, and (Caster zones) owner gone (B5) or the casting slot ended. The owner and the
            // slot share this zone's state authority (the caster's peer, or the master for a bot), so no RPC is needed.
            ChickenController owner = null;
            bool ownerAvailable = Anchor == ZoneAnchor.Caster && TryGetAvailableOwner(out owner);
            bool slotActive = ownerAvailable && owner.Abilities != null && owner.Abilities.IsSlotActive(SourceSlot);
            var end = AreaEffectRules.Despawn(LifetimeTimer.Expired(Runner), Consumed, Anchor, ownerAvailable, SourceSlot, slotActive);
            if (end != ZoneEnd.None)
            {
                if (end == ZoneEnd.OwnerGone || end == ZoneEnd.AbilityEnded)
                    _log?.Debug(Source, $"Caster zone despawned: {end} (effect={Effect}, slot={SourceSlot}).");
                Runner.Despawn(Object);
                return;
            }

            // OnEnter (Root Egg). WhileInside is evaluated by each victim (ChickenController.CheckAbilityZoneSlow).
            if (Effect == ZoneEffect.Root && HasChannel(AreaEffectChannel.OnEnter))
            {
                ScanOnEnter();
            }
        }

        /// <summary>Record only; <see cref="FixedUpdateNetwork"/> acts (see <see cref="_resetTick"/>).</summary>
        private void OnRoundReset(int resetTick) => _resetTick = resetTick;

        /// <summary>
        /// The OnEnter channel (B3), on the zone's authority: every chicken inside that this zone life has not fired on
        /// yet fires once - which includes the chickens already inside on the zone's first authority tick (Maestro: "it
        /// should have effect on the chickens that were in the zone when it is cast"). Iterates
        /// <see cref="ChickenController.ActiveControllers"/> (at most 4 entries) with the planar test
        /// (<see cref="AreaEffectRules.IsInside"/>), exactly like <c>AbilityBaseSO.GatherTargets</c> and
        /// <c>ChickenController.CheckAbilityZoneSlow</c>. Locked against <c>Physics.Overlap</c> by
        /// <c>AbilityAimTests.NoAbilityScript_CallsPhysicsOverlapDirectly</c>.
        ///
        /// <b>Root Egg</b> is consumed on its first catch, so it fires on exactly one chicken - the pre-WP3 behaviour.
        /// The authority sees remote victims a replication delay late, so a victim "inside at cast" on the caster's
        /// screen can register a few ticks later here: the slack favours the victim, like every caster-centred effect.
        ///
        /// <b>Balance note:</b> the trigger area is measured to a chicken's transform pivot rather than to the nearest
        /// point on its capsule, so the egg is very slightly harder to set off (by roughly the chicken's collider
        /// radius). The drawn ring is the truth.
        ///
        /// <b>A decoy does not set the egg off, even though decoys are targetable.</b> Being a valid target for a cast
        /// someone deliberately aimed is one thing; a placed trap is another — nobody aimed this at the decoy, it just
        /// walked over it. And a decoy cannot actually be rooted: <c>ChickenController.FixedUpdateNetwork</c>
        /// early-returns on <c>IsDecoy</c> before root is re-derived, so the egg would be consumed with no visible effect
        /// on anyone, while still firing a hit-confirm at its caster for a catch that did nothing. B7 makes that the rule
        /// for every zone channel: <see cref="AreaEffectRules.IgnoresChicken"/> (and its CW-232 note).
        /// </summary>
        private void ScanOnEnter()
        {
            if (!TryGetCenter(out Vector3 center)) return;   // a Caster zone whose owner is gone despawns next tick
            float radius = TriggerRadius;

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var chicken = all[i];
                if (chicken == null) continue;
                if (AreaEffectRules.IgnoresChicken(chicken.Id == OwnerChicken, chicken.IsDecoy)) continue;
                if (chicken.Combat != null && chicken.Combat.IsRemoved) continue;
                if (!AreaEffectRules.IsInside(center, radius, chicken.transform.position)) continue;
                if (!_entered.Add(chicken.Id)) continue;   // already fired on this chicken in this life

                // Names the caster, not this zone: the victim's authority checks the sender against the
                // caster's state authority (the zone's own), and may never have seen this zone at all
                // when it is consumed in its first ticks (F4, see ContainsRootOf).
                chicken.RPC_ApplyRoot(RootDuration, OwnerChicken);
                _log?.Debug(Source, $"Root applied to {chicken.name}.");

                // No hit-attribution push here, deliberately: this method is behind
                // FixedUpdateNetwork's HasStateAuthority gate, so it runs on the zone owner's
                // peer alone and the victim's HitFeedback is a remote proxy there. The push
                // belongs on the every-peer Render path — see NotifyCaughtVictims, driven off
                // the Consumed flip below.

                // Root Egg: consumed on first trigger (AreaEffectSpec.ConsumeOnTrigger; the spawner refuses any root spec
                // that is not). Every peer watches this flip in Render() and confirms the cast to the caster.
                Consumed = true;
                return;
            }
        }

        /// <summary>Moves a non-authority copy to the caster's spot (<see cref="SpawnPlacement"/>); retried from Render until stamped.</summary>
        private void ApplySpawnPosition()
        {
            if (SpawnPlacement.ProxyMustMove(HasStateAuthority, HasSpawnPosition, SpawnPosition, transform.position))
                transform.position = SpawnPosition;
            _placed = HasStateAuthority || HasSpawnPosition;
        }

        public override void Render()
        {
            // A Caster zone's transform follows its centre for AbilityZoneVisuals ONLY (F2: visuals on the zone, nothing
            // parented under the chicken). Simulation never reads it - see TryGetCenter.
            if (Anchor == ZoneAnchor.Caster) { if (TryGetCenter(out Vector3 follow)) transform.position = follow; }
            else if (!_placed) ApplySpawnPosition();
            ObserveRootEvidence();
            if (Effect == ZoneEffect.Root) ObserveRootConsumed();
            else                           ObserveSlowZoneEntry();
        }

        // ---- Local confirmation to the caster (no RPC) ------------------------

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
        /// no-op. Gives the caster of a placed zone the hit-confirm their cast could not give
        /// them: a zone reports 0 targets at cast time by construction (see
        /// <c>AbilityBaseSO.ReportsCastHits</c>).</summary>
        private void ConfirmRootTriggerOnce(NetworkRunner runner)
        {
            if (_rootConfirmSent) return;
            _rootConfirmSent = true;
            NotifyCaughtVictims();
            NotifyOwnerZoneTriggered(runner);
        }

        /// <summary>
        /// Slow zones never flip a flag — they just sit there slowing whoever is inside — so
        /// the "it caught someone" edge is derived from replicated positions instead. Rising
        /// edge only: a rival standing in the cloud must not re-confirm every frame.
        /// <para>
        /// With the OnEnter channel (an Aura, WP4) the edge is per CHICKEN instead of "anyone": each rival confirms once
        /// per zone life, the first frame it is seen inside - so the rivals already inside at the cast confirm on the
        /// zone's first frame (Maestro's rule), and a second rival walking in later confirms again. An aura sweeps over
        /// people as its caster runs; "the cloud became occupied" would hide every catch after the first.
        /// </para>
        /// </summary>
        private void ObserveSlowZoneEntry()
        {
            if (HasChannel(AreaEffectChannel.OnEnter)) { ConfirmEachFirstEntry(); return; }

            bool occupied = AnyRivalInside();
            if (occupied && !_slowZoneOccupied)
            {
                NotifyCaughtVictims();
                NotifyOwnerZoneTriggered(Runner);
            }
            _slowZoneOccupied = occupied;
        }

        /// <summary>The OnEnter half of <see cref="ObserveSlowZoneEntry"/>: each newly inside rival gets its bearing, and
        /// the caster one confirmation per frame in which someone new was caught.</summary>
        private void ConfirmEachFirstEntry()
        {
            if (!TryGetCenter(out Vector3 center)) return;
            float radius = TriggerRadius;
            bool caughtSomeone = false;

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var chicken = all[i];
                if (!IsCaught(chicken, center, radius) || !_confirmed.Add(chicken.Id)) continue;
                chicken.GetComponent<HitFeedback>()?.NotifyAttacker(center);   // missing component: see NotifyCaughtVictims
                caughtSomeone = true;
            }
            if (caughtSomeone) NotifyOwnerZoneTriggered(Runner);
        }

        /// <summary>
        /// Tells everyone this zone just caught <i>where the hit came from</i>, so their §3.2
        /// impact beat has a direction instead of pointing nowhere. Runs from
        /// <see cref="Render"/> (and <see cref="Despawned"/>) on <b>every</b> peer off already
        /// replicated state, so no RPC and no new networked property — the same reasoning
        /// <see cref="NotifyOwnerZoneTriggered"/> uses for the caster's half.
        /// </summary>
        /// <remarks>
        /// <b>It pushes the zone's CENTRE.</b> For the two anchors that is two different, equally honest answers:
        /// <list type="bullet">
        ///   <item><b>Static AoE (World): the zone, never the owner — and no, pointing at the caster is not the fix.</b>
        ///   That is the tempting change and it is wrong twice over, so if you arrived here thinking you had found a bug,
        ///   you have not. (1) <b>It would leak.</b> The trap-layer may be halfway across the arena by now; an arrow at
        ///   their live position hands the victim free intel about where an opponent currently is, which is exactly the
        ///   information asymmetry FEEDBACK.md §1.6 exists to protect. The whole point of a placed trap is that it acts
        ///   while its owner is elsewhere. (2) <b>It would be less true.</b> The zone <i>is</i> the proximate cause — the
        ///   trap is the thing that hit you, and the victim's honest question is "what did I walk into", not "who laid
        ///   it".</item>
        ///   <item><b>Aura (Caster): the caster (B6, decided 2026-10-21).</b> The aura's centre IS the caster, who is
        ///   standing right there inside the victim's view: pointing at them is the true cause and leaks nothing the
        ///   victim cannot already see. Same code path; only the centre differs.</item>
        /// </list>
        /// It also structurally cannot implicate a bystander, which was the original bug (see
        /// <c>HitAttribution</c>'s remarks).
        /// <para>
        /// <b>The degenerate case is intended.</b> A victim standing on top of the zone centre
        /// is closer to it than <c>FeedbackTuning.HitAttributionMinSeparation</c>, so
        /// <c>HitAttribution.TryBearing</c> declines and no direction is drawn at all. That is
        /// correct and it is not worth "fixing": an arrow that swung around as the victim
        /// shuffled about the centre of a trap would be noise animated to look like
        /// information. Clip the edge instead and the bearing points back at the trap, which
        /// is both stable and useful.
        /// </para>
        /// </remarks>
        private void NotifyCaughtVictims()
        {
            if (!TryGetCenter(out Vector3 center)) return;
            float radius = TriggerRadius;

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var chicken = all[i];
                if (!IsCaught(chicken, center, radius)) continue;

                // Same lookup NotifyOwnerZoneTriggered uses. A missing component is already
                // reported loudly there once per zone; staying quiet here avoids one warning
                // per victim per frame for the same prefab fault.
                chicken.GetComponent<HitFeedback>()?.NotifyAttacker(center);
            }
        }

        /// <summary>Same registry + planar-XZ test as <see cref="ScanOnEnter"/>, so the
        /// confirmation and the effect agree on what "inside" means. At most 4 entries —
        /// no <c>Physics.Overlap</c>.</summary>
        private bool AnyRivalInside()
        {
            if (!TryGetCenter(out Vector3 center)) return false;
            float radius = TriggerRadius;

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
                if (IsCaught(all[i], center, radius)) return true;

            return false;
        }

        /// <summary>The single "does this chicken count as caught" predicate, shared by the
        /// occupancy edge and the victim attribution so the two can never disagree about who
        /// was in the cloud.</summary>
        private bool IsCaught(ChickenController chicken, Vector3 center, float radius)
        {
            if (chicken == null) return false;
            if (AreaEffectRules.IgnoresChicken(chicken.Id == OwnerChicken, chicken.IsDecoy)) return false; // a phantom is not a catch
            if (chicken.Combat != null && chicken.Combat.IsRemoved) return false;

            var obj = chicken.Object;
            if (obj == null || !obj.IsValid) return false;

            return AreaEffectRules.IsInside(center, radius, chicken.transform.position);
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
    }
}
