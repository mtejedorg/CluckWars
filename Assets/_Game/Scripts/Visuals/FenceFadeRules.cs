using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules for the see-through fence near the local bird (Phase 6 chunk 7f, round-3 finding 9):
    /// at "3", GO! and in play the near fence could cover the player's own chicken down to its comb.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fade itself is a dithered alpha-clip in <c>Assets/_Game/Art/Shaders/CWFenceFade.hlsl</c>, used by
    /// the fence material's shader (<see cref="ShaderName"/>). <see cref="FragmentFade"/> is the C# twin of
    /// <c>CW_FenceFadeAmount</c> so the shape of the hole is pinned by EditMode tests; keep the two identical.
    /// </para>
    /// <para>
    /// <b>Two gates, on purpose.</b> The CPU decides WHETHER the bird is behind a fence piece
    /// (<see cref="IsOccluded"/>, a few rays from the bird toward the camera against the fence renderers'
    /// bounds) and eases a strength 0..1 over <see cref="FadeSeconds"/>; the shader decides WHERE: only fence
    /// fragments within a screen-space circle around the bird AND closer to the camera than the bird fade, so
    /// the far fence and anything behind the bird stay solid.
    /// </para>
    /// <para>
    /// <b>Phase 8 D4.</b> Besides the local bird's hole (every fence material), every other chicken — rivals and
    /// Doppelganger decoys alike — covered by an OUTER fence gets its own hole of the same shape and timing, on the
    /// materials with <see cref="AllBirdsKeywordName"/> only (<see cref="CombinedFragmentFade"/>).
    /// </para>
    /// <para>
    /// <b>Phase 8 E2.</b> Three more holes for the LOCAL player, on outer fences only: the own deposit pad and its two
    /// home-side piles (<see cref="FixedTargetWanted"/>, <see cref="IsTargetOccluded"/>, <see cref="TargetRadiusShift"/>).
    /// They have their own shader array, so they never take a chicken slot.
    /// </para>
    /// <para>Local presentation only — nothing here is networked.</para>
    /// </remarks>
    public static class FenceFadeRules
    {
        /// <summary>The fence / wall shader every fence renderer and every base coop in <c>Map.unity</c> must use (the
        /// coop sits against the spawn point, so at GET READY it is what covered the bird).</summary>
        public const string ShaderName = "Cluck Wars/Fence Fade Lit";

        /// <summary>Fade in / out time when the bird goes behind or comes out from a fence (spec: ~0.15 s).</summary>
        public const float FadeSeconds = 0.15f;

        /// <summary>Hole radius where the fade is full, as a fraction of the screen height. At the match
        /// camera's ortho size 5.6 (11.2 m of view height) 0.12 is ~1.3 m: the whole bird. Phase 8 C16 raised it
        /// from 0.09 so the dithered ramp to <see cref="OuterRadius"/> is a narrow edge
        /// (<see cref="EdgeBandWidth"/>), not a wide stippled band; the hole's outer size is unchanged.</summary>
        public const float InnerRadius = 0.12f;

        /// <summary>Where the fade has ramped back to solid, as a fraction of the screen height (~1.7 m).</summary>
        public const float OuterRadius = 0.15f;

        /// <summary>Width of the dithered edge (inner -> outer radius), fractions of the screen height. Phase 8 C16:
        /// 0.03 (was 0.06) keeps the edge to a few dither steps instead of a wide screen-door ring.</summary>
        public const float EdgeBandWidth = OuterRadius - InnerRadius;

        /// <summary>Peak fade. Below 1 leaves a faint screen-door of fence so the player still sees there is a
        /// wall between them and the open arena (it still blocks movement). The 4x4 dither has 16 thresholds at
        /// (k + 0.5) / 16, so 0.92 clears 15 of 16 pixels: 6.25 % of the fence remains at the hole centre
        /// (0.90 would clear only 14, leaving 12.5 %). Phase 8 A10 raised it from 0.85.</summary>
        public const float MaxFade = 0.92f;

        /// <summary>Width of the cream rim band at the hole's edge, as a fraction of the screen height
        /// (~0.13 of <see cref="OuterRadius"/>). Phase 8 A10.</summary>
        public const float RimWidth = 0.02f;

        /// <summary>Centre of the rim band: just inside <see cref="OuterRadius"/>, on the still-mostly-solid
        /// fence where the dithered ramp ends, so it outlines the hole on the pickets that remain.</summary>
        public const float RimCentre = OuterRadius - 0.5f * RimWidth;

        /// <summary>Peak blend of the fence colour toward <see cref="RimColor"/> (subtle: the fence keeps its
        /// wood colour, the rim reads as a light edge).</summary>
        public const float RimIntensity = 0.45f;

        /// <summary>The UI cream (<c>UiGfx.TextPrimary</c>, #FEF5E0) — the same light edge the HUD rims use.</summary>
        public static readonly Color RimColor = new Color32(0xFE, 0xF5, 0xE0, 0xFF);

        /// <summary>Global shader keyword that compiles the fade / rim code into the fence passes
        /// (<c>#pragma multi_compile_fragment _ _CW_FENCE_FADE</c>). Global, not local: the driver feeds globals,
        /// and keywords cannot go in a MaterialPropertyBlock.</summary>
        public const string KeywordName = "_CW_FENCE_FADE";

        /// <summary>World units in front of the bird over which the depth gate ramps in. Keeps the edge of the
        /// hole soft when the bird hugs the fence instead of popping on the fence's own plane.</summary>
        public const float DepthSoftness = 0.35f;

        /// <summary>Body height assumed when the chicken has no collider to measure (metres).</summary>
        public const float FallbackBodyHeight = 1f;

        /// <summary>Fractions of the body height (feet = 0, top = 1) tested for occlusion: feet, body, comb.
        /// The bird counts as "behind the fence" as soon as any of them is covered — the reported case showed
        /// only the comb.</summary>
        public static readonly float[] OcclusionSampleFractions = { 0.05f, 0.5f, 0.95f };

        /// <summary>
        /// Eases the global fade strength toward 1 while the bird is covered and toward 0 when it is not,
        /// at a constant rate that crosses the whole range in <see cref="FadeSeconds"/>.
        /// </summary>
        public static float StepStrength(float current, bool occluded, float deltaSeconds)
            => StepStrengthToward(current, occluded ? 1f : 0f, deltaSeconds);

        /// <summary>
        /// Eases a strength toward any <paramref name="target"/> in 0..1 at the same constant rate as
        /// <see cref="StepStrength"/> (the whole range in <see cref="FadeSeconds"/>). Phase 8 D4: a faded
        /// (ghosted) chicken's hole targets its visual opacity instead of 1 (<see cref="TargetStrength"/>).
        /// </summary>
        public static float StepStrengthToward(float current, float target, float deltaSeconds)
        {
            float rate = FadeSeconds > 0f ? Mathf.Max(0f, deltaSeconds) / FadeSeconds : 1f;
            return Mathf.MoveTowards(Mathf.Clamp01(current), Mathf.Clamp01(target), rate);
        }

        // ---- Phase 8 D4: holes for every chicken behind an OUTER fence ------------------------------------

        /// <summary>Material keyword (<c>#pragma shader_feature_local_fragment _ _CW_FENCE_ALL_BIRDS</c>) that marks a
        /// fence material as an OUTER perimeter fence: it opens for every chicken behind it (Phase 8 D4, Maestro:
        /// "all OUTER fences fade when a chicken is behind them"). Set on <c>Prop_wall_segment.mat</c> (the boundary
        /// runs <c>Wall_[NSEW]_*</c> and the prefab default); OFF on <c>Prop_wall_segment_Inner.mat</c> (the pinwheel
        /// arm panels <c>InnerWall_*</c>) and <c>Prop_chicken_coop.mat</c>, which keep the local-bird-only hole.
        /// The driver picks its outer occluders by this same keyword, so the CPU selection and the shader can never
        /// disagree about which panel is "outer".</summary>
        public const string AllBirdsKeywordName = "_CW_FENCE_ALL_BIRDS";

        /// <summary>Is <paramref name="material"/> an outer fence that opens for every chicken
        /// (<see cref="AllBirdsKeywordName"/> enabled on the material)?</summary>
        public static bool FadesForAllBirds(Material material) =>
            material != null && material.IsKeywordEnabled(AllBirdsKeywordName);

        /// <summary>Hole slots for chickens other than the local bird (shader <c>CW_FENCE_MAX_BIRDS</c>; keep equal).
        /// 3 rivals + their Doppelganger decoys + the local player's own decoy fit with room to spare.</summary>
        public const int MaxBirdSlots = 8;

        /// <summary>
        /// The strength a non-local chicken's hole eases toward. Not covered: 0. Covered: its networked
        /// <c>VisualOpacity</c> (every peer sees the same value), so a ghosted rival (Invisibility / Smoke Roost,
        /// 0.2) opens only a ~20 % screen-door hole and the hole never reveals more than the ghost body itself
        /// does in the open. 0 means "not replicated yet" and counts as fully visible (same rule as
        /// <c>ChickenVisuals</c>). Decoys go through exactly this rule; nothing here knows what a decoy is.
        /// </summary>
        public static float TargetStrength(bool occluded, float visualOpacity)
        {
            if (!occluded) return 0f;
            return visualOpacity > 0f ? Mathf.Clamp01(visualOpacity) : 1f;
        }

        /// <summary>
        /// Can a hole centred at <paramref name="viewport"/> (camera viewport coordinates, z = distance in front)
        /// touch the screen? The hole's outer radius is <see cref="OuterRadius"/> screen HEIGHTS, never more than
        /// that in viewport-width units on a landscape screen, so <see cref="OuterRadius"/> is a safe margin on both
        /// axes. Off-screen holes take no slot and do not keep the fade variant on (a rival behind the far side of
        /// the arena's outer fence costs nothing).
        /// </summary>
        public static bool HoleOnScreen(Vector3 viewport) =>
            viewport.z > 0f
            && viewport.x > -OuterRadius && viewport.x < 1f + OuterRadius
            && viewport.y > -OuterRadius && viewport.y < 1f + OuterRadius;

        /// <summary>
        /// Picks which candidates get one of the <paramref name="maxSlots"/> hole slots: only those with a strength
        /// above 0; the strongest first, ties to the lower <paramref name="priorities"/> value (the driver passes the
        /// distance from the screen centre). Writes candidate indices into <paramref name="chosen"/> and returns how
        /// many. No allocation (a partial selection sort over at most a dozen entries).
        /// </summary>
        public static int SelectSlots(IReadOnlyList<float> strengths, IReadOnlyList<float> priorities, int count,
            int[] chosen, int maxSlots)
        {
            if (strengths == null || chosen == null) return 0;
            count = Mathf.Min(count, strengths.Count);
            int cap = Mathf.Min(maxSlots, chosen.Length);
            int n = 0;
            while (n < cap)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if (strengths[i] <= 0f || Contains(chosen, n, i)) continue;
                    if (best < 0 || Better(i, best)) best = i;
                }
                if (best < 0) break;
                chosen[n++] = best;
            }
            return n;

            bool Better(int a, int b)
            {
                if (strengths[a] != strengths[b]) return strengths[a] > strengths[b];
                float pa = priorities != null && a < priorities.Count ? priorities[a] : 0f;
                float pb = priorities != null && b < priorities.Count ? priorities[b] : 0f;
                return pa != pb ? pa < pb : a < b;
            }
        }

        private static bool Contains(int[] values, int count, int value)
        {
            for (int i = 0; i < count; i++) if (values[i] == value) return true;
            return false;
        }

        /// <summary>One screen-space hole as one fence fragment sees it (C# side of a shader slot).</summary>
        public readonly struct HoleSample
        {
            public readonly float Strength, ScreenDist, BirdEyeDepth;
            public HoleSample(float strength, float screenDist, float birdEyeDepth)
            { Strength = strength; ScreenDist = screenDist; BirdEyeDepth = birdEyeDepth; }
        }

        /// <summary>
        /// C# twin of the combined clip in <c>CW_FenceFadeClipRim</c>: the local bird's hole applies to every fence
        /// material; the other chickens' holes only where the material is an outer fence
        /// (<paramref name="fadesForAllBirds"/>, i.e. <see cref="AllBirdsKeywordName"/> on). Overlapping holes take
        /// the max, so one hole alone is exactly <see cref="FragmentFade"/>.
        /// </summary>
        public static float CombinedFragmentFade(bool fadesForAllBirds, float fragmentEyeDepth, HoleSample local,
            IReadOnlyList<HoleSample> others)
        {
            float fade = FragmentFade(local.Strength, local.ScreenDist, fragmentEyeDepth, local.BirdEyeDepth);
            if (!fadesForAllBirds || others == null) return fade;
            int n = Mathf.Min(others.Count, MaxBirdSlots);
            for (int i = 0; i < n; i++)
            {
                var h = others[i];
                fade = Mathf.Max(fade, FragmentFade(h.Strength, h.ScreenDist, fragmentEyeDepth, h.BirdEyeDepth));
            }
            return fade;
        }

        // ---- Phase 8 E2: fixed targets (the local player's own pad + 2 home-side piles) ---------------------

        /// <summary>Fixed hole slots, separate from the <see cref="MaxBirdSlots"/> chicken slots so a 4-player match can
        /// never starve a rival's hole for the local player's pad (shader <c>CW_FENCE_MAX_TARGETS</c>; keep equal):
        /// slot 0 = the own deposit pad, slots 1-2 = the home-side piles.</summary>
        public const int MaxFixedTargets = 3;

        /// <summary>How many home-side piles the fixed targets reveal (the rest of <see cref="MaxFixedTargets"/> is the pad).</summary>
        public const int HomePileCount = MaxFixedTargets - 1;

        /// <summary>A fixed target opens while the local chicken is within this many metres of it (XZ, to the target's
        /// centre) even when it carries nothing.</summary>
        public const float FixedTargetNearMetres = 8f;

        /// <summary>Metres added to the target's own radius (pad ring, pile footprint) before it is turned into a hole
        /// radius, so the hole clears the ring / mound edge and not just the disc.</summary>
        public const float FixedTargetMarginMetres = 0.5f;

        /// <summary>Largest hole inner radius a fixed target may ask for (screen heights): keeps an unusually large
        /// pile from opening half the screen.</summary>
        public const float FixedTargetMaxInnerRadius = 0.32f;

        /// <summary>Where on the radius toward the camera the second occlusion probe sits (the near rim is what the
        /// fence hides first; the centre of a pile or of a 5 m pad is often clear while its near half is covered).</summary>
        public const float FixedTargetProbeFraction = 0.85f;

        /// <summary>Probe height of a pile (its mound), metres.</summary>
        public const float PileProbeHeight = 1.1f;

        /// <summary>Probe height of the flat deposit pad, metres.</summary>
        public const float PadProbeHeight = 0.1f;

        /// <summary>Piles the local pad skips before the home-side ones start: the nearest non-centre pile is the personal
        /// doorstep island, deep inside the arena (never behind a fence); the next ones are the two contested piles that sit
        /// beside the pad's own fences.</summary>
        public const int PersonalPilesSkipped = 1;

        /// <summary>
        /// Should a fixed target be open? It must be covered by an OUTER fence (nothing to reveal otherwise) AND the
        /// local chicken must be carrying food, or within <see cref="FixedTargetNearMetres"/> of the target, or the
        /// round must be in GET READY. Eased by the caller toward 1 / 0 at <see cref="FadeSeconds"/>.
        /// </summary>
        public static bool FixedTargetWanted(bool occluded, bool carrying, float distanceToTarget, bool getReady) =>
            occluded && FixedTargetInRange(carrying, distanceToTarget, getReady);

        /// <summary>The cheap half of <see cref="FixedTargetWanted"/>: carrying, GET READY, or within
        /// <see cref="FixedTargetNearMetres"/>. The driver tests this first and only casts the occlusion rays when it holds.</summary>
        public static bool FixedTargetInRange(bool carrying, float distanceToTarget, bool getReady) =>
            carrying || getReady || distanceToTarget <= FixedTargetNearMetres;

        /// <summary>
        /// Picks the home-side piles: of the non-centre piles, ranked by XZ distance from <paramref name="pad"/>, skip the
        /// first <see cref="PersonalPilesSkipped"/> (the personal island) and take the next <paramref name="chosen"/>.Length.
        /// Writes pile indices and returns how many. Ties go to the lower index (the map is 4-fold symmetric, so the two
        /// contested piles tie exactly). No allocation.
        /// </summary>
        public static int PickHomePiles(Vector3 pad, IReadOnlyList<Vector3> piles, IReadOnlyList<bool> isCentre, int[] chosen)
        {
            if (piles == null || chosen == null) return 0;
            int total = Mathf.Min(piles.Count, 32); // a match spawns 9; the mask below covers 32
            uint used = 0;
            int n = 0;
            for (int pass = 0; pass < PersonalPilesSkipped + chosen.Length; pass++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < total; i++)
                {
                    if ((used & (1u << i)) != 0 || (isCentre != null && i < isCentre.Count && isCentre[i])) continue;
                    float dx = piles[i].x - pad.x, dz = piles[i].z - pad.z;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) break;
                used |= 1u << best;
                if (pass >= PersonalPilesSkipped) chosen[n++] = best;
            }
            return n;
        }

        /// <summary>
        /// Is a fixed target (a disc / mound of <paramref name="radius"/> metres whose ground centre is
        /// <paramref name="groundCentre"/>) covered by an outer fence? The same <see cref="IsOccluded"/> ray test as a
        /// chicken, run from the centre and from a second probe on the near rim toward the camera.
        /// </summary>
        public static bool IsTargetOccluded(Vector3 groundCentre, float radius, float height, Vector3 towardCamera,
            float maxDistance, IReadOnlyList<Bounds> occluders)
        {
            if (IsOccluded(groundCentre, height, towardCamera, maxDistance, occluders)) return true;
            var flat = new Vector3(towardCamera.x, 0f, towardCamera.z);
            if (flat.sqrMagnitude < 1e-8f) return false;
            var rim = groundCentre + flat.normalized * (radius * FixedTargetProbeFraction);
            return IsOccluded(rim, height, towardCamera, maxDistance, occluders);
        }

        /// <summary>
        /// A fixed target's hole inner radius as a fraction of the screen height: its radius plus
        /// <see cref="FixedTargetMarginMetres"/> over the view height in metres at the target, never smaller than the
        /// chicken hole (<see cref="InnerRadius"/>) nor larger than <see cref="FixedTargetMaxInnerRadius"/>. The outer
        /// edge and the rim keep their offsets from it, so the edge band is still <see cref="EdgeBandWidth"/>.
        /// </summary>
        public static float TargetInnerRadius(float radiusMetres, float viewHeightMetres)
        {
            if (viewHeightMetres <= 1e-4f) return InnerRadius;
            return Mathf.Clamp((radiusMetres + FixedTargetMarginMetres) / viewHeightMetres, InnerRadius, FixedTargetMaxInnerRadius);
        }

        /// <summary>How far a fixed target's hole is shifted outward from the chicken hole (the shader's per-target
        /// <c>_CW_FenceTargetShift</c>): <see cref="TargetInnerRadius"/> minus <see cref="InnerRadius"/>.</summary>
        public static float TargetRadiusShift(float radiusMetres, float viewHeightMetres) =>
            TargetInnerRadius(radiusMetres, viewHeightMetres) - InnerRadius;

        /// <summary>C# twin of the shader's shifted hole: the chicken hole's shape with every radius (inner, outer, rim)
        /// moved out by <paramref name="radiusShift"/>.</summary>
        public static float ShiftedFragmentFade(float strength, float screenDist, float radiusShift, float fragmentEyeDepth,
            float targetEyeDepth) => FragmentFade(strength, screenDist - radiusShift, fragmentEyeDepth, targetEyeDepth);

        /// <summary>
        /// HoleOnScreen for a fixed target. The hole's outer radius is <see cref="OuterRadius"/> + shift in SCREEN HEIGHTS, so
        /// on the horizontal viewport axis it is that over <paramref name="aspect"/> (width / height); a pad's hole is wide
        /// enough that the chicken-sized margin of <see cref="HoleOnScreen"/> would keep far-away targets (and the fade
        /// variant) alive.
        /// </summary>
        public static bool TargetHoleOnScreen(Vector3 viewport, float radiusShift, float aspect)
        {
            float m = OuterRadius + Mathf.Max(0f, radiusShift);
            float mx = m / Mathf.Max(aspect, 1e-3f);
            return viewport.z > 0f && viewport.x > -mx && viewport.x < 1f + mx && viewport.y > -m && viewport.y < 1f + m;
        }

        /// <summary>
        /// True when a ray from any sample point on the bird toward the camera hits one of
        /// <paramref name="occluders"/> before reaching the camera.
        /// </summary>
        /// <param name="feet">Where the chicken touches the ground, world space.</param>
        /// <param name="bodyHeight">The chicken's height (its capsule's), metres.</param>
        /// <param name="towardCamera">Unit direction from the bird toward the camera (for the orthographic
        /// match camera, minus the camera's forward).</param>
        /// <param name="maxDistance">How far the ray may travel — the camera's distance.</param>
        /// <param name="occluders">World bounds of the fence renderers.</param>
        public static bool IsOccluded(Vector3 feet, float bodyHeight, Vector3 towardCamera, float maxDistance,
            IReadOnlyList<Bounds> occluders)
        {
            if (occluders == null || occluders.Count == 0) return false;
            if (towardCamera.sqrMagnitude < 1e-8f) return false;
            var dir = towardCamera.normalized;

            for (int h = 0; h < OcclusionSampleFractions.Length; h++)
            {
                var ray = new Ray(feet + Vector3.up * (OcclusionSampleFractions[h] * bodyHeight), dir);
                for (int i = 0; i < occluders.Count; i++)
                {
                    // IntersectRay reports 0 for an origin inside the box; a bird cannot stand inside a fence
                    // (it is solid), so counting that as covered is the right answer for a rotated wall's
                    // loose AABB too — the shader's depth gate still only fades what is in front.
                    if (occluders[i].IntersectRay(ray, out float d) && d <= maxDistance) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// C# twin of <c>CW_FenceFadeAmount</c>: 0 keeps the fence fragment, 1 removes it. Distances on screen
        /// are fractions of the screen height; depths are linear eye depths (distance along the view axis).
        /// </summary>
        public static float FragmentFade(float strength, float screenDist, float fragmentEyeDepth, float birdEyeDepth)
        {
            float radial = 1f - Smoothstep(InnerRadius, OuterRadius, screenDist);
            float inFront = Smoothstep(0f, Mathf.Max(DepthSoftness, 1e-4f), birdEyeDepth - fragmentEyeDepth);
            return Mathf.Clamp01(strength) * MaxFade * radial * inFront;
        }

        /// <summary>
        /// C# twin of <c>CW_FenceRimAmount</c>: the 0..1 rim blend for a fence fragment. A band of total width
        /// <see cref="RimWidth"/> centred on <see cref="RimCentre"/>, gated like the fade to fragments in front of
        /// the bird.
        /// </summary>
        public static float RimWeight(float strength, float screenDist, float fragmentEyeDepth, float birdEyeDepth)
        {
            float halfWidth = 0.5f * RimWidth;
            float band = 1f - Smoothstep(0f, Mathf.Max(halfWidth, 1e-5f), Mathf.Abs(screenDist - RimCentre));
            float inFront = Smoothstep(0f, Mathf.Max(DepthSoftness, 1e-4f), birdEyeDepth - fragmentEyeDepth);
            return Mathf.Clamp01(strength) * RimIntensity * band * inFront;
        }

        /// <summary>
        /// The keyword rule: the fade variant is on exactly while <paramref name="strength"/> is above 0.
        /// Returns true only when the keyword must CHANGE (so the driver never re-sets it every frame);
        /// <paramref name="wantOn"/> is the state to set.
        /// </summary>
        public static bool KeywordToggle(bool keywordOn, float strength, out bool wantOn)
        {
            wantOn = strength > 0f;
            return wantOn != keywordOn;
        }

        /// <summary>The shader global <c>_CW_FenceFadeParams</c>.</summary>
        public static Vector4 ShaderParams => new Vector4(InnerRadius, OuterRadius, MaxFade, DepthSoftness);

        /// <summary>The shader global <c>_CW_FenceRimParams</c> (centre, half width, intensity, unused).</summary>
        public static Vector4 RimParams => new Vector4(RimCentre, 0.5f * RimWidth, RimIntensity, 0f);

        /// <summary>HLSL <c>smoothstep</c> (Unity's <c>Mathf.SmoothStep</c> is an interpolator, not this).</summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
