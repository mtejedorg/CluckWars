using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CluckWars.Input
{
    /// <summary>
    /// Press-and-hold touch / mouse button. Exposes <see cref="IsHeld"/> for
    /// continuous inputs (the attack mash), and <see cref="WasPressedThisFrame"/>
    /// for edge-triggered inputs (ability activations) — clears one frame after
    /// the press, mirroring Unity Input System's <c>wasPressedThisFrame</c>.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool IsHeld { get; private set; }
        public bool WasPressedThisFrame { get; private set; }

        [Tooltip("Optional Image whose color is brightened while the button is held.")]
        [SerializeField] private Image _targetImage;
        [SerializeField] private Color _normalColor   = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color _pressedColor  = new Color(1f, 0.7f, 0.2f, 0.85f);

        public void SetVisuals(Image targetImage, Color normalColor, Color pressedColor)
        {
            _targetImage = targetImage;
            _normalColor = normalColor;
            _pressedColor = pressedColor;
            ApplyColor();
        }

        private void OnEnable() => ApplyColor();

        public void OnPointerDown(PointerEventData eventData)
        {
            IsHeld = true;
            WasPressedThisFrame = true;
            ApplyColor();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsHeld = false;
            ApplyColor();
        }

        private void OnDisable()
        {
            IsHeld = false;
            WasPressedThisFrame = false;
            ApplyColor();
        }

        private void LateUpdate()
        {
            // Clear the edge after the frame so callers reading in Update see it
            // exactly once. Matches Unity Input System's wasPressedThisFrame timing.
            WasPressedThisFrame = false;
        }

        private void ApplyColor()
        {
            if (_targetImage != null)
                _targetImage.color = IsHeld ? _pressedColor : _normalColor;
        }
    }
}
