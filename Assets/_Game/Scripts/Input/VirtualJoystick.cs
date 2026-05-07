using UnityEngine;
using UnityEngine.EventSystems;

namespace CluckWars.Input
{
    /// <summary>
    /// Touch / mouse-driven virtual joystick. Reports a normalized 2D vector in
    /// <see cref="Value"/> (range [-1,1] per axis). Drag the knob inside the base;
    /// it snaps back to center on release.
    /// </summary>
    /// <remarks>
    /// Built on UGUI EventSystem callbacks so it works for both touch and mouse
    /// (handy for testing on Windows). The component itself is the joystick
    /// "base"; the knob is a child <see cref="RectTransform"/> wired in the
    /// inspector or set programmatically by <c>TouchControlsHud</c>.
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Tooltip("Knob transform. If null, joystick still tracks input but no visual feedback.")]
        [SerializeField] private RectTransform _knob;

        [Tooltip("Maximum knob travel (px) from center, in canvas units. Should match the base radius.")]
        [Min(1f)]
        [SerializeField] private float _maxRadius = 80f;

        public Vector2 Value { get; private set; }

        private RectTransform _baseRT;

        public void SetKnob(RectTransform knob) => _knob = knob;
        public void SetMaxRadius(float radius) => _maxRadius = Mathf.Max(1f, radius);

        private void Awake()
        {
            _baseRT = GetComponent<RectTransform>();
        }

        public void OnPointerDown(PointerEventData eventData) => UpdateFromPointer(eventData);
        public void OnDrag(PointerEventData eventData) => UpdateFromPointer(eventData);

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
        }

        private void OnDisable()
        {
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
        }

        private void UpdateFromPointer(PointerEventData eventData)
        {
            if (_baseRT == null) return;

            // Convert screen pointer to local space relative to the joystick base center.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _baseRT, eventData.position, eventData.pressEventCamera, out var local))
            {
                return;
            }

            var clamped = Vector2.ClampMagnitude(local, _maxRadius);
            if (_knob != null) _knob.anchoredPosition = clamped;
            Value = clamped / _maxRadius;
        }
    }
}
