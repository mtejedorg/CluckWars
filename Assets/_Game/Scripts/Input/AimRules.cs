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

    /// <summary>How a provider expresses "where the player wants to aim".</summary>
    public enum AimInputKind : byte
    {
        /// <summary>No aim: the move uses the bird's facing (the pre-Phase-6 behaviour).</summary>
        None = 0,
        /// <summary>A stick direction in the same camera-relative space as <see cref="IInputProvider.GetMovement"/>.</summary>
        Direction = 1,
        /// <summary>A world-space point on the ground (mouse cursor); the direction is derived from the bird.</summary>
        GroundPoint = 2,
    }

    /// <summary>One provider's aim reading for this frame. Devices produce it; only
    /// <see cref="AimRules.Resolve"/> turns it into a world direction.</summary>
    public readonly struct AimInput
    {
        public readonly AimInputKind Kind;
        /// <summary>For <see cref="AimInputKind.Direction"/>: camera-relative stick vector (x right, y up on screen).</summary>
        public readonly Vector2 Direction;
        /// <summary>For <see cref="AimInputKind.GroundPoint"/>: world position under the cursor.</summary>
        public readonly Vector3 Point;

        private AimInput(AimInputKind kind, Vector2 direction, Vector3 point)
        {
            Kind = kind;
            Direction = direction;
            Point = point;
        }

        public static readonly AimInput None = default;
        public static AimInput FromStick(Vector2 stick) => new AimInput(AimInputKind.Direction, stick, default);
        public static AimInput FromGroundPoint(Vector3 point) => new AimInput(AimInputKind.GroundPoint, default, point);
    }

    /// <summary>
    /// Quantises a world-XZ aim direction into the <c>PlayerNetworkInput.Aim</c> byte (Phase 6 chunk 5, A7).
    /// 0 is reserved for "no aim, use facing"; 1..255 are <see cref="Steps"/> equal slices of the circle, measured
    /// clockwise from +Z. The worst-case error is half a step (0.706 degrees).
    /// </summary>
    public static class AimQuantizer
    {
        public const int Steps = 255;
        public const float StepDegrees = 360f / Steps;

        /// <summary>World-XZ direction (x, z) to a byte; a zero vector encodes as 0 (no aim).</summary>
        public static byte Encode(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) return 0;
            float turns = Mathf.Atan2(dir.x, dir.y) / (2f * Mathf.PI);
            turns -= Mathf.Floor(turns); // [0, 1)
            int index = Mathf.RoundToInt(turns * Steps) % Steps;
            return (byte)(index + 1);
        }

        /// <summary>Byte back to a unit world-XZ direction; 0 decodes to <see cref="Vector2.zero"/>.</summary>
        public static Vector2 Decode(byte aim)
        {
            if (aim == 0) return Vector2.zero;
            float radians = (aim - 1) * (2f * Mathf.PI / Steps);
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        /// <summary>What a peer will see for <paramref name="dir"/> after the round trip. The local preview draws this, so
        /// it matches the cast exactly.</summary>
        public static Vector2 Snap(Vector2 dir) => Decode(Encode(dir));
    }

    /// <summary>
    /// Right-stick aim shaping (A7): dead zone 0.25, outer zone 0.95 (the ramp is rescaled so 0.95 reads as full), and
    /// the last aim is held for 150 ms after the stick returns to centre, then drops to none. Time is passed in, so the
    /// filter is a pure function of its inputs plus its own last sample and is idempotent for repeated same-time calls.
    /// </summary>
    public sealed class StickAimFilter
    {
        public const float DeadZone = 0.25f;
        public const float OuterZone = 0.95f;
        public const float HoldSeconds = 0.15f;

        private Vector2 _last;
        private float _lastActiveTime = float.NegativeInfinity;

        /// <summary>The rescaled stick (direction kept, magnitude 0..1), the held aim just after release, or zero.</summary>
        public Vector2 Update(Vector2 raw, float now)
        {
            float mag = raw.magnitude;
            if (mag > DeadZone)
            {
                float scaled = Mathf.Clamp01((mag - DeadZone) / (OuterZone - DeadZone));
                _last = raw / mag * scaled;
                _lastActiveTime = now;
                return _last;
            }

            if (_last != Vector2.zero && now - _lastActiveTime <= HoldSeconds) return _last;
            return Vector2.zero;
        }

        public void Reset()
        {
            _last = Vector2.zero;
            _lastActiveTime = float.NegativeInfinity;
        }
    }

    /// <summary>Pure conversions between a provider's <see cref="AimInput"/> and a world-XZ direction.</summary>
    public static class AimRules
    {
        /// <summary>A cursor closer than this to the bird means "no aim": facing is used (A7).</summary>
        public const float MouseDeadRadius = 0.5f;

        /// <summary>Rotates a camera-relative stick vector into world XZ. The exact maths <c>FusionNetworkService</c>
        /// has always applied to movement, so aim and movement agree.</summary>
        public static Vector2 StickToWorld(Vector2 stick, float cameraYawDegrees)
        {
            float yaw = cameraYawDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(yaw);
            float sin = Mathf.Sin(yaw);
            return new Vector2(stick.x * cos + stick.y * sin, -stick.x * sin + stick.y * cos);
        }

        /// <summary>The unit world-XZ aim direction for <paramref name="aim"/>, or zero for "use facing".</summary>
        public static Vector2 Resolve(AimInput aim, Vector3 birdPosition, float cameraYawDegrees)
        {
            switch (aim.Kind)
            {
                case AimInputKind.Direction:
                {
                    if (aim.Direction.sqrMagnitude < 1e-6f) return Vector2.zero;
                    var world = StickToWorld(aim.Direction, cameraYawDegrees);
                    return world.sqrMagnitude < 1e-6f ? Vector2.zero : world.normalized;
                }
                case AimInputKind.GroundPoint:
                {
                    var offset = new Vector2(aim.Point.x - birdPosition.x, aim.Point.z - birdPosition.z);
                    return offset.magnitude < MouseDeadRadius ? Vector2.zero : offset.normalized;
                }
                default:
                    return Vector2.zero;
            }
        }

        /// <summary>Intersects <paramref name="ray"/> with the horizontal plane y = <paramref name="planeY"/>.</summary>
        public static bool TryRayToGround(Ray ray, float planeY, out Vector3 hit)
        {
            hit = default;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
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
}
