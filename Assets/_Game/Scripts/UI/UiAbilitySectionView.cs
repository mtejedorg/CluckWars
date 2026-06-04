using UnityEngine;
using UnityEngine.UI;

namespace CluckWars.UI
{
    public class UiAbilitySectionView : MonoBehaviour
    {
        [SerializeField] private Text _headerText;
        [SerializeField] private Transform _gridContainer;

        public Transform GridContainer => _gridContainer;

        public void Bind(string categoryName)
        {
            if (_headerText != null)
            {
                _headerText.text = categoryName;
                
                if (categoryName == "DAMAGE") _headerText.color = CharacterSelectController.DtRed;
                else if (categoryName == "CONTROL") _headerText.color = new Color(0.6f,0.3f,0.8f);
                else if (categoryName == "UTILITY") _headerText.color = new Color(0.3f, 0.6f, 0.9f);
                else _headerText.color = UiGfx.GreenTop;
            }
        }
    }
}
