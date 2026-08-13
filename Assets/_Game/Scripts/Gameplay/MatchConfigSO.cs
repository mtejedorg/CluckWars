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

        [Tooltip("Flat food paid straight into the Assassin's bounty bag for a successful " +
                 "execute, ON TOP of the victim's transferred cargo. This is the Assassin's " +
                 "income FLOOR: without it, an execute on an empty-handed rival pays exactly " +
                 "zero and the class can be starved in the opening seconds. 5 matches the " +
                 "magnitude of the ground drops it replaced, so it routes food rather than " +
                 "adding it. A starting value - solve it once the Predation axiom has real " +
                 "playtest numbers.")]
        [Min(0)] public int ExecuteBounty = 5;

        [Tooltip("Multiplier on ExecuteBounty when the victim was the match leader. The " +
                 "comeback valve that used to be 8 extra ground pickups. UNRESOLVED VALUE - " +
                 "2 is a first guess, not a solved number.")]
        [Min(1f)] public float LeaderExecuteBountyMultiplier = 2f;
        [Min(0f)] public float DeathStunSeconds = 5f;
        [Min(0.5f)] public float DepositRatePerSecond = 9f;

        [Header("Networking")]
        // Tick rate lives in NetworkProjectConfig.fusion (Fusion 2 ignores per-session values here)

        [Tooltip("Room capacity passed to StartGameArgs.PlayerCount. The host can start a match with any number 1..MaxPlayers — this is just the upper limit Photon enforces on joiners.")]
        [Range(1, 16)] public int MaxPlayers = 4;
    }
}
