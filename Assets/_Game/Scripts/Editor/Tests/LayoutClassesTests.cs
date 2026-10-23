using System.IO;
using System.Text.RegularExpressions;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// WP6 / PORTRAIT_AND_LANDSCAPE_SPEC §1.1: one helper classifies every UI root by screen shape, its thresholds live
    /// nowhere else, and the rule change for --narrow (portrait no longer counts) is visually neutral (S0).
    /// </summary>
    public sealed class LayoutClassesTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Classification ----------------------------------------------------------------------------------------

        //        w      h     portrait tall   short  narrow
        [TestCase(1920f, 1080f, false, false, true,  false, TestName = "Layout 16x9 at 1080 is short")]
        [TestCase(2157f,  961f, false, false, true,  false, TestName = "Layout Pixel 9 landscape canvas is short")]
        [TestCase(1663f, 1247f, false, false, false, true,  TestName = "Layout 4x3 tablet is narrow")]
        [TestCase(2560f, 1440f, false, false, false, false, TestName = "Layout 16x9 at 1440 is plain landscape")]
        [TestCase(1000f, 1000f, false, false, true,  true,  TestName = "Layout square counts as landscape and narrow")]
        [TestCase(1080f, 1920f, true,  false, false, false, TestName = "Layout 9x16 portrait is not tall")]
        [TestCase( 966f, 2147f, true,  true,  false, false, TestName = "Layout 9x20 portrait column is tall")]
        [TestCase( 540f,  960f, true,  false, true,  false, TestName = "Layout small 9x16 portrait is short and never narrow")]
        public void Classify(float w, float h, bool portrait, bool tall, bool isShort, bool narrow)
        {
            var s = LayoutClasses.Classify(w, h);
            Assert.IsTrue(s.Valid);
            Assert.AreEqual(portrait, s.Portrait, "portrait");
            Assert.AreEqual(!portrait, s.Landscape, "landscape");
            Assert.AreEqual(tall, s.Tall, "tall");
            Assert.AreEqual(isShort, s.Short, "short");
            Assert.AreEqual(narrow, s.Narrow, "narrow");
        }

        [Test]
        public void Thresholds_AreTheSpecValues_AndTheirEdges()
        {
            Assert.AreEqual(1.95f, LayoutClasses.TallAspect);
            Assert.AreEqual(1100f, LayoutClasses.ShortHeight);
            Assert.AreEqual(1.55f, LayoutClasses.NarrowAspect);

            Assert.IsTrue(LayoutClasses.Classify(2000f, 1099.9f).Short);
            Assert.IsFalse(LayoutClasses.Classify(2000f, 1100f).Short);
            Assert.IsTrue(LayoutClasses.Classify(1549f, 1000f).Narrow);
            Assert.IsFalse(LayoutClasses.Classify(1550f, 1000f).Narrow);
            Assert.IsTrue(LayoutClasses.Classify(1000f, 1000f).Narrow, "w/h = 1 is landscape and narrow");
            Assert.IsFalse(LayoutClasses.Classify(999f, 1000f).Narrow, "any portrait is never narrow");
            Assert.IsTrue(LayoutClasses.Classify(1000f, 1950f).Tall, "h/w exactly 1.95 is tall (>=)");
            Assert.IsFalse(LayoutClasses.Classify(1000f, 1949f).Tall);
        }

        [TestCase(1000f, 1950f)]
        [TestCase(2000f, 3900f)]
        public void Tall_ExactlyOnTheThreshold_IsTall_WhateverThePrecisionOfTheDivision(float w, float h)
        {
            // The first EditMode run failed 1000x1950: the runtime divided at double precision (1.95) and compared it
            // with 1.95f widened (1.9500000477). Classify casts the quotient back to float, so an exact edge is stable.
            Assume.That((float)(h / w), Is.EqualTo(LayoutClasses.TallAspect));
            Assert.IsTrue(LayoutClasses.Classify(w, h).Tall);
        }

        [TestCase(1550f, 1000f)]
        [TestCase(3100f, 2000f)]
        public void Narrow_ExactlyOnTheThreshold_IsNotNarrow(float w, float h)
        {
            Assume.That((float)(w / h), Is.EqualTo(LayoutClasses.NarrowAspect));
            Assert.IsFalse(LayoutClasses.Classify(w, h).Narrow, "w/h exactly 1.55 is not narrow (<)");
        }

        [Test]
        public void ExactlyOneOfPortraitAndLandscape_OverEveryShape()
        {
            for (float w = 300f; w <= 3000f; w += 37f)
            for (float h = 300f; h <= 3000f; h += 41f)
            {
                var s = LayoutClasses.Classify(w, h);
                Assert.IsTrue(s.Portrait ^ s.Landscape, $"{w}x{h}");
                Assert.IsFalse(s.Portrait && s.Narrow, $"{w}x{h}: portrait is never narrow");
                Assert.IsFalse(s.Landscape && s.Tall, $"{w}x{h}: landscape is never tall");
            }
        }

        [TestCase(0f, 1080f)]
        [TestCase(1920f, 0f)]
        [TestCase(float.NaN, 1080f)]
        [TestCase(1920f, float.NaN)]
        [TestCase(-5f, 100f)]
        public void UnmeasuredRect_IsNotAShape(float w, float h)
        {
            var s = LayoutClasses.Classify(w, h);
            Assert.IsFalse(s.Valid);
            Assert.IsFalse(s.Portrait || s.Landscape || s.Tall || s.Short || s.Narrow);
        }

        // ---- Apply ---------------------------------------------------------------------------------------------------

        [Test]
        public void Apply_Rotation_SwapsTheClasses_AndClearsTheOldOnes()
        {
            var ve = new VisualElement();
            LayoutClasses.Apply(ve, new Rect(0, 0, 2157, 961));   // landscape phone
            Assert.IsTrue(ve.ClassListContains(LayoutClasses.Landscape));
            Assert.IsTrue(ve.ClassListContains(LayoutClasses.Short));
            Assert.IsFalse(ve.ClassListContains(LayoutClasses.Portrait));

            LayoutClasses.Apply(ve, new Rect(0, 0, 966, 2147));   // the same phone held upright
            Assert.IsTrue(ve.ClassListContains(LayoutClasses.Portrait));
            Assert.IsTrue(ve.ClassListContains(LayoutClasses.Tall));
            Assert.IsFalse(ve.ClassListContains(LayoutClasses.Landscape));
            Assert.IsFalse(ve.ClassListContains(LayoutClasses.Short));
            Assert.IsFalse(ve.ClassListContains(LayoutClasses.Narrow));
        }

        [Test]
        public void Apply_UnmeasuredRect_LeavesTheLastShapeInPlace()
        {
            var ve = new VisualElement();
            LayoutClasses.Apply(ve, new Rect(0, 0, 1663, 1247));
            LayoutClasses.Apply(ve, new Rect(0, 0, float.NaN, float.NaN));
            Assert.IsTrue(ve.ClassListContains(LayoutClasses.Narrow));
            Assert.IsTrue(ve.ClassListContains(LayoutClasses.Landscape));
            Assert.DoesNotThrow(() => LayoutClasses.Apply(null, new Rect(0, 0, 100, 100)));
        }

        [Test]
        public void Bind_OnAnUnmeasuredElement_SetsNothingYet()
        {
            var measured = new VisualElement();
            var target = new VisualElement();
            LayoutClasses.Bind(measured, target);
            LayoutClasses.Bind(measured, target);   // a re-bind replaces, never throws
            Assert.IsFalse(target.ClassListContains(LayoutClasses.Landscape) || target.ClassListContains(LayoutClasses.Portrait));
        }

        [Test]
        public void BindAll_OnAnUnmeasuredElement_SetsNothingYet_AndSkipsNullTargets()
        {
            var measured = new VisualElement();
            var a = new VisualElement();
            var b = new VisualElement();
            Assert.DoesNotThrow(() => LayoutClasses.BindAll(measured, a, null, b));
            Assert.DoesNotThrow(() => LayoutClasses.BindAll(measured, a, b));   // a re-bind replaces the list
            Assert.DoesNotThrow(() => LayoutClasses.BindAll(null, a));
            Assert.DoesNotThrow(() => LayoutClasses.BindAll(measured, null));
            Assert.IsFalse(a.ClassListContains(LayoutClasses.Landscape) || a.ClassListContains(LayoutClasses.Portrait));
        }

        // ---- One helper, four roots ----------------------------------------------------------------------------------

        [Test]
        public void EveryRoot_BindsTheHelper_AndNoCallerKeepsItsOwnCopy()
        {
            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            string overlays = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            string hud = Read("Assets/_Game/Scripts/UI/MatchHudController.cs");
            string touch = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");

            StringAssert.Contains("LayoutClasses.Bind(_root);", menu);
            StringAssert.Contains("LayoutClasses.BindAll(_root, _lobbyOverlay, _matchEndOverlay, _leaveSheet, _sessionEndOverlay, _introGetReady);", overlays);
            StringAssert.Contains("LayoutClasses.Bind(_root, _topBarRoot ?? _root);", hud);
            StringAssert.Contains("LayoutClasses.Bind(_root, _hudRoot);", touch);
            // The device-mode switch is a different axis and stays its own method.
            StringAssert.Contains("private void UpdateLayoutMode()", touch);

            foreach (var (name, src) in new[] { ("MenuUiController", menu), ("MatchOverlaysController", overlays),
                                                 ("MatchHudController", hud), ("TouchControlsController", touch) })
            {
                Assert.IsFalse(Regex.IsMatch(src, @"EnableInClassList\(\s*""layout--"), $"{name} sets a layout-- class itself.");
                Assert.IsFalse(Regex.IsMatch(src, @"\b1100f\b|\b1\.55f\b|\b1\.95f\b"), $"{name} keeps its own layout threshold.");
                StringAssert.DoesNotContain("ShortLayoutHeight", src, name);
                StringAssert.DoesNotContain("NarrowLayoutAspect", src, name);
            }
        }

        // ---- WP9: portrait has its own rules; the WP6 twins are gone -------------------------------------------------

        [Test]
        public void NarrowRules_AreNotCopiedIntoPortrait_AnyMore()
        {
            // WP6 kept a .layout--portrait copy of every single-line .layout--narrow rule ("S0 parity"). WP9 replaced them
            // with real portrait styles (MenuPortraitLayoutTests pins those); the copies and their markers are gone.
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            StringAssert.DoesNotContain("S0 parity", css);
            StringAssert.DoesNotContain("S0 parity", Read("Assets/UI/Styles/MatchOverlays.uss"));
        }
    }

    /// <summary>The dev <c>-cw_safearea l,b,r,t</c> seam shared by SafeAreaPadding and HudControlZones.</summary>
    public sealed class SafeAreaSourceTests
    {
        [Test]
        public void Absent_IsNoOverride_AndNoError()
        {
            Assert.IsFalse(SafeAreaSource.TryParse(new[] { "CluckWars.exe", "-cw_hud", "1" }, out _, out var error));
            Assert.IsNull(error);
            Assert.IsFalse(SafeAreaSource.TryParse(null, out _, out error));
            Assert.IsNull(error);
        }

        [Test]
        public void Valid_ParsesLeftBottomRightTop_Pixels()
        {
            Assert.IsTrue(SafeAreaSource.TryParse(new[] { "x.exe", "-CW_SAFEAREA", "0,48,0,120.5" }, out var px, out var error));
            Assert.IsNull(error);
            Assert.AreEqual(new Vector4(0f, 48f, 0f, 120.5f), px);
        }

        [TestCase("1,2,3")]
        [TestCase("1,2,3,4,5")]
        [TestCase("a,0,0,0")]
        [TestCase("0,-1,0,0")]
        public void Malformed_IsRejected_WithAReason(string value)
        {
            Assert.IsFalse(SafeAreaSource.TryParse(new[] { "x.exe", "-cw_safearea", value }, out _, out var error));
            Assert.IsNotNull(error);
            StringAssert.Contains("-cw_safearea", error);
        }

        [Test]
        public void MissingValue_IsRejected_WithAReason()
        {
            Assert.IsFalse(SafeAreaSource.TryParse(new[] { "x.exe", "-cw_safearea" }, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void FromInsets_IsABottomLeftOriginRect_LikeScreenSafeArea()
        {
            // Portrait phone: 120 px punch-hole band at the top, 48 px gesture bar at the bottom.
            var r = SafeAreaSource.FromInsets(new Vector4(0f, 48f, 0f, 120f), 1080, 2400);
            Assert.AreEqual(Rect.MinMaxRect(0f, 48f, 1080f, 2280f), r);
            var none = SafeAreaSource.FromInsets(Vector4.zero, 1920, 1080);
            Assert.AreEqual(new Rect(0f, 0f, 1920f, 1080f), none);
        }

        [Test]
        public void FromInsets_WiderThanTheScreen_IsEmpty_NeverNegative()
        {
            var r = SafeAreaSource.FromInsets(new Vector4(900f, 0f, 900f, 0f), 1080, 2400);
            Assert.GreaterOrEqual(r.width, 0f);
            Assert.GreaterOrEqual(r.height, 0f);
        }

        [Test]
        public void ThePaddingAndTheChevronZones_ReadTheSameSource()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string padding = File.ReadAllText(Path.Combine(root, "Assets/_Game/Scripts/UI/SafeAreaPadding.cs"));
            string zones = File.ReadAllText(Path.Combine(root, "Assets/_Game/Scripts/Visuals/HudControlZones.cs"));
            StringAssert.Contains("SafeAreaSource.Current", padding);
            StringAssert.Contains("SafeAreaSource.Current", zones);
            Assert.IsFalse(Regex.IsMatch(padding, @"=\s*Screen\.safeArea"), "SafeAreaPadding reads Screen.safeArea directly.");
            Assert.IsFalse(Regex.IsMatch(zones, @"=\s*Screen\.safeArea"), "HudControlZones reads Screen.safeArea directly.");
        }
    }
}
