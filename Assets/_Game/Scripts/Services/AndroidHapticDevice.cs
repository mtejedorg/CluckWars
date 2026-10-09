using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.Services
{
    /// <summary>
    /// Android vibration through <c>android.os.Vibrator</c> and <c>VibrationEffect</c> (one-shot or waveform with
    /// amplitudes; NOT <c>Handheld.Vibrate</c>, which has a fixed ~1 s length and no amplitude). Honours the system
    /// "touch feedback" haptics switch when it can be queried. Needs <c>android.permission.VIBRATE</c>, added to the
    /// exported manifest by <c>AndroidVibratePermission</c> (Editor).
    /// </summary>
    /// <remarks>
    /// Any JNI failure is reported once through <see cref="ILogService"/> and turns the device off for the session: a
    /// missing vibrator is a legal state, a throwing one is a bug, and neither may break the hit feedback around it.
    /// </remarks>
    public sealed class AndroidHapticDevice : IHapticDevice
    {
        private const string Source = "Haptics";
        /// <summary>How long a read of the system haptic switch is trusted. The player rarely flips it mid-match.</summary>
        private const float SystemSettingCacheSeconds = 5f;

        private readonly ILogService _log;
        private AndroidJavaObject _vibrator;
        private AndroidJavaObject _resolver;
        private AndroidJavaClass _settingsSystem;
        private AndroidJavaClass _effectClass;
        private int _sdk;
        private bool _failed;
        private float _systemCheckedAt = float.NegativeInfinity;
        private bool _systemAllows = true;

        public AndroidHapticDevice(ILogService log)
        {
            _log = log;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                _sdk = version.GetStatic<int>("SDK_INT");

                // API 31 moved the vibrator behind VibratorManager; the plain service is deprecated there.
                _vibrator = _sdk >= 31
                    ? activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager")
                              .Call<AndroidJavaObject>("getDefaultVibrator")
                    : activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                // A phone with no vibrator motor is a legal state, not a failure: no buzz, no error.
                if (_vibrator != null && !_vibrator.Call<bool>("hasVibrator"))
                {
                    _log?.Info(Source, "This device has no vibrator; haptics are off.");
                    _vibrator.Dispose();
                    _vibrator = null;
                }

                _resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                _settingsSystem = new AndroidJavaClass("android.provider.Settings$System");
                if (_sdk >= 26) _effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            }
            catch (System.Exception e)
            {
                Fail("Could not reach the Android vibrator", e);
            }
        }

        public bool CanBuzz => !_failed && _vibrator != null && SystemAllows();

        public void Play(HapticPattern pattern)
        {
            if (_failed || _vibrator == null) return;
            try
            {
                if (_effectClass == null)
                {
                    // API < 26 has no amplitude control: one plain duration (the total of the pattern).
                    _vibrator.Call("vibrate", pattern.TotalMs);
                    return;
                }

                AndroidJavaObject effect = pattern.IsOneShot
                    ? _effectClass.CallStatic<AndroidJavaObject>("createOneShot", pattern.TimingsMs[1], pattern.Amplitudes[1])
                    : _effectClass.CallStatic<AndroidJavaObject>("createWaveform", pattern.TimingsMs, pattern.Amplitudes, -1);
                using (effect) _vibrator.Call("vibrate", effect);
            }
            catch (System.Exception e)
            {
                Fail("Vibrate call failed", e);
            }
        }

        /// <summary>The system "vibrate on touch" switch (Settings.System.HAPTIC_FEEDBACK_ENABLED), default on when unreadable.</summary>
        private bool SystemAllows()
        {
            float now = Time.unscaledTime;
            if (now - _systemCheckedAt < SystemSettingCacheSeconds) return _systemAllows;
            _systemCheckedAt = now;
            try
            {
                _systemAllows = _settingsSystem.CallStatic<int>("getInt", _resolver, "haptic_feedback_enabled", 1) != 0;
            }
            catch (System.Exception e)
            {
                // Not queryable on this device: keep buzzing (the player's own toggle still applies) but say so once.
                _log?.Warn(Source, $"System haptic setting unreadable ({e.GetType().Name}); assuming enabled.");
                _systemAllows = true;
                _systemCheckedAt = float.PositiveInfinity;
            }
            return _systemAllows;
        }

        private void Fail(string what, System.Exception e)
        {
            _failed = true;
            _log?.Error(Source, $"{what}; haptics are off for this session.", e);
        }
    }
}
