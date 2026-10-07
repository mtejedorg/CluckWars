using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.UI;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards Phase 3B's rules that nothing else would notice breaking: every juice entry point is
    /// gated on Reduced Motion, the rest scales the pop multiplies match the stylesheet, the retired
    /// decorative dots are gone and the Fx sprites have an import rule.
    /// </summary>
    public sealed class MenuJuiceGuardTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), rel));

        /// <summary>Public instance members of MenuJuice that are housekeeping, not effects.</summary>
        private static readonly string[] NotEffects = { "LandNow", "CancelAll", "Dispose" };

        [Test]
        public void EveryMenuJuiceEntryPoint_ChecksTheReducedMotionGate()
        {
            string src = Read("Assets/_Game/Scripts/UI/MenuJuice.cs");
            var entries = typeof(MenuJuice).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !NotEffects.Contains(m.Name)).ToList();
            Assert.GreaterOrEqual(entries.Count, 6, "Pop, Stagger, Fly, Stamp, BurstAt, Burst");

            foreach (var m in entries)
            {
                var start = Regex.Match(src, @"public [\w<>\.]+ " + m.Name + @"\(");
                Assert.IsTrue(start.Success, m.Name + " not found in source");
                int next = src.IndexOf("\n        public ", start.Index + 10, System.StringComparison.Ordinal);
                string body = src.Substring(start.Index, (next < 0 ? src.Length : next) - start.Index);
                Assert.IsTrue(Regex.IsMatch(body, @"if \(!Allowed\)\s*return"),
                    $"MenuJuice.{m.Name} must start with `if (!Allowed) return ...;` (Reduced Motion).");
            }
        }

        [Test]
        public void CelebrationReadsTheSameGate_AndTheMenuHasNoOwnCountdown()
        {
            StringAssert.Contains("MenuJuice.Allowed", Read("Assets/_Game/Scripts/UI/MatchCelebration.cs"));
            StringAssert.DoesNotContain("Countdown", Read("Assets/_Game/Scripts/UI/MenuUiController.cs").Replace("CountdownTick", "").Replace("CountdownGo", ""),
                "the 3-2-1 is the in-game intro (MatchOverlaysController.RefreshIntro), not a menu step");
            Assert.IsTrue(MenuJuice.Allowed || CluckWars.Settings.PlayerPreferences.ReducedMotionEnabled,
                "Allowed is exactly !ReducedMotionEnabled");
        }

        [Test]
        public void UiCode_DrivesMotionOnlyThroughMenuJuice()
        {
            // No second animation path that could skip the gate.
            foreach (var f in new[] { "MenuUiController.cs", "MatchOverlaysController.cs" })
            {
                string src = Read("Assets/_Game/Scripts/UI/" + f);
                StringAssert.DoesNotContain("experimental.animation", src, f);
                StringAssert.DoesNotContain("style.scale", src, f);
                StringAssert.DoesNotContain("style.rotate", src, f);
            }
        }

        [Test]
        public void PopRestScales_MatchTheStylesheet()
        {
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            Assert.IsTrue(Regex.IsMatch(css, @"\.cw-class-tile--selected \{\s*scale: 1\.04 1\.04"), "tile");
            Assert.IsTrue(Regex.IsMatch(css, @"\.cw-perk-badge--selected \{\s*scale: 1\.03 1\.03"), "perk badge");
            Assert.AreEqual(1.04f, MenuJuicePolicy.TileRestScale);
            Assert.AreEqual(1.03f, MenuJuicePolicy.PerkRestScale);
            // Phase 4 (re-audit item 8): a picked / focused deck card rests exactly where its row puts
            // it - no scale, no translate - so the controller's pop (rest 1) settles with no jump.
            foreach (var rule in new[] { "cw-ability-card--picked", "cw-ability-card--focused" })
            {
                foreach (Match m in Regex.Matches(css, @"\." + rule + @"\s*\{([^}]*)\}"))
                {
                    StringAssert.DoesNotContain("scale", m.Groups[1].Value, rule);
                    StringAssert.DoesNotContain("translate", m.Groups[1].Value, rule);
                }
            }
            // Selected tile: no lift either (re-audit item 19), so the stagger entry ends at rest.
            var tile = Regex.Match(css, @"\.cw-class-tile--selected\s*\{([^}]*)\}");
            StringAssert.DoesNotContain("translate", tile.Groups[1].Value, "tile lift");
        }

        [Test]
        public void RetiredBackgroundDots_AreGone_AndFxClassesExistInBothSheets()
        {
            StringAssert.DoesNotContain("cw-bg-particle", Read("Assets/UI/MatchOverlays.uxml"));
            string overlays = Read("Assets/UI/Styles/MatchOverlays.uss");
            string theme = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            StringAssert.DoesNotContain("cw-bg-particle", overlays);
            foreach (var cls in new[] { ".cw-fx-sparkle", ".cw-fx-dust", ".cw-fx-feather-1", ".cw-fx-feather-2", ".cw-fx-feather-3", ".cw-fx-particle" })
            {
                StringAssert.Contains(cls, overlays);
                StringAssert.Contains(cls, theme);
            }
            StringAssert.Contains("name=\"MeCelebration\"", Read("Assets/UI/MatchOverlays.uxml"));
        }

        [Test]
        public void ReducedMotionRule_BeatsEverySingleClassTransition()
        {
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            StringAssert.Contains(".cw-reduced-motion.cw-reduced-motion * { transition-duration: 0s; }", css);
        }

        [Test]
        public void Celebration_IsTheFirstChildOfTheWinnerColumn()
        {
            string xml = Read("Assets/UI/MatchOverlays.uxml");
            int hero = xml.IndexOf("name=\"MeHero\"");
            int cel = xml.IndexOf("name=\"MeCelebration\"");
            int crown = xml.IndexOf("name=\"MeCrown\"");
            Assert.Greater(hero, 0);
            Assert.Less(hero, cel, "inside the winner column");
            Assert.Less(cel, crown, "first child: drawn under the crown, ribbon, chicken and text");
        }

        [Test]
        public void IntroGo_ComesFromTheDictionary()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.DoesNotContain("\"GO!\"", src);
            StringAssert.Contains("UiKeys.CountdownGo", src);
            StringAssert.Contains("_introJuice", src);
        }

        [Test]
        public void FxSprites_HaveAnImportRule_Capped()
        {
            string src = Read("Assets/_Game/Scripts/Editor/UiSpriteImportSettings.cs");
            StringAssert.Contains("FxPrefix = \"Fx/\"", src);
            Assert.LessOrEqual(CluckWars.EditorTools.UiSpriteImportSettings.FxMaxSize, 128);
        }

        [Test]
        public void ParticleBursts_StayWithinThePool()
        {
            Assert.LessOrEqual(MenuJuicePolicy.MaxBurstParticles, 6);
            Assert.LessOrEqual(2 * MenuJuicePolicy.MaxBurstParticles, MenuJuicePolicy.ParticlePoolSize + MenuJuicePolicy.MaxBurstParticles,
                "the READY stamp fires two bursts; the pool covers one full burst plus reuse");
        }
    }
}
