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
    /// <b>Never throws.</b> <see cref="Spawn"/> is safe to call from anywhere, including
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
        private const float PopupCharacterSize = 0.032f;

        /// <summary>Above <see cref="ChickenNameplate"/>'s renderer so a number never
        /// disappears behind a name it is spawned on top of.</summary>
        private const int PopupSortingOrder = 210;

        private sealed class Popup
        {
            public Transform Root;
            public TextMesh  Text;
            public Vector3   Origin;
            public Color     BaseColor;
            public float     Age;
            public bool      Live;
        }

        private static FloatingCombatText _instance;

        private Popup[] _pool;
        private int     _liveCount;

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
        public static void Spawn(Vector3 worldPos, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            var pool = EnsureInstance();
            if (pool == null) return;
            pool.SpawnInternal(worldPos, text, color);
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
            Spawn(worldPos,
                  (gain ? "+" : "-") + whole + " " + CargoGlyph,
                  gain ? FeedbackTuning.FloatingTextCargoGainColor
                       : FeedbackTuning.FloatingTextCargoLossColor);
        }

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

        private void SpawnInternal(Vector3 worldPos, string text, Color color)
        {
            if (_pool == null) BuildPool();

            var popup = TakeFreeSlot() ?? TakeOldestLiveSlot();
            if (popup == null) return; // capacity 0 — impossible via Capacity's Max(1,…)

            popup.Origin    = worldPos;
            popup.BaseColor = color;
            popup.Age       = 0f;

            popup.Text.text  = text;
            popup.Text.color = color;

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
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
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

                p.Root.position = p.Origin + Vector3.up * (rise * t);

                float alpha = t <= hold ? 1f : 1f - Mathf.Clamp01((t - hold) / fade);
                var c = p.BaseColor;
                c.a *= alpha;
                p.Text.color = c;

                if (cam != null)
                {
                    var dir = p.Root.position - camPos;
                    if (dir.sqrMagnitude > 1e-6f)
                        p.Root.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
        }
    }
}
