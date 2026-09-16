using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CluckWars.Progression;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Everything slice 3 draws has to be drawable. LilitaOne carries roughly 225 characters, so a
    /// check mark, star, padlock or arrow renders as a blank box on a device and as nothing at all in
    /// a screenshot — which is exactly the kind of bug that passes every numeric check
    /// (memory/art-metrics-that-passed-while-broken.md).
    /// </summary>
    /// <remarks>
    /// The scan covers every string slice 3 can put on screen: the profile page's markup, the new
    /// elements on the main menu and character select, the shipped records' names, descriptions and
    /// titles, every word the name generator can draw, the banner names, and
    /// <c>ProfileController</c>'s own literals.
    /// </remarks>
    public sealed class ProfileGlyphTests
    {
        private const string UiRoot = "Assets/UI";
        private const string ProfileUxml = UiRoot + "/Profile.uxml";
        private const string MainMenuUxml = UiRoot + "/MainMenu.uxml";
        private const string CharacterSelectUxml = UiRoot + "/CharacterSelect.uxml";
        private const string ProfileUss = UiRoot + "/Styles/Profile.uss";
        private const string ProfileControllerPath = "Assets/_Game/Scripts/UI/ProfileController.cs";

        /// <summary>Elements whose text is new in slice 3, by their <c>name</c> attribute.</summary>
        private static readonly string[] NewMainMenuElements = { "IdentityStrip", "ProfileBtn" };

        private static readonly string[] NewCharacterSelectElements =
        {
            "ClassWarriorMastery", "ClassSpeedyMastery", "ClassFattyMastery", "ClassAssassinMastery",
        };

        // ---- Sources of on-screen text ----------------------------------------------

        private static XDocument Load(string path)
        {
            Assert.IsTrue(File.Exists(path), $"{path} not found on disk.");
            return XDocument.Load(path);
        }

        /// <summary>Every <c>text</c> attribute under the named elements (or the whole file when none are named).</summary>
        private static List<string> TextAttributes(string path, params string[] elementNames)
        {
            var root = Load(path).Root;
            IEnumerable<XElement> scope = root.DescendantsAndSelf();

            if (elementNames.Length > 0)
            {
                var named = new List<XElement>();
                foreach (string name in elementNames)
                {
                    var element = root.DescendantsAndSelf().FirstOrDefault(e => (string)e.Attribute("name") == name);
                    Assert.IsNotNull(element, $"{path} has no element named '{name}'; this scan has gone blind.");
                    named.AddRange(element.DescendantsAndSelf());
                }

                scope = named;
            }

            return scope.Select(e => (string)e.Attribute("text"))
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();
        }

        private static List<string> SourceLiterals(string path)
        {
            Assert.IsTrue(File.Exists(path), $"{path} not found on disk.");
            var literals = new List<string>();
            foreach (var (_, code) in SourceScan.CodeLines(path))
            {
                foreach (Match match in Regex.Matches(code, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    // Unescape only what a display string can hold; a literal newline or tab is not a glyph.
                    string text = match.Groups[1].Value
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\")
                        .Replace("\\n", string.Empty)
                        .Replace("\\t", string.Empty);
                    if (text.Length > 0) literals.Add(text);
                }
            }

            Assert.IsNotEmpty(literals, $"{path} yielded no string literals; the scan has gone blind.");
            return literals;
        }

        /// <summary>Everything slice 3 can put in front of a player, each labelled by where it came from.</summary>
        private static List<(string Where, string Text)> EveryNewString()
        {
            var strings = new List<(string, string)>();

            foreach (string text in TextAttributes(ProfileUxml)) strings.Add((ProfileUxml, text));
            foreach (string text in TextAttributes(MainMenuUxml, NewMainMenuElements)) strings.Add((MainMenuUxml, text));
            foreach (string text in TextAttributes(CharacterSelectUxml, NewCharacterSelectElements)) strings.Add((CharacterSelectUxml, text));
            foreach (string text in SourceLiterals(ProfileControllerPath)) strings.Add((ProfileControllerPath, text));

            var config = TestAssets.Load<ProgressionConfigSO>(TestAssets.ProgressionConfigPath);
            foreach (var record in config.Records)
            {
                strings.Add(("record " + record.Key, record.DisplayName));
                strings.Add(("record " + record.Key, record.Description));
                strings.Add(("record " + record.Key, record.Title));
            }

            foreach (var banner in config.Banners) strings.Add(("banner " + banner.Key, banner.DisplayName));
            foreach (string word in NameGenerator.Adjectives) strings.Add(("generator adjective", word));
            foreach (string word in NameGenerator.Nouns) strings.Add(("generator noun", word));

            Assert.Greater(strings.Count, 100, "Far too few strings collected; a source stopped being scanned.");
            return strings.Where(s => !string.IsNullOrEmpty(s.Item2)).ToList();
        }

        // ---- The checks --------------------------------------------------------------

        /// <summary>
        /// <c>Font.HasCharacter</c> cannot be the oracle here, and this test is what says so out loud.
        /// </summary>
        /// <remarks>
        /// LilitaOne is imported as a <b>dynamic</b> font, and a dynamic font answers <c>true</c> for
        /// every character — it means "I will ask the OS", not "I have this glyph". It returns true for
        /// a check mark, a star, an arrow and even a lone surrogate. Trusting it would be exactly the
        /// metric that passes while the screen is broken
        /// (memory/art-metrics-that-passed-while-broken.md), so the real rule is the character set
        /// below, and the assertion above it fails the day Unity makes <c>HasCharacter</c> mean
        /// something, so someone revisits this instead of the check silently staying weaker than it
        /// looks.
        /// </remarks>
        [Test]
        public void EveryStringSlice3CanDraw_IsInTheCharacterSetTheBrandFontActuallyHas()
        {
            var font = UiGfx.ChunkyFont();
            Assert.IsNotNull(font, "The brand font did not load, so nothing here could be verified.");

            Assert.IsTrue(font.dynamic && font.HasCharacter(''),
                "Font.HasCharacter has stopped answering true for a private-use character it cannot possibly have. " +
                "It may now be a real coverage test — revisit this check and use it if so.");

            var offenders = new List<string>();
            foreach (var (where, text) in EveryNewString())
            {
                foreach (char c in text)
                {
                    if (c == ' ' || c == '\n' || c == '\r' || c == '\t') continue;
                    if (c >= 0x20 && c <= 0x7E) continue;

                    offenders.Add($"{where}: U+{((int)c).ToString("X4")} '{c}' in \"{text}\"");
                }
            }

            Assert.IsEmpty(offenders,
                "LilitaOne carries about 225 glyphs, and the only range it is verified to cover is printable " +
                "ASCII. Anything else falls through to whatever the OS happens to have — which on Android is " +
                "often a blank box, and in a screenshot is nothing at all. Rings, pips, locks and dividers are " +
                "USS shapes for exactly this reason. Offenders:\n" + string.Join("\n", offenders.Distinct()));
        }

        [Test]
        public void EveryNewUxmlString_IsPlainAscii()
        {
            var offenders = new List<string>();
            var scanned = TextAttributes(ProfileUxml)
                .Concat(TextAttributes(MainMenuUxml, NewMainMenuElements))
                .Concat(TextAttributes(CharacterSelectUxml, NewCharacterSelectElements))
                .ToList();

            Assert.IsNotEmpty(scanned);
            foreach (string text in scanned)
            {
                foreach (char c in text)
                {
                    if (c > 0x7E) offenders.Add($"U+{((int)c).ToString("X4")} '{c}' in \"{text}\"");
                }
            }

            Assert.IsEmpty(offenders,
                "Slice 3's markup stays ASCII. The main menu's SOLO button predates this rule and keeps its " +
                "arrow; new text must not copy that. Offenders:\n" + string.Join("\n", offenders.Distinct()));
        }

        [Test]
        public void ThereIsNoFreeTextEntry_OnTheProfilePage()
        {
            // Names are generated and re-rolled. A TextField here would be the one string that later
            // has to be shown to other players, in a build with no moderation behind it.
            var fields = Load(ProfileUxml).Root.DescendantsAndSelf()
                .Where(e => e.Name.LocalName.Contains("TextField") || e.Name.LocalName == "TextInput")
                .Select(e => e.Name.LocalName)
                .ToList();

            Assert.IsEmpty(fields, "Profile.uxml must contain no text entry: " + string.Join(", ", fields));
        }

        [Test]
        public void TheRingsPipsAndLocks_AreDrawnByUss_NotByCharacters()
        {
            Assert.IsTrue(File.Exists(ProfileUss), $"{ProfileUss} not found on disk.");
            string uss = File.ReadAllText(ProfileUss);

            foreach (string selector in new[]
                     {
                         ".cw-emblem", ".cw-emblem__level", ".cw-pick--locked", ".cw-form-pip",
                         ".cw-record--earned", ".cw-plate",
                     })
            {
                StringAssert.Contains(selector + " ", uss + " ",
                    $"{selector} is what draws one of slice 3's shapes. If it is gone, something is drawing it with " +
                    "a character instead.");
            }

            // A ring is a border with a radius; a pip is the same. If those properties vanished, the
            // shapes would silently become flat rectangles.
            StringAssert.Contains("border-top-left-radius", uss);
            StringAssert.Contains("border-left-width", uss);
        }

        [Test]
        public void EveryBannerInTheCatalogue_HasTheUssClassThatPaintsIt()
        {
            string uss = File.ReadAllText(ProfileUss);
            foreach (var banner in TestAssets.Load<ProgressionConfigSO>(TestAssets.ProgressionConfigPath).Banners)
            {
                Assert.IsNotEmpty(banner.UssClass, $"Banner '{banner.Key}' names no USS class, so it would paint nothing.");
                StringAssert.Contains("." + banner.UssClass, uss,
                    $"Banner '{banner.Key}' points at .{banner.UssClass}, which {ProfileUss} does not define.");
            }
        }

        [Test]
        public void TheProfilePage_CarriesTheElementsItsControllerBinds()
        {
            var names = new HashSet<string>(Load(ProfileUxml).Root.DescendantsAndSelf()
                .Select(e => (string)e.Attribute("name"))
                .Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);

            foreach (string required in new[]
                     {
                         "ProfilePage", "ProfileContent", "ProfileNotReady", "ProfileBackBtn",
                         "PlatePreview", "PlateEmblem", "PlateEmblemLevel", "PlateName", "PlateTitle",
                         "RerollNameBtn", "TitleChoices", "EmblemChoices", "BannerChoices",
                         "RecordFilters", "RecordList", "RecordsEmpty",
                         "CareerGrid", "BestGrid", "RecentStrip", "RecentEmpty", "RoleList",
                     })
            {
                CollectionAssert.Contains(names, required,
                    $"ProfileController binds #{required}; without it that part of the page is silently blank.");
            }
        }

        [Test]
        public void TheMainMenuAndClassChips_CarryTheirIdentityElements()
        {
            var menu = new HashSet<string>(Load(MainMenuUxml).Root.DescendantsAndSelf()
                .Select(e => (string)e.Attribute("name")).Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);

            foreach (string required in new[]
                     {
                         "IdentityStrip", "IdentityEmblem", "IdentityEmblemLevel", "IdentityName",
                         "IdentityTitle", "IdentityGrain", "ProfileBtn",
                     })
            {
                CollectionAssert.Contains(menu, required, $"MenuUiController binds #{required} on the main menu.");
            }

            var chips = new HashSet<string>(Load(CharacterSelectUxml).Root.DescendantsAndSelf()
                .Select(e => (string)e.Attribute("name")).Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);

            foreach (string cls in new[] { "Warrior", "Speedy", "Fatty", "Assassin" })
            {
                CollectionAssert.Contains(chips, "Class" + cls + "Mastery");
                CollectionAssert.Contains(chips, "Class" + cls + "MasteryLevel");
            }
        }
    }
}
