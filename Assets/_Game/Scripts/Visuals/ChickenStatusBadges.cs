using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The §5.1 status badge stack: one small icon per active control status, floating
    /// above the nameplate with a numeric countdown, stacked vertically with the most
    /// severe on top (stun &gt; root &gt; slow). Answers FEEDBACK.md §7 cases 17 ("which
    /// control state"), 18 ("how long the state lasts") and 21 ("a rival's control state,
    /// from range") with an exact number, where the depleting ring
    /// (<see cref="ControlStateVFX"/>) answers the same questions approximately but from
    /// much further away.
    /// </summary>
    /// <remarks>
    /// <b>Extends ART.md §6.10, does not replace it.</b> §6.10 already defines two
    /// nameplate badges — 🌱 for root and 🐌 for slow — rendered inline in
    /// <see cref="ChickenNameplate"/>'s label. Those stay exactly as they are: they are
    /// the always-on, zero-cost "this chicken is not free" marker attached to the name.
    /// This component adds the layer §6.10 never had, the one §5.1 asks for — a *stack*,
    /// one row per simultaneously-active status, each carrying its own quantity (a
    /// countdown for the timed states, a magnitude for slow). A rooted-and-slowed chicken
    /// shows one §6.10 nameplate glyph and two badge rows.
    ///
    /// <b>Vertical band, and why this one.</b> The on-character stack is crowded:
    /// <list type="bullet">
    ///   <item>1.16 — cargo bar (<see cref="ChickenWorldBars"/>)</item>
    ///   <item>1.70 — nameplate (<see cref="ChickenNameplate"/>)</item>
    ///   <item><b>1.78 → 2.00 — this badge stack</b></item>
    ///   <item>2.05 → 3.15 — floating combat text (<see cref="FloatingCombatText"/>,
    ///   spawning at <see cref="FeedbackTuning.FloatingTextSpawnHeight"/> and rising by
    ///   <see cref="FeedbackTuning.FloatingTextRiseDistance"/>)</item>
    /// </list>
    /// That leaves a 0.35-unit gap between the nameplate and the floating-text floor, and
    /// the stack is sized to fit inside it: bottom row at
    /// <see cref="FeedbackTuning.StatusBadgeStackBaseHeight"/>, growing upward by
    /// <see cref="FeedbackTuning.StatusBadgeRowSpacing"/>, capped at
    /// <see cref="FeedbackTuning.StatusBadgeMaxRows"/> rows. Badge glyphs are authored
    /// deliberately smaller than the nameplate's so three rows still clear 2.05. In
    /// practice the top row sits lower than that worst case, because the stack billboards
    /// toward the camera and the iso view tilts its up-axis.
    ///
    /// <b>Bottom-anchored, growing upward.</b> A single badge therefore always appears in
    /// the same place, right above the name, rather than hovering at the top of a band
    /// that is mostly empty. "Most severe on top" is preserved by filling from the top of
    /// whatever height the stack currently has.
    ///
    /// <b>Local only.</b> Every value is observed from already-replicated state
    /// (<c>ControlFlags</c>, <c>StunRemaining</c>, <c>RootRemaining</c>) in
    /// <c>LateUpdate</c>. No RPCs, no new networked properties, nothing written.
    ///
    /// <b>Maestro:</b> add to the Chicken prefab alongside <see cref="ChickenNameplate"/>.
    /// No Inspector wiring required.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenStatusBadges : MonoBehaviour
    {
        /// <summary>
        /// Glyphs, one per state. Kept as named constants because they are the single
        /// thing here most likely to need swapping: <c>TextMesh</c> renders through the
        /// built-in dynamic font, and a platform whose font lacks one of these will draw a
        /// tofu box. ⚡ and 🐌 are already proven on this project (⚡ is new, 🐌 and ☠ ship
        /// today in <see cref="ChickenNameplate"/>). ⛓ is the §1.3 shackle called for by
        /// the spec; if a device fonts it as tofu, change this one const — 🌱 (the §6.10
        /// root glyph) is the drop-in fallback.
        ///
        /// Stage 5 (§5.2's HUD status strip) shows the same three states on the local
        /// player's touch HUD, and §5.1/§5.2 are explicitly one vocabulary — so these now
        /// alias <see cref="CluckWars.UI.HudFeedbackStyle"/>'s consts (compile-time, no
        /// runtime change) rather than holding their own copies. Swapping a tofu glyph
        /// there fixes both the world-space badge and the HUD row in one edit, which is
        /// the only way the two can be guaranteed never to drift apart.
        /// </summary>
        private const string StunGlyph = CluckWars.UI.HudFeedbackStyle.StunGlyph;
        private const string RootGlyph = CluckWars.UI.HudFeedbackStyle.RootGlyph;
        private const string SlowGlyph = CluckWars.UI.HudFeedbackStyle.SlowGlyph;

        /// <summary>Badge text size. About half the nameplate's characterSize (0.05) so
        /// three rows fit the 0.35-unit gap the layout budget allows — see the class
        /// remarks.</summary>
        private const float CharacterSize = 0.026f;
        private const int   FontSize      = 64;

        /// <summary>Duration of the scale punch when a badge first appears — the
        /// world-space half of §7 case 20 ("entering a control state"). Shares the 0.12 s
        /// tempo family used by every other "instant, no ambiguity" beat in this system
        /// (<see cref="FeedbackTuning.VictimHitFlashDurationSeconds"/>,
        /// <see cref="FeedbackTuning.DeniedPressBumpDurationSeconds"/>).</summary>
        private const float EntryPunchSeconds = FeedbackTuning.DeniedPressBumpDurationSeconds;

        /// <summary>Scale the stack punches in from.</summary>
        private const float EntryPunchStartScale = 0.55f;

        /// <summary>
        /// Threshold below which <c>SlowMultiplier</c> is treated as a real, printable
        /// magnitude. Note this is *not* the presence test — presence comes from the
        /// replicated <c>ControlVfx.Slowed</c> bit, which <c>ChickenController</c> raises
        /// at &lt; 0.92. This is only "is the local value meaningful enough to print",
        /// which is why it sits at the floating-point noise floor instead: a peer without
        /// state authority reads exactly 1 and must print nothing.
        /// </summary>
        private const float SlowMagnitudeEpsilon = 0.999f;

        private enum Badge : byte { None = 0, Stun = 1, Root = 2, Slow = 3 }

        private ChickenController _controller;

        private Transform   _stackRoot;
        private TextMesh[]  _rows;
        private Transform[] _rowTransforms;

        // Per-row cache so a text write only happens when the rendered string would
        // actually change — TextMesh writes rebuild the glyph mesh.
        private Badge[] _rowBadge;
        private int[]   _rowQuantity;

        // Scratch for this frame's active set. Fixed size, never reallocated.
        private Badge[] _active;
        private float[] _value;

        private float _nextTextUpdate;
        private byte  _lastActiveMask;
        private float _punchTimer = -1f;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();

            int rows = Mathf.Max(1, FeedbackTuning.StatusBadgeMaxRows);
            _rows          = new TextMesh[rows];
            _rowTransforms = new Transform[rows];
            _rowBadge      = new Badge[rows];
            _rowQuantity   = new int[rows];
            _active        = new Badge[rows];
            _value         = new float[rows];

            BuildStack(rows);
        }

        private void BuildStack(int rows)
        {
            var root = new GameObject("StatusBadges");
            root.transform.SetParent(transform, worldPositionStays: false);
            root.transform.localPosition = new Vector3(0f, FeedbackTuning.StatusBadgeStackBaseHeight, 0f);
            _stackRoot = root.transform;

            for (int i = 0; i < rows; i++)
            {
                var go = new GameObject("Badge" + i);
                go.transform.SetParent(_stackRoot, worldPositionStays: false);
                _rowTransforms[i] = go.transform;

                // Mirrors ChickenNameplate.BuildLabel exactly — same TextMesh path, same
                // billboard, so the two never drift apart in look or in cost.
                var text = go.AddComponent<TextMesh>();
                text.anchor        = TextAnchor.MiddleCenter;
                text.alignment     = TextAlignment.Center;
                text.fontSize      = FontSize;
                text.characterSize = CharacterSize;
                text.fontStyle     = FontStyle.Bold;
                text.text          = string.Empty;
                text.color         = Color.white;
                _rows[i] = text;

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows    = false;
                }

                go.SetActive(false);
                _rowBadge[i]    = Badge.None;
                _rowQuantity[i] = int.MinValue;
            }

            _stackRoot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_controller == null || _stackRoot == null) return;

            // The IsDecoy gate stays even though decoys are legitimate targets now
            // (AbilityBaseSO.WouldAffect), and it is worth saying why it is not the tell it
            // looks like: CollectActive() reads presence from the replicated ControlFlags
            // bits ONLY, and ChickenController.FixedUpdateNetwork early-returns on IsDecoy
            // before ControlFlags is ever written. Dropping this guard would therefore
            // change nothing at all — a decoy would still show zero badges — while making
            // that outcome depend silently on another component's early return. Making a
            // decoy badge honestly needs a decoy-side ControlFlags derivation, which is a
            // real feature, not a guard removal.
            var obj = _controller.Object;
            if (obj == null || !obj.IsValid || _controller.IsDecoy)
            {
                HideStack();
                return;
            }

            int count = CollectActive();
            if (count == 0)
            {
                HideStack();
                return;
            }

            if (!_stackRoot.gameObject.activeSelf) _stackRoot.gameObject.SetActive(true);

            UpdateEntryPunch(count);
            LayoutAndFill(count);
            BillboardToCamera();
        }

        /// <summary>
        /// Fills <see cref="_active"/>/<see cref="_value"/> in severity order and returns
        /// how many statuses are live. Presence always comes from the replicated
        /// <c>ControlFlags</c> bits so a rival's badge is correct on every peer (case 21);
        /// the *quantity* is looked up separately and may legitimately be unavailable, in
        /// which case the row renders as a bare glyph rather than a wrong number.
        ///
        /// A *removed* chicken (<c>ChickenCombat.IsRemoved</c>) deliberately gets no stun
        /// badge: removal runs on its own respawn clock, not the control-stun timer, and
        /// it is already told unambiguously by the nameplate's ☠ and the §3.4 execute
        /// treatment. Badging it would put a number-less ⚡ on a corpse.
        /// </summary>
        private int CollectActive()
        {
            var flags = _controller.ControlFlags;
            int n = 0;

            if ((flags & ControlVfx.Stunned) != 0 && n < _active.Length)
            {
                _active[n] = Badge.Stun;
                _value[n]  = _controller.StunRemaining;
                n++;
            }
            if ((flags & ControlVfx.Rooted) != 0 && n < _active.Length)
            {
                _active[n] = Badge.Root;
                _value[n]  = _controller.RootRemaining;
                n++;
            }
            if ((flags & ControlVfx.Slowed) != 0 && n < _active.Length)
            {
                _active[n] = Badge.Slow;
                // SlowMultiplier is recomputed StateAuthority-side every tick and reads as
                // a flat 1 on every other peer. Rather than render "×1.00" on a chicken
                // that is visibly slowed, a peer that cannot know the magnitude passes 0
                // here and gets the bare glyph. Slow also has no deadline to count down
                // to at all (see ChickenController.SlowMultiplier's remarks) — the
                // magnitude *is* the number for this state, by design, not a fallback.
                float m = _controller.SlowMultiplier;
                _value[n] = m < SlowMagnitudeEpsilon ? m : 0f;
                n++;
            }

            return n;
        }

        /// <summary>
        /// Punches the stack in when a status appears that was not there last frame — the
        /// world-space half of case 20. Only *additions* trigger it: a status expiring
        /// should not re-announce the ones still running.
        /// </summary>
        private void UpdateEntryPunch(int count)
        {
            byte mask = 0;
            for (int i = 0; i < count; i++) mask |= (byte)(1 << (int)_active[i]);

            if ((mask & ~_lastActiveMask) != 0) _punchTimer = 0f;
            _lastActiveMask = mask;

            float scale = 1f;
            if (_punchTimer >= 0f)
            {
                _punchTimer += Time.deltaTime;
                float t = _punchTimer / EntryPunchSeconds;
                if (t >= 1f) _punchTimer = -1f;
                else scale = Mathf.Lerp(EntryPunchStartScale, 1f, 1f - (1f - t) * (1f - t));
            }
            _stackRoot.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// Positions the live rows (most severe on top of a bottom-anchored stack) and
        /// refreshes their text. Text is only rewritten when the row's state changed or
        /// when the <see cref="FeedbackTuning.BadgeCountdownTextUpdateHz"/> gate opens
        /// <i>and</i> the value has actually moved by a displayed digit — a countdown
        /// showing one decimal has nothing to say between two frames 33 ms apart.
        /// </summary>
        private void LayoutAndFill(int count)
        {
            bool textGateOpen = Time.time >= _nextTextUpdate;
            if (textGateOpen)
                _nextTextUpdate = Time.time + 1f / Mathf.Max(1f, FeedbackTuning.BadgeCountdownTextUpdateHz);

            for (int i = 0; i < _rows.Length; i++)
            {
                var rowGo = _rowTransforms[i].gameObject;

                if (i >= count)
                {
                    if (rowGo.activeSelf) rowGo.SetActive(false);
                    _rowBadge[i] = Badge.None;
                    continue;
                }

                // Fill from the top: index 0 (most severe) takes the highest row.
                _rowTransforms[i].localPosition =
                    new Vector3(0f, FeedbackTuning.StatusBadgeRowSpacing * (count - 1 - i), 0f);

                Badge badge = _active[i];
                int quantity = Quantize(badge, _value[i]);

                bool badgeChanged = _rowBadge[i] != badge;
                if (badgeChanged || (textGateOpen && _rowQuantity[i] != quantity))
                {
                    _rowBadge[i]    = badge;
                    _rowQuantity[i] = quantity;
                    _rows[i].text   = Compose(badge, quantity);
                    _rows[i].color  = ColorFor(badge);
                }

                if (!rowGo.activeSelf) rowGo.SetActive(true);
            }
        }

        /// <summary>Reduces a raw value to the integer the label actually shows, so the
        /// string is only rebuilt when a visible digit moves. Tenths of a second for the
        /// timed states, hundredths of a multiplier for slow.</summary>
        private static int Quantize(Badge badge, float value)
        {
            if (value <= 0f) return 0;
            return badge == Badge.Slow
                ? Mathf.RoundToInt(value * 100f)
                : Mathf.CeilToInt(value * 10f);
        }

        private static string Compose(Badge badge, int quantity)
        {
            switch (badge)
            {
                case Badge.Stun:
                    return quantity > 0 ? StunGlyph + (quantity * 0.1f).ToString("0.0") + "s" : StunGlyph;
                case Badge.Root:
                    return quantity > 0 ? RootGlyph + " " + (quantity * 0.1f).ToString("0.0") + "s" : RootGlyph;
                case Badge.Slow:
                    // The magnitude, not a countdown — slow has no deadline.
                    return quantity > 0 ? SlowGlyph + " ×" + (quantity * 0.01f).ToString("0.00") : SlowGlyph;
                default:
                    return string.Empty;
            }
        }

        private static Color ColorFor(Badge badge)
        {
            switch (badge)
            {
                case Badge.Stun: return FeedbackTuning.CanonicalStunColor;
                case Badge.Root: return FeedbackTuning.CanonicalRootColor;
                case Badge.Slow: return FeedbackTuning.CanonicalSlowColor;
                default:         return Color.white;
            }
        }

        private void HideStack()
        {
            _lastActiveMask = 0;
            _punchTimer     = -1f;
            // Re-open the text gate so the first frame of the next status writes a fresh
            // countdown rather than showing the last one for up to 200 ms.
            _nextTextUpdate = 0f;
            if (_stackRoot.gameObject.activeSelf) _stackRoot.gameObject.SetActive(false);
        }

        /// <summary>Billboards the whole stack once, not once per row. Identical to
        /// <c>ChickenNameplate</c>'s own billboard.</summary>
        private void BillboardToCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var lookDir = _stackRoot.position - cam.transform.position;
            if (lookDir.sqrMagnitude > 1e-6f)
                _stackRoot.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
        }
    }
}
