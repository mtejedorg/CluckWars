using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Services;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Round-2 findings 1, 5 and 9: ONE player palette keyed by spawn corner (source-scan enforced), the
    /// corner each menu seat will spawn on, the YOU mark, cream HUD names and the category shape marks.
    /// </summary>
    public sealed class PlayerPaletteTests
    {
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string Read(string rel) => File.ReadAllText(Path.Combine(ProjectRoot, rel));

        // ---- One palette ----------------------------------------------------------------------------

        [Test]
        public void ForCorner_IsThePaletteByCorner_AndNeutralWhenUnknown()
        {
            Assert.AreEqual(4, PlayerPalette.Count);
            Assert.AreEqual(PlayerPalette.P1, PlayerPalette.ForCorner(0));
            Assert.AreEqual(PlayerPalette.P2, PlayerPalette.ForCorner(1));
            Assert.AreEqual(PlayerPalette.P3, PlayerPalette.ForCorner(2));
            Assert.AreEqual(PlayerPalette.P4, PlayerPalette.ForCorner(3));
            Assert.AreEqual(PlayerPalette.P1, PlayerPalette.ForCorner(4), "corners wrap like the spawn points");
            Assert.AreEqual(PlayerPalette.Neutral, PlayerPalette.ForCorner(-1));
            for (int c = 0; c < 4; c++)
                Assert.AreNotEqual(PlayerPalette.Neutral, PlayerPalette.ForCorner(c));
        }

        /// <summary>
        /// The palette's four colours, as any copy would spell them: the hex value (C#, USS, UXML) or the
        /// float triple the old per-file arrays used. Anything outside PlayerPalette.cs that spells one is
        /// a second copy that can drift (round-2 finding 1 found six).
        /// </summary>
        [Test]
        public void NoOtherFile_DefinesItsOwnCopyOfThePlayerColours()
        {
            var colours = new[] { PlayerPalette.P1, PlayerPalette.P2, PlayerPalette.P3, PlayerPalette.P4 };
            var patterns = new List<Regex>();
            foreach (var c in colours)
            {
                patterns.Add(new Regex(ColorUtility.ToHtmlStringRGB(c), RegexOptions.IgnoreCase));
                // 2-decimal float triple as the old Color(r, g, b) arrays spelled it (f and spacing optional).
                string F(float v) => Regex.Escape(v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)) + "f?";
                patterns.Add(new Regex($@"{F(c.r)}\s*,\s*{F(c.g)}\s*,\s*{F(c.b)}"));
            }
            patterns.Add(new Regex(@"--cw-player-\d"));

            string self = Path.Combine("Assets", "_Game", "Scripts", "UI", "PlayerPalette.cs");
            var offenders = new List<string>();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(ProjectRoot, "Assets"), "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (ext != ".cs" && ext != ".uss" && ext != ".uxml") continue;
                string rel = file.Substring(ProjectRoot.Length + 1).Replace('/', Path.DirectorySeparatorChar);
                if (rel == self || rel.Contains("PackageCache") || rel.Contains(Path.DirectorySeparatorChar + "Plugins" + Path.DirectorySeparatorChar)) continue;
                string text = File.ReadAllText(file);
                foreach (var p in patterns)
                    if (p.IsMatch(text)) { offenders.Add($"{rel} ({p})"); break; }
            }
            Assert.IsEmpty(offenders, "Player colours must come from PlayerPalette.ForCorner, not a local copy:\n" +
                                      string.Join("\n", offenders));
        }

        [Test]
        public void EveryPlayerColourSurface_ReadsThePalette()
        {
            foreach (var rel in new[]
                     {
                         "Assets/_Game/Scripts/UI/MenuUiController.cs", "Assets/_Game/Scripts/UI/MatchOverlaysController.cs",
                         "Assets/_Game/Scripts/UI/MatchHudController.cs", "Assets/_Game/Scripts/Visuals/ChickenNameplate.cs",
                         "Assets/_Game/Scripts/Visuals/ChickenWorldBars.cs", "Assets/_Game/Scripts/Gameplay/PlayerBase.cs",
                     })
                StringAssert.Contains("PlayerPalette.ForCorner(", Read(rel), rel);
        }

        // ---- Corners known in the menu ---------------------------------------------------------------

        [Test]
        public void Permutation_IsAShuffleOfTheFourCorners_DeterministicPerSeed()
        {
            for (int seed = -5; seed < 200; seed++)
            {
                var p = CornerAssignment.Permutation(seed);
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, p, $"seed {seed}");
                CollectionAssert.AreEqual(p, CornerAssignment.Permutation(seed), $"seed {seed} must repeat");
            }
            var layouts = new HashSet<string>();
            for (int seed = 0; seed < 200; seed++) layouts.Add(string.Join(",", CornerAssignment.Permutation(seed)));
            Assert.Greater(layouts.Count, 12, "a solo session should not always spawn on the same corners");
        }

        [Test]
        public void Permutation_MatchesTheShuffleTheSpawnerUsedBefore()
        {
            // The pre-chunk-3 MatchBootstrapper.InitCornerPermutation, inlined: a session name must keep
            // producing the same corners (MapGenerator and old builds agree on the seed).
            foreach (var name in new[] { "cluck-lan", "ABC123", "Q7XK2P" })
            {
                int seed = CornerAssignment.SessionNameSeed(name);
                var expected = new[] { 0, 1, 2, 3 };
                var rng = new System.Random(seed);
                for (int i = expected.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (int a, int b) = (expected[i], expected[j]);
                    expected[i] = b; expected[j] = a;
                }
                CollectionAssert.AreEqual(expected, CornerAssignment.Permutation(CornerAssignment.SeedFor(SessionMode.Host, name, 0)), name);
            }
        }

        [Test]
        public void LobbySeatCorner_SoloAndHostSeatsKnowTheirCorner_AGuestDoesNot()
        {
            var perm = CornerAssignment.Permutation(CornerAssignment.SeedFor(SessionMode.Solo, null, 1234));
            // Solo: You = slot 0, the three bots = slots 1..3 in the Coop's seat order (MatchBootstrapper
            // spawns bot i on ShuffledCorner(i + 1) with MatchStandings.SoloBotClasses[i]).
            for (int seat = 0; seat < 4; seat++)
                Assert.AreEqual(perm[seat], CornerAssignment.LobbySeatCorner(SessionMode.Solo, seat, perm));
            Assert.AreEqual(perm[0], CornerAssignment.LobbySeatCorner(SessionMode.Host, 0, perm), "host = first joiner = slot 0");
            Assert.AreEqual(-1, CornerAssignment.LobbySeatCorner(SessionMode.Host, 0, null), "host before its join code");
            for (int seat = 0; seat < 4; seat++)
                Assert.AreEqual(-1, CornerAssignment.LobbySeatCorner(SessionMode.Join, seat, perm), "a guest's corner is unknown");
            Assert.AreEqual(1234, CornerAssignment.SeedFor(SessionMode.Solo, "cluck-lan", 1234), "solo ignores the session name");
        }

        [Test]
        public void TheSpawnerAndTheMenu_UseTheSameCornerRule()
        {
            string boot = Read("Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs");
            StringAssert.Contains("CornerAssignment.Permutation(CornerAssignment.SeedFor(mode, sessionName, soloSeed))", boot);
            StringAssert.Contains("_selection.SoloCornerSeed", boot);
            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("_selection.SoloCornerSeed = ", menu);
            StringAssert.Contains("CornerAssignment.LobbySeatCorner(", menu);
        }

        // ---- YOU mark --------------------------------------------------------------------------------

        [Test]
        public void YouMark_IsGoldOnInk_InTheUssAndTheWorldSprite()
        {
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(PlayerPalette.YouMarkFill, PlayerPalette.YouMarkInk), 7f);
            string uss = Read("Assets/UI/Styles/YouMark.uss");
            var rule = Regex.Match(uss, @"Label\." + PlayerPalette.YouMarkClass + @" \{([^}]*)\}");
            Assert.IsTrue(rule.Success, "YouMark.uss must define Label." + PlayerPalette.YouMarkClass);
            StringAssert.Contains("background-color: #" + ColorUtility.ToHtmlStringRGB(PlayerPalette.YouMarkFill).ToLowerInvariant(), rule.Groups[1].Value);
            StringAssert.Contains("color: #" + ColorUtility.ToHtmlStringRGB(PlayerPalette.YouMarkInk).ToLowerInvariant(), rule.Groups[1].Value);
            StringAssert.Contains("PlayerPalette.YouMarkFill", Read("Assets/_Game/Scripts/Visuals/ChickenNameplate.cs"));
        }

        [Test]
        public void YouMark_IsOnTheLocalPlayer_InLobbyHudWaitingRoomAndPostMatch()
        {
            foreach (var ux in new[] { "Assets/UI/Lobby.uxml", "Assets/UI/MatchTopBar.uxml", "Assets/UI/MatchOverlays.uxml" })
                StringAssert.Contains("Assets/UI/Styles/YouMark.uss", Read(ux), ux);
            StringAssert.Contains("v.Name.EnableInClassList(PlayerPalette.YouMarkClass, you)", Read("Assets/_Game/Scripts/UI/MenuUiController.cs"));
            StringAssert.Contains("rowRefs.Name.EnableInClassList(PlayerPalette.YouMarkClass, isLocal)", Read("Assets/_Game/Scripts/UI/MatchHudController.cs"));
            string ov = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("v.Name.EnableInClassList(PlayerPalette.YouMarkClass, e.IsLocal)", ov);       // podium
            StringAssert.Contains("name.EnableInClassList(PlayerPalette.YouMarkClass, e.IsLocal)", ov);         // standings row
            StringAssert.Contains("v.Name.EnableInClassList(PlayerPalette.YouMarkClass, filled && corner == localCorner)", ov); // waiting room
        }

        // ---- HUD names (finding 5) ---------------------------------------------------------------------

        [Test]
        public void HudLeaderboardNames_AreCream_AndClearAAOnThePanel()
        {
            string css = Read("Assets/UI/Styles/MatchTopBar.uss");
            var name = Regex.Match(css, @"\.cw-lb-name \{([^}]*)\}");
            Assert.IsTrue(name.Success);
            var col = Regex.Match(name.Groups[1].Value, @"(?<!-)color: rgb\((\d+), (\d+), (\d+)\)");
            Assert.IsTrue(col.Success, ".cw-lb-name must set a cream text colour");
            var text = new Color32(byte.Parse(col.Groups[1].Value), byte.Parse(col.Groups[2].Value), byte.Parse(col.Groups[3].Value), 255);
            Assert.AreEqual((Color32)UiGfx.TextPrimary, text, "names are the cream text colour");

            var panel = Regex.Match(css, @"\.cw-lb-panel \{[^}]*background-color: rgba\((\d+), (\d+), (\d+), ([\d.]+)\)");
            Assert.IsTrue(panel.Success);
            float a = float.Parse(panel.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            // Worst case: the translucent panel over a white arena.
            var bg = Color.Lerp(Color.white, new Color32(byte.Parse(panel.Groups[1].Value), byte.Parse(panel.Groups[2].Value), byte.Parse(panel.Groups[3].Value), 255), a);
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(text, bg), AbilityPalette.MinLabelContrast);
            StringAssert.DoesNotContain("rowRefs.Name.style.color", Read("Assets/_Game/Scripts/UI/MatchHudController.cs"),
                "the player colour belongs on the dot, not the name");
        }

        [Test]
        public void HudHexes_TintFromTheCategoryPalette_NotAccentColor()
        {
            string touch = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            StringAssert.Contains("AbilityPalette.HexColor(equipped)", touch);
            StringAssert.Contains("tint = AbilityPalette.Idle(accent);", touch);
            var noTarget = Regex.Match(touch, @"case AbilityRefusal\.NoTarget:(.*?)break;", RegexOptions.Singleline);
            Assert.IsTrue(noTarget.Success);
            StringAssert.DoesNotContain("NeutralNoEffectColor", noTarget.Groups[1].Value,
                "a hex with no target keeps its category hue (AbilityPalette.Idle), not the bare grey");
            StringAssert.DoesNotContain("equipped.AccentColor", touch);
        }

        // ---- Category marks (finding 9) ----------------------------------------------------------------

        [Test]
        public void CategoryMarks_HaveADistinctShapePerCategory_OnMenuAndHudHexes()
        {
            string uss = Read("Assets/UI/Styles/CategoryMark.uss");
            var mods = new HashSet<string>();
            foreach (AbilityCategory cat in System.Enum.GetValues(typeof(AbilityCategory)))
            {
                string mod = CategoryMark.ModifierClass(cat);
                Assert.IsTrue(mods.Add(mod), $"{cat} shares a mark");
                StringAssert.Contains("." + mod + " {", uss, $"{cat} has no shape rule");
            }
            StringAssert.Contains("Assets/UI/Styles/CategoryMark.uss", Read("Assets/UI/MainMenu.uxml"));
            StringAssert.Contains("Assets/UI/Styles/CategoryMark.uss", Read("Assets/UI/TouchControls.uxml"));
            StringAssert.Contains("CategoryMark.Apply(view.Mark", Read("Assets/_Game/Scripts/UI/MenuUiController.cs"));
            StringAssert.Contains("CategoryMark.Apply(refs.Mark", Read("Assets/_Game/Scripts/Input/TouchControlsController.cs"));
        }
    }
}
