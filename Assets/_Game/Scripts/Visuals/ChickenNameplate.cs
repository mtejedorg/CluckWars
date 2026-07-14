using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Floating "P1 / P2 / P3 / P4" label rendered above each chicken's head.
    /// Color comes from the 4-player palette (Red / Blue / Green / Yellow). The
    /// label billboards toward the camera every frame.
    /// </summary>
    /// <remarks>
    /// Pure local visual — no networking. Reads
    /// <see cref="ChickenController.Object"/>.<c>InputAuthority</c> to pick the
    /// player slot. Drop this on the Chicken prefab so every chicken (real or
    /// Doppelganger decoy) carries one automatically.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenNameplate : MonoBehaviour
    {
        [Tooltip("Offset above the chicken's pivot. Adjust for taller / shorter visuals.")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, 1.7f, 0f);

        [Tooltip("TextMesh fontSize. Bigger = sharper at the cost of texture memory.")]
        [Range(8, 200)]
        [SerializeField] private int _fontSize = 96;

        [Tooltip("Scale of each character in world units.")]
        [Range(0.005f, 0.5f)]
        [SerializeField] private float _characterSize = 0.05f;

        // Per-player identity colors — mirror MatchHud / ART.md §6.
        private static readonly Color[] PlayerColors = new[]
        {
            new Color(0.95f, 0.30f, 0.25f, 1f), // Red
            new Color(0.30f, 0.55f, 0.95f, 1f), // Blue
            new Color(0.30f, 0.75f, 0.30f, 1f), // Green
            new Color(0.95f, 0.85f, 0.25f, 1f), // Yellow
        };

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private TextMesh  _text;
        private Transform _textTransform;
        private TextMesh  _cargoText;
        private Transform _cargoTransform;
        private int  _appliedPlayerId  = -2;
        private ChickenClass _appliedClass;
        private bool _appliedClassValid;
        private bool _appliedBountyActive;
        private bool _wasStunned;
        private string _appliedStateIcon = string.Empty;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();
            BuildLabel();
            BuildCargoLabel();
        }

        private void BuildLabel()
        {
            var go = new GameObject("Nameplate");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = _offset;
            _textTransform = go.transform;

            _text = go.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = _fontSize;
            _text.characterSize = _characterSize;
            _text.fontStyle = FontStyle.Bold;
            _text.text = "P?";
            _text.color = Color.white;

            // TextMesh comes with a default font; the MeshRenderer it owns
            // doesn't need a material assigned — Unity uses the font's
            // material automatically. Disable shadows / ambient receive so
            // the label reads clearly against the dark ground.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        private void LateUpdate()
        {
            if (_text == null || _controller == null) return;

            // ── Death skull ──────────────────────────────────────────────────
            bool isNowStunned = _combat != null && _combat.IsStunned;
            if (isNowStunned != _wasStunned)
            {
                _wasStunned = isNowStunned;
                if (!isNowStunned)
                {
                    // Just respawned — force normal label rebuild next block.
                    _appliedPlayerId  = -2;
                    _appliedClassValid = false;
                }
            }

            if (isNowStunned)
            {
                _text.text  = "☠";
                _text.color = new Color(0.75f, 0.20f, 0.20f, 0.85f);
                // Billboard + early return — skip normal rebuild while dead.
                BillboardToCamera();
                return;
            }

            // ── Normal label ────────────────────────────────────────────────
            // Apply once the NetworkObject is alive and we know the input
            // authority + class. Both can change post-Spawn (class is set via
            // onBeforeSpawned but the Doppelganger decoy re-stamps it), so we
            // re-render the label any time either changes.
            var obj = _controller.Object;
            if (obj != null && obj.IsValid)
            {
                // Identity = home corner, matching leaderboard P-numbers and base
                // tints for humans and bots alike (InputAuthority can't tell bots
                // apart — they all share [Player:None]).
                int corner = _controller.HomeCornerIndex;
                var klass = _controller.Class;
                bool bountyActive = _controller.LeaderBountyActive;
                string stateIcon = StateIcon();
                if (corner != _appliedPlayerId || !_appliedClassValid || klass != _appliedClass
                    || bountyActive != _appliedBountyActive || stateIcon != _appliedStateIcon)
                {
                    _appliedPlayerId = corner;
                    _appliedClass = klass;
                    _appliedClassValid = true;
                    _appliedBountyActive = bountyActive;
                    _appliedStateIcon = stateIcon;
                    if (corner < 0)
                    {
                        _text.text = bountyActive ? $"★ {klass} ★" : klass.ToString();
                        _text.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                    }
                    else
                    {
                        bool isBot = _controller.IsBot;
                        string label = isBot
                            ? $"P{corner + 1} {klass} (CPU)"
                            : $"P{corner + 1} {klass}";
                        if (bountyActive) label = $"★ {label} ★";
                        _text.text = stateIcon.Length > 0 ? $"{stateIcon} {label}" : label;
                        _text.color = PlayerColors[corner % PlayerColors.Length];
                    }
                }
            }

            BillboardToCamera();
            UpdateCargoLabel();
        }

        /// <summary>
        /// Control-state badge for the nameplate (ART.md §6.10). The stunned skull
        /// is handled earlier — this covers the movement states, root outranking
        /// slow since it's the harder lock. Returns "" when unaffected.
        /// The matching world-space overlays live on <see cref="ChickenStateOverlays"/>.
        /// </summary>
        private string StateIcon()
        {
            if (_controller == null) return string.Empty;
            if (_controller.Rooted) return "🌱";

            bool slowed = _controller.SlowMultiplier < 0.999f
                          || _controller.AuraSlowActive
                          || (_cargo != null && _cargo.IsPileSlow);
            return slowed ? "🐌" : string.Empty;
        }

        private void BillboardToCamera()
        {
            // Billboard toward whatever camera is currently rendering. Fixed
            // isometric camera in production, so this is effectively a constant
            // rotation; still doing it each frame for editor flexibility.
            var cam = Camera.main;
            if (cam != null && _textTransform != null)
            {
                var lookDir = _textTransform.position - cam.transform.position;
                if (lookDir.sqrMagnitude > 1e-6f)
                    _textTransform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }

        // ── Cargo line ─────────────────────────────────────────────────────────

        private void BuildCargoLabel()
        {
            var go = new GameObject("CargoIndicator");
            go.transform.SetParent(transform, worldPositionStays: false);
            // Sits 0.42 world-units below the main nameplate.
            go.transform.localPosition = new Vector3(0f, _offset.y - 0.42f, 0f);
            _cargoTransform = go.transform;

            _cargoText               = go.AddComponent<TextMesh>();
            _cargoText.anchor        = TextAnchor.MiddleCenter;
            _cargoText.alignment     = TextAlignment.Center;
            _cargoText.fontSize      = 72;
            _cargoText.characterSize = _characterSize * 0.80f;
            _cargoText.fontStyle     = FontStyle.Bold;
            _cargoText.text          = "";
            _cargoText.color         = Color.green;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows    = false;
            }
        }

        private void UpdateCargoLabel()
        {
            if (_cargoText == null || _cargoTransform == null) return;

            // Guard: wait until the NetworkObject is live (cargo is [Networked]).
            if (_cargo == null || _cargo.Object == null || !_cargo.Object.IsValid)
            {
                _cargoText.text = "";
                return;
            }

            // Hide cargo while dead — skull from main label covers this state.
            if (_combat != null && _combat.IsStunned)
            {
                _cargoText.text = "";
                return;
            }

            float cap = _cargo.Capacity;
            if (cap <= 0f) { _cargoText.text = ""; return; }

            float frac    = _cargo.Fraction;
            int   carried = Mathf.FloorToInt(_cargo.Cargo);
            int   capInt  = Mathf.FloorToInt(cap);

            if (frac >= 1f)
            {
                _cargoText.text  = "■ FULL!";
                _cargoText.color = new Color(1f, 0.18f, 0.08f, 1f); // bright red
            }
            else if (carried > 0)
            {
                _cargoText.text  = $"{carried}/{capInt}";
                _cargoText.color = frac >= 0.7f
                    ? new Color(1f, 0.55f, 0.05f, 1f)   // orange
                    : new Color(0.95f, 0.88f, 0.20f, 1f); // yellow
            }
            else
            {
                _cargoText.text = "";  // empty cargo → no clutter
            }

            // Billboard same as main label.
            var cam = Camera.main;
            if (cam != null)
            {
                var lookDir = _cargoTransform.position - cam.transform.position;
                if (lookDir.sqrMagnitude > 1e-6f)
                    _cargoTransform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }
    }
}
