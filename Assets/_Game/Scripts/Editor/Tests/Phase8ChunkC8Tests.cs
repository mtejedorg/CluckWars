using System.Reflection;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C8: the local player's "You" pill and text sort above every rival nameplate part (1-3), so a rival
    /// label never draws over it where two overlap.
    /// </summary>
    public class Phase8ChunkC8Tests
    {
        [Test]
        public void LocalOrders_AreAboveEveryRivalOrder()
        {
            Assert.AreEqual(11, ChickenNameplate.LocalPillOrder);
            Assert.AreEqual(12, ChickenNameplate.LocalTextOrder);
            Assert.Less(ChickenNameplate.RivalTextOrder, ChickenNameplate.LocalPillOrder);
            Assert.LessOrEqual(ChickenNameplate.RivalTextOrder, 3);
            Assert.LessOrEqual(ChickenNameplate.RivalPillOrder, 3);
        }

        [Test]
        public void FitMarks_AppliesLocalOrRivalTextOrder()
        {
            // Inactive so no Awake runs (the nameplate requires a networked ChickenController); build the label by hand.
            var go = new GameObject("C8NameplateProbe");
            go.SetActive(false);
            try
            {
                var plate = go.AddComponent<ChickenNameplate>();
                typeof(ChickenNameplate).GetMethod("BuildLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(plate, null);
                var textRenderer = go.transform.Find("Nameplate").GetComponent<MeshRenderer>();
                var youPill = go.transform.Find("Nameplate/YouMark").GetComponent<SpriteRenderer>();
                var fit = typeof(ChickenNameplate).GetMethod("FitMarks", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(fit, "FitMarks was renamed; update this test.");

                fit.Invoke(plate, new object[] { true, Color.white });
                Assert.AreEqual(ChickenNameplate.LocalTextOrder, textRenderer.sortingOrder, "local text");
                Assert.AreEqual(ChickenNameplate.LocalPillOrder, youPill.sortingOrder, "local pill");

                fit.Invoke(plate, new object[] { false, Color.white });
                Assert.AreEqual(ChickenNameplate.RivalTextOrder, textRenderer.sortingOrder, "rival text");
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                    if (sr != youPill)
                        Assert.LessOrEqual(sr.sortingOrder, 3, sr.name + " (rival part)");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
