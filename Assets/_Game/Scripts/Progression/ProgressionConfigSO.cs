using UnityEngine;

namespace CluckWars.Progression
{
    /// <summary>
    /// The Grain earn constants. One asset, <c>Assets/_Game/Data/Progression/ProgressionConfig.asset</c>,
    /// bound project-wide by <c>ProjectInstaller</c> from its one inspector slot.
    /// </summary>
    /// <remarks>
    /// The field initialisers mirror the shipped asset: the installer falls back to a default
    /// instance (with a loud warning) when the slot is empty.
    /// <para>
    /// <b>The wallet is re-derived from this asset every time the journal is folded.</b> Changing a
    /// value here changes every past round's Grain, and so the balance. That is intended while Grain
    /// is only earned; the first slice that lets the player spend it must record spending in the
    /// journal as its own facts.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "ProgressionConfig", menuName = "Progression/Progression Config")]
    public sealed class ProgressionConfigSO : ScriptableObject
    {
        [Header("Per round")]
        [Tooltip("Grain for finishing a round at all.")]
        [Min(0)] public int Participation = 20;

        [Tooltip("Grain per unit of the banked total in the round's standings.")]
        [Min(0f)] public float PerBank = 0.40f;

        [Tooltip("Grain per unit taken from rivals, before the cap below.")]
        [Min(0f)] public float PerSteal = 1.20f;

        [Tooltip("Most Grain a round's steals can earn.")]
        [Min(0)] public int StealCap = 12;

        [Tooltip("Grain by finishing position: element 0 is 1st place. Must cover MatchConfig.MaxPlayers " +
                 "positions, or a round finished below the last entry earns nothing.")]
        public int[] PlacementGrain = { 14, 8, 5, 3 };

        [Header("Rested bonus")]
        [Tooltip("Multiplier on a round's whole Grain while it is one of the first RestedRounds of the local day.")]
        [Min(1f)] public float RestedMultiplier = 1.5f;

        [Tooltip("How many rounds per local calendar day are rested.")]
        [Min(0)] public int RestedRounds = 3;

        [Header("Daily task")]
        [Tooltip("Grain for the daily task. Unused until slice 4 adds the task; nothing reads it yet.")]
        [Min(0)] public int DailyTaskBonus = 15;
    }
}
