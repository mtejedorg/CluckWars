using CluckWars.Gameplay;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only feedback for a <see cref="FoodPile"/>: flattens the mound as the pile is
    /// drained, tints it from the full to the empty colour (never darker than <see cref="TintFloor"/>),
    /// and swaps to the scattered-grain "empty" mesh at zero.
    /// Reads the networked <c>Amount</c> on every peer — no [Networked] state of its own.
    /// </summary>
    /// <remarks>
    /// <b>The mound's ground footprint IS the pile's collider (Phase 8 D9).</b> The X,Z size
    /// comes only from the pile root's scale (the current footprint step, the same scale that
    /// sizes the ellipse collider and the NavMesh carve), and the mesh is seated so its widest
    /// ring sits on the ground. Until D9 the mound also shrank in X,Z with the fill ratio and
    /// sat half-buried, so its visible oval was ~0.8x the collider when full and ~0.3x near
    /// empty: chickens stopped well short of the grain. Fill now reads through height.
    /// </remarks>
    [RequireComponent(typeof(FoodPile))]
    public sealed class FoodPileVisuals : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Mound transform (child of the pile root, unit-footprint mesh). Its Y scale follows the fill ratio; X,Z stay 1 so the footprint is the root's. Defaults to first child.")]
        [SerializeField] private Transform _meshRoot;

        [Tooltip("Renderer(s) tinted from full → empty. Auto-populated from _meshRoot children if empty.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("MeshFilter of the grain mound (child of _meshRoot carrying the FBX's 270 deg X node rotation). Swapped between _fullMesh and _emptyMesh.")]
        [SerializeField] private MeshFilter _moundFilter;

        [Tooltip("Mound mesh while the pile has food (Generated/Env/food_mound.fbx, unit footprint, ground ring at y 0).")]
        [SerializeField] private Mesh _fullMesh;

        [Tooltip("Scattered-grain mesh shown at Amount 0 (Generated/Env/food_mound_empty.fbx). Drawn at scale 1 in footprint space, lifted by _emptyLift.")]
        [SerializeField] private Mesh _emptyMesh;

        [Tooltip("Lift (pile-root local Y) of the empty-grain mesh so it clears the pile disc slab.")]
        [SerializeField] private float _emptyLift = 0.015f;

        [Tooltip("Mound height (local Y scale) at 100% full.")]
        [Min(0.01f)]
        [SerializeField] private float _fullHeight = 1f;

        [Tooltip("Mound height (local Y scale) at 0% (still visible so the pile doesn't disappear unexpectedly during draining).")]
        [Min(0.01f)]
        [SerializeField] private float _emptyHeight = 0.3f;

        [Tooltip("Height of the mound mesh's WIDEST ring below the mesh pivot, in mesh units. 0 for food_mound.fbx (its ground ring is its widest, at y 0). The mesh is lifted by this x its Y scale so that ring sits on the ground, which makes the visible oval equal to the collider ellipse (Phase 8 D9).")]
        [SerializeField] private float _widestRingDepth = 0f;

        [Tooltip("Legacy per-pile override. If null, ColorScheme.FoodPileFull is used.")]
        [SerializeField] private Color _fullColorOverride = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Legacy per-pile override. If null/alpha=0, ColorScheme.FoodPileEmpty is used.")]
        [SerializeField] private Color _emptyColorOverride = new Color(0f, 0f, 0f, 0f);

        /// <summary>Smallest fill-ratio change that re-writes the property blocks (0.5 % of a pile).</summary>
        public const float TintWriteThreshold = 0.005f;

        /// <summary>
        /// Tint floor (Phase 8 C S1, approved by Maestro): the pile colour never goes darker than
        /// <c>Lerp(Empty, Full, TintFloor)</c>. At the raw empty colour the low / empty states vanish into the pile disc.
        /// </summary>
        public const float TintFloor = 0.35f;

        /// <summary>Pile colour for a fill ratio: the empty-to-full lerp, clamped at <see cref="TintFloor"/>.</summary>
        public static Color TintFor(Color empty, Color full, float ratio) =>
            Color.Lerp(empty, full, Mathf.Max(Mathf.Clamp01(ratio), TintFloor));

        private FoodPile _pile;
        private float _lastTintRatio = float.NaN;
        private bool _showingEmpty;
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

        /// <summary>Mound Y scale for a fill ratio: linear from <see cref="_emptyHeight"/> to <see cref="_fullHeight"/>.</summary>
        public float MoundHeight(float ratio) => Mathf.Lerp(_emptyHeight, _fullHeight, Mathf.Clamp01(ratio));

        /// <summary>
        /// Pose of the mound root (pile-root local). Food present: Y scale by fill height, widest ring seated on the ground.
        /// Empty: the scattered-grain mesh is authored in footprint space, so scale 1, lifted just above the disc.
        /// </summary>
        public void MoundPose(bool showEmptyMesh, float ratio, out Vector3 scale, out Vector3 localPosition)
        {
            if (showEmptyMesh)
            {
                scale = Vector3.one;
                localPosition = new Vector3(0f, _emptyLift, 0f);
                return;
            }
            float height = MoundHeight(ratio);
            scale = new Vector3(1f, height, 1f);
            localPosition = new Vector3(0f, _widestRingDepth * height, 0f);
        }

        /// <summary>
        /// True when the tint must be re-written: first frame (NaN), a change beyond
        /// <see cref="TintWriteThreshold"/>, or the pile landing exactly on empty / full so the
        /// end colours are never left up to 0.5 % off.
        /// </summary>
        public static bool ShouldWriteTint(float lastWritten, float ratio)
        {
            if (float.IsNaN(lastWritten)) return true;
            float delta = Mathf.Abs(ratio - lastWritten);
            if (delta > TintWriteThreshold) return true;
            return delta > 0f && (ratio <= 0f || ratio >= 1f);
        }

        private void LateUpdate()
        {
            if (_pile == null) return;

            // Object.IsValid means the NetworkObject is spawned and has replicated state.
            if (_pile.Object == null || !_pile.Object.IsValid) return;

            float ratio = _pile.MaxAmount > 0f ? Mathf.Clamp01(_pile.Amount / _pile.MaxAmount) : 0f;

            bool empty = _pile.Amount <= 0f;
            if (_moundFilter != null && _fullMesh != null && _emptyMesh != null && (empty != _showingEmpty || _moundFilter.sharedMesh == null))
            {
                _showingEmpty = empty;
                _moundFilter.sharedMesh = empty ? _emptyMesh : _fullMesh;
            }

            if (_meshRoot != null)
            {
                MoundPose(empty && _emptyMesh != null, ratio, out var scale, out var pos);
                _meshRoot.localScale = scale;
                _meshRoot.localPosition = pos;
            }

            if (_renderers != null && _renderers.Length > 0 && ShouldWriteTint(_lastTintRatio, ratio))
            {
                _lastTintRatio = ratio;
                var color = TintFor(EmptyColor, FullColor, ratio);
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
