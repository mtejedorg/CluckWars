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
        /// (desktop Esc or right mouse button; touch: release while the edge-band cancel is armed, or a lost
        /// touch, surfaced via <c>TouchControlsController.ConsumeAbilityCancelled</c>). One flag covers
        /// every slot — only one hold can be charging at a time. Same one-shot /
        /// must-be-read-every-tick contract as <c>GetAbilityXPressed</c>.
        /// </summary>
        bool GetAbilityCancelPressed();

        /// <summary>
        /// Edge-triggered UI back / cancel (desktop Esc; Android's system back button, which the
        /// Input System reports as the Escape key). Polled by the MENU only - the match never reads
        /// it, so it cannot steal <see cref="GetAbilityCancelPressed"/>, which shares the Esc key.
        /// Both read the key's this-frame edge, which nothing consumes.
        /// </summary>
        bool GetBackPressed();

        /// <summary>
        /// Level-triggered (Phase 6, A4): true while a held ability's pointer sits in the touch edge band, i.e.
        /// releasing now would CANCEL. Read by the local preview (grey + dashed) and nothing else. Always false
        /// on keyboard / mouse and gamepad, which cancel with a button instead.
        /// </summary>
        bool IsAbilityCancelArmed();

        // ---- Phase 6 chunk 5 (A7): aim and device identity. Default members, so a provider with no aim
        // (touch, a test fake) needs no code and behaves exactly as before.

        /// <summary>Which device family this provider reads. Used to pick the aim assist that device gets.</summary>
        InputDeviceKind Device => InputDeviceKind.None;

        /// <summary>
        /// <c>Time.unscaledTime</c> of this provider's most recent real use (a key, a stick past its dead zone, a
        /// finger down), or negative infinity. The composite uses it to pick the most recently used device.
        /// </summary>
        float LastActiveTime => float.NegativeInfinity;

        /// <summary>
        /// Where the player wants to aim right now: a stick direction, a ground point under the mouse
        /// (<paramref name="groundY"/> is the height of the plane the cursor ray is cast onto, the local bird's y), or
        /// none, meaning "use facing". Level-triggered and side-effect free apart from filter state, so it is safe to
        /// read from the telegraph every frame and from <c>OnInput</c> every tick.
        /// </summary>
        AimInput GetAim(float groundY) => AimInput.None;
    }
}
