using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Installers;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// B1 (spec 2026-11-06 §12 D3a): the feedback colours live in <c>FeedbackPalette.asset</c>. Pins that every
    /// active ability wears a palette accent, that Stun and Slow wear the colour of the state they inflict, that the
    /// palette's colours stay apart from each other and from the four player colours, and that the installer binds
    /// the shipped asset.
    /// </summary>
    public sealed class FeedbackPaletteTests
    {
        private const string PalettePath = TestAssets.DataRoot + "/FeedbackPalette.asset";
        private const float MinDeltaE = 12f;

        private static readonly Color[] Players = { PlayerPalette.P1, PlayerPalette.P2, PlayerPalette.P3, PlayerPalette.P4 };

        private static readonly AccentKey[] Keys =
            System.Enum.GetValues(typeof(AccentKey)).Cast<AccentKey>().Where(k => k != AccentKey.Custom).ToArray();

        /// <summary>
        /// A3 (batch 4): the enum moved from <c>CluckWars.Visuals</c> to <c>CluckWars.Abilities</c>. Every ability
        /// <c>.asset</c> stores it as an int, so a reorder or an inserted member silently recolours shipped abilities.
        /// </summary>
        [Test]
        public void AccentKey_KeepsItsSerializedNumbers_AndLivesWithTheAbilities()
        {
            var expected = new (AccentKey key, int value)[]
            {
                (AccentKey.Custom, 0), (AccentKey.Steal, 1), (AccentKey.Stun, 2), (AccentKey.Shove, 3), (AccentKey.Slow, 4),
                (AccentKey.Speed, 5), (AccentKey.Jump, 6), (AccentKey.EggShell, 7), (AccentKey.Hide, 8), (AccentKey.Peck, 9),
                (AccentKey.Hunt, 10),
            };
            foreach (var (key, value) in expected) Assert.AreEqual(value, (int)key, key.ToString());
            Assert.AreEqual(expected.Length, System.Enum.GetValues(typeof(AccentKey)).Length, "a new key is appended here with its number");
            Assert.AreEqual("CluckWars.Abilities.AccentKey", typeof(AccentKey).FullName);
            Assert.AreEqual(typeof(int), System.Enum.GetUnderlyingType(typeof(AccentKey)));

            // The shipped assets still say what they said: the active Shove ability wears key 3 on disk.
            var shove = Active().First(a => a.AccentKey == AccentKey.Shove);
            string yaml = System.IO.File.ReadAllText(UnityEditor.AssetDatabase.GetAssetPath(shove));
            StringAssert.Contains("AccentKey: 3", yaml, shove.name);
        }

        [Test]
        public void AccentColor_IsResolvedByPresentation_WithTheSameRuleTheAbilityUsed()
        {
            var ability = ScriptableObject.CreateInstance<CluckShockAbilitySO>();
            try
            {
                ability.CustomAccentColor = new Color(0.1f, 0.2f, 0.3f, 1f);
                ability.AccentKey = AccentKey.Custom;
                Assert.AreEqual(ability.CustomAccentColor, ability.AccentColor(), "Custom = the ability's own literal");
                foreach (var key in Keys)
                {
                    ability.AccentKey = key;
                    Assert.AreEqual(FeedbackPalette.Current.Accent(key), ability.AccentColor(), key.ToString());
                }
            }
            finally { Object.DestroyImmediate(ability); }

            string so = System.IO.File.ReadAllText("Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs");
            StringAssert.DoesNotContain("FeedbackPalette.", so, "an ability names its accent; it does not read the palette");
            StringAssert.Contains("[FormerlySerializedAs(\"AccentColor\")]", so, "assets saved before the palette keep their literal");
        }

        private static FeedbackPaletteSO Shipped()
        {
            var p = AssetDatabase.LoadAssetAtPath<FeedbackPaletteSO>(PalettePath);
            Assert.IsNotNull(p, PalettePath + " is missing");
            return p;
        }

        private static List<AbilityBaseSO> Active()
        {
            var reg = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var active = reg.ActiveAbilities.ToList();
            Assert.Greater(active.Count, 0, "registry has no active abilities");
            return active;
        }

        [Test]
        public void EveryActiveAbility_WearsAPaletteAccent_AndNoTwoShareOne()
        {
            var active = Active();
            foreach (var a in active)
                Assert.AreNotEqual(AccentKey.Custom, a.AccentKey, $"{a.name} is active but its accent is a Custom literal");
            var dupes = active.GroupBy(a => a.AccentKey).Where(g => g.Count() > 1).Select(g => g.Key + ": " + string.Join(", ", g.Select(a => a.name)));
            CollectionAssert.IsEmpty(dupes, "two active abilities share one accent");
        }

        [Test]
        public void RetiredAbilities_KeepTheirCustomLiteral()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AbilityBaseSO", new[] { TestAssets.DataRoot + "/Abilities" }))
            {
                var a = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null || !a.Retired) continue;
                Assert.AreEqual(AccentKey.Custom, a.AccentKey, a.name);
                Assert.AreEqual(a.CustomAccentColor, a.AccentColor(), a.name);
            }
        }

        [Test]
        public void ShippedPalette_StunSlowAndEggShellAccents_AreTheirIdentityColours()
        {
            var p = Shipped();
            Assert.AreEqual(p.StunColor, p.Accent(AccentKey.Stun), "Stun wears the stun-state yellow");
            Assert.AreEqual(p.SlowColor, p.Accent(AccentKey.Slow), "Slow wears the slow-state cyan");
            Assert.AreEqual(p.CreamColor, p.Accent(AccentKey.EggShell), "Egg Shell wears the egg's cream (spec 4.7 / 6)");
        }

        [Test]
        public void ShippedPalette_StateColours_AreThePrePaletteValues()
        {
            // B1 moves the state colours into the asset; it does not change them (D3a retunes accents only).
            var p = Shipped();
            var d = FeedbackPaletteSO.BuiltInDefaults();
            Assert.AreEqual(d.StunColor, p.StunColor);
            Assert.AreEqual(d.SlowColor, p.SlowColor);
            Assert.AreEqual(d.KnockColor, p.KnockColor);
            Assert.AreEqual(d.NeutralNoEffectColor, p.NeutralNoEffectColor);
            Assert.AreEqual(d.InkColor, p.InkColor);
            Assert.AreEqual(d.CreamColor, p.CreamColor);
        }

        [Test]
        public void BuiltInDefaults_AreEachActiveAbilitysPrePaletteLiteral()
        {
            // With no palette bound nothing changes: each keyed ability's default accent is the literal it carried before.
            var d = FeedbackPaletteSO.BuiltInDefaults();
            foreach (var a in Active())
                Assert.AreEqual(a.CustomAccentColor, d.Accent(a.AccentKey), a.name);
        }

        /// <summary>
        /// Every accent stays dE00 &gt;= 12 from every other accent and from every state colour. An accent that IS a state
        /// colour by design (Stun = stun yellow, Slow = slow cyan, Egg Shell = cream) counts as that one colour.
        /// State-vs-state pairs are out of scope: they predate the palette and are not a D3 accent collision
        /// (Knock white vs Cream is 9.20).
        /// </summary>
        [Test]
        public void ShippedPalette_AccentsAreAtLeast12FromEveryOtherAccentAndStateColour()
        {
            var p = Shipped();
            var states = new List<(string name, Color c)>
            {
                ("Stun state", p.StunColor), ("Slow state", p.SlowColor), ("Knock", p.KnockColor),
                ("NeutralNoEffect", p.NeutralNoEffectColor), ("Ink", p.InkColor), ("Cream", p.CreamColor),
            };
            var accents = Keys.Select(k => (name: k + " accent", c: p.Accent(k))).ToList();

            var failures = new List<string>();
            for (int i = 0; i < accents.Count; i++)
            {
                for (int j = i + 1; j < accents.Count; j++)
                    Check(accents[i], accents[j], failures);
                // An identity accent IS its state colour, so its distance to the other states is a state-vs-state pair.
                if (states.Any(st => Opaque(st.c) == Opaque(accents[i].c))) continue;
                foreach (var st in states) Check(accents[i], st, failures);
            }
            CollectionAssert.IsEmpty(failures, "palette colours closer than dE00 " + MinDeltaE + ":\n" + string.Join("\n", failures));
        }

        private static void Check((string name, Color c) a, (string name, Color c) b, List<string> failures)
        {
            float de = AbilityPaletteTests.DeltaE2000(Opaque(a.c), Opaque(b.c));
            if (de < MinDeltaE) failures.Add($"{a.name} vs {b.name}: dE00 {de:0.00}");
        }

        [Test]
        public void ShippedPalette_AccentsAreAtLeast12FromEveryPlayerColour()
        {
            var p = Shipped();
            var failures = new List<string>();
            foreach (var k in Keys)
            for (int i = 0; i < Players.Length; i++)
            {
                float de = AbilityPaletteTests.DeltaE2000(p.Accent(k), Players[i]);
                if (de < MinDeltaE) failures.Add($"{k} vs P{i + 1}: dE00 {de:0.00}");
            }
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void BoundPalette_DrivesFeedbackTuningAndAbilityAccents()
        {
            var previous = FeedbackPalette.Current;
            var probe = ScriptableObject.CreateInstance<FeedbackPaletteSO>();
            try
            {
                probe.StunColor = new Color(0.1f, 0.2f, 0.3f, 1f);
                probe.HuntAccent = new Color(0.4f, 0.5f, 0.6f, 1f);
                FeedbackPalette.Current = probe;
                Assert.AreEqual(probe.StunColor, FeedbackTuning.CanonicalStunColor);
                var hunter = Active().First(a => a.AccentKey == AccentKey.Hunt);
                Assert.AreEqual(probe.HuntAccent, hunter.AccentColor(), hunter.name);
            }
            finally
            {
                FeedbackPalette.Current = previous;
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void ProjectContextPrefab_BindsTheShippedPalette()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ProjectContextPrefabPath);
            var installer = prefab.GetComponentInChildren<ProjectInstaller>(true);
            Assert.IsNotNull(installer, "ProjectContext.prefab has no ProjectInstaller.");
            var slot = new SerializedObject(installer).FindProperty("_feedbackPalette");
            Assert.IsNotNull(slot, "ProjectInstaller lost its _feedbackPalette slot.");
            Assert.AreSame(Shipped(), slot.objectReferenceValue,
                "ProjectContext.prefab must reference FeedbackPalette.asset: without it every accent falls back to the built-in defaults.");
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);
    }
}
