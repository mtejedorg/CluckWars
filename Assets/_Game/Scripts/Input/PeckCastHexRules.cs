using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// BL-212 / BL-213: the two Peck-cast cues on the local player's Peck hex. The hex fills (cream, bottom-up) over the cast, and flashes
    /// when a stun, shove, execution or emptied pile cuts it. Pure rules, no Unity objects beyond <see cref="Mathf"/>; the HUD
    /// (<see cref="TouchControlsController"/>, touch arc and desktop / pad strip alike) feeds them the replicated slot state.
    /// </summary>
    /// <remarks>
    /// <b>No second timer.</b> The fill reads the same fraction the buff ring drains from, <c>AbilityController.ActiveRemaining01For</c>:
    /// the slot's replicated timer over <c>ResolveActiveDuration</c>, which is <c>PeckCastRules.CastSeconds</c> of the caster's stats (so a
    /// balance delta on <c>PeckCastSeconds</c> moves the fill, the animation and the ring together). Nothing here holds a duration.
    /// </remarks>
    public static class PeckCastHexRules
    {
        /// <summary>USS class of the code-built cut flash laid over the hex.</summary>
        public const string CutFlashClass = "cw-hex-peckcut";

        /// <summary>
        /// How much of the hex the cast has filled: 0 when the slot is not running (the cast ended or was cut, so the cooldown sweep
        /// alone remains), otherwise elapsed / effective cast, that is one minus the slot's remaining fraction.
        /// </summary>
        public static float Fill01(bool slotActive, float remaining01) =>
            slotActive ? Mathf.Clamp01(1f - remaining01) : 0f;

        /// <summary>Is the fill layer shown: only on a Peck hex, only while its cast runs.</summary>
        public static bool FillShown(bool isPeckHex, bool slotActive) => isPeckHex && slotActive;

        /// <summary>Opacity of the cut flash <paramref name="elapsed"/> seconds in: full at the start, linear to 0 at <paramref name="seconds"/>.
        /// 0 outside the flash. Opacity only, no movement, so it needs no Reduced Motion variant.</summary>
        public static float FlashOpacity(float elapsed, float seconds)
        {
            if (seconds <= 0f || elapsed < 0f || elapsed >= seconds) return 0f;
            return 1f - elapsed / seconds;
        }
    }
}
