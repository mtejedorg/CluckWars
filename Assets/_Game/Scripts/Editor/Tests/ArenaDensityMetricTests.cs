using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// The GUARDRAIL for arena density: free-run length, measured the same way for every
    /// configuration, with the 38 m arena as the reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 2026-08-19 stacked two density changes that were each measured sufficient on their
    /// own — reverting the mis-scaled <see cref="PinwheelLayout.HubPlazaRadius"/> /
    /// <see cref="PinwheelLayout.OpeningWidth"/> (longer arms) and reviving the scatter.
    /// <b>Overshoot is the real risk</b>, and the failure mode is silent: a map that is too
    /// closed reads as "cluttered" long after the change that closed it is forgotten.
    /// </para>
    /// <para>
    /// <b>The 38 m arena is the reference, not a floor to beat.</b> Landing materially TIGHTER
    /// than it is a failure, not a win. When this test fails, the lever is
    /// <c>_coverPerSector</c> / <c>_barrierPerSector</c> in <c>Game.unity</c> — NOT the
    /// pinwheel constants, which are a correctness fix with a derivation behind them.
    /// </para>
    /// <para>
    /// Absolute numbers here will not match <c>level-designer</c>'s 2026-08-19 figures to the
    /// decimal — sampling density and step size are this implementation's own. What is
    /// reproducible, and what the assertions use, is the RATIO between configurations
    /// measured identically.
    /// </para>
    /// </remarks>
    public sealed class ArenaDensityMetricTests
    {
        /// <summary>The pre-rescale arena, in its own units. The reference row.</summary>
        private const float ReferenceHalfSize = 19f;

        // The constants the 2026-08-14 rescale wrongly scaled x1.35, kept here ONLY to
        // reconstruct the "broken today" row for comparison. They are not live anywhere.
        private const float BrokenHubPlaza = 13.5f;
        private const float BrokenOpening = 4.05f;

        /// <summary>
        /// Radial arms from first principles: the same topology <see cref="PinwheelLayout.Build"/>
        /// produces, but with the hub plaza and opening width passed in, so historical
        /// configurations can be reconstructed without making live constants mutable.
        /// Trustworthy because <see cref="Reconstruction_AgreesWithPinwheelLayout"/> pins it
        /// against the real builder at the shipped constants.
        /// </summary>
        private static WallSegment[] RadialArms(float half, int wedges, float hubPlaza, float opening)
        {
            var segs = new WallSegment[wedges];
            float step = 2f * Mathf.PI / wedges;
            for (int i = 0; i < wedges; i++)
            {
                float angle = (i + 0.5f) * step;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float boundary = half / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));

                float start = i % 2 == 0 ? hubPlaza : hubPlaza + opening;
                float end = i % 2 == 0 ? boundary - opening : boundary;

                segs[i] = new WallSegment(dir * start, dir * end, ObstacleClass.Standard);
            }
            return segs;
        }

        [Test]
        public void Reconstruction_AgreesWithPinwheelLayout()
        {
            float half = ShippedMap.HalfSize;
            float thickness = ShippedMap.WallThickness;
            float hubPlaza = PinwheelLayout.SolveHubPlazaRadius(ShippedMap.CentreKeepClear, thickness, 8);

            var real = PinwheelLayout.Build(half, 8, seed: 1, ShippedMap.CentreKeepClear, thickness);
            var rebuilt = RadialArms(half, 8, hubPlaza, PinwheelLayout.OpeningWidth);

            for (int i = 0; i < real.Length; i++)
            {
                Assert.AreEqual(real[i].A.x, rebuilt[i].A.x, 0.0005f, $"arm {i} start x");
                Assert.AreEqual(real[i].A.y, rebuilt[i].A.y, 0.0005f, $"arm {i} start z");
                Assert.AreEqual(real[i].B.x, rebuilt[i].B.x, 0.0005f, $"arm {i} end x");
                Assert.AreEqual(real[i].B.y, rebuilt[i].B.y, 0.0005f, $"arm {i} end z");
            }
        }

        private static ArenaDensityMetric.Reading MeasureArmsOnly(float half, float hubPlaza, float opening, float thickness)
        {
            var arms = RadialArms(half, 8, hubPlaza, opening);
            return ArenaDensityMetric.Measure(half, ArenaDensityMetric.CollectTerrain(arms, thickness, null));
        }

        /// <summary>Every row of the guardrail table, measured identically. Logged whatever the verdict.</summary>
        private static (string label, ArenaDensityMetric.Reading reading)[] MeasureTable()
        {
            float half = ShippedMap.HalfSize;
            float thickness = ShippedMap.WallThickness;
            float shippedHubPlaza = PinwheelLayout.SolveHubPlazaRadius(ShippedMap.CentreKeepClear, thickness, 8);

            var arms = ShippedMap.Arms(seed: 7);
            var scatter = ShippedMap.Scatter(seed: 7, arms);

            return new[]
            {
                ("38 m arena          (THE REFERENCE)",
                    MeasureArmsOnly(ReferenceHalfSize, PinwheelLayout.HubPlazaRadius, PinwheelLayout.OpeningWidth, 0.5f)),
                ("51.3 m, mis-scaled  (the bug)",
                    MeasureArmsOnly(half, BrokenHubPlaza, BrokenOpening, 0.5f)),
                ("51.3 m, Part A only (arms fixed)",
                    MeasureArmsOnly(half, shippedHubPlaza, PinwheelLayout.OpeningWidth, thickness)),
                ("51.3 m, Part A + B  (SHIPPED)",
                    ArenaDensityMetric.Measure(half, ArenaDensityMetric.CollectTerrain(arms, thickness, scatter.Boxes))),
            };
        }

        /// <summary>
        /// Not an assertion — a report. Runs on every suite execution so the table is in the
        /// log next to whatever else changed, rather than living in a doc that goes stale.
        /// </summary>
        [Test]
        public void FreeRunLength_ReportsTheGuardrailTable()
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- ARENA FREE-RUN LENGTH (terrain only, piles drained) ---");
            foreach (var (label, reading) in MeasureTable())
                sb.AppendLine($"{label,-38} {reading}");
            sb.AppendLine("-----------------------------------------------------------");
            Debug.Log(sb.ToString());
            Assert.Pass();
        }

        /// <summary>
        /// The shipped arena must land in a band AROUND the 38 m reference. Both directions
        /// are failures: too loose means the walls still do not bite, too tight means the
        /// two stacked changes overshot and the scatter counts must come down.
        /// </summary>
        [Test]
        public void FreeRunLength_ShippedArenaTracksThe38mReference()
        {
            var rows = MeasureTable();
            var reference = rows[0].reading;
            var shipped = rows[3].reading;

            // The shipped configuration measures 0.97x the reference (7-seed mean 12.37 vs
            // 12.78). The band is deliberately tight on the low side: one extra obstacle per
            // sector measured 0.84x, which is the overshoot this guardrail exists to catch,
            // and it must trip. The mis-scaled arena sits at 1.32x, so the high side catches
            // the original bug.
            float ratio = shipped.Mean / reference.Mean;
            Assert.That(ratio, Is.InRange(0.88f, 1.10f),
                $"shipped mean free run {shipped.Mean:0.00} m is {ratio:0.00}x the 38 m reference " +
                $"{reference.Mean:0.00} m.\n" +
                (ratio > 1f
                    ? "TOO OPEN: the arena still runs longer straight lines than the reference."
                    : "TOO CLOSED: the density changes overshot. Lower _coverPerSector / " +
                      "_barrierPerSector in Game.unity — do NOT touch PinwheelLayout.HubPlazaRadius " +
                      "or OpeningWidth, which are a correctness fix with a derivation behind them."));

            Assert.LessOrEqual(shipped.LongRunFraction, reference.LongRunFraction * 1.5f,
                $"{shipped.LongRunFraction * 100f:0.0}% of runs still exceed " +
                $"{ArenaDensityMetric.LongRunThreshold:0} m, against {reference.LongRunFraction * 100f:0.0}% " +
                "on the 38 m reference — long straight lines are the thing the walls exist to break.");
        }

        /// <summary>
        /// The mis-scaled arena must measure MEASURABLY worse than the fix, or the whole
        /// premise of the change is unfounded and this metric is not detecting what it claims.
        /// </summary>
        [Test]
        public void FreeRunLength_ConfirmsTheMisScaledArenaWasTheProblem()
        {
            var rows = MeasureTable();
            Assert.Greater(rows[1].reading.Mean, rows[2].reading.Mean + 2f,
                "the mis-scaled 51.3 m arena should run substantially longer straight lines " +
                "than the Part A fix — if it does not, the free-run metric is not measuring " +
                "the property the complaint was about.");
        }

        [Test]
        public void RunLength_StopsAtTheFirstBlockingBox()
        {
            var terrain = new List<ArenaDensityMetric.Box>
            {
                new ArenaDensityMetric.Box(new Vector2(10f, 0f), 1f, 20f, 0f),
            };

            float run = ArenaDensityMetric.RunLength(Vector2.zero, Vector2.right, 25f, terrain);
            Assert.AreEqual(9.5f, run, ArenaDensityMetric.MarchStep,
                "a 1 m thick wall centred at x=10 should stop the run just before x=9.5");
        }

        [Test]
        public void RunLength_StopsAtTheArenaBoundaryWhenNothingBlocks()
        {
            var empty = new List<ArenaDensityMetric.Box>();
            float run = ArenaDensityMetric.RunLength(Vector2.zero, Vector2.right, 25f, empty);
            Assert.AreEqual(25f, run, ArenaDensityMetric.MarchStep);
        }
    }
}
