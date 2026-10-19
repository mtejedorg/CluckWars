using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Re-fits <c>Map.unity</c>'s hand-authored dressing to the arena's current size.
    ///
    /// <para>
    /// <c>MapProps</c> and <c>MapSurroundings</c> are one-off hand-placed content — no
    /// script generates them, so unlike <c>GeneratedMapGeometry</c> they do not follow
    /// when <see cref="CluckWars.Gameplay.MapGenerator"/>'s <c>_planeSize</c> changes.
    /// After the 38 m → 51.3 m rescale the coops, fence and silos were left stranded
    /// well inside the play field. This command exists so that re-fitting them is a
    /// repeatable operation rather than a one-off hand edit that the next arena resize
    /// has to reinvent.
    /// </para>
    ///
    /// <para>
    /// <b>Run it after <see cref="MapSceneBaker"/>.</b> The baker writes the new geometry;
    /// this reads the resulting <c>GeneratedGround</c> to learn what size the arena now
    /// is, so the two never need to agree on a hardcoded number.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent.</b> The scale factor is not a constant — it is derived by comparing
    /// the size the props are currently placed for against the size the baked ground
    /// actually is. Run it twice and the second run finds a factor of 1 and does nothing,
    /// so the dressing can never be scaled by 1.35² by accident.
    /// </para>
    /// </summary>
    public static class MapPropsRescaler
    {
        private const string MapScenePath = "Assets/_Game/Scenes/Map.unity";
        private const string PropsRootName = "MapProps";
        private const string SurroundingsRootName = "MapSurroundings";
        private const string GeometryRootName = "GeneratedMapGeometry";
        private const string GroundName = "GeneratedGround";

        /// <summary>
        /// The outer grass apron is deliberately oversized (260 m) and centred on the
        /// origin, so it already covers any arena we would plausibly build. Scaling it
        /// with everything else would only push its edge further out of frame.
        /// </summary>
        private const string GrassApronName = "SurroundGrass";

        // ---- Authored baselines -------------------------------------------------
        // Measured off the dressing as it was hand-placed, against the 38 m arena.
        // They describe the ORIGINAL layout and must not be "updated" after a rescale —
        // they are what the current placement is compared against to derive the factor.

        private const float AuthoredPlaneSize = 38f;
        private const float AuthoredFenceLine = 18.60f;

        /// <summary>Width of one fence panel — renderer bounds at its authored localScale of 2.</summary>
        private const float FencePanelWidth = 3.99f;

        /// <summary>Half the panel's depth, i.e. centre plane to outer face.</summary>
        private const float FencePanelHalfDepth = 0.245f;

        /// <summary>
        /// The chicken's hard limit on each axis — the INNER FACE of the invisible boundary
        /// wall, which is exactly <c>_planeSize * 0.5</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="CluckWars.Gameplay.MapGenerator"/> centres each boundary wall at
        /// <c>half + t*0.5</c> with thickness <c>t</c>, so the face pointing into the arena
        /// lands back on <c>half</c> exactly. It is easy — and was done once already — to
        /// take the wall's CENTRE (<c>half + 0.25</c>) for this and end up 0.25 m out on
        /// every derived number.
        /// </para>
        /// <para>
        /// This matters more than it looks: <c>_wallsVisible</c> is false and
        /// <c>CreateWall</c> drops the renderer while keeping the BoxCollider, so the
        /// boundary is invisible and <b>this hand-authored fence is the only thing the
        /// player can see</b>. Its registration to that collider is what stops a chicken
        /// appearing to stand outside the world.
        /// </para>
        /// </remarks>
        private static float HardLimit(float planeSize) => planeSize * 0.5f;

        /// <summary>
        /// Gap between the fence's outer face and the hard limit, as authored: 0.155 m.
        /// </summary>
        /// <remarks>
        /// ABSOLUTE, not fractional — it is a clearance measured against a panel of fixed
        /// size, so it must not grow with the arena. Same call as <c>_pilePositionJitter</c>
        /// and the pile footprints. Scaling it fractionally would let a chicken's body
        /// reach 0.295 m past the visible fence instead of the authored 0.155 m.
        /// </remarks>
        private const float FenceStandoff =
            AuthoredPlaneSize * 0.5f - (AuthoredFenceLine + FencePanelHalfDepth);

        /// <summary>
        /// Where the fence's centre plane sits for a given arena. Fed the authored 38 m
        /// this returns exactly 18.60 — that round-trip is the check that this describes
        /// the authored fence rather than merely fitting it.
        /// </summary>
        public static float FenceLine(float planeSize) =>
            HardLimit(planeSize) - FenceStandoff - FencePanelHalfDepth;

        /// <summary>Below this the arena is already the size the props are placed for.</summary>
        private const float NoOpFactorTolerance = 0.001f;

        /// <summary>
        /// How far apart the north fence posts' Z may sit before
        /// <see cref="TryMeasureFenceLine"/> refuses to infer a fence line from them. Well
        /// above float noise on a rebuilt run (they are written from one value) and far below
        /// any real authoring difference.
        /// </summary>
        private const float FenceLineAgreementTolerance = 0.01f;

        private static readonly (string Prefix, bool RunsAlongX, float LineSign)[] FenceSides =
        {
            ("Wall_N_", true,   1f),
            ("Wall_S_", true,  -1f),
            ("Wall_E_", false,  1f),
            ("Wall_W_", false, -1f),
        };

        [MenuItem("Cluck Wars/Map/Rescale Hand-Authored Props", priority = 22)]
        public static void RescaleProps()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[MapPropsRescaler] Cancelled — unsaved changes were kept.");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (scene.path != MapScenePath)
                scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);

            var roots = scene.GetRootGameObjects();
            var props = roots.FirstOrDefault(g => g.name == PropsRootName);
            var geometry = roots.FirstOrDefault(g => g.name == GeometryRootName);

            if (props == null)
            {
                Debug.LogError($"[MapPropsRescaler] No '{PropsRootName}' root in {MapScenePath} — " +
                               "nothing to rescale. If a bake destroyed it, recover it from git.");
                return;
            }

            var ground = geometry != null ? geometry.transform.Find(GroundName) : null;
            if (ground == null)
            {
                Debug.LogError($"[MapPropsRescaler] No '{GeometryRootName}/{GroundName}' in " +
                               $"{MapScenePath}. Run 'Cluck Wars/Map/Bake Map Scene' first — the " +
                               "baked ground is what tells this command how big the arena now is.");
                return;
            }

            float targetPlaneSize = ground.localScale.x;

            if (!TryMeasureFenceLine(props.transform, out float fenceLine))
            {
                Debug.LogError($"[MapPropsRescaler] Could not find a 'Wall_N_*' fence post under " +
                               $"'{PropsRootName}'. The fence line is how this command works out " +
                               "what arena size the dressing is currently placed for.");
                return;
            }

            // What the dressing is currently sized for, inferred from where the fence sits.
            // The inverse of FenceLine(): line = planeSize/2 − standoff − halfDepth.
            float currentPlaneSize = 2f * (fenceLine + FenceStandoff + FencePanelHalfDepth);
            float factor = targetPlaneSize / currentPlaneSize;

            if (Mathf.Abs(factor - 1f) < NoOpFactorTolerance)
            {
                Debug.Log($"[MapPropsRescaler] Nothing to do — the dressing is already placed for a " +
                          $"{currentPlaneSize:F2} m arena and the baked ground is {targetPlaneSize:F2} m.");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rescale hand-authored map props");

            // Props keep their ABSOLUTE size — only their positions move. Precedent:
            // docs/STATE.md records _pilePositionJitter deliberately not being scaled,
            // for the same reason. A coop is a coop whatever size the field is; growing
            // it would silently change how much cover it gives and how far bots detour.
            int movedProps = TranslateChildren(props.transform, factor, skipName: null);

            // MapSurroundings would otherwise be left INSIDE the arena: with the wall out
            // at ±25.99, Bush_2/5/7/8 land on the play field and Tree_17 sits on the wall
            // line. Moving it with everything else preserves the authored composition.
            var surroundings = roots.FirstOrDefault(g => g.name == SurroundingsRootName);
            int movedSurroundings = surroundings != null
                ? TranslateChildren(surroundings.transform, factor, skipName: GrassApronName)
                : 0;

            int fencePosts = RebuildFence(props.transform, targetPlaneSize);

            // TranslateChildren just pushed the arm panels about the origin with everything
            // else, but the arm colliders come from PinwheelLayout, not from a uniform scale —
            // re-lay the art from the baked colliders so the walls stay where you collide.
            RelayArmWalls(props.transform, geometry.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            if (!EditorSceneManager.SaveScene(scene, MapScenePath))
            {
                Debug.LogError($"[MapPropsRescaler] Failed to save {MapScenePath}. The rescale is " +
                               "applied in memory only — undo it or fix the save before continuing.");
                return;
            }

            Debug.Log($"[MapPropsRescaler] Rescaled dressing x{factor:F4} " +
                      $"({currentPlaneSize:F2} m → {targetPlaneSize:F2} m arena): " +
                      $"{movedProps} {PropsRootName} children, {movedSurroundings} {SurroundingsRootName} " +
                      $"children, fence rebuilt to {fencePosts} posts.");
        }

        // ---- Pinwheel-arm wall art ----------------------------------------------

        /// <summary>Baked arm collider names: <c>Terrain_Standard_{arm}</c> under <c>GeneratedMapGeometry</c>.</summary>
        public const string ArmColliderPrefix = "Terrain_Standard_";

        /// <summary>Arm wall art names: <c>InnerWall_{arm}_{panel}</c>, direct children of <c>MapProps</c>.</summary>
        public const string ArmPanelPrefix = "InnerWall_";

        /// <summary>
        /// How many panels cover an arm collider and how much each is stretched along its run,
        /// for panels butted end to end (no shared post). The degenerate case of
        /// <see cref="SolveArmPanels(float, float, float)"/>: post spacing = covered span = panel length.
        /// </summary>
        public static (int Count, float Stretch) SolveArmPanels(float colliderLength, float panelLength) =>
            SolveArmPanels(colliderLength, panelLength, panelLength);

        /// <summary>
        /// How many panels cover an arm collider when neighbours SHARE a post, and how much each
        /// is stretched along its run (Phase 8 C13).
        /// </summary>
        /// <param name="colliderLength">The arm collider's length along its run.</param>
        /// <param name="postSpacing">Unstretched distance between a panel's two post centres. It is
        /// the pitch, so panel k's far post lands exactly on panel k+1's near post.</param>
        /// <param name="endSpan">Unstretched run one panel covers from the middle of its near end
        /// stub to the middle of its far end stub (<see cref="PanelProfile.EndSpan"/>).</param>
        /// <remarks>
        /// <c>n</c> panels cover <c>stretch * ((n - 1) * postSpacing + endSpan)</c>, which must equal
        /// the collider's length exactly. That leaves the count as the only choice; round to the
        /// count whose stretch is closest to 1 so the planks are distorted as little as possible.
        /// </remarks>
        public static (int Count, float Stretch) SolveArmPanels(float colliderLength, float postSpacing, float endSpan)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt((colliderLength - endSpan) / postSpacing) + 1);
            return (count, colliderLength / ((count - 1) * postSpacing + endSpan));
        }

        [MenuItem("Cluck Wars/Map/Re-lay Arm Walls From Colliders", priority = 23)]
        public static void RelayArmWalls()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[MapPropsRescaler] Cancelled — unsaved changes were kept.");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (scene.path != MapScenePath)
                scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);

            var roots = scene.GetRootGameObjects();
            var props = roots.FirstOrDefault(g => g.name == PropsRootName);
            var geometry = roots.FirstOrDefault(g => g.name == GeometryRootName);
            if (props == null || geometry == null)
            {
                Debug.LogError($"[MapPropsRescaler] {MapScenePath} needs both '{PropsRootName}' and " +
                               $"'{GeometryRootName}' to re-lay the arm walls (bake first; recover " +
                               "MapProps from git if a bake destroyed it).");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Re-lay pinwheel arm walls");
            int laid = RelayArmWalls(props.transform, geometry.transform);
            Undo.CollapseUndoOperations(undoGroup);
            if (laid <= 0) return; // already logged why.

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, MapScenePath))
                Debug.LogError($"[MapPropsRescaler] Failed to save {MapScenePath} after re-laying the arm walls.");
        }

        /// <summary>
        /// Rebuilds every <c>InnerWall_{arm}_*</c> run from its baked <c>Terrain_Standard_{arm}</c>
        /// collider: contiguous panels centred on the collider's centre line, covering its full
        /// length, oriented along it. Returns the number of panels laid, or -1 on failure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The colliders are the truth — they are what the chicken, bots and NavMesh collide
        /// with — and the art follows them, never the reverse. The 2026-08-14 ×1.35 rescale moved
        /// the art; the 2026-08-19 revert re-baked the colliders but not the art, leaving
        /// 3.6–4.5 m of invisible wall at each arm's hub end and 0.65 m see-through gaps between
        /// panels (four 2 m panels on a 2.55 m pitch). Deriving the art from the colliders means
        /// the two can only drift again if somebody forgets to run this after a bake.
        /// </para>
        /// <para>
        /// Existing panels are reused (keeping their prefab link); missing ones are cloned from
        /// the arm's first panel, surplus ones deleted. Only the panel roots' position, rotation
        /// and Z (run-axis) scale are written — mesh, material and height stay as authored.
        /// Panels face outward from the hub so the 4-fold symmetry is exact.
        /// </para>
        /// <para>
        /// Phase 8 C13, shared posts: the panel mesh's two posts stand ~14 cm in from its ends and
        /// the rails run on past them as stubs, so panels butted end to end read as a doubled
        /// post with rail nubs between at every joint. Panels are laid on a pitch of their own
        /// post spacing instead: panel k's far post and panel k+1's near post are one post, and
        /// k's stub is buried in k+1's rails. The stub at each run END cannot go without editing
        /// the mesh (pinned to the fence mesh by the B4/C1 tests), so the collider end sits at its
        /// midpoint: the end post stands ~7 cm inside where the chicken stops and the nub ends
        /// ~7 cm past it, inside the ±0.1 m coverage tolerance, instead of a 15 cm nub.
        /// </para>
        /// </remarks>
        public static int RelayArmWalls(Transform props, Transform geometry)
        {
            var arms = geometry.Cast<Transform>()
                .Select(t => (Transform: t, Arm: ArmIndexOf(t.name)))
                .Where(a => a.Arm >= 0)
                .OrderBy(a => a.Arm)
                .ToList();
            if (arms.Count == 0)
            {
                Debug.LogError($"[MapPropsRescaler] No '{ArmColliderPrefix}*' arm colliders under " +
                               $"'{geometry.name}' — nothing to lay the arm walls against. Bake first.");
                return -1;
            }

            var anyPanel = props.Cast<Transform>().FirstOrDefault(t => PanelIndexOf(t.name).Arm >= 0);
            if (anyPanel == null)
            {
                Debug.LogError($"[MapPropsRescaler] No '{ArmPanelPrefix}*' panel under '{props.name}' to " +
                               "use as a template — the arm wall art is missing (recover it from git).");
                return -1;
            }

            int total = 0;
            foreach (var (collider, arm) in arms)
            {
                var box = collider.GetComponent<BoxCollider>();
                if (box == null)
                {
                    Debug.LogError($"[MapPropsRescaler] '{collider.name}' has no BoxCollider — skipped; " +
                                   $"its {ArmPanelPrefix}{arm}_* art was left as it was.");
                    continue;
                }

                var panels = props.Cast<Transform>()
                    .Select(t => (Transform: t, Index: PanelIndexOf(t.name)))
                    .Where(p => p.Index.Arm == arm)
                    .OrderBy(p => p.Index.Panel)
                    .Select(p => p.Transform)
                    .ToList();
                var template = panels.Count > 0 ? panels[0] : anyPanel;

                if (!TryMeasurePanelProfile(template, out var profile))
                {
                    Debug.LogError($"[MapPropsRescaler] '{template.name}' has no mesh with two posts to " +
                                   $"measure — arm {arm} skipped.");
                    continue;
                }

                // The collider's long horizontal axis is the arm's run.
                Vector3 size = Vector3.Scale(box.size, collider.lossyScale);
                bool alongX = size.x >= size.z;
                Vector3 axis = (alongX ? collider.right : collider.forward);
                axis.y = 0f;
                axis.Normalize();
                float length = alongX ? size.x : size.z;
                Vector3 centre = collider.TransformPoint(box.center);
                if (Vector3.Dot(axis, new Vector3(centre.x, 0f, centre.z)) < 0f) axis = -axis; // outward

                var (count, stretch) = SolveArmPanels(length, profile.PostSpacing, profile.EndSpan);
                float pitch = profile.PostSpacing * stretch;
                Vector3 start = centre - axis * (length * 0.5f);
                // Panel 0's near end-stub midpoint sits on the collider's start (see remarks).
                Vector3 origin0 = start - axis * (stretch * (profile.RunMin + profile.NearStub * 0.5f));
                var rotation = Quaternion.LookRotation(axis, Vector3.up);
                float y = template.position.y;
                // Fresh prefab instances come in at the prefab's scale; keep the template's.
                Vector2 crossScale = new Vector2(template.localScale.x, template.localScale.y);

                while (panels.Count > count)
                {
                    Undo.DestroyObjectImmediate(panels[^1].gameObject);
                    panels.RemoveAt(panels.Count - 1);
                }
                while (panels.Count < count)
                {
                    int after = (panels.Count > 0 ? panels[^1] : template).GetSiblingIndex();
                    var clone = ClonePost(template, props);
                    clone.SetSiblingIndex(after + 1);
                    panels.Add(clone);
                }

                for (int k = 0; k < count; k++)
                {
                    var panel = panels[k];
                    Undo.RecordObject(panel, "Re-lay arm wall");
                    panel.gameObject.name = $"{ArmPanelPrefix}{arm}_{k}";
                    Vector3 pos = origin0 + axis * (pitch * k);
                    panel.SetPositionAndRotation(new Vector3(pos.x, y, pos.z), rotation);
                    panel.localScale = new Vector3(crossScale.x, crossScale.y, stretch);
                    total++;
                }
            }

            foreach (Transform t in props)
            {
                int arm = PanelIndexOf(t.name).Arm;
                if (arm >= 0 && arms.All(a => a.Arm != arm))
                    Debug.LogWarning($"[MapPropsRescaler] '{t.name}' has no '{ArmColliderPrefix}{arm}' " +
                                     "collider — left where it is. Delete it if that arm is gone for good.");
            }

            Debug.Log($"[MapPropsRescaler] Re-laid {total} arm wall panels over {arms.Count} arm colliders.");
            return total;
        }

        /// <summary>
        /// A panel's run profile in its root's own space (local Z, the axis
        /// <see cref="RelayArmWalls(Transform, Transform)"/> lays along) with the root's scale divided
        /// out, so it is the same however the panel is currently stretched: where the mesh starts
        /// and ends and where its two posts stand. Phase 8 C13.
        /// </summary>
        public readonly struct PanelProfile
        {
            public readonly float RunMin, RunMax;
            /// <summary>(min, max) of each post along the run; near = the -Z (hub-side) one.</summary>
            public readonly Vector2 NearPost, FarPost;

            public PanelProfile(float runMin, float runMax, Vector2 nearPost, Vector2 farPost)
            {
                RunMin = runMin; RunMax = runMax; NearPost = nearPost; FarPost = farPost;
            }

            public float PostSpacing => (FarPost.x + FarPost.y - NearPost.x - NearPost.y) * 0.5f;
            public float PostWidth => Mathf.Max(NearPost.y - NearPost.x, FarPost.y - FarPost.x);
            /// <summary>Rail stub reaching past the near post's outer face.</summary>
            public float NearStub => NearPost.x - RunMin;
            /// <summary>Rail stub reaching past the far post's outer face.</summary>
            public float FarStub => RunMax - FarPost.y;
            /// <summary>Run from the middle of the near end stub to the middle of the far one.</summary>
            public float EndSpan => RunMax - RunMin - (NearStub + FarStub) * 0.5f;
        }

        /// <summary>Top fraction of a panel's height that only its post caps reach.</summary>
        /// <remarks>wall_segment: rails top out at 1.27 m, post caps at 1.38-1.46 m of 1.49 m (top 8 % = above 1.34 m).</remarks>
        public const float PostCapBand = 0.08f;

        /// <summary>
        /// Measures <paramref name="panelRoot"/>'s <see cref="PanelProfile"/> from its mesh vertices.
        /// The posts are the only geometry reaching the top <see cref="PostCapBand"/> of the panel's
        /// height; those vertices on either side of the run's middle give each post's extent.
        /// </summary>
        /// <remarks>
        /// Read off the mesh, not hardcoded: <c>Prop_wall_segment</c>'s run is its source mesh's Y,
        /// turned onto the root's Z by the inner model's -90° X rotation and squashed by its
        /// 0.35 X scale — a stored constant would go stale the moment the prefab is touched.
        /// </remarks>
        public static bool TryMeasurePanelProfile(Transform panelRoot, out PanelProfile profile)
        {
            profile = default;
            // Child-to-root with the root's own scale divided back out.
            var toRoot = Matrix4x4.TRS(panelRoot.position, panelRoot.rotation, Vector3.one).inverse;
            var rootScale = panelRoot.lossyScale;
            var points = new List<Vector3>();
            foreach (var filter in panelRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var m = toRoot * filter.transform.localToWorldMatrix;
                foreach (var v in filter.sharedMesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    points.Add(new Vector3(p.x / rootScale.x, p.y / rootScale.y, p.z / rootScale.z));
                }
            }
            if (points.Count == 0) return false;

            float runMin = points.Min(p => p.z), runMax = points.Max(p => p.z);
            float bottom = points.Min(p => p.y), top = points.Max(p => p.y);
            float capLine = top - (top - bottom) * PostCapBand, mid = (runMin + runMax) * 0.5f;
            var caps = points.Where(p => p.y >= capLine).Select(p => p.z).ToList();
            var near = caps.Where(z => z < mid).ToList();
            var far = caps.Where(z => z >= mid).ToList();
            if (near.Count == 0 || far.Count == 0) return false;

            profile = new PanelProfile(runMin, runMax,
                new Vector2(near.Min(), near.Max()), new Vector2(far.Min(), far.Max()));
            return true;
        }


        private static int ArmIndexOf(string name) =>
            name.StartsWith(ArmColliderPrefix) && int.TryParse(name[ArmColliderPrefix.Length..], out int i) ? i : -1;

        private static (int Arm, int Panel) PanelIndexOf(string name)
        {
            if (!name.StartsWith(ArmPanelPrefix)) return (-1, -1);
            var parts = name[ArmPanelPrefix.Length..].Split('_');
            return parts.Length == 2 && int.TryParse(parts[0], out int arm) && int.TryParse(parts[1], out int panel)
                ? (arm, panel)
                : (-1, -1);
        }

        /// <summary>
        /// Reads the fence line off the first north-fence post. Positions are what the
        /// dressing's target arena size is inferred from, so this deliberately measures
        /// the scene rather than trusting a stored number that could go stale.
        /// </summary>
        /// <remarks>
        /// Every north post is checked, not just the first one found. The whole rescale
        /// factor — and therefore the position of all 121 props — is derived from this single
        /// number, so adopting one outlier silently would misplace the entire arena's
        /// dressing. <see cref="RebuildFence"/> always leaves the run at a uniform Z, so
        /// disagreement means the scene was hand-edited or a previous run was interrupted
        /// part-way; either way that is a stop-and-tell-someone condition, not something to
        /// average over.
        /// </remarks>
        private static bool TryMeasureFenceLine(Transform props, out float line)
        {
            line = 0f;
            bool found = false;
            float min = float.MaxValue, max = float.MinValue;

            foreach (Transform child in props)
            {
                if (!child.name.StartsWith("Wall_N_")) continue;
                float z = Mathf.Abs(child.position.z);
                min = Mathf.Min(min, z);
                max = Mathf.Max(max, z);
                found = true;
            }

            if (!found) return false;

            if (max - min > FenceLineAgreementTolerance)
            {
                Debug.LogError(
                    $"[MapPropsRescaler] The north fence posts disagree about where the fence " +
                    $"line is (min {min:F3}, max {max:F3}, spread {max - min:F3} > " +
                    $"{FenceLineAgreementTolerance}). The rescale factor for all {props.childCount} " +
                    "props is derived from this one measurement, so refusing rather than " +
                    "picking one. Was the scene hand-edited, or a previous run interrupted?");
                return false;
            }

            line = max;
            return line > 0.01f;
        }

        /// <summary>
        /// Scales every placed prop's position about the world origin in X and Z. Y is
        /// left alone — the ground did not move vertically, so anything resting on it
        /// must not either.
        /// </summary>
        /// <remarks>
        /// A direct child sitting exactly on the world origin with children of its own is
        /// an organisational container, not a placed prop — <c>MapProps/FenceDressing</c>
        /// is one, and its 16 scarecrows and milk churns hold the real positions. Moving
        /// the container would be a no-op (it is already at the origin) and would leave
        /// its contents stranded, so we recurse into it instead. No actual prop sits on
        /// the origin, which is what makes that test safe.
        /// </remarks>
        private static int TranslateChildren(Transform root, float factor, string skipName)
        {
            int moved = 0;
            foreach (Transform child in root)
            {
                if (child.name == skipName) continue;

                var p = child.position;
                bool isContainer = child.childCount > 0 &&
                                   Mathf.Abs(p.x) < 0.001f && Mathf.Abs(p.z) < 0.001f;
                if (isContainer)
                {
                    moved += TranslateChildren(child, factor, skipName);
                    continue;
                }

                Undo.RecordObject(child, "Rescale map prop");
                child.position = new Vector3(p.x * factor, p.y, p.z * factor);
                moved++;
            }
            return moved;
        }

        /// <summary>
        /// Rebuilds each fence run from the layout rule rather than stretching it.
        /// </summary>
        /// <remarks>
        /// Translating the posts alone would scale the 3.72 m spacing to 5.02 m while the
        /// panels stayed 3.99 m wide, turning a solid fence into a picket line with a 1 m
        /// hole between every panel, plus a gap at each corner. Instead the run is re-laid
        /// from one rule: <b>the covered extent equals the fence square's side (2F)</b>, so
        /// the end panels' outer edges land on exactly ±F — the centre plane of the
        /// perpendicular run — and every corner seals by construction rather than by a
        /// tuned inset. Post count is then the fewest that keeps panels touching.
        /// <para>
        /// Fed the authored 38 m arena this returns <b>10 posts</b>, which is exactly what
        /// is hand-placed in the scene — the round-trip that shows the rule describes this
        /// fence rather than merely fitting it. At 51.3 m it returns 13.
        /// </para>
        /// <para>
        /// Overlap is deliberately kept SMALL rather than maximised.
        /// <c>Prop_wall_segment.prefab</c> sets <c>m_LocalScale.x = 0.35</c> on its inner
        /// model — the source mesh is squashed ~3× along its run — so every metre of
        /// overlap doubles ~3 m of original geometry as coplanar plank faces, which
        /// z-fights. At 51.3 m this rule overlaps 0.114 m (doubling ~3% of the panel),
        /// better than the authored 0.27 m (~6.8%). One more post would overlap 0.41 m
        /// (~31%) and stripe the fence.
        /// </para>
        /// </remarks>
        /// <summary>
        /// The whole fence layout as a pure function of arena size. Public so the EditMode
        /// suite can pin the authored round-trip (38 m must return the 10 posts that are
        /// actually hand-placed in <c>Map.unity</c>) without going near a scene.
        /// </summary>
        public static (float Line, int PostCount, float Spacing) SolveFenceLayout(float planeSize)
        {
            float line = FenceLine(planeSize);

            // Span between the FIRST and LAST post centres. Adding a panel width to it
            // gives the covered extent, which we want to equal the full side, 2F.
            float centreSpan = 2f * line - FencePanelWidth;

            // Ceil, so spacing only ever tightens: panels must never leave a gap.
            // The epsilon stops an exact division from rounding up a whole extra post.
            int intervals = Mathf.Max(1, Mathf.CeilToInt(centreSpan / FencePanelWidth - 0.0001f));
            return (line, intervals + 1, centreSpan / intervals);
        }

        private static int RebuildFence(Transform props, float planeSize)
        {
            var (lineCoord, count, _) = SolveFenceLayout(planeSize);
            int intervals = count - 1;
            float halfSpan = (2f * lineCoord - FencePanelWidth) * 0.5f;

            int total = 0;
            foreach (var (prefix, runsAlongX, lineSign) in FenceSides)
            {
                var posts = props.Cast<Transform>()
                    .Where(t => t.name.StartsWith(prefix))
                    .OrderBy(t => TrailingIndex(t.name))
                    .ToList();

                if (posts.Count == 0)
                {
                    Debug.LogWarning($"[MapPropsRescaler] No '{prefix}*' fence posts found — " +
                                     "that side of the fence is missing and was left alone.");
                    continue;
                }

                var template = posts[0];
                while (posts.Count > count)
                {
                    Undo.DestroyObjectImmediate(posts[^1].gameObject);
                    posts.RemoveAt(posts.Count - 1);
                }
                while (posts.Count < count)
                    posts.Add(ClonePost(template, props));

                for (int i = 0; i < count; i++)
                {
                    float along = -halfSpan + i * (2f * halfSpan / intervals);
                    float line = lineSign * lineCoord;

                    var post = posts[i];
                    Undo.RecordObject(post, "Rebuild fence");
                    post.gameObject.name = prefix + i;
                    post.position = runsAlongX
                        ? new Vector3(along, template.position.y, line)
                        : new Vector3(line, template.position.y, along);
                    post.rotation = template.rotation;
                    post.localScale = template.localScale;
                    total++;
                }
            }
            return total;
        }

        /// <summary>
        /// Clones a fence post, keeping its prefab link where there is one so the new
        /// segments stay as editable as the hand-placed ones.
        /// </summary>
        private static Transform ClonePost(Transform template, Transform parent)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(template.gameObject);
            var clone = source != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(source, parent)
                : Object.Instantiate(template.gameObject, parent);

            // A fresh prefab instance drops the template's mesh override - the arm panels use
            // wall_segment_inner (Phase 8 B4: UVs compensated for their 0.35 thickness scale).
            var srcFilters = template.GetComponentsInChildren<MeshFilter>(true);
            var dstFilters = clone.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < Mathf.Min(srcFilters.Length, dstFilters.Length); i++)
                if (dstFilters[i].sharedMesh != srcFilters[i].sharedMesh)
                    dstFilters[i].sharedMesh = srcFilters[i].sharedMesh;

            // ...and its material override: arm panels use Prop_wall_segment_Inner.mat (Phase 8 D4: no
            // _CW_FENCE_ALL_BIRDS, so they keep the local-bird-only hole), the prefab default is the outer fence.
            var srcRenderers = template.GetComponentsInChildren<Renderer>(true);
            var dstRenderers = clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < Mathf.Min(srcRenderers.Length, dstRenderers.Length); i++)
                if (!srcRenderers[i].sharedMaterials.SequenceEqual(dstRenderers[i].sharedMaterials))
                    dstRenderers[i].sharedMaterials = srcRenderers[i].sharedMaterials;

            Undo.RegisterCreatedObjectUndo(clone, "Add fence post");
            return clone.transform;
        }

        /// <summary>Sorts "Wall_N_10" after "Wall_N_9" instead of alphabetically before it.</summary>
        private static int TrailingIndex(string name)
        {
            int split = name.LastIndexOf('_');
            return split >= 0 && int.TryParse(name[(split + 1)..], out int i) ? i : int.MaxValue;
        }
    }
}
