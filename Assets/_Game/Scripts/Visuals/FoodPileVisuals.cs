using CluckWars.Gameplay;
using UnityEngine;
using Zenject;

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

        [Tooltip("Legacy per-pile override. If null, ColorScheme.FoodPileFull is used.")]
        [SerializeField] private Color _fullColorOverride = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Legacy per-pile override. If null/alpha=0, ColorScheme.FoodPileEmpty is used.")]
        [SerializeField] private Color _emptyColorOverride = new Color(0f, 0f, 0f, 0f);

        private FoodPile _pile;
        private MaterialPropertyBlock _propertyBlock;
        private ColorSchemeSO _colors;

        [Inject]
        public void Construct(ColorSchemeSO colors) => _colors = colors;

        private void Awake()
        {
            _pile = GetComponent<FoodPile>();
            if (_meshRoot == null && transform.childCount > 0) _meshRoot = transform.GetChild(0);
            if ((_renderers == null || _renderers.Length == 0) && _meshRoot != null)
                _renderers = _meshRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            _propertyBlock = new MaterialPropertyBlock();

            // FoodPileVisuals lives on a procedurally-spawned NetworkObject (MapGenerator
            // spawns the pile at runtime), so Zenject's scene-time injection doesn't fire.
            // Self-inject the color scheme — same pattern as the NetworkBehaviours.
            if (_colors == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }
        }

        private Color FullColor => _fullColorOverride.a > 0f
            ? _fullColorOverride
            : (_colors != null ? _colors.FoodPileFull : new Color(1f, 0.85f, 0.3f));

        private Color EmptyColor => _emptyColorOverride.a > 0f
            ? _emptyColorOverride
            : (_colors != null ? _colors.FoodPileEmpty : new Color(0.4f, 0.3f, 0.15f));

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
                var color = Color.Lerp(EmptyColor, FullColor, ratio);
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
