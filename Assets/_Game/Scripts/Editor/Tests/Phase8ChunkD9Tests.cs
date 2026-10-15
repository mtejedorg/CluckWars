using System.Collections.Generic;
using System.Linq;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D9 (Maestro: "a") - pile fairness.
    /// <list type="number">
    /// <item>Outer piles get no independent per-pile jitter: the shipped nudge radius is 0, and any
    /// non-zero nudge is ONE vector per match rotated 90 degrees per corner, so the four seats are
    /// exact rotations of each other.</item>
    /// <item>The pile collides on its VISIBLE oval: a convex ellipse prism (was the footprint's
    /// bounding box, 0.7-1.5 m proud at the diagonals); the NavMesh carve is a staircase of the
    /// same ellipse; the mound's ground ring and the baked zone disc are the same ellipse.</item>
    /// <item>Outer ovals point their long axis at the arena centre (4-fold).</item>
    /// </list>
    /// Every number is read from the shipped Game.unity / Map.unity / prefabs / class assets.
    /// </summary>
    public class Phase8ChunkD9Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string FoodPilePrefabPath = "Assets/_Game/Prefabs/FoodPile.prefab";
        private const string ChickenPrefabPath = "Assets/_Game/Prefabs/Chicken.prefab";

        /// <summary>Collider vs visible oval, all axes incl. diagonals (brief: 0.1 m).</summary>
        private const float ColliderTolerance = 0.1f;

        /// <summary>NavMesh carve staircase vs the same ellipse (see PileFootprintShape.CarveBoxCount).</summary>
        private const float CarveTolerance = 0.16f;

        /// <summary>CharacterController radius 0.5 + skin 0.08: where a chicken pressed against a pile stands.</summary>
        private const float ChickenContactGap = 0.58f;

        private string _originalScenePath;

        [SetUp]
        public void CaptureOpenScene() => _originalScenePath = SceneManager.GetActiveScene().path;

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        // ---- shipped layout (Game.unity values + the generator's own formulas) -----------------

        private struct PileSpec
        {
            public string Name;
            public Vector3 Pos;
            public float Yaw;
            public Vector2 Footprint;
            public float Food;
        }

        private static Vector3 Rot90(Vector3 v, int turns)
        {
            for (int k = 0; k < ((turns % 4) + 4) % 4; k++) v = new Vector3(-v.z, 0f, v.x);
            return v;
        }

        /// <summary>The 9 piles exactly as <c>MapGenerator.SpawnFoodPiles</c> places them for a given per-match nudge.</summary>
        private static List<PileSpec> ShippedPiles(Vector2 personalNudge, Vector2 contestedNudge)
        {
            var corners = ShippedMap.Corners();
            var piles = new List<PileSpec>
            {
                new PileSpec { Name = "Center", Pos = Vector3.zero, Yaw = 0f, Footprint = ShippedMap.CentrePileFootprint,
                               Food = TestAssets.SceneFloat("_centerPileAmount") },
            };
            for (int i = 0; i < 4; i++)
            {
                var p = MapGenerator.OuterPilePosition(MapGenerator.PersonalPileNominal(corners[i], ShippedMap.PersonalPileInset), personalNudge, i);
                piles.Add(new PileSpec { Name = $"Personal{i}", Pos = p, Yaw = PileFootprintShape.RadialLongAxisYaw(p),
                                         Footprint = ShippedMap.PersonalPileFootprint, Food = TestAssets.SceneFloat("_personalPileAmount") });
            }
            for (int i = 0; i < 4; i++)
            {
                var p = MapGenerator.OuterPilePosition(
                    MapGenerator.ContestedPileNominal(corners[i], corners[(i + 1) % 4], ShippedMap.ContestedEdgeInset), contestedNudge, i);
                piles.Add(new PileSpec { Name = $"Contested{i}", Pos = p, Yaw = PileFootprintShape.RadialLongAxisYaw(p),
                                         Footprint = ShippedMap.ContestedPileFootprint, Food = TestAssets.SceneFloat("_contestedPileAmount") });
            }
            return piles;
        }

        private static Vector3[] ShippedBases() => ShippedMap.Corners().Select(MapGenerator.SpawnPointNominal).ToArray();

        private static float EllipseRadius(float a, float b, float localAngle)
        {
            float c = Mathf.Cos(localAngle), s = Mathf.Sin(localAngle);
            return a * b / Mathf.Sqrt(b * b * c * c + a * a * s * s);
        }

        // ---- 1. jitter --------------------------------------------------------------------------

        [Test]
        public void ShippedJitter_IsZero_PilesSitOnTheirZoneDiscs()
        {
            Assert.AreEqual(0f, ShippedMap.PilePositionJitter, 1e-6f,
                "D9 = a: piles ship with no per-match position jitter (BalanceOracle: any jitter changes SCT match to match; 0 keeps every seat on the solved nominal layout and every pile concentric with its baked zone disc).");
            Assert.AreEqual(MapGenerator.PileKeepClearSlack, ShippedMap.PileKeepClearAllowance, 1e-6f,
                "The terrain keep-clear inflation must stay 0.4 so a re-bake reproduces the shipped arms.");
        }

        [Test]
        public void Nudge_IsExactlyFourFold_ForAnyMatchNudge()
        {
            var rng = new System.Random(9);
            for (int trial = 0; trial < 50; trial++)
            {
                var pn = new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                var cn = new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                var piles = ShippedPiles(pn, cn);
                for (int i = 1; i < 4; i++)
                {
                    foreach (var tier in new[] { "Personal", "Contested" })
                    {
                        var p0 = piles.First(p => p.Name == tier + "0");
                        var pi = piles.First(p => p.Name == tier + i);
                        var expected = Rot90(p0.Pos, i);
                        Assert.Less((pi.Pos - expected).magnitude, 1e-4f, $"{tier}{i} is not seat 0's {tier} pile turned {90 * i} degrees (trial {trial}).");
                        float dYaw = Mathf.DeltaAngle(p0.Yaw + 90f * i, pi.Yaw);
                        Assert.Less(Mathf.Abs(Mathf.Repeat(dYaw + 90f, 180f) - 90f), 1e-3f, $"{tier}{i} yaw is not seat 0's + {90 * i} (mod 180).");
                    }
                }
            }
        }

        // ---- 3. orientation ---------------------------------------------------------------------

        [Test]
        public void OuterOvals_PointTheirLongAxisAtTheCentre()
        {
            foreach (var p in ShippedPiles(Vector2.zero, Vector2.zero).Where(p => p.Name != "Center"))
            {
                Assert.Greater(p.Footprint.x, p.Footprint.y, $"{p.Name}: local X must be the long axis.");
                var longAxis = Quaternion.Euler(0f, p.Yaw, 0f) * Vector3.right;
                var toCentre = (-p.Pos).normalized;
                float angle = Mathf.Acos(Mathf.Clamp(Mathf.Abs(Vector3.Dot(longAxis, toCentre)), 0f, 1f)) * Mathf.Rad2Deg;
                Assert.Less(angle, 2f, $"{p.Name}: long axis is {angle:F2} degrees off the centre.");
            }
        }

        [Test]
        public void MapScene_ZoneDiscs_AreTheShippedOvals()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var geo = scene.GetRootGameObjects().First(g => g.name == "GeneratedMapGeometry").transform;
            foreach (var p in ShippedPiles(Vector2.zero, Vector2.zero))
            {
                var disc = geo.Find("PileZone_" + p.Name);
                Assert.IsNotNull(disc, $"Map.unity has no PileZone_{p.Name}.");
                Assert.Less(Vector2.Distance(new Vector2(disc.position.x, disc.position.z), new Vector2(p.Pos.x, p.Pos.z)), 0.01f, $"{disc.name} is not on its pile.");
                Assert.AreEqual(p.Footprint.x, disc.lossyScale.x, 0.01f, $"{disc.name} X size");
                Assert.AreEqual(p.Footprint.y, disc.lossyScale.z, 0.01f, $"{disc.name} Z size");
                float d = Mathf.Abs(Mathf.Repeat(Mathf.DeltaAngle(disc.eulerAngles.y, p.Yaw) + 90f, 180f) - 90f);
                Assert.Less(d, 2f, $"{disc.name} yaw {disc.eulerAngles.y:F1} vs the spawned pile's {p.Yaw:F1}.");
            }
        }

        // ---- 2. collision shape -------------------------------------------------------------------

        /// <summary>A spawned-pile stand-in: the real prefab with its real blocker, at a pile's yaw and current-step scale.</summary>
        private static (GameObject root, FoodPile pile) BuildPile(PileSpec spec, float footprintScale)
        {
            // Never instantiate into a real scene (Bootstrap would go dirty and block the next run).
            if (!string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath);
            var go = Object.Instantiate(prefab);
            var pile = go.GetComponent<FoodPile>();
            var height = TestAssets.PrivateField<float>(pile, "_blockerHeight");
            go.transform.SetPositionAndRotation(spec.Pos, Quaternion.Euler(0f, spec.Yaw, 0f));
            go.transform.localScale = new Vector3(spec.Footprint.x * footprintScale, height, spec.Footprint.y * footprintScale);
            typeof(FoodPile).GetMethod("CreateBlocker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(pile, null);
            Physics.SyncTransforms();
            return (go, pile);
        }

        private static IEnumerable<float> FootprintStepScales(GameObject prefab)
        {
            var pile = prefab.GetComponent<FoodPile>();
            int steps = TestAssets.PrivateField<int>(pile, "_footprintSteps");
            float min = TestAssets.PrivateField<float>(pile, "_minFootprintScale");
            float k = TestAssets.PrivateField<float>(pile, "_shrinkExponent");
            for (int s = 1; s <= steps; s++) yield return FoodPileMath.FootprintScale(s, steps, min, k);
        }

        [Test]
        public void Blocker_IsAConvexEllipsePrism_WithinTolerance_OfTheVisibleOval_AtEveryFillStep()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath);
            float worst = 0f; string worstAt = "";
            foreach (var spec in ShippedPiles(Vector2.zero, Vector2.zero))
            foreach (float scale in FootprintStepScales(prefab))
            {
                var (go, _) = BuildPile(spec, scale);
                try
                {
                    var blocker = go.transform.Find("Blocker");
                    Assert.IsNotNull(blocker, "FoodPile.CreateBlocker made no Blocker child.");
                    Assert.IsEmpty(blocker.GetComponents<BoxCollider>(), "The old bounding-box collider is back.");
                    var hull = blocker.GetComponent<MeshCollider>();
                    Assert.IsNotNull(hull, "Blocker has no MeshCollider.");
                    Assert.IsTrue(hull.convex, "Blocker MeshCollider must be convex.");
                    Assert.IsFalse(hull.isTrigger);

                    float a = spec.Footprint.x * scale * 0.5f, b = spec.Footprint.y * scale * 0.5f;
                    for (int i = 0; i < 72; i++)
                    {
                        float local = i * 5f * Mathf.Deg2Rad; // includes 0/90 (axes) and 45 (diagonals)
                        var dirLocal = new Vector3(Mathf.Cos(local), 0f, Mathf.Sin(local));
                        var dir = Quaternion.Euler(0f, spec.Yaw, 0f) * dirLocal;
                        var origin = spec.Pos + dir * 20f + Vector3.up * 0.5f;
                        Assert.IsTrue(hull.Raycast(new Ray(origin, -dir), out var hit, 40f), $"{spec.Name}: no hit at {i * 5} deg");
                        float r = Vector2.Distance(new Vector2(hit.point.x, hit.point.z), new Vector2(spec.Pos.x, spec.Pos.z));
                        float err = Mathf.Abs(r - EllipseRadius(a, b, local));
                        if (err > worst) { worst = err; worstAt = $"{spec.Name} step x{scale:F2} at {i * 5} deg"; }
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            Debug.Log($"[D9] collider vs oval: worst {worst:F4} m ({worstAt})");
            Assert.Less(worst, ColliderTolerance, $"Collider strays {worst:F3} m from the visible oval at {worstAt}.");
        }

        [Test]
        public void Carve_StaircaseMatchesTheSameEllipse_BothWays()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath);
            float worst = 0f; string worstAt = "";
            foreach (var spec in ShippedPiles(Vector2.zero, Vector2.zero).Where(p => p.Name is "Center" or "Contested0" or "Personal0"))
            {
                var (go, _) = BuildPile(spec, 1f);
                try
                {
                    var obstacles = go.GetComponentsInChildren<NavMeshObstacle>(true);
                    Assert.AreEqual(PileFootprintShape.CarveBoxCount, obstacles.Length);
                    var halves = obstacles.Select(o =>
                    {
                        Assert.AreEqual(NavMeshObstacleShape.Box, o.shape);
                        Assert.IsTrue(o.carving, "pile obstacles must carve");
                        var s = Vector3.Scale(o.size, o.transform.lossyScale);
                        return new Vector2(s.x * 0.5f, s.z * 0.5f);
                    }).ToArray();
                    float a = spec.Footprint.x * 0.5f, b = spec.Footprint.y * 0.5f;
                    // ellipse -> union (inward notches) and union corners -> ellipse (outward), first quadrant (symmetric)
                    for (int i = 0; i <= 900; i++)
                    {
                        float t = i * 0.1f * Mathf.Deg2Rad;
                        var e = new Vector2(a * Mathf.Cos(t), b * Mathf.Sin(t));
                        float toUnion = halves.Min(h => new Vector2(Mathf.Max(0f, e.x - h.x), Mathf.Max(0f, e.y - h.y)).magnitude);
                        if (toUnion > worst) { worst = toUnion; worstAt = $"{spec.Name} notch {i * 0.1f:F1} deg"; }
                    }
                    foreach (var h in halves)
                    {
                        if (PileFootprintShape.InsideEllipse(h.x, h.y, a, b)) continue;
                        float d = Vector2.Distance(h, PileFootprintShape.ClosestPointOnEllipse(h.x, h.y, a, b));
                        if (d > worst) { worst = d; worstAt = $"{spec.Name} corner {h}"; }
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            Debug.Log($"[D9] carve vs oval: worst {worst:F4} m ({worstAt})");
            Assert.Less(worst, CarveTolerance, $"Carve strays {worst:F3} m from the oval at {worstAt}.");
        }

        [Test]
        public void Mound_GroundRing_IsTheColliderOval_AndNeverPokesOutsideIt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath);
            var visuals = prefab.GetComponent<FoodPileVisuals>();
            var meshRoot = TestAssets.PrivateField<Transform>(visuals, "_meshRoot");
            float depth = TestAssets.PrivateField<float>(visuals, "_widestRingDepth");
            Assert.AreEqual(Vector3.one.x, meshRoot.localScale.x, 1e-4f, "The mound must not carry its own X,Z scale.");
            Assert.AreEqual(Vector3.one.z, meshRoot.localScale.z, 1e-4f);

            var mesh = PlaceholderMeshFactory.Get(PlaceholderMeshFactory.Kind.FoodMound);
            foreach (float ratio in new[] { 1f, 0.5f, 0.05f })
            {
                float h = visuals.MoundHeight(ratio);
                var trs = Matrix4x4.TRS(new Vector3(0f, depth * h, 0f), Quaternion.identity, new Vector3(1f, h, 1f));
                float widestAboveGround = 0f, maxR = 0f;
                foreach (var v in mesh.vertices)
                {
                    var p = trs.MultiplyPoint3x4(v);     // pile-root local: unit footprint, ground at y = 0
                    float r = new Vector2(p.x, p.z).magnitude;
                    if (p.y < -1e-3f) continue;          // buried
                    maxR = Mathf.Max(maxR, r);
                    widestAboveGround = Mathf.Max(widestAboveGround, r);
                }
                // In unit space the oval is radius 0.5; on the 7.8 m centre pile 0.1 m = 0.0128 units.
                float tolUnits = ColliderTolerance / (ShippedMap.CentrePileFootprint.x);
                Assert.LessOrEqual(maxR, 0.5f + tolUnits, $"fill {ratio}: the mound pokes outside the collider oval (r {maxR:F4} > 0.5).");
                Assert.GreaterOrEqual(widestAboveGround, 0.5f - tolUnits, $"fill {ratio}: the visible ground ring (r {widestAboveGround:F4}) is narrower than the collider oval (0.5).");
            }
        }

        // ---- collection from every side ----------------------------------------------------------

        [Test]
        public void Collect_ReachableFromEverySide_AgainstTheCollider_AndApproachPointsCollect()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath);
            float reach = TestAssets.PrivateField<float>(prefab.GetComponent<FoodPile>(), "_collectReach");
            var chicken = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefabPath);
            float standoff = TestAssets.PrivateField<float>(chicken.GetComponent<BotController>(), "_pileStandoff");
            Assert.Greater(standoff, NavMesh.GetSettingsByID(0).agentRadius + CarveTolerance,
                "Bot standoff must clear the agent radius plus the carve's outward error, or approach points land in the carved hole.");
            Assert.LessOrEqual(standoff, reach);

            foreach (var spec in ShippedPiles(Vector2.zero, Vector2.zero))
            foreach (float scale in FootprintStepScales(prefab))
            {
                var (go, _) = BuildPile(spec, scale);
                try
                {
                    var hull = go.GetComponentInChildren<MeshCollider>();
                    var fp = spec.Footprint * scale;
                    for (int i = 0; i < 72; i++)
                    {
                        var dir = Quaternion.Euler(0f, spec.Yaw + i * 5f, 0f) * Vector3.right;
                        var origin = spec.Pos + dir * 20f + Vector3.up * 0.5f;
                        Assert.IsTrue(hull.Raycast(new Ray(origin, -dir), out var hit, 40f));
                        var standing = new Vector3(hit.point.x, 0f, hit.point.z) + dir * ChickenContactGap;
                        float d = PileFootprintShape.DistanceToSurfaceXZ(standing, spec.Pos, spec.Yaw, fp);
                        Assert.LessOrEqual(d, reach, $"{spec.Name} x{scale:F2}: a chicken pressed against the pile at {i * 5} deg is {d:F2} m from its surface - cannot collect.");
                        Assert.Greater(d, 0.3f, $"{spec.Name}: collect surface sits behind the collider at {i * 5} deg.");

                        var approach = PileFootprintShape.ApproachPoint(spec.Pos + dir * 30f, spec.Pos, spec.Yaw, fp, standoff);
                        float da = PileFootprintShape.DistanceToSurfaceXZ(approach, spec.Pos, spec.Yaw, fp);
                        Assert.AreEqual(standoff, da, 0.02f, $"{spec.Name}: approach point is not one standoff off the oval.");
                        Assert.LessOrEqual(da, reach);
                    }
                    // A mover stuck inside (or dead centre) is sent out, never to NaN or the centre.
                    var outPt = PileFootprintShape.ApproachPoint(spec.Pos, spec.Pos, spec.Yaw, fp, standoff);
                    Assert.IsFalse(float.IsNaN(outPt.x) || float.IsNaN(outPt.z));
                    Assert.AreEqual(standoff, PileFootprintShape.DistanceToSurfaceXZ(outPt, spec.Pos, spec.Yaw, fp), 0.02f);
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        [Test]
        public void ClosestPointOnEllipse_MatchesBruteForce()
        {
            var rng = new System.Random(4);
            foreach (var fp in new[] { ShippedMap.CentrePileFootprint, ShippedMap.ContestedPileFootprint, ShippedMap.PersonalPileFootprint, new Vector2(3f, 5f) })
            {
                float a = fp.x * 0.5f, b = fp.y * 0.5f;
                for (int n = 0; n < 200; n++)
                {
                    float px = (float)(rng.NextDouble() * 16 - 8), pz = (float)(rng.NextDouble() * 16 - 8);
                    var c = PileFootprintShape.ClosestPointOnEllipse(px, pz, a, b);
                    Assert.AreEqual(1f, (c.x / a) * (c.x / a) + (c.y / b) * (c.y / b), 1e-3f, "result is not on the ellipse");
                    float brute = float.MaxValue;
                    for (int i = 0; i < 7200; i++)
                    {
                        float t = i * Mathf.PI * 2f / 7200f;
                        brute = Mathf.Min(brute, Vector2.Distance(new Vector2(px, pz), new Vector2(a * Mathf.Cos(t), b * Mathf.Sin(t))));
                    }
                    Assert.AreEqual(brute, Vector2.Distance(new Vector2(px, pz), c), 0.005f, $"({px:F2},{pz:F2}) on {fp}");
                }
            }
        }

        // ---- oracle: equal travel per seat, from the shipped scene -------------------------------

        /// <summary>
        /// Builds the Oracle map from MAP.UNITY's baked discs (piles + bases), after pinning them to
        /// the generator's formulas, so the numbers are the shipped geometry - not the drifted
        /// <c>BalanceOracleTests.Task8OracleMap</c> fixture and not a restatement of literals.
        /// </summary>
        [Test]
        public void Oracle_TravelAndSct_AreEqualAcrossTheFourSeats_OnTheShippedMap()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var geo = scene.GetRootGameObjects().First(g => g.name == "GeneratedMapGeometry").transform;
            var specs = ShippedPiles(Vector2.zero, Vector2.zero);
            var piles = specs.Select(s =>
            {
                var disc = geo.Find("PileZone_" + s.Name);
                Assert.IsNotNull(disc);
                return (s, pos: new Vector2(disc.position.x, disc.position.z), yaw: disc.eulerAngles.y,
                        fp: new Vector2(disc.lossyScale.x, disc.lossyScale.z));
            }).ToList();
            var bases = Enumerable.Range(0, 4).Select(i =>
            {
                var bz = geo.Find($"BaseZone_{i}");
                Assert.IsNotNull(bz, $"Map.unity has no BaseZone_{i}.");
                Assert.Less(Vector3.Distance(new Vector3(bz.position.x, 0f, bz.position.z), ShippedBases()[i]), 0.01f);
                return new Vector2(bz.position.x, bz.position.z);
            }).ToArray();

            var config = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir).Where(s => SctTargets.All.Any(t => t.ClassName == s.DisplayName)).ToList();
            Assert.IsNotEmpty(stats);

            var oracleMap = piles.Select(p => new OraclePile { Pos = p.pos, Food = p.s.Food }).ToArray();
            var report = new System.Text.StringBuilder("[D9] per seat:");
            float[] refTravel = null;
            var refSct = new Dictionary<string, OracleResult>();
            for (int seat = 0; seat < 4; seat++)
            {
                int prev = (seat + 3) % 4;
                Vector2 Pos(string n) => piles.First(p => p.s.Name == n).pos;
                float Surf(string n)
                {
                    var p = piles.First(q => q.s.Name == n);
                    return PileFootprintShape.DistanceToSurfaceXZ(new Vector3(bases[seat].x, 0f, bases[seat].y),
                        new Vector3(p.pos.x, 0f, p.pos.y), p.yaw, p.fp);
                }
                var travel = new[]
                {
                    Vector2.Distance(bases[seat], Pos($"Personal{seat}")),
                    Vector2.Distance(bases[seat], Pos($"Contested{seat}")),
                    Vector2.Distance(bases[seat], Pos($"Contested{prev}")),
                    Vector2.Distance(bases[seat], Pos("Center")),
                    Surf($"Personal{seat}"), Surf($"Contested{seat}"), Surf($"Contested{prev}"), Surf("Center"),
                };
                report.Append($"\n seat{seat}: centre-to-centre personal {travel[0]:F3} contested {travel[1]:F3}/{travel[2]:F3} centre {travel[3]:F3}; " +
                              $"to surface personal {travel[4]:F3} contested {travel[5]:F3}/{travel[6]:F3} centre {travel[7]:F3}");
                if (refTravel == null) refTravel = travel;
                for (int k = 0; k < travel.Length; k++)
                    Assert.AreEqual(refTravel[k], travel[k], 0.01f, $"seat {seat} travel term {k} differs from seat 0.");
                Assert.AreEqual(travel[1], travel[2], 0.01f, $"seat {seat}: its two contested piles are not equidistant (chirality).");
                Assert.AreEqual(travel[5], travel[6], 0.01f, $"seat {seat}: its two contested piles' surfaces are not equidistant (chirality).");

                foreach (var s in stats)
                {
                    var r = BalanceOracle.Simulate(new OracleMap { BasePos = bases[seat], Piles = oracleMap },
                        new OracleChicken { MoveSpeed = s.MoveSpeed, CargoCapacity = s.CargoCapacity, PeckAmount = s.PeckAmount,
                                            PeckCooldown = s.PeckCooldown, DepositRate = config.DepositRatePerSecond },
                        config.FoodTargetToWin);
                    report.Append($" | {s.DisplayName} {r.SctSeconds:F2}s/{r.Trips}");
                    if (!refSct.TryGetValue(s.DisplayName, out var r0)) { refSct[s.DisplayName] = r; continue; }
                    Assert.AreEqual(r0.SctSeconds, r.SctSeconds, 0.01f, $"{s.DisplayName}: seat {seat} SCT differs from seat 0.");
                    Assert.AreEqual(r0.Trips, r.Trips, $"{s.DisplayName}: seat {seat} trip count differs from seat 0.");
                }
            }
            Debug.Log(report.ToString());
        }

        // ---- NavMesh reachability ------------------------------------------------------------------

        [Test]
        public void EveryPile_IsReachableOnTheBakedNavMesh_FromEveryBase()
        {
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var tri = NavMesh.CalculateTriangulation();
            Assert.Greater(tri.indices.Length, 0, "Map.unity's NavMesh did not load.");

            var chicken = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefabPath);
            float standoff = TestAssets.PrivateField<float>(chicken.GetComponent<BotController>(), "_pileStandoff");
            var path = new NavMeshPath();
            foreach (var basePos in ShippedBases())
            {
                Assert.IsTrue(NavMesh.SamplePosition(basePos, out var from, 2f, NavMesh.AllAreas), $"base {basePos} is off the NavMesh");
                foreach (var spec in ShippedPiles(Vector2.zero, Vector2.zero))
                {
                    // The point the bot itself steers to (BotController -> FoodPile.SurfaceApproachPoint), plus the
                    // far side of the pile, which the bot reaches when a rival holds the near side.
                    foreach (var fromDir in new[] { basePos, spec.Pos * 2f - basePos })
                    {
                        var target = PileFootprintShape.ApproachPoint(fromDir, spec.Pos, spec.Yaw, spec.Footprint, standoff);
                        Assert.IsTrue(NavMesh.SamplePosition(target, out var to, 2.5f, NavMesh.AllAreas), $"{spec.Name}: approach {target} is off the NavMesh");
                        Assert.IsTrue(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path), $"{spec.Name} from base {basePos}: no path");
                        Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, $"{spec.Name} from base {basePos}: path {path.status}");
                        var end = path.corners[path.corners.Length - 1];
                        float d = PileFootprintShape.DistanceToSurfaceXZ(end, spec.Pos, spec.Yaw, spec.Footprint);
                        Assert.LessOrEqual(d, TestAssets.PrivateField<float>(AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath).GetComponent<FoodPile>(), "_collectReach"),
                            $"{spec.Name} from base {basePos}: the path ends {d:F2} m from the pile - the bot would not collect.");
                    }
                }
            }
        }
    }
}
