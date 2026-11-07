using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Feedback batches D0 / D0b: the world status badge stack is anchored to the nameplate pill (pill top + a gap), clears
    /// the tallest pill (the local "You" one), stays under the floating-text floor, and pops spawn from that same floor.
    /// </summary>
    public sealed class StatusBadgeLayoutTests
    {
        private const string ChickenPrefab = "Assets/_Game/Prefabs/Chicken.prefab";

        /// <summary>
        /// Root-local top of the local "You" pill, from the same code the runtime uses: the prefab's own label settings,
        /// a TextMesh built like ChickenNameplate.BuildLabel, and ChickenNameplate.YouPillHeight / PillTopLocalY.
        /// Takes the taller of "You" and a long player name, as the pill follows the text bounds.
        /// </summary>
        private static float LocalPillTop()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            Assert.IsNotNull(prefab, ChickenPrefab);
            var so = new SerializedObject(prefab.GetComponent<ChickenNameplate>());
            Vector3 offset = so.FindProperty("_offset").vector3Value;
            int fontSize = so.FindProperty("_fontSize").intValue;
            float charSize = so.FindProperty("_characterSize").floatValue;

            float top = float.MinValue;
            foreach (string label in new[] { "You", "DashFox", "PeckNoir" })
            {
                var go = new GameObject("PillProbe") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var tm = go.AddComponent<TextMesh>();
                    tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
                    tm.fontSize = fontSize; tm.characterSize = charSize; tm.fontStyle = FontStyle.Bold; tm.text = label;
                    var b = go.GetComponent<MeshRenderer>().localBounds;
                    float h = b.size.y > 0f ? b.size.y : 0.3f;   // the runtime's own fallback for an unbuilt mesh
                    float half = ChickenNameplate.YouPillHeight(h) * 0.5f;
                    top = Mathf.Max(top, ChickenNameplate.PillTopLocalY(offset.y, b.center.y, half, 1f));
                }
                finally { Object.DestroyImmediate(go); }
            }
            return top;
        }

        private static float Row1Centre(float pillTop) => pillTop + FeedbackTuning.StatusBadgeStackPillGap;

        /// <summary>Pivot-local top of <paramref name="rows"/> shown rows over a pill topping out at <paramref name="pillTop"/>
        /// (an upright bird: the same arithmetic ChickenStatusBadges.CurrentStackTopLocalY does through the root transform).</summary>
        private static float StackTop(float pillTop, int rows) => Row1Centre(pillTop) + ChickenStatusBadges.StackTopInRoot(rows);

        [Test]
        public void FirstRowClearsTheTallestNameplatePill()
        {
            float pillTop = LocalPillTop();
            float row1Bottom = Row1Centre(pillTop) - FeedbackTuning.StatusBadgeGlyphSize * 0.5f;
            Assert.GreaterOrEqual(row1Bottom, pillTop,
                "row 1 must sit on or above the local pill so the badges never cover or garble the name (pill top " + pillTop + ")");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void PopSpawnClearsTheShownStack(int rows)
        {
            float pillTop = LocalPillTop();
            // 0 rows: the stack is hidden, CurrentStackTopLocalY is the 2.12 fallback base.
            float top = rows == 0 ? FeedbackTuning.StatusBadgeStackBaseHeight : StackTop(pillTop, rows);
            float spawn = FloatingCombatText.PopSpawnHeight(top);
            Assert.GreaterOrEqual(spawn, top + FeedbackTuning.FloatingTextStackClearance - 1e-5f, "pops start above the stack top " + top);
            Assert.GreaterOrEqual(spawn, FeedbackTuning.FloatingTextSpawnHeight, "the spawn floor holds");
        }

        [Test]
        public void SpawnHeightGrowsWithTheRowsAndFloorsWithNone()
        {
            float pillTop = LocalPillTop();
            Assert.AreEqual(FeedbackTuning.FloatingTextSpawnHeight, FloatingCombatText.PopSpawnHeight(0f), 1e-5f);
            Assert.Greater(FloatingCombatText.PopSpawnHeight(StackTop(pillTop, 3)), FloatingCombatText.PopSpawnHeight(StackTop(pillTop, 1)) - 1e-5f);
        }

        [Test]
        public void FallbackBaseClearsTheTallestPillToo()
        {
            float pillTop = LocalPillTop();
            // Only birds with no nameplate use the fallback (batch D0c keeps 2.12): its bottom row centre must still be at or above the pill top.
            Assert.GreaterOrEqual(FeedbackTuning.StatusBadgeStackBaseHeight, pillTop,
                "the no-nameplate fallback row should not sit inside the pill");
        }

        [Test]
        public void GlyphIsAtLeastTheAskedPhoneSize()
        {
            Assert.AreEqual(0.30f, FeedbackTuning.StatusBadgeGlyphSize, 1e-5f);
            Assert.GreaterOrEqual(FeedbackTuning.StatusBadgeRowSpacing, FeedbackTuning.StatusBadgeGlyphSize * 0.85f,
                "rows must not collide beyond the sprites' transparent margins");
        }

        [Test]
        public void PopSpawnPositionPinsXzAndAddsTheLocalHeight()
        {
            var pivot = new Vector3(3f, 0.25f, -4f);
            Vector3 floor = FloatingCombatText.PopSpawnPosition(pivot, 0f);
            Assert.AreEqual(FeedbackTuning.FloatingTextSpawnHeight, floor.y - pivot.y, 1e-5f);
            Assert.AreEqual(pivot.x, floor.x, 1e-5f);
            Assert.AreEqual(pivot.z, floor.z, 1e-5f);
            Vector3 high = FloatingCombatText.PopSpawnPosition(pivot, 3f);
            Assert.AreEqual(3f + FeedbackTuning.FloatingTextStackClearance, high.y - pivot.y, 1e-5f);
        }

        [Test]
        public void MarkMarkerClearsTheBadgeStack()
        {
            // MarkerPlacement raises the floor to ChickenStatusBadges.CurrentStackTopLocalY when rows show, so the constant
            // only has to clear the empty-stack fallback.
            Assert.GreaterOrEqual(FeedbackTuning.MarkMarkerClearHeight, FeedbackTuning.StatusBadgeStackBaseHeight,
                "the marker floor clears the badge stack fallback base");
        }
    }
}
