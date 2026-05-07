using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// IMGUI placeholder match HUD. Right-side panel: local cargo + per-base food
    /// totals + STUNNED indicator. Top center: match timer. Center overlay when
    /// the match ends: winner banner.
    /// </summary>
    /// <remarks>
    /// Despite the name, this also owns the timer / winner overlays added in
    /// Phase 7. Renaming to <c>MatchHud</c> is queued for the Phase 7+ polish
    /// pass — would otherwise force a scene-wiring update on every developer.
    /// The full UGUI HUD (per ART.md §6.1: top bar with timer + 4 player totals,
    /// bottom bar with HP / cargo / abilities) lands later in Phase 9 polish.
    /// </remarks>
    public sealed class CargoHud : MonoBehaviour
    {
        [SerializeField] private float _refreshInterval = 0.5f;

        private ChickenCargo _localCargo;
        private ChickenController _localController;
        private PlayerBase[] _bases = System.Array.Empty<PlayerBase>();
        private GameManager _gameManager;
        private float _nextRefresh;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh
                && _localCargo != null
                && _bases.Length > 0
                && _gameManager != null) return;
            _nextRefresh = Time.unscaledTime + _refreshInterval;

            if (_localCargo == null || _localController == null || !_localController.Object || !_localController.Object.IsValid)
            {
                FindLocalChicken();
            }
            if (_bases.Length == 0 || HasStaleBase())
            {
                _bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }
            if (_gameManager == null || !_gameManager.Object || !_gameManager.Object.IsValid)
            {
                _gameManager = FindFirstObjectByType<GameManager>();
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

        private bool HasStaleBase()
        {
            for (int i = 0; i < _bases.Length; i++)
            {
                var b = _bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) return true;
            }
            return false;
        }

        private void OnGUI()
        {
            DrawCargoPanel();
            DrawTopBar();
            DrawEndOverlay();
        }

        private void DrawCargoPanel()
        {
            const int pad = 16;
            const int width = 260;
            int x = Screen.width - width - pad;
            int y = pad;

            GUI.Box(new Rect(x, y, width, 130), "Cluck Wars — Match");

            string cargoText = "(no chicken)";
            if (_localCargo != null && _localController != null)
            {
                float capacity = _localCargo.Capacity;
                cargoText = capacity > 0f
                    ? $"Cargo: {Mathf.FloorToInt(_localCargo.Cargo)} / {Mathf.FloorToInt(capacity)}"
                    : $"Cargo: {Mathf.FloorToInt(_localCargo.Cargo)}";
            }
            GUI.Label(new Rect(x + 12, y + 28, width - 24, 22), cargoText);

            string totals = BuildBaseTotalsText();
            GUI.Label(new Rect(x + 12, y + 50, width - 24, 22), totals);

            int target = _gameManager != null ? _gameManager.FoodTargetToWin : 0;
            if (target > 0)
                GUI.Label(new Rect(x + 12, y + 72, width - 24, 22), $"Target: {target}");

            string stunText = _localController != null
                && _localController.Combat != null
                && _localController.Combat.IsStunned
                ? "STUNNED"
                : string.Empty;
            if (!string.IsNullOrEmpty(stunText))
                GUI.Label(new Rect(x + 12, y + 96, width - 24, 22), stunText);
        }

        private string BuildBaseTotalsText()
        {
            if (_bases.Length == 0) return "Base total: (no base in scene)";

            // Phase 7b: per-player labels — "P3: 42" for owned bases, plain "?: N"
            // for any base GameManager hasn't assigned yet.
            var sb = new System.Text.StringBuilder();
            int written = 0;
            for (int i = 0; i < _bases.Length; i++)
            {
                var b = _bases[i];
                if (b == null) continue;
                if (written > 0) sb.Append("  ");
                if (b.Owner.IsRealPlayer) sb.Append('P').Append(b.Owner.PlayerId).Append(": ");
                else sb.Append("?: ");
                sb.Append(Mathf.FloorToInt(b.FoodTotal));
                written++;
            }
            return written == 0 ? "Bases: —" : sb.ToString();
        }

        private void DrawTopBar()
        {
            if (_gameManager == null || _gameManager.State == MatchState.Ended) return;

            string timerText;
            if (_gameManager.State == MatchState.Active)
            {
                float remaining = _gameManager.TimeRemaining;
                int mm = Mathf.Max(0, Mathf.FloorToInt(remaining / 60f));
                int ss = Mathf.Max(0, Mathf.FloorToInt(remaining - mm * 60f));
                timerText = $"{mm:00}:{ss:00}";
            }
            else
            {
                timerText = "Waiting…";
            }

            const int width = 220;
            const int height = 36;
            int x = (Screen.width - width) / 2;
            int y = 12;

            var prevAlign = GUI.skin.box.alignment;
            var prevSize = GUI.skin.box.fontSize;
            GUI.skin.box.alignment = TextAnchor.MiddleCenter;
            GUI.skin.box.fontSize = 22;
            GUI.Box(new Rect(x, y, width, height), timerText);
            GUI.skin.box.alignment = prevAlign;
            GUI.skin.box.fontSize = prevSize;
        }

        private void DrawEndOverlay()
        {
            if (_gameManager == null || _gameManager.State != MatchState.Ended) return;

            const int width = 540;
            const int height = 180;
            int x = (Screen.width - width) / 2;
            int y = (Screen.height - height) / 2;

            GUI.Box(new Rect(x, y, width, height), "MATCH ENDED");

            string winnerText;
            var winner = _gameManager.WinnerPlayer;
            if (winner == default || !winner.IsRealPlayer)
            {
                winnerText = "Winner: (none)";
            }
            else
            {
                winnerText = $"Winner: Player {winner.PlayerId}";
            }

            var prevAlign = GUI.skin.label.alignment;
            var prevSize = GUI.skin.label.fontSize;
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;
            GUI.skin.label.fontSize = 24;
            GUI.Label(new Rect(x, y + 50, width, 36), winnerText);
            GUI.Label(new Rect(x, y + 90, width, 36), $"Final food: {Mathf.FloorToInt(_gameManager.WinnerFoodTotal)}");
            GUI.skin.label.fontSize = 16;
            GUI.Label(new Rect(x, y + 138, width, 28), "(restart by reloading the scene — auto-restart in Phase 9 polish)");
            GUI.skin.label.alignment = prevAlign;
            GUI.skin.label.fontSize = prevSize;
        }
    }
}
