using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CluckWars.UI
{
    /// <summary>
    /// Procedural UI graphics helpers — bakes rounded-rect / hex / gradient sprites
    /// at runtime so the "rich glossy" art direction (ART.md §6) can be applied in a
    /// fully code-driven UGUI without importing sprite assets. Also resolves a chunky
    /// cartoon font (Lilita One if installed, else a bold OS fallback) and adds the
    /// baseline text drop-shadow the spec calls for.
    /// </summary>
    /// <remarks>
    /// All textures are tiny (≤ ~80px) and cached — generation cost is negligible and
    /// happens once per radius/shape. Sprites are tinted via <see cref="Image.color"/>
    /// so a single white shape serves every palette entry.
    /// </remarks>
    public static class UiGfx
    {
        private static readonly Dictionary<int, Sprite> _rounded = new();
        private static readonly Dictionary<int, Sprite> _hex     = new();
        private static Sprite _gloss;
        private static Sprite _circle;
        private static Font   _heading;
        private static Font   _body;
        private static Font   _emoji;

        // ---- Theme tokens (ART.md §6.1, cluckwars-tokens-v2) -------------------
        public static readonly Color PanelOuter   = Hex32("5a3a1a");
        public static readonly Color PanelInner    = Hex32("3a2210");
        public static readonly Color PanelBorder   = Hex32("8a6a3a");
        public static readonly Color CardTop        = Hex32("3a2816");
        public static readonly Color CardBottom      = Hex32("2a1c0e");
        public static readonly Color CardBorder      = Hex32("6a4a28");
        public static readonly Color Gold            = Hex32("f5c842");
        public static readonly Color GoldDark        = Hex32("b88a14");
        public static readonly Color GreenTop        = Hex32("5ac54f");
        public static readonly Color GreenBottom     = Hex32("228b22");
        public static readonly Color GreenBorder     = Hex32("1a6a1a");
        public static readonly Color TextPrimary     = Hex32("fef5e0");
        public static readonly Color TextSecondary   = Hex32("c4a060");
        public static readonly Color ScreenBg        = Hex32("0e0804");

        /// <summary>Parse a 6-hex-digit color (no '#').</summary>
        public static Color Hex32(string hex)
        {
            byte r = (byte)System.Convert.ToInt32(hex.Substring(0, 2), 16);
            byte g = (byte)System.Convert.ToInt32(hex.Substring(2, 2), 16);
            byte b = (byte)System.Convert.ToInt32(hex.Substring(4, 2), 16);
            return new Color32(r, g, b, 255);
        }

        /// <summary>Lighten/darken a color by an additive 0–255 amount (per channel).</summary>
        public static Color Adjust(Color c, int amount)
        {
            float a = amount / 255f;
            return new Color(
                Mathf.Clamp01(c.r + a),
                Mathf.Clamp01(c.g + a),
                Mathf.Clamp01(c.b + a),
                c.a);
        }

        // ---- Font --------------------------------------------------------------

        /// <summary>Heading / label font — Lilita One (ART.md §6.2), shipped in Resources/Fonts.</summary>
        public static Font ChunkyFont()
        {
            if (_heading == null)
                _heading = LoadFont("Fonts/LilitaOne-Regular",
                    new[] { "Lilita One", "Bauhaus 93", "Arial Black", "Impact" });
            return _heading;
        }

        /// <summary>Body / description font — Nunito (ART.md §6.2).</summary>
        public static Font BodyFont()
        {
            if (_body == null)
                _body = LoadFont("Fonts/Nunito-Bold", new[] { "Nunito", "Verdana", "Arial" });
            return _body;
        }

        /// <summary>Monochrome emoji font (Noto Emoji) for ability icon glyphs — tintable.</summary>
        public static Font EmojiFont()
        {
            if (_emoji == null)
                _emoji = LoadFont("Fonts/NotoEmoji-Regular",
                    new[] { "Segoe UI Emoji", "Segoe UI Symbol", "Apple Color Emoji" });
            return _emoji;
        }

        private static Font LoadFont(string resourcePath, string[] osFallback)
        {
            var f = Resources.Load<Font>(resourcePath);
            if (f != null) return f;
            f = Font.CreateDynamicFontFromOSFont(osFallback, 32);
            return f ?? (Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf"));
        }

        // ---- TextMeshPro emoji icons -------------------------------------------
        // Legacy UnityEngine.UI.Text can't render supplementary-plane emoji
        // (🪽 = U+1FABD, etc.), so the ability icon glyphs use TMP, which does.

        private static TMP_FontAsset _tmpEmoji;

        /// <summary>Runtime TMP font asset built from the monochrome Noto Emoji ttf (dynamic atlas).</summary>
        public static TMP_FontAsset TmpEmojiFont()
        {
            if (_tmpEmoji != null) return _tmpEmoji;
            var src = EmojiFont();
            if (src != null)
            {
                try { _tmpEmoji = TMP_FontAsset.CreateFontAsset(src); }
                catch (System.Exception e) { Debug.LogWarning($"[UiGfx] TMP emoji font build failed: {e.Message}"); }
            }
            return _tmpEmoji;
        }

        /// <summary>
        /// Creates a TextMeshPro icon label (for emoji glyphs) on a fresh child of
        /// <paramref name="parent"/>, stretched to fill. Returns the TMP component so
        /// the caller can re-anchor or relayout it.
        /// </summary>
        public static TextMeshProUGUI AddIcon(Transform parent, string name, string glyph,
            float fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var t = go.AddComponent<TextMeshProUGUI>();
            var fa = TmpEmojiFont();
            if (fa != null) t.font = fa;
            t.text          = glyph ?? string.Empty;
            t.fontSize      = fontSize;
            t.color         = color;
            t.alignment     = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode  = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Adds the spec's baseline text drop-shadow (0,−2, dark, 60%).</summary>
        public static void AddShadow(Graphic g, float alpha = 0.6f, Vector2? offset = null)
        {
            if (g == null) return;
            var sh = g.gameObject.GetComponent<Shadow>();
            if (sh == null) sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor    = new Color(0f, 0f, 0f, alpha);
            sh.effectDistance = offset ?? new Vector2(0f, -2f);
        }

        /// <summary>Adds a colored outline (used for the selected-card gold glow).</summary>
        public static Outline AddOutline(Graphic g, Color color, float size = 3f)
        {
            var o = g.gameObject.GetComponent<Outline>();
            if (o == null) o = g.gameObject.AddComponent<Outline>();
            o.effectColor    = color;
            o.effectDistance = new Vector2(size, size);
            o.useGraphicAlpha = false;
            return o;
        }

        // ---- Sprites -----------------------------------------------------------

        /// <summary>9-sliced white rounded-rect sprite (tint via Image.color).</summary>
        public static Sprite Rounded(int radius)
        {
            radius = Mathf.Max(2, radius);
            if (_rounded.TryGetValue(radius, out var cached)) return cached;

            int size = radius * 2 + 6;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            float half = size / 2f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                           Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                float d = outside + inside - radius;
                float a = Mathf.Clamp01(0.5f - d);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply();

            float b = radius + 2f;
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            _rounded[radius] = s;
            return s;
        }

        /// <summary>White circle sprite (player dots, badges).</summary>
        public static Sprite Circle()
        {
            if (_circle != null) return _circle;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = size / 2f, r = c - 1f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f));
            }
            tex.SetPixels(px);
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }

        /// <summary>
        /// Glossy sheen overlay (Simple-stretched): white highlight at the top fading
        /// out, dark wash at the bottom. Lay over a flat fill to fake a beveled gradient.
        /// </summary>
        public static Sprite Gloss()
        {
            if (_gloss != null) return _gloss;
            const int h = 64;
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1); // 0 bottom .. 1 top
                Color c;
                if (t >= 0.55f) c = new Color(1f, 1f, 1f, Mathf.Lerp(0.04f, 0.42f, (t - 0.55f) / 0.45f));
                else if (t <= 0.30f) c = new Color(0f, 0f, 0f, Mathf.Lerp(0.24f, 0f, t / 0.30f));
                else c = new Color(0f, 0f, 0f, 0f);
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply();
            _gloss = Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 100f);
            return _gloss;
        }

        /// <summary>9-sliceable pointy-top hexagon sprite (ability hex buttons, §6.6).</summary>
        public static Sprite Hex(int size)
        {
            size = Mathf.Max(16, size);
            if (_hex.TryGetValue(size, out var cached)) return cached;

            int w = size;
            int h = Mathf.RoundToInt(size * 1.1547f); // pointy-top ratio 2/√3
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            float cx = w / 2f, cy = h / 2f;
            float r = w / 2f - 1f;                 // circumradius (to side points)
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Pointy-top hexagon coverage via supersampled point-in-hex test.
                float cover = 0f;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    float ax = Mathf.Abs(x + 0.25f + sx * 0.5f - cx);
                    float ay = Mathf.Abs(y + 0.25f + sy * 0.5f - cy);
                    // pointy-top: vertical half-extent = r, sloped sides
                    bool inside = ay <= r && (ax * 0.8660254f + ay * 0.5f) <= r;
                    if (inside) cover += 0.25f;
                }
                px[y * w + x] = new Color(1f, 1f, 1f, cover);
            }
            tex.SetPixels(px);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            _hex[size] = s;
            return s;
        }

        // ---- Procedural chicken figure (class cards / preview, ART.md §4) ------

        private static readonly Dictionary<string, Sprite> _chicken = new();

        private struct ChickenDef { public Color body, dark, light; public float rx, ry, headR; }

        private static ChickenDef DefFor(string key) => key switch
        {
            // body / dark / light + body extents (fraction of texture) per design v3 silhouettes
            "warrior"  => new ChickenDef { body = Hex32("C04030"), dark = Hex32("8A2A20"), light = Hex32("F09888"), rx = 0.30f, ry = 0.30f, headR = 0.145f },
            "speedy"   => new ChickenDef { body = Hex32("E85A2A"), dark = Hex32("B84418"), light = Hex32("FFB088"), rx = 0.24f, ry = 0.31f, headR = 0.130f },
            "fatty"    => new ChickenDef { body = Hex32("F5D75A"), dark = Hex32("B89E20"), light = Hex32("FFF3B0"), rx = 0.37f, ry = 0.30f, headR = 0.150f },
            "assassin" => new ChickenDef { body = Hex32("7B68EE"), dark = Hex32("5A48C8"), light = Hex32("C4B8FF"), rx = 0.23f, ry = 0.27f, headR = 0.120f },
            _          => new ChickenDef { body = Hex32("C04030"), dark = Hex32("8A2A20"), light = Hex32("F09888"), rx = 0.30f, ry = 0.30f, headR = 0.145f },
        };

        /// <summary>Cached procedural chicken figure for a class key (warrior/speedy/fatty/assassin).</summary>
        public static Sprite Chicken(string key)
        {
            if (_chicken.TryGetValue(key, out var cached)) return cached;
            var d = DefFor(key);
            const int S = 144;
            var px = new Color[S * S];

            float cx = S * 0.5f;
            float rx = d.rx * S, ry = d.ry * S, headR = d.headR * S;
            float bodyCY = S * 0.42f;
            float shadowCY = S * 0.12f;
            float headCY = bodyCY + ry * 0.92f + headR * 0.35f;
            float headCX = cx + headR * 0.12f;

            var comb  = Hex32("e74c3c");
            var beak  = Hex32("F0A020");
            var leg   = Hex32("E8A020");

            // Ground shadow.
            Raster(px, S, S, (int)(cx - rx), (int)(shadowCY - 8), (int)(cx + rx), (int)(shadowCY + 8),
                (x, y) => Ell(x, y, cx, shadowCY, rx * 0.82f, 7f), (x, y) => new Color(0f, 0f, 0f, 0.22f));

            // Legs (two stubby orange bars).
            for (int s = -1; s <= 1; s += 2)
            {
                float lx = cx + s * rx * 0.30f;
                Raster(px, S, S, (int)(lx - 3 + s * 4), (int)shadowCY, (int)(lx + 3), (int)(bodyCY),
                    (x, y) => Mathf.Abs(x - Mathf.Lerp(lx + s * 4, lx, (y - shadowCY) / Mathf.Max(1f, bodyCY - shadowCY))) <= 2.4f,
                    (x, y) => leg);
            }

            // Body with radial-ish gradient (light upper-left → body → dark lower-right).
            Raster(px, S, S, (int)(cx - rx - 1), (int)(bodyCY - ry - 1), (int)(cx + rx + 1), (int)(bodyCY + ry + 1),
                (x, y) => Ell(x, y, cx, bodyCY, rx, ry),
                (x, y) =>
                {
                    float nx = (x - cx) / rx, ny = (y - bodyCY) / ry;
                    float t = Mathf.Clamp01(0.5f + (ny - nx) * 0.42f);
                    return t < 0.5f ? Color.Lerp(d.dark, d.body, t / 0.5f)
                                    : Color.Lerp(d.body, d.light, (t - 0.5f) / 0.5f);
                });

            // Body highlight + wing.
            Raster(px, S, S, (int)(cx - rx), (int)(bodyCY), (int)(cx), (int)(bodyCY + ry),
                (x, y) => Ell(x, y, cx - rx * 0.35f, bodyCY + ry * 0.28f, rx * 0.34f, ry * 0.30f),
                (x, y) => new Color(1f, 1f, 1f, 0.14f));
            Raster(px, S, S, (int)(cx - rx), (int)(bodyCY - ry), (int)(cx + rx * 0.2f), (int)(bodyCY + ry),
                (x, y) => Ell(x, y, cx - rx * 0.45f, bodyCY - ry * 0.05f, rx * 0.28f, ry * 0.42f),
                (x, y) => new Color(d.dark.r, d.dark.g, d.dark.b, 0.45f));

            // Head.
            Raster(px, S, S, (int)(headCX - headR - 1), (int)(headCY - headR - 1), (int)(headCX + headR + 1), (int)(headCY + headR + 1),
                (x, y) => Ell(x, y, headCX, headCY, headR, headR),
                (x, y) =>
                {
                    float nx = (x - headCX) / headR, ny = (y - headCY) / headR;
                    float t = Mathf.Clamp01(0.5f + (ny - nx) * 0.45f);
                    return t < 0.5f ? Color.Lerp(d.dark, d.body, t / 0.5f)
                                    : Color.Lerp(d.body, d.light, (t - 0.5f) / 0.5f);
                });

            // Comb (two red bumps on top of the head).
            for (int b = 0; b < 2; b++)
            {
                float bx = headCX - headR * 0.30f + b * headR * 0.55f;
                float by = headCY + headR * 0.85f;
                Raster(px, S, S, (int)(bx - headR * 0.4f), (int)(by - headR * 0.2f), (int)(bx + headR * 0.4f), (int)(by + headR * 0.6f),
                    (x, y) => Ell(x, y, bx, by, headR * 0.28f, headR * 0.42f), (x, y) => comb);
            }

            // Beak (triangle pointing right).
            float bx0 = headCX + headR * 0.7f, by0 = headCY + headR * 0.18f;
            Raster(px, S, S, (int)bx0, (int)(by0 - headR * 0.5f), (int)(bx0 + headR * 1.1f), (int)(by0 + headR * 0.4f),
                (x, y) => InTri(x, y, bx0, by0 + headR * 0.22f, bx0 + headR * 1.0f, by0, bx0, by0 - headR * 0.30f),
                (x, y) => beak);

            // Eye (white + pupil + speck).
            float ex = headCX + headR * 0.32f, ey = headCY + headR * 0.30f, er = headR * 0.34f;
            Raster(px, S, S, (int)(ex - er - 1), (int)(ey - er - 1), (int)(ex + er + 1), (int)(ey + er + 1),
                (x, y) => Ell(x, y, ex, ey, er, er), (x, y) => Color.white);
            Raster(px, S, S, (int)(ex - er), (int)(ey - er), (int)(ex + er), (int)(ey + er),
                (x, y) => Ell(x, y, ex + er * 0.25f, ey, er * 0.55f, er * 0.55f), (x, y) => new Color(0.1f, 0.1f, 0.1f, 1f));

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            _chicken[key] = sprite;
            return sprite;
        }

        // Rasterizer with 2×2 supersampled coverage + src-over blend.
        private delegate bool InsideFn(float x, float y);
        private static void Raster(Color[] px, int w, int h, int x0, int y0, int x1, int y1,
            InsideFn inside, System.Func<float, float, Color> col)
        {
            x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0);
            x1 = Mathf.Min(w - 1, x1); y1 = Mathf.Min(h - 1, y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float cov = 0f;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                    if (inside(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f)) cov += 0.25f;
                if (cov <= 0f) continue;
                var c = col(x + 0.5f, y + 0.5f);
                c.a *= cov;
                Blend(px, w, x, y, c);
            }
        }

        private static bool Ell(float x, float y, float cx, float cy, float rx, float ry)
        {
            float nx = (x - cx) / rx, ny = (y - cy) / ry;
            return nx * nx + ny * ny <= 1f;
        }

        private static bool InTri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static void Blend(Color[] px, int w, int x, int y, Color src)
        {
            int i = y * w + x;
            if (i < 0 || i >= px.Length) return;
            Color dst = px[i];
            float a = src.a;
            float outA = a + dst.a * (1f - a);
            if (outA <= 0f) { px[i] = new Color(0f, 0f, 0f, 0f); return; }
            px[i] = new Color(
                (src.r * a + dst.r * dst.a * (1f - a)) / outA,
                (src.g * a + dst.g * dst.a * (1f - a)) / outA,
                (src.b * a + dst.b * dst.a * (1f - a)) / outA,
                outA);
        }

        // ---- Composers ---------------------------------------------------------

        /// <summary>
        /// Turns <paramref name="go"/> into a glossy rounded panel: rounded border
        /// frame + gradient fill + sheen overlay. Returns the fill Image (for tinting
        /// / Button.targetGraphic).
        /// </summary>
        public static Image StylePanel(GameObject go, Color fill, Color border,
            int radius = 14, int borderPx = 3, bool gloss = true)
        {
            var frame = go.GetComponent<Image>();
            if (frame == null) frame = go.AddComponent<Image>();
            frame.color  = fill;
            frame.pixelsPerUnitMultiplier = 1f;

            var sdf = go.GetComponent<SDFImageEffect>();
            if (sdf == null) sdf = go.AddComponent<SDFImageEffect>();
            
            var mat = new Material(Shader.Find("CluckWars/UI/SDF"));
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Radius", radius);
            if (borderPx > 0) {
                mat.SetColor("_BorderColor", border);
                mat.SetFloat("_BorderWidth", borderPx);
            }
            frame.material = mat;

            return frame;
        }

        public static Image StyleHexagon(GameObject go, Color fill, Color border, int borderPx = 3)
        {
            var frame = go.GetComponent<Image>();
            if (frame == null) frame = go.AddComponent<Image>();
            frame.color  = fill;
            frame.pixelsPerUnitMultiplier = 1f;

            var sdf = go.GetComponent<SDFImageEffect>();
            if (sdf == null) sdf = go.AddComponent<SDFImageEffect>();
            
            var mat = new Material(Shader.Find("CluckWars/UI/SDF"));
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Shape", 1f); // 1 = Hexagon
            if (borderPx > 0) {
                mat.SetColor("_BorderColor", border);
                mat.SetFloat("_BorderWidth", borderPx);
            }
            frame.material = mat;

            return frame;
        }
    }
}
