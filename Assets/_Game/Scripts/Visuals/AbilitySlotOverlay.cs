using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Settings;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The always-on <b>ability reach overlay</b>: a permanent, ground-level outline of what
    /// every equipped ability slot can reach, drawn for the local player during normal play.
    ///
    /// <para>
    /// Maestro's problem statement: a new player cannot tell what any ability covers until
    /// they are already aiming it, at which point it is too late to be a learning aid. This
    /// draws all four slots at once, all match, in each ability's own <c>AccentColor</c>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Additive to the aim telegraph, never a replacement.</b> <see cref="AbilityTelegraph"/>
    /// keeps its press-to-preview / release-to-launch behaviour unchanged. This is deliberately
    /// a lower-fidelity, unmistakably-not-aiming reference: the thinnest stroke in the feedback
    /// system (<see cref="FeedbackTuning.AmbientSlotOutlineWidth"/>) and alphas capped well
    /// under <see cref="FeedbackTuning.TelegraphPreviewAlpha"/>. Only alpha encodes state;
    /// width is flat across every tier.
    /// </para>
    /// <para>
    /// <b>Outlines, not filled areas.</b> Four simultaneous translucent fills would both
    /// obscure each other and cost real transparent overdraw on the 30 fps Android target.
    /// The outline primitive is shared with the telegraph and the cast flash via
    /// <see cref="TelegraphShapes"/>, so all three can never disagree about an ability's shape.
    /// </para>
    /// <para>
    /// <b>Local caster only (FEEDBACK.md §1.6), with more force than the telegraph.</b> Leaking
    /// one live aim is bad; leaking a rival's entire permanent reach map would solve the arena.
    /// Gated on <c>HasInputAuthority</c> exactly like <c>AbilityTelegraph.TryGetCharge</c> and
    /// the range ring this replaces.
    /// </para>
    /// <para>
    /// <b>Two independent gates.</b> <see cref="FeedbackTuning.AbilitySlotOverlayEnabled"/> is
    /// the developer kill switch, honoured in <see cref="Awake"/> before anything is allocated;
    /// <see cref="PlayerPreferences.AbilityRangeGuidesEnabled"/> is the player's own toggle,
    /// re-read every frame so flipping it mid-match takes effect live.
    /// </para>
    /// <para>
    /// <b>No networking of any kind.</b> No <c>[Networked]</c> state, no RPCs. It polls state
    /// that is already replicated (<c>ChargingSlot</c>, cooldowns, rival positions) in
    /// <c>LateUpdate</c>, exactly like <see cref="AbilityTelegraph"/> and
    /// <see cref="AbilityRangeIndicator"/> — per CONVENTIONS, visuals are never networked.
    /// </para>
    /// <para>
    /// <b>Maestro</b>: add to Chicken.prefab (Doppelganger inherits it). No Inspector wiring —
    /// the lines are built procedurally in <c>Awake</c>. Guarded by
    /// <c>AbilitySlotOverlayTests.ChickenPrefab_CarriesTheAbilitySlotOverlay</c>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ChickenController))]
    public sealed class AbilitySlotOverlay : MonoBehaviour
    {
        private ChickenController _controller;
        private AbilityController _abilities;

        /// <summary>One outline per slot, indexed by slot. Sized from
        /// <see cref="AbilityController.SlotCount"/> and never from a literal — the range ring
        /// this replaces hard-coded 3 and silently never drew slot 3 after the four-slot
        /// roster landed.</summary>
        private readonly LineRenderer[] _lines = new LineRenderer[AbilityController.SlotCount];

        /// <summary>Per-slot point buffers, grown by <see cref="TelegraphShapes.Apply"/> only
        /// when a slot's point count changes (i.e. never, on a steady frame). Per-slot rather
        /// than shared because four different shapes need four different lengths at once.</summary>
        private readonly Vector3[][] _pointBuffers = new Vector3[AbilityController.SlotCount][];

        /// <summary>Cached "a rival is inside this slot's shape" verdicts, refreshed at
        /// <see cref="FeedbackTuning.TargetScanPollHz"/>. See <see cref="PollHotSlots"/>.</summary>
        private readonly bool[] _hot = new bool[AbilityController.SlotCount];
        private float _hotPollTimer;

        private void Awake()
        {
            // Kill switch first, before any allocation: "off" must mean no LineRenderers, no
            // per-frame work and nothing left on screen — the RivalIndicator.Awake contract.
            if (!FeedbackTuning.AbilitySlotOverlayEnabled)
            {
                enabled = false;
                return;
            }

            _controller = GetComponent<ChickenController>();
            _abilities  = GetComponent<AbilityController>();

            // ONE material for all four lines. TelegraphShapes.BuildLineMaterial does
            // `new Material(...)` per call, so calling it per slot would create four distinct
            // instances of the same unlit transparent material and permanently cost four
            // unbatchable SetPass calls on the local chicken. Same one-material-many-lines
            // shape AbilityRangeIndicator.Awake already uses.
            var mat = TelegraphShapes.BuildLineMaterial();

            for (int slot = 0; slot < AbilityController.SlotCount; slot++)
            {
                var lr = TelegraphShapes.BuildLine(transform, $"AbilitySlotOverlay{slot}", mat,
                                                   FeedbackTuning.AmbientSlotOutlineWidth, loop: true);
                lr.enabled = false;
                _lines[slot] = lr;
            }
        }

        /// <summary>Disabling the component must not strand four outlines on the ground. The
        /// lines are children of this chicken so they die with it, but <c>enabled = false</c>
        /// is an ordinary Unity operation and the ghost would be baffling — the
        /// <c>RivalIndicator.OnDisable</c> precedent.</summary>
        private void OnDisable() => HideAll();

        private void LateUpdate()
        {
            if (!ShouldDraw())
            {
                HideAll();
                return;
            }

            PollHotSlots();

            byte chargingEncoded = _abilities.ChargingSlot; // 1-based; 0 = nothing being aimed
            Vector3 pos = _controller.transform.position;
            Vector3 fwd = _controller.transform.forward;

            for (int slot = 0; slot < AbilityController.SlotCount; slot++)
                DrawSlot(slot, chargingEncoded, pos, fwd);
        }

        /// <summary>
        /// Every reason to draw nothing at all, collapsed into one answer: missing components,
        /// a <c>NetworkObject</c> that is not live (reading <c>[Networked]</c> state outside
        /// Spawned..Despawned is not safe), a chicken that is not the local player (§1.6), or
        /// the player having turned the guides off.
        /// </summary>
        private bool ShouldDraw()
        {
            if (_controller == null || _abilities == null) return false;

            // Awake returns before building anything when the kill switch is off. Nothing
            // re-enables this component today, but `enabled = true` is an ordinary Unity
            // operation and there would be no geometry to drive if it happened.
            if (_lines[0] == null) return false;

            var obj = _abilities.Object;
            if (obj == null || !obj.IsValid) return false;

            if (!_controller.HasInputAuthority) return false;

            return PlayerPreferences.AbilityRangeGuidesEnabled;
        }

        /// <summary>
        /// Refreshes the per-slot "a rival is standing in this shape" flags that drive the
        /// brightened <see cref="FeedbackTuning.AmbientSlotOutlineAlphaHot"/> tier.
        /// </summary>
        /// <remarks>
        /// <b>Rate-limited to <see cref="FeedbackTuning.TargetScanPollHz"/>, while the geometry
        /// below stays per-frame.</b> Exactly the split <c>AbilityTelegraph.UpdateMarks</c>
        /// already makes, and for the same reason: a target crossing a boundary reads as
        /// instant at 10 Hz, but the shapes must track the caster's own movement and rotation
        /// smoothly or the overlay looks broken. The retired range ring evaluated
        /// <c>IsUsable</c> per-frame for up to three slots; scaling that to four permanent
        /// slots would have been four times the per-frame scan cost the project already judged
        /// too expensive to run every frame for ONE ability on a 2021 mid-range Android.
        ///
        /// <b>Deliberately not <c>AbilityBaseSO.HasAnyTarget</c>.</b> That counts the caster
        /// for an <c>AffectsSelf</c> ability, so Shadowstep — whose landing ring is centred on
        /// its own caster — would report a target on every single frame and pin the hot tier on
        /// permanently, making a pure mobility blink the brightest thing on screen all match.
        /// "Hot" here means a <i>rival</i> is in the shape, which is the decision the player is
        /// actually being helped with.
        /// </remarks>
        private void PollHotSlots()
        {
            _hotPollTimer -= Time.deltaTime;
            if (_hotPollTimer > 0f) return;
            _hotPollTimer = FeedbackTuning.TargetScanPollHz > 0f ? 1f / FeedbackTuning.TargetScanPollHz : 0.1f;

            for (int slot = 0; slot < AbilityController.SlotCount; slot++)
            {
                var ability = _abilities.GetSlot(slot);
                _hot[slot] = ability != null && HasRivalInShape(ability);
            }
        }

        /// <summary>Is any chicken other than the caster currently eligible inside
        /// <paramref name="ability"/>'s aim shape? Scans the ≤4-entry
        /// <see cref="ChickenController.ActiveControllers"/> registry — the same registry
        /// <c>GatherTargets</c> and the telegraph use, never a physics query.</summary>
        private bool HasRivalInShape(AbilityBaseSO ability)
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || candidate == _controller) continue;
                if (ability.WouldAffect(_controller, candidate)) return true;
            }
            return false;
        }

        private void DrawSlot(int slot, byte chargingEncoded, Vector3 casterPos, Vector3 casterForward)
        {
            var lr = _lines[slot];

            // The slot being aimed right now is FULLY hidden, not faded underneath.
            // AbilityTelegraph is already drawing identical geometry at 0.75 alpha plus the
            // illegal-cast wash plus target brackets; a faint duplicate of the same outline
            // would show up as a double-stroke fringe around the real preview.
            if (chargingEncoded != 0 && chargingEncoded - 1 == slot)
            {
                if (lr.enabled) lr.enabled = false;
                return;
            }

            var ability = _abilities.GetSlot(slot);
            if (!TryResolveAmbientShape(ability, out var shape, out float radius,
                                        out float forwardOffset, out float coneAngle))
            {
                if (lr.enabled) lr.enabled = false;
                return;
            }

            Color c = ability.AccentColor;
            c.a = AlphaFor(slot, aimGestureLive: chargingEncoded != 0);
            lr.startColor = lr.endColor = c;

            // NO STALK, by decision — one LineRenderer per slot, not two.
            // TelegraphShapes.NeedsStalk is true for a detached ForwardCircle (Feather Trap is
            // the live case) and the telegraph draws a caster→centre connector for it, so an
            // ambient Feather Trap circle floats ahead of the player with nothing tying it to
            // them. That is deliberate: the connector exists in the telegraph to say "that zone
            // is YOURS" among transient effects, whereas this overlay is local-only and every
            // outline on screen is already the player's own, so four permanent spokes radiating
            // from their feet would be clutter buying nothing. Flagged for the live look — if
            // the floating circle reads as unowned, adding a second per-slot line here is the
            // fix, not widening the outline.
            TelegraphShapes.Apply(lr, ref _pointBuffers[slot], shape, casterPos, casterForward,
                                  radius, forwardOffset, coneAngle);
            if (!lr.enabled) lr.enabled = true;
        }

        /// <summary>
        /// The four-tier readiness ladder. Cooldown is checked before "hot" on purpose: a slot
        /// you cannot fire is not a decision, however inviting the target inside it looks.
        /// </summary>
        private float AlphaFor(int slot, bool aimGestureLive)
        {
            // One slot is being aimed, and this is not it: recede as a group. Held steady, the
            // other three would read as "everything went on cooldown simultaneously" — the same
            // wrong story HexAlphaOtherActive exists to avoid on the HUD.
            if (aimGestureLive) return FeedbackTuning.AmbientSlotOutlineAlphaSuppressed;

            if (!_abilities.IsReady(slot)) return FeedbackTuning.AmbientSlotOutlineAlphaCooldown;

            return _hot[slot]
                ? FeedbackTuning.AmbientSlotOutlineAlphaHot
                : FeedbackTuning.AmbientSlotOutlineAlphaReady;
        }

        private void HideAll()
        {
            for (int slot = 0; slot < _lines.Length; slot++)
            {
                var lr = _lines[slot];
                if (lr != null && lr.enabled) lr.enabled = false;
            }
        }

        /// <summary>
        /// The ambient overlay's drawable parameters for <paramref name="ability"/>, or
        /// <c>false</c> when this ability must draw nothing at all. Pure and static so the
        /// rule below is EditMode-testable against the real shipped ability assets.
        /// </summary>
        /// <remarks>
        /// <b><see cref="AbilityAimShape.None"/> draws NOTHING here, which diverges from the
        /// telegraph on purpose. Do not "fix" this into consistency.</b>
        /// <see cref="TelegraphShapes.Resolve"/> degrades <c>None</c> into a caster self-ring at
        /// <see cref="FeedbackTuning.SelfRingRadius"/>, which is right for the telegraph — a
        /// self-buff being aimed still needs an "armed" pulse on the caster's own body, and it
        /// lasts only as long as the hold.
        ///
        /// It is wrong here for a concrete reason: <c>SelfRingRadius</c> is 0.62, which is
        /// exactly <c>ControlStateVFX</c>'s stun/root/slow status ring radius (that constant is
        /// literally defined as <c>FeedbackTuning.SelfRingRadius</c>). Reusing it would paint a
        /// permanent duplicate status ring at, say, a Spine Coat Warrior's feet for the entire
        /// match — and collide head-on with the real status ring the instant they were actually
        /// stunned, which is the exact moment that ring has to be unambiguous.
        ///
        /// So <c>None</c> short-circuits BEFORE <c>Resolve</c> is consulted. Everything else
        /// still goes through <c>Resolve</c>, because Jump genuinely needs its radius override
        /// (a fixed <see cref="AbilityBaseSO.JumpLandingRadius"/> landing ring rather than
        /// <c>AimRadius</c>) — Shadowstep's landing ring is the one shape where an always-on
        /// outline teaches something otherwise completely invisible.
        /// </remarks>
        public static bool TryResolveAmbientShape(AbilityBaseSO ability, out AbilityAimShape shape,
                                                  out float radius, out float forwardOffset,
                                                  out float coneAngleDeg)
        {
            shape         = AbilityAimShape.None;
            radius        = 0f;
            forwardOffset = 0f;
            coneAngleDeg  = 360f;

            // Read AimShape DIRECTLY, never via Resolve — see the remarks above.
            if (ability == null || ability.AimShape == AbilityAimShape.None) return false;

            TelegraphShapes.Resolve(ability, out shape, out radius, out forwardOffset, out coneAngleDeg);

            // Resolve has a second path to the self-ring: an ability that declares a real shape
            // but resolves to a non-positive radius. The same objection applies, so drop it too
            // rather than drawing a permanent 0.62 ring the author never asked for.
            if (shape == AbilityAimShape.None || radius <= 0f)
            {
                shape         = AbilityAimShape.None;
                radius        = 0f;
                forwardOffset = 0f;
                coneAngleDeg  = 360f;
                return false;
            }

            return true;
        }
    }
}
