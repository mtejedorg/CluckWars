using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Floating name above each chicken's head, the same identity the menus and the
    /// post-match screen use (<see cref="MatchStandings.DisplayName(bool, bool, ChickenClass, int)"/>):
    /// "You" for this peer's own chicken, the Coop's DashFox / BrunoB / PeckNoir for solo
    /// bots, "P{corner+1}" for a remote human. Colour = the corner's player colour
    /// (Okabe-Ito, same as the HUD and overlays). The label billboards toward the camera.
    /// </summary>
    /// <remarks>
    /// Pure local visual — no networking. Reads the replicated
    /// <c>HomeCornerIndex</c> / <c>IsBot</c> / <c>Class</c> and this peer's input authority; the
    /// text is rebuilt only when one of those (or the bounty / state badge) changes. Drop this on the Chicken prefab so every chicken (real or
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

        // Per-player identity colors (Okabe-Ito) — the same four as MatchHudController /
        // MatchOverlaysController / ART.md §6. (Was red / blue / green / yellow, which matched nothing.)
        private static readonly Color[] PlayerColors = new[]
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // Orange #E8751A
            new Color(0.10f, 0.50f, 0.77f, 1f), // Blue   #1A7FC4
            new Color(0.77f, 0.16f, 0.44f, 1f), // Pink   #C4286F
            new Color(0.05f, 0.62f, 0.48f, 1f), // Teal   #0D9E7A
        };

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private TextMesh  _text;
        private Transform _textTransform;

        private int  _appliedPlayerId  = -2;
        private ChickenClass _appliedClass;
        private bool _appliedClassValid;
        private bool _appliedLocal;
        private bool _appliedBot;
        private bool _appliedBountyActive;
        private bool _wasStunned;
        private string _appliedStateIcon = string.Empty;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();
            BuildLabel();
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
            _text.text = string.Empty;   // filled on the first LateUpdate with a valid network object
            _text.color = Color.white;

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
            bool isNowStunned = _combat != null && _combat.IsRemoved;
            if (isNowStunned != _wasStunned)
            {
                _wasStunned = isNowStunned;
                if (!isNowStunned)
                {
                    _appliedPlayerId  = -2;
                    _appliedClassValid = false;
                }
            }

            if (isNowStunned)
            {
                _text.text  = "☠";
                _text.color = new Color(0.75f, 0.20f, 0.20f, 0.85f);
                BillboardToCamera();
                return;
            }

            // ── Normal label ────────────────────────────────────────────────
            var obj = _controller.Object;
            if (obj != null && obj.IsValid)
            {
                int corner = _controller.HomeCornerIndex;
                var klass = _controller.Class;
                bool isLocal = _controller.HasInputAuthority;
                bool isBot = _controller.IsBot;
                bool bountyActive = _controller.LeaderBountyActive;
                string stateIcon = StateIcon();
                if (corner != _appliedPlayerId || !_appliedClassValid || klass != _appliedClass
                    || isLocal != _appliedLocal || isBot != _appliedBot
                    || bountyActive != _appliedBountyActive || stateIcon != _appliedStateIcon)
                {
                    _appliedPlayerId = corner;
                    _appliedClass = klass;
                    _appliedClassValid = true;
                    _appliedLocal = isLocal;
                    _appliedBot = isBot;
                    _appliedBountyActive = bountyActive;
                    _appliedStateIcon = stateIcon;

                    // No corner yet (spawn not stamped): the class is all there is to say.
                    string label = corner < 0
                        ? MatchStandings.ClassName(klass)
                        : MatchStandings.DisplayName(isLocal, isBot, klass, corner);
                    if (bountyActive) label = UiText.Format(UiKeys.NameplateBounty, ("name", label));
                    _text.text = stateIcon.Length > 0 ? stateIcon + " " + label : label;
                    _text.color = corner < 0 ? new Color(0.7f, 0.7f, 0.7f, 1f) : PlayerColors[corner % PlayerColors.Length];
                }
            }

            BillboardToCamera();
        }

        /// <summary>
        /// Control-state badge for the nameplate (ART.md §6.10).
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
            var cam = Camera.main;
            if (cam != null && _textTransform != null)
            {
                var lookDir = _textTransform.position - cam.transform.position;
                if (lookDir.sqrMagnitude > 1e-6f)
                    _textTransform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }
    }
}
