using System.Reflection;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// Regression for the 4-seat test session (2026-10-20): Fusion does not replicate the spawn position of a
    /// NetworkObject without a NetworkTransform, so on every client the bases stood at PlayerBase.prefab's root
    /// (9.47, 9.38) and every pile at the origin. Clients banked "at their base" there and the host rejected all 185 of
    /// their deposits. Bases and piles now carry their spawn position as a stamped [Networked] value that non-authority
    /// peers apply (<see cref="SpawnPlacement"/>).
    /// </summary>
    public sealed class SpawnPlacementTests
    {
        [Test]
        public void ProxiesMoveToTheStampedPosition_TheAuthorityNeverMoves()
        {
            var stamped = new Vector3(21.8f, 0f, -21.8f);
            var prefabRoot = new Vector3(9.47f, 0f, 9.38f);
            Assert.IsTrue(SpawnPlacement.ProxyMustMove(false, true, stamped, prefabRoot), "a client copy sits at the prefab root: move it");
            Assert.IsFalse(SpawnPlacement.ProxyMustMove(true, true, stamped, prefabRoot), "the spawner's transform is the truth");
            Assert.IsFalse(SpawnPlacement.ProxyMustMove(false, false, default, prefabRoot), "no stamp yet: wait (retried every frame)");
            Assert.IsFalse(SpawnPlacement.ProxyMustMove(false, true, stamped, stamped), "already there");
            Assert.IsTrue(SpawnPlacement.ProxyMustMove(false, true, Vector3.zero, new Vector3(3f, 0f, 0f)),
                "the centre pile's stamp IS the origin: the flag, not a non-zero position, says it was stamped");
        }

        [TestCase("Assets/_Game/Prefabs/PlayerBase.prefab", typeof(PlayerBase))]
        [TestCase("Assets/_Game/Prefabs/FoodPile.prefab", typeof(FoodPile))]
        public void StaticNetworkObjects_WithoutANetworkTransform_CarryTheirSpawnPosition(string prefabPath, System.Type type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            if (prefab.GetComponent<Fusion.NetworkTransform>() != null) return;   // the position replicates by itself

            foreach (var propName in new[] { "SpawnPosition", "HasSpawnPosition" })
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(prop, $"{type.Name} has no NetworkTransform, so it needs a [Networked] {propName}: " +
                    "without it every client puts it at the prefab root.");
                Assert.IsNotNull(prop.GetCustomAttribute<Fusion.NetworkedAttribute>(), $"{type.Name}.{propName} must be [Networked]");
            }
        }

        /// <summary>NetworkObject prefabs whose world position means nothing (they are not placed in the arena).</summary>
        private static readonly string[] PositionlessPrefabs = { "GameManager" };

        [Test]
        public void EverySpawnedNetworkObjectPrefab_HasANetworkTransformOrAStampedSpawnPosition()
        {
            // CW-216 (bases, piles) and CW-217 (ability zones): Fusion does not replicate the spawn position of an object
            // without a NetworkTransform, so every other peer puts it at the prefab root. Moving objects need a
            // NetworkTransform; statically placed ones need the [Networked] SpawnPosition + HasSpawnPosition stamp.
            var offenders = new System.Collections.Generic.List<string>();
            int checkedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<Fusion.NetworkObject>() == null) continue;
                checkedCount++;
                if (System.Array.IndexOf(PositionlessPrefabs, prefab.name) >= 0) continue;
                if (prefab.GetComponent<Fusion.NetworkTransform>() != null) continue;

                bool stamped = false;
                foreach (var nb in prefab.GetComponents<Fusion.NetworkBehaviour>())
                {
                    if (nb == null) continue;
                    var prop = nb.GetType().GetProperty("HasSpawnPosition", BindingFlags.Public | BindingFlags.Instance);
                    var pos = nb.GetType().GetProperty("SpawnPosition", BindingFlags.Public | BindingFlags.Instance);
                    if (prop?.GetCustomAttribute<Fusion.NetworkedAttribute>() != null && pos?.GetCustomAttribute<Fusion.NetworkedAttribute>() != null)
                    {
                        stamped = true;
                        break;
                    }
                }
                if (!stamped) offenders.Add(path);
            }
            Assert.Greater(checkedCount, 4, "found the game's NetworkObject prefabs");
            CollectionAssert.IsEmpty(offenders, "NetworkObject prefab(s) with neither a NetworkTransform nor a stamped spawn position: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void ThePlayerBasePrefabRoot_IsNotWhereAnyBaseStands()
        {
            // Why the bug was invisible on the host and total on clients: the prefab root is a real point in the arena,
            // (9.47, 9.38) - inside the playfield, near no corner (bases stand at about (+-21.8, +-21.8)).
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/PlayerBase.prefab");
            Assert.IsNotNull(prefab);
            var root = prefab.transform.position;
            foreach (var corner in new[] { new Vector3(25.65f, 0f, 25.65f), new Vector3(-25.65f, 0f, 25.65f),
                                           new Vector3(-25.65f, 0f, -25.65f), new Vector3(25.65f, 0f, -25.65f) })
            {
                var spawn = MapGenerator.SpawnPointNominal(corner);
                Assert.Greater(new Vector2(spawn.x - root.x, spawn.z - root.z).magnitude, 2.5f,
                    "if the prefab root were inside a deposit radius this test would no longer show the bug's shape");
            }
        }
    }
}
