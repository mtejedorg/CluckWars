using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// GEAR UP's ramp locks (review 2026-10-24, B1). The deck is built once per class, usually before the journal
    /// loads, and RefreshDeck rewrites <c>cw-ability-card--locked</c> for starters on every refresh, so a lock set
    /// at build time under that class never showed. Source scans: MenuUiController is a MonoBehaviour on a UIDocument.
    /// </summary>
    public class RampDeckLockTests
    {
        private const string Menu = "Assets/_Game/Scripts/UI/MenuUiController.cs";
        private const string Theme = "Assets/UI/Styles/CluckWarsTheme.uss";

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private static string Body(string src, string signature)
        {
            int start = src.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, $"{signature} not found in {Menu}");
            int open = src.IndexOf('{', start), depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
            }
            Assert.Fail($"unbalanced braces after {signature}");
            return null;
        }

        [Test]
        public void AddDeckCard_DecidesNoLockAtBuildTime()
        {
            string body = Body(Read(Menu), "private void AddDeckCard(");
            StringAssert.DoesNotContain("IsAbilityLocked(", body, "a build-time lock is stale once the journal loads");
            StringAssert.DoesNotContain("IsRampLockedCard(", body, "a build-time lock is stale once the journal loads");
        }

        [Test]
        public void RefreshDeck_TogglesTheRampLock_UnderItsOwnClass()
        {
            string body = Body(Read(Menu), "private void RefreshDeck(");
            StringAssert.Contains("\"cw-ability-card--ramp-locked\"", body);
            StringAssert.Contains("IsRampLockedCard(", body);
        }

        [Test]
        public void UnlockChanges_RefreshTheDeck()
        {
            string src = Read(Menu);
            StringAssert.Contains("OnUnlocksChanged += OnUnlocksChanged", src);
            StringAssert.Contains("OnUnlocksChanged -= OnUnlocksChanged", src);
            StringAssert.Contains("RefreshDeck()", Body(src, "private void OnUnlocksChanged("));
        }

        [Test]
        public void RampDimming_DoesNotStyleTheStarterClass()
        {
            string uss = Read(Theme);
            Assert.IsFalse(Regex.IsMatch(uss, @"\.cw-ability-card--locked\s+\.cw-ability-card__"),
                "--locked means 'starter' (RefreshDeck); the ramp dims under --ramp-locked");
            StringAssert.Contains(".cw-ability-card--ramp-locked .cw-ability-card__icon", uss);
        }
    }
}
