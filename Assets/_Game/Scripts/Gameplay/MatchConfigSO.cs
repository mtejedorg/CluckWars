using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>How a chicken's four abilities are chosen. The one switch between the balance baseline and free picking.</summary>
    public enum LoadoutMode : byte
    {
        /// <summary>Every specialization plays its authored kit (<see cref="CluckWars.Abilities.PassiveAbilitySO.Kit"/>, <see cref="FixedKit"/>).</summary>
        Fixed = 0,
        /// <summary>The pre-baseline rules: two pre-equips (<see cref="PreEquippedLoadout"/>), the rest hand-picked.</summary>
        Free = 1,
    }

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

        [Tooltip("The comeback event (golden pile / underdog surge / leader bounty / restock) fires " +
                 "once, when this many playable seconds are left (re-audit round 2, decision 2: the " +
                 "last 10 s). 0 disables it. Read by GameManager through ComebackEventTiming.")]
        [Min(0f)] public float ComebackEventSecondsLeft = 10f;

        [Tooltip("Base wall: neither a chicken's body nor any ability crosses this radius round a claimed base (m). " +
                 "Own base free for the body, unclaimed bases open. Independent of PlayerBase.DepositRadius, and " +
                 "the map keeps it passable: BaseKeepOutTests checks the real layout.")]
        [Min(0f)] public float BaseKeepOutRadius = 2.5f;

        [Header("Pile food")]
        [Tooltip("Food in each player's own pile next to their base. Keep it so that this plus ONE contested pile is " +
                 "not enough to win, and this plus TWO contested piles is (FoodRules checks both).")]
        [Min(0)] public float PersonalPileFood = 10f;

        [Tooltip("Food in each of the four piles between two neighbouring players.")]
        [Min(0)] public float ContestedPileFood = 15f;

        [Tooltip("Food in the one pile in the middle of the arena. The prize worth fighting over.")]
        [Min(0)] public float CentrePileFood = 40f;

        [Header("Loadouts")]
        [Tooltip("Fixed: every specialization plays its authored kit (the balance baseline; players and bots). " +
                 "Free: the old hand-picked loadouts, unchanged. One switch, read by the spawner, GEAR UP and LastSetup.")]
        public LoadoutMode LoadoutMode = LoadoutMode.Fixed;

        [Header("Targeting")]
        [Tooltip("RangeOnly (Maestro 2026-11-05): every targeted ability hits every rival within its radius of the caster, " +
                 "by planar distance only (no cone, forward offset or single pick). Shaped: each ability's authored shape. " +
                 "Pushed once into AbilityTargeting.Mode by ProjectInstaller; every peer reads this one asset.")]
        public CluckWars.Abilities.TargetingMode TargetingMode = CluckWars.Abilities.TargetingMode.RangeOnly;

        [Header("Networking")]
        // Tick rate lives in NetworkProjectConfig.fusion (Fusion 2 ignores per-session values here)

        [Tooltip("Room capacity passed to StartGameArgs.PlayerCount. The host can start a match with any number 1..MaxPlayers — this is just the upper limit Photon enforces on joiners.")]
        [Range(1, 16)] public int MaxPlayers = 4;
    }
}
