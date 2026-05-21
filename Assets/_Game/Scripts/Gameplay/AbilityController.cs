using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked ability slot manager on the chicken. Holds up to three equipped
    /// <see cref="AbilityBaseSO"/>s (slots 0 and 1 used by every class; slot 2
    /// only available to chickens with the <see cref="ChickenPassive.Combo"/> passive
    /// — i.e., Assassin), drives activation from the Fusion input buffer, and owns
    /// the per-slot cooldown timers.
    /// </summary>
    /// <remarks>
    /// Exactly one ability can be active at a time — pressing another slot is ignored
    /// if one is mid-duration. Ability gameplay effects mutate
    /// <see cref="ChickenController"/> state (move multiplier, movement lock,
    /// damage immunity) which lives only on the StateAuthority; remote peers
    /// observe the resulting <c>[Networked]</c> state (position, HP) instead of
    /// re-running ability logic. Cooldown progress is drawn from the
    /// <c>ActiveSlot</c> + <c>Cooldown0/1/2</c> networked properties so every peer
    /// (including the local HUD) sees an accurate radial fill.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AbilityController : NetworkBehaviour
    {
        private const string Source = "Ability";
        public const int InvalidSlot = -1;

        [Tooltip("Equipped ability for slot 0. Used by every class.")]
        [SerializeField] private AbilityBaseSO _slot0;

        [Tooltip("Equipped ability for slot 1. Used by every class.")]
        [SerializeField] private AbilityBaseSO _slot1;

        [Tooltip("Equipped ability for slot 2. Assassin (Combo passive) only; leave null for other classes.")]
        [SerializeField] private AbilityBaseSO _slot2;

        [Networked] public int ActiveSlot { get; set; }
        [Networked] private TickTimer ActivationTimer { get; set; }
        [Networked] private TickTimer Cooldown0 { get; set; }
        [Networked] private TickTimer Cooldown1 { get; set; }
        [Networked] private TickTimer Cooldown2 { get; set; }

        public AbilityBaseSO Slot0 => _slot0;
        public AbilityBaseSO Slot1 => _slot1;
        public AbilityBaseSO Slot2 => _slot2;

        public AbilityBaseSO ActiveAbility => GetSlot(ActiveSlot);

        private ChickenController _controller;
        private AbilityContext _ctx;
        private ChickenCombat _combat;
        private ChickenAnimator _animator;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private PrefabRegistrySO _prefabRegistry;
        private bool _initialized;

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg, PrefabRegistrySO prefabRegistry)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
            _prefabRegistry = prefabRegistry;
        }

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();
            _animator = GetComponent<ChickenAnimator>();
            _ctx = new AbilityContext(_controller);

            if (HasStateAuthority)
            {
                ActiveSlot = InvalidSlot;
            }

            if (_combat != null) _combat.OnDeath += HandleOwnerDeath;

            _initialized = true;
            _log?.Debug(Source, $"Spawned. Slot0={(Slot0 != null ? Slot0.name : "(none)")}, " +
                $"Slot1={(Slot1 != null ? Slot1.name : "(none)")}, " +
                $"Slot2={(Slot2 != null ? Slot2.name : "(none)")}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_combat != null) _combat.OnDeath -= HandleOwnerDeath;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !_initialized) return;

            if (_controller != null && _controller.IsDecoy) return;

            // Auto-deactivate when the duration timer expires.
            if (ActiveSlot != InvalidSlot && ActivationTimer.Expired(Runner))
            {
                Deactivate();
            }

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return;

            if (_combat != null && _combat.IsStunned) return;

            if (!GetInput<PlayerNetworkInput>(out var input)) return;

            // No double-cast: ignore presses while another ability is active.
            if (ActiveSlot != InvalidSlot)
            {
                if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose) &&
                    (input.Buttons.IsSet((int)InputButton.Ability1) ||
                     input.Buttons.IsSet((int)InputButton.Ability2) ||
                     input.Buttons.IsSet((int)InputButton.Ability3)))
                    _log.Verbose(Source, $"Ability press ignored — slot {ActiveSlot} already active.");
                return;
            }

            if (input.Buttons.IsSet((int)InputButton.Ability1))       TryActivate(0);
            else if (input.Buttons.IsSet((int)InputButton.Ability2))  TryActivate(1);
            else if (input.Buttons.IsSet((int)InputButton.Ability3))  TryActivate(2);
        }

        // ---- Public read-only helpers (used by the HUD / debug overlays) -------

        public float CooldownRemaining(int slot)
        {
            var cd = GetCooldown(slot);
            if (cd.ExpiredOrNotRunning(Runner)) return 0f;
            return cd.RemainingTime(Runner) ?? 0f;
        }

        public float CooldownProgress01(int slot)
        {
            var ability = GetSlot(slot);
            if (ability == null || ability.Cooldown <= 0f) return 1f;
            var remaining = CooldownRemaining(slot);
            return 1f - Mathf.Clamp01(remaining / ability.Cooldown);
        }

        public bool IsReady(int slot) => GetSlot(slot) != null && CooldownRemaining(slot) <= 0f;

        /// <summary>
        /// Number of ability slots available to this chicken.
        /// Assassins (Combo passive) get 3; all other classes get 2.
        /// </summary>
        public int EquippedSlotCount
        {
            get
            {
                var stats = _controller != null ? _controller.Stats : null;
                return (stats != null && stats.Passive == ChickenPassive.Combo) ? 3 : 2;
            }
        }

        /// <summary>
        /// Bot-only activation API. Mirrors the exact gates from the player input
        /// path so cooldown / stun / double-cast rules are always honoured.
        /// Returns <c>true</c> if the ability fired this call.
        /// Must be called from the StateAuthority (bot FSM already guards this).
        /// </summary>
        public bool BotTryActivate(int slot)
        {
            if (!HasStateAuthority) return false;
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return false;
            if (_combat != null && _combat.IsStunned) return false;
            if (ActiveSlot != InvalidSlot) return false;
            if (!IsReady(slot)) return false;
            TryActivate(slot);
            return ActiveSlot == slot;
        }

        /// <summary>
        /// Scans slots 0..<see cref="EquippedSlotCount"/>-1 for the first
        /// non-null, ready ability whose resolved <see cref="BotRole"/> matches
        /// <paramref name="role"/>. Returns <c>true</c> and writes the slot index
        /// to <paramref name="slot"/> when found.
        /// </summary>
        public bool TryGetReadySlotForRole(BotRole role, out int slot)
        {
            int count = EquippedSlotCount;
            for (int i = 0; i < count; i++)
            {
                var ability = GetSlot(i);
                if (ability == null || !IsReady(i)) continue;
                if (ability.ResolveBotRole() == role) { slot = i; return true; }
            }
            slot = InvalidSlot;
            return false;
        }

        /// <summary>
        /// Assigns ability assets before <see cref="Spawned"/> runs.
        /// Called by <see cref="MatchBootstrapper"/> inside the <c>onBeforeSpawned</c>
        /// callback so every peer already has the chosen abilities on first <c>Spawned</c>
        /// read. Null arguments leave the existing (prefab-default) value unchanged.
        /// </summary>
        public void SetSlots(AbilityBaseSO slot0, AbilityBaseSO slot1, AbilityBaseSO slot2 = null)
        {
            if (slot0 != null) _slot0 = slot0;
            if (slot1 != null) _slot1 = slot1;
            if (slot2 != null) _slot2 = slot2;
        }

        // ---- Internals ---------------------------------------------------------

        private AbilityBaseSO GetSlot(int slot) => slot switch
        {
            0 => _slot0,
            1 => _slot1,
            2 => _slot2,
            _ => null,
        };

        private TickTimer GetCooldown(int slot) => slot switch
        {
            0 => Cooldown0,
            1 => Cooldown1,
            2 => Cooldown2,
            _ => default,
        };

        private void SetCooldown(int slot, TickTimer timer)
        {
            if (slot == 0)      Cooldown0 = timer;
            else if (slot == 1) Cooldown1 = timer;
            else if (slot == 2) Cooldown2 = timer;
        }

        private void TryActivate(int slot)
        {
            // Slot 2 requires the Combo (Assassin) passive.
            if (slot == 2)
            {
                var stats = _controller != null ? _controller.Stats : null;
                if (stats == null || stats.Passive != ChickenPassive.Combo)
                {
                    _log?.Debug(Source, "TryActivate slot 2: only available to Assassin (Combo passive).");
                    return;
                }
            }

            var ability = GetSlot(slot);
            if (ability == null)
            {
                _log?.Debug(Source, $"TryActivate slot {slot}: no ability assigned.");
                return;
            }
            if (!GetCooldown(slot).ExpiredOrNotRunning(Runner))
            {
                _log?.Debug(Source, $"TryActivate slot {slot} ({ability.DisplayName}): " +
                    $"on cooldown ({CooldownRemaining(slot):0.0}s remaining).");
                return;
            }

            ActiveSlot = slot;
            ActivationTimer = TickTimer.CreateFromSeconds(Runner, ability.Duration);
            SetCooldown(slot, TickTimer.CreateFromSeconds(Runner, ability.Cooldown));
            // Refresh context fields that abilities need for NetworkObject spawning.
            _ctx.Runner         = Runner;
            _ctx.PrefabRegistry = _prefabRegistry;
            ability.OnActivate(_ctx);
            _animator?.TriggerAbilityCast();
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
            if (!HasStateAuthority) return;
            if (ActiveSlot != InvalidSlot) Deactivate();
        }
    }
}
