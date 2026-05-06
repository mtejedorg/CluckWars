using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// Platform-agnostic input source. Swapped at composition root:
    /// <see cref="KeyboardInputProvider"/> on Standalone, MobileInputProvider on Android.
    /// Read by <c>FusionNetworkService</c> on each <c>OnInput</c> tick — gameplay
    /// code never reads this directly.
    /// </summary>
    public interface IInputProvider
    {
        /// <summary>Stick / WASD axis, range roughly [-1,1] per axis. Not normalized.</summary>
        Vector2 GetMovement();
        bool GetAttackHeld();
        bool GetAbility1Pressed();
        bool GetAbility2Pressed();
    }
}
