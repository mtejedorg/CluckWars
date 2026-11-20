using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>Which physical device family an <see cref="IInputProvider"/> reads.</summary>
    public enum InputDeviceKind : byte
    {
        None = 0,
        KeyboardMouse = 1,
        Touch = 2,
        Gamepad = 3,
    }

    /// <summary>Pure conversion of a camera-relative stick vector into world XZ.</summary>
    public static class AimRules
    {
        /// <summary>Rotates a camera-relative stick vector into world XZ. The exact maths <c>FusionNetworkService</c>
        /// has always applied to movement, so aim and movement agree.</summary>
        public static Vector2 StickToWorld(Vector2 stick, float cameraYawDegrees)
        {
            float yaw = cameraYawDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(yaw);
            float sin = Mathf.Sin(yaw);
            return new Vector2(stick.x * cos + stick.y * sin, -stick.x * sin + stick.y * cos);
        }
    }

    /// <summary>
    /// Aim magnetism and angle-based picking (A7). Pure: callers pass candidate offsets (rival position minus caster
    /// position, world XZ) that they have already filtered for eligibility.
    /// </summary>
    public static class AimSoftLock
    {
        /// <summary>Touch snaps to a rival within this many degrees of facing.</summary>
        public const float TouchHalfAngleDegrees = 30f;
        /// <summary>Gamepad's lighter magnetism.</summary>
        public const float PadHalfAngleDegrees = 12f;

        /// <summary>Magnetism cone for a device: touch 30, pad 12, keyboard + mouse none.</summary>
        public static float HalfAngleFor(bool touch, bool pad) =>
            touch ? TouchHalfAngleDegrees : pad ? PadHalfAngleDegrees : 0f;

        public static float HalfAngleFor(InputDeviceKind device) =>
            HalfAngleFor(device == InputDeviceKind.Touch, device == InputDeviceKind.Gamepad);

        /// <summary>Index of the offset with the smallest angle to <paramref name="aim"/> within
        /// <paramref name="maxAngleDegrees"/>, or -1. Ties keep the lowest index (callers pass nearest-first). Offsets of
        /// zero length are skipped. With 180 as the limit this is a plain "smallest angle" pick.</summary>
        public static int PickClosestAngle(Vector2 aim, Vector2[] offsets, int count, float maxAngleDegrees)
        {
            if (offsets == null || aim.sqrMagnitude < 1e-8f) return -1;
            int best = -1;
            float bestAngle = float.MaxValue;
            int n = Mathf.Min(count, offsets.Length);
            for (int i = 0; i < n; i++)
            {
                if (offsets[i].sqrMagnitude < 1e-8f) continue;
                float angle = Vector2.Angle(aim, offsets[i]);
                if (angle > maxAngleDegrees || angle >= bestAngle) continue;
                best = i;
                bestAngle = angle;
            }
            return best;
        }
    }

    /// <summary>
    /// Pure rule for what counts as "the keyboard is in use" (Phase 7 chunk A, round-4 finding 6).
    /// </summary>
    public static class DesktopInputRules
    {
        /// <summary>
        /// Whether the keys currently down mean the player is on a keyboard. On a phone the system Back button arrives as
        /// Escape and must not flip the HUD to the desktop layout, so a lone Escape is ignored there.
        /// </summary>
        public static bool KeyDownIsActivity(bool anyKeyDown, bool escapeDown, int pressedKeyCount, bool mobilePlatform) =>
            anyKeyDown && !(mobilePlatform && escapeDown && pressedKeyCount <= 1);
    }
}
