using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Receiver-side rules that decide whether a <c>PlayerBase.RPC_AddFood</c> call is a
    /// real deposit. Pure static maths, deliberately free of Fusion types, so the EditMode
    /// suite can exercise every branch without a live <c>NetworkRunner</c> — the same split
    /// <see cref="StealMath"/>, <c>BotTactics</c> and <c>AbilityAim</c> already use.
    /// </summary>
    /// <remarks>
    /// <c>RPC_AddFood</c> is <c>RpcSources.All</c>, so any connected peer can call it on any
    /// base. Its only guard used to be <c>amount &gt; 0</c>, which put a compromised client one
    /// line away from <c>someBase.RPC_AddFood(9999)</c> and an instant win. Three independent
    /// facts have to hold for a deposit to be real, and each has a predicate here:
    /// the amount is a plausible single flush, the depositor is standing at the base, and the
    /// base belongs to the depositor's corner.
    /// </remarks>
    public static class BaseDepositRules
    {
        /// <summary>
        /// The window <c>ChickenCargo</c> accumulates a deposit over before flushing it as one
        /// RPC. It lives here rather than at the flush site because the receiver's
        /// plausible-amount bound is derived from it, and the sender and receiver silently
        /// disagreeing about the batch size is exactly the drift this constant prevents.
        /// </summary>
        public const float FlushSeconds = 0.25f;

        /// <summary>
        /// Largest multiplier any ability may apply to the deposit rate. Today the only lever
        /// on banking speed that an ability can reach is
        /// <c>QuickDropAbilitySO.DepositRateMultiplier</c>, whose <c>[Range]</c> tops out at 8.
        /// <c>BaseDepositRulesTests</c> pins both the attribute ceiling and every authored
        /// ability asset against this number, so a new deposit-rate ability cannot raise the
        /// real maximum past the bound without turning the suite red.
        /// </summary>
        public const float MaxDepositRateMultiplier = 8f;

        /// <summary>
        /// The largest food total one <c>RPC_AddFood</c> call can legitimately carry: a full
        /// flush window at the configured deposit rate, with every ability multiplier stacked.
        /// </summary>
        public static float MaxSingleDeposit(float depositRatePerSecond) =>
            Mathf.Max(0f, depositRatePerSecond) * FlushSeconds * MaxDepositRateMultiplier;

        /// <summary>True when <paramref name="amount"/> could have come from one real flush.</summary>
        public static bool IsPlausibleAmount(float amount, float depositRatePerSecond) =>
            amount > 0f && amount <= MaxSingleDeposit(depositRatePerSecond);

        /// <summary>
        /// How far past <c>DepositRadius</c> a legitimate depositor may have drifted by the time
        /// its RPC is applied. The chicken was inside the radius while the batch accrued; it
        /// then had the flush window plus the message's flight time to walk away at full speed.
        /// Rejecting that drift would drop real deposits, which is a worse failure than an
        /// attacker gaining a couple of metres of slack on a check they still cannot pass from
        /// across the map.
        /// </summary>
        public static float RangeMargin(float moveSpeed, float latencySeconds) =>
            Mathf.Max(0f, moveSpeed) * (FlushSeconds + Mathf.Max(0f, latencySeconds));

        /// <summary>
        /// Planar (XZ) proximity test, matching <c>ChickenCargo</c>'s sender-side gate. A
        /// chicken's pivot floats about a capsule half-height above a base's pivot, so a full
        /// 3D distance would push an in-range chicken outside the tuned radius — the same
        /// footgun documented on <c>ChickenCargo.HorizontalSqr</c>.
        /// </summary>
        public static bool IsWithinDepositRange(
            Vector3 basePosition, Vector3 chickenPosition, float depositRadius, float margin)
        {
            float limit = Mathf.Max(0f, depositRadius) + Mathf.Max(0f, margin);
            float dx = basePosition.x - chickenPosition.x;
            float dz = basePosition.z - chickenPosition.z;
            return dx * dx + dz * dz <= limit * limit;
        }

        /// <summary>
        /// A chicken may only bank into the base on its own corner.
        /// <c>ChickenController.HomeCornerIndex</c> is the match identity anchor — an unstamped
        /// chicken (-1) has no allegiance and can deposit nowhere.
        /// </summary>
        public static bool IsAllegianceMatch(int chickenHomeCornerIndex, int baseCornerIndex) =>
            chickenHomeCornerIndex >= 0 && chickenHomeCornerIndex == baseCornerIndex;
    }
}
