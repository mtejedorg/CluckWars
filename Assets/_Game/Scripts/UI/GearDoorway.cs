namespace CluckWars.UI
{
    /// <summary>
    /// GEAR UP's doorway bird (round-2 finding 7): it stands in its own layout slot, the height left
    /// between the slot row and the deck. A slot shorter than <see cref="MinBirdHeight"/> panel points
    /// shows no bird at all rather than a bird too small to read (4:3, where the class cards wrap to a
    /// second line). Never a partial bird: nothing overlaps the slot.
    /// </summary>
    public static class GearDoorway
    {
        /// <summary>Smallest doorway, in panel points, that still shows the bird (the 20:9 phone gets ~120, 4:3 ~76).</summary>
        public const float MinBirdHeight = 110f;

        /// <summary>True when a doorway of <paramref name="height"/> panel points shows the bird.</summary>
        public static bool Fits(float height) => !float.IsNaN(height) && height >= MinBirdHeight;
    }
}
