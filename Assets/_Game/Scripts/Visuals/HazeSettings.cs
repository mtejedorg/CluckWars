using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The one definition of the surround haze (Phase 8 C F1): colour, ramp and the world-space rule that says how
    /// much haze a point gets. Both render tiers read it: the PC / console tier through the full-screen
    /// <see cref="CWHazeFeature"/> (shader globals pushed from <see cref="PushShaderGlobals"/> every frame), the Mobile
    /// tier through material copies baked with <see cref="AmountAt"/>. Change a number here and both tiers follow.
    /// </summary>
    /// <remarks>
    /// <para><b>Inside-the-fence rule.</b> The arena is a square, so the distance used is the SQUARE (Chebyshev)
    /// distance from the arena centre, <c>max(|x - cx|, |z - cz|)</c>, not the radial one. A radial ramp starting at
    /// 27 m would haze the fence corners (25.65 * sqrt 2 = 36.3 m from the centre); the square distance of every
    /// point inside the fence, corners included, is at most <see cref="FenceHalfSize"/>, so
    /// <c>RampStart = FenceHalfSize + FenceClearance</c> guarantees zero haze on the whole playfield, the fence and
    /// anything standing on it, and starts the ramp just past the fence's outer face (posts reach 25.73 m) a little
    /// before the hedge strips (inner face ~27.95 m).</para>
    /// <para><b>Why not URP fog:</b> the match camera is orthographic, and URP fog distance is view depth, which on an
    /// ortho camera depends on the screen row only (docs/STATE.md, S3c row). Height is ignored here: haze is a
    /// horizontal-distance effect, so a tall tree gets the haze of its foot.</para>
    /// <para>The ramp is linear (monotone), so the Mobile bake can band it with plain lookups and the two tiers agree.</para>
    /// </remarks>
    public static class HazeSettings
    {
        /// <summary>
        /// Haze colour as an inspector-style (sRGB) colour value: what a material colour field would hold. The project
        /// is in Gamma colour space, where the colour buffer holds these values as-is, so this is also what the haze
        /// blends toward on screen. Tuned by eye on the shipped Gamma build (a darker, greener haze than the 0.60 / 0.64
        /// / 0.55 first documented, which was never what rendered); keep that look if the colour space ever changes
        /// (<see cref="ShaderColourFor"/> converts for Linear).
        /// </summary>
        public static readonly Color Colour = new Color(0.319f, 0.367f, 0.263f, 1f);

        /// <summary>
        /// World XZ centre of the arena. The map is generated and baked around the world origin: pile / base / pinwheel
        /// positions are lerps toward <c>Vector3.zero</c> (<see cref="MapGenerator.PersonalPileNominal"/>) and the
        /// ground plane spans +-<see cref="MapGenerator.ArenaHalfSize"/> around it, so there is no separate centre field.
        /// </summary>
        public static Vector2 ArenaCentre => Vector2.zero;

        /// <summary>Half the fence square's side, read live from <see cref="MapGenerator.ArenaHalfSize"/> (25.65 m).</summary>
        public static float FenceHalfSize => MapGenerator.ArenaHalfSize;

        /// <summary>
        /// Gap between the fence line and the start of the ramp. The fence posts' outer face sits at 25.73 m, so 1.35 m
        /// puts the ramp start at 27.0 m: clear of every fence pixel, just before the hedges.
        /// </summary>
        public const float FenceClearance = 1.35f;

        /// <summary>Square distance from the centre where haze starts (0 at and inside it). 27.0 m today.</summary>
        public static float RampStart => FenceHalfSize + FenceClearance;

        /// <summary>Square distance from the centre where haze reaches <see cref="MaxAmount"/> (60 m).</summary>
        public const float RampEnd = 60f;

        /// <summary>Largest blend toward <see cref="Colour"/>; also applied to background (sky / clear colour) pixels.</summary>
        public const float MaxAmount = 0.6f;

        /// <summary>Square (Chebyshev) horizontal distance of a world position from <see cref="ArenaCentre"/>.</summary>
        public static float SquareDistance(Vector3 worldPos)
        {
            var c = ArenaCentre;
            return Mathf.Max(Mathf.Abs(worldPos.x - c.x), Mathf.Abs(worldPos.z - c.y));
        }

        /// <summary>
        /// Haze amount (0..<see cref="MaxAmount"/>) for a world position: 0 anywhere inside the fence (corners included),
        /// a linear ramp from <see cref="RampStart"/> to <see cref="RampEnd"/>, <see cref="MaxAmount"/> beyond. Height is
        /// ignored. The haze shader evaluates exactly this formula from the constants of <see cref="PushShaderGlobals"/>.
        /// </summary>
        public static float AmountAt(Vector3 worldPos)
        {
            float t = Mathf.InverseLerp(RampStart, RampEnd, SquareDistance(worldPos));
            return MaxAmount * t;
        }

        // Shader global names (CWHaze.shader). One packed vector keeps the pass to two SetGlobal calls per camera.
        public static readonly int ColourId = Shader.PropertyToID("_CW_HazeColor");
        public static readonly int ParamsId = Shader.PropertyToID("_CW_HazeParams");

        /// <summary>
        /// Packed shader parameters: x, y = arena centre XZ; z = ramp start; w = <see cref="MaxAmount"/> / (end - start).
        /// The shader computes <c>min(max(d - z, 0) * w, MaxAmount)</c>, i.e. <see cref="AmountAt"/>; MaxAmount rides in
        /// the colour's alpha.
        /// </summary>
        public static Vector4 ShaderParams
        {
            get
            {
                var c = ArenaCentre;
                return new Vector4(c.x, c.y, RampStart, MaxAmount / (RampEnd - RampStart));
            }
        }

        /// <summary>
        /// Shader colour constant in colour-buffer space, alpha = <see cref="MaxAmount"/>. A Vector4 global gets no
        /// colour-space conversion from Unity, so the conversion is explicit: Linear project -> <c>Colour.linear</c>,
        /// Gamma project -> <see cref="Colour"/> as-is.
        /// </summary>
        public static Vector4 ShaderColour => ShaderColourFor(QualitySettings.activeColorSpace);

        /// <summary>Pure form of <see cref="ShaderColour"/> for a given colour space (tests).</summary>
        public static Vector4 ShaderColourFor(ColorSpace space)
        {
            var c = space == ColorSpace.Linear ? Colour.linear : Colour;
            return new Vector4(c.r, c.g, c.b, MaxAmount);
        }

        /// <summary>Sets the haze shader globals. Called by <see cref="CWHazeFeature"/> each frame it renders.</summary>
        public static void PushShaderGlobals()
        {
            Shader.SetGlobalVector(ColourId, ShaderColour);
            Shader.SetGlobalVector(ParamsId, ShaderParams);
        }
    }
}
