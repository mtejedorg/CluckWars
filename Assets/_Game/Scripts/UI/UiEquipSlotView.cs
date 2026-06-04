using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using CluckWars.Abilities;

namespace CluckWars.UI
{
    public class UiEquipSlotView : MonoBehaviour
    {
        [SerializeField] private Image _borderHexagon;
        [SerializeField] private Text _iconText;
        [SerializeField] private Text _label;
        [SerializeField] private Button _button;
        
        public int SlotIndex { get; private set; }

        private void Awake()
        {
            if (_borderHexagon != null && _borderHexagon.material != null) 
                _borderHexagon.material = new Material(_borderHexagon.material);
        }

        public void Bind(int slotIndex, AbilityBaseSO ability, bool isLocked, UnityAction onClick)
        {
            SlotIndex = slotIndex;
            if (_label != null) _label.text = "S" + (slotIndex + 1);

            if (_iconText != null)
            {
                if (isLocked)
                {
                    _iconText.text = "\U0001F512"; // Lock
                    _iconText.color = UiGfx.CardBorder;
                }
                else if (ability != null)
                {
                    _iconText.text = ability.ResolveIcon();
                    _iconText.color = ability.AccentColor;
                    if (UiGfx.TmpEmojiFont() != null) _iconText.font = UiGfx.TmpEmojiFont().sourceFontFile;
                }
                else
                {
                    _iconText.text = "+";
                    _iconText.color = UiGfx.CardBorder;
                }
            }

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                if (onClick != null) _button.onClick.AddListener(onClick);
            }
        }

        public void SetSelected(bool selected)
        {
            if (_borderHexagon != null && _borderHexagon.material != null)
            {
                _borderHexagon.material.SetColor("_BorderColor", selected ? CharacterSelectController.DtGold : new Color(0.4f, 0.35f, 0.3f));
                _borderHexagon.material.SetFloat("_BorderWidth", selected ? 4 : 3);
            }
        }
    }
}
