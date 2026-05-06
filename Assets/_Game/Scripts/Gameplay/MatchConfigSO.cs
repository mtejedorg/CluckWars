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
        [Min(1)] public int FoodTargetToWin = 150;
        [Min(0f)] public float DeathStunSeconds = 5f;

        [Header("Networking")]
        [Tooltip("Fusion simulation tick rate. 30 Hz keeps mid-range Android stable.")]
        [Range(15, 60)] public int TickRate = 30;
    }
}
