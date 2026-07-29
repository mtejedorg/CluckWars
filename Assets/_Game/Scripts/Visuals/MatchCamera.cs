using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Isometric camera (45° yaw + 30° pitch, orthographic) that smooth-follows
    /// the local chicken. Closer framing than the original ART.md §2 "fixed
    /// full-map" spec — chickens read clearly without squinting. Set
    /// <c>_followLocalChicken</c> to <c>false</c> if you want the old
    /// fixed-camera behavior; it falls back to <c>_focusPoint</c> at the map
    /// center.
    /// </summary>
    /// <remarks>
    /// Lives on the Main Camera GameObject in Game.unity. Tracks the chicken
    /// whose <c>HasInputAuthority</c> is true; falls back to the configured
    /// focus point during intro / between matches / after despawn.
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class MatchCamera : MonoBehaviour
    {
        [Header("Angle")]
        [Tooltip("Yaw — rotation around the Y axis. 45° gives the canonical isometric view.")]
        [Range(0f, 360f)]
        [SerializeField] private float _yaw = 45f;

        [Tooltip("Pitch — downward tilt from horizontal. 30° per ART.md §2.")]
        [Range(0f, 90f)]
        [SerializeField] private float _pitch = 30f;

        [Header("Framing")]
        [Tooltip("Fallback world-space point the camera looks at when no local chicken is found (intro / between matches).")]
        [SerializeField] private Vector3 _focusPoint = Vector3.zero;

        [Tooltip("How far back to pull the camera. Doesn't affect ortho framing — just keeps the near clip off the geometry.")]
        [Min(1f)]
        [SerializeField] private float _distance = 30f;

        [Tooltip("Half the vertical view height in world units. Smaller = more zoomed in. 5.6 per GDD v0.5.")]
        [Min(1f)]
        [SerializeField] private float _orthoSize = 5.6f;

        [Header("Follow")]
        [Tooltip("If true, the camera smooth-follows whichever chicken has HasInputAuthority on this peer.")]
        [SerializeField] private bool _followLocalChicken = true;

        [Tooltip("SmoothDamp time-to-target on the focus point. Smaller = snappier, bigger = floatier. 0.15s is a good arena-game feel.")]
        [Min(0f)]
        [SerializeField] private float _followSmoothTime = 0.15f;

        [Tooltip("Seconds between scans for the local chicken when we don't have a cached reference. Cheap; 0.5s avoids hammering FindObjectsByType.")]
        [Min(0.1f)]
        [SerializeField] private float _localChickenSearchInterval = 0.5f;

        [Header("Background")]
        [Tooltip("Solid background color when no skybox is in use.")]
        [SerializeField] private Color _backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);

        [Tooltip("If true, force solid color clear over whatever the scene was configured with.")]
        [SerializeField] private bool _useSolidColor = true;

        // Runtime state
        private Camera _camera;
        private ChickenController _localChicken;
        private Vector3 _currentFocus;
        private Vector3 _focusVelocity;
        private float _nextLocalChickenSearchTime;

        // Screen shake
        private float _shakeRemaining;
        private float _shakeDuration;
        private float _shakePeak;

        /// <summary>Current camera yaw angle in degrees.</summary>
        public float CurrentYaw => _yaw;

        /// <summary>
        /// Scene-wide singleton set in <c>Awake</c>. Abilities and combat use this
        /// to trigger positional impulses without needing a direct reference.
        /// </summary>
        public static MatchCamera Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            _camera = GetComponent<Camera>();
            _currentFocus = _focusPoint;
            ApplyCamera();
            ApplyTransform(_currentFocus);
        }

#if UNITY_EDITOR
        // Live-preview in the editor when tweaking tunables.
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            _camera = GetComponent<Camera>();
            _currentFocus = _focusPoint;
            ApplyCamera();
            ApplyTransform(_currentFocus);
        }
#endif

        private void LateUpdate()
        {
            // Camera config can change in the inspector at runtime (orthoSize,
            // background) — cheap to push every frame.
            ApplyCamera();

            // Resolve target — local chicken position if follow is on + the
            // chicken has spawned, otherwise the configured focus point.
            var target = _followLocalChicken
                ? ResolveLocalChickenPosition()
                : _focusPoint;

            // Per-player corner rotation (GDD v0.5 Section 3.1 & Phase 4):
            // Rotate camera by local chicken's HomeCornerIndex so home corner sits at bottom of screen.
            if (_localChicken != null && _localChicken.HomeCornerIndex >= 0 && _localChicken.HomeCornerIndex < 4)
            {
                int cornerIdx = _localChicken.HomeCornerIndex;
                Vector2 cornerPos = cornerIdx switch
                {
                    0 => new Vector2(19f, 19f),
                    1 => new Vector2(-19f, 19f),
                    2 => new Vector2(-19f, -19f),
                    3 => new Vector2(19f, -19f),
                    _ => Vector2.zero,
                };
                if (cornerPos != Vector2.zero)
                {
                    // Bearing from base toward centre (-c.x, -c.z)
                    float targetYaw = Mathf.Atan2(-cornerPos.x, -cornerPos.y) * Mathf.Rad2Deg;
                    if (targetYaw < 0f) targetYaw += 360f;
                    _yaw = Mathf.LerpAngle(_yaw, targetYaw, Time.deltaTime * 5f);
                }
            }

            _currentFocus = Vector3.SmoothDamp(_currentFocus, target, ref _focusVelocity, _followSmoothTime);
            ApplyTransform(_currentFocus);

            // Screen shake — positional impulse that decays linearly to zero
            // over the requested duration. Applied after ApplyTransform so it's
            // additive to the follow position, not smoothed out by SmoothDamp.
            if (_shakeRemaining > 0f)
            {
                float t   = _shakeDuration > 0f ? _shakeRemaining / _shakeDuration : 0f;
                float mag = _shakePeak * t;
                _shakeRemaining = Mathf.Max(0f, _shakeRemaining - Time.deltaTime);
                if (_shakeRemaining <= 0f) _shakePeak = 0f;

                transform.position += new Vector3(
                    (Random.value * 2f - 1f) * mag,
                    (Random.value * 2f - 1f) * mag * 0.3f,  // less vertical jitter
                    (Random.value * 2f - 1f) * mag);
            }
        }

        /// <summary>
        /// Trigger a positional shake impulse on the camera. If a stronger or longer
        /// shake is already running it wins; otherwise the new values override.
        /// Safe to call from any peer — guards internally so only the local camera shakes.
        /// </summary>
        public void ApplyShake(float magnitude, float duration)
        {
            if (magnitude > _shakePeak)
            {
                _shakePeak      = magnitude;
                _shakeDuration  = duration;
                _shakeRemaining = duration;
            }
            else if (_shakeRemaining < duration)
            {
                _shakeRemaining = duration;
            }
        }

        private Vector3 ResolveLocalChickenPosition()
        {
            // Lost / never had reference? Throttled re-scan for the local
            // chicken. ChickenController.HasInputAuthority disambiguates from
            // remote-player chickens that also live in the scene.
            if (_localChicken == null || _localChicken.Object == null || !_localChicken.Object.IsValid)
            {
                if (Time.unscaledTime >= _nextLocalChickenSearchTime)
                {
                    _nextLocalChickenSearchTime = Time.unscaledTime + _localChickenSearchInterval;
                    _localChicken = FindLocalChicken();
                }
            }

            return _localChicken != null ? _localChicken.transform.position : _focusPoint;
        }

        private static ChickenController FindLocalChicken()
        {
            var all = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                if (c.HasInputAuthority) return c;
            }
            return null;
        }

        private void ApplyCamera()
        {
            if (_camera == null) return;
            _camera.orthographic = true;
            _camera.orthographicSize = _orthoSize;
            if (_useSolidColor)
            {
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = _backgroundColor;
            }
        }

        private void ApplyTransform(Vector3 focus)
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var dir = rot * Vector3.forward;
            transform.SetPositionAndRotation(focus - dir * _distance, rot);
        }
    }
}
