#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Stamps the import settings that <c>Assets/_Game/Audio/UI/MANIFEST.md</c> asks for on every
    /// menu sound, on (re)import, so they live in code instead of hand-edited .meta files (same
    /// pattern as <see cref="UiSpriteImportSettings"/>).
    ///
    /// SFX: Decompress On Load + ADPCM, preloaded, Load In Background off. They are tens to
    /// hundreds of milliseconds long, so tap latency matters more than memory.
    /// Loops (<c>menu_loop</c>, the 32 s menu bed, and <c>match_loop*</c>, the 30 s match loop + its intense
    /// twin): music that must wrap seamlessly. <see cref="LoopFormat"/>
    /// and <see cref="LoopLoadType"/> are the result of measuring the imported clip's first and
    /// last samples (see <c>UiAudioTests</c> and STATE.md); if a codec ever adds a gap, change them here.
    /// </summary>
    public sealed class UiAudioImportSettings : AssetPostprocessor
    {
        public const string Root = "Assets/_Game/Audio/UI/";
        public const string LoopClipFile = "menu_loop.wav";
        /// <summary>The match music loops: match_loop.wav and match_loop_intense.wav (round-2 decision 4).</summary>
        public const string MatchLoopPrefix = "match_loop";

        public const AudioCompressionFormat SfxFormat = AudioCompressionFormat.ADPCM;
        public const AudioClipLoadType SfxLoadType = AudioClipLoadType.DecompressOnLoad;

        public const AudioCompressionFormat LoopFormat = AudioCompressionFormat.Vorbis;
        public const float LoopQuality = 0.7f;
        public const AudioClipLoadType LoopLoadType = AudioClipLoadType.CompressedInMemory;

        // Bump whenever the rules above change so Unity re-imports the clips.
        public override uint GetVersion() => 3;

        public static bool IsLoop(string assetPath)
        {
            string file = System.IO.Path.GetFileName(assetPath);
            return file == LoopClipFile || (file.StartsWith(MatchLoopPrefix) && file.EndsWith(".wav"));
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;

            var importer = (AudioImporter)assetImporter;
            bool loop = IsLoop(assetPath);

            var s = importer.defaultSampleSettings;
            s.loadType = loop ? LoopLoadType : SfxLoadType;
            s.compressionFormat = loop ? LoopFormat : SfxFormat;
            if (loop) s.quality = LoopQuality;
            s.preloadAudioData = true;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
            importer.loadInBackground = false;
        }
    }
}
#endif
