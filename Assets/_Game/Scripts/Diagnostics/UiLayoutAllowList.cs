using System;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// One known, accepted exception to a <see cref="UiRule"/>. The rules stay strict; a case that is acceptable by design is
    /// written down here, with the reason, instead of loosening a threshold for everyone.
    /// </summary>
    /// <remarks>
    /// Selectors: <c>Name</c> matches an element's <c>name</c>, <c>.class</c> one of its USS classes; for U6 the selector is
    /// the document label (its UXML asset name). A pair entry (<see cref="Other"/> set) matches in either order; a single
    /// entry matches any offender whose element (or, for a pair rule, either side) the selector names.
    /// <see cref="MinFontPx"/> &gt; 0 (U4 only): the case is accepted down to that size, never below it.
    /// </remarks>
    public readonly struct UiAllowEntry
    {
        public readonly UiRule Rule;
        public readonly string Element;
        public readonly string Other;
        public readonly float MinFontPx;
        public readonly string Reason;

        public UiAllowEntry(UiRule rule, string element, string other, float minFontPx, string reason)
        {
            Rule = rule; Element = element; Other = other; MinFontPx = minFontPx; Reason = reason;
        }
    }

    /// <summary>
    /// The UI layout probe's accepted exceptions (WP8). Add an entry only with a reason a reviewer can check against the
    /// spec or a pinned test; an entry is never a way to make a red run green.
    /// </summary>
    public static class UiLayoutAllowList
    {
        public static readonly UiAllowEntry[] Entries =
        {
            // ---- U1: overlap by design ------------------------------------------------------------------------------
            new UiAllowEntry(UiRule.U1, ".cw-ability-card__band", ".cw-ability-badge", 0f,
                "slot-number badge on a picked deck card: drawn on purpose over the card's top-right corner, so it overlaps the " +
                "category band's corner by 1-2 px2 (CluckWarsTheme.uss .cw-ability-badge top -16 / right -14; art, not a layout bug)"),

            // ---- U2: touch targets ---------------------------------------------------------------------------------
            new UiAllowEntry(UiRule.U2, "Ability2", "Ability3", 0f,
                "thumb-arc hexes: their 150 px bounding boxes are 10 px apart, but the art is a hexagon (the box corners " +
                "are empty) and the centres are 185 px apart; TouchHudLayoutTests pins the arc by centre distance (spec §1.5)"),

            // ---- U3: ellipsis by design (spec §3.5 U3 allow-list) -------------------------------------------------------
            new UiAllowEntry(UiRule.U3, ".cw-lb-name", null, 0f,
                "leaderboard names ellipsize instead of pushing the bar (MatchTopBar.uss .cw-lb-name, spec §1.5)"),
            new UiAllowEntry(UiRule.U3, ".cw-me-pod__name", null, 0f,
                "podium names ellipsize on the 4-plate draw podium (spec §1.6)"),

            // ---- U4: menu captions on the 28 px HUD floor rather than the 30 px menu floor ------------------------------
            new UiAllowEntry(UiRule.U4, ".cw-menu-build", null, 28f,
                "build stamp: a tester's reference, deliberately quiet (CluckWarsTheme.uss .cw-menu-build)"),
            new UiAllowEntry(UiRule.U4, ".cw-ability-mono", null, 28f,
                "monogram fallback shown only for an ability without icon art (AbilityIconStyle)"),
            new UiAllowEntry(UiRule.U4, ".cw-slot-box__num", null, 28f,
                "slot number badge: repeats the in-match button index, not load-bearing"),
            new UiAllowEntry(UiRule.U4, ".cw-ability-badge", null, 28f,
                "slot number on a picked card: repeats the slot box, not load-bearing"),
            new UiAllowEntry(UiRule.U4, ".cw-starter-tag__text", null, 28f,
                "STARTER tag on a deck card: secondary caption"),
            new UiAllowEntry(UiRule.U4, ".cw-ability-card__name--long", null, 28f,
                "a deck-card name that cannot fit at 30 px steps down (MenuUiController.FitCardName); single words cannot wrap"),

            // ---- U6: documents outside the orientation contract --------------------------------------------------------
            new UiAllowEntry(UiRule.U6, "AbilityLab", null, 0f,
                "dev tool, landscape-forced while open (decision D10, spec §1.7): no orientation classes"),
        };

        /// <summary>
        /// The entry that accepts this offender, or null. <paramref name="matchesA"/> / <paramref name="matchesB"/> answer
        /// whether a selector names the offending element / the other element of a pair (null = no other element).
        /// <paramref name="fontPx"/> is the effective size for U4 (ignored otherwise).
        /// </summary>
        public static string Find(UiRule rule, Func<string, bool> matchesA, Func<string, bool> matchesB, float fontPx = 0f)
        {
            if (matchesA == null) return null;
            for (int i = 0; i < Entries.Length; i++)
            {
                var e = Entries[i];
                if (e.Rule != rule) continue;
                if (e.MinFontPx > 0f && fontPx < e.MinFontPx - UiLayoutRules.FontTolerancePx) continue;
                if (e.Other == null)
                {
                    if (matchesA(e.Element) || (matchesB != null && matchesB(e.Element))) return e.Reason;
                    continue;
                }
                if (matchesB == null) continue;
                if ((matchesA(e.Element) && matchesB(e.Other)) || (matchesA(e.Other) && matchesB(e.Element))) return e.Reason;
            }
            return null;
        }

        /// <summary>Selector match against a name and a class test (<c>.x</c> = class x, anything else = the name).</summary>
        public static bool Matches(string selector, string name, Func<string, bool> hasClass)
        {
            if (string.IsNullOrEmpty(selector)) return false;
            if (selector[0] == '.') return hasClass != null && hasClass(selector.Substring(1));
            return string.Equals(selector, name, StringComparison.Ordinal);
        }
    }
}
