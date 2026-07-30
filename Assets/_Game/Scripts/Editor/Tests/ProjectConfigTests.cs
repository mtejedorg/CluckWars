using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using Fusion;

// Fusion ships its own `Assert`, same family of collision as the documented
// `Fusion.LogLevel` one (CONVENTIONS.md ▸ Footguns). Any test file that needs
// Fusion types has to disambiguate or every Assert call is CS0104.
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards project-level wiring: build settings, physics layers, prefab component
    /// composition, and the folder / shader conventions the rest of the docs assume.
    /// </summary>
    /// <remarks>
    /// Every assertion here corresponds to a bug the project has actually shipped:
    ///
    /// * <b>Scene order</b> — Bootstrap must be build index 0 or the player starts in
    ///   the Game scene with no class selection (CONVENTIONS.md).
    /// * <b>Missing NetworkTransform</b> (finding C1, 2026-07-18) — remote chickens
    ///   could never move; solo mode masked it completely.
    /// * <b>Duplicate components</b> (finding C2) — two <c>BotController</c>s meant
    ///   double-speed bots and invalidated every pacing measurement taken before the fix.
    /// * <b>Layers on the root only</b> — the 2026-07-18 layer pass set layer 9 on
    ///   prefab roots but not on the collider-bearing children, which broke all
    ///   collection and deposit until it was caught by hand.
    ///
    /// None of these produce a compile error, and several of them do not produce a
    /// runtime error either. They just make the game quietly wrong.
    /// </remarks>
    public sealed class ProjectConfigTests
    {
        // ---- Build settings -----------------------------------------------------

        [Test]
        public void BuildSettings_StartAtBootstrap_ThenGame_ThenBakedMap()
        {
            var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).ToList();

            Assert.AreEqual(3, enabled.Count,
                $"Expected Bootstrap + Game + Map in Build Settings, found {enabled.Count}: " +
                string.Join(", ", enabled.Select(s => s.path)));
            Assert.AreEqual(TestAssets.BootstrapScenePath, enabled[0].path,
                "Bootstrap must be build index 0. Otherwise the player starts straight in the Game " +
                "scene with no class selection (CONVENTIONS.md).");
            Assert.AreEqual(TestAssets.GameScenePath, enabled[1].path);
        }

        /// <summary>
        /// v0.5 bakes the arena into its own scene, additively loaded at runtime by
        /// <c>MapGenerator.EnsureBakedMapLoaded</c>. If it drops out of Build Settings the
        /// load silently no-ops and the match runs on an EMPTY arena — chickens fall
        /// through the world — so this is worth pinning separately from the ordering test.
        /// </summary>
        [Test]
        public void BuildSettings_ContainsBakedMapScene()
        {
            const string mapPath = "Assets/_Game/Scenes/Map.unity";

            Assert.IsTrue(System.IO.File.Exists(mapPath),
                $"{mapPath} is missing. Regenerate it via Cluck Wars/Map/Bake Map Scene.");

            var entry = EditorBuildSettings.scenes.FirstOrDefault(s => s.path == mapPath);
            Assert.IsNotNull(entry,
                $"{mapPath} is not in Build Settings — the runtime additive load will fail and the " +
                "arena will have no ground or walls.");
            Assert.IsTrue(entry.enabled, $"{mapPath} is in Build Settings but DISABLED, which fails " +
                "exactly like being absent.");
        }

        [Test]
        public void BuildSettings_EveryListedScene_ExistsOnDisk()
        {
            foreach (var s in EditorBuildSettings.scenes)
            {
                Assert.IsTrue(System.IO.File.Exists(s.path),
                    $"Build Settings lists '{s.path}' but no such scene exists — the build fails or " +
                    "ships a broken SceneLoader target.");
            }
        }

        // ---- Physics layers -----------------------------------------------------

        [Test]
        public void PhysicsLayers_MatchTheMasksBakedIntoCode()
        {
            // AbilityBaseSO.SearchMask defaults to 1 << 8 and the ability assets store
            // raw bitmasks. Renaming or renumbering these layers turns every ability
            // physics scan into a silent no-op.
            Assert.AreEqual("Chickens", LayerMask.LayerToName(8),
                "Layer 8 must be 'Chickens' — ability SearchMasks are authored as raw bit 8.");
            Assert.AreEqual("Interactables", LayerMask.LayerToName(9),
                "Layer 9 must be 'Interactables' — piles, pickups and bases are authored onto it.");
        }

        // ---- Chicken prefab composition ----------------------------------------

        private static GameObject ChickenPrefab() => TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);

        [Test]
        public void ChickenPrefab_HasEveryComponentTheGameplayLoopResolves()
        {
            var go = ChickenPrefab();

            void Require<T>(string why) where T : Component
            {
                Assert.IsNotNull(go.GetComponentInChildren<T>(true),
                    $"Chicken.prefab is missing {typeof(T).Name}. {why}");
            }

            Require<NetworkObject>("Nothing about the chicken replicates without it.");
            Require<NetworkTransform>(
                "This was finding C1: remote chickens never moved on any peer, and solo mode hid it " +
                "completely because there are no remote chickens in solo.");
            Require<CharacterController>("ChickenMovement drives this; without it the chicken cannot move.");
            Require<ChickenController>("Owns Class, stats resolution and the control-state flags.");
            Require<ChickenCombat>("Owns the execute removal/respawn state machine.");
            Require<ChickenCargo>("Owns collection and deposit — no scoring without it.");
            Require<AbilityController>("Owns activation, duration and cooldown for all three slots.");
            Require<ChickenMatchStats>("Kills / FoodDeposited on the match-end scoreboard.");
            Require<BotController>("Solo mode spawns 3 bots off this prefab.");
            Require<ChickenVFX>("Ability and hit VFX.");
        }

        [Test]
        public void ChickenPrefab_HasNoDuplicateBehaviourComponents()
        {
            // Finding C2: a duplicated BotController ticked the FSM twice per frame,
            // which made bots move at double speed and invalidated every pacing
            // measurement taken before 2026-07-18.
            var go = ChickenPrefab();

            void Single<T>() where T : Component
            {
                int count = go.GetComponentsInChildren<T>(true).Length;
                Assert.AreEqual(1, count,
                    $"Chicken.prefab has {count}× {typeof(T).Name}. Duplicates tick their logic once " +
                    "per copy — this is exactly the double-speed-bot defect from finding C2.");
            }

            Single<ChickenController>();
            Single<ChickenCombat>();
            Single<ChickenCargo>();
            Single<AbilityController>();
            Single<BotController>();
            Single<ChickenVFX>();
            Single<ChickenMatchStats>();
        }

        [Test]
        public void ChickenPrefab_IsOnTheChickensLayer()
        {
            // Ability SearchMasks scan layer 8; a chicken off that layer is
            // untargetable by every damage, push and steal ability in the game.
            var go = ChickenPrefab();

            Assert.AreEqual(8, go.layer,
                $"Chicken.prefab root is on layer {go.layer} ('{LayerMask.LayerToName(go.layer)}'), " +
                "not 8 (Chickens). Every ability's OverlapSphere would miss it.");
        }

        [Test]
        public void DoppelgangerPrefab_IsAChickenVariant_WithCargoStripped()
        {
            // A decoy that can carry and deposit food would be a free second farmer.
            var decoy = TestAssets.Load<GameObject>(TestAssets.DoppelgangerPrefabPath);

            Assert.IsNull(decoy.GetComponentInChildren<ChickenCargo>(true),
                "Doppelganger.prefab still has ChickenCargo. The decoy could then collect and deposit " +
                "food, turning a 4-second ability into a second harvester.");
            Assert.IsNotNull(decoy.GetComponentInChildren<NetworkObject>(true),
                "The decoy has to replicate — rivals on other peers must be able to see and hit it.");

            var source = PrefabUtility.GetCorrespondingObjectFromSource(decoy);
            Assert.IsNotNull(source,
                "Doppelganger.prefab is no longer a variant of Chicken.prefab. CONVENTIONS.md relies on " +
                "the inheritance so base-prefab changes propagate to the decoy.");
            Assert.AreEqual(TestAssets.ChickenPrefabPath, AssetDatabase.GetAssetPath(source));
        }

        // ---- Interactable prefabs: layers on the colliders, not just the root ----

        private static IEnumerable<Collider> CollidersOf(string prefabPath) =>
            TestAssets.Load<GameObject>(prefabPath).GetComponentsInChildren<Collider>(true);

        [TestCase(TestAssets.FoodPilePrefabPath)]
        [TestCase(TestAssets.FoodPickupPrefabPath)]
        [TestCase(TestAssets.PlayerBasePrefabPath)]
        public void InteractablePrefabs_PutEveryColliderOnTheInteractablesLayer(string prefabPath)
        {
            // The 2026-07-18 layer pass set layer 9 on the prefab ROOTS only. Unity
            // resolves a collider's layer from its own GameObject, so the tightened
            // masks stopped matching and ALL collection and deposit silently broke.
            var colliders = CollidersOf(prefabPath).ToList();
            Assert.IsNotEmpty(colliders, $"{prefabPath} has no colliders at all — it cannot be interacted with.");

            foreach (var c in colliders)
            {
                Assert.AreEqual(9, c.gameObject.layer,
                    $"{prefabPath}: collider on '{c.gameObject.name}' is on layer {c.gameObject.layer} " +
                    $"('{LayerMask.LayerToName(c.gameObject.layer)}'), not 9 (Interactables). Layer must be " +
                    "set on the collider's own GameObject, not only on the prefab root.");
            }
        }

        // ---- Rendering ----------------------------------------------------------

        [Test]
        public void EveryGameMaterial_UsesAUrpShader()
        {
            // CONVENTIONS.md: URP materials only. A Built-in RP shader renders solid
            // magenta under URP — obvious in the editor, but easy to miss on a
            // material that only appears on one prefab in one game mode.
            var offenders = new List<string>();

            // Only standalone .mat files we author. Unity auto-generates a
            // 'GUI/Text Shader' Font Material INSIDE every imported .ttf; those are
            // built-in sub-assets, not project materials, and are not ours to change.
            var authored = TestAssets.LoadAllIn<Material>("Assets/_Game")
                .Where(m => AssetDatabase.GetAssetPath(m).EndsWith(".mat"))
                .ToList();

            Assert.IsNotEmpty(authored, "No .mat files found under Assets/_Game — the test is stale.");

            foreach (var mat in authored)
            {
                if (mat.shader == null)
                {
                    offenders.Add($"{mat.name} (no shader)");
                    continue;
                }

                string shader = mat.shader.name;
                bool urp = shader.StartsWith("Universal Render Pipeline/")
                        || shader.StartsWith("Shader Graphs/")
                        || shader.StartsWith("Sprites/")
                        || shader.StartsWith("UI/");
                if (!urp) offenders.Add($"{mat.name} → '{shader}'");
            }

            Assert.IsEmpty(offenders,
                "These materials are not on a URP-compatible shader and will render magenta " +
                "(CONVENTIONS.md: URP materials only).");
        }

        // ---- Folder + naming conventions ---------------------------------------

        [Test]
        public void EveryProjectScriptableObjectAsset_LivesInTheDataFolder()
        {
            // CONVENTIONS.md pins SO assets to Assets/_Game/Data/. Zenject binds them
            // FromInstance via inspector slots, so a stray asset elsewhere is one
            // nobody is binding — dead data that still looks authored.
            var strays = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/_Game" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(p => p.EndsWith(".asset"))
                .Where(p => !p.StartsWith(TestAssets.DataRoot))
                .Where(p =>
                {
                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(p);
                    return so != null && so.GetType().Namespace != null
                           && so.GetType().Namespace.StartsWith("CluckWars");
                })
                .ToList();

            Assert.IsEmpty(strays,
                $"CluckWars ScriptableObject assets found outside {TestAssets.DataRoot}.");
        }

        [Test]
        public void EveryProjectScriptableObjectType_UsesTheSoSuffix()
        {
            // CONVENTIONS.md naming rule. It is what makes "is this a data asset?"
            // answerable from a type name alone in every registry and installer.
            var offenders = AssetDatabase.FindAssets("t:ScriptableObject", new[] { TestAssets.DataRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>)
                .Where(so => so != null)
                .Select(so => so.GetType())
                .Distinct()
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("CluckWars"))
                .Where(t => !t.Name.EndsWith("SO"))
                .Select(t => t.Name)
                .ToList();

            Assert.IsEmpty(offenders,
                "ScriptableObject types must use the 'SO' suffix (CONVENTIONS.md).");
        }
    }
}
