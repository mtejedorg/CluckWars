using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only presentation for a chicken: per-class tint + opacity. Sibling
    /// component on the Chicken prefab. Applies a per-class tint via a
    /// <see cref="MaterialPropertyBlock"/> so we never instantiate per-chicken material
    /// copies (instancing-friendly, mobile-friendly). Polls
    /// <see cref="ChickenController.VisualOpacity"/> in <c>LateUpdate</c> for
    /// abilities that modulate alpha (Invisibility, …).
    /// </summary>
    public sealed class ChickenVisuals : MonoBehaviour
    {
        // URP/Lit and the Standard fallback both honor _BaseColor / _Color respectively.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Renderers receiving the class tint. If empty, auto-populated from children at Awake.")]
        [SerializeField] private Renderer[] _renderers;

        private MaterialPropertyBlock _propertyBlock;
        private Color _currentTint = Color.white;
        private float _lastAppliedOpacity = 1f;
        private ChickenController _controller;

        private void Awake()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }
            _propertyBlock = new MaterialPropertyBlock();
            _controller = GetComponent<ChickenController>();
        }

        public void ApplyTint(Color color)
        {
            _currentTint = color;
            // Preserve current opacity when re-tinting.
            color.a *= _lastAppliedOpacity;
            PushColor(color);
        }

        private void LateUpdate()
        {
            if (_controller == null) return;
            // Watch for opacity changes (Invisibility ability) and re-push the tint
            // with the new alpha. Cheap: only writes the property block when it
            // actually changes.
            if (!Mathf.Approximately(_controller.VisualOpacity, _lastAppliedOpacity))
            {
                _lastAppliedOpacity = Mathf.Clamp01(_controller.VisualOpacity);
                var c = _currentTint;
                c.a = _lastAppliedOpacity;
                PushColor(c);
            }
        }

        private void PushColor(Color color)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                _propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
