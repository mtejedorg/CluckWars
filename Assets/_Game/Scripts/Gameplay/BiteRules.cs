namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Enforcer's bite as numbers (design 2026-11-25, redesign): a rival is fresh while its Peck is casting and for
    /// <see cref="FreshSeconds"/> after a Peck of its own landed food; any hit of the Enforcer on a fresh rival stacks one extra debt on it and, only when <see cref="FoodShare"/> is above 0, takes that share of the
    /// rival's bite as food. A <see cref="FreshSeconds"/> of 0 means no rival is ever fresh (the shipped value).
    /// </summary>
    public struct BiteFacts
    {
        /// <summary>Seconds a rival stays fresh after its own Peck landed with food. 0 = no rival is ever fresh.</summary>
        public float FreshSeconds;
        /// <summary>Share (0 to 1) of the rival's own Peck amount a hit on a fresh rival takes as food. 0 = the hit takes no food and only stacks a debt (shipped); 1 = the whole bite.</summary>
        public float FoodShare;

        public bool Active => FreshSeconds > 0f;

        public static BiteFacts None => default;
    }

    /// <summary>The pure rules of the bite, shared by the match simulator and, later, the game.</summary>
    public static class BiteRules
    {
        /// <summary>
        /// A rival can be bitten now: its state has not been bitten yet and either its Peck is casting, or its own Peck landed with food no longer
        /// than <paramref name="freshSeconds"/> ago. A <paramref name="freshSeconds"/> of 0 or less is never fresh, mid-cast included.
        /// </summary>
        public static bool Fresh(bool casting, float sinceLanded, bool landedWithFood, bool bitten, float freshSeconds) =>
            freshSeconds > 0f && !bitten && (casting || (landedWithFood && sinceLanded <= freshSeconds));

        /// <summary>
        /// The replicated "just landed" state is still the current one: a Peck landed food (<paramref name="fedTick"/> not 0) and no cast started and
        /// no newer landing opened since (<paramref name="fedStateId"/> still equals <paramref name="freshStateId"/>).
        /// </summary>
        public static bool LandedWithFood(int fedTick, byte fedStateId, byte freshStateId) =>
            fedTick != 0 && fedStateId == freshStateId;

        /// <summary>The bite from a casting rival's pile: the rival's own Peck amount, held to the food in the pile and to the Enforcer's free room.</summary>
        public static float FromCast(float victimPeckAmount, float pileFood, float freeRoom)
        {
            float bite = victimPeckAmount < pileFood ? victimPeckAmount : pileFood;
            if (freeRoom < bite) bite = freeRoom;
            return bite > 0f ? bite : 0f;
        }

        /// <summary>The bite from a rival whose Peck just landed: its Peck amount, held to the Enforcer's free room and to the rival's cargo (<see cref="StealMath.Clamp"/>).</summary>
        public static float FromCargo(float victimPeckAmount, float freeRoom, float victimStealable) =>
            StealMath.Clamp(victimPeckAmount, freeRoom, victimStealable);
    }
}
