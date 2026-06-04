using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using CluckWars.Abilities;

namespace CluckWars.UI
{
    public class UiAbilityCardView : MonoBehaviour
    {
        [SerializeField] private Image _bgPanel;
        [SerializeField] private TextMeshProUGUI _iconText;
        [SerializeField] private Text _nameText;
        [SerializeField] private Image _badgeBg;
        [SerializeField] private Text _badgeText;
        [SerializeField] private Button _button;

        private void Awake()
        {
            if (_bgPanel != null && _bgPanel.material != null) 
                _bgPanel.material = new Material(_bgPanel.material);
        }

        public void Bind(AbilityBaseSO ab, UnityAction onClick)
        {
            if (_bgPanel != null && _bgPanel.material != null)
                _bgPanel.material.SetColor("_BorderColor", ab.AccentColor);
            
            if (_iconText != null)
            {
                _iconText.text = ab.ResolveIcon();
                _iconText.color = ab.AccentColor;
            }
            
            if (_nameText != null)
            {
                _nameText.text = ab.DisplayName.ToUpper();
                _nameText.color = ab.AccentColor;
            }
            
            if (_badgeBg != null)
            {
                var bdgCol = new Color(ab.AccentColor.r * 0.8f, ab.AccentColor.g * 0.8f, ab.AccentColor.b * 0.8f);
                _badgeBg.color = bdgCol;
            }
            
            if (_badgeText != null)
            {
                _badgeText.text = ab.Duration > 2.0f ? "LONG" : (ab.Duration < 1.0f ? "SHORT" : "MED");
            }
            
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                if (onClick != null) _button.onClick.AddListener(onClick);
            }
        }
    }
}
