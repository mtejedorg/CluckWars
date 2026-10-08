#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Stamps the correct sprite import settings on every design-exported UI PNG
    /// under <c>Assets/_Game/Art/UI/</c> automatically on (re)import — so the
    /// design→Unity asset pipeline (<c>tools/export-design-assets.ps1</c>) never
    /// has to hand-author fragile <c>.meta</c> YAML (that approach corrupted three
    /// different ways on 2026-07-12; see docs/CONVENTIONS.md § UI asset pipeline).
    ///
    /// Re-export a PNG and Unity re-imports it through this postprocessor, so the
    /// 9-slice border, sprite type, and compression are always reapplied — the
    /// generated <c>.meta</c> stays valid and its GUID stable.
    ///
    /// • Atoms / Backgrounds: <c>Uncompressed</c> (RGBA32) — smooth gradients that
    ///   band badly under block compression.
    /// • Icons / Chickens: default <c>Compressed</c> — flat art tolerates it.
    /// • Fx: max 128, <c>Uncompressed</c> (soft alpha gradients; they are 64-128 px).
    /// • Icons/Abilities: max 256 on every platform. Backgrounds/Bg_*: max 4096,
    ///   compressed, ASTC 6x6 on Android. Frames: CompressedHQ, MANIFEST 9-slice borders;
    ///   Tex_WoodTile wraps Repeat (Art/UI/Frames/MANIFEST.md).
    /// • 9-slice borders (left, bottom, right, top, in final-PNG pixels) are keyed
    ///   per atom so frames stretch without distorting their beveled corners.
    /// </summary>
    public sealed class UiSpriteImportSettings : AssetPostprocessor
    {
        const string Root = "Assets/_Game/Art/UI/";

        // Path (relative to Root, no extension) → 9-slice border L,B,R,T.
        // Anything under Root not listed here imports borderless. Anything under
        // Atoms/ or Backgrounds/ imports uncompressed; everything else compressed.
        static readonly Dictionary<string, Vector4> Borders = new()
        {
            { "Atoms/PanelFrame",      new Vector4(96, 96, 96, 96) },
            { "Atoms/CardBg",          new Vector4(56, 56, 56, 56) },
            { "Atoms/CardGlowFrame",   new Vector4(72, 72, 72, 72) },
            { "Atoms/Ribbon",          new Vector4(92, 32, 92, 32) },
            { "Atoms/ButtonGrayscale", new Vector4(52, 52, 52, 52) },
            { "Atoms/BarTrough",       new Vector4(20, 20, 20, 20) },
            { "Atoms/BarFill",         new Vector4(12, 12, 12, 12) },
            { "Atoms/GlossOverlay",    new Vector4(28, 28, 28, 28) },
            { "Atoms/CodeTile",        new Vector4(40, 40, 40, 40) },
            // Menu overhaul Phase 2 frames (Art/UI/Frames/MANIFEST.md lists them as L/T/R/B;
            // here they are L,B,R,T like every row above).
            { "Frames/Frame_WoodPanel",     new Vector4(40, 40, 40, 40) },
            { "Frames/Frame_CtaPlank",      new Vector4(48, 40, 48, 36) },
            { "Frames/Frame_Ribbon",        new Vector4(168, 0, 168, 0) },
            { "Frames/Card_Cream",          new Vector4(40, 44, 40, 40) },
            { "Frames/Card_CreamSelected",  new Vector4(40, 44, 40, 40) },
        };

        /// <summary>Ability / perk icons (one per AbilityIconStyle entry): 256 px sources, capped on every platform.</summary>
        public const string AbilityIconPrefix = "Icons/Abilities/";
        public const int AbilityIconMaxSize = 256;

        /// <summary>Per-screen backdrops (2880x1440). Compressed (ASTC 6x6 on Android), not RGBA32: 16 MB each otherwise.</summary>
        public const string BackdropPrefix = "Backgrounds/Bg_";
        public const int BackdropMaxSize = 4096;

        /// <summary>Menu particles (Fx_Sparkle 64 px, Fx_Feather_* / Fx_Dust 128 px): tiny soft-alpha sprites, capped at 128 and left uncompressed so the gradients do not band (64 KB each).</summary>
        public const string FxPrefix = "Fx/";
        public const int FxMaxSize = 128;

        /// <summary>The one Fx texture that is NOT a UI sprite: the dashed ring the aim preview tiles along a
        /// world-space LineRenderer (AbilityTelegraph's "no target yet" outline). A line tiles along U, so it
        /// must be Default type, wrap Repeat (U) / Clamp (V), with mipmaps (it is minified at a distance) and
        /// 256 px so the 4-dash strip is not halved by the Fx cap.</summary>
        public const string DashedRingKey = "Fx/Fx_DashedRing";
        public const int DashedRingMaxSize = 256;

        static readonly string[] PlatformOverrides = { "Standalone", "Android", "iPhone" };

        // Bump whenever the rules above change: Unity then re-imports every texture this
        // postprocessor touched, so the .meta files follow the code (2 = Phase 2 art rules,
        // 3 = Phase 3B Fx rule, 4 = Fx_DashedRing world-ribbon exception).
        public override uint GetVersion() => 4;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;

            var ti = (TextureImporter)assetImporter;
            string key = assetPath.Substring(Root.Length);
            key = key.Substring(0, key.LastIndexOf('.'));

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.spritePixelsPerUnit = 100;
            ti.maxTextureSize = 2048;

            if (key == DashedRingKey)
            {
                ApplyTiledRibbon(ti);
                return;
            }

            bool backdrop = key.StartsWith(BackdropPrefix);
            bool uncompressed = key.StartsWith("Atoms/") || (key.StartsWith("Backgrounds/") && !backdrop);
            ti.textureCompression = uncompressed
                ? TextureImporterCompression.Uncompressed
                : key.StartsWith("Frames/") ? TextureImporterCompression.CompressedHQ
                : TextureImporterCompression.Compressed;

            if (key == "Frames/Tex_WoodTile") ti.wrapMode = TextureWrapMode.Repeat;

            if (key.StartsWith(FxPrefix))
            {
                ti.maxTextureSize = FxMaxSize;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                foreach (var platform in PlatformOverrides)
                {
                    var ps = ti.GetPlatformTextureSettings(platform);
                    ps.maxTextureSize = FxMaxSize;
                    ti.SetPlatformTextureSettings(ps);
                }
            }
            else if (key.StartsWith(AbilityIconPrefix))
            {
                ti.maxTextureSize = AbilityIconMaxSize;
                foreach (var platform in PlatformOverrides)
                {
                    var ps = ti.GetPlatformTextureSettings(platform);
                    ps.maxTextureSize = AbilityIconMaxSize;   // also caps an existing override
                    ti.SetPlatformTextureSettings(ps);
                }
            }
            else if (backdrop)
            {
                ti.maxTextureSize = BackdropMaxSize;
                var android = ti.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = BackdropMaxSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(android);
            }

            Vector4 border = Borders.TryGetValue(key, out var b) ? b : Vector4.zero;

            // 9-slice requires FullRect; round-trip through TextureImporterSettings
            // so mesh type + border land together.
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteBorder = border;
            s.spriteAlignment = (int)SpriteAlignment.Center;
            ti.SetTextureSettings(s);
            ti.spriteBorder = border;
        }

        static void ApplyTiledRibbon(TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = DashedRingMaxSize;
            foreach (var platform in PlatformOverrides)
            {
                var ps = ti.GetPlatformTextureSettings(platform);
                ps.maxTextureSize = DashedRingMaxSize;
                ti.SetPlatformTextureSettings(ps);
            }
        }
    }
}
#endif
