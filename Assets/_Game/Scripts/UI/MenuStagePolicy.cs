using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// The pure decisions behind the menu's live 3D chicken stage (<see cref="MenuChickenStage"/>),
    /// kept out of the MonoBehaviour world so they are unit tested: live vs static, whether a slot's
    /// camera renders this frame, how big its RenderTexture is, and the motion curves.
    /// </summary>
    public static class MenuStagePolicy
    {
        /// <summary>Largest RenderTexture side the stage allocates, in pixels.</summary>
        public const int MaxTextureSide = 1024;

        /// <summary>Smallest side worth rendering; below this the element is not laid out yet.</summary>
        public const int MinTextureSide = 64;

        /// <summary>Turntable speed (degrees per second): one turn every 24 s.</summary>
        public const float TurntableDegreesPerSecond = 15f;

        /// <summary>Rest yaw: the 3/4 view the static hero renders were taken at.</summary>
        public const float RestYaw = -28f;

        /// <summary>Length of the select hop, seconds.</summary>
        public const float HopDuration = 0.55f;

        /// <summary>
        /// Live chickens only when Performance Mode is OFF and the stage came up. Anything else
        /// shows the static Phase 2 hero renders (also the fallback when the stage failed).
        /// </summary>
        public static bool WantsLive(bool performanceMode, bool initFailed) => !performanceMode && !initFailed;

        /// <summary>
        /// A slot's camera renders only while the stage is live, the slot holds a chicken, its page
        /// is the one on screen and its element has been laid out at a renderable size.
        /// </summary>
        public static bool ShouldRender(bool live, bool hasChicken, bool pageVisible, int textureSide) =>
            live && hasChicken && pageVisible && textureSide >= MinTextureSide;

        /// <summary>
        /// Square texture side for an element of <paramref name="width"/> x <paramref name="height"/>
        /// panel points. Square because the static renders are square and drawn scale-to-fit: the
        /// live texture then lands exactly where the PNG did. Returns 0 when the element is not laid
        /// out (NaN / too small), otherwise the shorter side in pixels clamped to
        /// [<see cref="MinTextureSide"/>, <paramref name="cap"/>].
        /// </summary>
        public static int TextureSide(float width, float height, float pixelsPerPoint, int cap = MaxTextureSide)
        {
            if (float.IsNaN(width) || float.IsNaN(height) || float.IsNaN(pixelsPerPoint) || pixelsPerPoint <= 0f)
                return 0;
            float px = Mathf.Min(width, height) * pixelsPerPoint;
            if (px < MinTextureSide) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(px), MinTextureSide, Mathf.Max(MinTextureSide, cap));
        }

        /// <summary>
        /// Reallocate only when the wanted size moved by more than ~10% (or to/from zero): layout
        /// jitter during page transitions must not churn RenderTextures every frame.
        /// </summary>
        public static bool NeedsRealloc(int current, int wanted)
        {
            if (wanted <= 0) return false;
            if (current <= 0) return true;
            return Mathf.Abs(wanted - current) > current * 0.1f;
        }

        /// <summary>
        /// Model yaw at <paramref name="time"/> seconds. <paramref name="sway"/> slots (the lineup)
        /// rock +-25 degrees around the 3/4 view instead of turning all the way round, so four
        /// chickens never show the player their backs at once. Reduced Motion parks every slot at
        /// <see cref="RestYaw"/>.
        /// </summary>
        public static float Yaw(float time, float phase, bool sway, bool reducedMotion)
        {
            if (reducedMotion) return RestYaw;
            if (sway) return RestYaw + 25f * Mathf.Sin((time + phase) * 0.45f);
            return Mathf.Repeat(RestYaw + (time + phase) * TurntableDegreesPerSecond + 180f, 360f) - 180f;
        }

        /// <summary>
        /// Hop height as a fraction of the peak, for <paramref name="t"/> seconds since the hop
        /// started: one parabola over <see cref="HopDuration"/>, 0 before and after.
        /// </summary>
        public static float HopHeight(float t)
        {
            if (t <= 0f || t >= HopDuration) return 0f;
            float u = t / HopDuration;
            return 4f * u * (1f - u);
        }
    }
}
