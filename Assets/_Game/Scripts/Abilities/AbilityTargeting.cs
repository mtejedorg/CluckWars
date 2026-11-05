namespace CluckWars.Abilities
{
    /// <summary>How a targeted ability finds its victims. The one switch between Maestro's 2026-11-05 range-only rule and the
    /// older shaped areas (cones, forward circles, lanes, single picks).</summary>
    public enum TargetingMode : byte
    {
        /// <summary>Every rival within the ability's radius of the caster, by planar distance only: no arc, no forward
        /// offset, no single pick, no front or back.</summary>
        RangeOnly = 0,
        /// <summary>Each ability's authored shape (<see cref="AbilityBaseSO.AuthoredAimShape"/>), as before the ruling.</summary>
        Shaped = 1,
    }

    /// <summary>
    /// The project-wide <see cref="TargetingMode"/>, and the one rule that turns an authored aim shape into the shape every
    /// consumer reads (<see cref="AbilityBaseSO.AimShape"/>: the scan, previews, range guides, telegraph, bots, receiver bounds).
    /// </summary>
    /// <remarks>
    /// <b>Why a static.</b> Ability SOs are not injected and their shape is read from static code with no context (the
    /// receiver bounds, <c>BotTactics</c>, the HUD). The value is authored on <c>MatchConfigSO.TargetingMode</c> and pushed
    /// here once by <c>ProjectInstaller</c> before any scene loads, the same way it sets <c>HeavyProbes.Enabled</c>. Every
    /// peer ships the same asset, so every peer agrees. The default is the shipped rule, so code that runs without the
    /// installer (EditMode tests, editor tools) sees RangeOnly; a test that needs Shaped sets it and restores it.
    /// </remarks>
    public static class AbilityTargeting
    {
        /// <summary>The live mode. Set by <c>ProjectInstaller</c> from <c>MatchConfig.asset</c>.</summary>
        public static TargetingMode Mode = TargetingMode.RangeOnly;

        /// <summary>
        /// Does <paramref name="authored"/> collapse to a caster-centred circle under <paramref name="mode"/>? Only the
        /// shapes that pick victims (Cone, SelfCircle, ForwardCircle, Capsule, SingleTarget) and only for an ability that
        /// resolves at the cast (<paramref name="placesZone"/> false: a placed zone's footprint is where it lands, not who
        /// it targets). None (self buffs), Aura and Jump never change.
        /// </summary>
        public static bool Collapses(AbilityAimShape authored, bool placesZone, TargetingMode mode)
        {
            if (mode != TargetingMode.RangeOnly || placesZone) return false;
            switch (authored)
            {
                case AbilityAimShape.SelfCircle:
                case AbilityAimShape.Cone:
                case AbilityAimShape.ForwardCircle:
                case AbilityAimShape.Capsule:
                case AbilityAimShape.SingleTarget:
                    return true;
                default:
                    return false;
            }
        }
    }
}
