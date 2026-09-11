using CluckWars.Gameplay;
using CluckWars.Settings;
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

        /// <summary>
        /// The match <see cref="UnityEngine.Camera"/>, already cached. Exposed so per-frame
        /// consumers (<see cref="RivalIndicator"/>) do not each repeat the
        /// <c>GetComponent&lt;Camera&gt;()</c> this class has done once in <c>Awake</c> —
        /// that lookup was running once per chicken per frame against a 30fps mid-range
        /// Android budget.
        /// </summary>
        public Camera Camera => _camera;

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

                // Only the BEARING from this corner toward the centre is used below, and a
                // bearing is scale-invariant — so these are unit directions rather than real
                // corner coordinates. They used to be literal (±19, ±19), which silently
                // encoded the arena's half-size in the camera and would have gone stale the
                // moment the arena was resized (it was, x1.35, on 2026-08-14). Signs only.
                Vector2 cornerDir = cornerIdx switch
                {
                    0 => new Vector2(+1f, +1f),
                    1 => new Vector2(-1f, +1f),
                    2 => new Vector2(-1f, -1f),
                    3 => new Vector2(+1f, -1f),
                    _ => Vector2.zero,
                };
                if (cornerDir != Vector2.zero)
                {
                    // Bearing from base toward centre (-c.x, -c.z)
                    float targetYaw = Mathf.Atan2(-cornerDir.x, -cornerDir.y) * Mathf.Rad2Deg;
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
        /// No-ops entirely when the player has turned on
        /// <see cref="PlayerPreferences.ReducedMotionEnabled"/>.
        /// </summary>
        /// <remarks>
        /// <b>Full suppression, not attenuation.</b> A scaled-down shake is still shake: the
        /// vestibular response this preference exists to avoid is triggered by unrequested
        /// camera movement, not by its amplitude, so "quieter" would keep the symptom and only
        /// remove the effect. Attenuating would also be dishonest about what the toggle does,
        /// and would leave a magnitude for a future re-tune of <c>FeedbackTuning</c> to creep
        /// back up.
        ///
        /// <b>Nothing gameplay-critical is lost.</b> Every shake in the game is a redundant
        /// emphasis layer over a cue that is already carried some other way, and none is the
        /// sole signal for anything: the death shake (<c>ChickenCombat</c>) rides on the stun
        /// animation, the stun SFX and the replicated <c>IsRemoved</c> state; the caster
        /// micro-shake (<c>ChickenVFX</c>) rides on the ability particle burst in the ability's
        /// accent colour; the hit-confirm shakes (<c>HitFeedback</c>) ride on the hit spark,
        /// the hit flash and the floating damage text. A player with this on is told everything
        /// a player with it off is told.
        ///
        /// Gated HERE rather than at the call sites on purpose. This is the one chokepoint all
        /// three of those pass through, so a shake added later is covered by default instead of
        /// by remembering — the failure mode of per-call-site gating is a new shake that quietly
        /// ignores the preference. The read is a cached static field after its first touch, so
        /// it costs nothing at the per-cast rate this is called at.
        /// </remarks>
        public void ApplyShake(float magnitude, float duration)
        {
            if (PlayerPreferences.ReducedMotionEnabled) return;

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
            // chicken. HasInputAuthority disambiguates from remote-player chickens
            // that also live in the scene; !IsDecoy disambiguates from this player's
            // own Doppelganger decoy, which shares that same InputAuthority.
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
                // !IsDecoy as well: a Doppelganger decoy shares its caster's InputAuthority,
                // so it is an equally valid answer here — and FindObjectsByType is unordered,
                // so it can win. Once latched, the rescan above only re-runs when the
                // reference goes null, which meant the camera followed the decoy for its
                // whole lifetime instead of the player.
                if (c.HasInputAuthority && !c.IsDecoy) return c;
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
