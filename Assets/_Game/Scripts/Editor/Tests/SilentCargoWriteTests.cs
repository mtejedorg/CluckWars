using System;
using System.IO;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Ability Lab dummy refill must not raise the cargo pop (H8): the marker is pure and
    /// asserted for real; the Fusion-side wiring (Render consumes it before the pop, the dummy
    /// refills through it, gameplay never does) is pinned from source.
    /// </summary>
    public sealed class SilentCargoWriteTests
    {
        private const string CargoPath = "Assets/_Game/Scripts/Gameplay/ChickenCargo.cs";
        private const string DummyPath = "Assets/_Game/Scripts/AbilityLab/AbilityLabDummy.cs";

        [Test]
        public void Unarmed_change_is_announced()
        {
            var w = new SilentCargoWrite();
            Assert.IsFalse(w.ConsumeChange(), "A normal cargo change must still pop.");
        }

        [Test]
        public void Silent_write_suppresses_exactly_one_change()
        {
            var w = new SilentCargoWrite();
            w.Arm(10);
            Assert.IsTrue(w.ConsumeChange(), "The silent write itself must not pop.");
            Assert.IsFalse(w.ConsumeChange(), "The next real change must pop again.");
        }

        [Test]
        public void Marker_survives_a_render_on_the_write_frame_and_expires_on_the_next()
        {
            var w = new SilentCargoWrite();
            w.Arm(10);
            w.EndRender(10);   // Render ran before the write landed in the detector this frame
            Assert.IsTrue(w.IsArmed);
            w.EndRender(11);   // a later pass has seen it or never will: never swallow a real pop
            Assert.IsFalse(w.IsArmed);
            Assert.IsFalse(w.ConsumeChange());
        }

        [Test]
        public void Render_consumes_the_marker_before_the_pop()
        {
            string src = File.ReadAllText(CargoPath);
            int consume = src.IndexOf("_silentWrite.ConsumeChange()", StringComparison.Ordinal);
            int pop = src.IndexOf("FloatingCombatText.SpawnCargoPop(", StringComparison.Ordinal);
            int loss = src.IndexOf("_hitFeedback?.NotifyCargoLoss(", StringComparison.Ordinal);
            Assert.Greater(consume, 0, "ChickenCargo.Render no longer consults the silent-write marker.");
            Assert.Less(consume, pop, "The marker must be checked before the cargo pop.");
            Assert.Less(consume, loss, "The marker must be checked before the cargo-loss beat.");
            StringAssert.Contains("_silentWrite.EndRender(", src);
        }

        [Test]
        public void Lab_dummy_refills_silently_and_only_the_lab_uses_the_silent_path()
        {
            StringAssert.Contains("SetCargoWithoutPop(", File.ReadAllText(DummyPath),
                "AbilityLabDummy.Restock must refill without the pop.");
            foreach (var file in Directory.GetFiles("Assets/_Game/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                string p = file.Replace('\\', '/');
                if (p.Contains("/AbilityLab/") || p.Contains("/Editor/") || p == CargoPath) continue;
                StringAssert.DoesNotContain("SetCargoWithoutPop(", File.ReadAllText(file),
                    p + " uses the lab-only silent cargo write; gameplay changes must pop.");
            }
        }
    }
}
