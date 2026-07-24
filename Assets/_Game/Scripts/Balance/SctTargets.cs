namespace CluckWars.Balance
{
    /// <summary>One class's Solo Clear Time goalpost (spec §1).</summary>
    public struct SctTarget
    {
        public string ClassName;
        public float  Seconds;
        public int    Trips;
    }

    /// <summary>
    /// The SCT axiom's goalposts and the tolerance check. Change these four numbers to
    /// re-solve the whole game (spec §1 / §7.1). The Oracle asserts each class's naked
    /// stats land Within() its target.
    /// </summary>
    public static class SctTargets
    {
        public const float DefaultToleranceSeconds = 1.5f;

        public static readonly SctTarget[] All =
        {
            new SctTarget { ClassName = "Speedy",   Seconds = 30f, Trips = 4 },
            new SctTarget { ClassName = "Fatty",    Seconds = 30f, Trips = 2 },
            new SctTarget { ClassName = "Warrior",  Seconds = 35f, Trips = 3 },
            new SctTarget { ClassName = "Assassin", Seconds = 40f, Trips = 4 },
        };

        /// <summary>True iff the result reached the target, matched the trip count exactly,
        /// and landed within <paramref name="toleranceSeconds"/> of the target time.</summary>
        public static bool Within(OracleResult result, SctTarget target,
            float toleranceSeconds = DefaultToleranceSeconds)
        {
            if (!result.ReachedTarget) return false;
            if (result.Trips != target.Trips) return false;
            return UnityEngine.Mathf.Abs(result.SctSeconds - target.Seconds) <= toleranceSeconds;
        }
    }
}
