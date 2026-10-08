using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The gold invite-code letter tiles (THE COOP host card and the in-match waiting room): each tile
    /// is a fixed-width box sized from its font, and the row shrinks the font until the code fits the
    /// width the bar leaves it, then wraps to two lines (round-2 leftover: the 9-letter offline "cluck-lan"
    /// code ran under SHARE / COPY). <see cref="Fit"/> is the pure rule (EditMode tests); <see cref="Fill"/>
    /// builds the tiles and refits them whenever the row is laid out.
    /// </summary>
    public static class CodeTileFit
    {
        public const float MaxFont = 52f;
        public const float MinFont = 28f;

        /// <summary>Largest font once the code wraps: two lines stay about as tall as one full-size line.</summary>
        public const float WrapMaxFont = 36f;

        /// <summary>A tile's box per font point (a LilitaOne capital plus side room).</summary>
        public const float WidthPerFont = 1.15f;

        /// <summary>Per tile outside the box: 8 px right margin + 2 x 2 px border (.cw-code-tile).</summary>
        public const float Chrome = 12f;

        private const string FitClass = "cw-code-tiles--fit";

        /// <summary>Horizontal room one tile takes at <paramref name="font"/>.</summary>
        public static float TileWidth(float font) => font * WidthPerFont + Chrome;

        /// <summary>
        /// Font for <paramref name="count"/> tiles in <paramref name="available"/> px, and whether the row
        /// wraps: one line while the tiles can stay at least <see cref="MinFont"/>; otherwise two lines
        /// (half the tiles each) at up to <see cref="WrapMaxFont"/>.
        /// </summary>
        public static (float font, bool wrap) Fit(float available, int count)
        {
            if (count <= 0 || float.IsNaN(available) || available <= 0f) return (MaxFont, false);
            float oneLine = FontFor(available, count);
            if (oneLine >= MinFont) return (Mathf.Min(oneLine, MaxFont), false);
            return (Mathf.Clamp(FontFor(available, (count + 1) / 2), MinFont, WrapMaxFont), true);
        }

        private static float FontFor(float available, int perLine) => (available / perLine - Chrome) / WidthPerFont;

        /// <summary>Replaces <paramref name="tiles"/>' children with one tile per character of <paramref name="code"/>.</summary>
        public static void Fill(VisualElement tiles, string code)
        {
            code = code?.ToUpperInvariant() ?? string.Empty;
            // The waiting room re-fills on every poll: keep the fitted tiles while the code is the same.
            if (tiles.ClassListContains(FitClass) && Shows(tiles, code)) return;
            tiles.Clear();
            tiles.style.flexWrap = Wrap.NoWrap;
            if (!tiles.ClassListContains(FitClass))
            {
                tiles.AddToClassList(FitClass);
                tiles.RegisterCallback<GeometryChangedEvent>(_ => Refit(tiles));
            }
            if (code.Length == 0) return;
            foreach (var ch in code)
            {
                var t = new Label(ch.ToString());
                t.AddToClassList("cw-code-tile");
                SetFont(t, MaxFont);
                tiles.Add(t);
            }
        }

        // The row's width is what the bar leaves it (it shrinks under pressure, tiles overflowing); refit the
        // tiles to it. Once they fit the row hugs them, so this settles in one pass.
        private static void Refit(VisualElement tiles)
        {
            int n = tiles.childCount;
            float available = tiles.layout.width;
            if (n == 0 || float.IsNaN(available) || available <= 0f) return;
            if (tiles.style.flexWrap.value == Wrap.Wrap) return;   // already refitted for this code (Fill resets it)
            float current = tiles[0].style.fontSize.value.value;
            if (n * TileWidth(current) <= available + 0.5f) return;
            var (font, wrap) = Fit(available, n);
            foreach (var t in tiles.Children()) SetFont(t, font);
            if (wrap) tiles.style.flexWrap = Wrap.Wrap;
        }

        private static bool Shows(VisualElement tiles, string code)
        {
            if (tiles.childCount != code.Length) return false;
            for (int i = 0; i < code.Length; i++)
                if (!(tiles[i] is Label l) || l.text.Length != 1 || l.text[0] != code[i]) return false;
            return true;
        }

        private static void SetFont(VisualElement tile, float font)
        {
            tile.style.fontSize = font;
            tile.style.width = font * WidthPerFont;
        }
    }
}
