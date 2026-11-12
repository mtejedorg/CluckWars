using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using Edge = CluckWars.Visuals.ChickenShellVisuals.Edge;
using Phase = CluckWars.Visuals.ChickenShellVisuals.Phase;

namespace CluckWars.Tests
{
    /// <summary>
    /// Batch E1: the six ability-feedback clip slots on <see cref="AudioRegistrySO"/>, the null-safe
    /// <see cref="AudioRegistrySO.TryPlay"/> helper, and cheap checks that each hook site still reads the replicated trigger it
    /// mirrors. No Play mode: the hooks are local presentation observed off existing state.
    /// </summary>
    public sealed class AudioRegistryTests
    {
        private static readonly string[] FeedbackClips =
            { "HitConfirm", "CastWhiff", "DeniedClick", "ShellClose", "ShellOpen", "HuntLive" };

        private sealed class CountingAudio : IAudioService
        {
            public readonly List<(AudioClip Clip, float Volume, float Pitch)> Sfx = new();
            public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f, float delaySeconds = 0f,
                                string callerFile = "", int callerLine = 0) => Sfx.Add((clip, volume, pitch));
            public void PlaySFX(SfxId id, float volume = 1f) { }
            public void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true, float fadeInSeconds = 0f) { }
            public void FadeOutMusic(float seconds) { }
            public void SetMusicLevel(float level01, float seconds) { }
            public void CrossfadeMusic(AudioClip clip, float seconds) { }
            public void StopMusic() { }
            public bool IsMusicPlaying(AudioClip clip) => false;
            public void SetMasterVolume(float volume01) { }
            public void SetMusicVolume(float volume01) { }
            public void SetSfxVolume(float volume01) { }
        }

        private static string Src(string relative) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), relative));

        // ---- Fields --------------------------------------------------------------------------------------------------

        [Test]
        public void TheSixFeedbackClips_ArePublicAudioClipFields()
        {
            foreach (var name in FeedbackClips)
            {
                var f = typeof(AudioRegistrySO).GetField(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(f, $"AudioRegistrySO.{name} is missing");
                Assert.AreEqual(typeof(AudioClip), f.FieldType, $"AudioRegistrySO.{name} must be an AudioClip");
            }
        }

        [Test]
        public void TheShippedRegistry_Loads_AndItsFeedbackSlotsAreNullOrClips()
        {
            var reg = TestAssets.Load<AudioRegistrySO>(TestAssets.AudioRegistryPath);
            foreach (var name in FeedbackClips)
            {
                object v = typeof(AudioRegistrySO).GetField(name).GetValue(reg);
                Assert.IsTrue(v == null || v is AudioClip, $"{name} holds a {v?.GetType().Name}");
            }
        }

        // ---- Helper --------------------------------------------------------------------------------------------------

        [Test]
        public void TryPlay_IsFalse_AndNeverThrows_WhenAnythingIsMissing()
        {
            var audio = new CountingAudio();
            var clip = AudioClip.Create("e1_test", 64, 1, 44100, false);
            try
            {
                Assert.DoesNotThrow(() => Assert.IsFalse(AudioRegistrySO.TryPlay(null, clip)));
                Assert.DoesNotThrow(() => Assert.IsFalse(AudioRegistrySO.TryPlay(audio, null)));
                Assert.DoesNotThrow(() => Assert.IsFalse(AudioRegistrySO.TryPlay(null, null)));
                Assert.IsEmpty(audio.Sfx);
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Test]
        public void TryPlay_PlaysTheClipExactlyOnce_WithTheGivenVolumeAndPitch()
        {
            var audio = new CountingAudio();
            var clip = AudioClip.Create("e1_test", 64, 1, 44100, false);
            try
            {
                Assert.IsTrue(AudioRegistrySO.TryPlay(audio, clip, 0.6f, 1.2f));
                Assert.AreEqual(1, audio.Sfx.Count);
                Assert.AreSame(clip, audio.Sfx[0].Clip);
                Assert.AreEqual(0.6f, audio.Sfx[0].Volume);
                Assert.AreEqual(1.2f, audio.Sfx[0].Pitch);
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // ---- Hook sites: the replicated triggers they mirror ---------------------------------------------------------

        [Test]
        public void ShellHooks_FireOnTheCloseAndOpenEdgesOnly()
        {
            Assert.AreEqual(Edge.Close, ChickenShellVisuals.EdgeOf(Phase.Walk, Phase.Egg));
            Assert.AreEqual(Edge.Close, ChickenShellVisuals.EdgeOf(Phase.None, Phase.Egg));
            Assert.AreEqual(Edge.Open, ChickenShellVisuals.EdgeOf(Phase.Egg, Phase.None));
            Assert.AreEqual(Edge.None, ChickenShellVisuals.EdgeOf(Phase.Egg, Phase.Egg), "a held egg replays nothing");

            string src = Src("Assets/_Game/Scripts/Visuals/ChickenShellVisuals.cs");
            StringAssert.Contains("PlayEdgeSfx(open: false)", src);
            StringAssert.Contains("PlayEdgeSfx(open: true)", src);
            StringAssert.Contains("_haptics?.Buzz(HapticKind.CancelArm)", src);
        }

        [Test]
        public void HuntLive_IsTheAssassinsKillLiveEdge_NeverTheVictims()
        {
            Assert.IsTrue(MarkFeedback.ShowsHuntPop(new MarkOutputs { Role = MarkRole.Assassin, Events = MarkEvents.KillLive }));
            Assert.IsFalse(MarkFeedback.ShowsHuntPop(new MarkOutputs { Role = MarkRole.Victim, Events = MarkEvents.KillLive }));
            StringAssert.Contains("AudioRegistrySO.TryPlay(_audio, _sfx.HuntLive", Src("Assets/_Game/Scripts/Visuals/MarkFeedback.cs"));
        }

        [Test]
        public void CastAndDeniedHooks_ReadTheReplicatedCastCountAndTheOneShotDeniedFlag()
        {
            var hits = typeof(AbilityController).GetProperty(nameof(AbilityController.LastCastHitCount));
            Assert.IsNotNull(hits);
            Assert.IsNotNull(hits.GetCustomAttribute<Fusion.NetworkedAttribute>(), "LastCastHitCount is the replicated verdict the hook reads");

            var denied = typeof(AbilityController).GetMethod(nameof(AbilityController.TryConsumeDeniedPress));
            Assert.IsNotNull(denied);
            Assert.AreEqual(typeof(bool), denied.ReturnType);

            string hit = Src("Assets/_Game/Scripts/Visuals/HitFeedback.cs");
            StringAssert.Contains("PlayCasterSfx(_sfx != null ? _sfx.HitConfirm : null, 1f)", hit);
            StringAssert.Contains("PlayCasterSfx(_sfx != null ? _sfx.CastWhiff : null, FeedbackTuning.WhiffSfxVolumeMultiplier)", hit);
            StringAssert.Contains("AudioRegistrySO.TryPlay(_audio, _sfx.DeniedClick)", Src("Assets/_Game/Scripts/Input/TouchControlsController.cs"));
        }
    }
}
