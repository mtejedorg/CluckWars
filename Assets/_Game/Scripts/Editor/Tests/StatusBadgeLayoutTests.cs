using System.Reflection;
using CluckWars.Gameplay;
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
            Assert.AreEqual(0.36f, FeedbackTuning.StatusBadgeGlyphSize, 1e-5f);
            Assert.GreaterOrEqual(FeedbackTuning.StatusBadgeRowSpacing, FeedbackTuning.StatusBadgeGlyphSize,
                "rows must not overprint each other (H7)");
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
                    .Invoke(_pool, new object[] { pos, "TEST", Color.white, follow, FloatingCombatText.WordCharacterSize });
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

        // ---- F3: stars under the pill, the sorting band, the countdown refresh -----------------------

        private static float LowestPillBottomLocal(float headLift)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            var so = new SerializedObject(prefab.GetComponent<ChickenNameplate>());
            Vector3 offset = so.FindProperty("_offset").vector3Value;
            int fontSize = so.FindProperty("_fontSize").intValue;
            float charSize = so.FindProperty("_characterSize").floatValue;
            float bottom = float.MaxValue;
            foreach (string label in new[] { "You", "DashFox", "PeckNoir" })
            {
                var go = new GameObject("PillProbe") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var tm = go.AddComponent<TextMesh>();
                    tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
                    tm.fontSize = fontSize; tm.characterSize = charSize; tm.fontStyle = FontStyle.Bold; tm.text = label;
                    var b = go.GetComponent<MeshRenderer>().localBounds;
                    float h = b.size.y > 0f ? b.size.y : 0.3f;
                    float half = ChickenNameplate.YouPillHeight(h) * 0.5f;   // the rival pill adds the same vertical padding
                    bottom = Mathf.Min(bottom, offset.y + headLift + b.center.y - half);
                }
                finally { Object.DestroyImmediate(go); }
            }
            return bottom;
        }

        [TestCase(ChickenClass.Warrior)]
        [TestCase(ChickenClass.Speedy)]
        [TestCase(ChickenClass.Fatty)]
        [TestCase(ChickenClass.Assassin)]
        public void StunStarsStayUnderThePillBottom_ForEveryClassScale(ChickenClass cls)
        {
            var registry = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsTrue(registry.TryGet(cls, out var entry), cls + " in the registry");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            float lift = prefab.GetComponent<CharacterController>().height * (entry.ResolvedModelScale.y - 1f);

            float centre = ChickenStateOverlays.StarLocalY(1f, lift, entry.ResolvedModelScale.y);
            float starTop = centre + ChickenStateOverlays.StarBaseScale * 0.5f;   // steady size; the 0.15 s pop overshoot (x1.25) briefly draws behind the pill (order 8 < 10)
            float pillBottom = LowestPillBottomLocal(lift);
            Assert.Less(starTop, pillBottom, cls + ": a star tops out under the pill bottom " + pillBottom);
            Assert.Greater(centre, 0.85f + lift, cls + ": the stars still frame the head, not the belly");
        }

        [TestCase(ChickenClass.Warrior)]
        [TestCase(ChickenClass.Speedy)]
        [TestCase(ChickenClass.Fatty)]
        [TestCase(ChickenClass.Assassin)]
        public void H5_StunStarTopStaysUnderTheCargoBar_ForEveryClassScale(ChickenClass cls)
        {
            var registry = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsTrue(registry.TryGet(cls, out var entry), cls + " in the registry");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            float scale = entry.ResolvedModelScale.y;
            float lift = prefab.GetComponent<CharacterController>().height * (scale - 1f);
            var bars = prefab.GetComponent<ChickenWorldBars>();
            var so = new SerializedObject(bars);
            float barCentre = bars.BarBaseHeight + lift;
            float barHalf = so.FindProperty("_hpHeight").floatValue * 0.72f * 0.5f;      // CargoHeight / 2, the trough drawn at the bar centre
            float barBottom = barCentre - barHalf;

            float centre = ChickenStateOverlays.StarLocalY(1f, lift, scale);
            float tiltExtent = FeedbackTuning.StunStarOrbitRadius * Mathf.Sin(FeedbackTuning.StunStarTiltDegrees * Mathf.Deg2Rad);
            float starTop = centre + ChickenStateOverlays.StarBaseScale * 0.5f + tiltExtent;

            Debug.Log($"H5 geometry {cls}: scale {scale:0.00} lift {lift:0.000} starCentre {centre:0.000} starTop {starTop:0.000} barCentre {barCentre:0.000} barBottom {barBottom:0.000} gap {barBottom - starTop:0.000}");
            Assert.Less(starTop, barBottom, cls + ": the star top stays under the cargo bar bottom");
            Assert.LessOrEqual(centre, FeedbackTuning.StunStarBeakHeight * scale + 1e-5f, cls + ": never above the scaled beak height");
            Assert.AreEqual(ChickenStateOverlays.StunStarHeightFor(scale, barCentre), centre, 1e-5f, cls + ": the runtime height is the pure helper");
        }

        [Test]
        public void H5_StarHeightRule_IsMinOfScaledBeakAndBarMinusClearance_AndBonkFollowsTheScale()
        {
            Assert.AreEqual(FeedbackTuning.StunStarBeakHeight * 0.5f, ChickenStateOverlays.StunStarHeightFor(0.5f, 9f), 1e-5f, "a short class: the scaled beak height");
            Assert.AreEqual(1f - FeedbackTuning.StunStarBarClearance, ChickenStateOverlays.StunStarHeightFor(2f, 1f), 1e-5f, "a tall class: capped under the bar");
            Assert.AreEqual(FeedbackTuning.StunBonkHeight, ChickenStateOverlays.StunBonkHeightFor(1f), 1e-5f);
            Assert.AreEqual(FeedbackTuning.StunBonkHeight * 1.3f, ChickenStateOverlays.StunBonkHeightFor(1.3f), 1e-5f);
            Assert.AreEqual(15f, FeedbackTuning.StunStarTiltDegrees);
        }

        [Test]
        public void StarHeightIsAWorldMetreConstant_DividedByTheRootScale()
        {
            float cap = ChickenWorldBars.DefaultBarHeight - FeedbackTuning.StunStarBarClearance;
            Assert.AreEqual(cap, ChickenStateOverlays.StarLocalY(1f, 0f), 1e-5f, "an unscaled class sits under the bar");
            Assert.AreEqual(ChickenStateOverlays.StunStarHeightFor(1f, ChickenWorldBars.DefaultBarHeight * 2f), ChickenStateOverlays.StarLocalY(2f, 0f) * 2f, 1e-5f, "world metres, divided by the root scale (the bar is 2x higher in world metres too)");
            Assert.AreEqual(ChickenStateOverlays.StarLocalY(1f, 0f) + 0.5f, ChickenStateOverlays.StarLocalY(1f, 0.5f, 1f + 0.5f / 1.2f), 1e-4f, "head lift is root-local (bar and stars rise together)");
            Assert.AreEqual(cap, ChickenStateOverlays.StarLocalY(0f, 0f), 1e-5f, "an unauthored zero scale is one");
            StringAssert.Contains("lossyScale.y", System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ChickenVFX.cs"), "the bonk height divides by the root scale too");
        }

        private static int MaxOrder(Transform root, string childName)
        {
            var t = root.Find(childName);
            Assert.IsNotNull(t, childName + " built");
            int max = int.MinValue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) max = Mathf.Max(max, r.sortingOrder);
            Assert.AreNotEqual(int.MinValue, max, childName + " has renderers");
            return max;
        }

        private static int MinOrder(Transform root, string childName)
        {
            var t = root.Find(childName);
            Assert.IsNotNull(t, childName + " built");
            int min = int.MaxValue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) min = Mathf.Min(min, r.sortingOrder);
            return min;
        }

        [Test]
        public void SortingBand_StarsUnderPillUnderTextUnderBadgesUnderBarsUnderPopups()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                foreach (var type in new[] { typeof(ChickenNameplate), typeof(ChickenWorldBars), typeof(ChickenStatusBadges), typeof(ChickenStateOverlays) })
                    type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(root.GetComponent(type), null);
                var plate = root.GetComponent<ChickenNameplate>();
                typeof(ChickenNameplate).GetMethod("FitMarks", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plate, new object[] { false, Color.white });

                var rt = root.transform;
                int stars = MaxOrder(rt, "StunStars");
                int rivalPill = rt.Find("Nameplate/RivalMark").GetComponent<SpriteRenderer>().sortingOrder;
                int rivalText = rt.Find("Nameplate").GetComponent<MeshRenderer>().sortingOrder;
                int dotRing = rt.Find("Nameplate/RivalDotRing").GetComponent<SpriteRenderer>().sortingOrder;
                int dot = rt.Find("Nameplate/RivalDot").GetComponent<SpriteRenderer>().sortingOrder;
                int glyph = rt.Find("Nameplate/SlowGlyph").GetComponent<SpriteRenderer>().sortingOrder;
                int badgesMin = MinOrder(rt, "StatusBadges"), badgesMax = MaxOrder(rt, "StatusBadges");
                int barsTop = MaxOrder(rt, "WorldBars");
                int popup = (int)typeof(FloatingCombatText).GetField("PopupSortingOrder", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);
                int localPill = ChickenNameplate.LocalPillOrder, localText = ChickenNameplate.LocalTextOrder;

                Assert.AreEqual(8, stars, "stars sit on the head-overlay layer");
                Assert.Less(stars, rivalPill, "stars under the pill");
                Assert.Less(rivalPill, rivalText, "pill under its text");
                Assert.Less(rivalPill, dotRing);
                Assert.Less(dotRing, dot, "dot over its ring");
                Assert.Less(rivalText, glyph, "slow glyph over the text");
                Assert.Less(rivalText, badgesMin, "badges over the pill text");
                foreach (int o in new[] { rivalPill, rivalText, dotRing, dot, glyph, badgesMin, badgesMax })
                    Assert.That(o, Is.InRange(10, 12), "the label band is 10-12");
                Assert.Less(dot, localPill, "a rival label never draws over the local pill");
                Assert.Less(localPill, localText);
                Assert.Less(localText + 1, barsTop, "the local label (glyph one above its text) stays under the cargo bars");
                Assert.Less(badgesMax, barsTop, "badges under the bars");
                Assert.AreEqual(101, barsTop, "cargo bars keep 100/101");
                Assert.Less(barsTop, popup, "bars under the floating popups");
                Assert.AreEqual(210, popup);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CountdownRefreshesOnEveryTenthChange_NotOnAGate()
        {
            const ChickenStatusBadges.Badge stun = ChickenStatusBadges.Badge.Stun;
            Assert.AreEqual(14, ChickenStatusBadges.Quantize(stun, 1.31f));
            Assert.AreEqual(13, ChickenStatusBadges.Quantize(stun, 1.30f));
            Assert.IsFalse(ChickenStatusBadges.RowNeedsWrite(false, 14, 14), "same tenth: no rebuild");
            Assert.IsTrue(ChickenStatusBadges.RowNeedsWrite(false, 14, 13), "tenth moved: rebuild this frame");
            Assert.IsTrue(ChickenStatusBadges.RowNeedsWrite(true, 14, 14), "new badge: rebuild");

            // 3 s of 60 fps frames: the shown tenth never lags the true one, and rebuilds are one per tenth (not per frame).
            int shown = int.MinValue, writes = 0;
            for (float v = 3f; v > 0f; v -= 1f / 60f)
            {
                int q = ChickenStatusBadges.Quantize(stun, v);
                if (ChickenStatusBadges.RowNeedsWrite(false, shown, q)) { shown = q; writes++; }
                Assert.AreEqual(q, shown, "the row shows the current tenth at v=" + v);
            }
            Assert.LessOrEqual(writes, 31, "at most one rebuild per displayed tenth");
            Assert.Greater(writes, 25);
            StringAssert.DoesNotContain("_nextTextUpdate", System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ChickenStatusBadges.cs"), "the 5 Hz gate is gone from the world badges");
        }

        // ---- G3 (E3a): one glyph per state, the pill snail stands in only for a cut Slow row -------------------

        /// <summary>The badge flags a slowed bird publishes, through the same pure rule LateUpdate lays out
        /// (<see cref="ChickenStatusBadges.Collect"/>, capped at the shipped row count).</summary>
        private static bool SlowRowShown(ControlVfx flags, bool stunned = false, bool shell = false, bool hiddenRow = false)
        {
            var active = new ChickenStatusBadges.Badge[FeedbackTuning.StatusBadgeMaxRows];
            int n = ChickenStatusBadges.Collect(stunned, false, ChickenStatusBadges.ShowSlowRow(flags), shell, false, hiddenRow, false, active);
            return ChickenStatusBadges.IsRowShown(active, n, ChickenStatusBadges.Badge.Slow);
        }

        [Test]
        public void PillSnail_HiddenWhileTheSlowRowShows()
        {
            const ControlVfx snare = ControlVfx.Slowed | ControlVfx.Snared;
            Assert.IsTrue(SlowRowShown(snare), "a snared bird shows the Slow row");
            Assert.IsFalse(ChickenNameplate.ShowsPillSnail(snare, SlowRowShown(snare)), "one snail: the row's, not the pill's");
        }

        [Test]
        public void PillSnail_NeverForPileOrBumpDrag()
        {
            // Pile slow and bump drag (SlowSource.Pile / Collision) raise Slowed without Snared (SlowSourceTests).
            const ControlVfx drag = ControlVfx.Slowed;
            Assert.IsFalse(ChickenNameplate.ShowsPillSnail(drag, false), "drag with no Slow row");
            Assert.IsFalse(ChickenNameplate.ShowsPillSnail(drag, true), "drag with the Slow row");
            Assert.IsFalse(ChickenNameplate.ShowsPillSnail(ControlVfx.None, false));

            string body = MethodBody(System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ChickenNameplate.cs"), "private string StateIcon()");
            StringAssert.DoesNotContain("IsPileSlow", body, "pile slow never draws the pill snail");
            StringAssert.DoesNotContain("SlowMultiplier", body, "StateAuthority-only magnitude: the pill reads the replicated flags");
            StringAssert.Contains("IsRowVisible(ChickenStatusBadges.Badge.Slow)", body, "the pill defers to the live Slow row");
        }

        [Test]
        public void PillSnail_ShownWhenTheSlowRowIsCutByTheRowCap()
        {
            const ControlVfx snare = ControlVfx.Slowed | ControlVfx.Snared;
            // Every non-root state that outranks Slow (stun, shell, hidden): root is left out because the pill shows the
            // rooted sprout instead of any snail.
            var active = new ChickenStatusBadges.Badge[FeedbackTuning.StatusBadgeMaxRows];
            int above = ChickenStatusBadges.Collect(true, false, false, true, false, true, false, active);
            Assert.AreEqual(FeedbackTuning.StatusBadgeMaxRows, above,
                "the higher-priority non-root rows fill the cap; if the cap grew past them this case needs a new way to cut Slow");

            bool shown = SlowRowShown(snare, stunned: true, shell: true, hiddenRow: true);
            Assert.IsFalse(shown, "Slow is dropped below the cap");
            Assert.IsTrue(ChickenNameplate.ShowsPillSnail(snare, shown), "the pill snail stands in for the cut row");
        }

        [Test]
        public void IsRowShown_ReadsOnlyTheLaidOutRows()
        {
            var active = new[] { ChickenStatusBadges.Badge.Stun, ChickenStatusBadges.Badge.Slow, ChickenStatusBadges.Badge.None };
            Assert.IsTrue(ChickenStatusBadges.IsRowShown(active, 2, ChickenStatusBadges.Badge.Slow));
            Assert.IsFalse(ChickenStatusBadges.IsRowShown(active, 1, ChickenStatusBadges.Badge.Slow), "a stale entry past count is not a row");
            Assert.IsFalse(ChickenStatusBadges.IsRowShown(active, 3, ChickenStatusBadges.Badge.None));
        }

        private static string MethodBody(string src, string signature)
        {
            int start = src.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, signature);
            int open = src.IndexOf('{', start), depth = 0;
            for (int k = open; k < src.Length; k++)
            {
                if (src[k] == '{') depth++;
                else if (src[k] == '}' && --depth == 0) return src.Substring(open, k - open + 1);
            }
            Assert.Fail("unbalanced " + signature);
            return string.Empty;
        }

        // ---- E5a (2026-11-08): the Slow row is ability-only, like the pill snail --------------------------

        [Test]
        public void PileDrag_RaisesSlowedWithoutSnared_AndDrawsNoSlowRow_AndNoPillSnail()
        {
            var pileDrag = ControlVfx.Slowed;
            Assert.IsFalse(ChickenStatusBadges.ShowSlowRow(pileDrag), "pile and bump drag are ambient: no row");
            Assert.IsFalse(SlowRowShown(pileDrag), "Collect must not add a Slow row for a bare Slowed bit");
            Assert.IsFalse(ChickenNameplate.ShowsPillSnail(pileDrag, slowRowShown: false), "and the pill stays bare too (E3a)");
        }

        [Test]
        public void AbilitySlow_RaisesSnared_AndDrawsTheSlowRow()
        {
            var aura = ControlVfx.Slowed | ControlVfx.Snared;
            Assert.IsTrue(ChickenStatusBadges.ShowSlowRow(aura));
            Assert.IsTrue(SlowRowShown(aura), "an aura or trap slow draws the row on every peer");
        }

        // ---- H1 (2026-11-10): pops clear the rows ON SCREEN, not in world Y -------------------------------

        /// <summary>The match camera pitches 30 degrees: the rows extend along the billboard axis (1:1 on screen) while a pop
        /// offset is world-vertical and draws only up.y per metre, so a pop level with a row's world Y sits below it on screen.</summary>
        private static readonly Vector3 BillboardUp30 = new Vector3(0f, Mathf.Cos(30f * Mathf.Deg2Rad), -Mathf.Sin(30f * Mathf.Deg2Rad));

        [Test]
        public void ScreenLevelHeight_RaisesAWorldVerticalPop_ToTheBillboardTop()
        {
            Vector3 twoRowsUpTheBillboard = BillboardUp30 * 2.0f;
            float h = ChickenStatusBadges.ScreenLevelHeight(twoRowsUpTheBillboard, BillboardUp30);
            Assert.AreEqual(2.0f / BillboardUp30.y, h, 1e-4f, "dot(from, up) / up.y");
            Assert.Greater(h, twoRowsUpTheBillboard.y, "the correction never lowers the pop below the row's world Y");
            Assert.AreEqual(1.5f, ChickenStatusBadges.ScreenLevelHeight(Vector3.up * 1.5f, Vector3.up), 1e-5f, "a top-down-free camera changes nothing");
        }

        [Test]
        public void StackClearance_CoversAWordPopHalfHeight_OnScreen()
        {
            // A 0.058 word pop reaches ~0.15 m below its MiddleCenter anchor along the billboard; the clearance is
            // world-vertical and draws up.y (0.866 at 30 degrees) per metre, so it must cover 0.15 m plus a margin.
            const float wordHalfHeightOnScreen = 0.15f;
            float drawn = FeedbackTuning.FloatingTextStackClearance * BillboardUp30.y;
            Assert.GreaterOrEqual(drawn, wordHalfHeightOnScreen + 0.02f, "the STUN word must not touch the STUN 1.5s row at onset (round-3 R01)");
        }

        [Test]
        public void WideGlyphSprite_RowWidthFollowsItsAspect_AndTheTextStartsAfterIt()
        {
            var tex = new Texture2D(256, 160);
            var wide = Sprite.Create(tex, new Rect(0, 0, 256, 160), new Vector2(0.5f, 0.5f), 100f);
            var square = Sprite.Create(tex, new Rect(0, 0, 160, 160), new Vector2(0.5f, 0.5f), 100f);
            try
            {
                Assert.AreEqual(FeedbackTuning.StatusBadgeGlyphSize, ChickenStatusBadges.GlyphWidthFor(square), 1e-5f, "a square sprite keeps the glyph size");
                Assert.AreEqual(FeedbackTuning.StatusBadgeGlyphSize * 1.6f, ChickenStatusBadges.GlyphWidthFor(wide), 1e-5f, "a wide sprite is as tall as the slot and wider by its aspect");
                Assert.AreEqual(0f, ChickenStatusBadges.GlyphWidthFor(null), 1e-6f);

                float w = ChickenStatusBadges.GlyphWidthFor(wide);
                ChickenStatusBadges.RowLayout(w, FeedbackTuning.StatusBadgeGlyphGap, 0.5f, out float gx, out float tx);
                Assert.GreaterOrEqual(tx, gx + w * 0.5f + FeedbackTuning.StatusBadgeGlyphGap - 1e-5f, "the text starts after the wide glyph and its gap");
            }
            finally { Object.DestroyImmediate(wide); Object.DestroyImmediate(square); Object.DestroyImmediate(tex); }
        }
    }

}
