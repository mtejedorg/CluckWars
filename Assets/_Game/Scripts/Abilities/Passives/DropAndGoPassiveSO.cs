using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy alternative.</b> Banks cargo almost instantly, removing the trip-count tax.
    /// </summary>
    /// <remarks>
    /// ⚠️ The core-redesign spec records this as worth <b>~27% of Speedy's SCT</b> and warns
    /// it "requires a Speedy stat re-solve if equipped is to be balanced". Passives sit
    /// outside the NAKED axiom, so this is legal as written — but it is by far the most
    /// SCT-distorting passive in the set, and it is the first thing to measure once there is
    /// playtest data. Expressed as a multiplier rather than a literal instant deposit so the
    /// Oracle can model it if that re-solve ever happens.
    /// </remarks>
    [CreateAssetMenu(fileName = "DropAndGo", menuName = "Cluck Wars/Passive/Drop and Go", order = 12)]
    public sealed class DropAndGoPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on deposit rate. High enough to read as instant at the 9/s base.")]
        [Min(1f)] public float DepositRateMultiplier = 6f;

        public DropAndGoPassiveSO()
        {
            DisplayName = "Drop & Go";
            ShortLabel = "DROP";
            AllowedClasses = ChickenClassFlags.Speedy;
        }

        protected override string DefaultIcon => "\U0001F4B0";

        public override float ModifyDepositRate(float perSecond, ChickenController self) =>
            perSecond * DepositRateMultiplier;
    }
}
