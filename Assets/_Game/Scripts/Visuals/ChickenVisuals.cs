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

        [Tooltip("Renderers receiving the class tint. If empty, auto-populated from children at Awake, " +
                 "then replaced by SetModelRoot once the per-class model is attached at spawn.")]
        [SerializeField] private Renderer[] _renderers;

        private MaterialPropertyBlock _propertyBlock;
        private Color _currentTint = Color.white;
        private Color _lastPushedColor = Color.white;
        private float _lastAppliedOpacity = 1f;
        private bool  _isDeadState;
        private ChickenController _controller;
        private ChickenCombat     _combat;

        /// <summary>
        /// The exact colour this component last wrote to the body. <see cref="HitFeedback"/>
        /// reads it as the base for its white hit flash and writes it straight back when the
        /// flash ends, so the hand-back is byte-identical to whatever tint/opacity/dead
        /// state was in effect and can never corrupt it.
        /// </summary>
        public Color BodyColor => _lastPushedColor;

        /// <summary>
        /// The renderer set that carries the class tint. Exposed so
        /// <see cref="HitFeedback"/>'s hit flash addresses exactly the same surfaces — if
        /// the two ever disagreed, a flash could leave a stray renderer permanently white,
        /// or miss the one renderer the player is actually looking at.
        /// </summary>
        public Renderer[] TintedRenderers => _renderers;

        private void Awake()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }
            _propertyBlock = new MaterialPropertyBlock();
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
        }

        /// <summary>
        /// Re-points the tint at the per-class model that <see cref="ChickenController.Spawned"/>
        /// just parented under this chicken. Needed because <c>Awake</c> — where
        /// <see cref="_renderers"/> is normally populated — runs before <c>Spawned</c>, so the
        /// model does not exist yet and would otherwise never be tinted or hit-flashed.
        /// </summary>
        /// <remarks>
        /// The scan is deliberately scoped to <paramref name="modelRoot"/> rather than to this
        /// GameObject. By the time <c>Spawned</c> runs, <c>ChickenNameplate</c>,
        /// <c>ChickenWorldBars</c>, <c>ChickenStatusBadges</c> and <c>FloatingCombatText</c> have
        /// each parented TextMesh children under the chicken; a whole-hierarchy re-scan would
        /// sweep those into the tint set, turning the nameplate and health bar the class colour
        /// and flashing them white on every hit.
        /// </remarks>
        public void SetModelRoot(Transform modelRoot)
        {
            if (modelRoot == null) return;

            _renderers = modelRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            // Re-apply whatever colour was last in effect so a model attached after a tint /
            // opacity / dead-state change does not spawn in default white.
            PushColor(_lastPushedColor);
        }

        /// <summary>
        /// Applies the class wash. <paramref name="strength"/> is
        /// <see cref="ChickenClassRegistrySO.Entry.TintStrength"/>: 0 leaves the model's baked
        /// albedo atlas untouched, 1 is the old full-strength flat tint.
        /// </summary>
        /// <remarks>
        /// The blend happens <b>here</b>, not at the callsite, and the <i>effective</i> colour is
        /// what lands in <see cref="_currentTint"/>. Everything downstream — the dead-state grey
        /// lerp in <c>LateUpdate</c>, the opacity multiply, and <see cref="HitFeedback"/>'s flash
        /// hand-back via <see cref="BodyColor"/> — reads that one field, so they all keep working
        /// unchanged and none of them has to know a strength exists.
        ///
        /// Why a wash at all rather than dropping the tint: URP/Lit computes albedo as
        /// <c>_BaseMap × _BaseColor</c>, and the models now carry per-class baked atlases whose
        /// hues already match the registry tints. Multiplying the two at full strength double-applies
        /// the class colour and turns a maroon bird into dark mud. A light wash keeps a hue cue
        /// readable at ortho-iso distance without eating the authored art.
        /// </remarks>
        public void ApplyTint(Color color, float strength)
        {
            var effective = Color.Lerp(Color.white, color, Mathf.Clamp01(strength));
            effective.a = color.a;
            _currentTint = effective;
            // Preserve current opacity when re-tinting.
            effective.a *= _lastAppliedOpacity;
            PushColor(effective);
        }

        private void LateUpdate()
        {
            if (_controller == null) return;

            // VisualOpacity is [Networked] (defaults to 0). Treat 0 as "not yet
            // replicated" → fall back to fully visible. The Invisibility ability
            // uses 0.2 (ghostly outline) rather than full 0, so this is safe in
            // practice and prevents a 1-frame flicker on proxies between Spawned
            // and the first snapshot.
            float raw = _controller.VisualOpacity;
            float effective = raw > 0f ? raw : 1f;

            bool isStunned  = _combat != null && _combat.IsRemoved;
            bool stunChanged = isStunned != _isDeadState;

            // Repaint when opacity changes (Invisibility ability) OR stun state flips.
            // Cheap: only writes the property block when something actually changed.
            if (!Mathf.Approximately(effective, _lastAppliedOpacity) || stunChanged)
            {
                _lastAppliedOpacity = Mathf.Clamp01(effective);
                _isDeadState        = isStunned;

                Color c;
                if (isStunned)
                {
                    // Grey-out + semi-transparent ghost so dead chickens read as
                    // clearly non-interactive without disappearing entirely.
                    c   = Color.Lerp(_currentTint, new Color(0.4f, 0.4f, 0.4f, 1f), 0.75f);
                    c.a = 0.40f;
                }
                else
                {
                    c   = _currentTint;
                    c.a = _lastAppliedOpacity;
                }
                PushColor(c);
            }
        }

        private void PushColor(Color color)
        {
            _lastPushedColor = color;
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
