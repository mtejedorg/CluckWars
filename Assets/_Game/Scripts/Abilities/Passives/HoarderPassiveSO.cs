using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Fatty signature.</b> Carries at least a full win's worth of food, so a perfect run
    /// is one trip: fill up once, walk home once, win.
    /// </summary>
    /// <remarks>
    /// <see cref="MinimumCapacity"/> must stay at or above <c>MatchConfig.FoodTargetToWin</c>
    /// or the fantasy silently fails — the chicken walks home one bite short and has to go
    /// back out. A DataIntegrity test pins that relationship rather than trusting the two
    /// numbers to be edited together.
    /// </remarks>
    [CreateAssetMenu(fileName = "Hoarder", menuName = "Cluck Wars/Passive/Hoarder", order = 13)]
    public sealed class HoarderPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Capacity floor. MUST be >= MatchConfig.FoodTargetToWin, or the one-trip win " +
                 "this passive exists for cannot happen.")]
        [Min(1f)] public float MinimumCapacity = 40f;

        public HoarderPassiveSO()
        {
            DisplayName = "Hoarder";
            ShortLabel = "HRDR";
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        public override bool IsSignature => true;
        protected override string DefaultIcon => "\U0001F4E6";

        public override float ModifyCargoCapacity(float capacity, ChickenController self) =>
            Mathf.Max(capacity, MinimumCapacity);
    }
}
