using System.IO;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Localization;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Death Mark presentation's wiring: what reads the asset, what is local, what was deleted, the hex size cap, the
    /// marker's placement rule and the static clear. The model's own rules are in <see cref="MarkPresentationModelTests"/>.
    /// Tests named <c>_SourceGuard</c> read source text: they pin a line of wiring, not behaviour.
    /// </summary>
    public class MarkFeedbackTests
    {
        private static string Read(string path) => File.ReadAllText(path);

        [Test]
        public void AmbushReach_IsTheAssetsOwnShape()
        {
            var ambush = TestAssets.Load<AmbushAbilitySO>(TestAssets.AbilitiesDir + "/Ambush.asset");
            Vector3 me = Vector3.zero, fwd = Vector3.forward;
            float r = ambush.AimRadius;
            Assert.AreEqual(ambush.StunRadius, r, "Ambush's shape is its StunRadius");
            Assert.IsTrue(AbilityAim.InShape(ambush.AimShape, me, fwd, new Vector3(r - 0.05f, 0f, 0f), r, ambush.AimForwardOffset, ambush.AimConeAngle));
            Assert.IsFalse(AbilityAim.InShape(ambush.AimShape, me, fwd, new Vector3(r + 0.05f, 0f, 0f), r, ambush.AimForwardOffset, ambush.AimConeAngle));
        }

        [Test]
        public void AmbushLit_AsksTheEquippedAmbushWouldAffect_NoHardcodedReach_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/MarkFeedback.cs");
            StringAssert.Contains("is AmbushAbilitySO ambush", src);
            StringAssert.Contains("ambush.WouldAffect(local, target, local.transform.forward)", src);
            StringAssert.DoesNotContain("3.6", src);
        }

        [Test]
        public void InnerCircle_IsTheFloorRingsOwnRadius_SourceGuard()
        {
            StringAssert.Contains("FloorRivalArrows.RingRadiusFor(cam)", Read("Assets/_Game/Scripts/Visuals/MarkFeedback.cs"));
            StringAssert.Contains("_radius = RingRadiusFor(cam);", Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs"));
        }

        [Test]
        public void ThePresentation_IsLocal_NoRpcNoNetworkedField_SourceGuard()
        {
            foreach (var path in new[]
            {
                "Assets/_Game/Scripts/Visuals/MarkFeedback.cs", "Assets/_Game/Scripts/Visuals/MarkPresentationModel.cs",
                "Assets/_Game/Scripts/UI/MarkHudController.cs", "Assets/_Game/Scripts/UI/MarkerPlacement.cs",
            })
            {
                string src = Read(path);
                StringAssert.DoesNotContain("[Rpc", src, path);
                StringAssert.DoesNotContain("[Networked", src, path);
            }
        }

        [Test]
        public void TheModel_HoldsNoUnityObject_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/MarkPresentationModel.cs");
            foreach (var t in new[] { "MonoBehaviour", "ChickenController", "GameObject", "Transform", "Camera" })
                StringAssert.DoesNotContain(t, src, t);
        }

        [Test]
        public void TheMarkedRival_LetsTheMarkerBeItsArrow_AndTheHunterArrowIsForced_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            StringAssert.Contains("else if (chevron && marked)", src);
            StringAssert.Contains("forcedHunter = MarkFeedback.ForcesArrowFor(_controller);", src);
            StringAssert.DoesNotContain("MarkColor", src);
            StringAssert.DoesNotContain("MarkChevronScale", src);
        }

        [Test]
        public void TheOldMarkVisuals_AreGone()
        {
            Assert.IsNull(typeof(FeedbackTuning).GetField("MarkColor"));
            Assert.IsNull(typeof(FeedbackTuning).GetField("MarkChevronScale"));
            Assert.IsNull(typeof(FeedbackTuning).GetField("MarkBannerLandscape"));
            Assert.IsNull(typeof(FeedbackTuning).GetField("MarkFootRingRadius"));
            StringAssert.DoesNotContain("MarkBanner", Read("Assets/_Game/Scripts/UI/MatchHud.cs"));
            string mf = Read("Assets/_Game/Scripts/Visuals/MarkFeedback.cs");
            foreach (var t in new[] { "LineRenderer", "SpriteRenderer", "MarkPhase" }) StringAssert.DoesNotContain(t, mf, t);
        }

        [Test]
        public void MarkCopy_NewKeysExist_OldKeysAreGone()
        {
            Assert.AreEqual("KILL", UiText.Get(UiKeys.MarkHexKill));
            Assert.AreEqual("MARKED!", UiText.Get(UiKeys.MarkToast));
            Assert.AreEqual("9s", UiText.Format(UiKeys.MarkClock, ("secs", 9)));
            Assert.AreEqual("24 m", UiText.Format(UiKeys.MarkDistance, ("m", 24)));
            foreach (var old in new[] { "mark.hunt", "mark.stun", "mark.closein", "mark.walled", "mark.kill", "mark.victim" })
                Assert.IsFalse(UiText.HasKey(old), old);
        }

        [Test]
        public void KillHex_IsCappedAt115_PopsAboveThenSettles()
        {
            Assert.AreEqual(1.15f, FeedbackTuning.MarkHexKillScale);
            Assert.AreEqual(FeedbackTuning.MarkHexKillPopFrom, MarkHexRules.KillScale(0f, false), 1e-4f);
            for (float t = FeedbackTuning.MarkHexKillPopSeconds; t < 3f; t += 0.01f)
                Assert.LessOrEqual(MarkHexRules.KillScale(t, false), 1.15f + 1e-4f, $"t={t}");
            Assert.AreEqual(1.15f, MarkHexRules.KillScale(0f, true), "reduced motion: static");
        }

        [Test]
        public void KillHex_ScaleIsMultipliedIntoTheInlineScale_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            StringAssert.Contains("(_markKill[slot] ? MarkHexRules.KillScale(", src);
            StringAssert.Contains("a is MarkKillAbilitySO", src);
            StringAssert.Contains("a is AmbushAbilitySO", src);
        }

        [Test]
        public void RangeGuides_DuringAMark_OnlyAmbush()
        {
            var ambush = ScriptableObject.CreateInstance<AmbushAbilitySO>();
            var peck = ScriptableObject.CreateInstance<PeckAbilitySO>();
            try
            {
                Assert.IsTrue(AbilitySlotOverlay.ShowsGuideDuringMark(ambush, true));
                Assert.IsFalse(AbilitySlotOverlay.ShowsGuideDuringMark(peck, true));
                Assert.IsTrue(AbilitySlotOverlay.ShowsGuideDuringMark(peck, false), "every guide comes back when the mark ends");
            }
            finally
            {
                Object.DestroyImmediate(ambush);
                Object.DestroyImmediate(peck);
            }
        }

        [Test]
        public void DrawTickPitch_RisesAStepPerRow_Capped()
        {
            Assert.AreEqual(1.00f, MarkFeedback.DrawTickPitch(0), 1e-4f);
            Assert.AreEqual(1.05f, MarkFeedback.DrawTickPitch(1), 1e-4f);
            Assert.AreEqual(1.35f, MarkFeedback.DrawTickPitch(20), 1e-4f);
        }

        [Test]
        public void MarkerOverTheYouPlate_RisesThenSlidesAway()
        {
            var plate = new Rect(900f, 500f, 120f, 40f);
            // Clear of the plate: untouched.
            var far = new Vector2(400f, 300f);
            Assert.AreEqual(far, MarkerPlacement.AvoidPlate(far, plate));
            // A shallow overlap: raised just enough.
            var near = new Vector2(960f, 450f);
            var raised = MarkerPlacement.AvoidPlate(near, plate);
            Assert.AreEqual(near.x, raised.x);
            Assert.Less(raised.y, near.y);
            Assert.IsFalse(MarkerPlacement.Box(raised).Overlaps(Inflate(plate)));
            // A deep overlap: the rise is capped, then it slides sideways off the plate.
            var deep = new Vector2(940f, 560f);
            var moved = MarkerPlacement.AvoidPlate(deep, plate);
            Assert.GreaterOrEqual(moved.y, deep.y - FeedbackTuning.MarkMarkerYouPlateMaxRisePx - 1e-3f);
            Assert.IsFalse(MarkerPlacement.Box(moved).Overlaps(Inflate(plate)));
        }

        [Test]
        public void MarkerPinnedUnderTheBand_SlidesOffTheYouPlate_WithoutRising()
        {
            var plate = new Rect(900f, 500f, 120f, 40f);
            var pinned = new Vector2(950f, 470f);
            var moved = MarkerPlacement.AvoidPlate(pinned, plate, 0f);
            Assert.AreEqual(pinned.y, moved.y, "no rise: it is already at the band's edge");
            Assert.IsFalse(MarkerPlacement.Box(moved).Overlaps(Inflate(plate)));
        }

        private static Rect Inflate(Rect r)
        {
            float m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            return Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);
        }

        [Test]
        public void SceneUnload_ClearsTheScreenMood_AndThePresentation()
        {
            MarkScreenEffect.Set(1f, 1f, 1f);
            MarkFeedback.ClearPresentation();
            Assert.AreEqual(Vector3.zero, MarkScreenEffect.Current);
            Assert.IsTrue(MarkFeedback.Current.IsClean);
            string src = Read("Assets/_Game/Scripts/Visuals/MarkFeedback.cs");
            StringAssert.Contains("SceneManager.sceneUnloaded += OnSceneUnloaded;", src);
            StringAssert.Contains("private void OnDisable() => HardClear();", src);
        }

        [Test]
        public void CwMarkShots_Parses() =>
            Assert.IsTrue(AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_markshots" }).MarkShots);
    }
}
