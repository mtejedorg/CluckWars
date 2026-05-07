using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Base ScriptableObject for every ability. Adding a new ability = subclass
    /// this, override <see cref="OnActivate"/> + <see cref="OnDeactivate"/>, drop
    /// a concrete asset under <c>/Assets/_Game/Data/Abilities/</c>. No code
    /// changes to <c>AbilityController</c> or anything else.
    /// </summary>
    /// <remarks>
    /// Activation timing (duration / cooldown) is handled by <c>AbilityController</c>.
    /// Concrete abilities only describe what to do at the start and end of the
    /// active window. They mutate state via the <see cref="AbilityContext"/>'s
    /// <c>ChickenController</c> hooks (<c>MoveSpeedMultiplier</c>, <c>MovementLocked</c>,
    /// <c>DamageImmune</c>, …) which the StateAuthority reads each tick.
    /// </remarks>
    public abstract class AbilityBaseSO : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName;
        [Tooltip("Short label drawn on the ability button (≤ 4 chars works best).")]
        public string ShortLabel;
        [Tooltip("Accent color for VFX / UI highlight. Phase 6 uses it for the cooldown overlay tint.")]
        public Color AccentColor = new Color(0.45f, 0.7f, 1f, 1f);

        [Header("Timings")]
        [Tooltip("How long the active effect lasts after activation (seconds). GDD calls for 1–2s on most abilities.")]
        [Min(0.05f)] public float Duration = 1.5f;

        [Tooltip("Lockout between activations (seconds). Counted from activation, not from deactivation.")]
        [Min(0f)] public float Cooldown = 8f;

        [Header("Animation")]
        [Tooltip("Optional clip override. Phase 6 doesn't drive animations from abilities — slot reserved for Phase 9 polish.")]
        public AnimationClip AbilityAnimationClip;

        public abstract void OnActivate(AbilityContext ctx);
        public abstract void OnDeactivate(AbilityContext ctx);
    }
}
