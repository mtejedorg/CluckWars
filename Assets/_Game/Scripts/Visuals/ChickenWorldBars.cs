using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// World-space HP + cargo bars and a player-identity ring for one chicken —
    /// the "on-character indicators" of ART.md §6.3 (Stage 4a of the UI rebuild).
    /// </summary>
    /// <remarks>
    /// Pure local visual: it only *observes* the replicated <c>ChickenCombat.HP</c>
    /// / <c>ChickenCargo</c> state each frame and never writes or RPCs anything,
    /// per the "animation + VFX are local" rule. Screen-space UI is UI Toolkit,
    /// but on-character indicators live in world space, so these are sprites
    /// (SpriteRenderer) rather than UITK — same call as the sibling
    /// <see cref="ChickenNameplate"/>, which owns the text line above these bars.
    ///
    /// The bar stack billboards to the camera; the identity ring stays flat on
    /// the ground. Bars hide while the chicken is stunned (the nameplate's skull
    /// carries that state) and the cargo bar hides when carrying nothing, so an
    /// idle chicken shows only HP + ring.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenWorldBars : MonoBehaviour
    {
        [Tooltip("Bar stack offset above the chicken's pivot (below the nameplate).")]
        [SerializeField] private Vector3 _barOffset = new Vector3(0f, 1.16f, 0f);

        [Tooltip("Bar width in world units.")]
        [Range(0.3f, 2f)]
        [SerializeField] private float _barWidth = 0.82f;

        [Tooltip("HP bar height in world units (cargo bar is thinner, per §6.3).")]
        [Range(0.02f, 0.3f)]
        [SerializeField] private float _hpHeight = 0.11f;

        [Tooltip("Radius of the player-identity ring at the chicken's feet.")]
        [Range(0.1f, 1.5f)]
        [SerializeField] private float _ringRadius = 0.42f;

        // Okabe-Ito per-player identity colours — same palette as the HUD,
        // overlays and menus (ART.md §6).
        private static readonly Color[] PlayerColors =
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // P1 orange #E8751A
            new Color(0.10f, 0.50f, 0.77f, 1f), // P2 blue   #1A7FC4
            new Color(0.77f, 0.16f, 0.44f, 1f), // P3 pink   #C4286F
            new Color(0.05f, 0.62f, 0.48f, 1f), // P4 teal   #0D9E7A
        };

        private static readonly Color TroughColor = new Color(0.04f, 0.024f, 0.016f, 0.85f);

        // Shared procedural sprites (one set for every chicken in the match).
        private static Sprite _quadCenter; // pivot centre — troughs
        private static Sprite _quadLeft;   // pivot left   — fills (grow rightward)
        private static Sprite _ringSprite; // soft ring    — identity ring

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;

        private Transform      _barRoot;
        private SpriteRenderer _hpFill;
        private SpriteRenderer _cargoTrough;
        private SpriteRenderer _cargoFill;
        private SpriteRenderer _ring;

        private int _appliedRingCorner = -2;

        private float CargoHeight => _hpHeight * 0.72f;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();

            BuildBars();
            BuildRing();
        }

        // ── Construction ───────────────────────────────────────────────────────

        private void BuildBars()
        {
            var root = new GameObject("WorldBars");
            root.transform.SetParent(transform, worldPositionStays: false);
            root.transform.localPosition = _barOffset;
            _barRoot = root.transform;

            float cargoY = -(_hpHeight * 0.5f + CargoHeight * 0.5f + 0.03f);

            // HP row: dark trough + coloured fill anchored to the trough's left edge.
            MakeTrough(_barRoot, "HpTrough", Vector3.zero, _hpHeight);
            _hpFill = MakeFill(_barRoot, "HpFill", 0f, _hpHeight);

            // Cargo row (thinner, just below HP) — hidden while carrying nothing.
            _cargoTrough = MakeTrough(_barRoot, "CargoTrough", new Vector3(0f, cargoY, 0f), CargoHeight);
            _cargoFill   = MakeFill(_barRoot, "CargoFill", cargoY, CargoHeight);
        }

        private SpriteRenderer MakeTrough(Transform parent, string name, Vector3 localPos, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = new Vector3(_barWidth, height, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = QuadCenter();
            sr.color        = TroughColor;
            sr.sortingOrder = 100;
            return sr;
        }

        private SpriteRenderer MakeFill(Transform parent, string name, float localY, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            // Left-pivot sprite parked on the trough's left edge, nudged toward the
            // camera so it draws over the trough; scale.x is the fill fraction.
            go.transform.localPosition = new Vector3(-_barWidth * 0.5f, localY, -0.01f);
            go.transform.localScale    = new Vector3(_barWidth, height * 0.78f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = QuadLeft();
            sr.color        = Color.white;
            sr.sortingOrder = 101;
            return sr;
        }

        private void BuildRing()
        {
            var go = new GameObject("IdentityRing");
            go.transform.SetParent(transform, worldPositionStays: false);
            // Flat on the ground, lifted a hair to avoid z-fighting with the floor.
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale    = Vector3.one * (_ringRadius * 2f);

            _ring = go.AddComponent<SpriteRenderer>();
            _ring.sprite       = RingSprite();
            _ring.color        = Color.white;
            _ring.sortingOrder = 1;
        }

        // ── Per-frame refresh ──────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_controller == null || _barRoot == null) return;

            var obj = _controller.Object;
            bool live = obj != null && obj.IsValid;

            RefreshRing(live);

            // While stunned the nameplate's skull carries the state — hide the bars
            // so the dead chicken doesn't read as a live threat (§6.10).
            bool stunned = _combat != null && _combat.IsStunned;
            bool show    = live && !stunned;
            if (_barRoot.gameObject.activeSelf != show)
                _barRoot.gameObject.SetActive(show);
            if (!show) return;

            RefreshHp();
            RefreshCargo();
            Billboard();
        }

        private void RefreshHp()
        {
            if (_hpFill == null || _combat == null) return;

            var stats = _controller.Stats;
            float max = stats != null ? stats.MaxHP : 0f;
            float frac = max > 0f ? Mathf.Clamp01(_combat.HP / max) : 0f;

            var s = _hpFill.transform.localScale;
            s.x = _barWidth * frac;
            _hpFill.transform.localScale = s;
            _hpFill.color = HpColor(frac);
        }

        private void RefreshCargo()
        {
            if (_cargoFill == null || _cargoTrough == null) return;

            float frac = 0f;
            if (_cargo != null && _cargo.Object != null && _cargo.Object.IsValid && _cargo.Capacity > 0f)
                frac = Mathf.Clamp01(_cargo.Fraction);

            // Empty cargo → hide the row entirely (no clutter), matching the
            // nameplate's cargo text, which also blanks at zero.
            bool carrying = frac > 0f;
            if (_cargoTrough.enabled != carrying) _cargoTrough.enabled = carrying;
            if (_cargoFill.enabled   != carrying) _cargoFill.enabled   = carrying;
            if (!carrying) return;

            var s = _cargoFill.transform.localScale;
            s.x = _barWidth * frac;
            _cargoFill.transform.localScale = s;
            _cargoFill.color = CargoColor(frac);
        }

        private void RefreshRing(bool live)
        {
            if (_ring == null) return;

            int corner = live ? _controller.HomeCornerIndex : -1;
            if (corner == _appliedRingCorner) return;
            _appliedRingCorner = corner;

            if (corner < 0)
            {
                // Unowned / not yet spawned (e.g. a Doppelganger decoy pre-stamp).
                _ring.color = new Color(0.7f, 0.7f, 0.7f, 0.5f);
                return;
            }

            var c = PlayerColors[corner % PlayerColors.Length];
            c.a = 0.75f;
            _ring.color = c;
        }

        private void Billboard()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var dir = _barRoot.position - cam.transform.position;
            if (dir.sqrMagnitude > 1e-6f)
                _barRoot.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        // ── Colour ramps ───────────────────────────────────────────────────────

        /// <summary>Green above half, ramping through yellow to red as HP drains.</summary>
        private static Color HpColor(float frac) => frac > 0.5f
            ? Color.Lerp(new Color(0.95f, 0.82f, 0.20f, 1f), new Color(0.30f, 0.80f, 0.32f, 1f), (frac - 0.5f) * 2f)
            : Color.Lerp(new Color(0.90f, 0.20f, 0.16f, 1f), new Color(0.95f, 0.82f, 0.20f, 1f), frac * 2f);

        /// <summary>Yellow → orange → bright red as the sack fills (red = FULL).</summary>
        private static Color CargoColor(float frac) =>
            frac >= 1f   ? new Color(1f, 0.18f, 0.08f, 1f) :
            frac >= 0.7f ? new Color(1f, 0.55f, 0.05f, 1f) :
                           new Color(0.95f, 0.88f, 0.20f, 1f);

        // ── Shared procedural sprites ──────────────────────────────────────────

        private static Sprite QuadCenter() => _quadCenter ??= MakeQuad(new Vector2(0.5f, 0.5f));
        private static Sprite QuadLeft()   => _quadLeft   ??= MakeQuad(new Vector2(0f, 0.5f));

        /// <summary>1×1 white quad; <paramref name="pivot"/> left = grows rightward.</summary>
        private static Sprite MakeQuad(Vector2 pivot)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            // pixelsPerUnit 1 → the sprite is exactly 1 world unit, so localScale
            // reads directly as the bar's world size.
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), pivot, 1f);
        }

        /// <summary>Soft-edged ring (hollow disc) for the identity marker at the feet.</summary>
        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            const int S = 128;
            const float outer = 0.5f, inner = 0.34f, feather = 0.05f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                // Fade in across the inner edge and out across the outer edge.
                float a = Mathf.Clamp01((d - inner) / feather) *
                          Mathf.Clamp01((outer - d) / feather);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            return _ringSprite;
        }
    }
}
