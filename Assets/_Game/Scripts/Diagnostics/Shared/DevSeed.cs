using System;
using System.Globalization;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// <c>-cw_seed N</c> (dev tools, solo): one number that fixes what a solo match otherwise rolls at random - the arena's
    /// interior obstacles, the corner permutation and the bot kit lineup - and seeds <c>UnityEngine.Random</c> once, so two
    /// runs with the same seat flags start from the same match (BL-208). Absent, or in a production build
    /// (<see cref="DevTools.Available"/> false), nothing changes. It lives in the Core assembly because the map and the
    /// bootstrapper (Gameplay) read it; it reads the process command line itself, so no start-up order matters.
    /// </summary>
    public static class DevSeed
    {
        public const string Key = "cw_seed";

        private static bool _read;
        private static bool _has;
        private static int _value;

        /// <summary>The seed the command line asked for; false when none was (or this is a production build).</summary>
        public static bool TryGet(out int seed)
        {
            if (!_read)
            {
                _read = true;
                _has = DevTools.Available && TryFromArgs(Environment.GetCommandLineArgs(), out _value);
                if (_has) UnityEngine.Random.InitState(_value);
            }
            seed = _value;
            return _has;
        }

        /// <summary>Pure: finds <c>-cw_seed N</c> (one or two dashes, any case) in <paramref name="args"/>.</summary>
        public static bool TryFromArgs(string[] args, out int seed)
        {
            seed = 0;
            if (args == null) return false;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                var a = args[i];
                if (a == null) continue;
                a = a.TrimStart('-');
                if (string.Equals(a, Key, StringComparison.OrdinalIgnoreCase)) return TryParse(args[i + 1], out seed);
            }
            return false;
        }

        public static bool TryParse(string value, out int seed) =>
            int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
    }
}
