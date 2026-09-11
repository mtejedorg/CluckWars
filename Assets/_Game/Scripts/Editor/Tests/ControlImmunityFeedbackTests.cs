using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Two feedback fixes that both come down to the same distinction: <b>what a system does
    /// is not the same fact as what it should say about it.</b>
    ///
    /// <list type="number">
    ///   <item><b>Immovable — eligible vs affected.</b> A control-immune chicken stays a
    ///   legitimate target (gathered, counted, telegraphed) but the effect is refused on
    ///   arrival. Splitting the two facts is what lets the telegraph grey the bracket and the
    ///   impact beat print IMMUNE <i>without</i> touching usability or whiff styling — see
    ///   <see cref="AbilityBaseSO.IsEffectNullified"/>.</item>
    ///   <item><b>Feint — presentation vs transport.</b> A self-applied sidestep is delivered
    ///   as the same impulse, through the same physics, and is simply reported on a different
    ///   replicated counter so the victim beat does not fire on it.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>Why so much of this is source analysis rather than behaviour.</b> Both mechanisms
    /// hinge on <c>ChickenController.IsControlImmune</c>, which reads a <c>[Networked]</c>
    /// <c>TickTimer</c> against a live <c>Runner</c>. EditMode probes are never
    /// <c>Spawned()</c>, so <c>Runner</c> is null and the property is hard-false — a
    /// behavioural test could only ever assert the un-immune case, which is exactly the case
    /// that was never broken. Pinning the wiring is what actually protects these fixes, and it
    /// is the same tool <c>DataIntegrityTests.ControlImmunity_IsEnforcedAtBothChokepoints_NotPerAbility</c>
    /// already uses on the enforcement half of the very same feature. Where a real assertion is
    /// available (declaration coverage across the whole shipped ability pool) it is used.
    /// </remarks>
    public sealed class ControlImmunityFeedbackTests
    {
        private const string AbilityDir      = "Assets/_Game/Scripts/Abilities";
        private const string BasePath        = "Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs";
        private const string FeintPath       = "Assets/_Game/Scripts/Abilities/FeintAbilitySO.cs";
        private const string ControllerPath  = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const string HitFeedbackPath = "Assets/_Game/Scripts/Visuals/HitFeedback.cs";
        private const string TelegraphPath   = "Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs";
        private const string ControlVfxPath  = "Assets/_Game/Scripts/Visuals/ControlStateVFX.cs";

        // ---- Helpers ---------------------------------------------------------

        private static List<Type> ConcreteAbilityTypes() =>
            typeof(AbilityBaseSO).Assembly
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(AbilityBaseSO)) && !t.IsAbstract)
                .OrderBy(t => t.Name)
                .ToList();

        private static string Read(string path)
        {
            Assert.IsTrue(File.Exists(path), $"Expected source file not found: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// Strips block and line comments. Every scan below looks for <i>calls</i>, and this
        /// file's own subject matter guarantees the prose is full of the same identifiers —
        /// the comment on Feint's fixed call site literally names <c>RPC_ApplyKnockback</c> to
        /// say it is no longer used. Scanning raw text would read those explanations as code
        /// and invert the answer.
        /// </summary>
        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var sb = new StringBuilder(src.Length);
            foreach (var line in src.Split('\n'))
            {
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(slash >= 0 ? line.Substring(0, slash) : line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>A window of source starting at <paramref name="signature"/>. Same blunt
        /// instrument DataIntegrityTests uses on this feature; the span is generous enough to
        /// cover the method and small enough not to spill into the next one.</summary>
        private static string BodyAt(string src, string signature, int span = 1400)
        {
            int i = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.Greater(i, 0, $"'{signature}' not found — the test's anchor moved, or the method was renamed.");
            return src.Substring(i, Math.Min(span, src.Length - i));
        }

        /// <summary>
        /// Comment-stripped source of an ability type <i>and every base class up to (not
        /// including) <see cref="AbilityBaseSO"/></i>. Ambush and Wing Slam declare no effects
        /// of their own — <c>StunBurstAbilitySO</c> owns the stun for both — so a scan that
        /// stopped at the leaf type would conclude they deliver nothing at all.
        /// </summary>
        private static string ChainSource(Type type, Dictionary<string, string> byTypeName)
        {
            var sb = new StringBuilder();
            for (var t = type; t != null && t != typeof(AbilityBaseSO); t = t.BaseType)
            {
                Assert.IsTrue(byTypeName.TryGetValue(t.Name, out var src),
                    $"No source file named {t.Name}.cs under {AbilityDir}. Ability sources are " +
                    "scanned by file name; rename the file to match the type.");
                sb.Append(src).Append('\n');
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> AbilitySourcesByTypeName()
        {
            var map = new Dictionary<string, string>();
            foreach (var path in Directory.GetFiles(AbilityDir, "*.cs", SearchOption.AllDirectories))
                map[Path.GetFileNameWithoutExtension(path)] = StripComments(File.ReadAllText(path));
            return map;
        }

        /// <summary>The four sinks control immunity actually refuses, named as the RPCs an
        /// ability calls on a victim. Both chokepoints they funnel through are pinned by
        /// <c>DataIntegrityTests.ControlImmunity_IsEnforcedAtBothChokepoints_NotPerAbility</c>.</summary>
        private static readonly string[] ControlDelivery =
        {
            "RPC_ApplyStun", "RPC_ApplyRoot", "RPC_ApplyAbilitySlow", "RPC_ApplyKnockback",
        };

        /// <summary>Anything a target receives that immunity does <i>not</i> stop. Cargo is
        /// Immovable's designed counterplay — a Fatty who presses it while full is protecting
        /// the wrong thing — so an ability that also robs is never "immune-proof".</summary>
        private static readonly string[] NonControlDelivery =
        {
            "RPC_DrainStolen", "AssassinExecute",
        };

        // ---- 1. The split itself: eligibility must not have moved ------------

        /// <summary>
        /// The load-bearing constraint of the whole fix. Folding immunity into
        /// <c>ExtraTargetFilter</c> would have been the one-line version, and it would have
        /// dropped immune targets out of <c>GatherTargets</c> — silently changing whether a
        /// <c>RequiresEnemyInRange</c> ability can fire at all when the only rival nearby is
        /// Immovable, and whether a cast that reached someone styles as a whiff via
        /// <c>ZeroHitsIsAWhiff</c>. Neither is a feedback change.
        /// </summary>
        [Test]
        public void Immunity_IsNotAnEligibilityFilter()
        {
            string src = StripComments(Read(BasePath));

            string wouldAffect = BodyAt(src, "public bool WouldAffect(", 600);
            StringAssert.DoesNotContain("IsControlImmune", wouldAffect,
                "WouldAffect is eligibility. Consulting immunity here removes immune targets from " +
                "GatherTargets, which changes ability usability and whiff styling — not feedback.");
            StringAssert.DoesNotContain("IsEffectNullified", wouldAffect,
                "Same reason: nullification is a statement about the effect, not about eligibility.");

            string gather = BodyAt(src, "public int GatherTargets(", 1600);
            StringAssert.DoesNotContain("IsEffectNullified", gather,
                "GatherTargets is resolution — an immune target must still be gathered and still " +
                "counted in LastCastHitCount, or the two consumers above change behaviour.");

            foreach (var kvp in AbilitySourcesByTypeName())
            {
                int i = kvp.Value.IndexOf("override bool ExtraTargetFilter", StringComparison.Ordinal);
                if (i < 0) continue;
                string body = kvp.Value.Substring(i, Math.Min(900, kvp.Value.Length - i));
                StringAssert.DoesNotContain("IsControlImmune", body,
                    $"{kvp.Key}.ExtraTargetFilter consults immunity. Declare " +
                    "TargetEffectIsPurelyControl instead — a filter makes the target ineligible, " +
                    "which is a gameplay change, not the grey bracket that was asked for.");
            }
        }

        /// <summary>
        /// The predicate's own contract: it may only ever be true for an ability that declares
        /// its target effect purely control, it must read the live immunity window, and it must
        /// never fire on the caster themselves (Feint self-targets, and a caster greying out
        /// their own ring would be nonsense).
        /// </summary>
        [Test]
        public void IsEffectNullified_ConsultsImmunity_AndExcludesTheCaster()
        {
            string body = BodyAt(StripComments(Read(BasePath)), "public bool IsEffectNullified(", 700);

            StringAssert.Contains("TargetEffectIsPurelyControl", body,
                "IsEffectNullified must gate on the per-ability declaration, or a Snatch against " +
                "an Immovable rival would print IMMUNE while the theft goes through.");
            StringAssert.Contains("IsControlImmune", body,
                "IsEffectNullified must read the live immunity window — that is the whole question.");
            StringAssert.Contains("candidate == caster", body,
                "The caster is never their own nullified target.");
        }

        /// <summary>Cheap contract lock on the arguments the predicate is handed by real
        /// callers. Not a proof of the immune case — see the class remarks for why EditMode
        /// cannot reach one — but it does pin that a null or self candidate can never be
        /// reported as nullified, which is what the telegraph and hit-confirm loops rely on.</summary>
        [Test]
        public void IsEffectNullified_IsFalseForNullAndForTheCasterThemselves()
        {
            var caster = NewProbe();
            try
            {
                foreach (var type in ConcreteAbilityTypes())
                {
                    var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                    try
                    {
                        Assert.IsFalse(probe.IsEffectNullified(caster, null), $"{type.Name}: null candidate");
                        Assert.IsFalse(probe.IsEffectNullified(caster, caster), $"{type.Name}: the caster themselves");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(probe); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(caster.gameObject); }
        }

        private static ChickenController NewProbe()
        {
            // Never Spawned(), so Runner is null and nothing networked is live. Same probe
            // shape AbilityAimTests uses; see its helper for why AddComponent alone is safe.
            var go = new GameObject("ControlImmunityFeedbackTests_Probe");
            return go.AddComponent<ChickenController>();
        }

        // ---- 2. Declaration coverage across the shipped pool -----------------

        /// <summary>
        /// The real regression risk. <c>TargetEffectIsPurelyControl</c> is declared, not
        /// inferred (Category answers a different question and is wrong in both directions —
        /// Mark/Kill is Control but applies an execute mark; Roll Push is pure knockback and
        /// declares no category at all), so the failure mode is a new ability quietly joining
        /// the wrong bucket by omission. This derives the honest answer from what each
        /// ability's <c>OnActivate</c> actually calls and holds the declaration to it.
        /// </summary>
        [Test]
        public void EveryAbility_DeclaresWhetherItsTargetEffectIsPurelyControl()
        {
            var sources = AbilitySourcesByTypeName();

            foreach (var type in ConcreteAbilityTypes())
            {
                var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                try
                {
                    string src = ChainSource(type, sources);

                    bool deliversControl    = ControlDelivery.Any(r => src.Contains(r));
                    bool deliversSomethingElse = NonControlDelivery.Any(r => src.Contains(r));

                    // A placed zone's slow and root reach the victim through
                    // CheckAbilityZoneSlow / CheckAuraSlow, which call ApplySlow directly
                    // rather than routing through ApplyPassiveControlDuration — so immunity
                    // does not refuse them, and claiming it would grey a bracket for an
                    // effect that lands. They also resolve long after the cast.
                    bool placesZone = probe.PlacesZone;

                    // A self-only ability delivers nothing to a rival at all (Feint's sidestep
                    // is an impulse on its own caster).
                    bool touchesRivals = probe.AffectsEnemies;

                    bool expected = deliversControl && !deliversSomethingElse && !placesZone && touchesRivals;

                    Assert.AreEqual(expected, probe.TargetEffectIsPurelyControl,
                        $"{type.Name}: TargetEffectIsPurelyControl is {probe.TargetEffectIsPurelyControl}, " +
                        $"but its source says control={deliversControl}, other-delivery={deliversSomethingElse}, " +
                        $"placesZone={placesZone}, affectsEnemies={touchesRivals}. Declaring true for an " +
                        "ability that also steals prints IMMUNE over a rival who was just robbed; " +
                        "declaring false for a purely-control one puts the accent 'will be hit' bracket " +
                        "on a chicken the cast is about to bounce off. See AbilityBaseSO.IsEffectNullified.");
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }
        }

        /// <summary>
        /// The dangerous direction, stated once more as an explicit list so the intent survives
        /// even if the derivation above is ever loosened. Immovable's counterplay <i>is</i>
        /// cargo — these abilities all take from a Fatty who is standing there being unmovable.
        /// </summary>
        [Test]
        public void AbilitiesThatStillTakeCargo_AreNeverClaimedImmuneProof()
        {
            foreach (var name in new[]
                     {
                         nameof(SnatchAbilitySO), nameof(SneakyStealAbilitySO),
                         nameof(ScrapAbilitySO), nameof(RollTrampleAbilitySO),
                         nameof(SpineCoatAbilitySO), nameof(MarkKillAbilitySO),
                     })
            {
                var type = ConcreteAbilityTypes().FirstOrDefault(t => t.Name == name);
                Assert.IsNotNull(type, $"{name} no longer exists — update this list deliberately.");

                var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                try
                {
                    Assert.IsFalse(probe.TargetEffectIsPurelyControl,
                        $"{name} does something to a target that immunity does not stop, so the " +
                        "grey IMMUNE label would be a lie about a cast that just worked.");
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }
        }

        // ---- 3. The two consumers ---------------------------------------------

        /// <summary>FEEDBACK.md §2.2 case 4: the grey dashed ⃠ bracket. A target the scan
        /// selected but whose effect will be refused must read as NoEffect, not Valid.</summary>
        [Test]
        public void Telegraph_GreysTheBracket_ForANullifiedTarget()
        {
            string body = BodyAt(StripComments(Read(TelegraphPath)), "private void UpdateMarks(", 2000);

            StringAssert.Contains("IsEffectNullified", body,
                "UpdateMarks classified purely on GatherTargets membership, so an Immovable rival " +
                "got the accent 'will be hit' bracket. §2.2 names immunity as the canonical " +
                "no-effect case.");
            StringAssert.Contains("WillBeHit", body,
                "Selection is still required — nullification narrows Valid, it does not replace it.");
        }

        /// <summary>
        /// FEEDBACK.md §3.2 case 15: the grey IMMUNE text, and — just as important — the
        /// absence of a hit-confirm spark and of an attacker attribution for a hit that landed
        /// on nothing. Attribution exists to draw the victim's direction lines; a refused
        /// impulse has no direction worth pointing at.
        /// </summary>
        [Test]
        public void HitFeedback_LabelsANullifiedVictim_InsteadOfSparkingThem()
        {
            string body = BodyAt(StripComments(Read(HitFeedbackPath)), "private void ConfirmHits(", 2000);

            int nullifiedAt = body.IndexOf("IsEffectNullified", StringComparison.Ordinal);
            Assert.Greater(nullifiedAt, 0,
                "ConfirmHits is the only place a control-immune victim can be caught: they are " +
                "counted, so hitCount > 0 and MarkImmuneTargets (zero-hit only) never sees them.");

            StringAssert.Contains("\"IMMUNE\"", body,
                "The nullified branch must spawn the §3.2 case 15 label — the same word and colour " +
                "§2.2's grey bracket just used, so 'nothing will happen' and 'nothing happened' match.");

            int sparkAt = body.IndexOf("PlayHitSpark", StringComparison.Ordinal);
            Assert.Greater(sparkAt, 0, "ConfirmHits should still spark the victims it really hit.");
            Assert.Less(nullifiedAt, sparkAt,
                "The nullified branch must short-circuit BEFORE the spark and the attribution — a " +
                "white 'you connected' spark on a chicken that shrugged the hit off is the same lie " +
                "in a different colour.");
        }

        // ---- 4. Feint: presentation split from transport ----------------------

        /// <summary>
        /// The sidestep is still delivered as an impulse, and that is load-bearing: a wall stops
        /// a shove exactly as it stops running, which is the counter-play Speedy's denied
        /// terrain-skipping depends on. What changed is only which counter reports it.
        /// </summary>
        [Test]
        public void Feint_ReportsItsSidestep_AsASelfImpulse()
        {
            string feint = StripComments(Read(FeintPath));

            StringAssert.Contains("RPC_ApplySelfImpulse", feint,
                "Feint must announce its sidestep on the self-impulse signal.");
            StringAssert.DoesNotContain("RPC_ApplyKnockback", feint,
                "On the knockback counter, Speedy's own dodge played the white flash, the recoil " +
                "and the victim-tier shake with no attacker — the exact signature of being hit " +
                "from off-screen.");
            StringAssert.DoesNotContain("RPC_TeleportTo", feint,
                "Do not 'fix' the feedback by teleporting: a blink is not stopped by a wall, and " +
                "that is precisely the advantage this class is denied.");
        }

        /// <summary>
        /// Byte-identical physics is the requirement, so there must be exactly one place an
        /// impulse is written. The origin may only reach the event-id bump.
        /// </summary>
        [Test]
        public void SelfImpulse_SharesTheKnockbackPhysicsPath_Exactly()
        {
            string src = StripComments(Read(ControllerPath));

            // Only the impulse write is counted: RPC_TeleportTo separately clears the field to
            // zero, which is a reset and not a second delivery path.
            int writes = Regex.Matches(src, @"ExternalDisplacement\s*=\s*impulse").Count;
            Assert.AreEqual(1, writes,
                $"An impulse is written to ExternalDisplacement in {writes} places. A dodge and a " +
                "shove must share exactly one, or they will drift apart — the origin is " +
                "presentation only and must never reach the physics.");

            string apply = BodyAt(src, "public void ApplyKnockback(", 1200);
            StringAssert.Contains("SelfImpulseEventId++", apply, "the self-applied branch");
            StringAssert.Contains("KnockbackEventId++", apply, "the hit branch");
            StringAssert.Contains("IsControlImmune", apply,
                "Immunity still refuses both — a self-impulse is not a loophole around Immovable.");

            string rpc = BodyAt(src, "public void RPC_ApplySelfImpulse(", 400);
            StringAssert.Contains("ImpulseOrigin.SelfApplied", rpc,
                "RPC_ApplySelfImpulse must route through the shared impulse path with the self origin.");
        }

        /// <summary>Hit is the default and the zero value, so an impulse site that predates this
        /// split — or forgets the argument — reports as a hit rather than silently telling a
        /// victim their injury was self-inflicted.</summary>
        [Test]
        public void ImpulseOrigin_DefaultsToHit()
        {
            Assert.AreEqual(0, (int)ImpulseOrigin.Hit,
                "Hit must be the zero value so the safe answer is the default one.");
            Assert.AreNotEqual((int)ImpulseOrigin.Hit, (int)ImpulseOrigin.SelfApplied);

            StringAssert.Contains("ImpulseOrigin origin = ImpulseOrigin.Hit",
                StripComments(Read(ControllerPath)),
                "Existing knockback call sites must keep meaning 'somebody hit you' untouched.");
        }

        /// <summary>
        /// The suppression is structural, not a filter: the victim-beat observers simply do not
        /// read the self-impulse counter. If either one ever starts to, the dodge goes straight
        /// back to reading as an attack.
        /// </summary>
        [Test]
        public void VictimBeatObservers_NeverReadTheSelfImpulseCounter()
        {
            foreach (var path in new[] { HitFeedbackPath, ControlVfxPath })
            {
                string src = StripComments(Read(path));
                StringAssert.Contains("KnockbackEventId", src,
                    $"{Path.GetFileName(path)} must still observe real knockbacks.");
                StringAssert.DoesNotContain("SelfImpulseEventId", src,
                    $"{Path.GetFileName(path)} drives the vocabulary of BEING HIT — flash, recoil, " +
                    "victim-tier shake, ground shockwave. A self-applied dodge must not reach it.");
            }
        }

        // ---- 5. The authority gate opens FixedUpdateNetwork -------------------

        /// <summary>
        /// CONVENTIONS.md: the gate opens the method. It was sitting below a per-tick Verbose
        /// log, which on any peer with unresolved stats printed at the 32 Hz simulation rate
        /// with <c>MinLevel</c> defaulting to Verbose in dev — the kind of noise that makes
        /// people stop reading the log at all. Nothing was written above it, so this was never
        /// a correctness bug; it was a diagnostic pointed at the wrong peer.
        /// </summary>
        [Test]
        public void FixedUpdateNetwork_OpensWithTheAuthorityGate()
        {
            string body = BodyAt(StripComments(Read(ControllerPath)), "public override void FixedUpdateNetwork()", 900);

            int gate = body.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal);
            Assert.Greater(gate, 0, "FixedUpdateNetwork must gate on HasStateAuthority.");

            string beforeGate = body.Substring(0, gate);
            StringAssert.DoesNotContain("_log", beforeGate,
                "Nothing may run before the gate — least of all a logger, which on a proxy " +
                "reports a condition that peer neither owns nor can fix, every single tick.");
            StringAssert.DoesNotContain("_movement == null", beforeGate,
                "The stats-resolution diagnostic belongs under the gate: _movement is built in " +
                "Spawned from _activeStats and the StateAuthority is the peer where unresolved " +
                "stats actually stop the chicken moving. Proxies were never the audience.");
        }

        /// <summary>A stuck condition that reprints every tick stops being read. The report is
        /// latched on the way in and released on the way out, so a genuinely intermittent
        /// failure still reports each occurrence.</summary>
        [Test]
        public void MissingMovementDiagnostic_IsLatched_NotPerTick()
        {
            string src = StripComments(Read(ControllerPath));

            StringAssert.Contains("_movementMissingReported", src,
                "The missing-ChickenMovement report must be edge-latched.");
            Assert.AreEqual(2, Regex.Matches(src, @"_movementMissingReported\s*=").Count,
                "Exactly two writes expected: set when the condition opens, cleared when it " +
                "closes. One write alone means it only ever reports the first occurrence.");
        }
    }
}
