using System.Collections.Generic;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Ability category as per GDD §7.2. Shown in the character-select picker to
    /// help players understand what an ability does before equipping it.
    /// </summary>
    public enum AbilityCategory : byte
    {
        Steal   = 0,
        Control = 1,
        Defense = 2,
        Utility = 3,
    }

    /// <summary>
    /// ADR 0003 Decision 3 pool classification.
    /// Common abilities sit in a shared pool; Character abilities sit in a per-class pool.
    /// </summary>
    public enum AbilitySlotKind : byte
    {
        Common    = 0,
        Character = 1,
    }

    /// <summary>
    /// Bitmask of classes allowed to equip a Character-slot ability (ADR 0003 Decision 3).
    /// Derived from existing <see cref="ChickenClass"/> ordinals (Warrior=0, Speedy=1, Fatty=2, Assassin=3).
    /// </summary>
    [System.Flags]
    public enum ChickenClassFlags : byte
    {
        None     = 0,
        Warrior  = 1 << 0, // 1
        Speedy   = 1 << 1, // 2
        Fatty    = 1 << 2, // 4
        Assassin = 1 << 3, // 8
        All      = Warrior | Speedy | Fatty | Assassin, // 15
    }

    /// <summary>
    /// How the bot AI should use this ability.
    /// <c>Auto</c> derives the role from <see cref="AbilityCategory"/>:
    /// Damage→Offense, Control→Control, Defense→Defense, Utility→Escape.
    /// Override only when Auto would be wrong (e.g. Sneaky Steal = Steal).
    /// </summary>
    public enum BotRole : byte
    {
        Auto    = 0, // derive from AbilityCategory (see AbilityBaseSO.ResolveBotRole)
        Offense = 1, // close in and deal damage / stun the rival
        Defense = 2, // turtle up / block hits
        Control = 3, // displace or pin a rival
        Escape  = 4, // dash / blur away from danger
        Steal   = 5, // snatch cargo from a rival
        Forage  = 6, // take food from a pile (Peck). Appended - BotRole is byte-serialised in .asset files.
        Bank    = 7, // accelerate the deposit at your own base (Quick Drop). Appended, same reason.
    }

    /// <summary>
    /// Base ScriptableObject for every ability. Adding a new ability = subclass
    /// this, override <see cref="OnActivate"/> + <see cref="OnDeactivate"/>, drop
    /// a concrete asset under <c>/Assets/_Game/Data/Abilities/</c>. No code
    /// changes to <c>AbilityController</c> or anything else.
    /// </summary>
    /// <remarks>
    /// Activation timing (duration / cooldown) is handled by <c>AbilityController</c>.
    /// Concrete abilities only describe what to do at the start and end of the
    /// active window. They mutate state via the <see cref="AbilityContext"/>'s
    /// <c>ChickenController</c> hooks (<c>MoveSpeedMultiplier</c>, <c>MovementLocked</c>,
    /// <c>DamageImmune</c>, …) which the StateAuthority reads each tick.
    /// </remarks>
    public abstract class AbilityBaseSO : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName;
        [Tooltip("Short label drawn on the ability button (≤ 4 chars works best).")]
        public string ShortLabel;
        [Tooltip("One-line effect text for the character-select preview and the ability pick cards. " +
                 "Say what the ability does to the player, not how it is implemented.")]
        [TextArea(2, 4)] public string Description;
        [Tooltip("Icon glyph (emoji) drawn on the hex ability button — design v3 (cluckwars-tokens-v3). " +
                 "Leave blank to fall back to the subclass DefaultIcon.")]
        public string Icon;
        [Tooltip("Accent color for VFX / UI highlight. Phase 6 uses it for the cooldown overlay tint.")]
        public Color AccentColor = new Color(0.45f, 0.7f, 1f, 1f);
        [Tooltip("GDD §7.2 category. Used by the character-select ability grid to group abilities.")]
        public AbilityCategory Category = AbilityCategory.Utility;

        [Header("ADR 0003 Classification")]
        [Tooltip("Terrain traversal capability granted to the caster while this ability is active (ADR 0003 Decision 1). Deprecated in v0.5 in favor of JumpTier.")]
        public TerrainTraversal TerrainTraversal = TerrainTraversal.None;

        [Tooltip("Jump length tier for teleport jump traversal abilities (Short=5m, Normal=10m, Big=18m; GDD v0.5 §3.5).")]
        public JumpLengthTier JumpTier = JumpLengthTier.None;

        [Tooltip("Pool classification: Common (shared) vs Character (class pool) per ADR 0003 Decision 3.")]
        public AbilitySlotKind SlotKind = AbilitySlotKind.Common;

        [Tooltip("Bitmask of classes allowed to equip this ability when SlotKind is Character. All (15) for Common.")]
        public ChickenClassFlags AllowedClasses = ChickenClassFlags.All;

        [Header("Bot")]
        [Tooltip("How the bot AI classifies and uses this ability. Auto derives the role from Category.")]
        public BotRole BotRole = BotRole.Auto;

        [Header("Timings")]
        [Tooltip("How long the active effect lasts after activation (seconds). GDD calls for 1–2s on most abilities.")]
        [Min(0.05f)] public float Duration = 1.5f;

        [Tooltip("Lockout between activations (seconds). Counted from activation, not from deactivation.")]
        [Min(0f)] public float Cooldown = 8f;

        [Header("Animation")]
        [Tooltip("The body action this cast reads as. Routed by ChickenAnimator.TriggerAbilityCast " +
                 "to the matching animator state, which an AnimatorOverrideController has filled " +
                 "with the casting class's own clip. Peck is deliberately absent - it keeps its own " +
                 "dedicated state and trigger.")]
        public CastArchetype CastMotion = CastArchetype.Lunge;

        [Header("Physics Scans")]
        [Tooltip("Legacy layer mask from the Stage G Physics.OverlapSphere scans. No longer used to find " +
                 "chicken targets — every ability's OnActivate now scans ChickenController.ActiveControllers " +
                 "via GatherTargets (see AbilityAimTests' 'no stray scans' regression lock). Kept in case a " +
                 "future ability needs a real physics query (e.g. line-of-sight); not currently read.")]
        public LayerMask SearchMask = 256; // 1 << 8 (Chickens layer)

        /// <summary>
        /// Per-subclass default icon glyph (design v3, cluckwars-tokens-v3).
        /// Used by <see cref="ResolveIcon"/> when the serialized <see cref="Icon"/>
        /// field is left blank, so existing assets pick up the correct glyph
        /// without re-authoring. Override in each concrete ability.
        /// </summary>
        protected virtual string DefaultIcon => "✦";

        /// <summary>
        /// The icon glyph to draw on the ability button: the authored
        /// <see cref="Icon"/> if set, otherwise the subclass <see cref="DefaultIcon"/>.
        /// </summary>
        public string ResolveIcon() => string.IsNullOrEmpty(Icon) ? DefaultIcon : Icon;

        /// <summary>
        /// Returns the effective <see cref="BotRole"/> for this ability.
        /// When <see cref="BotRole"/> is <c>Auto</c>, derives from
        /// <see cref="Category"/>: Damage→Offense, Control→Control,
        /// Defense→Defense, Utility→Escape.
        /// </summary>
        public BotRole ResolveBotRole() => BotRole switch
        {
            BotRole.Auto => Category switch
            {
                AbilityCategory.Steal   => BotRole.Steal,
                AbilityCategory.Defense => BotRole.Defense,
                AbilityCategory.Control => BotRole.Control,
                _                       => BotRole.Escape, // Utility → Escape
            },
            _ => BotRole,
        };

        // ---- Targeting / usability feedback (HUD range rings + grey-out) ------

        /// <summary>
        /// World-space range this ability cares about, surfaced for UI feedback
        /// (ground range ring, usability grey-out). 0 = no meaningful range
        /// (self-buffs, placed zones). Subclasses with a tuned range field
        /// override this to expose it — no asset re-authoring needed.
        /// </summary>
        public virtual float IndicatorRange => 0f;

        /// <summary>
        /// True when activating without an enemy inside <see cref="IndicatorRange"/>
        /// would do nothing (Snatch, Sneaky Steal, Cluck Shock, Mark/Kill, the stun
        /// bursts). The HUD greys the button out and <c>AbilityController.TryActivate</c>
        /// refuses the cast so the cooldown isn't burned on a guaranteed whiff.
        ///
        /// <b>False for every ability whose cast is worth making on its own.</b> That is
        /// the whole mobility family — a gap-closer moves the caster whether or not anyone
        /// is standing in the lane, so gating it on a target turns a traversal tool into a
        /// button that does nothing when you most want it (Maestro, playtest item 5,
        /// 2026-08-14). It is also false for Peck, which resolves against a
        /// <c>FoodPile</c> and not a chicken at all, and overrides <see cref="IsUsable"/>
        /// with its own pile-and-cargo-space test instead.
        ///
        /// The two buckets are pinned in <c>AbilityAimTests</c> — see
        /// <c>MobilityAbilities_FireWithNoTargetInRange</c> and
        /// <c>TargetGatedAbilities_AreExactlyTheOnesWithNothingToDoWithoutOne</c>.
        /// </summary>
        public virtual bool RequiresEnemyInRange => false;

        /// <summary>
        /// Can this ability do something right now (cooldown aside)? Default: yes.
        /// Range-gated abilities override <see cref="RequiresEnemyInRange"/>, which
        /// routes this through <see cref="HasAnyTarget"/> — the same predicate the
        /// preview and <see cref="OnActivate"/> use, so the grey-out can never
        /// disagree with what actually fires. Mark Kill is the one ability that still
        /// overrides <see cref="IsUsable"/> itself, for its post-mark "kill ready"
        /// state, which has nothing to do with the aim shape.
        /// </summary>
        public virtual bool IsUsable(Gameplay.ChickenController caster)
        {
            if (!RequiresEnemyInRange) return true;
            return HasAnyTarget(caster);
        }

        /// <summary>
        /// This ability's cooldown for a specific caster. Defaults to the authored
        /// <see cref="Cooldown"/>; Peck overrides it because foraging cadence is a
        /// per-class stat solved against the SCT axiom, not an ability-wide constant.
        /// </summary>
        /// <remarks>
        /// Every cooldown read goes through here — <c>AbilityController.TryActivate</c> when
        /// it starts the timer AND <c>CooldownProgress01</c> when it draws the radial fill.
        /// Reading the raw field in one place and this in the other would draw a hex whose
        /// sweep finishes at a different moment than the ability actually becomes ready.
        /// </remarks>
        public virtual float ResolveCooldown(Gameplay.ChickenController caster) => Cooldown;

        /// <summary>
        /// How much cargo <paramref name="caster"/> actually takes for a nominal
        /// <paramref name="baseAmount"/>, after its class specialization has had a say
        /// (Bully, Thief). The single chokepoint every stealing ability calls, so a passive
        /// that scales steals cannot apply to some of them and miss others.
        /// </summary>
        protected static float ResolveStealAmount(float baseAmount, Gameplay.ChickenController caster)
        {
            var passive = caster != null ? caster.Passive : null;
            return passive != null ? passive.ModifyStealAmount(baseAmount, caster) : baseAmount;
        }

        /// <summary>
        /// Cargo this ability takes off a rival per hit, before <see cref="ResolveStealAmount"/>
        /// applies the caster's passive. 0 for every ability that does not steal.
        /// </summary>
        /// <remarks>
        /// <b>Declared, not inferred from <see cref="AbilityCategory.Steal"/>.</b> Category is a
        /// picker label (GDD §7.2) that a designer can retag without touching behaviour, and the
        /// thing that actually matters here is "does this call
        /// <c>ChickenCargo.RPC_DrainStolen</c>" — which only the ability itself knows.
        /// <para>
        /// The receiver-side bound on <c>RPC_DrainStolen</c> is the maximum of this across the
        /// shipped <c>AbilityRegistrySO</c> (see <c>StealRules.MaxSingleSteal</c>), so a new
        /// steal ability that forgets to override it contributes nothing to the bound and gets
        /// its steals rejected. <c>StealRulesTests</c> pins every ability whose source calls the
        /// RPC against this override so the omission turns the suite red instead of shipping.
        /// </para>
        /// </remarks>
        public virtual float NominalStealAmount => 0f;

        // ---- Aim descriptor (FEEDBACK.md §4 / §8) ------------------------------
        // One declarative shape per ability, used by every consumer that needs to
        // agree on "what does this ability cover": the hold-to-aim preview, the
        // target threat-overlay, the usability gate above, and OnActivate itself via
        // GatherTargets/HasAnyTarget below. Defaults are chosen so an ability that
        // doesn't override anything is inert (AimShape.None) rather than silently
        // wrong — see AbilityAimTests' completeness check.

        /// <summary>Landing-ring radius drawn for <see cref="AbilityAimShape.Jump"/> abilities. Not a per-ability tunable — the ring is cosmetic, the jump length is what <see cref="AimForwardOffset"/> describes.</summary>
        public const float JumpLandingRadius = 1.0f;

        /// <summary>
        /// The shape this ability's area occupies. Default is <c>None</c> (no area,
        /// self-buff) so every ability that actually scans for a target must opt in
        /// explicitly — see the per-ability table in FEEDBACK.md §4. Deliberately
        /// NOT derived from <see cref="JumpTier"/>: Doppelganger sets JumpTier=Big
        /// for its decoy-spawn teleport but is a pure self-buff with no target area
        /// (AffectsSelf below), so an implicit "JumpTier != None ⇒ Jump" default
        /// would have silently mis-classified it as an offensive ability.
        /// </summary>
        public virtual AbilityAimShape AimShape => AbilityAimShape.None;

        /// <summary>Radius of the circle/cone/landing ring. Falls back to <see cref="IndicatorRange"/> so most abilities need only one override.</summary>
        public virtual float AimRadius => IndicatorRange;

        /// <summary>
        /// How far along the caster's forward the shape's <i>defining point</i> sits, in
        /// metres. Read two ways depending on <see cref="AimShape"/>, and both are
        /// deliberate — one serialized field serves both rather than adding a second one to
        /// every SO plus an asset migration:
        /// <list type="bullet">
        ///   <item><see cref="AbilityAimShape.ForwardCircle"/> / <see cref="AbilityAimShape.Jump"/>:
        ///   the <b>centre</b> of the circle. The shape is detached from the caster once the
        ///   offset exceeds the radius.</item>
        ///   <item><see cref="AbilityAimShape.Capsule"/>: the <b>far end of the axis</b>. The
        ///   shape always touches the caster, because the near cap is centred on them.</item>
        /// </list>
        /// Under both readings total forward reach is <c>AimForwardOffset + AimRadius</c>.
        /// Ignored by the caster-centred shapes.
        /// </summary>
        public virtual float AimForwardOffset => 0f;

        /// <summary>Full arc in degrees for <see cref="AbilityAimShape.Cone"/>. 360 degenerates to a full circle.</summary>
        public virtual float AimConeAngle => 360f;

        /// <summary>
        /// Must this ability's aim shape be resolved from the caster's pose <i>before</i>
        /// their teleport jump rather than after it?
        ///
        /// <c>AbilityController.TryActivate</c> normally jumps first and resolves after,
        /// which is right for a shape anchored on the caster: an ability that blinks and
        /// then detonates should detonate where it landed. A <see cref="AbilityAimShape.Capsule"/>
        /// is the opposite case — it describes the lane the caster sweeps <i>through</i>, so
        /// its origin is the take-off point. Flying Peck is the ability this exists for:
        /// with <c>JumpTier: Short</c> its lane was resolving 5 m past the press point, so it
        /// flew over everything the player aimed at while the telegraph drew the lane at the
        /// live position.
        ///
        /// Keyed off the shape, never off an ability's name, so a future capsule ability
        /// inherits the correct ordering without anyone remembering to special-case it.
        /// </summary>
        public bool ResolvesBeforeJump => AimShape == AbilityAimShape.Capsule;

        /// <summary>
        /// Is the pose this cast was aimed from already gone, and unrecoverable, by the time
        /// a peer observes the cast event? True only for an ability that resolves before its
        /// own teleport (<see cref="ResolvesBeforeJump"/>) <i>and</i> actually teleports —
        /// Flying Peck, today the only one.
        ///
        /// The take-off pose is not replicated (this stage adds no networked state and no
        /// RPCs), and every observer runs after the jump has landed, so anything that tries
        /// to re-derive the cast's footprint locally would draw or spark it a full jump
        /// length off. Two consumers opt out on this rather than each restating the rule:
        /// <c>HitFeedback.ConfirmHits</c> (skips per-victim sparks, keeps the caster's punch)
        /// and <c>AbilityRangeIndicator.ObserveCast</c> (falls back to a caster self-ring
        /// instead of drawing a mislocated lane).
        /// </summary>
        public bool CastPoseIsUnreconstructable => ResolvesBeforeJump && JumpTier != JumpLengthTier.None;

        /// <summary>
        /// Does aiming this ability mean aiming a <i>direction</i>? True for every shape
        /// whose footprint moves when the caster turns, which is what
        /// <c>ChickenController.IsAimRotating</c> uses to convert the movement stick into
        /// facing during a hold (FEEDBACK.md §2.3).
        /// </summary>
        /// <remarks>
        /// Written as an exclusion list rather than an inclusion list on purpose: this used
        /// to be three hard-coded shape comparisons inside <c>ChickenController</c>, so
        /// adding <see cref="AbilityAimShape.Capsule"/> would have silently taken aim-rotate
        /// away from the two most directional abilities in the game. Inverting it makes a
        /// newly added shape directional by default — a harmless wrong answer (you can turn
        /// while aiming a circle) instead of a broken one.
        /// </remarks>
        public bool IsDirectionalAim => AimShape switch
        {
            AbilityAimShape.None         => false, // nothing to aim
            AbilityAimShape.SelfCircle   => false, // caster-centred, rotation-invariant
            AbilityAimShape.Aura         => false,
            AbilityAimShape.SingleTarget => false, // resolves by distance, not by facing
            _                            => true,  // Cone, ForwardCircle, Jump, Capsule, …
        };

        /// <summary>
        /// True for abilities that <i>place</i> something (an <c>AbilityZone</c>) instead of
        /// resolving a hit at the instant of the cast. Their aim shape is real — it is the
        /// zone's footprint, and the telegraph draws it correctly — but nobody is hit when
        /// the button is released; the hit happens later, in <c>AbilityZone</c>'s own tick.
        /// Feather Trap and Root Egg. See <see cref="ReportsCastHits"/> for why this needs
        /// to be declared rather than inferred.
        /// </summary>
        public virtual bool PlacesZone => false;

        /// <summary>
        /// Is <c>AbilityController.LastCastHitCount</c> a meaningful hit-vs-whiff verdict for
        /// this ability? False in exactly two cases, unified into one predicate so no
        /// consumer has to restate them (and so a future ability cannot join the wrong
        /// bucket by omission):
        /// <list type="bullet">
        ///   <item><b>Self-buffs.</b> <see cref="GatherTargets"/> only returns candidates
        ///   inside an aim shape, and <see cref="AbilityAimShape.None"/> has no shape, so a
        ///   self-buff always reports 0.</item>
        ///   <item><b>Placed zones</b> (<see cref="PlacesZone"/>). Root Egg spawns at the
        ///   caster's own feet with <see cref="AffectsSelf"/> false, so on open ground it
        ///   reports 0 on <i>every single cast</i> — a perfectly placed trap was being
        ///   whiff-styled every time.</item>
        /// </list>
        /// Every whiff-vs-hit consumer gates on this: <c>HitFeedback.ObserveOwnCast</c> and
        /// <c>AbilityRangeIndicator.ObserveCast</c>'s grey cast ring (FEEDBACK.md §3.3).
        /// A placed zone gets its confirmation later instead, when the zone actually fires
        /// (<c>AbilityZone.Render</c> → <c>HitFeedback.NotifyZoneTriggered</c>).
        /// </summary>
        public bool ReportsCastHits => AimShape != AbilityAimShape.None && !PlacesZone;

        /// <summary>
        /// Is a cast-time hit count of <b>zero</b> a miss for this ability? The narrower
        /// half of <see cref="ReportsCastHits"/>, and the only thing the grey whiff ring
        /// should ever gate on.
        ///
        /// The two questions come apart for gap-closers. Dive Bomb declares a real
        /// <see cref="AbilityAimShape.Capsule"/> and genuinely robs whoever it sweeps
        /// through, so <see cref="ReportsCastHits"/> is true and must stay true — a landed
        /// dive still earns its hit-confirm sparks. But since playtest item 5 it also fires
        /// with nobody in the lane, because the dive itself is the point. Reading that as a
        /// whiff would grey-ring the player for doing exactly what they asked for: the same
        /// bug class as the placed-zone false whiff fixed on 2026-08-02, arriving from the
        /// other direction.
        ///
        /// Derived from <see cref="RequiresEnemyInRange"/> rather than declared separately,
        /// so the two can never disagree. An ability that refuses to fire without a target
        /// can only reach zero hits by having genuinely lost one between the gate and the
        /// scan — a real miss. An ability that is allowed to fire without one cannot be
        /// missing when it reports zero; that is its normal case.
        /// </summary>
        public bool ZeroHitsIsAWhiff => ReportsCastHits && RequiresEnemyInRange;

        /// <summary>Does this ability's area mark rival chickens?</summary>
        public virtual bool AffectsEnemies => true;

        /// <summary>Does this ability's area mark the caster themselves (self-buffs)?</summary>
        public virtual bool AffectsSelf => false;

        /// <summary>
        /// Per-ability <b>eligibility</b> condition beyond geometry. Called only after the
        /// geometric test passes, and only from <see cref="WouldAffect"/>.
        /// </summary>
        /// <remarks>
        /// <b>What actually overrides this today</b> — a cargo requirement (Snatch, Sneaky
        /// Steal, Scrap, Roll Trample all reject an empty-handed rival), Dust Kick's rear-arc
        /// restriction, and Mark/Kill's isolation + rival rules. That is the complete list.
        ///
        /// <b>Immunity is deliberately NOT one of them, and must not become one.</b> A control
        /// -immune chicken (Immovable) is still <i>eligible</i>: it is gathered, it counts
        /// toward <c>AbilityController.LastCastHitCount</c>, and it keeps
        /// <see cref="RequiresEnemyInRange"/> satisfiable. Filtering it out here would make an
        /// ability refuse to fire because the only rival nearby is Immovable, and would
        /// whiff-style a cast that genuinely reached someone. The "it landed on nothing"
        /// half is <see cref="IsEffectNullified"/> instead — see there for the split.
        /// </remarks>
        protected virtual bool ExtraTargetFilter(Gameplay.ChickenController caster, Gameplay.ChickenController candidate) => true;

        /// <summary>
        /// Is <i>everything</i> this ability does to a target a control effect — knockback,
        /// stun, root or ability-slow — with nothing else left over? Declared per ability,
        /// never inferred.
        /// </summary>
        /// <remarks>
        /// <b>Declared, because nothing already authored answers it.</b>
        /// <see cref="AbilityCategory.Control"/> is the obvious candidate and is wrong in both
        /// directions: Mark/Kill is categorised Control but applies an execute mark, which no
        /// immunity stops; Roll Push is pure knockback and declares no category at all.
        /// Category is a design-pool label, not a statement about what lands on a victim.
        ///
        /// <b>Mixed abilities are false, on purpose.</b> Snatch shoves <i>and</i> robs. Against
        /// an Immovable rival the shove is dropped and the theft goes through untouched, so
        /// "IMMUNE" over their head would be a lie — Immovable's counterplay <i>is</i> cargo
        /// (see <c>ImmovableAbilitySO</c>). Only an ability with nothing left over after the
        /// control is removed may set this true.
        ///
        /// <b>Placed zones are false too</b>, for a different reason: a zone's slow and root
        /// reach the victim through <c>ChickenController.CheckAbilityZoneSlow</c> /
        /// <c>CheckAuraSlow</c>, which call <c>ApplySlow</c> directly rather than routing
        /// through <c>ApplyPassiveControlDuration</c> — so immunity does not stop them, and
        /// claiming it would grey out a bracket for an effect that lands.
        /// </remarks>
        public virtual bool TargetEffectIsPurelyControl => false;

        /// <summary>
        /// <b>Eligible, but the effect will land on nothing.</b> The third state between
        /// "outside the shape" and "will be hit": <paramref name="candidate"/> is gathered,
        /// counted and telegraphed exactly as before, and every effect this ability would
        /// deliver to them is refused at <c>ChickenController</c>'s immunity chokepoints.
        /// </summary>
        /// <remarks>
        /// <b>Why this is not folded into <see cref="ExtraTargetFilter"/>.</b> Eligibility and
        /// effect are different questions and the codebase needs both answers. Dropping an
        /// immune target out of <see cref="GatherTargets"/> would silently change two things
        /// that have nothing to do with feedback: <see cref="RequiresEnemyInRange"/> usability
        /// (an ability refusing to fire because the only rival in range is Immovable) and
        /// <c>LastCastHitCount</c> (a cast that reached someone being styled as a whiff via
        /// <see cref="ZeroHitsIsAWhiff"/>). Keeping the target eligible and marking it
        /// unaffected changes the feedback and only the feedback.
        ///
        /// Two consumers, and they are the two halves of the same story — FEEDBACK.md §2.2's
        /// grey ⃠ bracket (<c>AbilityTelegraph.UpdateMarks</c>, "nothing will happen") and
        /// §3.2 case 15's grey <c>IMMUNE</c> text (<c>HitFeedback.ConfirmHits</c>, "nothing
        /// happened"). Both run on every peer off already-replicated state; no RPC, no new
        /// networked field. <c>IsControlImmune</c> is readable everywhere for exactly this
        /// reason.
        /// </remarks>
        public bool IsEffectNullified(Gameplay.ChickenController caster, Gameplay.ChickenController candidate)
        {
            if (!TargetEffectIsPurelyControl) return false;
            if (candidate == null || candidate == caster) return false;
            return candidate.IsControlImmune;
        }

        /// <summary>
        /// Geometry only: is <paramref name="candidate"/> inside this ability's aim
        /// shape right now? True even for a candidate that <see cref="WouldAffect"/>
        /// will go on to reject via <see cref="ExtraTargetFilter"/> — the telegraph
        /// (Stage 3) uses that gap to draw the grey "in the area but no effect"
        /// overlay (FEEDBACK.md §2.2) instead of a valid-target one.
        /// </summary>
        public bool IsInAimShape(Gameplay.ChickenController caster, Gameplay.ChickenController candidate)
        {
            if (caster == null || candidate == null) return false;
            return AbilityAim.InShape(AimShape, caster.transform.position, caster.transform.forward,
                candidate.transform.position, AimRadius, AimForwardOffset, AimConeAngle);
        }

        /// <summary>
        /// Per-candidate <b>eligibility</b>: in-shape AND alive AND on the right side
        /// (<see cref="AffectsSelf"/>/<see cref="AffectsEnemies"/>) AND passes
        /// <see cref="ExtraTargetFilter"/>. It must always imply
        /// <see cref="IsInAimShape"/>, never the reverse — that invariant is EditMode-locked
        /// and is what lets the telegraph's grey "in the area, no effect" overlay be the
        /// exact gap between the two.
        ///
        /// <b>Eligibility is not selection.</b> For a shape that hits everything it covers
        /// the two coincide, but a <see cref="AbilityAimShape.SingleTarget"/> ability has
        /// several eligible candidates and hits one. <see cref="GatherTargets"/> owns that
        /// resolution step; ask it, not this, for "who actually gets hit".
        ///
        /// <b>Eligibility is not effect either.</b> A control-immune target is eligible and
        /// gets hit; the hit simply does nothing. Ask <see cref="IsEffectNullified"/>, not
        /// this, for "will it accomplish anything" — that is what the grey ⃠ bracket and the
        /// <c>IMMUNE</c> text are about, and folding it in here would change usability and
        /// whiff styling as a side effect.
        ///
        /// <b>Decoys are legitimate targets</b> (Maestro's call — an ability that phased
        /// through a Doppelganger would have no reason to exist). The attacker commits, pays
        /// the cooldown and gets nothing of value; the decoy cannot score, carry, deposit,
        /// credit a kill or change a player count, which is enforced at those systems rather
        /// than by refusing to aim at it. As a bonus this removes a latent hazard: targeting
        /// legality no longer depends on whether <c>Doppelganger.Spawned</c> happened to run
        /// before <c>ChickenController.Spawned</c> on a given peer.
        /// </summary>
        public bool WouldAffect(Gameplay.ChickenController caster, Gameplay.ChickenController candidate)
        {
            if (caster == null || candidate == null) return false;

            bool isSelf = candidate == caster;
            if (isSelf && !AffectsSelf) return false;
            if (!isSelf && !AffectsEnemies) return false;

            if (candidate.Combat != null && candidate.Combat.IsDead) return false;

            if (!IsInAimShape(caster, candidate)) return false;

            return ExtraTargetFilter(caster, candidate);
        }

        /// <summary>
        /// Zero-allocation scratch buffer for <see cref="GatherTargets"/> calls made
        /// from inside <see cref="OnActivate"/>. Safe as a shared static because Unity
        /// runs single-threaded and every <c>OnActivate</c> call fully consumes the
        /// buffer synchronously before returning — no ability holds a reference to it
        /// across activations.
        /// </summary>
        [System.NonSerialized] protected static readonly List<Gameplay.ChickenController> _scratch = new List<Gameplay.ChickenController>(4);

        /// <summary>
        /// <b>Resolution:</b> fills <paramref name="buffer"/> with the chickens this ability
        /// will actually hit if it fires right now, in ascending distance order from the
        /// shape's centre (<see cref="AbilityAim.ShapeCenter"/>).
        ///
        /// This is the ONLY target scan in the codebase. <see cref="OnActivate"/>,
        /// <c>AbilityController.TryActivate</c>'s hit count, the hold-to-aim telegraph's
        /// valid-target marks and <c>HitFeedback.ConfirmHits</c> all consume this one call,
        /// so what the player sees marked is exactly what gets hit.
        ///
        /// <b>Eligibility (<see cref="WouldAffect"/>) is filtered here; selection is decided
        /// here too.</b> A <see cref="AbilityAimShape.SingleTarget"/> ability is eligible
        /// against every carrier in range but only ever robs one of them, so the buffer is
        /// truncated to the nearest — otherwise the telegraph lit up three rivals for a cast
        /// that took from one.
        ///
        /// Iterates <see cref="Gameplay.ChickenController.ActiveControllers"/> (≤4 entries)
        /// and insertion-sorts — cheap enough to not need <see cref="System.Linq"/>, which
        /// would allocate.
        /// </summary>
        public int GatherTargets(Gameplay.ChickenController caster, List<Gameplay.ChickenController> buffer)
        {
            buffer.Clear();
            if (caster == null) return 0;

            var all = Gameplay.ChickenController.ActiveControllers;
            Vector3 center = AbilityAim.ShapeCenter(AimShape, caster.transform.position, caster.transform.forward, AimForwardOffset);

            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || !WouldAffect(caster, candidate)) continue;

                float distSqr = PlanarSqrDistance(center, candidate.transform.position);
                int insertAt = buffer.Count;
                for (int j = 0; j < buffer.Count; j++)
                {
                    if (PlanarSqrDistance(center, buffer[j].transform.position) > distSqr) { insertAt = j; break; }
                }
                buffer.Insert(insertAt, candidate);
            }

            // Single-target abilities resolve to exactly the nearest eligible candidate. Done
            // after the sort rather than by tracking a running minimum so there is one
            // ordering rule for every shape, and so index 0 keeps meaning "nearest" for the
            // OnActivate implementations that already rely on it.
            if (AimShape == AbilityAimShape.SingleTarget && buffer.Count > 1)
                buffer.RemoveRange(1, buffer.Count - 1);

            return buffer.Count;
        }

        /// <summary>
        /// Is there any <i>eligible</i> candidate right now? Early-outs on the first hit, no
        /// buffer — cheap enough for per-frame HUD polling (<see cref="IsUsable"/>,
        /// <c>AbilityRangeIndicator</c>). Deliberately eligibility and not resolution: a
        /// single-target ability with three carriers in range is just as usable as one with
        /// a single carrier, so the grey-out only ever needs to know "any".
        /// </summary>
        public bool HasAnyTarget(Gameplay.ChickenController caster)
        {
            if (caster == null) return false;
            var all = Gameplay.ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && WouldAffect(caster, all[i])) return true;
            }
            return false;
        }

        private static float PlanarSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Pre-commitment gate: is this ability wired up well enough to run at all? Checked
        /// once per press, immediately before <c>AbilityController.TryActivate</c> commits
        /// <c>ActiveSlot</c>, the activation timer and the cooldown — so a false here costs
        /// the player nothing.
        /// </summary>
        /// <remarks>
        /// <b>Not the same question as <see cref="IsUsable"/>.</b> <c>IsUsable</c> asks
        /// "would this ability accomplish anything right now" — a gameplay question, polled
        /// continuously, which is why it feeds the HUD grey-out. <c>CanActivate</c> asks "is
        /// this ability wired up well enough to run at all" — a wiring question, and a false
        /// is a genuine bug (an unassigned prefab, a missing registry) that the player should
        /// not be charged a cast and a cooldown for.
        ///
        /// It deliberately does <b>not</b> reach the HUD. Routing a wiring failure into the
        /// grey-out would trade a silent failure for a button that is dead forever with no
        /// explanation — quieter, not better. A press that refuses and logs why is the louder
        /// of the two, which is the one this project wants.
        ///
        /// An override is expected to have <b>already logged the reason</b> before returning
        /// false — that is <see cref="AbilityContext.CanSpawnZone"/>'s contract — so the
        /// caller only has to return. Returning false silently would reintroduce exactly the
        /// failure this gate exists to surface.
        /// </remarks>
        public virtual bool CanActivate(AbilityContext ctx) => true;

        public abstract void OnActivate(AbilityContext ctx);
        public abstract void OnDeactivate(AbilityContext ctx);
    }
}
