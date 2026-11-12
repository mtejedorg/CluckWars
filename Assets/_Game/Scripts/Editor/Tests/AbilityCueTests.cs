using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// S-2 / S-3 (review of audit batch 2): the tick plays no sound. The four own-player moments of ability use
    /// (cancel, fizzle, cargo full, expire) are latched by the tick, raised from <c>Render</c> as a local event and
    /// played by presentation, by <c>SfxId</c>.
    /// </summary>
    public sealed class AbilityCueTests
    {
        private static readonly AbilityCue[] Cues = (AbilityCue[])Enum.GetValues(typeof(AbilityCue));

        [Test]
        public void TheLatch_HandsEachQueuedCueOutOnce_LowestFirst()
        {
            var latch = new AbilityCueLatch();
            Assert.IsFalse(latch.Any);
            Assert.IsFalse(latch.TryTake(out _));

            latch.Queue(AbilityCue.Expired);
            latch.Queue(AbilityCue.Cancelled);
            latch.Queue(AbilityCue.Expired);        // two ticks before one Render: one cue
            Assert.IsTrue(latch.Any);

            var taken = new List<AbilityCue>();
            while (latch.TryTake(out var cue)) taken.Add(cue);
            CollectionAssert.AreEqual(new[] { AbilityCue.Cancelled, AbilityCue.Expired }, taken);
            Assert.IsFalse(latch.Any);
        }

        [Test]
        public void TheLatch_HoldsEveryCue_AndClears()
        {
            Assert.AreEqual(AbilityCueLatch.CueCount, Cues.Length, "a new AbilityCue needs a bit in the latch");
            var latch = new AbilityCueLatch();
            foreach (var cue in Cues) latch.Queue(cue);
            int n = 0;
            while (latch.TryTake(out _)) n++;
            Assert.AreEqual(Cues.Length, n);

            latch.Queue(AbilityCue.Fizzled);
            latch.Clear();
            Assert.IsFalse(latch.Any, "a despawned or pooled chicken's cue is dropped");
        }

        [Test]
        public void TheCue_ReachesItsListeners_AndSaysWhenNobodyListens()
        {
            var events = new ChickenLocalEvents();
            Assert.IsFalse(events.RaiseAbilityCue(AbilityCue.Fizzled));

            var seen = new List<AbilityCue>();
            events.AbilityCueRaised += seen.Add;
            Assert.IsTrue(events.RaiseAbilityCue(AbilityCue.Fizzled));
            Assert.IsTrue(events.RaiseAbilityCue(AbilityCue.CargoFull));
            CollectionAssert.AreEqual(new[] { AbilityCue.Fizzled, AbilityCue.CargoFull }, seen);
        }

        // ---- Source guards (the NetworkBehaviours cannot run without a runner) ---------------------------------------

        [Test]
        public void TheAbilityController_PlaysNoSound()
        {
            string code = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            StringAssert.DoesNotMatch(@"PlaySFX|IAudioService|AudioRegistrySO|AudioClip|PlayLocalSfx", code,
                "AbilityController names the moment (AbilityCue); presentation plays it");
            foreach (var cue in Cues)
                StringAssert.Contains($"_cues.Queue(AbilityCue.{cue})", code, $"nothing latches {cue} any more");

            string render = AssassinExecuteCreditTests.MethodBody(
                "Assets/_Game/Scripts/Gameplay/AbilityController.cs", "public override void Render()");
            StringAssert.Contains("RaisePendingCues()", render, "the latched cues are raised from Render, never from the tick");
            string tick = AssassinExecuteCreditTests.MethodBody(
                "Assets/_Game/Scripts/Gameplay/AbilityController.cs", "public override void FixedUpdateNetwork()");
            StringAssert.DoesNotContain("RaiseAbilityCue", tick);
            StringAssert.DoesNotContain("RaisePendingCues", tick);
        }

        [Test]
        public void HitFeedback_PlaysEveryCue_ById_ForTheOwnSoundAudience()
        {
            string code = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Visuals/HitFeedback.cs");
            foreach (var cue in Cues)
                StringAssert.IsMatch($@"case AbilityCue\.{cue}:\s*PlayOwnSfx\(SfxId\.\w+", code, $"{cue} has no sound");
            StringAssert.Contains("_events.AbilityCueRaised   += OnAbilityCue;", code);
            StringAssert.Contains("_events.AbilityCueRaised   -= OnAbilityCue;", code);
            StringAssert.DoesNotContain("AudioRegistrySO.TryPlay", code, "a clip passed through TryPlay hides an empty slot");
            StringAssert.DoesNotContain("PlayCasterSfx", code);

            string own = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Visuals/HitFeedback.cs", "private void PlayOwnSfx(");
            StringAssert.Contains("OwnSoundRules.IsAudible(_controller.HasInputAuthority, _controller.IsBot, _controller.IsDecoy)", own);
            StringAssert.Contains("_audio.PlaySFX(id, volume);", own);
        }
    }
}
