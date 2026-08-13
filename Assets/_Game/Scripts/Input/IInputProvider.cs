using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// Platform-agnostic input source. Swapped at composition root:
    /// <see cref="KeyboardInputProvider"/> on Standalone, MobileInputProvider on Android.
    /// Read by <c>FusionNetworkService</c> on each <c>OnInput</c> tick — gameplay
    /// code never reads this directly.
    /// </summary>
    /// <remarks>
    /// v0.3: Attack removed. Ability3 added for Assassin's third ability slot.
    /// </remarks>
    public interface IInputProvider
    {
        /// <summary>Stick / WASD axis, range roughly [-1,1] per axis. Not normalized.</summary>
        Vector2 GetMovement();
        bool GetAbility1Pressed();
        bool GetAbility2Pressed();
        bool GetAbility3Pressed();
        /// <summary>Ability slot 4. Available to every class as of v0.7's four-slot loadouts.</summary>
        bool GetAbility4Pressed();

        /// <summary>
        /// True while ability slot 0..3's button/key is currently held down (v0.6
        /// hold-to-aim, FEEDBACK.md §2). Level-triggered, unlike the edge-triggered
        /// <c>GetAbilityXPressed</c> calls above — safe to read every frame/tick with
        /// no consumption semantics.
        /// </summary>
        bool GetAbilityHeld(int slot);

        /// <summary>
        /// Edge-triggered: true for one frame when the hold-cancel gesture fires
        /// (desktop Esc; touch drag-off-the-button, surfaced via
        /// <c>TouchControlsController.ConsumeAbilityCancelled</c>). One flag covers
        /// every slot — only one hold can be charging at a time. Same one-shot /
        /// must-be-read-every-tick contract as <c>GetAbilityXPressed</c>.
        /// </summary>
        bool GetAbilityCancelPressed();
    }
}
