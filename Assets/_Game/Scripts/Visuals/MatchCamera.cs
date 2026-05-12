using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Fixed isometric camera setup per ART.md §2: orthographic, 45° yaw + 30°
    /// pitch, framed to the full map. No follow, no zoom — every chicken on
    /// screen at all times. Configure tunables in the inspector; the component
    /// sets the <see cref="Camera"/> up on <c>Awake</c> and is otherwise idle.
    /// </summary>
    /// <remarks>
    /// Lives on the Main Camera GameObject in Game.unity. If <c>_orthoSize</c>
    /// doesn't match your map, eyeball it: a 30×30 plane at the origin frames
    /// nicely around 18–20.
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
        [Tooltip("World-space point the camera looks at. Origin by default.")]
        [SerializeField] private Vector3 _focusPoint = Vector3.zero;

        [Tooltip("How far back to pull the camera. Doesn't affect ortho framing — just keeps the near clip off the geometry.")]
        [Min(1f)]
        [SerializeField] private float _distance = 30f;

        [Tooltip("Half the vertical view height in world units. Increase if the map clips off the bottom / top.")]
        [Min(1f)]
        [SerializeField] private float _orthoSize = 18f;

        [Header("Background")]
        [Tooltip("Solid background color when no skybox is in use.")]
        [SerializeField] private Color _backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);

        [Tooltip("If true, force solid color clear over whatever the scene was configured with.")]
        [SerializeField] private bool _useSolidColor = true;

        private void Awake()
        {
            Apply();
        }

#if UNITY_EDITOR
        // Live-preview in the editor when tweaking tunables.
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            Apply();
        }
#endif

        private void Apply()
        {
            var cam = GetComponent<Camera>();
            if (cam == null) return;

            cam.orthographic = true;
            cam.orthographicSize = _orthoSize;
            if (_useSolidColor)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = _backgroundColor;
            }

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var dir = rot * Vector3.forward;
            transform.SetPositionAndRotation(_focusPoint - dir * _distance, rot);
        }
    }
}
