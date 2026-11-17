using System.Linq;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>The hunter's ground brackets (K4): where they sort, what colour they are, who sees them, what stands still under
    /// Reduced Motion, and that they are one renderer on the shared material. Numbers come from <see cref="FeedbackTuning"/> and
    /// the floor-arrow constants.</summary>
    public sealed class HuntBracketsTests
    {
        private GameObject _root;
        private HuntBrackets _brackets;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HuntBracketsTestRoot");
            _brackets = HuntBrackets.Create(_root.transform, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            Assert.IsFalse(HuntBrackets.ShownNow, "a destroyed instance leaves nothing shown");
        }

        private static MarkOutputs Outputs(MarkRole role, bool armed, MarkMarkerMode mode, float alpha = 1f, int corner = 2) => new MarkOutputs
        {
            Role = role, Armed = armed, MarkerMode = mode, MarkerAlpha = alpha, TargetCorner = corner,
            MarkerPop = 1f, DrawScale = 1f, ToastScale = 1f, LiveScale = 1f, SkullCorner = -1, HunterCorner = -1,
        };

        // ---- Layering and geometry ------------------------------------------------------------------------------------------

        [Test]
        public void SortsAboveFloorArrowsAndRivalRing()
        {
            Assert.Greater(FeedbackTuning.HuntBracketSortingOrder, FloorRivalArrows.ArrowSortingOrder);
            Assert.Greater(FeedbackTuning.HuntBracketSortingOrder, FloorRivalArrows.RingSortingOrder);
            Assert.Greater(FeedbackTuning.HuntBracketSortingOrder, RivalIndicator.RingSortingOrder);
            Assert.Less(FeedbackTuning.HuntBracketSortingOrder, ChickenVFX.BodyFxSortingOrder, "body FX stay on top of the brackets");
            Assert.Greater(TelegraphShapes.GroundY + FeedbackTuning.HuntBracketHeightOffset, FloorRivalArrows.ArrowY,
                "also higher than the arrows, so equal sorting would still not z-fight");
            Assert.AreEqual(FeedbackTuning.HuntBracketSortingOrder, _brackets.BracketRenderer.sortingOrder);
        }

        [Test]
        public void Strokes_ClearTheRivalRing_AndAreThickerThanIt()
        {
            float rival = FeedbackTuning.RivalRingRadius;   // the smallest authored ring; bigger classes scale both together
            Assert.Greater(HuntBrackets.InnerEdge(HuntBrackets.RadiusFor(rival)), rival + FeedbackTuning.RivalRingWidth,
                "the whole stroke, outline included, stands outside the rival ring with a gap");
            Assert.GreaterOrEqual(FeedbackTuning.HuntBracketWidth, 1.5f * FeedbackTuning.RivalRingWidth, "thick enough for a phone");
            Assert.Greater(FeedbackTuning.HuntBracketOutlineWidth, 0f);
            Assert.Less(FeedbackTuning.HuntBracketArcDegrees, 90f, "four arcs never merge into a ring");
        }

        [Test]
        public void Mesh_FollowsTheTargetsRingAndStaysOutsideIt()
        {
            var o = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Hunt);
            float ring = FeedbackTuning.RivalRingRadius * 1.3f;   // a scaled class
            _brackets.Apply(in o, true, new Vector3(3f, 0f, -2f), ring, false, 0f);
            var mesh = _brackets.GetComponent<MeshFilter>().sharedMesh;
            float inner = HuntBrackets.InnerEdge(HuntBrackets.RadiusFor(ring));
            Assert.IsTrue(mesh.vertices.All(v => new Vector2(v.x, v.z).magnitude >= inner - 0.001f), "no vertex inside the stroke's inner edge");
            Assert.AreEqual(new Vector3(3f, TelegraphShapes.GroundY + FeedbackTuning.HuntBracketHeightOffset, -2f), _brackets.transform.position);
        }

        // ---- Colour and visibility ------------------------------------------------------------------------------------------

        [Test]
        public void ColourByState()
        {
            var arming = Outputs(MarkRole.Assassin, false, MarkMarkerMode.Hunt, corner: 1);
            var blocked = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Walled);
            var pounce = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Kill);

            Assert.AreEqual(HuntBracketState.Arming, HuntBrackets.StateFor(in arming, true));
            Assert.AreEqual(HuntBracketState.Blocked, HuntBrackets.StateFor(in blocked, true));
            Assert.AreEqual(HuntBracketState.Pounce, HuntBrackets.StateFor(in pounce, true));

            Assert.AreEqual(PlayerPalette.ForCorner(1), HuntBrackets.ColourFor(in arming), "seat colour while arming");
            Assert.AreEqual(FeedbackTuning.MarkHuntBlockedColor, HuntBrackets.ColourFor(in blocked), "red when live and blocked");
            Assert.AreEqual(FeedbackTuning.MarkKillColor, HuntBrackets.ColourFor(in pounce), "yellow when the pounce is legal");

            _brackets.Apply(in pounce, true, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
            Assert.AreEqual(HuntBracketState.Pounce, HuntBrackets.StateNow);
            Assert.AreEqual(FeedbackTuning.MarkKillColor, HuntBrackets.ColourNow);
            var fill = _brackets.GetComponent<MeshFilter>().sharedMesh.colors.First(c => c.r > 0.9f && c.g > 0.8f);
            Assert.AreEqual(FeedbackTuning.MarkKillColor.g, fill.g, 0.001f, "the vertices carry the stroke colour");
        }

        [Test]
        public void HiddenForVictimAndThirdParty()
        {
            foreach (var role in new[] { MarkRole.None, MarkRole.Victim, MarkRole.ThirdParty })
            {
                var o = Outputs(role, true, MarkMarkerMode.Kill);
                _brackets.Apply(in o, true, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
                Assert.IsFalse(HuntBrackets.ShownNow, role.ToString());
                Assert.IsFalse(_brackets.BracketRenderer.enabled, role.ToString());
            }
            var gone = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Kill, alpha: 0f);
            _brackets.Apply(in gone, true, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
            Assert.IsFalse(HuntBrackets.ShownNow, "a faded-out marker");
            var live = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Kill);
            _brackets.Apply(in live, false, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
            Assert.IsFalse(HuntBrackets.ShownNow, "no target chicken");
            _brackets.Apply(in live, true, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
            Assert.IsTrue(HuntBrackets.ShownNow);
            _brackets.Apply(in gone, true, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
            Assert.IsFalse(HuntBrackets.ShownNow, "hides again");
        }

        [Test]
        public void AlphaFollowsTheMarkerAlpha()
        {
            var o = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Hunt, alpha: 0.4f);
            _brackets.Apply(in o, true, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
            var colours = _brackets.GetComponent<MeshFilter>().sharedMesh.colors;
            Assert.AreEqual(0.4f, colours.Max(c => c.a), 0.001f, "the strongest vertex is the stroke at marker alpha");
            Assert.IsTrue(colours.All(c => c.a <= 0.4f + 0.001f));
        }

        // ---- Motion ---------------------------------------------------------------------------------------------------------

        [Test]
        public void StaticUnderReducedMotion()
        {
            var o = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Kill);
            o.MarkerPop = FeedbackTuning.MarkMarkerPopFrom;   // mid-snap
            float quarterPeriod = 0.25f / FeedbackTuning.MarkMarkerKillPulseHz;

            Assert.AreEqual(1f, HuntBrackets.ScaleFor(in o, true, 0f), "no snap");
            Assert.AreEqual(1f, HuntBrackets.ScaleFor(in o, true, quarterPeriod), "no breathe");
            Assert.Greater(HuntBrackets.ScaleFor(in o, false, 0f), 1f, "the snap closes in from larger");
            Assert.AreNotEqual(HuntBrackets.ScaleFor(in o, false, 0f), HuntBrackets.ScaleFor(in o, false, quarterPeriod), "it breathes");

            _brackets.Apply(in o, true, Vector3.zero, FeedbackTuning.RivalRingRadius, true, 0f);
            Vector3 a = _brackets.transform.localScale;
            _brackets.Apply(in o, true, Vector3.zero, FeedbackTuning.RivalRingRadius, true, quarterPeriod);
            Assert.AreEqual(a, _brackets.transform.localScale);
            Assert.AreEqual(Vector3.one, a);

            var settled = Outputs(MarkRole.Assassin, false, MarkMarkerMode.Hunt);
            Assert.AreEqual(1f, HuntBrackets.ScaleFor(in settled, false, 3.3f), "no snap, no breathe outside the pounce");
        }

        // ---- Cost and clean-up ----------------------------------------------------------------------------------------------

        [Test]
        public void OneRendererOneSharedMaterial()
        {
            var renderers = _root.GetComponentsInChildren<Renderer>(true);
            Assert.AreEqual(1, renderers.Length);
            Assert.AreSame(_brackets.BracketRenderer, renderers[0]);
            Assert.AreEqual(1, _brackets.BracketRenderer.sharedMaterials.Length);
            Assert.AreSame(TelegraphShapes.SharedUnlitSpriteMaterial(null), _brackets.BracketRenderer.sharedMaterial);
            Assert.AreEqual(0, _root.GetComponentsInChildren<ParticleSystem>(true).Length, "no particles");
            Assert.AreEqual(1, _root.GetComponentsInChildren<MeshFilter>(true).Length, "one mesh");
        }

        [Test]
        public void Destroy_ReleasesTheMeshAndClearsTheStatics()
        {
            var o = Outputs(MarkRole.Assassin, true, MarkMarkerMode.Kill);
            _brackets.Apply(in o, true, Vector3.zero, FeedbackTuning.RivalRingRadius, false, 0f);
            Assert.IsTrue(HuntBrackets.ShownNow);
            var mesh = _brackets.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(_root);
            Assert.IsTrue(mesh == null, "the mesh is destroyed with its owner");
            Assert.IsFalse(HuntBrackets.ShownNow);
            Assert.AreEqual(HuntBracketState.Hidden, HuntBrackets.StateNow);
        }
    }
}
