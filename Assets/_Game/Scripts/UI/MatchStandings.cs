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

        /// <summary>
        /// Whether a corner gets a row on the HUD leaderboard and the post-match standings: a chicken
        /// stands on it, or it banked food (a player who left keeps their score). An unclaimed corner
        /// in a 1-3 player session gets no row of zeros.
        /// </summary>
        public static bool Listed(bool hasChicken, float total) => hasChicken || total > 0f;

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
        /// The win banner, decided from the standings alone so no caller can forget the draw:
        /// "EMPTY NESTS!" when nobody banked a single food (<see cref="IsDraw"/>, whatever corner
        /// GameManager named), else "YOU WIN!" for the local winner, "{NAME} WINS!" for anyone else,
        /// "MATCH ENDED" when the named corner is not in the standings (or none was named).
        /// </summary>
        public static string WinBanner(IReadOnlyList<Entry> ranked, int winnerCorner)
        {
            if (IsDraw(ranked)) return UiText.Get(UiKeys.PostmatchDraw);
            int idx = IndexOfCorner(ranked, winnerCorner);
            if (idx < 0) return UiText.Get(UiKeys.PostmatchEnded);
            var winner = ranked[idx];
            if (winner.IsLocal) return UiText.Get(UiKeys.PostmatchYouWin);
            return UiText.Format(UiKeys.PostmatchWins, ("name", DisplayName(winner).ToUpperInvariant()));
        }

        /// <summary>Index of the entry on <paramref name="corner"/>, or -1 (also for corner &lt; 0).</summary>
        public static int IndexOfCorner(IReadOnlyList<Entry> ranked, int corner)
        {
            if (corner < 0) return -1;
            for (int i = 0; i < ranked.Count; i++) if (ranked[i].Corner == corner) return i;
            return -1;
        }

        /// <summary>
        /// The line under the banner. Normally the winner's class. When the winner shares 1st with
        /// someone (equal shown food) it names how the tie was settled, using what GameManager
        /// actually does on a timer tie: exact food, then KOs (visible in the KO column), then the lower
        /// corner. Only the KO decision is something a player can see, so any other tie reads
        /// "Tied for 1st!" and never claims a reason it cannot show. Null when there is no winner.
        /// </summary>
        public static string WinSubLine(IReadOnlyList<Entry> ranked, int winnerCorner)
        {
            if (IsDraw(ranked)) return null;
            int idx = IndexOfCorner(ranked, winnerCorner);
            if (idx < 0) return null;
            var winner = ranked[idx];
            int top = Score(winner);
            bool tied = false, koDecided = true;
            for (int i = 0; i < ranked.Count; i++)
            {
                if (i == idx || Score(ranked[i]) != top) continue;
                tied = true;
                // KOs settle it only when the exact totals were level (GameManager compares exact food
                // first) and the winner has strictly more knockouts than this rival.
                if (!UnityEngine.Mathf.Approximately(ranked[i].Total, winner.Total) || winner.Kills <= ranked[i].Kills)
                    koDecided = false;
            }
            if (!tied) return UiText.Format(UiKeys.PostmatchWinSub, ("cls", ClassName(winner.Class)));
            return UiText.Get(koDecided ? UiKeys.PostmatchTiedKos : UiKeys.PostmatchTied);
        }

        /// <summary>The food a chicken is shown with (and ranked by): whole units, rounded down.</summary>
        public static int Score(in Entry e) => (int)System.Math.Floor(e.Total);

        /// <summary>
        /// Final order: most food (as shown) first; on a tie the declared winner first (GameManager's
        /// call is authoritative - it breaks food ties on knockouts, then corner - so the podium never
        /// contradicts the banner), then the larger exact total, then the lower corner.
        /// </summary>
        public static List<Entry> Ranked(IEnumerable<Entry> entries, int winnerCorner)
        {
            var list = new List<Entry>(entries);
            list.Sort((a, b) =>
            {
                int byScore = Score(b).CompareTo(Score(a));
                if (byScore != 0) return byScore;
                bool aw = a.Corner == winnerCorner, bw = b.Corner == winnerCorner;
                if (aw != bw) return aw ? -1 : 1;
                int byTotal = b.Total.CompareTo(a.Total);
                if (byTotal != 0) return byTotal;
                return a.Corner.CompareTo(b.Corner);
            });
            return list;
        }

        /// <summary>
        /// Placing (1-based) of each entry of a <see cref="Ranked"/> list, competition style: equal
        /// shown food = the same place, and the next place skips (40, 20, 20, 5 -> 1, 2, 2, 4).
        /// The podium step height and every medal follow the place, so tied birds stand level.
        /// </summary>
        public static int[] Places(IReadOnlyList<Entry> ranked)
        {
            var places = new int[ranked.Count];
            for (int i = 0; i < ranked.Count; i++)
                places[i] = i > 0 && Score(ranked[i]) == Score(ranked[i - 1]) ? places[i - 1] : i + 1;
            return places;
        }

        /// <summary>
        /// Nobody banked anything (every shown score is 0, or nobody is listed): a draw. GameManager
        /// still names a corner (its tie-breaks), but crowning a 0 would be a lie, so the screen
        /// shows "EMPTY NESTS!" / "No winner this round." and no podium; the board still lists everyone.
        /// </summary>
        public static bool IsDraw(IReadOnlyList<Entry> ranked)
        {
            for (int i = 0; i < ranked.Count; i++) if (Score(ranked[i]) > 0) return false;
            return true;
        }

        /// <summary>"1st".."4th" from the shared hud.rank.* keys (the HUD uses the same ones).</summary>
        public static string Ordinal(int place) => UiText.Get(place switch
        {
            1 => UiKeys.HudRank1,
            2 => UiKeys.HudRank2,
            3 => UiKeys.HudRank3,
            _ => UiKeys.HudRank4,
        });

        /// <summary>
        /// The "You · 2nd" / "You · tied 1st" chip when someone else won: the local player's place
        /// (the tied form when another bird shares it). Null (no chip) when the local player won, on a
        /// draw / no winner, or when they are not listed. The controller puts it on the local player's
        /// own podium step (or the headline row off the podium), never under the winner's class tag.
        /// </summary>
        public static string YouPlaceChip(IReadOnlyList<Entry> ranked, int winnerCorner)
        {
            if (IsDraw(ranked) || IndexOfCorner(ranked, winnerCorner) < 0) return null;
            int you = -1;
            for (int i = 0; i < ranked.Count; i++) if (ranked[i].IsLocal) { you = i; break; }
            if (you < 0 || ranked[you].Corner == winnerCorner) return null;
            var places = Places(ranked);
            bool shared = false;
            for (int i = 0; i < places.Length; i++) if (i != you && places[i] == places[you]) shared = true;
            string youName = UiText.Get(UiKeys.LabelYou), place = Ordinal(places[you]);
            if (shared) return UiText.Format(UiKeys.PostmatchYouPlaceTied, ("you", youName), ("place", place));
            return UiText.Format(UiKeys.PostmatchYouPlace, ("you", youName), ("place", place));
        }

        /// <summary>
        /// Index into the ranked list shown on each podium step, left to right: 2nd, 1st, 3rd. Steps
        /// with nobody to show are removed from the layout (display: none), so a 2-bird podium is
        /// two steps centred, not 2nd + 1st with an empty gap on the right.
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
