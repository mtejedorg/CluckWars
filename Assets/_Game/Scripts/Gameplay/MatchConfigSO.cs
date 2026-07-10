using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Single source of truth for match-wide tunables. Lives at /Assets/_Game/Data/MatchConfig.asset.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MatchConfig",
        menuName = "Cluck Wars/Match Config",
        order = 1)]
    public sealed class MatchConfigSO : ScriptableObject
    {
        [Header("Match")]
        [Min(30f)] public float MatchDurationSeconds = 180f;
        [Min(1)] public int FoodTargetToWin = 110;
        [Min(0f)] public float DeathStunSeconds = 5f;
        [Min(0.5f)] public float DepositRatePerSecond = 6f;

        [Header("Networking")]
        [Tooltip("Fusion simulation tick rate. 30 Hz keeps mid-range Android stable.")]
        [Range(15, 60)] public int TickRate = 30;

        [Tooltip("Room capacity passed to StartGameArgs.PlayerCount. The host can start a match with any number 1..MaxPlayers — this is just the upper limit Photon enforces on joiners.")]
        [Range(1, 16)] public int MaxPlayers = 4;
    }
}
