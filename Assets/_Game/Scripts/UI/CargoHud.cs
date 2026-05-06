using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// Phase-4 placeholder cargo HUD. IMGUI overlay showing the local player's carried
    /// food and the running deposit total of the first <see cref="PlayerBase"/> in scene.
    /// Lives in <c>Game.unity</c> as a single GameObject with this script.
    /// </summary>
    /// <remarks>
    /// The real UGUI HUD ships in Phase 7 alongside the proper menu pass.
    /// </remarks>
    public sealed class CargoHud : MonoBehaviour
    {
        [SerializeField] private float _refreshInterval = 0.5f;

        private ChickenCargo _localCargo;
        private ChickenController _localController;
        private PlayerBase _firstBase;
        private float _nextRefresh;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh && _localCargo != null && _firstBase != null) return;
            _nextRefresh = Time.unscaledTime + _refreshInterval;

            if (_localCargo == null || _localController == null || !_localController.Object || !_localController.Object.IsValid)
            {
                FindLocalChicken();
            }
            if (_firstBase == null || !_firstBase.Object || !_firstBase.Object.IsValid)
            {
                _firstBase = FindFirstObjectByType<PlayerBase>();
            }
        }

        private void FindLocalChicken()
        {
            _localCargo = null;
            _localController = null;

            var all = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var c in all)
            {
                if (c.Object == null || !c.Object.IsValid) continue;
                if (!c.HasInputAuthority) continue;
                _localController = c;
                _localCargo = c.Cargo;
                break;
            }
        }

        private void OnGUI()
        {
            const int pad = 16;
            const int width = 260;
            int x = Screen.width - width - pad;
            int y = pad;

            GUI.Box(new Rect(x, y, width, 110), "Cluck Wars — Match");

            string cargoText = "(no chicken)";
            if (_localCargo != null && _localController != null)
            {
                float capacity = _localCargo.Capacity;
                cargoText = capacity > 0f
                    ? $"Cargo: {Mathf.FloorToInt(_localCargo.Cargo)} / {Mathf.FloorToInt(capacity)}"
                    : $"Cargo: {Mathf.FloorToInt(_localCargo.Cargo)}";
            }
            GUI.Label(new Rect(x + 12, y + 28, width - 24, 22), cargoText);

            string baseText = _firstBase != null
                ? $"Base total: {Mathf.FloorToInt(_firstBase.FoodTotal)}"
                : "Base total: (no base in scene)";
            GUI.Label(new Rect(x + 12, y + 50, width - 24, 22), baseText);

            string stunText = _localController != null && _localController.Combat != null && _localController.Combat.IsStunned
                ? "STUNNED"
                : string.Empty;
            if (!string.IsNullOrEmpty(stunText))
                GUI.Label(new Rect(x + 12, y + 76, width - 24, 22), stunText);
        }
    }
}
