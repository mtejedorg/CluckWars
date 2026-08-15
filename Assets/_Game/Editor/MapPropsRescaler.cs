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
        private static float FenceLine(float planeSize) =>
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
