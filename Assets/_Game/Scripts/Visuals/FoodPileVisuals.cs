using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only feedback for a <see cref="FoodPile"/>: scales a child mesh down as the
    /// pile is drained, and tints it from <see cref="_fullColor"/> to <see cref="_emptyColor"/>.
    /// Reads the networked <c>Amount</c> on every peer — no [Networked] state of its own.
    /// </summary>
    [RequireComponent(typeof(FoodPile))]
    public sealed class FoodPileVisuals : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Transform whose local scale is multiplied by the pile fill ratio. Defaults to first child.")]
        [SerializeField] private Transform _meshRoot;

        [Tooltip("Renderer(s) tinted from full → empty. Auto-populated from _meshRoot children if empty.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("Scale at 100% full.")]
        [SerializeField] private Vector3 _fullScale = Vector3.one;

        [Tooltip("Scale at 0% (still visible so the pile doesn't disappear unexpectedly during draining).")]
        [SerializeField] private Vector3 _emptyScale = new Vector3(0.3f, 0.3f, 0.3f);

        [SerializeField] private Color _fullColor = new Color(1f, 0.85f, 0.3f);   // ripe corn
        [SerializeField] private Color _emptyColor = new Color(0.4f, 0.3f, 0.15f); // husk

        private FoodPile _pile;
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            _pile = GetComponent<FoodPile>();
            if (_meshRoot == null && transform.childCount > 0) _meshRoot = transform.GetChild(0);
            if ((_renderers == null || _renderers.Length == 0) && _meshRoot != null)
                _renderers = _meshRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (_pile == null) return;

            // Object.IsValid means the NetworkObject is spawned and has replicated state.
            if (_pile.Object == null || !_pile.Object.IsValid) return;

            float ratio = _pile.MaxAmount > 0f ? Mathf.Clamp01(_pile.Amount / _pile.MaxAmount) : 0f;

            if (_meshRoot != null)
            {
                _meshRoot.localScale = Vector3.Lerp(_emptyScale, _fullScale, ratio);
            }

            if (_renderers != null && _renderers.Length > 0)
            {
                var color = Color.Lerp(_emptyColor, _fullColor, ratio);
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var r = _renderers[i];
                    if (r == null) continue;
                    r.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(BaseColorId, color);
                    _propertyBlock.SetColor(ColorId, color);
                    r.SetPropertyBlock(_propertyBlock);
                }
            }
        }
    }
}
