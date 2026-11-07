using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.Localization;
using CluckWars.UI;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Feedback batch C3, world-space half: the badge stack draws state glyph sprites, the new rows (Waddle, Egg,
    /// Hidden, Speeding) follow the spec section 5 priority inside the three-row cap, the HIDDEN row is the owner's
    /// own cue only, and Chicken.prefab carries every sprite, derived from the <see cref="StateGlyphStyle"/> map.
    /// </summary>
    public sealed class StatusBadgeSpriteTests
    {
        private const string ChickenPrefab = "Assets/_Game/Prefabs/Chicken.prefab";
        private const string TurtleModeAsset = "Assets/_Game/Data/Abilities/TurtleMode.asset";

        private static readonly ChickenStatusBadges.Badge[] SpriteBadges =
        {
            ChickenStatusBadges.Badge.Stun, ChickenStatusBadges.Badge.Slow, ChickenStatusBadges.Badge.Waddle,
            ChickenStatusBadges.Badge.Egg, ChickenStatusBadges.Badge.Hidden, ChickenStatusBadges.Badge.Speed,
        };

        private static List<ChickenStatusBadges.Badge> Collect(bool stun = false, bool root = false, bool slow = false,
            bool shell = false, bool immune = false, bool hidden = false, bool speed = false, int rows = -1)
        {
            if (rows < 0) rows = FeedbackTuning.StatusBadgeMaxRows;
            var buf = new ChickenStatusBadges.Badge[rows];
            int n = ChickenStatusBadges.Collect(stun, root, slow, shell, immune, hidden, speed, buf);
            return buf.Take(n).ToList();
        }

        // ---- mapping to the state glyph sprites ----------------------------------------------------------

        [TestCase(ChickenStatusBadges.Badge.Stun,   StateGlyphStyle.StateGlyph.Stunned)]
        [TestCase(ChickenStatusBadges.Badge.Slow,   StateGlyphStyle.StateGlyph.Slowed)]
        [TestCase(ChickenStatusBadges.Badge.Waddle, StateGlyphStyle.StateGlyph.ShieldWalk)]
        [TestCase(ChickenStatusBadges.Badge.Egg,    StateGlyphStyle.StateGlyph.ShieldEgg)]
        [TestCase(ChickenStatusBadges.Badge.Hidden, StateGlyphStyle.StateGlyph.Hidden)]
        [TestCase(ChickenStatusBadges.Badge.Speed,  StateGlyphStyle.StateGlyph.Speeding)]
        public void EachBadge_MapsToItsStateGlyph(ChickenStatusBadges.Badge badge, StateGlyphStyle.StateGlyph expected)
        {
            Assert.IsTrue(ChickenStatusBadges.TryGlyphFor(badge, out var glyph));
            Assert.AreEqual(expected, glyph);
        }

        [Test]
        public void Root_HasNoSprite_AndStaysText()
        {
            Assert.IsFalse(ChickenStatusBadges.TryGlyphFor(ChickenStatusBadges.Badge.Root, out _));
            Assert.IsFalse(ChickenStatusBadges.TryGlyphFor(ChickenStatusBadges.Badge.None, out _));
        }

        // ---- the Hidden row is the owner's own cue --------------------------------------------------------

        [Test]
        public void HiddenRow_IsNeverBuiltForANonAuthorityBird()
        {
            for (float op = 0f; op <= 1.0001f; op += 0.05f)
                Assert.IsFalse(ChickenStatusBadges.ShowHiddenRow(false, op), "remote peer at opacity " + op);
        }

        [Test]
        public void HiddenRow_IsBuiltForTheAuthorityBird_OnlyWhileFaded()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<InvisibilityAbilitySO>("Assets/_Game/Data/Abilities/Invisibility.asset");
            Assert.IsNotNull(shipped);
            Assert.IsTrue(ChickenStatusBadges.ShowHiddenRow(true, shipped.Opacity), "the shipped Hide opacity must show the row");
            Assert.IsFalse(ChickenStatusBadges.ShowHiddenRow(true, 1f), "fully visible");
            Assert.IsFalse(ChickenStatusBadges.ShowHiddenRow(true, 0f), "0 is the networked unset default, not invisible");
        }

        [Test]
        public void HiddenRow_FollowsTheSameFadedPredicateAsTheRestOfTheHideTells()
        {
            for (float op = 0f; op <= 1.0001f; op += 0.01f)
                Assert.AreEqual(ControlStateVFX.IsFadedOpacity(op), ChickenStatusBadges.ShowHiddenRow(true, op), "opacity " + op);
        }

        // ---- priority and the three-row cap ---------------------------------------------------------------

        [Test]
        public void MaxRows_StaysThree()
        {
            Assert.AreEqual(3, FeedbackTuning.StatusBadgeMaxRows);
        }

        [Test]
        public void EveryStateOn_FillsOnlyThreeRows_InSpecPriority()
        {
            // Stun > Egg > Waddle > Hidden > Root > Slow > Speed (spec section 5). Walk and egg are exclusive phases.
            var walk = Collect(stun: true, root: true, slow: true, shell: true, immune: false, hidden: true, speed: true);
            CollectionAssert.AreEqual(new[] { ChickenStatusBadges.Badge.Stun, ChickenStatusBadges.Badge.Waddle, ChickenStatusBadges.Badge.Hidden }, walk);

            var egg = Collect(stun: true, shell: true, immune: true, hidden: true);
            CollectionAssert.AreEqual(new[] { ChickenStatusBadges.Badge.Stun, ChickenStatusBadges.Badge.Egg, ChickenStatusBadges.Badge.Hidden }, egg);
        }

        [Test]
        public void TheRealisticWorstCase_StunSlowZoom_IsThreeRows()
        {
            var rows = Collect(stun: true, slow: true, speed: true);
            CollectionAssert.AreEqual(new[] { ChickenStatusBadges.Badge.Stun, ChickenStatusBadges.Badge.Slow, ChickenStatusBadges.Badge.Speed }, rows);
        }

        [Test]
        public void ShieldPhases_AreExclusive_AndOnlyWhileTheShieldRuns()
        {
            CollectionAssert.AreEqual(new[] { ChickenStatusBadges.Badge.Waddle }, Collect(shell: true, immune: false));
            CollectionAssert.AreEqual(new[] { ChickenStatusBadges.Badge.Egg }, Collect(shell: true, immune: true));
            CollectionAssert.IsEmpty(Collect(shell: false, immune: true), "control immunity without the Shield slot running draws no row");
        }

        [Test]
        public void SpeedAndHidden_AreEachTheirOwnRow()
        {
            CollectionAssert.AreEqual(new[] { ChickenStatusBadges.Badge.Speed }, Collect(speed: true));
            CollectionAssert.AreEqual(new[] { ChickenStatusBadges.Badge.Hidden }, Collect(hidden: true));
            CollectionAssert.IsEmpty(Collect());
        }

        [Test]
        public void Collect_NeverWritesPastTheBuffer()
        {
            var rows = Collect(stun: true, slow: true, speed: true, hidden: true, rows: 2);
            Assert.AreEqual(2, rows.Count);
        }

        // ---- countdown comes from the ability asset ------------------------------------------------------

        [Test]
        public void ShieldCountdown_WalkAndEgg_AreReadFromTheShippedAsset()
        {
            var turtle = AssetDatabase.LoadAssetAtPath<TurtleModeAbilitySO>(TurtleModeAsset);
            Assert.IsNotNull(turtle);
            Assert.AreEqual(turtle.WalkSeconds + turtle.EggSeconds, turtle.Duration, 1e-4f, "the run is the walk plus the egg");

            Assert.AreEqual(turtle.WalkSeconds, ChickenStatusBadges.ShellPhaseSeconds(false, turtle.Duration, turtle.EggSeconds), 1e-4f, "walk, run start");
            Assert.AreEqual(0f, ChickenStatusBadges.ShellPhaseSeconds(false, turtle.EggSeconds, turtle.EggSeconds), 1e-4f, "walk, egg about to start");
            Assert.AreEqual(turtle.EggSeconds, ChickenStatusBadges.ShellPhaseSeconds(true, turtle.EggSeconds, turtle.EggSeconds), 1e-4f, "egg, start");
            Assert.AreEqual(0f, ChickenStatusBadges.ShellPhaseSeconds(true, -0.2f, turtle.EggSeconds), "never negative");
        }

        [Test]
        public void NewBadgeWords_ComeFromUiText()
        {
            foreach (var key in new[] { UiKeys.HudBadgeWaddle, UiKeys.HudBadgeEgg, UiKeys.HudBadgeHidden, UiKeys.HudBadgeFast })
                Assert.IsFalse(string.IsNullOrWhiteSpace(UiText.Get(key)), key);
            Assert.AreEqual("WADDLE", UiText.Get(UiKeys.HudBadgeWaddle));
            Assert.AreEqual("EGG", UiText.Get(UiKeys.HudBadgeEgg));
            Assert.AreEqual("HIDDEN", UiText.Get(UiKeys.HudBadgeHidden));
            Assert.AreEqual("FAST", UiText.Get(UiKeys.HudBadgeFast), "D1: the shipped generic Speed word, not Zoom");
        }

        // ---- layout ----------------------------------------------------------------------------------------

        [Test]
        public void RowLayout_CentresTheGlyphAndTextPair()
        {
            ChickenStatusBadges.RowLayout(0.13f, 0.02f, 0.30f, out float gx, out float tx);
            Assert.AreEqual(-(0.13f + 0.02f + 0.30f) * 0.5f + 0.13f * 0.5f, gx, 1e-5f);
            Assert.AreEqual(gx + 0.13f * 0.5f + 0.02f, tx, 1e-5f, "text starts one gap right of the glyph");
            Assert.AreEqual(0f, (tx + 0.30f + gx - 0.13f * 0.5f) * 0.5f, 1e-5f, "pair centred on the origin");

            ChickenStatusBadges.RowLayout(0.13f, 0.02f, 0f, out float gx2, out _);
            Assert.AreEqual(0f, gx2, 1e-5f, "a glyph with no text sits on the origin");
        }

        // ---- the prefab carries every sprite --------------------------------------------------------------

        private static IEnumerable<FieldInfo> GlyphFields() =>
            typeof(ChickenStatusBadges).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(Sprite) && f.Name.StartsWith("_glyph", StringComparison.Ordinal));

        [Test]
        public void EveryGlyphField_IsNamedAfterAStateGlyph_AndEverySpriteBadgeHasOne()
        {
            var fieldStates = GlyphFields()
                .Select(f => Enum.Parse(typeof(StateGlyphStyle.StateGlyph), f.Name.Substring("_glyph".Length)))
                .Cast<StateGlyphStyle.StateGlyph>().ToList();
            var needed = SpriteBadges.Select(b => { ChickenStatusBadges.TryGlyphFor(b, out var g); return g; }).ToList();
            CollectionAssert.AreEquivalent(needed, fieldStates, "one sprite field per state a badge can draw");
        }

        [Test]
        public void ChickenPrefab_HasEverySpriteWired_ToTheStateGlyphMapAsset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            Assert.IsNotNull(prefab, ChickenPrefab);
            var badges = prefab.GetComponent<ChickenStatusBadges>();
            Assert.IsNotNull(badges, "ChickenStatusBadges on the prefab root");

            var so = new SerializedObject(badges);
            var problems = new List<string>();
            foreach (var field in GlyphFields())
            {
                var state = (StateGlyphStyle.StateGlyph)Enum.Parse(typeof(StateGlyphStyle.StateGlyph), field.Name.Substring("_glyph".Length));
                var expected = AssetDatabase.LoadAssetAtPath<Sprite>(StateGlyphStyle.SpritePathFor(state));
                var actual = so.FindProperty(field.Name).objectReferenceValue as Sprite;
                if (expected == null) problems.Add($"{state}: no sprite at {StateGlyphStyle.SpritePathFor(state)}");
                else if (actual == null) problems.Add($"{state}: {field.Name} is not wired on the prefab");
                else if (actual != expected) problems.Add($"{state}: {field.Name} is {AssetDatabase.GetAssetPath(actual)}, expected {StateGlyphStyle.SpritePathFor(state)}");
                else if (badges.SpriteFor(state) != expected) problems.Add($"{state}: SpriteFor disagrees with the field");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
