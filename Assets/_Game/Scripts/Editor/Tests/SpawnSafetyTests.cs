using System.Linq;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the two spawn promises Maestro asked for on 2026-08-19, after a chicken was
    /// found wedged in the edge wall at match start: a player must always appear INSIDE the
    /// playfield, and never inside or hard against a solid.
    /// </summary>
    /// <remarks>
    /// These measure the SHIPPED map — `Game.unity` for the tuning plus the baked
    /// `Map.unity` for the geometry — rather than a reconstruction, because the failure was
    /// an interaction between generated geometry and hand-authored props that no synthetic
    /// fixture would have contained. Containment at runtime is enforced separately and
    /// unconditionally by <c>ChickenMovement.ClampInsideArena</c>; this pins the spawn side,
    /// which a clamp cannot rescue — a chicken that starts inside a coop is stuck there.
    /// </remarks>
    public class SpawnSafetyTests
    {
        private const string GameScene = "Assets/_Game/Scenes/Game.unity";
        private const string MapScene  = "Assets/_Game/Scenes/Map.unity";

        /// <summary>
        /// Free space demanded around a spawn point, beyond the chicken's own radius. A body
        /// that merely fits is not enough: it is the pocket a single knockback or network
        /// correction wedges you into. One extra radius means the chicken can always take a
        /// step in some direction before touching anything.
        /// </summary>
        private const float SpawnBreathingRoom = PinwheelLayout.ChickenRadius;

        private string _originalScenePath;

        /// <summary>
        /// These tests must open the real scenes — the bug they guard was an interaction
        /// between generated geometry and hand-authored props that no synthetic fixture
        /// contains, and <c>Collider.ClosestPoint</c>/<c>SphereCast</c> need a loaded
        /// collision world. That is a deliberate departure from <c>TestAssets.SceneFloat</c>,
        /// which parses scene YAML as text precisely so it does NOT disturb the developer's
        /// open scene.
        ///
        /// Since we do disturb it, we put it back. Without this, running the suite in an
        /// interactive Editor silently replaces whatever the developer had open and leaves it
        /// that way — and any later test that assumed a particular scene inherits ours.
        /// </summary>
        [SetUp]
        public void CaptureOpenScene()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
        }

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
            {
                // Nothing meaningful was open (an untitled scene); leave a clean empty one
                // rather than whichever map this test happened to load last.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return;
            }

            if (SceneManager.GetActiveScene().path != _originalScenePath)
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        private static Collider[] LoadShippedColliders()
        {
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);

            return Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                // Triggers are deposit/collect zones — you are meant to stand in them.
                .Where(c => !c.isTrigger)
                // The ground is what you stand ON; every spawn "overlaps" it by design.
                .Where(c => c.name != "GeneratedGround")
                .ToArray();
        }

        private static Vector3[] ShippedSpawnPoints()
        {
            var gen = Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                            .FirstOrDefault();
            Assert.IsNotNull(gen, $"No MapGenerator in {GameScene}.");

            float d = TestAssets.SceneFloat("_baseCornerDistance");
            var corners = new[]
            {
                new Vector3(+d, 0f, +d), new Vector3(-d, 0f, +d),
                new Vector3(-d, 0f, -d), new Vector3(+d, 0f, -d),
            };
            return corners.Select(MapGenerator.SpawnPointNominal).ToArray();
        }

        [Test]
        public void EverySpawnPoint_IsInsideTheBoundary_WithRoomForTheBody()
        {
            float half = TestAssets.SceneFloat("_planeSize") * 0.5f;
            float limit = half - PinwheelLayout.ChickenRadius;

            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            foreach (var (p, i) in ShippedSpawnPoints().Select((p, i) => (p, i)))
            {
                Assert.LessOrEqual(Mathf.Abs(p.x), limit,
                    $"spawn {i} x={p.x:0.00} is outside the wall's inner face ({half:0.00}) " +
                    $"once the chicken's radius is accounted for.");
                Assert.LessOrEqual(Mathf.Abs(p.z), limit,
                    $"spawn {i} z={p.z:0.00} is outside the wall's inner face ({half:0.00}).");
            }
        }

        [Test]
        public void NoSpawnPoint_SitsInsideOrHardAgainstASolid()
        {
            var cols = LoadShippedColliders();
            var spawns = ShippedSpawnPoints();
            Assert.Greater(cols.Length, 0, "No solid colliders found — the map did not load.");

            float required = PinwheelLayout.ChickenRadius + SpawnBreathingRoom;
            var offenders = new System.Collections.Generic.List<string>();

            for (int i = 0; i < spawns.Length; i++)
            {
                foreach (var c in cols)
                {
                    float dist = Vector3.Distance(c.ClosestPoint(spawns[i]), spawns[i]);
                    if (dist < required)
                        offenders.Add($"spawn {i} is {dist:0.000} m from '{c.name}' " +
                                      $"(needs {required:0.000} = radius {PinwheelLayout.ChickenRadius:0.00} " +
                                      $"+ breathing room {SpawnBreathingRoom:0.00})");
                }
            }

            Assert.IsEmpty(offenders,
                "A player would appear inside or jammed against a solid:\n  " +
                string.Join("\n  ", offenders) +
                "\nMove the spawn, or move the prop — a runtime clamp cannot fix this, " +
                "because a chicken that starts inside geometry has nowhere legal to be pushed to.");
        }

        [Test]
        public void EverySpawnPoint_HasAnEscapeRoute()
        {
            // Being blocked toward the corner is correct — that is the boundary. Being
            // blocked in EVERY direction is the trap. Requires a clear walkable arc.
            // Load first: ShippedSpawnPoints reads the MapGenerator out of the open scene.
            LoadShippedColliders();
            var spawns = ShippedSpawnPoints();

            const int bearings = 72;
            const float probe = 3f;

            for (int i = 0; i < spawns.Length; i++)
            {
                int open = 0;
                for (int a = 0; a < bearings; a++)
                {
                    float rad = a * Mathf.PI * 2f / bearings;
                    var dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
                    var origin = spawns[i] + Vector3.up * 0.8f;
                    if (!Physics.SphereCast(origin, PinwheelLayout.ChickenRadius, dir, out _, probe))
                        open++;
                }

                float openFraction = (float)open / bearings;
                Assert.Greater(openFraction, 0.25f,
                    $"spawn {i} has only {openFraction * 100f:0}% of bearings clear within {probe} m " +
                    "— the player starts boxed in. A corner spawn is expected to be blocked " +
                    "toward the walls, but not in most directions.");
            }
        }
        // ---- Containment arithmetic (PinwheelLayout.ClampIntoArena) ------------
        // The runtime path (ChickenMovement.ClampInsideArena) is entangled with a
        // CharacterController and cannot be unit-tested; the arithmetic it delegates to can be,
        // and that is the half that can be silently wrong. Asserted as RELATIONSHIPS, not
        // coordinates, so an arena rescale does not turn these red for the wrong reason.

        [Test]
        public void ClampIntoArena_AlwaysReturnsAPointInsideTheLimit()
        {
            float half = TestAssets.SceneFloat("_planeSize") * 0.5f;
            float limit = PinwheelLayout.ArenaClampLimit(half);

            // Includes points far outside, exactly on the wall, and in the corners — the
            // corner is where two walls meet and where the reported wedge happened.
            var probes = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(half, 1f, half),
                new Vector3(-half, 1f, -half),
                new Vector3(half * 4f, 2f, -half * 4f),
                new Vector3(float.MaxValue * 0f + 9999f, 0f, -9999f),
            };

            foreach (var p in probes)
            {
                var c = PinwheelLayout.ClampIntoArena(p, half);
                Assert.LessOrEqual(Mathf.Abs(c.x), limit + 1e-4f, $"x escaped for input {p}");
                Assert.LessOrEqual(Mathf.Abs(c.z), limit + 1e-4f, $"z escaped for input {p}");
            }
        }

        [Test]
        public void ClampIntoArena_LeavesAnAlreadyLegalPositionExactlyAlone()
        {
            float half = TestAssets.SceneFloat("_planeSize") * 0.5f;
            var inside = new Vector3(1.25f, 3.5f, -2.75f);

            Assert.AreEqual(inside, PinwheelLayout.ClampIntoArena(inside, half),
                "A legal position must be returned bit-identical, or the clamp fights ordinary " +
                "movement every tick instead of only rescuing a body that left the arena.");
        }

        [Test]
        public void ClampIntoArena_NeverTouchesY()
        {
            float half = TestAssets.SceneFloat("_planeSize") * 0.5f;

            foreach (float y in new[] { -50f, 0f, 0.83f, 120f })
            {
                var c = PinwheelLayout.ClampIntoArena(new Vector3(9999f, y, -9999f), half);
                Assert.AreEqual(y, c.y,
                    "Y is gravity and jump arcs; clamping it would fight the vertical velocity " +
                    "the movement tick owns.");
            }
        }

        [Test]
        public void ClampLimit_LeavesRoomForTheBody_AndIsTighterThanTheJumpValidator()
        {
            float half = TestAssets.SceneFloat("_planeSize") * 0.5f;

            Assert.AreEqual(half - PinwheelLayout.ChickenRadius,
                PinwheelLayout.ArenaClampLimit(half), 1e-5f);

            // JumpResolver validates landings against the un-skinned radius, so it is the more
            // permissive of the two by exactly the skin width. That ordering is deliberate: a
            // jump may land fractionally deep and get nudged out, never the reverse.
            Assert.Less(PinwheelLayout.ArenaClampLimit(half),
                half - JumpResolver.BodyClearance + 1e-5f,
                "The clamp must be at least as tight as the jump validator, or a jump could " +
                "land somewhere the clamp then refuses to correct.");
        }

        [Test]
        public void DegenerateArena_ReturnsTheInputRatherThanCollapsingToTheOrigin()
        {
            var p = new Vector3(12f, 1f, -7f);
            Assert.AreEqual(p, PinwheelLayout.ClampIntoArena(p, 0.1f),
                "A tiny/unset ArenaHalfSize must not teleport every chicken to a degenerate box " +
                "— that would be worse than not clamping at all.");
        }

    }
}
