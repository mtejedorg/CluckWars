using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Logging;
using CluckWars.Networking;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked ability slot manager on the chicken. Holds up to two equipped
    /// <see cref="AbilityBaseSO"/>s (slot 0 used by every class; slot 1 only by
    /// Assassin), drives activation from the Fusion input buffer, and owns the
    /// per-slot cooldown timers.
    /// </summary>
    /// <remarks>
    /// Exactly one ability can be active at a time — pressing Ability1/Ability2 is
    /// ignored if another is mid-duration. Ability gameplay effects mutate
    /// <see cref="ChickenController"/> state (move multiplier, movement lock,
    /// damage immunity) which lives only on the StateAuthority; remote peers
    /// observe the resulting <c>[Networked]</c> state (position, HP) instead of
    /// re-running ability logic. Cooldown progress is drawn from the
    /// <c>ActiveSlot</c> + <c>Cooldown0/1</c> networked properties so every peer
    /// (including the local HUD) sees an accurate radial fill.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AbilityController : NetworkBehaviour
    {
        private const string Source = "Ability";
        public const int InvalidSlot = -1;

        [Tooltip("Equipped ability for slot 0. Used by every class — Warrior / Speedy / Fatty / Assassin.")]
        [SerializeField] private AbilityBaseSO _slot0;

        [Tooltip("Equipped ability for slot 1. Assassin only; leave null for other classes.")]
        [SerializeField] private AbilityBaseSO _slot1;

        [Networked] public int ActiveSlot { get; set; }
        [Networked] private TickTimer ActivationTimer { get; set; }
        [Networked] private TickTimer Cooldown0 { get; set; }
        [Networked] private TickTimer Cooldown1 { get; set; }

        public AbilityBaseSO Slot0 => _slot0;
        public AbilityBaseSO Slot1 => _slot1;

        public AbilityBaseSO ActiveAbility => GetSlot(ActiveSlot);

        private ChickenController _controller;
        private AbilityContext _ctx;
        private ChickenCombat _combat;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private bool _initialized;

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
        }

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();
            _ctx = new AbilityContext(_controller);

            // Only the StateAuthority initializes the slot to "none"; proxies pick up
            // the replicated value automatically.
            if (HasStateAuthority)
            {
                ActiveSlot = InvalidSlot;
            }

            // Subscribe to OnDeath so an ability mid-duration is forcibly ended when
            // the chicken dies (so the chicken doesn't respawn buffed / locked).
            if (_combat != null) _combat.OnDeath += HandleOwnerDeath;

            _initialized = true;
            _log?.Debug(Source, $"Spawned. Slot0={(Slot0 != null ? Slot0.name : "(none)")}, Slot1={(Slot1 != null ? Slot1.name : "(none)")}.");

            // Class-vs-ability allowlist check (GDD §7.1). Empty allowlist on the
            // class means "any ability", so this only warns when a class has an
            // explicit pool and the equipped slot isn't in it. Doesn't block —
            // the ability still works; this is a design-intent guard.
            var stats = _controller != null ? _controller.Stats : null;
            if (stats != null)
            {
                if (Slot0 != null && !stats.Allows(Slot0))
                    _log?.Warn(Source, $"Slot0 ability '{Slot0.name}' is not in {stats.DisplayName}'s ability pool.");
                if (Slot1 != null && !stats.Allows(Slot1))
                    _log?.Warn(Source, $"Slot1 ability '{Slot1.name}' is not in {stats.DisplayName}'s ability pool.");
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_combat != null) _combat.OnDeath -= HandleOwnerDeath;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !_initialized) return;

            // Decoys (Doppelganger) share input authority with the caster — silence
            // ability input and timer ticking on them entirely.
            if (_controller != null && _controller.IsDecoy) return;

            // Auto-deactivate when the duration timer expires.
            if (ActiveSlot != InvalidSlot && ActivationTimer.Expired(Runner))
            {
                Deactivate();
            }

            // Lobby / intro / end lockout — no ability activation outside the
            // playable phase. Cooldowns are TickTimer-based so they still
            // advance regardless of this gate; the gate just prevents NEW casts.
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return;

            // Stunned chickens can't activate — cooldown still ticks regardless.
            if (_combat != null && _combat.IsStunned) return;

            if (!GetInput<PlayerNetworkInput>(out var input)) return;

            // No double-cast: ignore presses while another ability is active.
            if (ActiveSlot != InvalidSlot) return;

            if (input.Buttons.IsSet((int)InputButton.Ability1)) TryActivate(0);
            else if (input.Buttons.IsSet((int)InputButton.Ability2)) TryActivate(1);
        }

        // ---- Public read-only helpers (used by the HUD / debug overlays) -------

        /// <summary>
        /// Cooldown remaining in seconds for the given slot, or 0 if ready.
        /// Slot index out of range returns 0.
        /// </summary>
        public float CooldownRemaining(int slot)
        {
            var cd = GetCooldown(slot);
            if (cd.ExpiredOrNotRunning(Runner)) return 0f;
            return cd.RemainingTime(Runner) ?? 0f;
        }

        /// <summary>
        /// Cooldown progress in [0,1] — 0 just activated, 1 fully ready. Useful for
        /// a radial fill overlay.
        /// </summary>
        public float CooldownProgress01(int slot)
        {
            var ability = GetSlot(slot);
            if (ability == null || ability.Cooldown <= 0f) return 1f;
            var remaining = CooldownRemaining(slot);
            return 1f - Mathf.Clamp01(remaining / ability.Cooldown);
        }

        public bool IsReady(int slot) => GetSlot(slot) != null && CooldownRemaining(slot) <= 0f;

        /// <summary>
        /// Assigns ability assets before <see cref="Spawned"/> runs.
        /// Called by <see cref="MatchBootstrapper"/> inside the <c>onBeforeSpawned</c>
        /// callback so every peer already has the chosen abilities on first <c>Spawned</c>
        /// read. Null arguments leave the existing (prefab-default) value unchanged.
        /// </summary>
        public void SetSlots(AbilityBaseSO slot0, AbilityBaseSO slot1)
        {
            if (slot0 != null) _slot0 = slot0;
            if (slot1 != null) _slot1 = slot1;
        }

        // ---- Internals ---------------------------------------------------------

        private AbilityBaseSO GetSlot(int slot) => slot switch
        {
            0 => _slot0,
            1 => _slot1,
            _ => null,
        };

        private TickTimer GetCooldown(int slot) => slot switch
        {
            0 => Cooldown0,
            1 => Cooldown1,
            _ => default,
        };

        private void SetCooldown(int slot, TickTimer timer)
        {
            if (slot == 0) Cooldown0 = timer;
            else if (slot == 1) Cooldown1 = timer;
        }

        private void TryActivate(int slot)
        {
            var ability = GetSlot(slot);
            if (ability == null) return;
            if (!GetCooldown(slot).ExpiredOrNotRunning(Runner)) return;

            ActiveSlot = slot;
            ActivationTimer = TickTimer.CreateFromSeconds(Runner, ability.Duration);
            SetCooldown(slot, TickTimer.CreateFromSeconds(Runner, ability.Cooldown));
            ability.OnActivate(_ctx);
            _audio?.PlaySFX(_audioReg != null ? _audioReg.AbilityActivate : null);
            _log?.Info(Source, $"Activated slot {slot} ({ability.DisplayName}) for {ability.Duration:0.00}s, CD {ability.Cooldown:0.00}s.");
        }

        private void Deactivate()
        {
            var ability = ActiveAbility;
            if (ability != null)
            {
                ability.OnDeactivate(_ctx);
                _audio?.PlaySFX(_audioReg != null ? _audioReg.AbilityExpire : null);
                _log?.Debug(Source, $"Deactivated {ability.DisplayName}.");
            }
            ActiveSlot = InvalidSlot;
        }

        private void HandleOwnerDeath()
        {
            // OnDeath fires on every peer; only the authority owns ability state.
            if (!HasStateAuthority) return;
            if (ActiveSlot != InvalidSlot) Deactivate();
        }
    }
}
