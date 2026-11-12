using System;
using CluckWars.Abilities;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Gameplay
{
    /// <summary>A cast, as every peer sees it. Ids and numbers only: text, clips and the ability asset itself are
    /// the listener's to resolve.</summary>
    public readonly struct CastEvent
    {
        /// <summary>The slot that cast (<c>AbilityController.LastCastSlot</c>).</summary>
        public readonly int Slot;
        /// <summary>The registry code (<c>AbilityRegistrySO.CodeOf</c>) of the ability in that slot;
        /// <c>AbilityRegistrySO.NoAbilityCode</c> when this peer has not resolved the caster's loadout yet.</summary>
        public readonly byte AbilityCode;
        /// <summary><see cref="AbilityCode"/>, else the code of the last ability this peer saw the caster charge (the
        /// cast clears the charge in the tick it fires, and a very short ability may already be over).
        /// <c>NoAbilityCode</c> when neither is known. A listener that needs the asset resolves it through the
        /// registry: <see cref="ChickenLocalEvents.AbilityOf"/>.</summary>
        public readonly byte AimedAbilityCode;
        /// <summary>Targets the cast hit (<c>AbilityController.LastCastHitCount</c>); 0 is a whiff only where the
        /// ability reports hits at all (<c>AbilityBaseSO.ReportsCastHits</c>).</summary>
        public readonly byte HitCount;
        /// <summary>The body action of the aimed ability; <see cref="CastArchetype.None"/> when unknown.</summary>
        public readonly CastArchetype Archetype;
        /// <summary>The cast was the Peck, which has its own animation beat instead of an archetype.</summary>
        public readonly bool IsPeck;

        public CastEvent(int slot, byte abilityCode, byte aimedAbilityCode, byte hitCount, CastArchetype archetype, bool isPeck)
        {
            Slot = slot;
            AbilityCode = abilityCode;
            AimedAbilityCode = aimedAbilityCode;
            HitCount = hitCount;
            Archetype = archetype;
            IsPeck = isPeck;
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

        // One list per event instead of a multicast delegate: every subscriber is called on its own, so one that
        // throws is reported and the rest still get the beat (S-1). A raise allocates nothing.
        private readonly ListenerList<Action<CastEvent>> _castListeners = new ListenerList<Action<CastEvent>>();
        private readonly ListenerList<Action> _knockbackListeners = new ListenerList<Action>();
        private readonly ListenerList<Action> _selfImpulseListeners = new ListenerList<Action>();
        private readonly ListenerList<Action> _jumpListeners = new ListenerList<Action>();
        private readonly ListenerList<Action<StealEvent>> _stolenListeners = new ListenerList<Action<StealEvent>>();
        private readonly ListenerList<Action> _depositedListeners = new ListenerList<Action>();
        private readonly ListenerList<Action<CargoChangeEvent>> _cargoChangedListeners = new ListenerList<Action<CargoChangeEvent>>();
        private readonly ListenerList<Action<Vector3>> _attackerNamedListeners = new ListenerList<Action<Vector3>>();
        private readonly ListenerList<Action> _zoneTriggeredListeners = new ListenerList<Action>();
        private readonly ListenerList<Action> _classModelAttachedListeners = new ListenerList<Action>();
        private readonly ListenerList<Action<AbilityCue>> _abilityCueListeners = new ListenerList<Action<AbilityCue>>();

        /// <summary>The ability registry every peer ships (the chicken sets it once injected): what turns the
        /// ability of a cast into the code its event carries, and a code back into the asset for a listener.</summary>
        public AbilityRegistrySO Registry { get; set; }

        /// <summary>The asset a <see cref="CastEvent"/> code names; null for "no ability", an unknown code or no registry.</summary>
        public AbilityBaseSO AbilityOf(byte code) => Registry != null ? Registry.FromCode(code) : null;

        private byte CodeOf(AbilityBaseSO ability) => Registry != null ? Registry.CodeOf(ability) : AbilityRegistrySO.NoAbilityCode;

        /// <summary>Where a throwing listener is reported. The chicken sets it once it has its logger; until then
        /// (and in tests that set none) the report still goes out, through a plain <see cref="UnityLogService"/>
        /// (<see cref="LocalRaise"/>).</summary>
        public ILogService Log { get; set; }

        /// <summary><c>AbilityController.LastCastEventId</c> moved: this chicken cast.</summary>
        public event Action<CastEvent> Cast { add => _castListeners.Add(value); remove => _castListeners.Remove(value); }
        /// <summary><c>ChickenController.KnockbackEventId</c> moved: somebody knocked this chicken back.</summary>
        public event Action Knockback { add => _knockbackListeners.Add(value); remove => _knockbackListeners.Remove(value); }
        /// <summary><c>ChickenController.SelfImpulseEventId</c> moved: this chicken displaced itself (a dodge, not a hit).</summary>
        public event Action SelfImpulse { add => _selfImpulseListeners.Add(value); remove => _selfImpulseListeners.Remove(value); }
        /// <summary><c>ChickenController.JumpEventId</c> moved: a teleport jump moved this chicken.</summary>
        public event Action Jump { add => _jumpListeners.Add(value); remove => _jumpListeners.Remove(value); }
        /// <summary><c>ChickenCargo.StealEventId</c> moved: this chicken was robbed.</summary>
        public event Action<StealEvent> Stolen { add => _stolenListeners.Add(value); remove => _stolenListeners.Remove(value); }
        /// <summary><c>ChickenCargo.DepositEventId</c> moved: this chicken finished banking its whole load.</summary>
        public event Action Deposited { add => _depositedListeners.Add(value); remove => _depositedListeners.Remove(value); }

        // ---- Local moments, raised from Render by the gameplay behaviour that owns the state ----

        /// <summary>The carried food changed by a meaningful amount (not the trickle of collecting or depositing).</summary>
        public event Action<CargoChangeEvent> CargoChanged { add => _cargoChangedListeners.Add(value); remove => _cargoChangedListeners.Remove(value); }
        /// <summary>Something that just hit this chicken came from this world position (a zone's centre).</summary>
        public event Action<Vector3> AttackerNamed { add => _attackerNamedListeners.Add(value); remove => _attackerNamedListeners.Remove(value); }
        /// <summary>A zone this chicken placed just caught someone: the caster's confirmation.</summary>
        public event Action ZoneTriggered { add => _zoneTriggeredListeners.Add(value); remove => _zoneTriggeredListeners.Remove(value); }
        /// <summary>The per-class model was parented under this chicken (its renderers exist from now on).</summary>
        public event Action ClassModelAttached { add => _classModelAttachedListeners.Add(value); remove => _classModelAttachedListeners.Remove(value); }

        /// <summary>This chicken's own-player ability cue (cancel, fizzle, cargo full, expire): raised on the peer
        /// that simulates the chicken, never replicated. The listener decides who hears it (<c>OwnSoundRules</c>).</summary>
        public event Action<AbilityCue> AbilityCueRaised { add => _abilityCueListeners.Add(value); remove => _abilityCueListeners.Remove(value); }

        /// <summary>From <c>AbilityController.Render</c>, on the peer whose tick noticed the cue. False when nothing listens.</summary>
        public bool RaiseAbilityCue(AbilityCue cue) =>
            LocalRaise.Invoke(_abilityCueListeners, nameof(AbilityCueRaised), Log, _controller, cue);

        /// <summary>From <c>ChickenCargo.Render</c>, every peer. False when nothing listens (a prefab fault the caller reports).</summary>
        public bool RaiseCargoChanged(float delta, int stealCreditFrame) =>
            LocalRaise.Invoke(_cargoChangedListeners, nameof(CargoChanged), Log, _controller, new CargoChangeEvent(delta, stealCreditFrame));

        /// <summary>From <c>AbilityZone.Render</c>, every peer. False when nothing listens.</summary>
        public bool RaiseAttackerNamed(Vector3 attackerWorldPosition) =>
            LocalRaise.Invoke(_attackerNamedListeners, nameof(AttackerNamed), Log, _controller, attackerWorldPosition);

        /// <summary>From <c>AbilityZone.Render</c>, every peer. False when nothing listens (a prefab fault the caller reports).</summary>
        public bool RaiseZoneTriggered() => LocalRaise.Invoke(_zoneTriggeredListeners, nameof(ZoneTriggered), Log, _controller);

        /// <summary>From <c>ChickenController.Spawned</c>, once the class model is attached. False when nothing listens.</summary>
        public bool RaiseClassModelAttached() => LocalRaise.Invoke(_classModelAttachedListeners, nameof(ClassModelAttached), Log, _controller);

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
                    {
                        // The payload is ids only (S-5): the assets are read here, on the gameplay side, for their code,
                        // archetype and peck flag, and never travel with the event.
                        var aimed = s.CastAbility != null ? s.CastAbility : _lastCharging;
                        LocalRaise.Invoke(_castListeners, nameof(Cast), Log, _controller,
                            new CastEvent(s.CastSlot, CodeOf(s.CastAbility), CodeOf(aimed), s.CastHitCount,
                                aimed != null ? aimed.CastMotion : CastArchetype.None, aimed is PeckAbilitySO));
                    }
                }

                if (_knockback.Observe(s.KnockbackEventId)) LocalRaise.Invoke(_knockbackListeners, nameof(Knockback), Log, _controller);
                if (_selfImpulse.Observe(s.SelfImpulseEventId)) LocalRaise.Invoke(_selfImpulseListeners, nameof(SelfImpulse), Log, _controller);
                if (_jump.Observe(s.JumpEventId)) LocalRaise.Invoke(_jumpListeners, nameof(Jump), Log, _controller);

                if (s.HasCargo)
                {
                    if (_steal.Observe(s.StealEventId)) LocalRaise.Invoke(_stolenListeners, nameof(Stolen), Log, _controller, new StealEvent(s.Thief));
                    if (_deposit.Observe(s.DepositEventId)) LocalRaise.Invoke(_depositedListeners, nameof(Deposited), Log, _controller);
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
