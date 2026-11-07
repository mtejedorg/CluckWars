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

        private static float StackTop(float pillTop) =>
            Row1Centre(pillTop)
            + FeedbackTuning.StatusBadgeRowSpacing * (FeedbackTuning.StatusBadgeMaxRows - 1)
            + FeedbackTuning.StatusBadgeGlyphSize * 0.5f;

        [Test]
        public void FirstRowClearsTheTallestNameplatePill()
        {
            float pillTop = LocalPillTop();
            float row1Bottom = Row1Centre(pillTop) - FeedbackTuning.StatusBadgeGlyphSize * 0.5f;
            // The 0.08 gap is a hair under the glyph's half height (0.085); the sprite art keeps a transparent margin of
            // that order, and the D0b captures show the visible glyph and text clear of the pill. Allow exactly that sliver.
            const float SpriteMargin = 0.01f;
            Assert.GreaterOrEqual(row1Bottom, pillTop - SpriteMargin,
                "row 1 must sit on or above the local pill so the badges never cover or garble the name (pill top " + pillTop + ")");
        }

        [Test]
        public void StackTopStaysUnderTheFloatingTextFloor()
        {
            float top = StackTop(LocalPillTop());
            Assert.Less(top, FeedbackTuning.FloatingTextSpawnHeight, "pops start above the full badge stack (top " + top + ")");
        }

        [Test]
        public void FallbackBaseClearsTheTallestPillToo()
        {
            float pillTop = LocalPillTop();
            Assert.GreaterOrEqual(FeedbackTuning.StatusBadgeStackBaseHeight - FeedbackTuning.StatusBadgeGlyphSize * 0.5f, pillTop - 0.05f,
                "the no-nameplate fallback should sit near the pill too");
        }

        [Test]
        public void SelfPopSpawnsAtTheFloatingTextConstant()
        {
            var pivot = new Vector3(3f, 0.25f, -4f);
            Vector3 p = FloatingCombatText.PopSpawnPosition(pivot);
            Assert.AreEqual(FeedbackTuning.FloatingTextSpawnHeight, p.y - pivot.y, 1e-5f);
            Assert.AreEqual(pivot.x, p.x, 1e-5f);
            Assert.AreEqual(pivot.z, p.z, 1e-5f);
        }

        [Test]
        public void MarkMarkerClearsTheBadgeStack()
        {
            Assert.GreaterOrEqual(FeedbackTuning.MarkMarkerClearHeight, StackTop(LocalPillTop()),
                "the on-screen mark marker must clear the badge stack");
        }
    }
}
