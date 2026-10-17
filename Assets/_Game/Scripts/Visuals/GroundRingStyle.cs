using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Phase 8 chunk C R3: shared geometry for the player-colour foot rings (<c>IdentityRing</c> under every chicken,
    /// <c>RivalRing</c> under an on-screen rival). The ring radius follows the BODY footprint rather than the
    /// CharacterController capsule, so the ring shows past a big belly (Fatty) instead of hiding under it.
    /// Phase 8 D9: the colour stroke is framed by a two-tone edge, a dark INK band against the stroke and a thin CREAM rim
    /// outside it. Ink separates every player colour from light ground (sunlit grass, dirt path, straw pad, mound); the cream rim
    /// takes over on dark ground (fence / prop / bird shadows), so the ring reads against whatever it sits on without a per-pixel
    /// shader switch. Both tones are baked into the one edge sprite (no extra renderer).
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

        /// <summary>Phase 8 D9: ink band just outside the identity stroke (the R3 edge width), the cream rim outside the ink, and
        /// the edge sprite's alpha (both tones).</summary>
        public const float IdentityEdge = 0.045f;
        public const float IdentityRim = 0.03f;
        public const float IdentityEdgeAlpha = 0.9f;

        /// <summary>The rival ring never goes inside this gap beyond the identity ring.</summary>
        public const float RivalGapOutsideIdentity = 0.30f;
        /// <summary>Rival ring ink band / cream rim / alpha: thinner and lighter than the identity edge, but opaque enough for the
        /// ink to clear 3:1 on sunlit grass and straw (the R3 cream edge at 0.55 measured 1.2:1).</summary>
        public const float RivalEdge = 0.04f;
        public const float RivalRim = 0.025f;
        public const float RivalEdgeAlpha = 0.85f;

        /// <summary>Phase 8 D9: inner tone of the edge (next to the stroke; carries light ground) = the rival-pill / chevron-outline ink.</summary>
        public static Color EdgeInk => UI.UiGfx.TextDark;

        /// <summary>Phase 8 D9: outer rim of the edge (carries dark, shadowed ground) = the UI cream.</summary>
        public static Color EdgeRim => UI.UiGfx.TextPrimary;

        /// <summary>Phase 8 D8: opacity of the ghost arc (the part of a ring hidden by a food-pile mound) for a ring at its
        /// nominal alpha (see <see cref="SyncGhost"/>). Lives on the material (<c>RingGhost.mat</c> <c>_GhostAlpha</c>); pinned here for the tests.</summary>
        public const float GhostAlpha = 0.4f;

        /// <summary>Name suffix of the ghost child (<c>IdentityRingGhost</c>, <c>RivalRingGhost</c>).</summary>
        public const string GhostSuffix = "Ghost";

        /// <summary>Ghost sorting order relative to its ring (the edge is at -1).</summary>
        public const int GhostSortingOffset = -2;

        private const int Size = 256;
        private const float Feather = 0.012f; // root-local metres

        // Edge sprites carry a 1-2 px rim at the match camera, so they get finer texels and a tighter outer feather than the stroke.
        private const int EdgeSize = 512;
        private const float EdgeFeather = 0.006f;   // outer silhouette, root-local metres
        private const float ToneBlend = 0.006f;     // ink -> cream transition width

        private static readonly Dictionary<long, Sprite> Cache = new Dictionary<long, Sprite>();
        private static readonly Dictionary<(int, int, int), Sprite> EdgeCache = new Dictionary<(int, int, int), Sprite>();

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
        /// Phase 8 D9: the two-tone edge sprite for a colour stroke whose outer radius is <paramref name="radius"/>: opaque from just
        /// under the stroke (<c>radius - 0.006</c>, so no ground shows between stroke and edge) out to <c>radius + ink + rim</c>,
        /// coloured <see cref="EdgeInk"/> up to <c>radius + ink</c> and <see cref="EdgeRim"/> beyond. The sprite spans the edge's
        /// outer diameter (one unit wide, like <see cref="Annulus"/>). The tones live in the texture, so the renderer stays white
        /// and only its alpha fades; transparent texels carry the neighbouring tone so bilinear filtering never bleeds a third colour
        /// into the silhouette. Cached per (radius, ink, rim).
        /// </summary>
        public static Sprite EdgeBand(float radius, float ink, float rim)
        {
            var key = (Mathf.RoundToInt(radius * 1000f), Mathf.RoundToInt(ink * 1000f), Mathf.RoundToInt(rim * 1000f));
            if (EdgeCache.TryGetValue(key, out var cached) && cached != null) return cached;

            float outer = radius + ink + rim;
            float k = 0.5f / outer;                                   // metres -> texture units (the sprite spans [-0.5, 0.5])
            float inner = (radius - 0.006f) * k, f = Feather * k, fo = EdgeFeather * k;
            float split = (radius + ink) * k, tb = ToneBlend * k;
            const float o = 0.498f;
            Color inkC = EdgeInk, rimC = EdgeRim;
            var px = new Color[EdgeSize * EdgeSize];
            for (int y = 0; y < EdgeSize; y++)
            for (int x = 0; x < EdgeSize; x++)
            {
                float dx = (x + 0.5f) / EdgeSize - 0.5f;
                float dy = (y + 0.5f) / EdgeSize - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((d - (inner - f)) / (2f * f)) * Mathf.Clamp01((o - d) / fo);
                var c = Color.Lerp(inkC, rimC, Mathf.Clamp01((d - (split - tb * 0.5f)) / tb));
                c.a = a;
                px[y * EdgeSize + x] = c;
            }

            var tex = new Texture2D(EdgeSize, EdgeSize, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RingEdge" };
            tex.SetPixels(px);
            // 1 MB per edge sprite on the GPU (up to 8 live: identity + rival per class radius); a player build does not keep the
            // CPU copy as well. The Editor keeps it readable for the EditMode tests that sample the tones.
            tex.Apply(false, makeNoLongerReadable: !Application.isEditor);
            var sprite = Sprite.Create(tex, new Rect(0, 0, EdgeSize, EdgeSize), new Vector2(0.5f, 0.5f), EdgeSize);
            EdgeCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Builds (or rebuilds) a ring under <paramref name="anchor"/>: the colour stroke on <paramref name="ring"/> and its two-tone
        /// edge child named <c>name + "Edge"</c> (<see cref="EdgeBand"/>: an <paramref name="ink"/> band, then a cream
        /// <paramref name="rim"/>). The ring transform's scale is the colour diameter, so callers that pulse the ring scale the
        /// edge with it.
        /// </summary>
        public static void Apply(SpriteRenderer ring, ref SpriteRenderer edge, float radius, float stroke, float ink, float rim, float edgeAlpha)
        {
            ring.sprite = Annulus(radius, radius - stroke);
            ring.transform.localScale = Vector3.one * (radius * 2f);

            if (edge == null)
            {
                var go = new GameObject(ring.gameObject.name + "Edge");
                go.transform.SetParent(ring.transform, worldPositionStays: false);
                go.transform.localRotation = Quaternion.identity;
                edge = go.AddComponent<SpriteRenderer>();
                edge.color = new Color(1f, 1f, 1f, edgeAlpha);       // tones are in the texture; the renderer only fades
            }

            // Edge sprite spans the colour outer edge plus ink + rim; drawn one unit wide, scaled to that diameter relative to the parent.
            float outer = radius + ink + rim;
            edge.sprite = EdgeBand(radius, ink, rim);
            edge.transform.localPosition = Vector3.zero;
            edge.transform.localScale = Vector3.one * (outer / radius);
            edge.sortingOrder = ring.sortingOrder - 1;
            edge.sortingLayerID = ring.sortingLayerID;
        }

        /// <summary>
        /// Phase 8 D8: builds (or refreshes) the ghost child of <paramref name="ring"/>: the same annulus sprite drawn with
        /// <paramref name="material"/> (<c>Art/Materials/RingGhost.mat</c>, shader <c>Cluck Wars/Ring Ghost</c>), which only
        /// draws where the ring is behind a food-pile mound (ZTest Greater + the mound's stencil mark), at
        /// <see cref="GhostAlpha"/> alpha in the ring's colour. A child at identity, so it follows the ring's scale (GET READY pulse),
        /// position and active state; the colour follows via <see cref="SyncGhost"/>. Only the colour stroke gets a ghost: the
        /// ghost says "your colour is behind the pile"; the D9 ink / cream edge is a ground frame and is not ghosted. Returns false (no ghost) when <paramref name="material"/> is null.
        /// </summary>
        public static bool ApplyGhost(SpriteRenderer ring, ref SpriteRenderer ghost, Material material)
        {
            if (ring == null || material == null) return false;
            if (ghost == null)
            {
                var go = new GameObject(ring.gameObject.name + GhostSuffix);
                go.transform.SetParent(ring.transform, worldPositionStays: false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                ghost = go.AddComponent<SpriteRenderer>();
                ghost.sharedMaterial = material;
            }
            ghost.sprite = ring.sprite;
            ghost.sortingLayerID = ring.sortingLayerID;
            // Below the ring (order) and its edge (order - 1): every ghost sorts into one consecutive run, so the
            // ghost shader costs one SetPass per frame instead of a switch per ring. Ghost and ring never share a pixel
            // (ZTest Greater vs LEqual), so drawing it first changes nothing visible.
            ghost.sortingOrder = ring.sortingOrder + GhostSortingOffset;
            ghost.color = new Color(ring.color.r, ring.color.g, ring.color.b, 0f); // SyncGhost sets the alpha
            return true;
        }

        /// <summary>
        /// Copies the ring's colour onto its ghost, with the alpha expressed relative to the ring's nominal alpha
        /// (<paramref name="nominalAlpha"/>: identity 0.75, rival <see cref="FeedbackTuning.RivalRingAlpha"/>), so a ring at its
        /// normal opacity gets a ghost at exactly <see cref="GhostAlpha"/> (the material multiplies by it) and a fading ring
        /// (rival opacity, unstamped corner) fades its ghost with it. Brighter than nominal (GET READY) clamps at 1.
        /// </summary>
        public static void SyncGhost(SpriteRenderer ring, SpriteRenderer ghost, float nominalAlpha)
        {
            if (ring == null || ghost == null) return;
            var c = ring.color;
            c.a = nominalAlpha > 0f ? Mathf.Clamp01(c.a / nominalAlpha) : 0f;
            if (ghost.color != c) ghost.color = c;
        }

        /// <summary>Opacity of the two-tone edge follows the ring's own fade (rival opacity).</summary>
        public static void SetEdgeAlpha(SpriteRenderer edge, float alpha)
        {
            if (edge == null) return;
            var c = edge.color;
            c.a = alpha;
            edge.color = c;
        }
    }
}
