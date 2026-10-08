using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Localization;

namespace CluckWars.UI
{
    /// <summary>
    /// Pure rules behind the post-match screen and the in-match lobby (menu overhaul Phase 4):
    /// who a chicken is called, the final ranking, which ranks stand on which podium step and
    /// whether the KO column earns its space. No Unity objects, so EditMode tests cover it.
    /// </summary>
    /// <remarks>
    /// <b>Names (re-audit finding 16).</b> The match numbers players by <i>corner</i>
    /// (<c>HomeCornerIndex + 1</c>), and corners are a shuffled permutation
    /// (<c>MatchBootstrapper.ShuffledCorner</c>), so the corner number never matched THE COOP's
    /// seat numbers: the human always sits in seat 1 ("You"), the solo bots in seats 2-4 as
    /// DashFox / BrunoB / PeckNoir. Identity therefore never comes from the corner here:
    /// the local chicken is "You", a solo bot is named by its class (bot <i>i</i> spawns as
    /// <see cref="SoloBotClasses"/>[i], the same order the Coop seats them), and only a remote
    /// human, who has no other identity yet, is "P{corner + 1}", the number the in-match lobby
    /// grid and the HUD use for them.
    /// </remarks>
    public static class MatchStandings
    {
        /// <summary>
        /// Solo bot classes in spawn / seat order. Mirrors <c>MatchBootstrapper.TrySpawnBots</c>
        /// (bot i = class i) and <c>MenuUiController.BotClasses</c> (seat i + 1 = class i, named
        /// <c>lobby.bot.(i + 1)</c>); <c>MatchStandingsTests</c> fails if either copy drifts.
        /// </summary>
        public static readonly ChickenClass[] SoloBotClasses = { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        private static readonly string[] BotNameKeys = { UiKeys.LobbyBot1, UiKeys.LobbyBot2, UiKeys.LobbyBot3 };

        /// <summary>One chicken's final line: identity plus what it banked.</summary>
        public readonly struct Entry
        {
            public readonly int Corner;
            public readonly float Total;
            public readonly int Kills;
            public readonly bool IsLocal;
            public readonly bool IsBot;
            public readonly ChickenClass Class;
            /// <summary>False when no live chicken stands on this corner's base (left the match).</summary>
            public readonly bool HasChicken;

            public Entry(int corner, float total, int kills, bool isLocal, bool isBot, ChickenClass cls, bool hasChicken = true)
            {
                Corner = corner; Total = total; Kills = kills;
                IsLocal = isLocal; IsBot = isBot; Class = cls; HasChicken = hasChicken;
            }
        }

        /// <summary>Seat-order index (0..2) of a solo bot of <paramref name="cls"/>, or -1.</summary>
        public static int BotIndex(ChickenClass cls) => System.Array.IndexOf(SoloBotClasses, cls);

        /// <summary>What a chicken is called on the overlays: "You", the Coop's bot name, else "P{corner+1}".</summary>
        public static string DisplayName(bool isLocal, bool isBot, ChickenClass cls, int corner)
        {
            if (isLocal) return UiText.Get(UiKeys.LabelYou);
            if (isBot)
            {
                int i = BotIndex(cls);
                if (i >= 0) return UiText.Get(BotNameKeys[i]);
            }
            return UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1));
        }

        public static string DisplayName(in Entry e) => DisplayName(e.IsLocal, e.IsBot, e.Class, e.Corner);

        /// <summary>Player-facing class name from the wording dictionary (class.*.short).</summary>
        public static string ClassName(ChickenClass cls) => UiText.Get(cls switch
        {
            ChickenClass.Speedy   => UiKeys.ClassSpeedyShort,
            ChickenClass.Fatty    => UiKeys.ClassFattyShort,
            ChickenClass.Assassin => UiKeys.ClassAssassinShort,
            _                     => UiKeys.ClassWarriorShort,
        });

        /// <summary>The class's role from the wording dictionary (role.*), as THE COOP's seat line shows it.</summary>
        public static string RoleName(ChickenClass cls) => UiText.Get(cls switch
        {
            ChickenClass.Speedy   => UiKeys.RoleSpeedy,
            ChickenClass.Fatty    => UiKeys.RoleFatty,
            ChickenClass.Assassin => UiKeys.RoleAssassin,
            _                     => UiKeys.RoleWarrior,
        });

        /// <summary>"{CLASS} · {role}": the seat line of THE COOP and of the in-match waiting room.</summary>
        public static string ClassRoleLine(ChickenClass cls) =>
            UiText.Format(UiKeys.LobbyClassLine, ("cls", ClassName(cls)), ("role", RoleName(cls)));

        /// <summary>
        /// The win banner: "YOU WIN!" for the local player, "{NAME} WINS!" for anyone else,
        /// "MATCH ENDED" with no winner.
        /// </summary>
        public static string WinBanner(bool hasWinner, in Entry winner)
        {
            if (!hasWinner) return UiText.Get(UiKeys.PostmatchEnded);
            if (winner.IsLocal) return UiText.Get(UiKeys.PostmatchYouWin);
            return UiText.Format(UiKeys.PostmatchWins, ("name", DisplayName(winner).ToUpperInvariant()));
        }

        /// <summary>
        /// Final order: most food first; on a tie the declared winner first (GameManager's call
        /// is authoritative, so the podium never contradicts the banner), then the lower corner.
        /// </summary>
        public static List<Entry> Ranked(IEnumerable<Entry> entries, int winnerCorner)
        {
            var list = new List<Entry>(entries);
            list.Sort((a, b) =>
            {
                int byTotal = b.Total.CompareTo(a.Total);
                if (byTotal != 0) return byTotal;
                bool aw = a.Corner == winnerCorner, bw = b.Corner == winnerCorner;
                if (aw != bw) return aw ? -1 : 1;
                return a.Corner.CompareTo(b.Corner);
            });
            return list;
        }

        /// <summary>
        /// Rank (0 = first) shown on each podium step, left to right: 2nd, 1st, 3rd. Steps whose
        /// rank is not in the standings stay hidden.
        /// </summary>
        public static readonly int[] PodiumRankBySlot = { 1, 0, 2 };

        /// <summary>The KO column only earns its space once someone has knocked a chicken out.</summary>
        public static bool ShowKoColumn(IEnumerable<Entry> entries)
        {
            foreach (var e in entries) if (e.Kills > 0) return true;
            return false;
        }

        /// <summary>True when the winner reached the food goal (vs. won on the timer).</summary>
        public static bool GoalReached(float winnerTotal, int target) => target > 0 && winnerTotal >= target;
    }
}
