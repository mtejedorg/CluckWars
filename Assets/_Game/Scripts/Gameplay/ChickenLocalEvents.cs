using System;
using CluckWars.Abilities;
using Fusion;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>A cast, as every peer sees it. Ids and numbers only: text and clips are the listener's to resolve.</summary>
    public readonly struct CastEvent
    {
        /// <summary>The slot that cast (<c>AbilityController.LastCastSlot</c>).</summary>
        public readonly int Slot;
        /// <summary>The ability in that slot, or null when this peer has not resolved the caster's loadout yet.</summary>
        public readonly AbilityBaseSO Ability;
        /// <summary><see cref="Ability"/>, else the last ability this peer saw the caster charge (the cast clears the
        /// charge in the tick it fires, and a very short ability may already be over). Null when neither is known.</summary>
        public readonly AbilityBaseSO AimedAbility;
        /// <summary>Targets the cast hit (<c>AbilityController.LastCastHitCount</c>); 0 is a whiff only where the
        /// ability reports hits at all (<c>AbilityBaseSO.ReportsCastHits</c>).</summary>
        public readonly byte HitCount;
        /// <summary>The body action of <see cref="AimedAbility"/>; <see cref="CastArchetype.None"/> when unknown.</summary>
        public readonly CastArchetype Archetype;
        /// <summary>The cast was the Peck, which has its own animation beat instead of an archetype.</summary>
        public readonly bool IsPeck;

        public CastEvent(int slot, AbilityBaseSO ability, AbilityBaseSO aimedAbility, byte hitCount)
        {
            Slot = slot;
            Ability = ability;
            AimedAbility = aimedAbility;
            HitCount = hitCount;
            Archetype = aimedAbility != null ? aimedAbility.CastMotion : CastArchetype.None;
            IsPeck = aimedAbility is PeckAbilitySO;
        }
    }

    /// <summary>A robbery of this chicken: who did it (<c>ChickenCargo.LastThiefId</c>).</summary>
    public readonly struct StealEvent
    {
        public readonly NetworkBehaviourId Thief;
        public StealEvent(NetworkBehaviourId thief) { Thief = thief; }
    }

    /// <summary>A meaningful change of the food this chicken carries, as <c>ChickenCargo.Render</c> saw it.</summary>
    public readonly struct CargoChangeEvent
    {
        /// <summary>Positive: gained. Negative: lost.</summary>
        public readonly float Delta;
        /// <summary>The frame this peer last credited this chicken a steal (-1: none). A gain observed in that
        /// frame or the next one is that credit, which its listener may present later than an ordinary pickup.</summary>
        public readonly int StealCreditFrame;

        public CargoChangeEvent(float delta, int stealCreditFrame)
        {
            Delta = delta;
            StealCreditFrame = stealCreditFrame;
        }
    }

    /// <summary>
    /// What <see cref="ChickenLocalEvents"/> reads in one observation: the replicated one-shot counters of a chicken
    /// and the fields written beside them. A plain value, so the observer runs in a test with no runner.
    /// </summary>
    public struct ChickenEventSnapshot
    {
        public bool HasAbilities;
        public byte CastEventId;
        public int CastSlot;
        public AbilityBaseSO CastAbility;
        public AbilityBaseSO ChargingAbility;
        public byte CastHitCount;

        public byte KnockbackEventId;
        public byte SelfImpulseEventId;
        public byte JumpEventId;

        public bool HasCargo;
        public byte StealEventId;
        public NetworkBehaviourId Thief;
        public byte DepositEventId;
    }

    /// <summary>
    /// The ONE local observer of a chicken's replicated one-shot moments (architecture audit 2026-11-12, A12 / D2a).
    /// On every peer it watches the <c>[Networked] byte XEventId</c> counters the state authority bumps, and raises
    /// plain C# events. Presentation (visuals, animation, audio, HUD) subscribes; none keeps a cursor of its own.
    /// </summary>
    /// <remarks>
    /// <b>Gameplay side, on purpose.</b> It references no Visuals, UI or Audio type, so Visuals depends on it and
    /// never the other way round. Reach it through <see cref="ChickenController.LocalEvents"/>.
    /// <para>
    /// <b>Payloads are ids, enums and numbers.</b> Never a localized string, never an <c>AudioClip</c>: a listener
    /// resolves text through <c>UiText</c> / <c>UiKeys</c> and sound through the audio registry, so a language pack
    /// swaps both without touching gameplay.
    /// </para>
    /// <para>
    /// <b>When it observes.</b> <see cref="ChickenController"/> calls <see cref="Observe()"/> once a frame from
    /// <c>Render</c>. A listener that must act at its own point in the frame (a <c>LateUpdate</c> that runs after
    /// other work, a HUD <c>Update</c>) latches the event in its handler, calls <see cref="Observe()"/> at that point
    /// and then consumes its latch: whichever call sees the counter move first raises the event, so the listener
    /// acts in the same frame, at the same point, as when it polled the counter itself. Observing is a few byte
    /// compares and allocates nothing.
    /// </para>
    /// <para>
    /// <b>Local moments too.</b> A moment that gameplay notices in <c>Render</c> from continuous replicated state
    /// (a cargo delta, a zone catching someone, the class model being attached) is raised here by the behaviour
    /// that owns that state (the <c>Raise...</c> methods), so gameplay never calls a presentation type.
    /// </para>
    /// <para>
    /// <b>Baselines.</b> One <see cref="EventEdge"/> per counter (its remarks hold the rules: seed first, fire once
    /// per change, wrap is a change, several bumps fire once). <see cref="Reset"/> on spawn and despawn re-seeds.
    /// </para>
    /// </remarks>
    public sealed class ChickenLocalEvents
    {
        private readonly ChickenController _controller;
        private AbilityController _abilities;
        private ChickenCargo _cargo;
        private bool _siblingsResolved;

        private EventEdge _cast, _knockback, _selfImpulse, _jump, _steal, _deposit;
        private AbilityBaseSO _lastCharging;
        private bool _raising;

        /// <summary><c>AbilityController.LastCastEventId</c> moved: this chicken cast.</summary>
        public event Action<CastEvent> Cast;
        /// <summary><c>ChickenController.KnockbackEventId</c> moved: somebody knocked this chicken back.</summary>
        public event Action Knockback;
        /// <summary><c>ChickenController.SelfImpulseEventId</c> moved: this chicken displaced itself (a dodge, not a hit).</summary>
        public event Action SelfImpulse;
        /// <summary><c>ChickenController.JumpEventId</c> moved: a teleport jump moved this chicken.</summary>
        public event Action Jump;
        /// <summary><c>ChickenCargo.StealEventId</c> moved: this chicken was robbed.</summary>
        public event Action<StealEvent> Stolen;
        /// <summary><c>ChickenCargo.DepositEventId</c> moved: this chicken finished banking its whole load.</summary>
        public event Action Deposited;

        // ---- Local moments, raised from Render by the gameplay behaviour that owns the state ----

        /// <summary>The carried food changed by a meaningful amount (not the trickle of collecting or depositing).</summary>
        public event Action<CargoChangeEvent> CargoChanged;
        /// <summary>Something that just hit this chicken came from this world position (a zone's centre).</summary>
        public event Action<Vector3> AttackerNamed;
        /// <summary>A zone this chicken placed just caught someone: the caster's confirmation.</summary>
        public event Action ZoneTriggered;
        /// <summary>The per-class model was parented under this chicken (its renderers exist from now on).</summary>
        public event Action ClassModelAttached;

        /// <summary>From <c>ChickenCargo.Render</c>, every peer.</summary>
        public void RaiseCargoChanged(float delta, int stealCreditFrame) =>
            CargoChanged?.Invoke(new CargoChangeEvent(delta, stealCreditFrame));

        /// <summary>From <c>AbilityZone.Render</c>, every peer. False when nothing listens.</summary>
        public bool RaiseAttackerNamed(Vector3 attackerWorldPosition)
        {
            var listeners = AttackerNamed;
            if (listeners == null) return false;
            listeners(attackerWorldPosition);
            return true;
        }

        /// <summary>From <c>AbilityZone.Render</c>, every peer. False when nothing listens (a prefab fault the caller reports).</summary>
        public bool RaiseZoneTriggered()
        {
            var listeners = ZoneTriggered;
            if (listeners == null) return false;
            listeners();
            return true;
        }

        /// <summary>From <c>ChickenController.Spawned</c>, once the class model is attached.</summary>
        public void RaiseClassModelAttached() => ClassModelAttached?.Invoke();

        /// <param name="controller">The chicken observed; null only in tests, which feed snapshots.</param>
        public ChickenLocalEvents(ChickenController controller = null) { _controller = controller; }

        /// <summary>Reads this chicken's replicated counters and raises an event for each one that moved. Safe to
        /// call from any presentation code, any number of times a frame; a no-op until the object is live.</summary>
        public void Observe()
        {
            if (_controller == null) return;
            var obj = _controller.Object;
            if (obj == null || !obj.IsValid) return;

            if (!_siblingsResolved)
            {
                _siblingsResolved = true;
                _abilities = _controller.GetComponent<AbilityController>();
                _cargo = _controller.GetComponent<ChickenCargo>();   // a decoy has none
            }

            var s = new ChickenEventSnapshot
            {
                KnockbackEventId = _controller.KnockbackEventId,
                SelfImpulseEventId = _controller.SelfImpulseEventId,
                JumpEventId = _controller.JumpEventId,
            };
            if (_abilities != null)
            {
                s.HasAbilities = true;
                s.CastEventId = _abilities.LastCastEventId;
                s.ChargingAbility = _abilities.ChargingAbility;
                if (_cast.WouldFire(s.CastEventId))
                {
                    s.CastSlot = _abilities.LastCastSlot;
                    s.CastAbility = _abilities.LastCastAbility;
                    s.CastHitCount = _abilities.LastCastHitCount;
                }
            }
            if (_cargo != null)
            {
                s.HasCargo = true;
                s.StealEventId = _cargo.StealEventId;
                if (_steal.WouldFire(s.StealEventId)) s.Thief = _cargo.LastThiefId;
                s.DepositEventId = _cargo.DepositEventId;
            }
            Observe(in s);
        }

        /// <summary>The observation itself, over a plain snapshot (what <see cref="Observe()"/> builds from the live object).</summary>
        public void Observe(in ChickenEventSnapshot s)
        {
            // A listener that asks for an observation from inside a callback gets the pass already running.
            if (_raising) return;
            _raising = true;
            try
            {
                if (s.HasAbilities)
                {
                    if (s.ChargingAbility != null) _lastCharging = s.ChargingAbility;
                    if (_cast.Observe(s.CastEventId))
                        Cast?.Invoke(new CastEvent(s.CastSlot, s.CastAbility, s.CastAbility != null ? s.CastAbility : _lastCharging, s.CastHitCount));
                }

                if (_knockback.Observe(s.KnockbackEventId)) Knockback?.Invoke();
                if (_selfImpulse.Observe(s.SelfImpulseEventId)) SelfImpulse?.Invoke();
                if (_jump.Observe(s.JumpEventId)) Jump?.Invoke();

                if (s.HasCargo)
                {
                    if (_steal.Observe(s.StealEventId)) Stolen?.Invoke(new StealEvent(s.Thief));
                    if (_deposit.Observe(s.DepositEventId)) Deposited?.Invoke();
                }
            }
            finally
            {
                _raising = false;
            }
        }

        /// <summary>Forgets every baseline (spawn, despawn, pool reuse): the next observation seeds and fires nothing.
        /// Subscriptions are the listeners' to pair; they are not touched.</summary>
        public void Reset()
        {
            _cast.Reset();
            _knockback.Reset();
            _selfImpulse.Reset();
            _jump.Reset();
            _steal.Reset();
            _deposit.Reset();
            _lastCharging = null;
            _siblingsResolved = false;
            _abilities = null;
            _cargo = null;
        }
    }
}
