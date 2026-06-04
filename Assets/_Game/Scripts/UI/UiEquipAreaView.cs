using UnityEngine;
using UnityEngine.UI;

namespace CluckWars.UI
{
    public class UiEquipAreaView : MonoBehaviour
    {
        [SerializeField] private Text _headerText;
        [SerializeField] private Transform _slotsContainer;

        public Transform SlotsContainer => _slotsContainer;

        public void SetEquippedCount(int count, int max)
        {
            if (_headerText != null)
                _headerText.text = $"EQUIPPED - {count}/{max}";
        }
    }
}
