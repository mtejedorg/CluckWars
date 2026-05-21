using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Ability category as per GDD §7.2. Shown in the character-select picker to
    /// help players understand what an ability does before equipping it.
    /// </summary>
    public enum AbilityCategory : byte
    {
        Damage  = 0,
        Control = 1,
        Defense = 2,
        Utility = 3,
    }

    /// <summary>
    /// How the bot AI should use this ability.
    /// <c>Auto</c> derives the role from <see cref="AbilityCategory"/>:
    /// Damage→Offense, Control→Control, Defense→Defense, Utility→Escape.
    /// Override only when Auto would be wrong (e.g. Sneaky Steal = Steal).
    /// </summary>
    public enum BotRole : byte
    {
        Auto    = 0, // derive from AbilityCategory (see AbilityBaseSO.ResolveBotRole)
        Offense = 1, // close in and deal damage / stun the rival
        Defense = 2, // turtle up / block hits
        Control = 3, // displace or pin a rival
        Escape  = 4, // dash / blur away from danger
        Steal   = 5, // snatch cargo from a rival
    }

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
        [Tooltip("GDD §7.2 category. Used by the character-select ability grid to group abilities.")]
        public AbilityCategory Category = AbilityCategory.Utility;

        [Header("Bot")]
        [Tooltip("How the bot AI classifies and uses this ability. Auto derives the role from Category.")]
        public BotRole BotRole = BotRole.Auto;

        [Header("Timings")]
        [Tooltip("How long the active effect lasts after activation (seconds). GDD calls for 1–2s on most abilities.")]
        [Min(0.05f)] public float Duration = 1.5f;

        [Tooltip("Lockout between activations (seconds). Counted from activation, not from deactivation.")]
        [Min(0f)] public float Cooldown = 8f;

        [Header("Animation")]
        [Tooltip("Optional clip override. Phase 6 doesn't drive animations from abilities — slot reserved for Phase 9 polish.")]
        public AnimationClip AbilityAnimationClip;

        /// <summary>
        /// Returns the effective <see cref="BotRole"/> for this ability.
        /// When <see cref="BotRole"/> is <c>Auto</c>, derives from
        /// <see cref="Category"/>: Damage→Offense, Control→Control,
        /// Defense→Defense, Utility→Escape.
        /// </summary>
        public BotRole ResolveBotRole() => BotRole switch
        {
            BotRole.Auto => Category switch
            {
                AbilityCategory.Damage  => BotRole.Offense,
                AbilityCategory.Defense => BotRole.Defense,
                AbilityCategory.Control => BotRole.Control,
                _                       => BotRole.Escape, // Utility → Escape
            },
            _ => BotRole,
        };

        public abstract void OnActivate(AbilityContext ctx);
        public abstract void OnDeactivate(AbilityContext ctx);
    }
}
