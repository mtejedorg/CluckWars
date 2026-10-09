namespace CluckWars.Services
{
    /// <summary>What the buzz is for. Severity (and so which buzz may interrupt which) follows the declaration order.</summary>
    public enum HapticKind : byte
    {
        /// <summary>The edge-band cancel armed under a held finger: a very light tick.</summary>
        CancelArm = 0,
        /// <summary>Your bird was hit by a move.</summary>
        Hit = 1,
        /// <summary>Your bird was stunned or knocked out: the longer double buzz.</summary>
        Stun = 2,
    }

    /// <summary>
    /// Phone haptics (Phase 6, A9). A LOCAL presentation channel like animation and VFX: it is triggered from the same
    /// local change detection that drives the hit flash and is never an RPC. Implementations decide whether the device
    /// can buzz at all; the player's "Buzz When Hit" choice and the minimum gap between buzzes live in
    /// <see cref="HapticsService"/>, not in the gameplay callers.
    /// </summary>
    public interface IHapticsService
    {
        void Buzz(HapticKind kind);
    }

    /// <summary>Inert haptics: desktop, the Editor and tests.</summary>
    public sealed class NullHapticsService : IHapticsService
    {
        public void Buzz(HapticKind kind) { }
    }
}
