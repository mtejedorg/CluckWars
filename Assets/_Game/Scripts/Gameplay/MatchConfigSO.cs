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
        [Min(30f)] public float MatchDurationSeconds = 45f;
        [Min(1)] public int FoodTargetToWin = 40;
        [Min(0)] public int SpoilerBounty = 15;
        [Min(0f)] public float DeathStunSeconds = 5f;
        [Min(0.5f)] public float DepositRatePerSecond = 9f;

        [Header("Networking")]
        // Tick rate lives in NetworkProjectConfig.fusion (Fusion 2 ignores per-session values here)

        [Tooltip("Room capacity passed to StartGameArgs.PlayerCount. The host can start a match with any number 1..MaxPlayers — this is just the upper limit Photon enforces on joiners.")]
        [Range(1, 16)] public int MaxPlayers = 4;
    }
}
