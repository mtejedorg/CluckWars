using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only presentation for a chicken: per-class tint + opacity. Sibling
    /// component on the Chicken prefab. Applies a per-class tint via a
    /// <see cref="MaterialPropertyBlock"/> so we never instantiate per-chicken material
    /// copies (instancing-friendly, mobile-friendly). Polls
    /// <see cref="ChickenController.VisualOpacity"/> in <c>LateUpdate</c> for
    /// abilities that modulate alpha (Invisibility, …).
    ///
    /// <para><b>Ghost fade (Phase 8 C2).</b> The chicken materials are URP Lit <i>Opaque</i>, which
    /// ignores <c>_BaseColor.a</c>, so a pushed alpha used to do nothing. While the pushed alpha is
    /// below <see cref="GhostOpacityThreshold"/> every tinted renderer's <c>sharedMaterials</c> are
    /// swapped for ghost twins (<c>Cluck Wars/Chicken Ghost Lit</c>, a dithered opaque clip that keeps
    /// <c>_BaseColor.a</c> of the pixels) and swapped back to the cached originals when it returns.
    /// One ghost twin per original material, shared by every chicken — never a per-chicken or
    /// per-frame instance. Below <see cref="GhostShadowOpacityThreshold"/> shadow casting is also off.</para>
    /// </summary>
    public sealed class ChickenVisuals : MonoBehaviour
    {
        // URP/Lit and the Standard fallback both honor _BaseColor / _Color respectively.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Renderers receiving the class tint. If empty, auto-populated from children at Awake, " +
                 "then replaced by SetModelRoot once the per-class model is attached at spawn.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("Template for the ghost fade (Assets/_Game/Art/Materials/Chicken_Ghost.mat, shader " +
                 "Cluck Wars/Chicken Ghost Lit). Each original body material gets one shared ghost twin " +
                 "built from it, swapped in only while opacity < 0.99 (Invisibility, Smoke Roost, stunned).")]
        [SerializeField] private Material _ghostMaterial;

        /// <summary>Below this pushed alpha the body renders with the dithered ghost material.</summary>
        public const float GhostOpacityThreshold = 0.99f;

        /// <summary>Below this pushed alpha the ghost also stops casting a shadow (a stippled shadow
        /// of a 0.2 bird would give the stealth away and reads as dirt on the ground).</summary>
        public const float GhostShadowOpacityThreshold = 0.5f;

        private const string Source = "ChickenVisuals";

        // Original material -> its ghost twin. Bounded by the number of distinct chicken materials (4 classes).
        private static readonly Dictionary<Material, Material> GhostTwins = new Dictionary<Material, Material>();

        // Non-null exactly while the ghost is applied: the renderers it was applied to and what they had before.
        private Renderer[]          _ghostedRenderers;
        private Material[][]        _savedMaterials;
        private ShadowCastingMode[] _savedShadowModes;
        private bool                _ghostShadowsOff;
        private bool                _missingGhostReported;
        private bool                _warmupGhost;
        private ILogService         _log;

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

        /// <summary>True while the body renders with the ghost material (test/diagnostic seam).</summary>
        public bool IsGhosted => _ghostedRenderers != null;

        /// <summary>
        /// Arena warm-up override (<c>ArenaReadinessDriver</c>): forces the ghost twin materials on while the overview renders, so the
        /// ghost shader's programs exist before the first Invisibility / Smoke Roost / stun. Presentation only: the pushed opacity,
        /// <see cref="ChickenController.VisualOpacity"/> and every [Networked] value are untouched, and releasing it hands the body
        /// back to exactly what the normal ghost logic (<see cref="ApplyGhostState"/>) wants for the opacity in effect.
        /// </summary>
        public void SetWarmupGhost(bool on)
        {
            if (_warmupGhost == on) return;
            _warmupGhost = on;
            ApplyGhostState(_lastPushedColor.a);
        }

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }
            _propertyBlock ??= new MaterialPropertyBlock();
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

            // Hand the old renderers their own materials back before the set changes; PushColor below
            // re-applies the ghost to the new set if the bird is still faded.
            RestoreOriginalMaterials();
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

        private void OnDestroy() => RestoreOriginalMaterials();

        private void PushColor(Color color)
        {
            _lastPushedColor = color;
            if (_renderers == null) return;
            _propertyBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                _propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
            ApplyGhostState(color.a);
        }

        // ---- ghost fade (Phase 8 C2) ------------------------------------------------------------------

        /// <summary>
        /// Swaps the ghost in / out and toggles shadow casting from the alpha just pushed. Runs only
        /// when <see cref="PushColor"/> runs (tint / opacity / stun changes), so material arrays are
        /// assigned on transitions only — never per frame.
        /// </summary>
        private void ApplyGhostState(float opacity)
        {
            bool wantGhost = _warmupGhost || opacity < GhostOpacityThreshold;
            if (wantGhost && _ghostedRenderers == null)
            {
                // Breadcrumb only: the first real ghost of this scene (the warm-up's forced ghost is not a first use).
                if (!_warmupGhost) CluckWars.Diagnostics.FirstUseMarkers.Note(CluckWars.Diagnostics.FirstUseKind.Invisibility);
                ApplyGhostMaterials();
            }
            else if (!wantGhost && _ghostedRenderers != null) RestoreOriginalMaterials();

            if (_ghostedRenderers != null) SetGhostShadowsOff(opacity < GhostShadowOpacityThreshold);
        }

        private void ApplyGhostMaterials()
        {
            if (_ghostMaterial == null)
            {
                // A wiring bug, not a game state: the bird would stay fully opaque while faded.
                if (!_missingGhostReported)
                {
                    _missingGhostReported = true;
                    if (_log == null) ProjectContext.Instance.Container.Inject(this);
                    _log?.Error(Source, $"{name}: no ghost material assigned; Invisibility / Smoke Roost / stun " +
                                        "cannot fade the body. Wire Chicken_Ghost.mat on ChickenVisuals.");
                }
                return;
            }

            var renderers = _renderers;
            _savedMaterials   = new Material[renderers.Length][];
            _savedShadowModes = new ShadowCastingMode[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var originals = r.sharedMaterials;
                _savedMaterials[i]   = originals;
                _savedShadowModes[i] = r.shadowCastingMode;

                var ghosts = new Material[originals.Length];
                for (int m = 0; m < originals.Length; m++) ghosts[m] = GhostTwinOf(originals[m]);
                r.sharedMaterials = ghosts;
            }
            _ghostedRenderers = renderers;
            _ghostShadowsOff  = false;
        }

        private void RestoreOriginalMaterials()
        {
            if (_ghostedRenderers == null) return;
            for (int i = 0; i < _ghostedRenderers.Length; i++)
            {
                var r = _ghostedRenderers[i];
                if (r == null || _savedMaterials[i] == null) continue;
                r.sharedMaterials   = _savedMaterials[i];
                r.shadowCastingMode = _savedShadowModes[i];
            }
            _ghostedRenderers = null;
            _savedMaterials   = null;
            _savedShadowModes = null;
            _ghostShadowsOff  = false;
        }

        private void SetGhostShadowsOff(bool off)
        {
            if (off == _ghostShadowsOff) return;
            _ghostShadowsOff = off;
            for (int i = 0; i < _ghostedRenderers.Length; i++)
            {
                var r = _ghostedRenderers[i];
                if (r == null || _savedMaterials[i] == null) continue;
                r.shadowCastingMode = off ? ShadowCastingMode.Off : _savedShadowModes[i];
            }
        }

        /// <summary>
        /// The one shared ghost twin of <paramref name="original"/>: the template's shader with the
        /// original's properties and keywords (texture, matte settings), built once and reused by
        /// every chicken wearing that material.
        /// </summary>
        private Material GhostTwinOf(Material original)
        {
            if (original == null) return null;
            if (original.shader == _ghostMaterial.shader) return original; // already a ghost
            if (GhostTwins.TryGetValue(original, out var twin) && twin != null) return twin;

            twin = new Material(_ghostMaterial)
            {
                name      = original.name + "_Ghost",
                hideFlags = HideFlags.DontSave,
            };
            twin.CopyPropertiesFromMaterial(original);
            twin.enableInstancing = true;
            GhostTwins[original] = twin;
            return twin;
        }
    }
}
