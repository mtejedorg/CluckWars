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

        // ---- F1: state-entry pops clear the row added this frame; self-pops follow the bird -------------

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AnEntryPopSpawnsAboveTheRowAddedThisFrame(int shownRows)
        {
            var active = new ChickenStatusBadges.Badge[FeedbackTuning.StatusBadgeMaxRows];
            var fill = new[] { ChickenStatusBadges.Badge.Stun, ChickenStatusBadges.Badge.Slow, ChickenStatusBadges.Badge.Speed };
            for (int i = 0; i < shownRows; i++) active[i] = fill[i];

            int rows = ChickenStatusBadges.RowsWithEntry(active, shownRows, ChickenStatusBadges.Badge.Hidden, active.Length);
            Assert.AreEqual(shownRows + 1, rows, "the entering row counts");

            float pillTop = LocalPillTop();
            float newRowTop = StackTop(pillTop, rows);
            float spawn = FloatingCombatText.PopSpawnHeight(newRowTop);
            Assert.GreaterOrEqual(spawn, newRowTop + FeedbackTuning.FloatingTextStackClearance - 1e-5f, "starts above the new row's top");
            Assert.GreaterOrEqual(spawn, FeedbackTuning.FloatingTextSpawnHeight, "the 2.50 floor holds");
            Assert.GreaterOrEqual(spawn, FloatingCombatText.PopSpawnHeight(StackTop(pillTop, shownRows)) - 1e-5f);
        }

        [Test]
        public void ARowAlreadyShownOrCappedAddsNothing()
        {
            var active = new[] { ChickenStatusBadges.Badge.Stun, ChickenStatusBadges.Badge.Waddle, ChickenStatusBadges.Badge.None };
            Assert.AreEqual(2, ChickenStatusBadges.RowsWithEntry(active, 2, ChickenStatusBadges.Badge.Stun, 3), "already shown");
            Assert.AreEqual(2, ChickenStatusBadges.RowsWithEntry(active, 2, ChickenStatusBadges.Badge.Egg, 3), "egg replaces the waddle row");
            Assert.AreEqual(2, ChickenStatusBadges.RowsWithEntry(active, 2, ChickenStatusBadges.Badge.None, 3), "no entry");
            Assert.AreEqual(2, ChickenStatusBadges.RowsWithEntry(active, 2, ChickenStatusBadges.Badge.Hidden, 2), "at the row cap");
            Assert.AreEqual(3, ChickenStatusBadges.RowsWithEntry(active, 2, ChickenStatusBadges.Badge.Hidden, 3));
        }

        private static readonly System.Reflection.BindingFlags NonPublic =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        /// <summary>A pool built the way Awake builds it (edit mode never runs Awake), plus a way to spawn and tick it.</summary>
        private sealed class PoolHarness
        {
            public readonly GameObject Go = new GameObject("FloatingTextTestPool");
            private readonly FloatingCombatText _pool;
            public PoolHarness()
            {
                _pool = Go.AddComponent<FloatingCombatText>();
                typeof(FloatingCombatText).GetMethod("BuildPool", NonPublic).Invoke(_pool, null);
            }
            public void Spawn(Vector3 pos, Transform follow) =>
                typeof(FloatingCombatText).GetMethod("SpawnInternal", NonPublic)
                    .Invoke(_pool, new object[] { pos, "TEST", Color.white, follow });
            public void Tick() => typeof(FloatingCombatText).GetMethod("LateUpdate", NonPublic).Invoke(_pool, null);
            public Vector3 PopPosition(int slot) => Go.transform.GetChild(slot).position;
        }

        [Test]
        public void ASelfPopStaysWithinTwentyCentimetresOfItsMovingBird()
        {
            var bird = new GameObject("Bird");
            var h = new PoolHarness();
            try
            {
                bird.transform.position = new Vector3(1f, 0f, 2f);
                Vector3 spawnAt = FloatingCombatText.PopSpawnPosition(bird.transform.position, 0f);
                h.Spawn(spawnAt, bird.transform);

                Vector3 anchorBefore = spawnAt;
                bird.transform.position += new Vector3(2f, 0f, 0f);            // the bird moves 2 m
                h.Tick();
                Vector3 popped = h.PopPosition(0);
                Vector3 anchorNow = FloatingCombatText.PopSpawnPosition(bird.transform.position, 0f);
                Vector2 flat = new Vector2(popped.x - anchorNow.x, popped.z - anchorNow.z);
                Assert.Less(flat.magnitude, 0.2f, "the pop is over the bird, not " + (popped - anchorBefore).magnitude + " m from its spawn point");

                Object.DestroyImmediate(bird);                                 // owner removed mid-pop
                bird = null;
                Assert.DoesNotThrow(h.Tick);
                Vector3 frozen = h.PopPosition(0);
                Assert.DoesNotThrow(h.Tick);
                Assert.AreEqual(frozen.x, h.PopPosition(0).x, 1e-4f, "freezes at the last anchor");
                Assert.AreEqual(frozen.z, h.PopPosition(0).z, 1e-4f);
            }
            finally { if (bird != null) Object.DestroyImmediate(bird); Object.DestroyImmediate(h.Go); }
        }

        [Test]
        public void AVictimPopStaysWorldFixedAndAReusedSlotDropsItsFollowTarget()
        {
            var bird = new GameObject("Bird");
            var h = new PoolHarness();
            try
            {
                Vector3 at = new Vector3(0f, 2.5f, 0f);
                h.Spawn(at, null);                                             // a victim pop: no follow target
                bird.transform.position = new Vector3(5f, 0f, 0f);
                h.Tick();
                Assert.AreEqual(at.x, h.PopPosition(0).x, 1e-4f, "world-fixed");

            }
            finally { Object.DestroyImmediate(bird); Object.DestroyImmediate(h.Go); }

            // Recycling: slot 0 follows a bird, then the pool is filled so slot 0 (the oldest) is reused for a follow-less pop.
            var bird2 = new GameObject("Bird2");
            var h2 = new PoolHarness();
            try
            {
                Vector3 at = new Vector3(0f, 2.5f, 0f);
                h2.Spawn(at, bird2.transform);
                for (int i = 0; i < FloatingCombatText.Capacity; i++) h2.Spawn(at, null);
                bird2.transform.position = new Vector3(9f, 0f, 0f);
                h2.Tick();
                for (int i = 0; i < FloatingCombatText.Capacity; i++)
                    Assert.AreEqual(at.x, h2.PopPosition(i).x, 1e-4f, "slot " + i + " did not keep a stale follow target");
            }
            finally { Object.DestroyImmediate(bird2); Object.DestroyImmediate(h2.Go); }

        }
    }
}
