using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pooled world-space floating combat text — the numeric half of the Impact beat
    /// (FEEDBACK.md §3.2, the <c>FloatingTextService</c> of §8, budgeted by §10).
    /// Spawns short strings above a chicken's head that rise and fade:
    /// <c>-5 🌽</c> / <c>+5 🌽</c> for cargo changing hands, <c>STUN 1.5s</c> /
    /// <c>ROOT 2.0s</c> / <c>SLOW 45%</c> for control, <c>IMMUNE</c> for a no-op hit.
    /// </summary>
    /// <remarks>
    /// <b>One pool per peer, never grown.</b> <see cref="FeedbackTuning.FloatingTextLivePeerCap"/>
    /// popups are built once and reused forever. When every slot is live, the request
    /// <i>recycles the oldest</i> rather than dropping the newest or allocating a 13th:
    /// in a 4-player scrum a fresh number is always worth more than one that is already
    /// most of the way through its fade, and the alternative — silently dropping the new
    /// text — is exactly the "an event happened and nothing told me" failure the whole
    /// feedback pass exists to kill.
    ///
    /// <b>TextMesh, not UGUI.</b> Matches <see cref="ChickenNameplate"/> and
    /// <see cref="ChickenWorldBars"/>, which already bill­board a <c>TextMesh</c> at the
    /// camera. A world-space Canvas would add a second UI batch and a layout pass per
    /// popup for no gain — §10's budget is 30 fps on a 2021 mid-range Android.
    ///
    /// <b>Never throws.</b> <see cref="SpawnUngated"/> is safe to call from anywhere, including
    /// EditMode tests and scenes with no pool: outside play mode it returns silently
    /// instead of creating a GameObject, and in play mode it lazily creates the single
    /// pool object on first use. Nothing needs to be wired in the Inspector.
    ///
    /// Purely local. Nothing here reads or writes networked state; callers derive the
    /// numbers from already-replicated properties (per the project's "Animation + VFX are
    /// local" rule).
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FloatingCombatText : MonoBehaviour
    {
        /// <summary>
        /// Corn glyph used by the cargo popups (§3.2's <c>-5 🌽</c>). Kept as one named
        /// constant because the built-in <c>TextMesh</c> font falls back to a system font
        /// for emoji: <see cref="ChickenNameplate"/> already ships ☠/🌱/🐌 this way, but if
        /// a target device tofus this glyph, blanking it here fixes every cargo popup at
        /// once instead of hunting call sites.
        /// </summary>
        public const string CargoGlyph = "🌽";

        // Text metrics chosen to sit between the nameplate (fontSize 96 / charSize 0.05)
        // and the cargo readout (48 / 0.025): a combat number must out-read the cargo
        // count without competing with the player's own identity label.
        private const int   PopupFontSize      = 72;
        /// <summary>Bare ±n cargo numbers (rivals' cargo change, <see cref="SpawnCargoDelta"/>).</summary>
        public const float PopupCharacterSize = 0.032f;
        /// <summary>Every catalog word pop (STUN, PUSH, SLOW, ROB, EGG!, IMMUNE, HUNT!, …) and the own-screen ROB/+n self-pop; ~1.8x the number size: sized so the word STUN is >= 30 px tall on a 2400x1080 phone (F6; 0.048 measured 25 px).</summary>
        public const float WordCharacterSize = 0.058f;

        /// <summary>Above <see cref="ChickenNameplate"/>'s renderer so a number never
        /// disappears behind a name it is spawned on top of.</summary>
        private const int PopupSortingOrder = 210;

        /// <summary>The local bird's own pops (E9c): one above a rival pop so they print on top where they overlap.</summary>
        private const int LocalPopupSortingOrder = PopupSortingOrder + 1;

        /// <summary>The order a pop draws at: <see cref="LocalPopupSortingOrder"/> for the owner's own pops, else <see cref="PopupSortingOrder"/>.</summary>
        public static int PopupOrderFor(bool isLocal) => isLocal ? LocalPopupSortingOrder : PopupSortingOrder;

        private sealed class Popup
        {
            public Transform Root;
            public TextMesh  Text;
            public Vector3   Origin;
            /// <summary>Owner's transform for a self-pop (it tracks the bird); null for a world-fixed pop, and cleared on reuse.</summary>
            public Transform Follow;
            /// <summary>Spawn point relative to <see cref="Follow"/>'s position (the pop height, clear of the badge rows at spawn).</summary>
            public Vector3   FollowOffset;
            public Color     BaseColor;
            public float     Age;
            public bool      Live;
        }

        private static FloatingCombatText _instance;

        private Popup[] _pool;
        private int     _liveCount;
        private readonly List<Vector3> _liveOrigins = new List<Vector3>(16);

        /// <summary>Live popups on this peer right now. Read-only; exists for the debug
        /// HUD and for tests that want to assert the cap is respected.</summary>
        public static int LiveCount => _instance != null ? _instance._liveCount : 0;

        /// <summary>Hard cap this pool was built to, mirroring §10's budget.</summary>
        public static int Capacity => Mathf.Max(1, FeedbackTuning.FloatingTextLivePeerCap);

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Shows <paramref name="text"/> at <paramref name="worldPos"/>, rising
        /// <see cref="FeedbackTuning.FloatingTextRiseDistance"/> over
        /// <see cref="FeedbackTuning.FloatingTextLifetimeSeconds"/> — fully opaque for the
        /// first <see cref="FeedbackTuning.FloatingTextHoldFraction"/> of that, then fading
        /// over the remaining <see cref="FeedbackTuning.FloatingTextFadeFraction"/>.
        /// Silently does nothing for null/empty text or outside play mode; never throws.
        /// </summary>
        public static void SpawnUngated(Vector3 worldPos, string text, Color color) =>
            SpawnUngated(worldPos, text, color, null);

        /// <summary>
        /// As <see cref="SpawnUngated(Vector3,string,Color)"/>, but a non-null <paramref name="follow"/> makes the pop track that
        /// transform: it keeps its spawn offset from it for its whole life (plus the rise), so it stays over a moving bird. If
        /// <paramref name="follow"/> is destroyed mid-pop the pop freezes at its last position. Null: world-fixed.
        /// </summary>
        public static void SpawnUngated(Vector3 worldPos, string text, Color color, Transform follow) =>
            SpawnSized(worldPos, text, color, follow, WordCharacterSize);

        /// <summary>Shared body of every spawn: <paramref name="characterSize"/> is written on each spawn, so a recycled slot never keeps the previous pop's size.</summary>
        private static void SpawnSized(Vector3 worldPos, string text, Color color, Transform follow, float characterSize, bool isLocal = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var pool = EnsureInstance();
            if (pool == null) return;
            pool.SpawnInternal(worldPos, text, color, follow, characterSize, isLocal);
        }

        /// <summary>
        /// Where a pop that follows an owner sits this frame: the owner's position plus its spawn offset, or
        /// <paramref name="lastOrigin"/> once the owner is gone (a destroyed Unity object reads null), so a bird removed mid-pop
        /// leaves its text frozen where it was, with no exception and no log.
        /// </summary>
        public static Vector3 FollowedOrigin(Transform follow, Vector3 offset, Vector3 lastOrigin) =>
            follow != null ? follow.position + offset : lastOrigin;

        /// <summary>
        /// Pure form of the hidden-bird gate (D9, spec 2026-11-06 section 3.2): text AT a faded remote bird is never drawn (it would
        /// mark where the hidden bird is); the faded local bird's own screen keeps its text. Same predicate as every other tell
        /// (<see cref="HiddenVisibilityRules"/>).
        /// </summary>
        public static bool IsSuppressed(float visualOpacity, bool isLocal) => HiddenVisibilityRules.AlphaFor(visualOpacity, isLocal) <= 0f;

        /// <summary>True when text over <paramref name="at"/> must not be drawn on this peer (a faded remote bird).</summary>
        public static bool IsSuppressedAt(ChickenController at) => HiddenVisibilityRules.IsHiddenFor(at);

        /// <summary>
        /// THE spawn for any text drawn at a chicken (PUSH, STUN / SLOW digits, IMMUNE / SAFE, HUNT!, cargo text): shows
        /// <paramref name="text"/> unless <paramref name="at"/> is a faded remote bird (<see cref="IsSuppressedAt"/>). Returns whether it
        /// was spawned. The raw <see cref="SpawnUngated"/> is for the pop helpers below, which gate themselves.
        /// </summary>
        public static bool Spawn(ChickenController at, Vector3 worldPos, string text, Color color) =>
            Spawn(at, worldPos, text, color, null);

        /// <summary>
        /// As <see cref="Spawn(ChickenController,Vector3,string,Color)"/>, but a non-null <paramref name="follow"/> makes the pop
        /// ride on that transform (see <see cref="SpawnUngated(Vector3,string,Color,Transform)"/>). Only PUSH passes one (I6): the
        /// shoved bird slides away from the impact point; every other victim pop stays world-fixed.
        /// </summary>
        public static bool Spawn(ChickenController at, Vector3 worldPos, string text, Color color, Transform follow)
        {
            if (IsSuppressedAt(at)) return false;
            SpawnUngated(worldPos, text, color, follow);
            return true;
        }

        /// <summary>
        /// §3.2's cargo popup: <c>-5 🌽</c> in <see cref="FeedbackTuning.FloatingTextCargoLossColor"/>
        /// at the victim, <c>+5 🌽</c> in <see cref="FeedbackTuning.FloatingTextCargoGainColor"/>
        /// at the thief. The amount is floored to a whole cob because that is the unit the
        /// player counts in (<see cref="ChickenWorldBars"/> floors it the same way) — a
        /// <c>-4.87 🌽</c> is not a number anyone reads mid-fight.
        /// </summary>
        public static void SpawnCargoDelta(Vector3 worldPos, float delta)
        {
            int whole = Mathf.RoundToInt(Mathf.Abs(delta));
            if (whole <= 0) return;

            bool gain = delta > 0f;
            SpawnSized(worldPos,
                  (gain ? "+" : "-") + whole + " " + CargoGlyph,
                  gain ? FeedbackTuning.FloatingTextCargoGainColor
                       : FeedbackTuning.FloatingTextCargoLossColor,
                  null, PopupCharacterSize);
        }

        /// <summary>
        /// The self-pop rule (spec 2026-11-06 section 12 D7b): a self-pop is drawn only on the peer that
        /// owns the bird, never for rivals, and never over a decoy (a decoy shares its caster's input
        /// authority, so the authority test alone would let it pop on the caster's screen).
        /// </summary>
        public static bool ShouldSpawnSelfPop(bool hasInputAuthority, bool isDecoy) => hasInputAuthority && !isDecoy;

        /// <summary>True when <paramref name="controller"/> is a valid, non-decoy bird this peer owns.</summary>
        public static bool IsSelfPopTarget(ChickenController controller)
        {
            if (controller == null) return false;
            var obj = controller.Object;
            return obj != null && obj.IsValid && ShouldSpawnSelfPop(controller.HasInputAuthority, controller.IsDecoy);
        }

        /// <summary>
        /// A catalog word (<paramref name="key"/>) over <paramref name="controller"/>'s head, on its owner's
        /// screen only (see <see cref="ShouldSpawnSelfPop"/>). Returns whether it was spawned. Format the
        /// args only here, on the event, never per frame.
        /// </summary>
        public static bool SpawnSelfPop(ChickenController controller, string key, Color color,
                                        params (string name, object value)[] args) =>
            SpawnSelfPop(controller, key, color, ChickenStatusBadges.Badge.None, args);

        /// <summary>
        /// <see cref="SpawnSelfPop(ChickenController,string,Color,(string name, object value)[])"/> for a state-entry pop: the
        /// spawn height clears the badge row <paramref name="entering"/> that appears on this same frame
        /// (<see cref="ChickenPresentationAccess.PopAnchorEntering"/>). The pop follows the bird (see <see cref="SpawnUngated(Vector3,string,Color,Transform)"/>).
        /// </summary>
        public static bool SpawnSelfPop(ChickenController controller, string key, Color color,
                                        ChickenStatusBadges.Badge entering,
                                        params (string name, object value)[] args)
        {
            if (!IsSelfPopTarget(controller)) return false;
            string text = args == null || args.Length == 0 ? UiText.Get(key) : UiText.Format(key, args);
            SpawnSized(controller.PopAnchorEntering(entering), text, color, controller.transform, WordCharacterSize, isLocal: true);
            return true;
        }

        /// <summary>Pivot-local height a pop starts at: <see cref="FeedbackTuning.FloatingTextSpawnHeight"/> as the floor, or
        /// <see cref="FeedbackTuning.FloatingTextStackClearance"/> above <paramref name="stackTopLocalY"/>
        /// (<c>ChickenStatusBadges.PopClearHeight</c>, the screen-level top of the shown rows) when they reach higher.</summary>
        public static float PopSpawnHeight(float stackTopLocalY) =>
            Mathf.Max(FeedbackTuning.FloatingTextSpawnHeight, stackTopLocalY + FeedbackTuning.FloatingTextStackClearance);

        /// <summary>Where a pop over a bird at <paramref name="pivot"/> whose badge stack tops out at
        /// <paramref name="stackTopLocalY"/> starts: <see cref="PopSpawnHeight"/> above the pivot, clear of every shown row.</summary>
        public static Vector3 PopSpawnPosition(Vector3 pivot, float stackTopLocalY) =>
            pivot + Vector3.up * PopSpawnHeight(stackTopLocalY);

        /// <summary>
        /// The cargo popup for <paramref name="controller"/>'s change of <paramref name="delta"/>: on the
        /// owner's own screen it is the catalog word (<c>ROB -n</c> / <c>+n</c>, D7b), everywhere else the
        /// unchanged <see cref="SpawnCargoDelta"/>. Exactly one pop per event on any peer.
        /// </summary>
        public static void SpawnCargoPop(ChickenController controller, Vector3 worldPos, float delta)
        {
            int whole = Mathf.RoundToInt(Mathf.Abs(delta));
            if (whole <= 0) return;
            if (IsSuppressedAt(controller)) return;   // D9: no +n / -n drawn AT a faded remote bird

            bool gain = delta > 0f;
            if (SpawnSelfPop(controller,
                             gain ? UiKeys.HudPopGrabbed : UiKeys.HudPopRobbed,
                             gain ? FeedbackTuning.FloatingTextCargoGainColor : FeedbackTuning.FloatingTextCargoLossColor,
                             ("n", whole)))
                return;

            SpawnCargoDelta(worldPos, delta);
        }

        // ── Stacking (BL-232) ──────────────────────────────────────────────────

        /// <summary>
        /// How many live pops sit within <see cref="FeedbackTuning.FloatingTextStackRadius"/> (horizontally) of <paramref name="at"/>: the pops a new one
        /// there would print over. Pure: <paramref name="liveOrigins"/> are the live pops' current points.
        /// </summary>
        public static int NearbyLive(Vector3 at, IReadOnlyList<Vector3> liveOrigins)
        {
            if (liveOrigins == null) return 0;
            float r2 = FeedbackTuning.FloatingTextStackRadius * FeedbackTuning.FloatingTextStackRadius;
            int n = 0;
            for (int i = 0; i < liveOrigins.Count; i++)
            {
                float dx = liveOrigins[i].x - at.x, dz = liveOrigins[i].z - at.z;
                if (dx * dx + dz * dz <= r2) n++;
            }
            return n;
        }

        /// <summary>Extra start height for a pop with <paramref name="nearbyLive"/> live pops already at its bird: one
        /// <see cref="FeedbackTuning.FloatingTextStackStep"/> each, up to <see cref="FeedbackTuning.FloatingTextStackMax"/> steps. It falls back by itself as the older pops expire.</summary>
        public static float StackOffset(int nearbyLive) =>
            Mathf.Clamp(nearbyLive, 0, Mathf.Max(0, FeedbackTuning.FloatingTextStackMax)) * FeedbackTuning.FloatingTextStackStep;

        /// <summary>Where a pop asked for at <paramref name="worldPos"/> actually starts, given the live pops' points: lifted by <see cref="StackOffset"/> so it never prints over the pops already there.</summary>
        public static Vector3 StackedSpawnPosition(Vector3 worldPos, IReadOnlyList<Vector3> liveOrigins) =>
            worldPos + Vector3.up * StackOffset(NearbyLive(worldPos, liveOrigins));

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // A scene-authored pool and a lazily-created one can never both be needed.
                Destroy(gameObject);
                return;
            }
            _instance = this;
            BuildPool();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Clears the static handle between Editor play sessions — "Enter Play Mode
        /// Options" can suppress the domain reload that would otherwise reset it, leaving
        /// <see cref="_instance"/> pointing at a destroyed pool from the previous run.
        /// Same guard <see cref="AbilityTelegraph"/> uses.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private static FloatingCombatText EnsureInstance()
        {
            // Unity's == overload treats a destroyed object as null, so a pool lost to a
            // scene load transparently rebuilds on the next Spawn.
            if (_instance != null) return _instance;

            // EditMode tests and edit-time tooling must never get a stray GameObject.
            if (!Application.isPlaying) return null;

            var go = new GameObject("[FloatingCombatText]");
            return go.AddComponent<FloatingCombatText>();
        }

        private void BuildPool()
        {
            int capacity = Capacity;
            _pool = new Popup[capacity];
            for (int i = 0; i < capacity; i++) _pool[i] = BuildPopup(i);
            _liveCount = 0;
        }

        private Popup BuildPopup(int index)
        {
            var go = new GameObject("Popup" + index);
            go.transform.SetParent(transform, worldPositionStays: false);

            var text = go.AddComponent<TextMesh>();
            text.anchor        = TextAnchor.MiddleCenter;
            text.alignment     = TextAlignment.Center;
            text.fontSize      = PopupFontSize;
            text.characterSize = PopupCharacterSize;
            text.fontStyle     = FontStyle.Bold;
            text.text          = string.Empty;
            text.color         = Color.white;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows    = false;
                mr.sortingOrder      = PopupSortingOrder;
            }

            go.SetActive(false);
            return new Popup { Root = go.transform, Text = text };
        }

        // ── Spawn / recycle ────────────────────────────────────────────────────

        private void SpawnInternal(Vector3 worldPos, string text, Color color, Transform follow, float characterSize, bool isLocal)
        {
            if (_pool == null) BuildPool();

            // One shared place for every kit: a pop over a bird that already shows live pops starts higher than they did (BL-232).
            _liveOrigins.Clear();
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i].Live) _liveOrigins.Add(_pool[i].Origin);
            worldPos = StackedSpawnPosition(worldPos, _liveOrigins);

            var popup = TakeFreeSlot() ?? TakeOldestLiveSlot();
            if (popup == null) return; // capacity 0 — impossible via Capacity's Max(1,…)

            popup.Origin       = worldPos;
            popup.Follow       = follow;   // always overwritten, so a recycled slot never keeps its previous owner
            popup.FollowOffset = follow != null ? worldPos - follow.position : Vector3.zero;
            popup.BaseColor    = color;
            popup.Age       = 0f;

            popup.Text.text          = text;
            popup.Text.color         = color;
            popup.Text.characterSize = characterSize;   // always overwritten, so a recycled slot never keeps a stale size
            var renderer = popup.Text.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sortingOrder = PopupOrderFor(isLocal);   // same: a recycled slot never keeps a stale order

            popup.Root.position = worldPos;
            if (!popup.Root.gameObject.activeSelf) popup.Root.gameObject.SetActive(true);

            if (!popup.Live)
            {
                popup.Live = true;
                _liveCount++;
            }
        }

        private Popup TakeFreeSlot()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Live) return _pool[i];
            }
            return null;
        }

        /// <summary>
        /// Pool exhausted: reuse whichever popup is furthest through its life. Deliberately
        /// NOT "drop the new one" — the newest number is the one describing what just
        /// happened, and the oldest is already mid-fade, so recycling it is the cheapest
        /// possible information loss.
        /// </summary>
        private Popup TakeOldestLiveSlot()
        {
            Popup oldest = null;
            float bestAge = -1f;
            for (int i = 0; i < _pool.Length; i++)
            {
                var p = _pool[i];
                if (!p.Live) continue;
                if (p.Age > bestAge) { bestAge = p.Age; oldest = p; }
            }
            return oldest ?? (_pool.Length > 0 ? _pool[0] : null);
        }

        // ── Per-frame animation ────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_pool == null || _liveCount == 0) return;

            float life = Mathf.Max(0.01f, FeedbackTuning.FloatingTextLifetimeSeconds);
            float hold = Mathf.Clamp01(FeedbackTuning.FloatingTextHoldFraction);
            float fade = Mathf.Max(0.0001f, FeedbackTuning.FloatingTextFadeFraction);
            float rise = FeedbackTuning.FloatingTextRiseDistance;

            // One Camera.main resolve per frame, shared by every live popup, instead of
            // one per popup like the per-chicken billboards do.
            var cam = Camera.main;
            float dt = Time.deltaTime;

            for (int i = 0; i < _pool.Length; i++)
            {
                var p = _pool[i];
                if (!p.Live) continue;

                p.Age += dt;
                float t = p.Age / life;
                if (t >= 1f)
                {
                    p.Live = false;
                    _liveCount--;
                    if (p.Root.gameObject.activeSelf) p.Root.gameObject.SetActive(false);
                    continue;
                }

                // World-fixed pops (no Follow) and pops whose owner was destroyed both keep their Origin.
                p.Origin = FollowedOrigin(p.Follow, p.FollowOffset, p.Origin);
                p.Root.position = p.Origin + Vector3.up * (rise * t);

                float alpha = t <= hold ? 1f : 1f - Mathf.Clamp01((t - hold) / fade);
                var c = p.BaseColor;
                c.a *= alpha;
                p.Text.color = c;

                if (cam != null)
                {
                    var dir = BillboardFacing.Direction(cam, p.Root.position);
                    if (dir.sqrMagnitude > 1e-6f)
                        p.Root.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
        }
    }
}
