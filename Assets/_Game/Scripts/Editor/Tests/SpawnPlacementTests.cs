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
