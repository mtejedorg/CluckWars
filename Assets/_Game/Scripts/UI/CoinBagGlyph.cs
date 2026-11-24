using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The loot bag: a sack with a tied neck, a flared top and a coin on its belly, drawn with <see cref="Painter2D"/> in a square, so it needs no sprite.
    /// <b>A placeholder for the 2D artist</b> (owner 2026-11-24: "como una bolsa de monedas", never a crown): gold sack, ink outline, cream coin, in the two-tone
    /// style of the ability glyphs. When the real icon exists, the three elements that call this swap to a USS background image and this file goes.
    /// </summary>
    public static class CoinBagGlyph
    {
        /// <summary>Draws the bag centred in <paramref name="box"/> (the largest square that fits). <paramref name="fill"/> is the sack, <paramref name="coin"/> the coin.</summary>
        public static void Draw(Painter2D p, Rect box, Color fill, Color ink, Color coin)
        {
            float s = Mathf.Min(box.width, box.height);
            if (s <= 1f) return;
            Vector2 o = box.center - new Vector2(s, s) * 0.5f;
            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);

            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.lineWidth = Mathf.Max(1.5f, s * 0.05f);
            p.strokeColor = ink;
            p.fillColor = fill;

            // The sack.
            p.BeginPath();
            p.Arc(P(0.5f, 0.64f), 0.30f * s, 0f, 360f);
            p.ClosePath();
            p.Fill();
            p.Stroke();

            // The neck, wider toward the sack.
            p.BeginPath();
            p.MoveTo(P(0.41f, 0.30f));
            p.LineTo(P(0.59f, 0.30f));
            p.LineTo(P(0.65f, 0.44f));
            p.LineTo(P(0.35f, 0.44f));
            p.ClosePath();
            p.Fill();
            p.Stroke();

            // The flared top.
            p.BeginPath();
            p.MoveTo(P(0.34f, 0.12f));
            p.LineTo(P(0.42f, 0.30f));
            p.LineTo(P(0.58f, 0.30f));
            p.LineTo(P(0.66f, 0.12f));
            p.LineTo(P(0.50f, 0.20f));
            p.ClosePath();
            p.Fill();
            p.Stroke();

            // The tie.
            p.lineWidth = Mathf.Max(2f, s * 0.07f);
            p.BeginPath();
            p.MoveTo(P(0.38f, 0.31f));
            p.LineTo(P(0.62f, 0.31f));
            p.Stroke();

            // The coin.
            p.lineWidth = Mathf.Max(1.5f, s * 0.05f);
            p.fillColor = coin;
            p.BeginPath();
            p.Arc(P(0.5f, 0.66f), 0.12f * s, 0f, 360f);
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }

        /// <summary>Hooks <paramref name="element"/> so it paints the bag in the loot gold (the palette) over the ink and the cream.</summary>
        public static void Attach(VisualElement element)
        {
            if (element == null) return;
            element.generateVisualContent += ctx =>
                Draw(ctx.painter2D, element.contentRect, Visuals.FeedbackTuning.LootGoldColor, Visuals.FeedbackTuning.MarkInk, Visuals.FeedbackTuning.MarkCream);
        }
    }
}
