using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Phase 8 chunk C R3: shared geometry for the player-colour foot rings (<c>IdentityRing</c> under every chicken,
    /// <c>RivalRing</c> under an on-screen rival). The ring radius follows the BODY footprint rather than the
    /// CharacterController capsule, so the ring shows past a big belly (Fatty) instead of hiding under it, and the
    /// colour stroke gets a thin cream outer edge so a dark player colour (P4 teal) separates from the grass.
    /// Visual only: hitboxes, the capsule, stats and the class model scale are untouched.
    /// </summary>
    /// <remarks>
    /// All distances are chicken-root-local metres (the rings live under the ground anchor, which inherits the class
    /// scale), so a ring authored for the unit-scale Warrior is automatically right for the scaled classes.
    /// </remarks>
    public static class GroundRingStyle
    {
        /// <summary>Identity ring floor: the old radius (capsule radius 0.40 + 0.02). A tiny body never gets a smaller ring.</summary>
        public const float IdentityMinRadius = 0.42f;

        /// <summary>How far the identity ring's OUTER colour edge sits beyond the body's lateral half-width.</summary>
        public const float IdentityMargin = 0.16f;

        /// <summary>Identity colour stroke (was ~0.13 soft-edged). Thick enough to read on a phone at match distance,
        /// still well under the D3 base ring.</summary>
        public const float IdentityStroke = 0.15f;

        /// <summary>Cream outer edge beyond the identity stroke, and its alpha.</summary>
        public const float IdentityEdge = 0.035f;
        public const float IdentityEdgeAlpha = 0.85f;

        /// <summary>The rival ring never goes inside this gap beyond the identity ring.</summary>
        public const float RivalGapOutsideIdentity = 0.30f;
        public const float RivalEdge = 0.022f;
        public const float RivalEdgeAlpha = 0.55f;

        private const int Size = 256;
        private const float Feather = 0.012f; // root-local metres

        private static readonly Dictionary<long, Sprite> Cache = new Dictionary<long, Sprite>();

        /// <summary>Outer radius of the identity colour stroke for a body of the given lateral half-width.</summary>
        public static float IdentityRadius(float bodyHalfWidth) =>
            Mathf.Max(IdentityMinRadius, bodyHalfWidth + IdentityMargin);

        /// <summary>Outer radius of the rival colour stroke: the authored radius, or just outside the identity ring.</summary>
        public static float RivalRadius(float bodyHalfWidth) =>
            Mathf.Max(FeedbackTuning.RivalRingRadius, IdentityRadius(bodyHalfWidth) + RivalGapOutsideIdentity);

        /// <summary>
        /// Annulus sprite one unit wide whose outer edge is <paramref name="outer"/> and inner edge
        /// <paramref name="inner"/> (both in the sprite's own metres, so a sprite drawn at scale 2 x outer shows exactly that
        /// stroke width). Cached per (outer, inner).
        /// </summary>
        public static Sprite Annulus(float outer, float inner)
        {
            long key = ((long)Mathf.RoundToInt(outer * 1000f) << 32) | (uint)Mathf.RoundToInt(inner * 1000f);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            float o = 0.5f;                          // texture units: the sprite spans [-0.5, 0.5]
            float k = 0.5f / outer;                  // metres -> texture units
            float i = inner * k, f = Feather * k;
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size - 0.5f;
                float dy = (y + 0.5f) / Size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((d - (i - f)) / (2f * f)) * Mathf.Clamp01(((o - 0.002f) - d) / (2f * f));
                px[y * Size + x] = new Color(1f, 1f, 1f, a);
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Builds (or rebuilds) a ring under <paramref name="anchor"/>: the colour stroke on <paramref name="ring"/> and a cream
        /// edge child named <c>name + "Edge"</c>. The ring transform's scale is the colour diameter, so callers that pulse the ring
        /// scale the edge with it.
        /// </summary>
        public static void Apply(SpriteRenderer ring, ref SpriteRenderer edge, float radius, float stroke, float edgeWidth, float edgeAlpha)
        {
            ring.sprite = Annulus(radius, radius - stroke);
            ring.transform.localScale = Vector3.one * (radius * 2f);

            if (edge == null)
            {
                var go = new GameObject(ring.gameObject.name + "Edge");
                go.transform.SetParent(ring.transform, worldPositionStays: false);
                go.transform.localRotation = Quaternion.identity;
                edge = go.AddComponent<SpriteRenderer>();
                edge.color = new Color(UI.UiGfx.TextPrimary.r, UI.UiGfx.TextPrimary.g, UI.UiGfx.TextPrimary.b, edgeAlpha);
            }

            // Edge sprite spans the colour outer edge plus the edge width; drawn one unit wide, scaled to that diameter relative to the parent.
            float outer = radius + edgeWidth;
            edge.sprite = Annulus(outer, radius - 0.006f);
            edge.transform.localPosition = Vector3.zero;
            edge.transform.localScale = Vector3.one * (outer / radius);
            edge.sortingOrder = ring.sortingOrder - 1;
            edge.sortingLayerID = ring.sortingLayerID;
        }

        /// <summary>Opacity of the cream edge follows the ring's own fade (rival opacity, GET READY pulse).</summary>
        public static void SetEdgeAlpha(SpriteRenderer edge, float alpha)
        {
            if (edge == null) return;
            var c = edge.color;
            c.a = alpha;
            edge.color = c;
        }
    }
}
