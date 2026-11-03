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
            Assert.AreEqual(FeedbackTuning.MarkDrawTickPitch, MarkFeedback.DrawTickPitch(0), 1e-4f);
            Assert.AreEqual(FeedbackTuning.MarkDrawTickPitch + FeedbackTuning.MarkDrawTickPitchStep, MarkFeedback.DrawTickPitch(1), 1e-4f);
            Assert.AreEqual(FeedbackTuning.MarkDrawTickPitchMax, MarkFeedback.DrawTickPitch(1000), 1e-4f);
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

        // ---- Placement round an on-screen target (pure) ----------------------------------------------------------------

        private static MarkerPlacement.MarkerScene Scene(Rect body, Rect? plate = null, Rect? you = null, float clearTop = 290f,
                                                         params Rect[] avoid)
        {
            return new MarkerPlacement.MarkerScene
            {
                Panel = new Vector2(1920f, 1080f), Body = body,
                HasPlate = plate.HasValue, TargetPlate = plate ?? default,
                HasYou = you.HasValue, You = you ?? default,
                Others = new System.Collections.Generic.List<Rect>(), ClearTop = clearTop,
                Avoid = new System.Collections.Generic.List<Rect>(avoid),
            };
        }

        private static void AssertClear(MarkerPlacement.MarkerScene s, Vector2 c, MarkerPlacement.MarkerSlot slot)
        {
            var box = MarkerPlacement.BoxFor(c, slot);
            Assert.IsFalse(box.Overlaps(s.Body), "never on the target's body");
            if (s.HasPlate) Assert.IsFalse(box.Overlaps(s.TargetPlate), "never on the target's plate");
            if (s.HasYou) Assert.IsFalse(box.Overlaps(Inflate(s.You)), "never on the You plate");
            Assert.GreaterOrEqual(box.yMin, s.ClearTop, "never under the top band");
        }

        [Test]
        public void OnScreen_AboveIsClear_ItSitsAbovePointingDown()
        {
            var s = Scene(new Rect(900f, 600f, 80f, 100f), plate: new Rect(860f, 540f, 160f, 40f));
            var above = new Vector2(940f, 540f - 70f);
            Assert.IsTrue(MarkerPlacement.TryPlaceAround(s, above, out var c, out var slot));
            Assert.AreEqual(MarkerPlacement.MarkerSlot.Above, slot);
            Assert.AreEqual(Vector2.down, MarkerPlacement.PointerFor(slot));
            AssertClear(s, c, slot);
        }

        [Test]
        public void AboveBlockedByTheTopBand_ItGoesBelowTheFeet_PointingUp()
        {
            // The 1920x1080 kill / walled captures: a target just under the band, its plate in the way above.
            var s = Scene(new Rect(900f, 330f, 80f, 100f), plate: new Rect(820f, 290f, 240f, 40f));
            var above = new Vector2(940f, 290f - 70f);                     // would be under the band
            Assert.IsTrue(MarkerPlacement.TryPlaceAround(s, above, out var c, out var slot));
            Assert.AreEqual(MarkerPlacement.MarkerSlot.Below, slot);
            Assert.AreEqual(Vector2.up, MarkerPlacement.PointerFor(slot));
            Assert.Greater(c.y, s.Body.yMax, "under the target");
            AssertClear(s, c, slot);
        }

        [Test]
        public void AboveBlockedByTheYouPlate_ItGoesBelow()
        {
            var s = Scene(new Rect(900f, 600f, 80f, 100f), plate: new Rect(860f, 540f, 160f, 40f), you: new Rect(880f, 450f, 120f, 40f));
            Assert.IsTrue(MarkerPlacement.TryPlaceAround(s, new Vector2(940f, 470f), out var c, out var slot));
            Assert.AreEqual(MarkerPlacement.MarkerSlot.Below, slot);
            AssertClear(s, c, slot);
        }

        [Test]
        public void AboveAndBelowBlocked_ItGoesToTheFreeSide()
        {
            // Under the band above, a hex cluster right under the feet.
            var hexes = new Rect(700f, 450f, 500f, 400f);
            var s = Scene(new Rect(900f, 330f, 80f, 100f), plate: new Rect(820f, 290f, 240f, 40f), clearTop: 290f, avoid: hexes);
            Assert.IsTrue(MarkerPlacement.TryPlaceAround(s, new Vector2(940f, 220f), out var c, out var slot));
            Assert.IsTrue(slot == MarkerPlacement.MarkerSlot.Left || slot == MarkerPlacement.MarkerSlot.Right);
            AssertClear(s, c, slot);
            Assert.IsFalse(MarkerPlacement.BoxFor(c, slot).Overlaps(hexes));
            var p = MarkerPlacement.PointerFor(slot);
            Assert.AreEqual(slot == MarkerPlacement.MarkerSlot.Left ? 1f : -1f, p.x, "pointing at the target");
        }

        [Test]
        public void EverySlotBlocked_ReportsIt_ForTheEdgePin()
        {
            var wall = new Rect(0f, 0f, 1920f, 1080f);
            var s = Scene(new Rect(900f, 500f, 80f, 100f), avoid: wall);
            Assert.IsFalse(MarkerPlacement.TryPlaceAround(s, new Vector2(940f, 400f), out _, out _));
        }

        [Test]
        public void OnScreenTargets_NeverGetTheEdgePin_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/UI/MarkerPlacement.cs");
            StringAssert.Contains("bool onScreen = vp3.z > 0f && feet.x >= 0f && feet.x <= 1f && feet.y >= 0f && feet.y <= 1f;", src);
            StringAssert.Contains("if (TryPlaceAround(scene, aboveCentre, out at, out var slot))", src);
            StringAssert.Contains("target.Nameplate", src, "cached accessors, no per-frame GetComponent");
            StringAssert.DoesNotContain("GetComponent<", src);
        }

        [Test]
        public void TheHunterBadge_SlidesAlongItsArrowOffTheYouPlate()
        {
            var plate = new Rect(900f, 500f, 120f, 40f);
            var at = new Vector2(950f, 520f);
            var moved = MarkerPlacement.SlideOff(at, 22f, plate, Vector2.up * -1f);   // arrow tip points up the screen
            Assert.Less(moved.y, at.y);
            Assert.AreEqual(at.x, moved.x, 1e-3f);
            Assert.IsFalse(Rect.MinMaxRect(moved.x - 22f, moved.y - 22f, moved.x + 22f, moved.y + 22f).Overlaps(Inflate(plate)));
            Assert.AreEqual(new Vector2(100f, 100f), MarkerPlacement.SlideOff(new Vector2(100f, 100f), 22f, plate, Vector2.up), "clear: untouched");
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
            // A live victim mark with darkness on, then what HardClear does on scene unload / disable.
            var m = new MarkPresentationModel();
            var marked = new MarkInputs { MatchRunning = true, Role = MarkRole.Victim, MarkKey = 42, Remaining = 7f, Window = 7f,
                                          ArmSeconds = 0.6f, HunterDistance = 1f, InnerRadius = 4f, BannerMayShow = true };
            MarkOutputs o = default;
            for (int i = 0; i < 60; i++) o = m.Step(marked, 1f / 60f);
            Assert.Greater(o.Dim, 0f);
            Assert.Greater(o.Cold, 0f);
            m.Reset();
            Assert.IsTrue(m.Current.IsClean, "cleared at once, no fade");
            Assert.IsTrue(m.Step(default, 1f / 60f).IsClean, "and stays clean with nothing to show");
            // The same mark seen again after an unload is not announced twice (memory survives; a new match forgets it).
            Assert.AreEqual(MarkEvents.None, m.Step(marked, 1f / 60f).Events & MarkEvents.Marked);

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
