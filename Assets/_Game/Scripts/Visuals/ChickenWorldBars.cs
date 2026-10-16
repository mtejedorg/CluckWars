using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Settings;
using CluckWars.UI;
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

        [Tooltip("Minimum radius of the player-identity ring at the chicken's feet (root-local). The live radius grows to clear the " +
                 "class model's body: see GroundRingStyle / ChickenFootprint.")]
        [Range(0.1f, 1.5f)]
        [SerializeField] private float _ringRadius = 0.42f;

        [Header("Sprites")]
        [SerializeField] private Sprite _barTroughSprite;
        [SerializeField] private Sprite _barFillSprite;

        private static readonly Color TroughColor = new Color(1f, 1f, 1f, 1f); // Sprite is already dark

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;

        private Transform      _barRoot;
        private SpriteRenderer _cargoTrough;
        private SpriteRenderer _cargoFill;
        private TextMesh       _cargoText;
        private SpriteRenderer _ring;
        private SpriteRenderer _ringEdge;    // thin cream outer edge so a dark player colour reads on grass
        private float _ringDrawRadius;       // live outer radius of the colour stroke, root-local

        private int _appliedRingCorner = -2;
        private Color _ringBaseColor = Color.white;
        private float _youEmphasis;          // 0..1, GET READY "this is you" marker (YouMarkerRules)
        private bool _ringEmphasised;        // last frame wrote an emphasised scale/alpha

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

        /// <summary>Raises the bar stack for a class model drawn taller than the collider (Phase 8 D6).
        /// Root-local metres; presentation only.</summary>
        public void SetHeadLift(float lift)
        {
            if (_barRoot != null) _barRoot.localPosition = _barOffset + Vector3.up * lift;
        }

        private void BuildBars()
        {
            var root = new GameObject("WorldBars");
            root.transform.SetParent(transform, worldPositionStays: false);
            root.transform.localPosition = _barOffset;
            _barRoot = root.transform;

            // v0.4: HP deleted. Only the cargo bar remains; it now sits at the bar root centre.
            float cargoY = 0f;

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
            go.transform.SetParent(ChickenGround.Anchor(this), worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale    = Vector3.one * (_ringRadius * 2f);

            _ring = go.AddComponent<SpriteRenderer>();
            _ring.color        = Color.white;
            _ring.sortingOrder = 1;
            SetRingRadius(_ringRadius);
        }

        /// <summary>Re-sizes the identity ring to clear the class model's body (Phase 8 chunk C R3). Presentation only;
        /// <paramref name="bodyHalfWidth"/> is the model's lateral half-width in root-local metres.</summary>
        public void SetBodyFootprint(float bodyHalfWidth)
        {
            if (_ring == null) return;
            SetRingRadius(Mathf.Max(_ringRadius, GroundRingStyle.IdentityRadius(bodyHalfWidth)));
        }

        private void SetRingRadius(float radius)
        {
            _ringDrawRadius = radius;
            GroundRingStyle.Apply(_ring, ref _ringEdge, radius, GroundRingStyle.IdentityStroke,
                GroundRingStyle.IdentityEdge, GroundRingStyle.IdentityEdgeAlpha);
        }

        // ── Per-frame refresh ──────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_controller == null || _barRoot == null) return;

            var obj = _controller.Object;
            bool live = obj != null && obj.IsValid;

            RefreshRing(live);
            RefreshYouMarker(live);

            bool stunned = _combat != null && _combat.IsRemoved;
            bool show    = live && !stunned;
            if (_barRoot.gameObject.activeSelf != show)
                _barRoot.gameObject.SetActive(show);
            if (!show) return;

            RefreshCargo();
            Billboard();
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
                _cargoText.text  = UiText.Get(UiKeys.HudPopFull);
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

            // The corner's player colour (PlayerPalette, same as the HUD, menus and podium); neutral and
            // fainter until the spawn stamps a corner.
            var c = PlayerPalette.ForCorner(corner);
            c.a = corner < 0 ? 0.5f : 0.75f;
            _ringBaseColor = c;
            _ring.color = c;
        }

        /// <summary>
        /// GET READY "this is you" marker (Phase 6 chunk 7f): the local bird's ring grows, goes opaque and
        /// pulses during GET READY and the countdown, then eases back after GO. Static under Reduced Motion.
        /// See <see cref="YouMarkerRules"/>.
        /// </summary>
        private void RefreshYouMarker(bool live)
        {
            if (_ring == null) return;

            // This peer's own bird only (a Doppelganger decoy shares its input authority).
            bool show = live && _controller.HasInputAuthority && !_controller.IsDecoy && YouMarkerRules.IsGetReadyNow();
            bool reduced = PlayerPreferences.ReducedMotionEnabled;
            _youEmphasis = YouMarkerRules.StepEmphasis(_youEmphasis, show, Time.unscaledDeltaTime, reduced);

            if (_youEmphasis <= 0f)
            {
                if (_ringEmphasised)
                {
                    _ring.transform.localScale = Vector3.one * (_ringDrawRadius * 2f);
                    _ring.color = _ringBaseColor;
                    _ringEmphasised = false;
                }
                return;
            }

            float scale = YouMarkerRules.RingScaleAt(_youEmphasis, Time.unscaledTime, reduced);
            _ring.transform.localScale = Vector3.one * (_ringDrawRadius * 2f * scale);
            var c = _ringBaseColor;
            c.a = YouMarkerRules.RingAlphaAt(_youEmphasis, _ringBaseColor.a);
            _ring.color = c;
            _ringEmphasised = true;
        }

        private void Billboard()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var dir = BillboardFacing.Direction(cam, _barRoot.position);
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
    }
}
