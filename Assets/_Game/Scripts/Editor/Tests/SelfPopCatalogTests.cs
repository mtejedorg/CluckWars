using System.IO;
using System.Linq;
using CluckWars.Localization;
using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// B4 pop catalog (spec 2026-11-06 section 12 D7b / D8b): the new pop words exist in UiText.csv,
    /// the self-pops are drawn only on the owner's peer, and every self-pop call site goes through
    /// <see cref="FloatingCombatText.SpawnSelfPop"/> while PUSH (a victim pop) stays on the all-peers path.
    /// </summary>
    public class SelfPopCatalogTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";

        private static string Read(string relative) => File.ReadAllText(Scripts + relative);

        [Test]
        public void CatalogKeys_ResolveToTheirWords()
        {
            Assert.AreEqual("WADDLE", UiText.Get(UiKeys.HudPopWaddle));
            Assert.AreEqual("EGG!",   UiText.Get(UiKeys.HudPopEgg));
            Assert.AreEqual("HATCH!", UiText.Get(UiKeys.HudPopHatch));
            Assert.AreEqual("POOF!",  UiText.Get(UiKeys.HudPopPoof));
            Assert.AreEqual("FAST!",  UiText.Get(UiKeys.HudPopFast));
            Assert.AreEqual("HOP!",   UiText.Get(UiKeys.HudPopHop));
            Assert.AreEqual("PUSH",   UiText.Get(UiKeys.HudPopPush));
            Assert.AreEqual("HUNT!",  UiText.Get(UiKeys.HudPopHunt));
            Assert.AreEqual("ROB -5", UiText.Format(UiKeys.HudPopRobbed, ("n", 5)));
            Assert.AreEqual("+5",     UiText.Format(UiKeys.HudPopGrabbed, ("n", 5)));
        }

        [Test]
        public void CatalogKeys_AreRowsInTheCsv()
        {
            var keys = new[]
            {
                UiKeys.HudPopWaddle, UiKeys.HudPopEgg, UiKeys.HudPopHatch, UiKeys.HudPopPoof, UiKeys.HudPopFast,
                UiKeys.HudPopHop, UiKeys.HudPopRobbed, UiKeys.HudPopGrabbed, UiKeys.HudPopPush,
            };
            var csv = File.ReadAllLines("Assets/_Game/Resources/Text/UiText.csv");
            foreach (var key in keys)
                Assert.AreEqual(1, csv.Count(l => l.StartsWith(key + ",")), key + " must be exactly one CSV row");
        }

        [Test]
        public void RefusalAndControlRows_AreUnchanged()
        {
            // D8b: IMMUNE / SAFE / STUCK stay as shipped (STUCK stays, bang dropped 10 Nov).
            Assert.AreEqual("IMMUNE", UiText.Get(UiKeys.HudPopImmune));
            Assert.AreEqual("SAFE",   UiText.Get(UiKeys.HudPopSafe));
            Assert.AreEqual("STUCK", UiText.Get(UiKeys.HudPopStuck));
            Assert.AreEqual("STUN",   UiText.Get(UiKeys.HudPopStun));
        }

        [Test]
        public void SelfPopRule_OnlyTheOwnersNonDecoyBird()
        {
            Assert.IsTrue(FloatingCombatText.ShouldSpawnSelfPop(hasInputAuthority: true,  isDecoy: false));
            Assert.IsFalse(FloatingCombatText.ShouldSpawnSelfPop(hasInputAuthority: false, isDecoy: false), "rival peer");
            Assert.IsFalse(FloatingCombatText.ShouldSpawnSelfPop(hasInputAuthority: true,  isDecoy: true),  "own decoy");
            Assert.IsFalse(FloatingCombatText.ShouldSpawnSelfPop(hasInputAuthority: false, isDecoy: true));
            Assert.IsFalse(FloatingCombatText.IsSelfPopTarget(null));
            Assert.IsFalse(FloatingCombatText.SpawnSelfPop(null, UiKeys.HudPopFast, UnityEngine.Color.white));
        }

        [Test]
        public void PopSize_CatalogWordsLargerThanBareCargoNumbers()
        {
            Assert.AreEqual(0.058f, FloatingCombatText.WordCharacterSize, 1e-6f, "catalog word pops");
            Assert.AreEqual(0.032f, FloatingCombatText.PopupCharacterSize, 1e-6f, "bare +n / -n cargo numbers");
            Assert.Greater(FloatingCombatText.WordCharacterSize, FloatingCombatText.PopupCharacterSize);

            // Bare cargo numbers are the only pop drawn at the number size; every other spawn goes through the word path.
            string fct = Read("Visuals/FloatingCombatText.cs");
            StringAssert.Contains("null, PopupCharacterSize);", fct, "SpawnCargoDelta draws at the number size");
        }

        [Test]
        public void ShellPops_WaddleThenEggThenHatch()
        {
            bool was = false, inEgg = false;
            Assert.AreEqual(UiKeys.HudPopWaddle, ControlStateVFX.ShellPopKey(true, false, ref was, ref inEgg));
            Assert.IsNull(ControlStateVFX.ShellPopKey(true, false, ref was, ref inEgg), "walk continuing");
            Assert.AreEqual(UiKeys.HudPopEgg, ControlStateVFX.ShellPopKey(true, true, ref was, ref inEgg));
            Assert.IsNull(ControlStateVFX.ShellPopKey(true, true, ref was, ref inEgg), "egg continuing");
            Assert.AreEqual(UiKeys.HudPopHatch, ControlStateVFX.ShellPopKey(false, false, ref was, ref inEgg));
            Assert.IsNull(ControlStateVFX.ShellPopKey(false, false, ref was, ref inEgg));
        }

        [Test]
        public void ShellPops_HatchOnceOnEitherEdge_NoHatchWithoutEgg()
        {
            // Immunity closes first (second press / another cast) while the slot still runs: one HATCH!.
            bool was = false, inEgg = false;
            ControlStateVFX.ShellPopKey(true, false, ref was, ref inEgg);
            ControlStateVFX.ShellPopKey(true, true, ref was, ref inEgg);
            Assert.AreEqual(UiKeys.HudPopHatch, ControlStateVFX.ShellPopKey(true, false, ref was, ref inEgg));
            Assert.IsNull(ControlStateVFX.ShellPopKey(false, false, ref was, ref inEgg), "no second HATCH!");

            // A walk that ends before the egg (stunned out of it) gets no HATCH!.
            was = false; inEgg = false;
            ControlStateVFX.ShellPopKey(true, false, ref was, ref inEgg);
            Assert.IsNull(ControlStateVFX.ShellPopKey(false, false, ref was, ref inEgg));
        }

        [Test]
        public void SelfPops_GoThroughSpawnSelfPop_PushDoesNot()
        {
            string[] selfKeys =
            {
                "HudPopWaddle", "HudPopEgg", "HudPopHatch", "HudPopPoof", "HudPopFast", "HudPopHop",
                "HudPopRobbed", "HudPopGrabbed",
            };
            var users = Directory.GetFiles(Scripts, "*.cs", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/'))
                .Where(f => !f.Contains("/Editor/Tests/") && !f.EndsWith("Localization/UiKeys.cs"))
                .Where(f => selfKeys.Any(k => File.ReadAllText(f).Contains("UiKeys." + k)))
                .Select(Path.GetFileName).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "ControlStateVFX.cs", "FloatingCombatText.cs" }, users,
                "self-pop words may only be used by the self-pop path");

            string vfx = Read("Visuals/ControlStateVFX.cs");
            StringAssert.DoesNotContain("FloatingCombatText.Spawn(", vfx, "ControlStateVFX must not use the all-peers Spawn");
            StringAssert.Contains("FloatingCombatText.SpawnSelfPop(", vfx);
            foreach (var call in new[] { "SelfPop(UiKeys.HudPopFast", "SelfPop(UiKeys.HudPopPoof", "SelfPop(UiKeys.HudPopHop", "SelfPop(shellKey" })
                StringAssert.Contains(call, vfx);

            string fct = Read("Visuals/FloatingCombatText.cs");
            int start = fct.IndexOf("public static void SpawnCargoPop", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            string cargoPop = fct.Substring(start, fct.IndexOf("// ── Lifecycle", start, System.StringComparison.Ordinal) - start);
            StringAssert.Contains("SpawnSelfPop(", cargoPop);
            Assert.Less(cargoPop.IndexOf("SpawnSelfPop(", System.StringComparison.Ordinal),
                        cargoPop.IndexOf("SpawnCargoDelta(", System.StringComparison.Ordinal),
                        "the owner's word must be tried before the remote cargo popup");

            string cargo = Read("Visuals/HitFeedback.cs");   // the cargo change listener (ChickenCargo raises, it draws)
            StringAssert.Contains("FloatingCombatText.SpawnCargoPop(", cargo);
            StringAssert.DoesNotContain("SpawnCargoDelta(", cargo, "one cargo pop per event: no direct delta popup");

            var pushLines = Read("Visuals/HitFeedback.cs").Split('\n').Where(l => l.Contains("UiKeys.HudPopPush")).ToArray();
            Assert.AreEqual(1, pushLines.Length);
            StringAssert.Contains("FloatingCombatText.Spawn(", pushLines[0], "PUSH is the victim's pop on every peer");
            StringAssert.DoesNotContain("SelfPop", pushLines[0]);
        }

        // ---- E2 (spec section 12, 2026-11-08): pops are words only, the badge row owns the number --------

        /// <summary>Every <c>hud.pop.*</c> word that takes no argument (the self-pop catalog plus STUN / STUCK / SLOW and
        /// the refusal words) carries no digit and no percent. The argument rows (ROB -n, +n) are the cargo numbers.</summary>
        [Test]
        public void E2_EveryWordPop_CarriesNoDigitOrPercent()
        {
            var keys = typeof(UiKeys).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.IsLiteral && f.Name.StartsWith("HudPop", System.StringComparison.Ordinal))
                .Where(f => !f.IsDefined(typeof(UiArgsAttribute), false))
                .Select(f => (string)f.GetRawConstantValue()).ToArray();
            CollectionAssert.IsSupersetOf(keys, new[] { UiKeys.HudPopStun, UiKeys.HudPopStuck, UiKeys.HudPopSlow,
                UiKeys.HudPopWaddle, UiKeys.HudPopEgg, UiKeys.HudPopHatch, UiKeys.HudPopPoof, UiKeys.HudPopFast, UiKeys.HudPopHop });
            foreach (var key in keys)
            {
                string word = UiText.Get(key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(word), key);
                Assert.IsFalse(word.Any(char.IsDigit), key + " = " + word + ": a pop is a word, the badge row owns the number");
                StringAssert.DoesNotContain("%", word, key);
            }
        }

        /// <summary>The control pops spawn their label untouched: no seconds suffix, no percent branch, on every peer.</summary>
        [Test]
        public void E2_ControlPops_SpawnTheBareWord()
        {
            string hit = Read("Visuals/HitFeedback.cs");
            foreach (var method in new[] { "private void SpawnControlText(", "private void SpawnSlowText(" })
            {
                int start = hit.IndexOf(method, System.StringComparison.Ordinal);
                Assert.Greater(start, 0, method);
                int end = hit.IndexOf("\n        }", start, System.StringComparison.Ordinal);
                string body = hit.Substring(start, end - start);
                StringAssert.Contains("FloatingCombatText.Spawn(", body, method);
                foreach (var banned in new[] { "%", "ToString(", "$\"", "HasStateAuthority", "SlowMultiplier", "Remaining" })
                    StringAssert.DoesNotContain(banned, body, method + " must emit the word only");
            }
            StringAssert.Contains("SpawnControlText(UiText.Get(UiKeys.HudPopStun), FeedbackTuning.CanonicalStunColor", hit);
            StringAssert.Contains("SpawnControlText(UiText.Get(UiKeys.HudPopStuck), FeedbackTuning.CanonicalRootColor", hit);
        }
    }
}
