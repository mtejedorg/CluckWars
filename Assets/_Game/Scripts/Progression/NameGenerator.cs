using System;
using System.Collections.Generic;
using System.Globalization;

namespace CluckWars.Progression
{
    /// <summary>
    /// Makes the player's display name: an adjective, a noun, then <c>#</c> and four digits
    /// (<c>SwiftBeak#2213</c>). Pure — the caller supplies the <see cref="System.Random"/>, so the same
    /// seed always makes the same name and tests never depend on a real clock.
    /// </summary>
    /// <remarks>
    /// <b>Names are generated, never typed.</b> There is no free-text field anywhere in the game
    /// (Maestro's ruling, 2026-09-14): a local-only build has no moderation, and a typed name is the
    /// one string that would later have to be shown to other players. Re-rolling is the whole of the
    /// player's control over it.
    /// <para>
    /// The word lists are vetted so that <b>every</b> adjective+noun pair is inoffensive and fits
    /// <see cref="ProfileEvent.MaxNameLength"/> — there are only <c>Adjectives × Nouns</c> of them and
    /// the tests check both properties over the whole product, not a sample.
    /// </para>
    /// </remarks>
    public static class NameGenerator
    {
        /// <summary>How many digits follow the <c>#</c>, and so the number's range (0..9999).</summary>
        public const int DiscriminatorDigits = 4;

        private const int DiscriminatorRange = 10000;

        /// <summary>Attempts to differ from the current name before the number is nudged instead.</summary>
        private const int RerollAttempts = 8;

        /// <summary>Farmyard adjectives. ASCII letters only, capitalised.</summary>
        public static readonly IReadOnlyList<string> Adjectives = new[]
        {
            "Amber", "Autumn", "Barnyard", "Brave", "Clever", "Crafty", "Daring", "Dusty",
            "Eager", "Feathered", "Frosty", "Gentle", "Golden", "Hasty", "Hearty", "Humble",
            "Jolly", "Lucky", "Merry", "Mighty", "Nimble", "Noble", "Plucky", "Quiet",
            "Rowdy", "Ruffled", "Rustic", "Scrappy", "Silver", "Sleepy", "Speckled", "Spry",
            "Stormy", "Sturdy", "Sunny", "Swift", "Tidy", "Wheaten", "Wily", "Zesty",
        };

        /// <summary>Farmyard nouns. ASCII letters only, capitalised.</summary>
        public static readonly IReadOnlyList<string> Nouns = new[]
        {
            "Bantam", "Barn", "Beak", "Comb", "Coop", "Crest", "Duckling", "Egg",
            "Furrow", "Gosling", "Granary", "Harvest", "Haystack", "Hedgerow", "Meadow", "Millstone",
            "Nest", "Orchard", "Paddock", "Pasture", "Perch", "Pitchfork", "Plume", "Pullet",
            "Quill", "Roost", "Rooster", "Scarecrow", "Shell", "Sickle", "Silo", "Stable",
            "Talon", "Thresher", "Trough", "Windmill", "Wing",
        };

        /// <summary>How many distinct word pairs exist, for the tests and for diagnostics.</summary>
        public static int Combinations => Adjectives.Count * Nouns.Count;

        /// <summary>
        /// A new name. When <paramref name="current"/> is given, the result is never equal to it: a
        /// re-roll that returned the same name would read as a broken button.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
        public static string Next(System.Random random, string current = null)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            string name = Draw(random);
            for (int attempt = 0; attempt < RerollAttempts && Equal(name, current); attempt++)
            {
                name = Draw(random);
            }

            // Astronomically unlikely (one chance in Combinations x 10000, eight times over), but a
            // re-roll must be *guaranteed* to change something, so the number is nudged rather than
            // the loop being trusted.
            if (Equal(name, current)) name = NudgeDiscriminator(name);
            return name;
        }

        private static string Draw(System.Random random) =>
            Adjectives[random.Next(Adjectives.Count)] +
            Nouns[random.Next(Nouns.Count)] +
            "#" + random.Next(DiscriminatorRange).ToString("D" + DiscriminatorDigits.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);

        private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        /// <summary>The same words with the next number, wrapping at <see cref="DiscriminatorRange"/>.</summary>
        private static string NudgeDiscriminator(string name)
        {
            int hash = name.LastIndexOf('#');
            if (hash < 0 || !int.TryParse(name.Substring(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                return name; // not a shape this makes; Next never produces one, so nothing to nudge
            }

            number = (number + 1) % DiscriminatorRange;
            return name.Substring(0, hash + 1) +
                   number.ToString("D" + DiscriminatorDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
