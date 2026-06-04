using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using CluckWars.Gameplay;

namespace CluckWars.UI
{
    public class UiClassCardView : MonoBehaviour
    {
        [SerializeField] private Image _bgPanel;
        [SerializeField] private Image _avatarImage;
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _roleText;
        [SerializeField] private Text _badgeText;
        [SerializeField] private Button _button;

        public ChickenClass ClassType { get; private set; }

        private void Awake()
        {
            if (_bgPanel != null && _bgPanel.material != null) 
                _bgPanel.material = new Material(_bgPanel.material);
        }

        public void Bind(ChickenClass cls, string roleName, UnityAction onClick)
        {
            ClassType = cls;
            
            if (_avatarImage != null)
                _avatarImage.sprite = UiGfx.Chicken(cls.ToString().ToLowerInvariant());
                
            if (_nameText != null)
                _nameText.text = cls.ToString();
            
            if (_roleText != null)
                _roleText.text = roleName; 

            if (_badgeText != null)
                _badgeText.text = (cls == ChickenClass.Assassin) ? "x3" : "x2";

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                if (onClick != null) _button.onClick.AddListener(onClick);
            }
        }

        public void SetSelected(bool selected)
        {
            if (_bgPanel != null && _bgPanel.material != null)
                _bgPanel.material.SetColor("_BorderColor", selected ? CharacterSelectController.DtGold : UiGfx.CardBorder);
            
            if (_nameText != null)
                _nameText.color = selected ? CharacterSelectController.DtGold : CharacterSelectController.DtTextPrimary;
        }
    }
}
