using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.UI;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The §5.1 status badge stack: one small icon per active status, floating above the
    /// nameplate with a numeric countdown where the state has one, stacked vertically with
    /// the most severe on top (stun &gt; egg &gt; waddle &gt; hidden &gt; root &gt; slow &gt; speeding,
    /// spec 2026-11-06 §5). Answers FEEDBACK.md §7 cases 17 ("which control state"), 18
    /// ("how long the state lasts") and 21 ("a rival's control state, from range") with an exact
    /// number, where the depleting ring (<see cref="ControlStateVFX"/>) answers the same
    /// questions approximately but from much further away.
    /// </summary>
    /// <remarks>
    /// <b>Extends ART.md §6.10, does not replace it.</b> §6.10 already defines two
    /// nameplate badges (a root glyph and a slow glyph) rendered in <see cref="ChickenNameplate"/>.
    /// Those stay: they are the always-on, zero-cost "this chicken is not free" marker attached
    /// to the name. This component adds the layer §6.10 never had, the one §5.1 asks for — a
    /// *stack*, one row per simultaneously-active status, each carrying its own quantity.
    ///
    /// <b>Sprites, not text glyphs (feedback batch C3).</b> Each row draws its state glyph with a
    /// <see cref="SpriteRenderer"/> (billboarded with the stack, same sorting as the row text); the
    /// word and the countdown stay <c>TextMesh</c>. The sprites are <c>[SerializeField]</c> fields
    /// wired on <c>Chicken.prefab</c> (no Resources load); the text glyph is only the documented
    /// fallback when a field is null, and that is logged once so a missing wiring is not silent.
    /// Root has no sprite (no caster applies it any more), so it stays text.
    ///
    /// <b>Vertical band, and why this one.</b> The on-character stack is crowded:
    /// <list type="bullet">
    ///   <item>1.16 — cargo bar (<see cref="ChickenWorldBars"/>)</item>
    ///   <item>1.70 — nameplate (<see cref="ChickenNameplate"/>), a pill whose top differs between the local "You"
    ///   pill and a rival's</item>
    ///   <item><b>pill top + <see cref="FeedbackTuning.StatusBadgeStackPillGap"/> and up — this badge stack,</b> anchored
    ///   to the pill (<see cref="ChickenNameplate.TryGetPillTopAnchor"/>) rather than a constant, so the taller local pill
    ///   is cleared too; <see cref="FeedbackTuning.StatusBadgeStackBaseHeight"/> is the fallback with no nameplate</item>
    ///   <item>above the shown rows — floating combat text (<see cref="FloatingCombatText"/>), spawning at
    ///   <c>max(<see cref="FeedbackTuning.FloatingTextSpawnHeight"/>, <see cref="CurrentStackTopLocalY"/> +
    ///   <see cref="FeedbackTuning.FloatingTextStackClearance"/>)</c> and rising by
    ///   <see cref="FeedbackTuning.FloatingTextRiseDistance"/></item>
    /// </list>
    /// Rows grow upward by <see cref="FeedbackTuning.StatusBadgeRowSpacing"/>, capped at
    /// <see cref="FeedbackTuning.StatusBadgeMaxRows"/> rows; pops clear whatever rows are shown (<c>StatusBadgeLayoutTests</c>). The worst case on one bird is
    /// stun + slow + zoom (the egg refuses both controls, the waddle refuses slows), so three
    /// rows stay enough.
    ///
    /// <b>Bottom-anchored, growing upward.</b> A single badge therefore always appears in
    /// the same place, right above the name, rather than hovering at the top of a band
    /// that is mostly empty. "Most severe on top" is preserved by filling from the top of
    /// whatever height the stack currently has.
    ///
    /// <b>Local only.</b> Every value is observed from already-replicated state
    /// (<c>ControlFlags</c>, <c>StunRemaining</c>, <c>RootRemaining</c>, the slot active mask and
    /// timers, <c>IsControlImmune</c>, <c>VisualOpacity</c>) in <c>LateUpdate</c>. No RPCs, no new
    /// networked properties, nothing written.
    ///
    /// <b>Maestro:</b> sprite fields are wired on the Chicken prefab (see the STATE entry).
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenStatusBadges : MonoBehaviour
    {
        private const string Source = "ChickenStatusBadges";

        /// <summary>
        /// Text fallbacks, used only when a state sprite field is null (see the class remarks).
        /// They alias <see cref="CluckWars.UI.HudFeedbackStyle"/>'s consts so the HUD strip and the
        /// world badge fall back to the same glyph.
        /// </summary>
        private const string StunGlyph = CluckWars.UI.HudFeedbackStyle.StunGlyph;
        private const string RootGlyph = CluckWars.UI.HudFeedbackStyle.RootGlyph;
        private const string SlowGlyph = CluckWars.UI.HudFeedbackStyle.SlowGlyph;

        // State glyph sprites, one per StateGlyphStyle state a badge can draw. Field names are
        // "_glyph" + the StateGlyph enum name: the wiring test derives the expected asset from that.
        [SerializeField] private Sprite _glyphStunned;
        [SerializeField] private Sprite _glyphSlowed;
        [SerializeField] private Sprite _glyphShieldWalk;
        [SerializeField] private Sprite _glyphShieldEgg;
        [SerializeField] private Sprite _glyphHidden;
        [SerializeField] private Sprite _glyphSpeeding;

        /// <summary>Sorting order of the glyph sprite and the row text: the top of the 10-12 label band
        /// (<see cref="FeedbackTuning.StatusBadgeSortingOrder"/>), over the rival pill and text and under the cargo bars. The stack
        /// sits above the nameplate pill, so it rarely overlaps one, but a neighbour's label must never draw over a badge.</summary>
        public const int SortingOrder = FeedbackTuning.StatusBadgeSortingOrder;

        /// <summary>Badge text size: 0.0338 (D0b) x 0.30/0.17 (D0c) to match the 0.30 glyph, so the row text cap height
        /// tracks the glyph; about 1.2x the nameplate's characterSize (0.05).</summary>
        private const float CharacterSize = 0.0596f;
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

        /// <summary>The badge kinds, in no particular order (priority lives in <see cref="Collect"/>).
        /// Values stay below 8 so the entry-punch mask fits a byte.</summary>
        public enum Badge : byte { None = 0, Stun = 1, Root = 2, Slow = 3, Waddle = 4, Egg = 5, Hidden = 6, Speed = 7 }

        private ChickenController _controller;
        private ChickenNameplate _nameplate;   // the stack sits on its pill; absent: StatusBadgeStackBaseHeight
        private float _headLift;
        private AbilityController _abilities;
        private ILogService _log;

        private Transform       _stackRoot;
        private TextMesh[]      _rows;
        private MeshRenderer[]  _rowRenderers;
        private Transform[]     _rowTransforms;
        private SpriteRenderer[] _glyphs;

        // Per-row cache so a text write only happens when the rendered string would
        // actually change — TextMesh writes rebuild the glyph mesh.
        private Badge[] _rowBadge;
        private int[]   _rowQuantity;
        private bool[]  _rowHasGlyph;

        // Scratch for this frame's active set. Fixed size, never reallocated.
        private Badge[] _active;
        private float[] _value;

        private readonly bool[] _warnedMissingSprite = new bool[8];

        private byte  _lastActiveMask;
        private float _punchTimer = -1f;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _abilities  = GetComponent<AbilityController>();
            _nameplate  = GetComponent<ChickenNameplate>();

            int rows = Mathf.Max(1, FeedbackTuning.StatusBadgeMaxRows);
            _rows          = new TextMesh[rows];
            _rowRenderers  = new MeshRenderer[rows];
            _rowTransforms = new Transform[rows];
            _glyphs        = new SpriteRenderer[rows];
            _rowBadge      = new Badge[rows];
            _rowQuantity   = new int[rows];
            _rowHasGlyph   = new bool[rows];
            _active        = new Badge[rows];
            _value         = new float[rows];

            BuildStack(rows);
        }

        /// <summary>
        /// Top edge of the badge rows this bird shows right now (or is about to show this frame), in the chicken's pivot-local Y:
        /// metres above its pivot, the same space pops spawn in (<c>pivot + Vector3.up * y</c>). Computed on demand from the live
        /// status set (<see cref="CollectActive"/>), not from last frame's layout, so a pop spawned on the very edge that adds a
        /// row already clears it. Conversion: the stack is anchored at the pill top along the billboard axis, which tilts toward
        /// the camera, so the pivot-local Y of the bottom row is that anchor's world Y minus the pivot's world Y, and each row
        /// offset in the root's space adds its vertical component, <c>offset * _stackRoot.up.y</c> (the bird is upright and not
        /// scaled in Y). With no rows (or a hidden bird or a decoy, which show none) it is the fallback base
        /// (<see cref="FeedbackTuning.StatusBadgeStackBaseHeight"/> plus the head lift).
        /// </summary>
        public float CurrentStackTopLocalY => StackTopLocalY(Badge.None);

        /// <summary>
        /// <see cref="CurrentStackTopLocalY"/> counting the row <paramref name="entering"/> that is added on this frame, for a
        /// state-entry pop spawned on the edge, before the layout (or, for a hit's control flag, the replicated state) shows it:
        /// the pop then starts above that row instead of on it. A row already shown adds nothing (<see cref="RowsWithEntry"/>).
        /// </summary>
        public float StackTopLocalYEntering(Badge entering) => StackTopLocalY(entering);

        private float StackTopLocalY(Badge entering)
        {
            float baseY = FeedbackTuning.StatusBadgeStackBaseHeight + _headLift;
            if (_controller == null || _stackRoot == null) return baseY;
            var obj = _controller.Object;
            if (obj == null || !obj.IsValid || _controller.IsDecoy || HiddenVisibilityRules.IsHiddenFor(_controller)) return baseY;
            int count = RowsWithEntry(_active, CollectActive(), entering, _active.Length);
            if (count == 0) return baseY;
            float bottomY = _nameplate != null && _nameplate.TryGetPillTopAnchor(FeedbackTuning.StatusBadgeStackPillGap, out Vector3 world)
                ? world.y - transform.position.y
                : baseY;
            return bottomY + StackTopInRoot(count) * _stackRoot.up.y;
        }

        /// <summary>
        /// Rows shown once <paramref name="entering"/> joins the <paramref name="count"/> live ones in <paramref name="active"/>:
        /// unchanged for <see cref="Badge.None"/>, when that row (or, for the shield's walk and egg, which replace each other,
        /// its twin) is already live, or at the <paramref name="maxRows"/> cap; otherwise one more.
        /// </summary>
        public static int RowsWithEntry(Badge[] active, int count, Badge entering, int maxRows)
        {
            if (entering == Badge.None || count >= maxRows) return count;
            Badge twin = entering == Badge.Egg ? Badge.Waddle : entering == Badge.Waddle ? Badge.Egg : entering;
            for (int i = 0; i < count; i++)
                if (active[i] == entering || active[i] == twin) return count;
            return count + 1;
        }

        /// <summary>
        /// Whether <paramref name="badge"/> has a row on this bird right now, for this peer (E3: the nameplate's pill snail
        /// stands in only when the Slow row is not shown). Live, like <see cref="CurrentStackTopLocalY"/>: it re-runs the same
        /// collection <c>LateUpdate</c> lays out (<see cref="CollectActive"/>, then <see cref="IsRowShown"/>) instead of reading
        /// last frame's layout, so a caller ahead of this component in the frame gets no one-frame lag. False whenever the
        /// whole stack is hidden (invalid object, decoy, a bird hidden for this peer) and for a row cut by the
        /// <see cref="FeedbackTuning.StatusBadgeMaxRows"/> cap. Allocation-free.
        /// </summary>
        public bool IsRowVisible(Badge badge)
        {
            if (badge == Badge.None || _controller == null || _stackRoot == null) return false;
            var obj = _controller.Object;
            if (obj == null || !obj.IsValid || _controller.IsDecoy || HiddenVisibilityRules.IsHiddenFor(_controller)) return false;
            return IsRowShown(_active, CollectActive(), badge);
        }

        /// <summary>Whether <paramref name="badge"/> is among the first <paramref name="count"/> (laid-out) entries of
        /// <paramref name="active"/>, the buffer <see cref="Collect"/> filled and capped at the row count.</summary>
        public static bool IsRowShown(Badge[] active, int count, Badge badge)
        {
            if (badge == Badge.None) return false;
            for (int i = 0; i < count && i < active.Length; i++)
                if (active[i] == badge) return true;
            return false;
        }

        /// <summary>Raises the badge stack for a class model drawn taller than the collider (Phase 8 D6).
        /// Root-local metres; presentation only.</summary>
        public void SetHeadLift(float lift)
        {
            _headLift = lift;
            if (_stackRoot != null) PlaceStack();
        }

        /// <summary>Places the stack: its bottom row <see cref="FeedbackTuning.StatusBadgeStackPillGap"/> above the
        /// nameplate's pill top along the label's billboard axis (the nameplate already carries the head lift), or at the
        /// constant base plus the lift when there is no fitted nameplate. Call after the stack's own billboard.</summary>
        private void PlaceStack()
        {
            if (_nameplate != null && _nameplate.TryGetPillTopAnchor(FeedbackTuning.StatusBadgeStackPillGap, out Vector3 world))
                _stackRoot.position = world;
            else
                _stackRoot.localPosition = new Vector3(0f, FeedbackTuning.StatusBadgeStackBaseHeight + _headLift, 0f);
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

                // Mirrors ChickenNameplate.BuildLabel — same TextMesh path, same billboard, so the two
                // never drift apart in look or in cost. Left-anchored: the row lays the glyph and the
                // text out side by side and centres the pair (LayoutRow).
                var textGo = new GameObject("Text");
                textGo.transform.SetParent(go.transform, worldPositionStays: false);
                var text = textGo.AddComponent<TextMesh>();
                text.anchor        = TextAnchor.MiddleLeft;
                text.alignment     = TextAlignment.Left;
                text.fontSize      = FontSize;
                text.characterSize = CharacterSize;
                text.fontStyle     = FontStyle.Bold;
                text.text          = string.Empty;
                text.color         = Color.white;
                _rows[i] = text;

                var mr = textGo.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows    = false;
                    mr.sortingOrder      = SortingOrder;
                }
                _rowRenderers[i] = mr;

                // The state glyph. Shared sprite material (no per-instance material); the sprites are
                // two-tone art, so the colour stays white and only a future alpha gate would touch it.
                var glyphGo = new GameObject("Glyph");
                glyphGo.transform.SetParent(go.transform, worldPositionStays: false);
                var sr = glyphGo.AddComponent<SpriteRenderer>();
                sr.sortingOrder      = SortingOrder;
                sr.color             = Color.white;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sr.receiveShadows    = false;
                sr.enabled           = false;
                _glyphs[i] = sr;

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
            // A1: a hidden bird (Hide / Smoke Roost) shows no badges to a remote peer; its owner keeps them
            // (the HIDDEN row itself is the owner's own cue, see ShowHiddenRow).
            if (obj == null || !obj.IsValid || _controller.IsDecoy || HiddenVisibilityRules.IsHiddenFor(_controller))
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
            // The pill's size differs between the local "You" pill and a rival's, and eases during GET READY, so re-read it.
            PlaceStack();
        }

        /// <summary>Top edge of <paramref name="rows"/> shown rows in the stack root's own space: the bottom row's centre is the
        /// root origin, each further row one spacing up, plus half a glyph.</summary>
        public static float StackTopInRoot(int rows) =>
            FeedbackTuning.StatusBadgeRowSpacing * Mathf.Max(0, rows - 1) + FeedbackTuning.StatusBadgeGlyphSize * 0.5f;

        // ---- Pure rules (public static so the EditMode tests drive them without a runner) ----------

        /// <summary>The state sprite a badge draws, or <c>false</c> when the badge has none and stays text (Root).</summary>
        public static bool TryGlyphFor(Badge badge, out StateGlyphStyle.StateGlyph glyph)
        {
            switch (badge)
            {
                case Badge.Stun:   glyph = StateGlyphStyle.StateGlyph.Stunned;    return true;
                case Badge.Slow:   glyph = StateGlyphStyle.StateGlyph.Slowed;     return true;
                case Badge.Waddle: glyph = StateGlyphStyle.StateGlyph.ShieldWalk; return true;
                case Badge.Egg:    glyph = StateGlyphStyle.StateGlyph.ShieldEgg;  return true;
                case Badge.Hidden: glyph = StateGlyphStyle.StateGlyph.Hidden;     return true;
                case Badge.Speed:  glyph = StateGlyphStyle.StateGlyph.Speeding;   return true;
                default:           glyph = default;                               return false;
            }
        }

        /// <summary>
        /// The HIDDEN row is the owner's own cue (spec §5, "self only"): drawn when this is the input-authority
        /// bird and its replicated <c>VisualOpacity</c> reads as faded. A remote peer never builds it (and
        /// never sees the bird's badges at all, A1).
        /// </summary>
        public static bool ShowHiddenRow(bool isLocal, float visualOpacity) =>
            isLocal && ControlStateVFX.IsFadedOpacity(visualOpacity);

        /// <summary>
        /// Fills <paramref name="into"/> with the live badges in stacking priority (spec §5: stunned, then egg,
        /// waddle, hidden, then the legacy root, slow, and zoom last) and returns how many fit; extra states
        /// beyond <c>into.Length</c> (the row cap) are dropped from the bottom of the priority.
        /// </summary>
        public static int Collect(bool stunned, bool rooted, bool slowed, bool shellActive, bool controlImmune,
                                  bool hiddenRow, bool speeding, Badge[] into)
        {
            int n = 0;
            void Add(bool on, Badge b) { if (on && n < into.Length) into[n++] = b; }

            Add(stunned, Badge.Stun);
            Add(shellActive && controlImmune, Badge.Egg);
            Add(shellActive && !controlImmune, Badge.Waddle);
            Add(hiddenRow, Badge.Hidden);
            Add(rooted, Badge.Root);
            Add(slowed, Badge.Slow);
            Add(speeding, Badge.Speed);
            return n;
        }

        /// <summary>
        /// Seconds the Shield's current phase has left, from the slot run's remaining time: the egg runs to the
        /// end of the slot, the walk ends when <paramref name="eggSeconds"/> remain (<see cref="ShieldRules"/>).
        /// </summary>
        public static float ShellPhaseSeconds(bool egg, float runRemaining, float eggSeconds) =>
            egg ? Mathf.Max(0f, runRemaining) : Mathf.Max(0f, runRemaining - eggSeconds);

        /// <summary>Centres a glyph + text pair on the row origin: x of the glyph centre and of the text's left edge.</summary>
        public static void RowLayout(float glyphWidth, float gap, float textWidth, out float glyphX, out float textX)
        {
            float g = Mathf.Max(0f, glyphWidth);
            float t = Mathf.Max(0f, textWidth);
            float sep = g > 0f && t > 0f ? gap : 0f;
            float left = -(g + sep + t) * 0.5f;
            glyphX = left + g * 0.5f;
            textX  = left + g + sep;
        }

        /// <summary>
        /// Fills <see cref="_active"/>/<see cref="_value"/> in priority order and returns how many statuses are
        /// live. Presence always comes from replicated state so a rival's badge is correct on every peer
        /// (case 21); the *quantity* may legitimately be unavailable, in which case the row renders as a bare
        /// glyph or word rather than a wrong number.
        ///
        /// A *removed* chicken (<c>ChickenCombat.IsRemoved</c>) deliberately gets no stun
        /// badge: removal runs on its own respawn clock, not the control-stun timer, and
        /// it is already told unambiguously by the nameplate's skull and the §3.4 execute
        /// treatment.
        /// </summary>
        private int CollectActive()
        {
            var flags = _controller.ControlFlags;

            bool shell = false, speeding = false;
            float shellRemaining = 0f, eggSeconds = 0f;
            ReadAbilityState(ref shell, ref speeding, ref shellRemaining, ref eggSeconds);
            bool immune = _controller.IsControlImmune;

            int n = Collect((flags & ControlVfx.Stunned) != 0, (flags & ControlVfx.Rooted) != 0,
                            (flags & ControlVfx.Slowed) != 0, shell, immune,
                            ShowHiddenRow(_controller.HasInputAuthority, _controller.VisualOpacity), speeding, _active);

            for (int i = 0; i < n; i++)
            {
                switch (_active[i])
                {
                    case Badge.Stun: _value[i] = _controller.StunRemaining; break;
                    case Badge.Root: _value[i] = _controller.RootRemaining; break;
                    case Badge.Slow:
                        // SlowMultiplier is recomputed StateAuthority-side every tick and reads as a flat 1 on
                        // every other peer. Rather than render a wrong number on a chicken that is visibly
                        // slowed, a peer that cannot know the magnitude passes 0 and gets the bare glyph. Slow
                        // also has no deadline (see ChickenController.SlowMultiplier's remarks).
                        float m = _controller.SlowMultiplier;
                        _value[i] = m < SlowMagnitudeEpsilon ? m : 0f;
                        break;
                    case Badge.Waddle:
                    case Badge.Egg:
                        _value[i] = ShellPhaseSeconds(_active[i] == Badge.Egg, shellRemaining, eggSeconds);
                        break;
                    default: _value[i] = 0f; break;
                }
            }
            return n;
        }

        /// <summary>Reads the Shield and Speed slots off the replicated active mask / timers. The Shield's
        /// remaining run and egg length come from the slot timer and the ability asset, nothing hard-coded.</summary>
        private void ReadAbilityState(ref bool shell, ref bool speeding, ref float shellRemaining, ref float eggSeconds)
        {
            if (_abilities == null) return;
            var abilitiesObj = _abilities.Object;
            if (abilitiesObj == null || !abilitiesObj.IsValid) return;

            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                if (!_abilities.IsSlotActive(i)) continue;
                var ability = _abilities.GetSlot(i);
                if (ability is SpeedBurstAbilitySO) speeding = true;
                else if (ability is TurtleModeAbilitySO turtle)
                {
                    shell = true;
                    eggSeconds = turtle.EggSeconds;
                    shellRemaining = _abilities.ActiveRemaining01For(i) * turtle.ResolveActiveDuration(_controller);
                }
            }
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
        /// when the displayed tenth changes (<see cref="RowNeedsWrite"/>): at most 10 string rebuilds a second per row, never
        /// per frame, and the row never lags the hex digit by a gate interval.
        /// </summary>
        private void LayoutAndFill(int count)
        {
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
                if (RowNeedsWrite(badgeChanged, _rowQuantity[i], quantity))
                {
                    _rowBadge[i]    = badge;
                    _rowQuantity[i] = quantity;
                    ApplyRow(i, badge, quantity);
                }

                if (!rowGo.activeSelf) rowGo.SetActive(true);
                LayoutRow(i);
            }
        }

        /// <summary>A row's text is rebuilt when its badge changed or the displayed (quantised) value moved: compares ints, so
        /// the unchanged frames allocate nothing.</summary>
        public static bool RowNeedsWrite(bool badgeChanged, int shownQuantity, int quantity) =>
            badgeChanged || shownQuantity != quantity;

        /// <summary>Writes one row: the sprite (or, with none wired, the text fallback glyph) and the text.</summary>
        private void ApplyRow(int i, Badge badge, int quantity)
        {
            Sprite sprite = SpriteFor(badge);
            string text = Compose(badge, quantity);

            if (sprite == null)
            {
                string fallback = FallbackGlyph(badge);
                if (fallback.Length > 0)
                {
                    text = text.Length > 0 ? fallback + " " + text : fallback;
                    WarnMissingSprite(badge);
                }
            }

            _rowHasGlyph[i] = sprite != null;
            var sr = _glyphs[i];
            sr.sprite  = sprite;
            sr.enabled = sprite != null;
            if (sprite != null)
            {
                float h = sprite.bounds.size.y;
                float s = h > 1e-4f ? FeedbackTuning.StatusBadgeGlyphSize / h : 1f;
                sr.transform.localScale = new Vector3(s, s, 1f);
            }

            _rows[i].text  = text;
            _rows[i].color = ColorFor(badge);
        }

        /// <summary>Centres the glyph + text pair on the row. The text width is the renderer's mesh bounds, which
        /// settles one frame after a text write; a row's first frame is a one-frame approximation.</summary>
        private void LayoutRow(int i)
        {
            float glyphW = _rowHasGlyph[i] ? FeedbackTuning.StatusBadgeGlyphSize : 0f;
            var mr = _rowRenderers[i];
            float textW = mr != null && _rows[i].text.Length > 0 ? mr.localBounds.size.x : 0f;
            RowLayout(glyphW, FeedbackTuning.StatusBadgeGlyphGap, textW, out float gx, out float tx);
            _glyphs[i].transform.localPosition = new Vector3(gx, 0f, 0f);
            _rows[i].transform.localPosition = new Vector3(tx, 0f, 0f);
        }

        /// <summary>The wired sprite for a state glyph, or null when that field is unwired or the state has no field.
        /// The nameplate reads the slow glyph through this, so there is one wiring point.</summary>
        public Sprite SpriteFor(StateGlyphStyle.StateGlyph glyph)
        {
            switch (glyph)
            {
                case StateGlyphStyle.StateGlyph.Stunned:    return _glyphStunned;
                case StateGlyphStyle.StateGlyph.Slowed:     return _glyphSlowed;
                case StateGlyphStyle.StateGlyph.ShieldWalk: return _glyphShieldWalk;
                case StateGlyphStyle.StateGlyph.ShieldEgg:  return _glyphShieldEgg;
                case StateGlyphStyle.StateGlyph.Hidden:     return _glyphHidden;
                case StateGlyphStyle.StateGlyph.Speeding:   return _glyphSpeeding;
                default:                                    return null;
            }
        }

        private Sprite SpriteFor(Badge badge) =>
            TryGlyphFor(badge, out var glyph) ? SpriteFor(glyph) : null;

        /// <summary>Text glyph drawn only when the badge's sprite field is null. Empty when the badge has none
        /// (Root prints its own text glyph in <see cref="Compose"/>; the new states are covered by their word).</summary>
        private static string FallbackGlyph(Badge badge)
        {
            switch (badge)
            {
                case Badge.Stun: return StunGlyph;
                case Badge.Slow: return SlowGlyph;
                default:         return string.Empty;
            }
        }

        private void WarnMissingSprite(Badge badge)
        {
            int k = (int)badge;
            if (_warnedMissingSprite[k]) return;
            _warnedMissingSprite[k] = true;

            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            _log?.Warn(Source, $"No state glyph sprite wired for the {badge} badge on {name}; drawing the text fallback. " +
                               "Re-wire the _glyph* fields on Chicken.prefab.");
        }

        /// <summary>Reduces a raw value to the integer the label actually shows, so the
        /// string is only rebuilt when a visible digit moves. Tenths of a second for the
        /// timed states, hundredths of a multiplier for slow.</summary>
        public static int Quantize(Badge badge, float value)
        {
            if (value <= 0f) return 0;
            return badge == Badge.Slow ? 1 : Mathf.CeilToInt(value * 10f);
        }

        /// <summary>The row text without a glyph: a countdown, a word, both, or nothing. Root keeps its text
        /// glyph (it has no sprite).</summary>
        private static string Compose(Badge badge, int quantity)
        {
            string secs = quantity > 0 ? CluckWars.UI.HudFeedbackStyle.Seconds(quantity) : string.Empty;
            switch (badge)
            {
                // E2 (spec section 12): the row owns the countdown, word first like EGG 1.0s; pops are words only.
                case Badge.Stun:
                    return WordWithCountdown(UiText.Get(UiKeys.HudStatusStun), secs);
                case Badge.Root:
                    return RootGlyph + " " + WordWithCountdown(UiText.Get(UiKeys.HudStatusStuck), secs);
                case Badge.Slow:
                    // Icon only: slow has no deadline and a x0.45 means nothing to a kid (Phase 7 A12).
                    return string.Empty;
                case Badge.Waddle: return WordWithCountdown(UiText.Get(UiKeys.HudBadgeWaddle), secs);
                case Badge.Egg:    return WordWithCountdown(UiText.Get(UiKeys.HudBadgeEgg), secs);
                case Badge.Hidden: return UiText.Get(UiKeys.HudBadgeHidden);
                case Badge.Speed:  return UiText.Get(UiKeys.HudBadgeFast);
                default:
                    return string.Empty;
            }
        }

        private static string WordWithCountdown(string word, string secs) =>
            secs.Length > 0 ? word + " " + secs : word;

        private static Color ColorFor(Badge badge)
        {
            switch (badge)
            {
                case Badge.Stun:   return FeedbackTuning.CanonicalStunColor;
                case Badge.Root:   return FeedbackTuning.CanonicalRootColor;
                case Badge.Slow:   return FeedbackTuning.CanonicalSlowColor;
                case Badge.Waddle:
                case Badge.Egg:    return FeedbackPalette.Current.Accent(AccentKey.EggShell);
                case Badge.Hidden: return FeedbackPalette.Current.Accent(AccentKey.Hide);
                case Badge.Speed:  return FeedbackPalette.Current.Accent(AccentKey.Speed);
                default:           return Color.white;
            }
        }

        private void HideStack()
        {
            _lastActiveMask = 0;
            _punchTimer     = -1f;
            if (_stackRoot.gameObject.activeSelf) _stackRoot.gameObject.SetActive(false);
        }

        /// <summary>Billboards the whole stack once, not once per row. Identical to
        /// <c>ChickenNameplate</c>'s own billboard.</summary>
        private void BillboardToCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var lookDir = BillboardFacing.Direction(cam, _stackRoot.position);
            if (lookDir.sqrMagnitude > 1e-6f)
                _stackRoot.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
        }
    }
}
