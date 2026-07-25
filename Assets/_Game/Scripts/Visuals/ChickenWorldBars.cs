using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// World-space HP + cargo bars and a player-identity ring for one chicken —
    /// the "on-character indicators" of ART.md §6.3 (Stage 4a of the UI rebuild).
    /// </summary>
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

        [Header("Sprites")]
        [SerializeField] private Sprite _barTroughSprite;
        [SerializeField] private Sprite _barFillSprite;

        // Okabe-Ito per-player identity colours — same palette as the HUD,
        // overlays and menus (ART.md §6).
        private static readonly Color[] PlayerColors =
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // P1 orange #E8751A
            new Color(0.10f, 0.50f, 0.77f, 1f), // P2 blue   #1A7FC4
            new Color(0.77f, 0.16f, 0.44f, 1f), // P3 pink   #C4286F
            new Color(0.05f, 0.62f, 0.48f, 1f), // P4 teal   #0D9E7A
        };

        private static readonly Color TroughColor = new Color(1f, 1f, 1f, 1f); // Sprite is already dark

        private static Sprite _ringSprite; // soft ring    — identity ring

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;

        private Transform      _barRoot;
        private SpriteRenderer _hpFill;
        private SpriteRenderer _cargoTrough;
        private SpriteRenderer _cargoFill;
        private TextMesh       _cargoText;
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

            MakeTrough(_barRoot, "HpTrough", Vector3.zero, _hpHeight);
            _hpFill = MakeFill(_barRoot, "HpFill", 0f, _hpHeight * 0.78f);

            _cargoTrough = MakeTrough(_barRoot, "CargoTrough", new Vector3(0f, cargoY, 0f), CargoHeight);
            _cargoFill   = MakeFill(_barRoot, "CargoFill", cargoY, CargoHeight * 0.78f);

            // Cargo text beside the cargo bar
            var textGo = new GameObject("CargoCount");
            textGo.transform.SetParent(_barRoot, worldPositionStays: false);
            textGo.transform.localPosition = new Vector3(_barWidth * 0.5f + 0.05f, cargoY, -0.01f);
            
            _cargoText = textGo.AddComponent<TextMesh>();
            _cargoText.anchor = TextAnchor.MiddleLeft;
            _cargoText.alignment = TextAlignment.Left;
            _cargoText.fontSize = 48;
            _cargoText.characterSize = 0.025f;
            _cargoText.fontStyle = FontStyle.Bold;
            _cargoText.text = "";
            var mr = textGo.GetComponent<MeshRenderer>();
            if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
        }

        private SpriteRenderer MakeTrough(Transform parent, string name, Vector3 localPos, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;

            var sr = go.AddComponent<SpriteRenderer>();
            if (_barTroughSprite != null)
            {
                sr.sprite = _barTroughSprite;
                float sx = _barWidth / _barTroughSprite.bounds.size.x;
                float sy = height / _barTroughSprite.bounds.size.y;
                go.transform.localScale = new Vector3(sx, sy, 1f);
            }
            sr.color        = TroughColor;
            sr.sortingOrder = 100;
            return sr;
        }

        private SpriteRenderer MakeFill(Transform parent, string name, float localY, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(-_barWidth * 0.5f, localY, -0.01f);

            var sr = go.AddComponent<SpriteRenderer>();
            if (_barFillSprite != null)
            {
                sr.sprite = _barFillSprite;
                float sy = height / _barFillSprite.bounds.size.y;
                go.transform.localScale = new Vector3(0f, sy, 1f);
            }
            sr.color        = Color.white;
            sr.sortingOrder = 101;
            return sr;
        }

        private void BuildRing()
        {
            var go = new GameObject("IdentityRing");
            go.transform.SetParent(transform, worldPositionStays: false);
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
            if (_hpFill == null) return;
            UpdateFill(_hpFill, 0f, _hpHeight * 0.78f);
            _hpFill.enabled = false;
        }

        private void RefreshCargo()
        {
            if (_cargoFill == null || _cargoTrough == null) return;

            float frac = 0f;
            if (_cargo != null && _cargo.Object != null && _cargo.Object.IsValid && _cargo.Capacity > 0f)
                frac = Mathf.Clamp01(_cargo.Fraction);

            bool carrying = frac > 0f;
            if (_cargoTrough.enabled != carrying) _cargoTrough.enabled = carrying;
            if (_cargoFill.enabled   != carrying) _cargoFill.enabled   = carrying;
            if (_cargoText.gameObject.activeSelf != carrying) _cargoText.gameObject.SetActive(carrying);
            
            if (!carrying) return;

            UpdateFill(_cargoFill, frac, CargoHeight * 0.78f);
            _cargoFill.color = CargoColor(frac);

            int   carried = Mathf.FloorToInt(_cargo.Cargo);
            int   capInt  = Mathf.FloorToInt(_cargo.Capacity);
            if (frac >= 1f)
            {
                _cargoText.text  = "FULL!";
                _cargoText.color = new Color(1f, 0.18f, 0.08f, 1f);
            }
            else
            {
                _cargoText.text  = $"{carried}/{capInt}";
                _cargoText.color = frac >= 0.7f
                    ? new Color(1f, 0.55f, 0.05f, 1f)
                    : new Color(0.95f, 0.88f, 0.20f, 1f);
            }
        }

        private void UpdateFill(SpriteRenderer fill, float frac, float height)
        {
            float fillWidth = _barWidth * frac;
            
            if (fill.sprite != null)
            {
                float sx = fillWidth / fill.sprite.bounds.size.x;
                float sy = height / fill.sprite.bounds.size.y;
                fill.transform.localScale = new Vector3(sx, sy, 1f);

                // Adjust position so it expands to the right (accounting for pivot)
                Vector2 pivot = new Vector2(fill.sprite.pivot.x / fill.sprite.rect.width, fill.sprite.pivot.y / fill.sprite.rect.height);
                float offsetX = -_barWidth * 0.5f + fillWidth * pivot.x;
                fill.transform.localPosition = new Vector3(offsetX, fill.transform.localPosition.y, fill.transform.localPosition.z);
            }
        }

        private void RefreshRing(bool live)
        {
            if (_ring == null) return;

            int corner = live ? _controller.HomeCornerIndex : -1;
            if (corner == _appliedRingCorner) return;
            _appliedRingCorner = corner;

            if (corner < 0)
            {
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
