using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;
using Zenject;
// CluckWars.Logging.LogLevel collides with Fusion.LogLevel; alias to ours.
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Tags which system is currently contributing a slow to a chicken.
    /// Multiple sources can be active simultaneously; the minimum multiplier wins.
    /// Tracked so a future passive can exempt a specific source (e.g. a passive
    /// that ignores pile slow but not ability slow).
    /// </summary>
    [System.Flags]
    public enum SlowSource : byte
    {
        None      = 0,
        Collision = 1 << 0, // Two chickens brushing each other (GDD §6.1).
        Pile      = 1 << 1, // Standing on a food pile while collecting (GDD §6.2).
        Ability   = 1 << 2, // Applied by an ability (e.g. Feather Trap).
    }

    /// <summary>
    /// Compact, replicated control-state flags used purely to drive on-target VFX
    /// (the slow/root ground rings) on every peer. The gameplay effect itself
    /// already replicates via the networked transform (a slowed/rooted chicken
    /// moves differently, which every peer sees) — only this minimal trigger
    /// crosses the wire so the *particles/rings stay local* on each client.
    /// </summary>
    [System.Flags]
    public enum ControlVfx : byte
    {
        None   = 0,
        Slowed = 1 << 0,
        Rooted = 1 << 1,
    }

    /// <summary>
    /// Top-level networked chicken. Class-aware: a <c>[Networked]</c>
    /// <see cref="Class"/> selects the active <see cref="ChickenStatsSO"/> from
    /// the injected registry, with the prefab's serialized <c>_stats</c> kept only
    /// as a safety fallback.
    /// </summary>
    /// <remarks>
    /// State authority drives motion via the Fusion input buffer; pure-MonoBehaviour
    /// helpers (animator, visuals) stay local and react to <c>[Networked]</c> state.
    ///
    /// v0.3: added <see cref="SlowMultiplier"/>, <see cref="Rooted"/>,
    /// <see cref="ExternalDisplacement"/> for the Interaction &amp; Control System
    /// (GDD §6). Also added passive hooks: <see cref="ApplySlow"/>,
    /// <see cref="ApplyKnockback"/>, <see cref="ApplyOutgoingDamage"/>.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ChickenController : NetworkBehaviour
    {
        private const string Source = "Chicken";

        // ---- Passive tuning constants (balance-pass values; Part B / test session) ---
        private const float CollisionSlowRadius      = 1.2f;  // metres — two chickens touching
        private const float CollisionSlowFactor      = 0.75f; // GDD TBD #7
        private const float PileSlowFactor           = 0.80f; // GDD TBD #6
        private const float SlipperySlowRetention    = 0.50f; // slows are 50% as effective for Slippery
        private const float SlipperyDurationReduction = 0.40f; // control-state durations 60% shorter for Slippery
        private const float ImmovableKnockbackFactor = 0.15f; // knockback heavily reduced for Immovable
        private const float ToughDamageBonus         = 1.25f; // 25% bonus outgoing damage for Tough
        private const float KnockbackDecayRate       = 8f;    // 1/s; ExternalDisplacement decays to zero
        private const float AuraSlowSearchRadius     = 10f;   // broadphase for CheckAuraSlow

        public static readonly System.Collections.Generic.List<ChickenController> ActiveControllers = new System.Collections.Generic.List<ChickenController>();

        // Static array for broadphase overlaps to prevent per-tick allocation
        private static readonly Collider[] _overlapHits = new Collider[16];

        [Tooltip("Fallback used only if the class registry is missing or has no entry for this chicken's class.")]
        [SerializeField] private ChickenStatsSO _fallbackStats;

        private CharacterController _characterController;
        private ChickenMovement _movement;
        private ChickenClassRegistrySO _registry;
        private ChickenStatsSO _activeStats;
        private ChickenCombat _combat;
        private ChickenCargo _cargo;
        private AbilityController _abilities;
        private ILogService _log;

        // Slow accumulation — reset to None/1 at top of each FixedUpdateNetwork.
        private SlowSource _activeSlowSources;

        // Timer-based ability slow (StateAuthority-side; set by RPC_ApplyAbilitySlow).
        private double _abilitySlowUntil  = double.MinValue;
        private float  _abilitySlowFactor = 1f;

        // Timer-based root (StateAuthority-side; set by RPC_ApplyRoot).
        private double _rootUntil = double.MinValue;

        /// <summary>The chicken's archetype. Replicated; set by the spawner via <c>OnBeforeSpawned</c>.</summary>
        [Networked] public ChickenClass Class { get; set; } = ChickenClass.Warrior;

        public ChickenStatsSO Stats => _activeStats != null ? _activeStats : _fallbackStats;
        public ChickenCombat Combat => _combat;
        public ChickenCargo Cargo => _cargo;
        public AbilityController Abilities => _abilities;

        // ---- Ability state (StateAuthority-side only) -------------------------
        // Abilities mutate these locally on the StateAuthority. Other peers don't
        // need to mirror these values — they observe the resulting [Networked]
        // position / HP changes instead.

        /// <summary>Multiplier applied to <c>Stats.MoveSpeed</c> by active abilities. 1 = no buff.</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        /// <summary>While true, <c>ChickenMovement</c> ignores planar input but keeps gravity.</summary>
        public bool MovementLocked { get; set; }

        /// <summary>While true, <c>ChickenCombat.RPC_ApplyDamage</c> drops incoming damage.</summary>
        public bool DamageImmune { get; set; }

        /// <summary>0 = full damage, 1 = no damage taken.</summary>
        public float DamageResistance { get; set; }

        /// <summary>When true, incoming damage is sent back to the attacker instead of applied here.</summary>
        public bool ReflectDamage { get; set; }

        /// <summary>
        /// Knockback impulse (world-units/sec) applied to the attacker when Spine Coat
        /// reflects damage. Set by <c>SpineCoatAbilitySO</c>; 0 when inactive.
        /// </summary>
        public float SpineCoatKnockbackStrength { get; set; }

        /// <summary>
        /// 0 = invisible, 1 = fully opaque. Networked so the fade is visible to every player.
        /// </summary>
        [Networked] public float VisualOpacity { get; set; }

        /// <summary>True for the Doppelganger decoy: skips input processing.</summary>
        public bool IsDecoy { get; set; }

        /// <summary>True for AI-controlled bots spawned in solo mode.</summary>
        [Networked] public bool IsBot { get; set; }

        /// <summary>
        /// Replicated slow/root state, set on the StateAuthority each tick. Read by
        /// <c>ControlStateVFX</c> on every peer to drive the local ground rings — the
        /// VFX themselves never cross the wire, only this flag does.
        /// </summary>
        [Networked] public ControlVfx ControlFlags { get; set; }

        /// <summary>
        /// Bumped on the StateAuthority each time a knockback impulse is applied. A
        /// one-shot networked "event" — peers watch for the change and fire the local
        /// knockback shockwave once. Wraps at 255 (only the change matters).
        /// </summary>
        [Networked] public byte KnockbackEventId { get; set; }

        /// <summary>
        /// Corner (0..3) this chicken spawned at — its match identity. Drives base
        /// ownership, deposit gating, leaderboard attribution, nameplate numbering,
        /// and restart teleports for humans and bots alike. Stamped by
        /// <c>MatchBootstrapper</c> in <c>onBeforeSpawned</c>; -1 = not yet assigned.
        /// </summary>
        [Networked] public int HomeCornerIndex { get; set; } = -1;

        // ---- v0.3 Feather Aura (Networked so every peer sees the caster's state) ---

        /// <summary>True while the Feather Aura ability is active on this chicken.
        /// Replicated so nearby chickens can self-apply the slow in their own FUN.</summary>
        [Networked] public bool  AuraSlowActive { get; set; }
        /// <summary>World-units radius of the active aura slow effect.</summary>
        [Networked] public float AuraSlowRadius { get; set; }
        /// <summary>Speed multiplier broadcast by the aura (applied to chickens inside the radius).</summary>
        [Networked] public float AuraSlowFactor { get; set; }

        // ---- v0.3 Control-state fields (GDD §6.4) ----------------------------

        /// <summary>
        /// Accumulated speed multiplier from all active slow sources this tick.
        /// 1 = no slow; reset to 1 at the top of each <c>FixedUpdateNetwork</c>
        /// and then re-populated by <see cref="ApplySlow"/> calls.
        /// Read by <see cref="ChickenMovement"/>.
        /// </summary>
        public float SlowMultiplier { get; set; } = 1f;

        /// <summary>
        /// Planar movement blocked (like <see cref="MovementLocked"/>) but abilities
        /// can still be cast while rooted. Gravity still runs.
        /// </summary>
        public bool Rooted { get; set; }

        /// <summary>
        /// External velocity impulse (units per second) applied by knockback effects.
        /// Decayed to zero by <see cref="ChickenMovement"/> each tick.
        /// Set via <see cref="ApplyKnockback"/> to respect the Immovable passive.
        /// </summary>
        public Vector3 ExternalDisplacement { get; set; }

        [Inject]
        public void Construct(ChickenClassRegistrySO registry, ILogService log)
        {
            _registry = registry;
            _log = log;
        }

        public override void Spawned()
        {
            ActiveControllers.Add(this);
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            _log?.Debug(Source, $"Spawned. Class={Class}, HasStateAuthority={HasStateAuthority}, registryBound={_registry != null}.");

            _characterController = GetComponent<CharacterController>();
            _combat = GetComponent<ChickenCombat>();
            _cargo = GetComponent<ChickenCargo>();
            _abilities = GetComponent<AbilityController>();
            _activeStats = ResolveStatsForClass(Class);

            if (_activeStats == null)
            {
                _log?.Error(Source, $"{name}: could not resolve stats for class '{Class}'. Assign _fallbackStats on the prefab or populate ChickenClassRegistry.");
                return;
            }

            _log?.Info(Source, $"Stats resolved: '{_activeStats.DisplayName}', moveSpeed={_activeStats.MoveSpeed}, passive={_activeStats.Passive}.");
            _movement = new ChickenMovement(_characterController, this);

            if (HasStateAuthority && VisualOpacity <= 0f) VisualOpacity = 1f;

            transform.localScale = Vector3.one * _activeStats.Scale;
            _log?.Debug(Source, $"Applied scale {_activeStats.Scale} for class {Class}.");

            if (_registry != null && _registry.TryGet(Class, out var entry))
            {
                var visuals = GetComponent<ChickenVisuals>();
                if (visuals != null)
                {
                    visuals.ApplyTint(entry.TintColor);
                    _log?.Debug(Source, $"Applied tint {entry.TintColor} for class {Class}.");
                }
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveControllers.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (_movement == null)
            {
                if (_log != null && _log.IsEnabled(LogLevel.Verbose))
                    _log.Verbose(Source, "FixedUpdateNetwork: _movement is null (stats unresolved?). Skipping tick.");
                return;
            }
            if (!HasStateAuthority) return;

            // Decoys (Doppelganger) share input authority with the caster — skip all
            // logic so the decoy doesn't walk in lockstep with the real chicken.
            if (IsDecoy) return;

            // ---- Reset and re-compute slow sources each tick -----------------
            // This runs for both player chickens AND bots so BotController.BotTick
            // benefits from the final SlowMultiplier that's set here.
            SlowMultiplier     = 1f;
            _activeSlowSources = SlowSource.None;
            Rooted             = false; // re-evaluated by timer check below

            CheckCollisionSlow();

            // Pile slow: ChickenCargo sets IsPileSlow on the previous tick (1-tick
            // lag is imperceptible; piles don't move).
            if (_cargo != null && _cargo.IsPileSlow)
                ApplySlow(SlowSource.Pile, PileSlowFactor);

            // Ability slow timer (RPC_ApplyAbilitySlow — zones, aura, etc.).
            if (Runner.SimulationTime < _abilitySlowUntil)
                ApplySlow(SlowSource.Ability, _abilitySlowFactor);

            // Feather Aura: self-check from nearby casters broadcasting an aura.
            CheckAuraSlow();

            // Placed-zone slow (Feather Trap).
            CheckAbilityZoneSlow();

            // Root timer: RPC_ApplyRoot sets _rootUntil; Rooted persists until it elapses.
            if (Runner.SimulationTime < _rootUntil) Rooted = true;

            // Publish the compact control-state for remote VFX. Particles stay local
            // on each peer; this replicated flag is the only thing that crosses.
            var vfx = ControlVfx.None;
            if (SlowMultiplier < 0.92f) vfx |= ControlVfx.Slowed;
            if (Rooted)                 vfx |= ControlVfx.Rooted;
            if (ControlFlags != vfx)    ControlFlags = vfx;

            // Bots exit here — BotController.BotTick handles their movement with
            // the SlowMultiplier already computed above.
            if (IsBot) return;

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return;

            if (_combat != null && _combat.IsStunned) return;

            if (GetInput<PlayerNetworkInput>(out var input))
            {
                _movement.Tick(input.Movement, Runner.DeltaTime);
            }
        }

        // ---- Passive hooks ---------------------------------------------------

        /// <summary>
        /// Applies a speed penalty from a tagged source. Multiple sources stack
        /// multiplicatively (the minimum multiplier wins). Respects the
        /// <see cref="ChickenPassive.Slippery"/> passive which halves the effect.
        /// Call <em>after</em> resetting <c>SlowMultiplier = 1f</c> at the top of
        /// each tick.
        /// </summary>
        public void ApplySlow(SlowSource source, float factor)
        {
            _activeSlowSources |= source;
            // Slippery passive: slow effect is partially negated.
            if (Stats?.Passive == ChickenPassive.Slippery)
                factor = Mathf.Lerp(1f, factor, SlipperySlowRetention);
            SlowMultiplier = Mathf.Min(SlowMultiplier, factor);
        }

        /// <summary>
        /// Sets an external displacement impulse on this chicken (integrated and
        /// decayed by <see cref="ChickenMovement"/>).
        /// Respects the <see cref="ChickenPassive.Immovable"/> passive which
        /// drastically reduces the impulse for Fatty.
        /// Must be called on the StateAuthority.
        /// </summary>
        public void ApplyKnockback(Vector3 impulse)
        {
            if (Stats?.Passive == ChickenPassive.Immovable)
                impulse *= ImmovableKnockbackFactor;
            ExternalDisplacement = impulse;
            // Fire the networked one-shot so every peer plays the shockwave locally.
            if (impulse.sqrMagnitude > 1f) KnockbackEventId++;
        }

        /// <summary>
        /// Scales an outgoing damage amount by this chicken's passive.
        /// <see cref="ChickenPassive.Tough"/> (Warrior) grants a bonus.
        /// Call before <see cref="ChickenCombat.RPC_ApplyDamage"/> when the
        /// damage source is this chicken's ability.
        /// </summary>
        public float ApplyOutgoingDamage(float rawAmount)
        {
            if (Stats?.Passive == ChickenPassive.Tough)
                return rawAmount * ToughDamageBonus;
            return rawAmount;
        }

        // ---- Networking helpers ----------------------------------------------

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_TeleportTo(Vector3 position)
        {
            if (_characterController != null)
            {
                _characterController.enabled = false;
                transform.position = position;
                _characterController.enabled = true;
            }
            else
            {
                transform.position = position;
            }
            _log?.Debug(Source, $"Teleported to {position}.");
        }

        // ---- v0.3 Interaction primitive RPCs (B1) — all route to StateAuthority ----

        /// <summary>
        /// Applies an external displacement (knockback) impulse. Routes to the
        /// chicken's StateAuthority; integrated + decayed by <see cref="ChickenMovement"/>.
        /// Respects <see cref="ChickenPassive.Immovable"/>.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyKnockback(Vector3 impulse)
        {
            ApplyKnockback(impulse); // already scales by Immovable passive
            _log?.Debug(Source, $"RPC_ApplyKnockback: impulse={impulse:F2}.");
        }

        /// <summary>
        /// Applies a timed ability slow. Respects <see cref="ChickenPassive.Slippery"/>
        /// — duration is reduced for Speedy.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyAbilitySlow(float duration, float factor)
        {
            if (Stats?.Passive == ChickenPassive.Slippery) duration *= SlipperyDurationReduction;
            _abilitySlowUntil  = Runner.SimulationTime + duration;
            _abilitySlowFactor = factor;
            _log?.Debug(Source, $"RPC_ApplyAbilitySlow: factor={factor:P0} for {duration:0.0}s.");
        }

        /// <summary>
        /// Roots this chicken for <paramref name="duration"/> seconds — movement
        /// blocked, abilities still castable (GDD §6.4). Respects
        /// <see cref="ChickenPassive.Slippery"/>.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyRoot(float duration)
        {
            if (Stats?.Passive == ChickenPassive.Slippery) duration *= SlipperyDurationReduction;
            _rootUntil = Runner.SimulationTime + duration;
            _log?.Debug(Source, $"RPC_ApplyRoot: rooted for {duration:0.0}s.");
        }

        /// <summary>
        /// Resets all v0.3 control states (slow, root, knockback, aura).
        /// Called by <see cref="GameManager"/> on match restart.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetControlStates()
        {
            _abilitySlowUntil    = double.MinValue;
            _abilitySlowFactor   = 1f;
            _rootUntil           = double.MinValue;
            Rooted               = false;
            AuraSlowActive       = false;
            ExternalDisplacement = Vector3.zero;
            _log?.Debug(Source, "Control states reset for new match.");
        }

        /// <summary>
        /// Drives movement for bot-controlled chickens. Called by
        /// <see cref="BotController"/> each <c>FixedUpdateNetwork</c> tick instead
        /// of reading Fusion player input. Only valid on the StateAuthority peer.
        /// </summary>
        public void BotTick(Vector2 movement, float deltaTime)
        {
            if (!HasStateAuthority || _movement == null) return;
            _movement.Tick(movement, deltaTime);
        }

        // ---- Private helpers -------------------------------------------------

        /// <summary>
        /// Checks whether another live chicken is within contact range and, if so,
        /// applies the collision slow source. Runs once per FixedUpdateNetwork on
        /// the authority — avoids relying on OnTriggerStay which is unreliable for
        /// networked state (CONVENTIONS.md).
        /// </summary>
        private void CheckCollisionSlow()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position, CollisionSlowRadius, _overlapHits, ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                var other = _overlapHits[i].GetComponentInParent<ChickenController>();
                if (other == null || other == this) continue;
                // Ignore dead chickens (stunned / falling through respawn).
                if (other.Combat != null && other.Combat.IsDead) continue;
                ApplySlow(SlowSource.Collision, CollisionSlowFactor);
                break; // One other chicken is enough to trigger the slow.
            }
        }

        /// <summary>
        /// Checks whether this chicken is inside the aura of any nearby caster with
        /// <see cref="AuraSlowActive"/> set. Runs locally on this chicken's authority —
        /// no RPC needed because <c>AuraSlowActive/Radius/Factor</c> are Networked.
        /// </summary>
        private void CheckAuraSlow()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position, AuraSlowSearchRadius, _overlapHits, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                var caster = _overlapHits[i].GetComponentInParent<ChickenController>();
                if (caster == null || caster == this) continue;
                if (!caster.AuraSlowActive) continue;
                float sqr = (caster.transform.position - transform.position).sqrMagnitude;
                if (sqr <= caster.AuraSlowRadius * caster.AuraSlowRadius)
                    ApplySlow(SlowSource.Ability, caster.AuraSlowFactor);
            }
        }

        /// <summary>
        /// Applies the slow factor from any active <see cref="AbilityZone"/>s of
        /// type <see cref="ZoneEffect.Slow"/> within their trigger radius. Runs
        /// locally — no RPC needed because zones are Networked scene objects.
        /// </summary>
        private void CheckAbilityZoneSlow()
        {
            for (int i = 0; i < AbilityZone.ActiveZones.Count; i++)
            {
                var zone = AbilityZone.ActiveZones[i];
                if (zone == null || zone.Effect != ZoneEffect.Slow) continue;
                float sqr = (zone.transform.position - transform.position).sqrMagnitude;
                float r   = zone.TriggerRadius;
                if (sqr <= r * r)
                    ApplySlow(SlowSource.Ability, zone.SlowFactor);
            }
        }

        private ChickenStatsSO ResolveStatsForClass(ChickenClass cls)
        {
            if (_registry != null && _registry.TryGet(cls, out var entry) && entry.Stats != null)
                return entry.Stats;
            return _fallbackStats;
        }
    }
}
