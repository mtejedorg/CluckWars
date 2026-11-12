using System;
using System.Collections.Generic;
using CluckWars.Audio;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A08 (architecture audit 2026-11-12): a sound that cannot play is reported once per call site or sound id,
    /// through the real <see cref="UnityAudioService"/>.
    /// </summary>
    public sealed class MissingSfxTests
    {
        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Warnings = new List<string>();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) => Warnings.Add($"[{source}] {message}");
            public void Error(string source, string message, Exception exception = null) { }
        }

        private GameObject _parent;
        private AudioRegistrySO _registry;
        private RecordingLog _log;
        private IAudioService _audio;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("MissingSfxTests");
            _registry = ScriptableObject.CreateInstance<AudioRegistrySO>();   // every slot empty
            _log = new RecordingLog();
            _audio = new UnityAudioService(_parent.transform, _log, _registry);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_parent);
            UnityEngine.Object.DestroyImmediate(_registry);
        }

        [Test]
        public void ANullClip_IsReportedOncePerCallSite_NotPerCall()
        {
            for (int i = 0; i < 5; i++) _audio.PlaySFX((AudioClip)null);   // one site, five calls
            Assert.AreEqual(1, _log.Warnings.Count, "one line for the site, however often it plays");
            StringAssert.Contains("MissingSfxTests.cs:", _log.Warnings[0], "the line names the call site");
            StringAssert.StartsWith("[Audio]", _log.Warnings[0]);

            _audio.PlaySFX((AudioClip)null);   // a second site
            Assert.AreEqual(2, _log.Warnings.Count, "a different call site is a different report");
        }

        [Test]
        public void AnIdWithNoClip_IsReportedOncePerId_AndNamesTheId()
        {
            _audio.PlaySFX(SfxId.Deposit);
            _audio.PlaySFX(SfxId.Deposit);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("Deposit", _log.Warnings[0]);

            _audio.PlaySFX(SfxId.AbilityActivate);
            Assert.AreEqual(2, _log.Warnings.Count);
            StringAssert.Contains("AbilityActivate", _log.Warnings[1]);
        }

        [Test]
        public void NoSoundId_IsSilent_AndSaysNothing()
        {
            _audio.PlaySFX(SfxId.None);
            Assert.AreEqual(0, _log.Warnings.Count);
        }

        [Test]
        public void WithoutARegistry_AnIdIsStillReported()
        {
            var bare = new UnityAudioService(_parent.transform, _log, null);
            bare.PlaySFX(SfxId.Deposit);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("no audio registry", _log.Warnings[0]);
        }

        [Test]
        public void EverySoundId_ResolvesToItsOwnRegistrySlot()
        {
            // One distinct clip in every AudioClip slot of the registry: each id must then resolve to the slot of its
            // own name, and no two ids to the same slot.
            var clips = new List<AudioClip>();
            try
            {
                foreach (var field in typeof(AudioRegistrySO).GetFields())
                {
                    if (field.FieldType != typeof(AudioClip)) continue;
                    var clip = AudioClip.Create(field.Name, 64, 1, 8000, false);
                    clips.Add(clip);
                    field.SetValue(_registry, clip);
                }

                Assert.IsNull(_registry.Resolve(SfxId.None));
                var seen = new HashSet<AudioClip>();
                foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
                {
                    if (id == SfxId.None) continue;
                    var clip = _registry.Resolve(id);
                    Assert.IsNotNull(clip, $"SfxId.{id} has no registry slot in Resolve");
                    Assert.AreEqual(id.ToString(), clip.name, $"SfxId.{id} resolves to the wrong registry slot");
                    Assert.IsTrue(seen.Add(clip), $"SfxId.{id} shares a slot with another id");
                }
            }
            finally
            {
                foreach (var clip in clips) UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void EverySoundId_WithNoClip_IsReportedOnce_ByName()
        {
            // S-3: the own-player sounds (cancel, fizzle, cargo full, expire, hit-confirm, whiff) used to pass a clip
            // through null guards that hid an empty slot. By id, the audio service reports each one, once.
            int expected = 0;
            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                if (id == SfxId.None) continue;
                _audio.PlaySFX(id);
                _audio.PlaySFX(id, 0.5f);
                expected++;
                Assert.AreEqual(expected, _log.Warnings.Count, $"SfxId.{id}: one report, not one per call");
                StringAssert.Contains($"PlaySFX({id})", _log.Warnings[expected - 1]);
            }
            Assert.GreaterOrEqual(expected, 8);
        }

        [Test]
        public void TheReporter_IsOncePerKey()
        {
            var r = new MissingSfxReporter();
            Assert.IsNotNull(r.NullClip("a.cs", 1));
            Assert.IsNull(r.NullClip("a.cs", 1));
            Assert.IsNotNull(r.NullClip("a.cs", 2));
            Assert.IsNotNull(r.NullClip("b.cs", 1));
            Assert.IsNotNull(r.MissingId(SfxId.Deposit, true));
            Assert.IsNull(r.MissingId(SfxId.Deposit, true));
        }
    }
}
