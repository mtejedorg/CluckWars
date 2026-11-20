namespace CluckWars.Balance
{
    /// <summary>
    /// Maestro's two rules for the starting pile food (decision D1b, 2026-11-20): a player's own pile plus ONE contested pile is
    /// not enough to win, and the own pile plus TWO contested piles is. Pure; the pile food test checks the shipped config
    /// with them and the solver (Balance Oracle v2) uses them as hard limits on any food it proposes.
    /// </summary>
    public static class FoodRules
    {
        /// <summary>True while the own pile plus one contested pile stays under the win target.</summary>
        public static bool OwnPlusOneIsNotEnough(OracleFood food, float target) => food.Personal + food.Contested < target;

        /// <summary>True while the own pile plus two contested piles reaches the win target.</summary>
        public static bool OwnPlusTwoIsEnough(OracleFood food, float target) => food.Personal + 2f * food.Contested >= target;
    }
}
