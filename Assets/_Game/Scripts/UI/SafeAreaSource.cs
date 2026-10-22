using System;
using System.Globalization;
using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// The safe area every UI and HUD rule reads: <c>Screen.safeArea</c>, or in dev builds the
    /// <c>-cw_safearea l,b,r,t</c> override (insets in screen pixels from the left, bottom, right and top edges), so a PC
    /// window can stand in for a phone's punch-hole and gesture bar in the orientation captures. Shared by
    /// <see cref="SafeAreaPadding"/> and <c>HudControlZones</c>, so the UI padding and the chevron avoid-zones agree.
    /// </summary>
    /// <remarks>
    /// The insets are per physical screen edge and do not follow a rotation (a phone's notch moves from the top to a
    /// side); a capture run passes the set for the orientation it captures. Release builds ignore the argument.
    /// </remarks>
    public static class SafeAreaSource
    {
        public const string Arg = "-cw_safearea";
        private const string Source = "SafeArea";

        private static bool _read, _hasOverride, _logged;
        private static Vector4 _insetsPx;
        private static string _error;

        /// <summary>The safe area in screen pixels (bottom-left origin), like <c>Screen.safeArea</c>.</summary>
        public static Rect Current
        {
            get
            {
                ReadArgsOnce();
                return _hasOverride ? FromInsets(_insetsPx, Screen.width, Screen.height) : Screen.safeArea;
            }
        }

        /// <summary>
        /// Parses <c>-cw_safearea l,b,r,t</c> from <paramref name="args"/>. False with a null <paramref name="error"/> when
        /// the argument is absent; false with an error when it is present but not four non-negative numbers.
        /// </summary>
        public static bool TryParse(string[] args, out Vector4 insetsPx, out string error)
        {
            insetsPx = Vector4.zero;
            error = null;
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], Arg, StringComparison.OrdinalIgnoreCase)) continue;
                string value = i + 1 < args.Length ? args[i + 1] : null;
                string[] parts = value?.Split(',');
                if (parts == null || parts.Length != 4)
                {
                    error = $"{Arg} wants four comma-separated pixel insets 'l,b,r,t', got '{value ?? "(nothing)"}'.";
                    return false;
                }
                var v = new float[4];
                for (int k = 0; k < 4; k++)
                {
                    if (!float.TryParse(parts[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]) || v[k] < 0f)
                    {
                        error = $"{Arg} inset '{parts[k]}' is not a non-negative number (value '{value}').";
                        return false;
                    }
                }
                insetsPx = new Vector4(v[0], v[1], v[2], v[3]);
                return true;
            }
            return false;
        }

        /// <summary>The safe rect left by <paramref name="insetsPx"/> (l, b, r, t) on a <paramref name="width"/> x
        /// <paramref name="height"/> screen; insets wider than the screen leave an empty rect, never a negative one.</summary>
        public static Rect FromInsets(Vector4 insetsPx, int width, int height)
        {
            float xMin = Mathf.Min(insetsPx.x, width), yMin = Mathf.Min(insetsPx.y, height);
            float xMax = Mathf.Max(xMin, width - insetsPx.z), yMax = Mathf.Max(yMin, height - insetsPx.w);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>One line per run: the active override, or why the argument was rejected. Silent when it is absent.</summary>
        public static void LogOnce(ILogService log)
        {
            ReadArgsOnce();
            if (_logged || log == null) return;
            _logged = true;
            if (_error != null) log.Error(Source, $"{_error} The real Screen.safeArea is used.");
            else if (_hasOverride) log.Info(Source, $"Safe-area override {Arg} l,b,r,t = {_insetsPx.x},{_insetsPx.y},{_insetsPx.z},{_insetsPx.w} px.");
        }

        private static void ReadArgsOnce()
        {
            if (_read) return;
            _read = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string[] args = Environment.GetCommandLineArgs();
#else
            string[] args = null;   // release builds never take the override
#endif
            _hasOverride = TryParse(args, out _insetsPx, out _error);
        }
    }
}
