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
            Assert.AreEqual(13, ChickenNameplate.LocalPillOrder);
            Assert.AreEqual(14, ChickenNameplate.LocalTextOrder);
            Assert.Less(ChickenNameplate.RivalTextOrder, ChickenNameplate.LocalPillOrder);
            Assert.GreaterOrEqual(ChickenNameplate.RivalTextOrder, 10, "F3: rival marks live in the 10-12 label band");
            Assert.GreaterOrEqual(ChickenNameplate.RivalPillOrder, 10);
            Assert.Less(FeedbackTuning.RivalDotSortingOrder, ChickenNameplate.LocalPillOrder, "a rival dot or glyph never draws over the local pill");
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
                    {
                        Assert.GreaterOrEqual(sr.sortingOrder, 10, sr.name + " (rival part, label band)");
                        Assert.Less(sr.sortingOrder, ChickenNameplate.LocalPillOrder, sr.name + " (rival part under the local pill)");
                    }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
