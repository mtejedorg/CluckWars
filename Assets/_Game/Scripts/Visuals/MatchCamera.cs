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

        [Tooltip("Half the vertical view height in world units. Smaller = more zoomed in. 8 reads cleanly around a single chicken; bump to ~18 for full-map view.")]
        [Min(1f)]
        [SerializeField] private float _orthoSize = 8f;

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

        private void Awake()
        {
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

            _currentFocus = Vector3.SmoothDamp(_currentFocus, target, ref _focusVelocity, _followSmoothTime);
            ApplyTransform(_currentFocus);
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
