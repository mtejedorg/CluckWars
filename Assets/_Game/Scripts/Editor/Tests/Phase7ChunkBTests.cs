using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 7 chunk B (round-4 findings 1, 2, 3, 10, 20): the held move reads on grass, the cancel zone, one forward for
    /// preview and release, the fizzle puff, and the small targets.
    /// </summary>
    public sealed class Phase7ChunkBTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private static float CssPx(string css, string selector, string property)
        {
            var block = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            Assert.IsTrue(block.Success, $"selector {selector} not found");
            var m = Regex.Match(block.Groups[1].Value, @"(?<![\w-])" + Regex.Escape(property) + @"\s*:\s*(-?[\d.]+)px");
            Assert.IsTrue(m.Success, $"{selector} has no {property}: Npx");
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // ---- 1: the held move reads on the REAL grass ---------------------------------------------------------

        private const string GroundMaterialPath = "Assets/_Game/Materials/GrassFloor.mat";

        /// <summary>The arena's ground: GrassFloor.mat's base map (decoded from the file, so no import setting matters)
        /// times its base colour. Returns the sRGB pixel colours.</summary>
        private static Color[] GroundPixels()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            Assert.IsNotNull(mat, GroundMaterialPath);
            var tex = mat.GetTexture("_BaseMap");
            Assert.IsNotNull(tex, "the ground material has no base map");
            string path = AssetDatabase.GetAssetPath(tex);
            Color tint = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(
                    Path.Combine(Path.GetDirectoryName(Application.dataPath), path))), path);
                var px = decoded.GetPixels();
                for (int i = 0; i < px.Length; i++) px[i] = new Color(px[i].r * tint.r, px[i].g * tint.g, px[i].b * tint.b, 1f);
                return px;
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        private static Color Over(Color top, Color under)
        {
            float a = top.a;
            return new Color(top.r * a + under.r * (1f - a), top.g * a + under.g * (1f - a), top.b * a + under.b * (1f - a), 1f);
        }

        /// <summary>Colours to hold the ink stroke against: the mean pixel and the mean of the lightest decile.</summary>
        private static (Color mean, Color lightestDecile, Color p99) GrassStats()
        {
            var px = GroundPixels();
            var lum = new float[px.Length];
            var order = new int[px.Length];
            double r = 0, g = 0, b = 0;
            for (int i = 0; i < px.Length; i++)
            {
                lum[i] = AbilityPalette.Luminance(px[i]);
                order[i] = i;
                r += px[i].r; g += px[i].g; b += px[i].b;
            }
            System.Array.Sort(order, (x, y) => lum[x].CompareTo(lum[y]));

            int top = px.Length / 10;
            double tr = 0, tg = 0, tb = 0;
            for (int k = 0; k < top; k++)
            {
                var c = px[order[px.Length - 1 - k]];
                tr += c.r; tg += c.g; tb += c.b;
            }
            return (new Color((float)(r / px.Length), (float)(g / px.Length), (float)(b / px.Length), 1f),
                    new Color((float)(tr / top), (float)(tg / top), (float)(tb / top), 1f),
                    px[order[(int)(px.Length * 0.99f)]]);
        }

        [Test]
        public void TheGroundMaterial_IsTheOneTheGameSceneUses()
        {
            string guid = AssetDatabase.AssetPathToGUID(GroundMaterialPath);
            Assert.IsNotEmpty(guid, GroundMaterialPath);
            StringAssert.Contains(guid, Read("Assets/_Game/Scenes/Game.unity"),
                "the arena ground must be GrassFloor.mat; if it moves, point GroundMaterialPath at the new material");
        }

        [Test]
        public void TheHeldStrokes_KeepAThreeToOneEdge_AgainstTheRealGrassTexture()
        {
            var (mean, lightest, p99) = GrassStats();
            var ink = FeedbackTuning.PreviewInkUnderStrokeColor;

            foreach (var (name, ground) in new[] { ("mean", mean), ("lightest decile", lightest), ("99th percentile pixel", p99) })
            {
                // Every held state (solid accent, cream dashed no-target, grey cancel) sits on the same near-opaque ink
                // under-stroke, so that is the outer edge the player sees against the grass.
                float edge = AbilityPalette.ContrastRatio(Over(ink, ground), ground);
                Assert.GreaterOrEqual(edge, 3f, $"ink under-stroke vs grass ({name}) = {edge:0.00}:1");
            }

            // And the cream dashes / grey cancel line on that ink must read too (they cannot rely on the grass).
            var inkOnGrass = Over(ink, lightest);
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(FeedbackTuning.NoTargetPreviewStalkColor, inkOnGrass), 3f, "cream dashes on ink");
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(FeedbackTuning.CancelArmedPreviewColor, inkOnGrass), 3f, "grey cancel line on ink");
        }

        [Test]
        public void TheCreamAndGreyStrokes_AloneAreNotEnoughOnGrass_SoTheInkUnderStrokeIsLoadBearing()
        {
            // The reason for the under-stroke, pinned so a "simplification" cannot drop it: the bare cream / grey outlines
            // fall under 3:1 against the real grass.
            var (mean, lightest, _) = GrassStats();
            Assert.Less(AbilityPalette.ContrastRatio(FeedbackTuning.CancelArmedPreviewColor, mean), 3f, "grey vs mean grass");
            Assert.Less(AbilityPalette.ContrastRatio(FeedbackTuning.NoTargetPreviewStalkColor, lightest), 3f, "cream vs lightest grass");
        }

        [Test]
        public void HeldShape_HasAnInkRim_AndACreamFillOfAboutAQuarter()
        {
            Assert.That(FeedbackTuning.HeldFillAlpha, Is.InRange(0.2f, 0.3f));
            Assert.Less(FeedbackTuning.HeldFillAlphaCancelArmed, FeedbackTuning.HeldFillAlpha, "the armed (will-not-fire) shape is fainter");
            Assert.GreaterOrEqual(FeedbackTuning.PreviewInkUnderStrokeColor.a, 0.85f);
            // 2-3 px a side at ~96 px per world unit.
            Assert.That(FeedbackTuning.PreviewInkUnderStrokeMargin / 2f * 96f, Is.InRange(2f, 3.5f));

            string tele = Read("Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs");
            StringAssert.Contains("UpdateUnderlays(_shapeBuf, dashed)", tele, "every held state (solid, dashed, grey) gets the under-stroke and fill");
            StringAssert.Contains("PreviewInkUnderStrokeColor", tele);
            StringAssert.Contains("HeldFillColor", tele);
        }

        [Test]
        public void OtherGuides_LoseTheirInkEdgeWhileAMoveIsHeld()
        {
            Assert.Less(FeedbackTuning.AmbientInkAlphaWhileAimingMultiplier, 0.5f);
            Assert.Less(FeedbackTuning.AmbientSlotOutlineAlphaSuppressed, FeedbackTuning.AmbientSlotOutlineAlphaCooldown);
            StringAssert.Contains("AmbientInkAlphaWhileAimingMultiplier", Read("Assets/_Game/Scripts/Visuals/AbilitySlotOverlay.cs"));
        }

        // ---- 2: the cancel zone ---------------------------------------------------------------------------------

        [Test]
        public void CancelZone_ShowsOnlyOnceAHoldPassesTheTapThreshold()
        {
            float t = FeedbackTuning.TapHoldThresholdSeconds;
            Assert.IsFalse(HoldCancelRules.ZoneVisible(true, 0f, t), "the down frame is still a tap");
            Assert.IsFalse(HoldCancelRules.ZoneVisible(true, t - 0.01f, t));
            Assert.IsTrue(HoldCancelRules.ZoneVisible(true, t, t));
            Assert.IsTrue(HoldCancelRules.ZoneVisible(true, 2f, t), "visible whether or not it is armed");
            Assert.IsFalse(HoldCancelRules.ZoneVisible(false, 2f, t), "released: gone");
        }

        [Test]
        public void CancelZone_IsBetween40And60Dp_AndMatchesTheArmedBand()
        {
            Assert.That(HoldCancelRules.ZoneDp, Is.InRange(40f, 60f));
            Assert.That(HoldCancelRules.ArmDp, Is.InRange(32f, 40f));
            Assert.AreEqual(8f, HoldCancelRules.DisarmDp - HoldCancelRules.ArmDp, 1e-4f, "+8 dp hysteresis");
            Assert.AreEqual(HoldCancelRules.DisarmDp, HoldCancelRules.ZoneDp, 1e-4f, "the drawn zone reaches the disarm line");

            string uss = Read("Assets/UI/Styles/TouchControls.uss");
            float px = CssPx(uss, ".cw-edge-glow--left, .cw-edge-glow--right", "width");
            Assert.That(px / 2.35f, Is.InRange(40f, 60f), "the strip is 40-60 dp deep at 1 dp = 2.35 px");
            Assert.AreEqual(px, CssPx(uss, ".cw-edge-glow--top, .cw-edge-glow--bottom", "height"));
        }

        [Test]
        public void CancelZone_IsDimUntilArmed_ThenBright_AndTheIconDimsToFortyPercent()
        {
            string uss = Read("Assets/UI/Styles/TouchControls.uss");
            var dim = Regex.Match(uss, @"\.cw-edge-glow \{[^}]*background-color: rgba\(42, 26, 12, ([\d.]+)\)");
            var bright = Regex.Match(uss, @"\.cw-edge-glow--armed \{ background-color: rgba\(254, 245, 224, ([\d.]+)\)");
            Assert.IsTrue(dim.Success && bright.Success, "dim zone = ink wash, armed zone = cream");
            Assert.Greater(float.Parse(bright.Groups[1].Value, CultureInfo.InvariantCulture),
                           float.Parse(dim.Groups[1].Value, CultureInfo.InvariantCulture) * 1.5f, "armed is clearly brighter");
            StringAssert.Contains(".cw-hex--cancel-armed .cw-hex-icon { opacity: 0.4; }", uss);
            Assert.GreaterOrEqual(CssPx(uss, ".cw-hex-x-bar", "height"), 14f);
            StringAssert.Contains("border-left-width: 4px", Regex.Match(uss, @"\.cw-hex-x-bar \{[^}]*\}").Value, "ink-outlined X");
        }

        [Test]
        public void TouchController_EvaluatesTheBandOnTheDownFrame_ShowsTheZoneAndDimsTheIcon()
        {
            string src = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            int down = src.IndexOf("private void OnHexDown", System.StringComparison.Ordinal);
            int move = src.IndexOf("private void OnHexMove", System.StringComparison.Ordinal);
            Assert.Greater(down, 0);
            int call = src.IndexOf("EvaluateCancelBand(slot, evt.position)", down, System.StringComparison.Ordinal);
            Assert.That(call, Is.GreaterThan(down).And.LessThan(move), "the down frame runs HoldCancelRules too");
            StringAssert.Contains("HoldCancelRules.ZoneVisible", src);
            StringAssert.Contains("CancelArmedClass", src);
            StringAssert.Contains("_armedEdge[slot] = armed ? edge : ScreenEdge.None", src);
        }

        // ---- 3: one forward for the preview and the release ------------------------------------------------------

        [Test]
        public void ForwardChoice_ExplicitAimWins_ThenSoftLock_ThenFacing()
        {
            var facing = Vector3.forward;
            var mouse = new Vector2(1f, 0f);
            var pick = new Vector2(0f, -1f);

            Assert.AreEqual(Vector3.right, AbilityAimForward.Choose(mouse, pick, facing, out bool aimed));
            Assert.IsTrue(aimed, "mouse / stick byte beats the soft-lock");
            Assert.AreEqual(Vector3.back, AbilityAimForward.Choose(Vector2.zero, pick, facing, out aimed));
            Assert.IsTrue(aimed, "touch soft-lock pick");
            Assert.AreEqual(facing, AbilityAimForward.Choose(Vector2.zero, Vector2.zero, facing, out aimed));
            Assert.IsFalse(aimed, "nothing resolved: the bird's facing");
        }

        [Test]
        public void SoftLockCone_PerDevice_IsTouch30_Pad12_KeyboardNone()
        {
            Assert.AreEqual(30f, AimSoftLock.HalfAngleFor(InputDeviceKind.Touch));
            Assert.AreEqual(12f, AimSoftLock.HalfAngleFor(InputDeviceKind.Gamepad));
            Assert.AreEqual(0f, AimSoftLock.HalfAngleFor(InputDeviceKind.KeyboardMouse));
            Assert.AreEqual(0f, AimSoftLock.HalfAngleFor(InputDeviceKind.None));
            Assert.AreEqual(AimSoftLock.HalfAngleFor(true, false), AimSoftLock.HalfAngleFor(InputDeviceKind.Touch), "button bits and device agree");
            Assert.AreEqual(AimSoftLock.HalfAngleFor(false, true), AimSoftLock.HalfAngleFor(InputDeviceKind.Gamepad));
        }

        private static ChickenController Probe(Vector3 pos, Vector3 fwd)
        {
            var go = new GameObject("Phase7B_Probe");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            return go.AddComponent<ChickenController>();
        }

        [Test]
        public void PreviewForward_EqualsFireForward_ForEveryInputCase()
        {
            // Both the preview (AbilityTelegraph / AbilitySlotOverlay) and the release (AbilityController.ApplyAimForFire)
            // call AbilityAimForward.Resolve with the same inputs; this pins what it answers for each device.
            var slam = TestAssets.Load<AbilityBaseSO>("Assets/_Game/Data/Abilities/WingSlam.asset");
            Assert.IsTrue(slam.UsesAim && slam.RotatesToAim, "precondition: a directional, turn-to-aim move");

            var caster = Probe(Vector3.zero, Vector3.forward);
            var rival = Probe(new Vector3(Mathf.Sin(25f * Mathf.Deg2Rad), 0f, Mathf.Cos(25f * Mathf.Deg2Rad)) * 3f, Vector3.back);
            var scratch = new Vector2[8];
            ChickenController.ActiveControllers.Add(caster);
            ChickenController.ActiveControllers.Add(rival);
            try
            {
                Vector3 toRival = (rival.transform.position - caster.transform.position).normalized;
                var mouseAim = new Vector2(-1f, 0f);

                // Touch: no explicit aim, soft-lock 30 -> toward the rival 25 degrees off the heading.
                var touch = AbilityAimForward.Resolve(slam, caster, Vector2.zero, AimSoftLock.HalfAngleFor(InputDeviceKind.Touch), scratch, out bool aimed);
                Assert.IsTrue(aimed);
                Assert.Less(Vector3.Angle(touch, toRival), 0.5f, "touch preview and fire both point at the soft-lock pick");
                Assert.Greater(Vector3.Angle(touch, caster.transform.forward), 20f, "...not at the heading");

                // Pad: 12 degrees cannot reach a rival 25 degrees off -> the heading.
                var pad = AbilityAimForward.Resolve(slam, caster, Vector2.zero, AimSoftLock.HalfAngleFor(InputDeviceKind.Gamepad), scratch, out aimed);
                Assert.IsFalse(aimed);
                Assert.Less(Vector3.Angle(pad, caster.transform.forward), 0.01f);

                // Mouse / right stick: the explicit byte wins, even over a rival the soft-lock would take.
                var mouse = AbilityAimForward.Resolve(slam, caster, mouseAim, AimSoftLock.HalfAngleFor(InputDeviceKind.Touch), scratch, out aimed);
                Assert.IsTrue(aimed);
                Assert.Less(Vector3.Angle(mouse, new Vector3(mouseAim.x, 0f, mouseAim.y)), 0.01f);

                // Keyboard + mouse with no aim: no magnetism, heading.
                var keys = AbilityAimForward.Resolve(slam, caster, Vector2.zero, AimSoftLock.HalfAngleFor(InputDeviceKind.KeyboardMouse), scratch, out aimed);
                Assert.IsFalse(aimed);
                Assert.Less(Vector3.Angle(keys, caster.transform.forward), 0.01f);

                // The same call twice answers the same (the preview polls it every frame, the cast once).
                Assert.AreEqual(touch, AbilityAimForward.Resolve(slam, caster, Vector2.zero, 30f, scratch, out _));
            }
            finally
            {
                ChickenController.ActiveControllers.Remove(caster);
                ChickenController.ActiveControllers.Remove(rival);
                Object.DestroyImmediate(caster.gameObject);
                Object.DestroyImmediate(rival.gameObject);
            }
        }

        [Test]
        public void AMoveThatDoesNotTurnToTheAim_KeepsTheHeading_OnEveryDevice()
        {
            var burst = TestAssets.Load<AbilityBaseSO>("Assets/_Game/Data/Abilities/SpeedBurst.asset");
            var caster = Probe(Vector3.zero, Vector3.forward);
            try
            {
                var fwd = AbilityAimForward.Resolve(burst, caster, new Vector2(1f, 0f), 30f, new Vector2[8], out bool aimed);
                Assert.IsFalse(aimed);
                Assert.Less(Vector3.Angle(fwd, Vector3.forward), 0.01f);
            }
            finally { Object.DestroyImmediate(caster.gameObject); }
        }

        [Test]
        public void ThePreviewTheMarksAndThePip_AllCallTheSameFunctionAsTheCast_SourceGuard()
        {
            string ctl = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            string tele = Read("Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs");
            string overlay = Read("Assets/_Game/Scripts/Visuals/AbilitySlotOverlay.cs");

            StringAssert.Contains("AbilityAimForward.Resolve(", ctl);
            StringAssert.Contains("AbilityAimForward.Resolve(", tele);
            StringAssert.DoesNotContain("PickSoftLockDirection", ctl, "the controller's private copy of the pick is gone");

            // The shape, the target marks and the pip's "hot" verdict all read the one preview forward.
            StringAssert.Contains("Vector3 fwd = _previewForward;", tele);
            StringAssert.Contains("private Vector3 AimedForward() => _previewForward;", tele);
            StringAssert.Contains("_telegraph.FireForward(ability)", overlay);
            StringAssert.Contains("WouldAffect(_controller, candidate, forward)", overlay);
            StringAssert.DoesNotContain("ability.WouldAffect(_controller, candidate))", overlay, "the pip no longer asks along the heading only");
        }

        // ---- 10: the fizzle puff ------------------------------------------------------------------------------

        [Test]
        public void FizzlePuff_Is90px_OffTheTopRightOfTheHex_Starting70Percent()
        {
            string uss = Read("Assets/UI/Styles/TouchControls.uss");
            Assert.AreEqual(90f, CssPx(uss, ".cw-hex-puff", "width"));
            Assert.AreEqual(90f, CssPx(uss, ".cw-hex-puff", "height"));
            const float hex = 150f;
            float left = CssPx(uss, ".cw-hex-puff", "left"), top = CssPx(uss, ".cw-hex-puff", "top");
            float cx = left + 45f, cy = top + 45f;
            Assert.Greater(cx, hex * 0.85f, "centred near the right edge");
            Assert.Less(cy, hex * 0.15f, "centred near the top edge");
            Assert.AreEqual(0.7f, FeedbackTuning.FizzlePuffStartAlpha, 1e-6f);
            StringAssert.Contains("FeedbackTuning.FizzlePuffStartAlpha * (1f - t)", Read("Assets/_Game/Scripts/Input/TouchControlsController.cs"));
        }

        // ---- 20: small targets ----------------------------------------------------------------------------------

        [Test]
        public void InRangePip_Is40px()
        {
            string uss = Read("Assets/UI/Styles/TouchControls.uss");
            Assert.AreEqual(40f, CssPx(uss, ".cw-hex-pip", "width"));
            Assert.AreEqual(40f, CssPx(uss, ".cw-hex-pip", "height"));
            Assert.AreEqual(55f, CssPx(uss, ".cw-hex-pip", "left"), "centred on the 150 px hex");
        }

        [Test]
        public void SettingsSlider_HasABigThumb_AFullWidthTrack_AndAPercentReadout()
        {
            string uss = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            Assert.GreaterOrEqual(CssPx(uss, ".cw-setting-slider .unity-base-slider__dragger", "width"), SliderRowRules.MinThumbPx);
            Assert.GreaterOrEqual(CssPx(uss, ".cw-setting-slider .unity-base-slider__dragger", "height"), SliderRowRules.MinThumbPx);
            StringAssert.Contains("flex-grow: 1", Regex.Match(uss, @"\.cw-setting-slider \{[^}]*\}").Value, "the slider spans the row");

            string uxml = Read("Assets/UI/MainMenu.uxml");
            foreach (var n in new[] { "MusicVolumePercent", "SfxVolumePercent" })
                StringAssert.Contains($"name=\"{n}\"", uxml);

            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("BindFullRowDrag(slider)", menu);
            StringAssert.Contains("SliderRowRules.Percent", menu);
        }

        [Test]
        public void SliderRowRules_MapThePointerOntoTheTrack_AndClamp()
        {
            // Track 100..1100 (1000 wide), knob 72: the knob centre travels 136..1064.
            Assert.AreEqual(0f, SliderRowRules.FractionFromPointer(100f, 100f, 1000f, 72f), 1e-5f);
            Assert.AreEqual(0f, SliderRowRules.FractionFromPointer(-50f, 100f, 1000f, 72f), "left of the track pins to 0");
            Assert.AreEqual(1f, SliderRowRules.FractionFromPointer(5000f, 100f, 1000f, 72f), "right of the track pins to 100%");
            Assert.AreEqual(0.5f, SliderRowRules.FractionFromPointer(600f, 100f, 1000f, 72f), 1e-5f);
            Assert.AreEqual(0f, SliderRowRules.FractionFromPointer(600f, 100f, 40f, 72f), "a degenerate track never divides by zero");
            Assert.AreEqual(0.25f, SliderRowRules.ValueFromFraction(0.25f, 0f, 1f), 1e-6f);
            Assert.AreEqual(5.5f, SliderRowRules.ValueFromFraction(0.5f, 1f, 10f), 1e-5f);
        }

        [Test]
        public void SliderPercent_IsInvariant_AndRounded()
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("es-ES");
                Assert.AreEqual("73%", SliderRowRules.Percent(0.734f));
                Assert.AreEqual("0%", SliderRowRules.Percent(-1f));
                Assert.AreEqual("100%", SliderRowRules.Percent(3f));
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        [Test]
        public void LeaderboardDot_HasACreamRing_ThatContrastsWithThePanel()
        {
            string uss = Read("Assets/UI/Styles/MatchTopBar.uss");
            var block = Regex.Match(uss, @"\.cw-lb-dot \{[^}]*\}").Value;
            StringAssert.Contains("border-left-width: 3px", block);
            StringAssert.Contains("border-left-color: rgb(254, 245, 224)", block);
            var cream = new Color32(254, 245, 224, 255);
            var panel = new Color32(20, 14, 8, 255);
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(cream, panel), 7f);
            // The P4 teal on its own against the panel is what the audit measured (2.73:1 on its background); the ring is
            // the edge that does not depend on any player colour.
            Assert.GreaterOrEqual(CssPx(uss, ".cw-lb-dot", "width"), 36f);
        }
    }
}
