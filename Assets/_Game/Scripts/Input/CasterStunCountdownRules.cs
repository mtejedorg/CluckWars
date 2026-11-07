using System.Globalization;
using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// A4 (spec 2026-11-06 §4.2): while the local bird's last Stun (WingSlam) cast still holds a rival, the Stun hex
    /// centre shows the LARGEST remaining stun as a one-decimal digit. Pure rules, no Unity objects; the HUD
    /// (<see cref="TouchControlsController"/>, both the touch arc and the desktop / pad strip) feeds it replicated state.
    /// <para>
    /// Approximation of "stunned by this caster": replicated state carries no stun source, so a rival counts when the
    /// caster's last cast was a Stun that hit someone, the cast is still inside its own stun length plus
    /// <see cref="Visuals.FeedbackTuning.CasterStunCountdownSlackSeconds"/>, and the rival's remaining stun is no longer
    /// than what this cast could still have left (stun length - time since cast + slack). A stun from another bird
    /// (or a Death Mark kill) that lands on a rival inside that same short window can still be counted; the digit then
    /// shows at most this cast's own remaining time, so it never overstates his stun.
    /// </para>
    /// </summary>
    public static class CasterStunCountdownRules
    {
        /// <summary>
        /// Should the digit show, and for how many seconds? <paramref name="rivalStunRemaining"/>[0..count) holds every
        /// rival's <c>StunRemaining</c> (never the caster's own). No allocation.
        /// </summary>
        public static bool TryGetSeconds(bool lastCastIsWingSlam, int lastCastHitCount, float secondsSinceCast,
            float castStunSeconds, float[] rivalStunRemaining, int count, out float seconds)
        {
            seconds = 0f;
            if (!lastCastIsWingSlam || lastCastHitCount <= 0 || rivalStunRemaining == null) return false;

            float slack = Visuals.FeedbackTuning.CasterStunCountdownSlackSeconds;
            if (secondsSinceCast < 0f || secondsSinceCast > castStunSeconds + slack) return false;

            float ceiling = castStunSeconds - secondsSinceCast + slack;
            int n = Mathf.Min(count, rivalStunRemaining.Length);
            for (int i = 0; i < n; i++)
            {
                float r = rivalStunRemaining[i];
                if (r > 0f && r <= ceiling && r > seconds) seconds = r;
            }
            return seconds > 0f;
        }

        /// <summary>The displayed tenth, rounded up so a live stun never reads "0.0". The HUD reformats only when this moves.</summary>
        public static int Tenths(float seconds) => Mathf.Max(1, Mathf.CeilToInt(seconds * 10f - 0.001f));

        /// <summary>"1.1" style, invariant culture (never "1,1"). No unit: the hex says what it counts.</summary>
        public static string Format(int tenths) => (tenths / 10f).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
