using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The Steal beat's flying kernels (D7, vfx.md 2.1) and the zero-hit whiff arcs: pooled world-space <see cref="SpriteRenderer"/>
    /// movers on the floating-text pool pattern. One pool per peer, built once at
    /// <see cref="FeedbackTuning.StealChunkPoolSize"/> and never grown; when it is full the oldest live mover is recycled.
    /// No allocation per flight: the movers, the sprites and the vectors are all cached.
    ///
    /// <b>Local only.</b> Driven by <see cref="HitFeedback"/> (the victim's <c>StealEventId</c> edge and the caster's cast edge);
    /// nothing here is networked. Nothing is ever drawn toward or at a thief this peer must not see: a hidden or faded thief
    /// reroutes the kernels up above the victim, where they vanish (<see cref="StealBeatRules.ChunkRoute"/>). The movers are
    /// sprites, not particles: they do not count against the per-chicken particle caps, and are reported against the arena
    /// total by the probe.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FoodChunkFlight : MonoBehaviour
    {
        private enum Kind : byte { Kernel, Whiff }

        private sealed class Mover
        {
            public Transform Root;
            public SpriteRenderer Renderer;
            public bool Live;
            public Kind Kind;
            public float Age;          // seconds since Play (negative while waiting out its stagger)
            public float Life;
            public bool Reduced;
            public bool Arrives;       // the last kernel of a robbery: arrival sparkles
            public StealBeatRules.ChunkRoute Route;
            public Vector3 Start, End, Lateral;
            public ChickenController Thief;
            public ChickenVFX ThiefVfx;
            public HitFeedback ThiefFeedback;   // I5: the Arrives kernel only, on the ToThief route
            public float Roll;
            public float BaseScale;
            public Color Tint;
            public int Order;          // sorting order of the renderer
            public float ScaleStart, ScaleEnd, Alpha;   // whiff arcs only
        }

        private static FoodChunkFlight s_instance;
        private static Sprite s_cornSprite, s_whiffSprite;

        private Mover[] _pool;
        private float[] _ages;
        private int _live;

        /// <summary>Kernels and whiff arcs live right now (the probe reports this against the arena total).</summary>
        public static int LiveCount => s_instance != null ? s_instance._live : 0;

        /// <summary>Movers the pool holds (0 before first use).</summary>
        public static int PoolSize => s_instance != null && s_instance._pool != null ? s_instance._pool.Length : 0;

        /// <summary>The shared Fx_Corn sprite (registered by the first chicken to wake; null before that).</summary>
        public static Sprite CornSprite => s_cornSprite;

        /// <summary>Whether a pool exists on this peer.</summary>
        public static bool HasInstance => s_instance != null;

        /// <summary>Called by every <see cref="ChickenVFX"/> on Awake with the shared Fx sprites (the pool needs no scene wiring).</summary>
        public static void RegisterSprites(Sprite corn, Sprite whiff)
        {
            if (corn != null) s_cornSprite = corn;
            if (whiff != null) s_whiffSprite = whiff;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_instance = null; s_cornSprite = null; s_whiffSprite = null; }

        /// <summary>True when this peer must draw nothing at or from <paramref name="c"/> (hidden for this viewer, or faded).</summary>
        public static bool IsQuiet(ChickenController c) =>
            c == null || HiddenVisibilityRules.IsHiddenFor(c) || ControlStateVFX.IsFadedOpacity(c.VisualOpacity);

        // ---- Public API ---------------------------------------------------------------------------------

        /// <summary>
        /// The victim's half of a robbery: <see cref="FeedbackTuning.StealChunksPerVictim"/> kernels from <paramref name="victim"/>
        /// to <paramref name="thief"/> (or, with the thief hidden or faded, up above the victim). Nothing for a hidden victim.
        /// </summary>
        public static void PlaySteal(ChickenController victim, ChickenController thief)
        {
            if (victim == null || thief == null || s_cornSprite == null) return;
            if (!Application.isPlaying) return;
            if (IsQuiet(victim)) return;

            var pool = EnsureInstance();
            if (pool == null) return;

            bool reduced = PlayerPreferences_Reduced();
            var route = StealBeatRules.Route(IsQuiet(thief));
            Vector3 victimGround = victim.transform.position;
            Vector3 start = victimGround + Vector3.up * FeedbackTuning.StealChunkHeight;
            Vector3 end = StealBeatRules.ChunkEnd(route, victimGround, thief.transform.position);
            Vector3 flat = end - start; flat.y = 0f;
            Vector3 side = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            var gold = FeedbackPalette.Current.YolkColor;
            var thiefVfx = route == StealBeatRules.ChunkRoute.ToThief ? thief.GetComponent<ChickenVFX>() : null;
            var thiefFeedback = route == StealBeatRules.ChunkRoute.ToThief ? thief.GetComponent<HitFeedback>() : null;

            int n = FeedbackTuning.StealChunksPerVictim;
            for (int i = 0; i < n; i++)
            {
                float spread = n > 1 ? (i / (float)(n - 1)) * 2f - 1f : 0f;   // -1, 0, +1
                var m = pool.Take();
                if (m == null) return;
                m.Kind = Kind.Kernel;
                m.Reduced = reduced;
                m.Route = route;
                m.Start = start;
                m.End = end;
                m.Lateral = side * (spread * FeedbackTuning.StealChunkSpread);
                m.Thief = thief;
                m.ThiefVfx = thiefVfx;
                m.Order = FeedbackTuning.StealChunkSortingOrder;
                m.Arrives = i == n - 1;
                m.ThiefFeedback = m.Arrives ? thiefFeedback : null;
                m.Roll = i * 120f;
                m.Tint = gold;
                m.Life = StealBeatRules.ChunkLifeSeconds(reduced);
                m.Age = reduced ? 0f : -i * FeedbackTuning.StealChunkStaggerSeconds;
                m.BaseScale = FeedbackTuning.StealChunkSize / SpriteWidth(s_cornSprite);
                m.Renderer.sprite = s_cornSprite;
                pool.Activate(m);
                if (m.ThiefFeedback != null) m.ThiefFeedback.NotifyStealFlightLaunched();   // I5: the thief +n waits for this one
            }
        }

        /// <summary>The caster half of a zero-hit Steal: two grey whiff arcs at the beak (none on a quiet caster).</summary>
        public static void PlayWhiff(ChickenController caster, Vector3 beak, Vector3 forward)
        {
            if (caster == null || s_whiffSprite == null || !Application.isPlaying) return;
            if (IsQuiet(caster)) return;
            var pool = EnsureInstance();
            if (pool == null) return;

            bool reduced = PlayerPreferences_Reduced();
            Vector3 flat = forward; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            int n = FeedbackTuning.StealWhiffCount;
            for (int i = 0; i < n; i++)
            {
                float s = n > 1 ? (i / (float)(n - 1)) * 2f - 1f : 0f;
                var m = pool.Take();
                if (m == null) return;
                m.Kind = Kind.Whiff;
                m.Reduced = reduced;
                m.Route = StealBeatRules.ChunkRoute.ToThief;
                m.Start = beak + Quaternion.Euler(0f, s * FeedbackTuning.StealWhiffSpreadDegrees, 0f) * flat * 0.15f;
                m.End = m.Start;
                m.Lateral = Vector3.zero;
                m.Thief = null;
                m.ThiefVfx = null;
                m.ThiefFeedback = null;
                m.Arrives = false;
                m.Roll = s * FeedbackTuning.StealWhiffSpreadDegrees;
                m.Tint = FeedbackTuning.StealWhiffColor;
                m.Order = FeedbackTuning.StealChunkSortingOrder;
                m.ScaleStart = FeedbackTuning.StealWhiffScaleStart;
                m.ScaleEnd = FeedbackTuning.StealWhiffScaleEnd;
                m.Alpha = FeedbackTuning.StealWhiffAlpha;
                m.Life = FeedbackTuning.StealWhiffSeconds;
                m.Age = 0f;
                m.BaseScale = FeedbackTuning.StealWhiffSize / SpriteWidth(s_whiffSprite);
                m.Renderer.sprite = s_whiffSprite;
                pool.Activate(m);
            }
        }

        /// <summary>
        /// The caster half of a Wing Slam (D10): <see cref="FeedbackTuning.StunArcCount"/> arcs, one each side of the caster, in the
        /// Stun accent. Nothing on a quiet (hidden or faded) caster. The caller gates on <see cref="StunBeatRules.WhiffArcsApply"/>.
        /// </summary>
        public static void PlayStunArcs(ChickenController caster, Color accent)
        {
            if (caster == null || s_whiffSprite == null || !Application.isPlaying) return;
            if (IsQuiet(caster)) return;
            var pool = EnsureInstance();
            if (pool == null) return;

            bool reduced = PlayerPreferences_Reduced();
            Transform t = caster.transform;
            Vector3 right = t.right; right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            int n = FeedbackTuning.StunArcCount;
            for (int i = 0; i < n; i++)
            {
                float side = n > 1 ? (i / (float)(n - 1)) * 2f - 1f : 1f;   // -1 (left), +1 (right)
                var m = pool.Take();
                if (m == null) return;
                m.Kind = Kind.Whiff;
                m.Reduced = reduced;
                m.Route = StealBeatRules.ChunkRoute.ToThief;
                m.Start = t.position + right * (side * FeedbackTuning.StunArcSideMetres) + Vector3.up * FeedbackTuning.StunArcHeight;
                m.End = m.Start;
                m.Lateral = Vector3.zero;
                m.Thief = null;
                m.ThiefVfx = null;
                m.ThiefFeedback = null;
                m.Arrives = false;
                m.Roll = -side * FeedbackTuning.StunArcRollDegrees;
                m.Tint = new Color(accent.r, accent.g, accent.b, 1f);
                m.Order = FeedbackTuning.StunArcSortingOrder;
                m.ScaleStart = FeedbackTuning.StunArcScaleStart;
                m.ScaleEnd = FeedbackTuning.StunArcScaleEnd;
                m.Alpha = FeedbackTuning.StunArcAlpha;
                m.Life = FeedbackTuning.StunArcSeconds;
                m.Age = 0f;
                m.BaseScale = FeedbackTuning.StunArcSize / SpriteWidth(s_whiffSprite);
                m.Renderer.sprite = s_whiffSprite;
                pool.Activate(m);
            }
        }

        // ---- Pool ----------------------------------------------------------------------------------------

        private static bool PlayerPreferences_Reduced() => CluckWars.Settings.PlayerPreferences.ReducedMotionEnabled;

        private static float SpriteWidth(Sprite s) => Mathf.Max(0.001f, s.bounds.size.x);

        private static FoodChunkFlight EnsureInstance()
        {
            if (s_instance != null) return s_instance;
            if (!Application.isPlaying) return null;
            var go = new GameObject("[FoodChunkFlight]");
            return go.AddComponent<FoodChunkFlight>();
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            BuildPool();
        }

        private void OnDestroy() { if (s_instance == this) s_instance = null; }

        private void BuildPool()
        {
            int n = Mathf.Max(1, FeedbackTuning.StealChunkPoolSize);
            _pool = new Mover[n];
            _ages = new float[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Chunk" + i);
                go.transform.SetParent(transform, worldPositionStays: false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = FeedbackTuning.StealChunkSortingOrder;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sr.receiveShadows = false;
                go.SetActive(false);
                _pool[i] = new Mover { Root = go.transform, Renderer = sr };
                _ages[i] = -1f;
            }
            _live = 0;
        }

        /// <summary>A free mover, else the oldest live one (recycled).</summary>
        private Mover Take()
        {
            if (_pool == null) BuildPool();
            for (int i = 0; i < _pool.Length; i++) _ages[i] = _pool[i].Live ? _pool[i].Age : -1f;
            int slot = StealBeatRules.PickSlot(_ages);
            if (slot < 0) return null;
            var m = _pool[slot];
            // A robbery's last kernel recycled mid-air never lands: the thief held +n falls back to its timer (I5).
            if (m.Live && m.ThiefFeedback != null) m.ThiefFeedback.NotifyStealFlightCulled();
            m.ThiefFeedback = null;
            return m;
        }

        private void Activate(Mover m)
        {
            if (!m.Live) { m.Live = true; _live++; }
            m.Root.gameObject.SetActive(true);
            m.Renderer.sortingOrder = m.Order;
            m.Renderer.enabled = m.Age >= 0f;
            Apply(m, Camera.main);
        }

        private void LateUpdate()
        {
            if (_pool == null || _live == 0) return;
            float dt = Time.deltaTime;
            var cam = Camera.main;
            for (int i = 0; i < _pool.Length; i++)
            {
                var m = _pool[i];
                if (!m.Live) continue;
                m.Age += dt;
                if (m.Age >= m.Life) { Finish(m); continue; }
                if (m.Age < 0f) continue;
                if (!m.Renderer.enabled) m.Renderer.enabled = true;
                Apply(m, cam);
            }
        }

        private void Finish(Mover m)
        {
            if (m.Kind == Kind.Kernel && m.Arrives && m.ThiefVfx != null && m.Thief != null && m.Route == StealBeatRules.ChunkRoute.ToThief)
                m.ThiefVfx.EmitStealArrival();   // gates on the thief opacity and Reduced Motion itself
            if (m.Kind == Kind.Kernel && m.Arrives && m.ThiefFeedback != null)
                m.ThiefFeedback.NotifyStealFlightArrived();   // I5: the thief own +n pops as the corn lands
            m.Live = false;
            _live--;
            m.Thief = null;
            m.ThiefVfx = null;
            m.ThiefFeedback = null;
            m.Root.gameObject.SetActive(false);
        }

        private static void Apply(Mover m, Camera cam)
        {
            float t = Mathf.Clamp01(m.Age / Mathf.Max(0.0001f, m.Life));
            var c = m.Tint;
            Vector3 pos;
            float scale = m.BaseScale;
            float roll;

            if (m.Kind == Kind.Whiff)
            {
                pos = m.Start;
                // Reduced Motion: alpha only, no growth.
                float s = m.Reduced ? 0.5f * (m.ScaleStart + m.ScaleEnd)
                                    : Mathf.Lerp(m.ScaleStart, m.ScaleEnd, t);
                scale *= s;
                c.a = m.Alpha * (1f - t);
                roll = m.Roll;
            }
            else
            {
                Vector3 end = m.End;
                // A live thief is followed (the kernels land on where it is now); the hidden route stays above the victim.
                if (m.Route == StealBeatRules.ChunkRoute.ToThief && m.Thief != null)
                    end = StealBeatRules.ChunkEnd(m.Route, m.Start - Vector3.up * FeedbackTuning.StealChunkHeight, m.Thief.transform.position);
                pos = StealBeatRules.ChunkTravels(m.Reduced) ? StealBeatRules.ChunkPosition(m.Start, end, t, m.Lateral) : end;
                c.a = StealBeatRules.ChunkAlpha(m.Age, m.Reduced, m.Route);
                roll = m.Roll + (m.Reduced ? 0f : StealBeatRules.SpinDegrees(m.Age));
            }

            m.Root.position = pos;
            m.Root.localScale = Vector3.one * scale;
            m.Renderer.color = c;
            if (cam != null) m.Root.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, roll);
        }
    }
}
