using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.Audio;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.Installers;
using CluckWars.Logging;
using CluckWars.UI;

namespace CluckWars.Tests
{
    /// <summary>
    /// Menu audio (menu overhaul Phase 3C): the shipped catalogue points at the right files, the
    /// import settings follow Audio/UI/MANIFEST.md, the menu loop wraps without padding, the
    /// facade behaves (pitch jitter, delayed cluck, one warning per missing cue, no double-start),
    /// and no UI script reaches for an AudioSource.
    /// </summary>
    public sealed class UiAudioTests
    {
        private const string UiAudioPath = TestAssets.DataRoot + "/UiAudio.asset";
        private const string AudioDir = "Assets/_Game/Audio/UI";
        private const string ProjectContextPrefab = "Assets/_Game/Resources/ProjectContext.prefab";

        private static IEnumerable<System.Reflection.FieldInfo> CueFields() =>
            typeof(UiAudioSO).GetFields().Where(f => f.FieldType == typeof(UiCue));

        private static string SnakeCase(string pascal) =>
            Regex.Replace(pascal, "(?<=[a-z])(?=[A-Z])", "_").ToLowerInvariant();

        // ---- Catalogue -------------------------------------------------------------

        [Test]
        public void ShippedCatalogue_EveryCueHasAClipFromTheMatchingFile()
        {
            var so = TestAssets.Load<UiAudioSO>(UiAudioPath);
            var problems = new List<string>();
            foreach (var f in CueFields())
            {
                var cue = (UiCue)f.GetValue(so);
                string path = cue.Clip != null ? AssetDatabase.GetAssetPath(cue.Clip) : null;
                if (path == null) { problems.Add($"{f.Name}: no clip assigned"); continue; }

                string file = Path.GetFileNameWithoutExtension(path);
                string snake = SnakeCase(f.Name);
                if (!path.StartsWith(AudioDir + "/") || (file != snake && file != "ui_" + snake))
                    problems.Add($"{f.Name}: points at {path} (expected {snake}.wav or ui_{snake}.wav in {AudioDir})");
                if (cue.Volume <= 0f || cue.Volume > 1f) problems.Add($"{f.Name}: volume {cue.Volume} outside (0,1]");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void ShippedCatalogue_UsesEveryWavInTheAudioFolder()
        {
            var so = TestAssets.Load<UiAudioSO>(UiAudioPath);
            var used = CueFields().Select(f => AssetDatabase.GetAssetPath(((UiCue)f.GetValue(so)).Clip)).ToHashSet();
            var orphans = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => !used.Contains(p)).ToList();
            Assert.That(orphans, Is.Empty, "WAVs in Audio/UI that no UiAudioSO cue plays: " + string.Join(", ", orphans));
        }

        [Test]
        public void EveryChickenClass_HasACluck()
        {
            var so = TestAssets.Load<UiAudioSO>(UiAudioPath);
            foreach (ChickenClass cls in Enum.GetValues(typeof(ChickenClass)))
                Assert.That(so.CluckFor(cls).Clip, Is.Not.Null, $"{cls} has no cluck");
        }

        [Test]
        public void ProjectContext_BindsTheShippedCatalogue()
        {
            var root = PrefabUtility.LoadPrefabContents(ProjectContextPrefab);
            try
            {
                var installer = root.GetComponentInChildren<ProjectInstaller>(true);
                Assert.That(installer, Is.Not.Null);
                var slot = new SerializedObject(installer).FindProperty("_uiAudio");
                Assert.That(slot, Is.Not.Null, "ProjectInstaller lost its _uiAudio slot");
                Assert.That(AssetDatabase.GetAssetPath(slot.objectReferenceValue), Is.EqualTo(UiAudioPath));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---- Import settings (MANIFEST.md "Unity import notes") --------------------

        private static IEnumerable<string> WavPaths() =>
            Directory.GetFiles(AudioDir, "*.wav").Select(p => p.Replace('\\', '/'));

        [Test]
        public void Sfx_DecompressOnLoad_PcmOrAdpcm_NotLoadedInBackground()
        {
            var problems = new List<string>();
            foreach (var p in WavPaths().Where(p => !UiAudioImportSettings.IsLoop(p)))
            {
                var imp = (AudioImporter)AssetImporter.GetAtPath(p);
                var s = imp.defaultSampleSettings;
                if (s.loadType != AudioClipLoadType.DecompressOnLoad) problems.Add($"{p}: loadType {s.loadType}");
                if (s.compressionFormat != AudioCompressionFormat.ADPCM && s.compressionFormat != AudioCompressionFormat.PCM)
                    problems.Add($"{p}: format {s.compressionFormat}");
                if (imp.loadInBackground) problems.Add($"{p}: Load In Background is on");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void MenuLoop_ImportKeepsEverySampleSoItWrapsAtTheSeam()
        {
            string path = AudioDir + "/" + UiAudioImportSettings.LoopClipFile;
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            Assert.That(imp.loadInBackground, Is.False);
            Assert.That(imp.defaultSampleSettings.loadType, Is.Not.EqualTo(AudioClipLoadType.DecompressOnLoad),
                "a 32 s stereo loop decompressed on load is ~6 MB of resident PCM");

            // A codec that pads or trims the ends (priming silence) changes the sample count; the
            // WAV source is the loop-exact reference. (The measured seam numbers are in STATE.md.)
            long wavFrames = WavFrameCount(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            Assert.That(clip.samples, Is.EqualTo(wavFrames), "the imported loop no longer has the source's exact length");
        }

        private static long WavFrameCount(string path)
        {
            using var r = new BinaryReader(File.OpenRead(path));
            r.ReadBytes(12);                                  // RIFF, size, WAVE
            int channels = 0, bits = 0;
            while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
            {
                string id = new string(r.ReadChars(4));
                int size = r.ReadInt32();
                if (id == "fmt ")
                {
                    r.ReadInt16(); channels = r.ReadInt16(); r.ReadInt32(); r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16();
                    r.ReadBytes(size - 16);
                }
                else if (id == "data") return size / (channels * bits / 8);
                else r.ReadBytes(size + (size & 1));
            }
            throw new InvalidDataException(path + " has no data chunk");
        }

        // ---- Services --------------------------------------------------------------

        [Test]
        public void NullAudioService_AcceptsTheWholeInterface()
        {
            IAudioService a = new NullAudioService();
            Assert.DoesNotThrow(() =>
            {
                a.PlaySFX(null, 1f, 1.1f, 0.05f);
                a.PlayMusic(null, 1f, true, 0.3f);
                a.FadeOutMusic(0.4f);
                a.StopMusic();
                a.SetMasterVolume(0.5f);
            });
            Assert.That(a.IsMusicPlaying(null), Is.False);
        }

        // ---- MenuAudio facade ------------------------------------------------------

        private sealed class FakeAudio : IAudioService
        {
            public readonly List<(AudioClip Clip, float Volume, float Pitch, float Delay)> Sfx = new();
            public readonly List<(AudioClip Clip, float Volume, bool Loop, float FadeIn)> Music = new();
            public readonly List<float> FadeOuts = new();
            public AudioClip Playing;

            public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f, float delaySeconds = 0f) => Sfx.Add((clip, volume, pitch, delaySeconds));
            public void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true, float fadeInSeconds = 0f) { Music.Add((clip, volume, loop, fadeInSeconds)); Playing = clip; }
            public void FadeOutMusic(float seconds) => FadeOuts.Add(seconds);
            public void StopMusic() => Playing = null;
            public bool IsMusicPlaying(AudioClip clip) => clip != null && Playing == clip;
            public void SetMasterVolume(float volume01) { }
        }

        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Warnings = new();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) => Warnings.Add(message);
            public void Error(string source, string message, Exception exception = null) { }
        }

        private readonly List<UnityEngine.Object> _made = new();

        [TearDown] public void TearDown() { foreach (var o in _made) if (o != null) UnityEngine.Object.DestroyImmediate(o); _made.Clear(); }

        private AudioClip Clip(string name)
        {
            var c = AudioClip.Create(name, 64, 1, 8000, false);
            _made.Add(c);
            return c;
        }

        /// <summary>A catalogue with every cue filled with a distinct clip, optionally leaving some out.</summary>
        private UiAudioSO Catalogue(params string[] leaveOut)
        {
            var so = ScriptableObject.CreateInstance<UiAudioSO>();
            _made.Add(so);
            foreach (var f in CueFields().Where(f => !leaveOut.Contains(f.Name)))
            {
                var cue = (UiCue)f.GetValue(so);
                cue.Clip = Clip(f.Name);
                f.SetValue(so, cue);
            }
            return so;
        }

        [Test]
        public void EquipTap_IsTheTapClipAtHalfItsVolume()
        {
            var fake = new FakeAudio(); var so = Catalogue();
            var menu = new MenuAudio(fake, so, new RecordingLog());
            menu.EquipTap();

            Assert.AreEqual(1, fake.Sfx.Count);
            Assert.AreEqual(so.Tap.Clip, fake.Sfx[0].Clip);
            Assert.AreEqual(so.Tap.Volume * MenuAudio.EquipTapVolume, fake.Sfx[0].Volume, 1e-6f);
            Assert.AreEqual(0.5f, MenuAudio.EquipTapVolume);
        }

        [Test]
        public void Tap_PlaysTheTapClipWithinFivePercentPitchJitter()
        {
            var fake = new FakeAudio(); var so = Catalogue();
            var menu = new MenuAudio(fake, so, new RecordingLog());
            for (int i = 0; i < 50; i++) menu.Tap();

            Assert.That(fake.Sfx.All(s => s.Clip == so.Tap.Clip && s.Volume == so.Tap.Volume), Is.True);
            Assert.That(fake.Sfx.All(s => s.Pitch >= 0.95f && s.Pitch <= 1.05f), Is.True);
            Assert.That(fake.Sfx.Select(s => s.Pitch).Distinct().Count(), Is.GreaterThan(1), "taps should not all share one pitch");
        }

        [Test]
        public void SelectClass_PlaysSelectThenTheClassCluckSlightlyLater()
        {
            var fake = new FakeAudio(); var so = Catalogue();
            new MenuAudio(fake, so, new RecordingLog()).SelectClass(ChickenClass.Fatty);

            Assert.That(fake.Sfx.Count, Is.EqualTo(2));
            Assert.That(fake.Sfx[0].Clip, Is.EqualTo(so.Select.Clip));
            Assert.That(fake.Sfx[0].Delay, Is.EqualTo(0f));
            Assert.That(fake.Sfx[1].Clip, Is.EqualTo(so.CluckFatty.Clip));
            Assert.That(fake.Sfx[1].Delay, Is.InRange(0.01f, 0.2f));
        }

        [Test]
        public void CountdownTick_PitchRisesAsItCountsDown()
        {
            var fake = new FakeAudio();
            var menu = new MenuAudio(fake, Catalogue(), new RecordingLog());
            menu.CountdownTick(3); menu.CountdownTick(2); menu.CountdownTick(1);
            Assert.That(fake.Sfx[0].Pitch, Is.LessThan(fake.Sfx[1].Pitch));
            Assert.That(fake.Sfx[1].Pitch, Is.LessThan(fake.Sfx[2].Pitch));
        }

        [Test]
        public void MissingClip_WarnsOncePerCueAndPlaysNothing()
        {
            var fake = new FakeAudio(); var log = new RecordingLog();
            var menu = new MenuAudio(fake, Catalogue(nameof(UiAudioSO.EquipThunk)), log);
            menu.Equip(); menu.Equip(); menu.Equip();

            Assert.That(fake.Sfx, Is.Empty);
            Assert.That(log.Warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void MissingCatalogue_WarnsOnceNotPerCue()
        {
            var fake = new FakeAudio(); var log = new RecordingLog();
            var menu = new MenuAudio(fake, null, log);
            menu.Tap(); menu.Back(); menu.Equip(); menu.StartMenuMusic();

            Assert.That(fake.Sfx, Is.Empty);
            Assert.That(fake.Music, Is.Empty);
            Assert.That(log.Warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Silent_NeverThrowsAndNeverLogs()
        {
            var menu = MenuAudio.Silent();
            Assert.DoesNotThrow(() =>
            {
                menu.Tap(); menu.Back(); menu.SelectClass(ChickenClass.Warrior); menu.SelectPerk(); menu.ArmSlot();
                menu.Equip(); menu.Clear(); menu.Ready(); menu.CountdownTick(3); menu.CountdownGo(); menu.MatchSting();
                menu.StartMenuMusic(); menu.StopMenuMusicForMatch();
            });
        }

        [Test]
        public void MenuMusic_StartsOnceWithAFadeInThenFadesOutForTheMatch()
        {
            var fake = new FakeAudio(); var so = Catalogue();
            var menu = new MenuAudio(fake, so, new RecordingLog());
            menu.StartMenuMusic();
            menu.StartMenuMusic();   // a page change or a return from a match must not restart the loop

            Assert.That(fake.Music.Count, Is.EqualTo(1));
            Assert.That(fake.Music[0].Clip, Is.EqualTo(so.MenuLoop.Clip));
            Assert.That(fake.Music[0].Loop, Is.True);
            Assert.That(fake.Music[0].FadeIn, Is.GreaterThan(0f));

            menu.StopMenuMusicForMatch();
            Assert.That(fake.FadeOuts.Count, Is.EqualTo(1));
            Assert.That(fake.FadeOuts[0], Is.GreaterThan(0f));
        }

        // ---- Boundary --------------------------------------------------------------

        [Test]
        public void UiScripts_NeverTouchAnAudioSource()
        {
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles("Assets/_Game/Scripts/UI", "*.cs", SearchOption.AllDirectories))
            {
                string code = Regex.Replace(File.ReadAllText(file), @"//[^\r\n]*", "");
                code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
                if (code.Contains("AudioSource")) offenders.Add(file);
            }
            Assert.That(offenders, Is.Empty, "UI code plays sound through MenuAudio / IAudioService only: " + string.Join(", ", offenders));
        }
    }
}
