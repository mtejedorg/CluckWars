using System;

namespace CluckWars.Services
{
    /// <summary>The platform end of haptics: can this device buzz, and play one pattern. Android's is <c>AndroidHapticDevice</c>.</summary>
    public interface IHapticDevice
    {
        /// <summary>False when the device has no vibrator or the system has haptics switched off.</summary>
        bool CanBuzz { get; }

        void Play(HapticPattern pattern);
    }

    /// <summary>
    /// The haptics policy in one place: the player's "Buzz When Hit" choice, the device's willingness and the 200 ms
    /// rate limit, in front of a platform <see cref="IHapticDevice"/>. Callers just say what happened.
    /// </summary>
    public sealed class HapticsService : IHapticsService
    {
        private readonly IHapticDevice _device;
        private readonly Func<bool> _enabled;
        private readonly Func<float> _clock;
        private readonly HapticLimiter _limiter = new HapticLimiter();

        public HapticsService(IHapticDevice device, Func<bool> enabled, Func<float> clock)
        {
            _device = device;
            _enabled = enabled;
            _clock = clock;
        }

        public void Buzz(HapticKind kind)
        {
            // Preference first, then the device, then the limiter: a disabled or buzz-less phone must not
            // consume the rate-limit window.
            if (!_enabled() || !_device.CanBuzz) return;
            if (!_limiter.TryAcquire(_clock(), kind)) return;
            _device.Play(HapticRules.PatternFor(kind));
        }
    }
}
