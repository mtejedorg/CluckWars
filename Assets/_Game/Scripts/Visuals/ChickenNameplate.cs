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
        private TextMesh _text;
        private Transform _textTransform;
        private int _appliedPlayerId = -2;
        private ChickenClass _appliedClass = (ChickenClass)(-1);

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
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

            // Apply once the NetworkObject is alive and we know the input
            // authority + class. Both can change post-Spawn (class is set via
            // onBeforeSpawned but the Doppelganger decoy re-stamps it), so we
            // re-render the label any time either changes.
            var obj = _controller.Object;
            if (obj != null && obj.IsValid)
            {
                int playerId = obj.InputAuthority.PlayerId;
                var klass = _controller.Class;
                if (playerId != _appliedPlayerId || klass != _appliedClass)
                {
                    _appliedPlayerId = playerId;
                    _appliedClass = klass;
                    if (playerId < 0)
                    {
                        _text.text = klass.ToString();
                        _text.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                    }
                    else
                    {
                        _text.text = $"P{playerId + 1} {klass}";
                        _text.color = PlayerColors[playerId % PlayerColors.Length];
                    }
                }
            }

            // Billboard toward whatever camera is currently rendering. Fixed
            // isometric camera in production, so this is effectively a constant
            // rotation; still doing it each frame for editor flexibility.
            var cam = Camera.main;
            if (cam != null && _textTransform != null)
            {
                var lookDir = _textTransform.position - cam.transform.position;
                if (lookDir.sqrMagnitude > 1e-6f)
                {
                    _textTransform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
            }
        }
    }
}
