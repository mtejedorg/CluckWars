namespace CluckWars.Gameplay
{
    /// <summary>The control ladder (spec §3.1). Backing byte to match the project's
    /// other gameplay enums; order is severity-ascending, do not renumber.</summary>
    public enum ControlState : byte { Free = 0, Slowed = 1, Rooted = 2, Stunned = 3 }

    /// <summary>Total function of ControlState → what the chicken may do. Pure so the
    /// whole ladder is pinned by EditMode tests; the NetworkBehaviours call these.</summary>
    public static class ControlRules
    {
        public static bool CanMove(ControlState s)    => s != ControlState.Rooted && s != ControlState.Stunned;
        public static bool CanCast(ControlState s)    => s != ControlState.Stunned;
        public static bool CanCollect(ControlState s) => s == ControlState.Free || s == ControlState.Slowed;
    }
}
