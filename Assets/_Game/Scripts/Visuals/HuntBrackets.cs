using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>What the hunter's ground brackets show right now (the colour state the capture harness logs).</summary>
    public enum HuntBracketState : byte
    {
        /// <summary>Not drawn.</summary>
        Hidden = 0,
        /// <summary>The mark is arming: the target's seat colour.</summary>
        Arming = 1,
        /// <summary>Live and the pounce is not legal (wall, egg, range, no stun): red.</summary>
        Blocked = 2,
        /// <summary>Live and a press kills now: yellow, breathing.</summary>
        Pounce = 3,
    }

    /// <summary>
    /// <b>The hunter's ground brackets</b> (K4, design 2026-11-17 H1a(a)): four ink-outlined arcs painted on the floor round the
    /// marked rival, on the screen of the local Assassin who owns the mark and nowhere else, so "who is my target" reads at a
    /// glance whenever that bird is in view. The marker (over the head, or on the floor arrow outside the circle) is untouched;
    /// the brackets add the one sign that is always on the target itself.
    /// </summary>
    /// <remarks>
    /// <para>One scene-level object, created and driven by <see cref="MarkFeedback"/> (never parented under a chicken, so
    /// <c>ChickenVisuals</c>' renderer scan and ghost swap never find it, and no <c>Chicken.prefab</c> edit). One
    /// <see cref="MeshRenderer"/>, one mesh of four arcs built once, the shared <see cref="TelegraphShapes.SharedUnlitSpriteMaterial"/>
    /// with the colour in the vertices, one draw call, no particles. Per frame only the transform moves; vertex colours are
    /// rewritten when the state colour or the alpha changes, and the vertex positions when the target's ring radius changes.</para>
    /// <para>Layering: sorting order <see cref="FeedbackTuning.HuntBracketSortingOrder"/> and a height over the floor arrows put it
    /// above the rival ring, the floor-arrow ring and the arrows, and below the body FX. Alpha follows the marker alpha, so it
    /// holds through the end hold and fades with it. The lock snap on appear and the breathe at pounce-legal stand down under
    /// Reduced Motion (the marker's own pop and pulse values, <see cref="HuntRules.KillPulseFactor"/>).</para>
    /// <para>Purely local presentation: no networked state, no RPC, no injection (the logger is handed in by the owner).</para>
    /// </remarks>
    [ExecuteAlways]   // only so an EditMode test destroying it runs OnDestroy; it is created at runtime, never saved in a scene
    public sealed class HuntBrackets : MonoBehaviour
    {
        private const string Source = "HuntBrackets";

        /// <summary>Cross-section of one stroke: ink | colour | ink, each pair of vertices duplicated so the colours never blend.</summary>
        private const int RowsPerColumn = 6;
        private const int SegmentsPerArc = 8;
        private const int ArcCount = 4;
        private const int ColumnsPerArc = SegmentsPerArc + 1;
        private const int VertsPerArc = ColumnsPerArc * RowsPerColumn + 8;   // + two 4-vertex ink end caps
        private const int VertexCount = VertsPerArc * ArcCount;

        // ---- What the harness and the tests read --------------------------------------------------------------------------

        private static bool _shownNow;
        private static HuntBracketState _stateNow;
        private static Color _colourNow;

        /// <summary>The brackets are drawn this frame (capture harness / tests).</summary>
        public static bool ShownNow => _shownNow;

        /// <summary>Their colour state this frame, <see cref="HuntBracketState.Hidden"/> when not drawn.</summary>
        public static HuntBracketState StateNow => _stateNow;

        /// <summary>The stroke colour drawn this frame (alpha 1; the fade is in the vertices).</summary>
        public static Color ColourNow => _colourNow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => SetShown(HuntBracketState.Hidden, default);

        private static void SetShown(HuntBracketState state, Color colour)
        {
            _stateNow = state;
            _shownNow = state != HuntBracketState.Hidden;
            _colourNow = colour;
        }

        // ---- Pure rules (pinned by tests) -----------------------------------------------------------------------------------

        /// <summary>Drawn only for the local Assassin's own mark while its marker shows (end hold and fade included) and the
        /// target still exists. Never for a victim, a bystander or a clean model.</summary>
        public static bool ShouldShow(in MarkOutputs o, bool hasTarget) =>
            hasTarget && o.Role == MarkRole.Assassin && o.MarkerAlpha > 0f;

        /// <summary>Seat colour while arming, yellow when a press kills now, red in the live second otherwise.</summary>
        public static HuntBracketState StateFor(in MarkOutputs o, bool hasTarget) =>
            !ShouldShow(in o, hasTarget) ? HuntBracketState.Hidden
            : !o.Armed ? HuntBracketState.Arming
            : o.MarkerMode == MarkMarkerMode.Kill ? HuntBracketState.Pounce
            : HuntBracketState.Blocked;

        /// <summary>The stroke colour for the outputs (<see cref="HuntRules.BracketColour"/>, the one home).</summary>
        public static Color ColourFor(in MarkOutputs o) =>
            HuntRules.BracketColour(o.Armed, o.MarkerMode == MarkMarkerMode.Kill, o.TargetCorner);

        /// <summary>The brackets' scale: the lock snap (they close in from the marker's pop-from size at the appear) times the
        /// pounce breathe. Both are exactly 1 under Reduced Motion.</summary>
        public static float ScaleFor(in MarkOutputs o, bool reducedMotion, float unscaledTime)
        {
            float snap = reducedMotion ? 1f : 1f / Mathf.Max(FeedbackTuning.MarkMarkerPopFrom, o.MarkerPop);
            return snap * HuntRules.KillPulseFactor(o.MarkerMode, reducedMotion, unscaledTime);
        }

        /// <summary>The centre-line radius for a target whose rival ring has the given world radius.</summary>
        public static float RadiusFor(float rivalRingWorldRadius) => rivalRingWorldRadius * FeedbackTuning.HuntBracketRadiusFactor;

        /// <summary>The innermost edge of a stroke at <paramref name="radius"/>, outline included.</summary>
        public static float InnerEdge(float radius) =>
            radius - 0.5f * FeedbackTuning.HuntBracketWidth - FeedbackTuning.HuntBracketOutlineWidth;

        // ---- Instance -----------------------------------------------------------------------------------------------------

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private readonly Vector3[] _verts = new Vector3[VertexCount];
        private readonly Color[] _colours = new Color[VertexCount];
        private readonly bool[] _isInk = new bool[VertexCount];
        private float _builtRadius = -1f;
        private Color _shownColour = new Color(-1f, -1f, -1f, -1f);

        /// <summary>The one renderer (tests count it).</summary>
        public MeshRenderer BracketRenderer => _renderer;

        /// <summary>Builds the object under <paramref name="parent"/>: one mesh, one renderer, the shared material, hidden.</summary>
        public static HuntBrackets Create(Transform parent, ILogService log)
        {
            var go = new GameObject("HuntBrackets");
            go.transform.SetParent(parent, worldPositionStays: false);
            var brackets = go.AddComponent<HuntBrackets>();
            brackets.Build(log);
            return brackets;
        }

        private void Build(ILogService log)
        {
            var mat = TelegraphShapes.SharedUnlitSpriteMaterial(log);   // shared URP unlit material, never destroyed here
            if (mat == null)
                log?.Error(Source, "No unlit sprite / line material in the build: the hunter's ground brackets will draw with " +
                    "Unity's default material (magenta).");

            _mesh = new Mesh { name = "HuntBrackets" };
            _mesh.MarkDynamic();
            BuildTopology();
            _mesh.colors = _colours;

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            if (mat != null) _renderer.sharedMaterial = mat;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sortingOrder = FeedbackTuning.HuntBracketSortingOrder;
            _renderer.enabled = false;
        }

        /// <summary>Triangles and the ink / colour role of every vertex: fixed for the session.</summary>
        private void BuildTopology()
        {
            var tris = new System.Collections.Generic.List<int>(ArcCount * (SegmentsPerArc * 3 + 2) * 6);
            for (int a = 0; a < ArcCount; a++)
            {
                int baseIndex = a * VertsPerArc;
                for (int c = 0; c < ColumnsPerArc; c++)
                    for (int r = 0; r < RowsPerColumn; r++)
                        _isInk[baseIndex + c * RowsPerColumn + r] = r != 2 && r != 3;
                for (int s = 0; s < SegmentsPerArc; s++)
                {
                    int c0 = baseIndex + s * RowsPerColumn, c1 = c0 + RowsPerColumn;
                    for (int band = 0; band < RowsPerColumn; band += 2)   // rows (0,1) ink, (2,3) colour, (4,5) ink
                        AddQuad(tris, c0 + band, c0 + band + 1, c1 + band + 1, c1 + band);
                }
                int capBase = baseIndex + ColumnsPerArc * RowsPerColumn;
                for (int k = 0; k < 8; k++) _isInk[capBase + k] = true;
                AddQuad(tris, capBase + 3, capBase + 2, capBase + 1, capBase);   // runs against the bearings: reversed
                AddQuad(tris, capBase + 4, capBase + 5, capBase + 6, capBase + 7);
            }
            _mesh.vertices = _verts;   // zeros until the first radius write; the triangles need the vertex count
            _mesh.SetTriangles(tris, 0);
        }

        private static void AddQuad(System.Collections.Generic.List<int> tris, int a, int b, int c, int d)
        {
            // Clockwise seen from above (bearings run clockwise), the same as the floor arrows; both paint materials are two-sided.
            tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d);
        }

        /// <summary>Lays the four arcs at the centre-line radius <paramref name="radius"/> (local XZ, around the origin).</summary>
        private void WriteVertices(float radius)
        {
            float halfW = 0.5f * FeedbackTuning.HuntBracketWidth, o = FeedbackTuning.HuntBracketOutlineWidth;
            float[] offsets = { -halfW - o, -halfW, -halfW, halfW, halfW, halfW + o };
            float halfSpan = 0.5f * FeedbackTuning.HuntBracketArcDegrees;
            float capDeg = Mathf.Rad2Deg * (o / Mathf.Max(0.01f, radius));
            for (int a = 0; a < ArcCount; a++)
            {
                int baseIndex = a * VertsPerArc;
                float centre = 45f + 90f * a;
                for (int c = 0; c < ColumnsPerArc; c++)
                {
                    Vector3 d = FloorArrowRules.Direction(centre - halfSpan + FeedbackTuning.HuntBracketArcDegrees * c / SegmentsPerArc);
                    for (int r = 0; r < RowsPerColumn; r++) _verts[baseIndex + c * RowsPerColumn + r] = d * (radius + offsets[r]);
                }
                int capBase = baseIndex + ColumnsPerArc * RowsPerColumn;
                WriteCap(capBase, centre - halfSpan, centre - halfSpan - capDeg, radius, halfW + o);
                WriteCap(capBase + 4, centre + halfSpan, centre + halfSpan + capDeg, radius, halfW + o);
            }
            _mesh.vertices = _verts;   // copies the fixed buffer
            _mesh.RecalculateBounds();
            _builtRadius = radius;
        }

        /// <summary>One ink end cap: a quad from the arc's end bearing to a bearing <c>o / radius</c> beyond it.</summary>
        private void WriteCap(int first, float endBearing, float outerBearing, float radius, float halfExtent)
        {
            Vector3 e = FloorArrowRules.Direction(endBearing), x = FloorArrowRules.Direction(outerBearing);
            _verts[first] = e * (radius - halfExtent);
            _verts[first + 1] = e * (radius + halfExtent);
            _verts[first + 2] = x * (radius + halfExtent);
            _verts[first + 3] = x * (radius - halfExtent);
        }

        private void WriteColours(Color stroke, float alpha)
        {
            Color fill = stroke;
            fill.a = alpha;
            Color ink = FeedbackTuning.MarkInk;
            ink.a = FeedbackTuning.HuntBracketInkAlpha * alpha;
            for (int i = 0; i < _colours.Length; i++) _colours[i] = _isInk[i] ? ink : fill;
            _mesh.colors = _colours;   // copies the fixed buffer
        }

        /// <summary>
        /// Drives the brackets for this frame, after <see cref="MarkFeedback"/>'s model step. <paramref name="targetPosition"/> is
        /// the marked chicken's position (its x, z; the height is the brackets' own) and <paramref name="rivalRingWorldRadius"/>
        /// its rival ring's world radius.
        /// </summary>
        public void Apply(in MarkOutputs o, bool hasTarget, Vector3 targetPosition, float rivalRingWorldRadius, bool reducedMotion, float unscaledTime)
        {
            var state = StateFor(in o, hasTarget);
            if (state == HuntBracketState.Hidden || _renderer == null)
            {
                Hide();
                return;
            }

            float radius = RadiusFor(rivalRingWorldRadius);
            if (!Mathf.Approximately(radius, _builtRadius)) WriteVertices(radius);

            Color stroke = ColourFor(in o);
            stroke.a = Mathf.Clamp01(o.MarkerAlpha);   // the marker's own fade (end hold included) rides in alpha
            if (stroke != _shownColour)
            {
                WriteColours(stroke, stroke.a);
                _shownColour = stroke;
            }

            float s = ScaleFor(in o, reducedMotion, unscaledTime);
            transform.SetPositionAndRotation(new Vector3(targetPosition.x, TelegraphShapes.GroundY + FeedbackTuning.HuntBracketHeightOffset, targetPosition.z),
                Quaternion.identity);
            transform.localScale = new Vector3(s, 1f, s);
            _renderer.enabled = true;
            Color shown = stroke;
            shown.a = 1f;
            SetShown(state, shown);
        }

        /// <summary>Not drawn (the next draw rewrites nothing unless the colour or radius changed).</summary>
        public void Hide()
        {
            if (_renderer != null && _renderer.enabled) _renderer.enabled = false;
            SetShown(HuntBracketState.Hidden, default);
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            SetShown(HuntBracketState.Hidden, default);
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
