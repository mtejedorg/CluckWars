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
        };

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

            bool uncompressed = key.StartsWith("Atoms/") || key.StartsWith("Backgrounds/");
            ti.textureCompression = uncompressed
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.Compressed;

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
    }
}
#endif
