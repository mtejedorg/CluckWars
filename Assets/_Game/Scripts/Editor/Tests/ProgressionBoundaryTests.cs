using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CluckWars.Progression;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Enforces the gameplay → progression boundary. There is no <c>.asmdef</c> under
    /// <c>Assets/_Game</c> — everything compiles into one <c>Assembly-CSharp</c> — so the compiler
    /// cannot keep gameplay off progression's internals. These source scans do, in the style of
    /// <c>StealRulesTests.SourceFilesCallingTheDrainRpc</c>.
    /// </summary>
    /// <remarks>
    /// Every scan is <b>code only</b>: <c>//</c> comments and <c>/* */</c> block comments (tracked
    /// across lines, including one opened mid-line) are removed by <see cref="SourceScan"/>. The rules
    /// are about API and dependencies, not prose, and slice 0's docs legitimately cite gameplay
    /// provenance (e.g. <c>RoundTypes</c> names <c>MatchConfigSO</c>). Every scan also asserts it
    /// found what it expects to find, so a rename cannot blind it.
    /// </remarks>
    public sealed class ProgressionBoundaryTests
    {
        private const string ProgressionDir = SourceScan.ScriptsRoot + "/Progression";

        /// <summary>
        /// The only <c>CluckWars.Progression</c> types gameplay code may name. Everything else in the
        /// namespace — the sinks, the tracker, the journal, the service — is internal to progression,
        /// whatever its C# visibility. <c>UI/</c> has its own, slightly wider list (<see cref="UiSurface"/>).
        /// </summary>
        private static readonly string[] ContractSurface =
        {
            nameof(IMatchEventSink),
            nameof(RoundRuleset),
            nameof(RoundStandings),
            nameof(RoundStandingEntry),
            nameof(UnlockKeyTable),
        };

        /// <summary>
        /// Folders exempt from the gameplay contract-surface scan: progression itself, the composition
        /// root (it names the tracker, journal and service), the UI (scanned against its own, wider
        /// <see cref="UiSurface"/> below) and editor code. Default-deny: any other folder, including one
        /// added later, is scanned automatically.
        /// </summary>
        private static readonly string[] ExemptFolders = { "Progression", "Installers", "UI" };

        // ---- 1b. The UI's surface --------------------------------------------------

        private const string UiDir = SourceScan.ScriptsRoot + "/UI";

        /// <summary>
        /// What <c>UI/</c> may name from <c>CluckWars.Progression</c>: the contract surface plus the read
        /// side of <see cref="IProgressionService"/>. UI reads progression only through that interface; it
        /// never names the tracker, the service's concrete type, the journal or the ledger.
        /// </summary>
        /// <remarks>
        /// Slice 3 added exactly two: <see cref="ProgressionIdentity"/> (whose display models are
        /// nested inside it on purpose, so they cost nothing here) and <see cref="NameplateSlot"/>,
        /// which appears in the two command signatures. Everything else identity is built from —
        /// <c>RecordDefinitionSO</c>, <c>RecordEngine</c>, <c>IdentityFold</c>, <c>NameGenerator</c>,
        /// <c>NameplateComposer</c>, <c>ProfileEvent</c> — stays off the UI's surface.
        /// </remarks>
        private static readonly string[] UiSurface = ContractSurface.Concat(new[]
        {
            nameof(IProgressionService),
            nameof(ProgressionProfile),
            nameof(ProgressionIdentity),
            nameof(NameplateSlot),
            nameof(RoundAward),
            nameof(ProgressionFault),
            nameof(ProgressionFaultKind),
        }).ToArray();

        [Test]
        public void UiCode_NamesOnlyTheProgressionReadSurface()
        {
            var progressionTypeNames = ProgressionTypeNames();
            foreach (var allowed in UiSurface)
            {
                CollectionAssert.Contains(progressionTypeNames, allowed,
                    $"UI surface type '{allowed}' no longer exists in CluckWars.Progression. Update UiSurface deliberately.");
            }

            var disallowed = progressionTypeNames.Except(UiSurface).ToList();
            CollectionAssert.Contains(disallowed, nameof(MatchTracker), "The UI deny list lost MatchTracker — the sweep has gone blind.");
            CollectionAssert.Contains(disallowed, nameof(ProgressionService),
                "ProgressionService (the concrete type) must stay off the UI surface: UI reads IProgressionService.");

            var files = SourceScan.FilesUnder(UiDir).ToList();
            foreach (string consumer in new[] { UiDir + "/MatchOverlaysController.cs", UiDir + "/ProfileController.cs" })
            {
                Assert.IsTrue(files.Contains(consumer), $"{consumer} is not in the UI scan; the file enumeration changed.");
                Assert.IsTrue(SourceScan.CodeLines(consumer).Any(l => Regex.IsMatch(l.Code, $@"\b{nameof(IProgressionService)}\b")),
                    $"{consumer} has no code line naming {nameof(IProgressionService)}; the UI scan would be checking nothing.");
            }

            var patterns = disallowed.Select(n => (Name: n, Rx: new Regex($@"\b{Regex.Escape(n)}\b"))).ToList();
            var violations = new List<string>();
            foreach (var path in files)
            {
                foreach (var (number, code) in SourceScan.CodeLines(path))
                {
                    foreach (var (name, rx) in patterns)
                    {
                        if (rx.IsMatch(code)) violations.Add($"{path}:{number} names {name}: {code}");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "UI code may name only the progression contract surface plus IProgressionService's read side (" +
                string.Join(", ", UiSurface) + "). Violations:\n" + string.Join("\n", violations));
        }

        /// <summary>Every top-level type name in <c>CluckWars.Progression</c>, as the default-deny sweeps see them.</summary>
        private static List<string> ProgressionTypeNames() =>
            typeof(IMatchEventSink).Assembly.GetTypes()
                .Where(t => t.Namespace != null &&
                            (t.Namespace == "CluckWars.Progression" || t.Namespace.StartsWith("CluckWars.Progression.")))
                .Where(t => !t.IsNested)
                .Select(t => t.Name)
                .Where(n => !n.Contains("<"))
                .Select(n => n.Split('`')[0])
                .Distinct()
                .ToList();

        private static readonly string[] SinkConsumers =
        {
            SourceScan.ScriptsRoot + "/Gameplay/ChickenCargo.cs",
            SourceScan.ScriptsRoot + "/Gameplay/ChickenMatchStats.cs",
            SourceScan.ScriptsRoot + "/Gameplay/AbilityController.cs",
            SourceScan.ScriptsRoot + "/Gameplay/GameManager.cs",
        };

        // ---- 1. Contract surface ---------------------------------------------------

        [Test]
        public void GameplayCode_NamesOnlyTheProgressionContractSurface()
        {
            var progressionTypeNames = typeof(IMatchEventSink).Assembly.GetTypes()
                .Where(t => t.Namespace != null &&
                            (t.Namespace == "CluckWars.Progression" || t.Namespace.StartsWith("CluckWars.Progression.")))
                // A nested type (e.g. GuardedMatchEventSink.Verb) is only reachable through its outer
                // type's name, which is already denied; scanning its bare name would flag any
                // unrelated gameplay identifier that happens to share it.
                .Where(t => !t.IsNested)
                .Select(t => t.Name)
                .Where(n => !n.Contains("<"))                  // compiler-generated
                .Select(n => n.Split('`')[0])                  // generic arity
                .Distinct()
                .ToList();

            foreach (var allowed in ContractSurface)
            {
                CollectionAssert.Contains(progressionTypeNames, allowed,
                    $"Contract type '{allowed}' no longer exists in CluckWars.Progression. Update the " +
                    "contract surface here and in docs/CONVENTIONS.md deliberately — it is the boundary.");
            }

            var disallowed = progressionTypeNames.Except(ContractSurface).ToList();
            CollectionAssert.Contains(disallowed, nameof(NullMatchEventSink),
                "The deny list lost NullMatchEventSink — the reflection sweep has stopped seeing " +
                "CluckWars.Progression, so this scan would pass while checking nothing.");
            CollectionAssert.Contains(disallowed, nameof(GuardedMatchEventSink),
                "GuardedMatchEventSink must stay off the contract surface: only the composition root binds it.");

            var patterns = disallowed.Select(n => (Name: n, Rx: new Regex($@"\b{Regex.Escape(n)}\b"))).ToList();
            var scanned = ScannedGameplayFiles().ToList();
            Assert.IsNotEmpty(scanned, $"No files scanned under {SourceScan.ScriptsRoot}.");

            var violations = new List<string>();
            foreach (var path in scanned)
            {
                foreach (var (number, code) in SourceScan.CodeLines(path))
                {
                    foreach (var (name, rx) in patterns)
                    {
                        if (rx.IsMatch(code)) violations.Add($"{path}:{number} names {name}: {code}");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "Gameplay code may reference only the progression contract surface (" +
                string.Join(", ", ContractSurface) + "). Only Installers/ (the composition root) names " +
                "concrete progression types. Violations:\n" + string.Join("\n", violations));
        }

        [Test]
        public void ContractScan_IsNotBlind_ItSeesTheSinkInEveryConsumer()
        {
            var scanned = new HashSet<string>(ScannedGameplayFiles());
            var sinkWord = new Regex($@"\b{nameof(IMatchEventSink)}\b");

            foreach (var consumer in SinkConsumers)
            {
                Assert.IsTrue(scanned.Contains(consumer),
                    $"{consumer} is not in the contract-surface scan. Either it moved into an exempt folder " +
                    "or the file enumeration changed — the boundary scan would no longer cover it.");
                Assert.IsTrue(SourceScan.CodeLines(consumer).Any(l => sinkWord.IsMatch(l.Code)),
                    $"{consumer} has no code line naming {nameof(IMatchEventSink)}. It is one of slice 1's " +
                    "four announcement consumers; if that changed, update this list.");
            }
        }

        // ---- 2. Steal credits go through ReceiveStolen -----------------------------

        private static readonly Regex DirectCargoWrite = new Regex(@"\.Cargo\s*\+=");

        /// <summary>Files allowed to credit cargo directly. Foraging is not a steal.</summary>
        private static readonly string[] DirectCargoWriteAllowlist = { "PeckAbilitySO.cs" };

        [Test]
        public void NoStealPath_WritesCargoDirectly()
        {
            var violations = new List<string>();
            foreach (var path in SourceScan.AllRuntimeScripts())
            {
                // ChickenCargo owns Cargo and declares ReceiveStolen; its own writes (deposit drains,
                // resets, the drain side of a steal) are not steal credits.
                if (path.EndsWith("/ChickenCargo.cs")) continue;
                if (DirectCargoWriteAllowlist.Contains(Path.GetFileName(path))) continue;

                foreach (var (number, code) in SourceScan.CodeLines(path))
                {
                    if (DirectCargoWrite.IsMatch(code)) violations.Add($"{path}:{number}: {code}");
                }
            }

            Assert.IsEmpty(violations,
                "A steal must credit the thief through ChickenCargo.ReceiveStolen(amount, victim), the one " +
                "chokepoint that announces ResourceStolen. A raw '.Cargo +=' credits silently and " +
                "progression never hears about the steal. If this is not a steal (like Peck's foraging), " +
                "add the file to the allowlist here with a reason. Violations:\n" + string.Join("\n", violations));
        }

        [Test]
        public void DirectCargoWriteAllowlist_HasNotRotted()
        {
            foreach (var file in DirectCargoWriteAllowlist)
            {
                var path = SourceScan.AllRuntimeScripts().SingleOrDefault(p => Path.GetFileName(p) == file);
                Assert.IsNotNull(path, $"Allowlisted {file} no longer exists. Remove it from the allowlist.");
                Assert.IsTrue(SourceScan.CodeLines(path).Any(l => DirectCargoWrite.IsMatch(l.Code)),
                    $"Allowlisted {file} no longer writes '.Cargo +='. Remove it from the allowlist so the " +
                    "exemption cannot cover a future steal.");
            }
        }

        private static readonly Regex DirectBountyWrite = new Regex(@"\bBountyBag\s*\+=");

        /// <summary>
        /// Files allowed to pay into the bounty bag directly, each with the <b>one</b> write it may make.
        /// <c>AssassinExecute</c> pays the flat <c>ExecuteBounty</c> — income, not a steal, so it is
        /// deliberately never announced. Pinned to that exact statement so the exemption cannot cover a
        /// second write (say, a victim's cargo) added to the same file later.
        /// </summary>
        private static readonly (string File, Regex OnlyWrite)[] DirectBountyWriteAllowlist =
        {
            ("AssassinExecute.cs", new Regex(@"^_cargo\.BountyBag\s*\+=\s*bounty\s*;$")),
        };

        [Test]
        public void NoStealPath_WritesTheBountyBagDirectly()
        {
            var violations = new List<string>();
            foreach (var path in SourceScan.AllRuntimeScripts())
            {
                // ChickenCargo owns BountyBag; its write is the execute's cargo transfer
                // (RPC_TransferAllToBountyBag), which ChickenCargo.AnnounceExecuteSteal announces.
                if (path.EndsWith("/ChickenCargo.cs")) continue;
                var allowed = DirectBountyWriteAllowlist.FirstOrDefault(a => a.File == Path.GetFileName(path));

                foreach (var (number, code) in SourceScan.CodeLines(path))
                {
                    if (!DirectBountyWrite.IsMatch(code)) continue;
                    if (allowed.OnlyWrite != null && allowed.OnlyWrite.IsMatch(code)) continue;
                    violations.Add($"{path}:{number}: {code}");
                }
            }

            Assert.IsEmpty(violations,
                "Cargo taken from a rival must not reach a bounty bag by a new path: the execute's transfer lives in " +
                "ChickenCargo.RPC_TransferAllToBountyBag and is announced by ChickenCargo.AnnounceExecuteSteal. A raw " +
                "'BountyBag +=' elsewhere credits silently. If it is income rather than a steal (like the execute " +
                "bounty), add the file and its one statement to the allowlist here with a reason. Violations:\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void DirectBountyWriteAllowlist_HasNotRotted_AndCoversExactlyOneWrite()
        {
            foreach (var (file, onlyWrite) in DirectBountyWriteAllowlist)
            {
                var path = SourceScan.AllRuntimeScripts().SingleOrDefault(p => Path.GetFileName(p) == file);
                Assert.IsNotNull(path, $"Allowlisted {file} no longer exists. Remove it from the allowlist.");

                var writes = SourceScan.CodeLines(path).Where(l => DirectBountyWrite.IsMatch(l.Code)).ToList();
                Assert.AreEqual(1, writes.Count,
                    $"Allowlisted {file} must write 'BountyBag +=' exactly once (found {writes.Count}): " +
                    string.Join(" | ", writes.Select(w => w.Code)));
                StringAssert.IsMatch(onlyWrite.ToString(), writes[0].Code,
                    $"{file}'s one bounty-bag write must be the allowlisted statement (the execute bounty), not another credit.");
            }
        }

        // ---- 3. Progression vocabulary ---------------------------------------------

        private static readonly Regex GameplayWord = new Regex("food|chicken|cluck", RegexOptions.IgnoreCase);

        /// <summary>
        /// Namespaces progression code may import: its own, and infrastructure that is not
        /// gameplay. Every other <c>CluckWars.*</c> namespace in the runtime assembly is denied.
        /// </summary>
        private static readonly string[] AllowedProgressionImports =
        {
            "CluckWars.Progression",
            "CluckWars.Logging",
            "CluckWars.Services",
        };

        /// <summary>The one progression file allowed to name a gameplay type: the translation table.</summary>
        private const string TranslationTable = "UnlockKeyTable.cs";

        [Test]
        public void VocabularyMatcher_FlagsGameplayWords_ButNotTheRootNamespace()
        {
            // Self-test of the matcher, so the scan below cannot pass by matching nothing.
            Assert.IsTrue(ViolatesVocabulary("public float FoodTotal;"));
            Assert.IsTrue(ViolatesVocabulary("ChickenClass role"));
            Assert.IsTrue(ViolatesVocabulary("var cluckMeter = 0;"));
            Assert.IsFalse(ViolatesVocabulary("namespace CluckWars.Progression"));
            Assert.IsFalse(ViolatesVocabulary("void ResourceBanked(int actorId, float amount);"));
        }

        [Test]
        public void ProgressionCode_UsesGenericVocabulary_AndImportsNoGameplay()
        {
            var files = SourceScan.FilesUnder(ProgressionDir).ToList();
            foreach (var expected in new[] { "IMatchEventSink.cs", "NullMatchEventSink.cs", "GuardedMatchEventSink.cs", "RoundTypes.cs", TranslationTable })
            {
                Assert.IsTrue(files.Any(p => Path.GetFileName(p) == expected),
                    $"{expected} not found under {ProgressionDir}; the vocabulary scan's file set has changed.");
            }

            var deniedNamespaces = DeniedProgressionImports();
            CollectionAssert.Contains(deniedNamespaces, "CluckWars.Gameplay", "Namespace sweep has gone blind.");
            CollectionAssert.Contains(deniedNamespaces, "CluckWars.Abilities", "Namespace sweep has gone blind.");
            var namespaceRx = deniedNamespaces.Select(n => (Name: n, Rx: new Regex($@"\b{Regex.Escape(n)}\b"))).ToList();

            var violations = new List<string>();
            foreach (var path in files)
            {
                if (Path.GetFileName(path) == TranslationTable) continue;

                foreach (var (number, code) in SourceScan.CodeLines(path))
                {
                    if (ViolatesVocabulary(code))
                        violations.Add($"{path}:{number} uses gameplay vocabulary: {code}");
                    foreach (var (name, rx) in namespaceRx)
                    {
                        if (rx.IsMatch(code)) violations.Add($"{path}:{number} references {name}: {code}");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "Progression's API vocabulary is generic (actor, resource, round, ability key) and it " +
                "depends on no gameplay namespace; " + TranslationTable + " is the one translation point. " +
                "Violations:\n" + string.Join("\n", violations));
        }

        [Test]
        public void TranslationTableExemption_HasNotRotted()
        {
            var path = ProgressionDir + "/" + TranslationTable;
            Assert.IsTrue(SourceScan.CodeLines(path).Any(l => l.Code == "using CluckWars.Gameplay;"),
                $"{TranslationTable} no longer imports CluckWars.Gameplay. If it no longer needs a gameplay " +
                "type, drop its exemption from the vocabulary scan.");
        }

        // ---- Helpers ----------------------------------------------------------------

        private static bool ViolatesVocabulary(string code) =>
            GameplayWord.IsMatch(code.Replace("CluckWars", string.Empty));

        private static List<string> DeniedProgressionImports() =>
            typeof(IMatchEventSink).Assembly.GetTypes()
                .Select(t => t.Namespace)
                .Where(n => n != null && n.StartsWith("CluckWars."))
                .Distinct()
                .Where(n => !AllowedProgressionImports.Any(a => n == a || n.StartsWith(a + ".")))
                .ToList();

        private static IEnumerable<string> ScannedGameplayFiles() =>
            SourceScan.AllRuntimeScripts()
                .Where(p => !ExemptFolders.Any(f => p.StartsWith(SourceScan.ScriptsRoot + "/" + f + "/")));
    }

    /// <summary>
    /// Self-tests for <see cref="SourceScan"/>. The boundary and emit-site tests are only as good as
    /// the scanner under them, so it is pinned on fed-in lines rather than trusted.
    /// </summary>
    public sealed class SourceScanTests
    {
        private static string[] Codes(params string[] lines) =>
            SourceScan.CodeLines(lines).Select(l => l.Code).ToArray();

        private static string[] Body(string signature, params string[] lines) =>
            SourceScan.MethodBody(lines, signature).Select(l => l.Code).ToArray();

        [Test]
        public void BlockComments_AreNeverCode_AcrossLines_AndWhenOpenedMidLine()
        {
            var codes = Codes(
                "int a = 1;",
                "/* int hidden1 = 2;",
                "   int hidden2 = 3; */ int b = 4;",
                "int c = 5; /* opened mid-line",
                "int hidden3 = 6; */ int d = 7;",
                "/* one-line */ int e = 8; /* again */",
                "// int hidden4 = 9;",
                "int f = 10; // tail");

            CollectionAssert.AreEqual(
                new[] { "int a = 1;", "int b = 4;", "int c = 5;", "int d = 7;", "int e = 8;", "int f = 10;" }, codes);
            Assert.IsFalse(codes.Any(c => c.Contains("hidden")), "Commented-out code was counted as code.");
        }

        [Test]
        public void CommentMarkersInsideLiterals_AreNotComments()
        {
            var codes = Codes(
                "var url = \"http://example\"; // real comment",
                "var s = \"/* not a comment */\";",
                "var v = @\"C:\\path // still a string\";",
                "var ch = '/';",
                "int after = 1;");

            CollectionAssert.AreEqual(new[]
            {
                "var url = \"http://example\";",
                "var s = \"/* not a comment */\";",
                "var v = @\"C:\\path // still a string\";",
                "var ch = '/';",
                "int after = 1;",
            }, codes);
        }

        [Test]
        public void MethodBody_IgnoresABlockCommentedOutCopy()
        {
            var body = Body("void Target(",
                "/* void Target()",
                "{",
                "    Old();",
                "} */",
                "void Target()",
                "{",
                "    New();",
                "}");

            CollectionAssert.AreEqual(new[] { "New();" }, body);
        }

        [Test]
        public void MethodBody_ExpressionBodied_ReturnsTheExpression_AndStopsThere()
        {
            CollectionAssert.AreEqual(new[] { "42" }, Body("int Answer(",
                "int Answer() => 42;",
                "void Next()",
                "{",
                "    Other();",
                "}"));

            var multiLine = Body("Rules Make(",
                "Rules Make() => new Rules",
                "{",
                "    A = 1,",
                "};",
                "void Next() { Other(); }");
            Assert.IsTrue(multiLine.Any(c => c.Contains("A = 1")), "The expression's initializer must be part of the body.");
            Assert.IsFalse(multiLine.Any(c => c.Contains("Other")), "An expression body must stop at its own ';'.");
        }

        [Test]
        public void MethodBody_NeverCollectsTheNextMembersBody()
        {
            Assert.Throws<SourceScanException>(() => SourceScan.MethodBody(new[]
            {
                "void Target();",
                "void Other()",
                "{",
                "    Stolen();",
                "}",
            }, "void Target("), "A declaration without a body must fail, not borrow the next method's.");

            Assert.Throws<SourceScanException>(() => SourceScan.MethodBody(new[]
            {
                "void Target()",
                "[SomeAttribute]",
                "void Other()",
                "{",
                "    Stolen();",
                "}",
            }, "void Target("), "'{' must open on the signature line or the next code line.");

            Assert.Throws<SourceScanException>(() => SourceScan.MethodBody(new[] { "void Other() { }" }, "void Target("),
                "A missing method must fail loudly.");

            CollectionAssert.AreEqual(new[] { "Own();" }, Body("void Target(",
                "void Target()",
                "// a comment line between signature and brace is not a code line",
                "{",
                "    Own();",
                "}",
                "void Other() { Stolen(); }"));
        }

        [Test]
        public void MethodBody_BracesInsideLiterals_DoNotEndTheBody()
        {
            var body = Body("void Target(",
                "void Target()",
                "{",
                "    var s = \"}\";",
                "    var t = $\"{x}}}\";",
                "    var c = '}';",
                "    Still();",
                "}",
                "void Other() { Stolen(); }");

            Assert.IsTrue(body.Contains("Still();"), "A brace inside a literal closed the body early.");
            Assert.IsFalse(body.Any(c => c.Contains("Stolen")), "The body ran on into the next method.");
        }

        [Test]
        public void MethodBody_SingleLineBlock_ReturnsItsStatements()
        {
            CollectionAssert.AreEqual(new[] { "A(); B();" }, Body("void Target(", "void Target() { A(); B(); }"));
        }
    }

    /// <summary>Thrown by <see cref="SourceScan"/> when it cannot locate what a test asked for.</summary>
    internal sealed class SourceScanException : Exception
    {
        public SourceScanException(string message) : base(message) { }
    }

    /// <summary>
    /// Line-level C# source scanning shared by the boundary and emit-site tests. A small lexer
    /// removes <c>//</c> and <c>/* */</c> comments (block state carried across lines) and knows
    /// string, verbatim-string and char literals, so comment markers and braces inside a literal
    /// never count. Simple by design — these files are ours and conventional — but never silent:
    /// anything it cannot locate throws <see cref="SourceScanException"/>.
    /// </summary>
    internal static class SourceScan
    {
        public const string ScriptsRoot = "Assets/_Game/Scripts";

        /// <summary>One line of a member body, trimmed, with its brace depth relative to the body.</summary>
        public sealed class BodyLine
        {
            public string Code;
            public int Depth;
        }

        /// <summary>
        /// One physical line with its comments removed. <see cref="Code"/> keeps literals intact;
        /// <see cref="Structure"/> is the same text with literal contents blanked, for brace and
        /// terminator counting. Both have the same length, so an index into one is valid in the other.
        /// </summary>
        public sealed class ScannedLine
        {
            public int Number;
            public string Code;
            public string Structure;
        }

        private enum LexState { Code, Block, String, Verbatim, Char }

        public static IEnumerable<string> FilesUnder(string dir)
        {
            Assert.IsTrue(Directory.Exists(dir), $"{dir} not found on disk.");
            return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/'));
        }

        /// <summary>Every runtime script under <see cref="ScriptsRoot"/>: any <c>Editor/</c> folder is excluded.</summary>
        public static IEnumerable<string> AllRuntimeScripts() =>
            FilesUnder(ScriptsRoot).Where(p => !p.Contains("/Editor/"));

        public static string[] ReadLines(string path)
        {
            Assert.IsTrue(File.Exists(path), $"{path} not found on disk.");
            return File.ReadAllLines(path);
        }

        public static List<ScannedLine> Scan(IReadOnlyList<string> lines)
        {
            var result = new List<ScannedLine>(lines.Count);
            var code = new StringBuilder();
            var structure = new StringBuilder();
            var state = LexState.Code;

            for (int n = 0; n < lines.Count; n++)
            {
                string line = lines[n] ?? string.Empty;
                code.Clear();
                structure.Clear();

                // Regular strings and char literals cannot span lines; block comments and verbatim strings can.
                if (state == LexState.String || state == LexState.Char) state = LexState.Code;

                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    char next = i + 1 < line.Length ? line[i + 1] : '\0';

                    switch (state)
                    {
                        case LexState.Code:
                            if (c == '/' && next == '/') { i = line.Length; break; }
                            if (c == '/' && next == '*') { state = LexState.Block; i++; break; }
                            if (c == '@' && next == '"') { Both(code, structure, "@\""); state = LexState.Verbatim; i++; break; }
                            if (c == '"') { Both(code, structure, "\""); state = LexState.String; break; }
                            if (c == '\'') { Both(code, structure, "'"); state = LexState.Char; break; }
                            code.Append(c);
                            structure.Append(c);
                            break;

                        case LexState.Block:
                            if (c == '*' && next == '/')
                            {
                                state = LexState.Code;
                                i++;
                                Both(code, structure, " "); // keep the tokens either side apart
                            }
                            break;

                        case LexState.String:
                        case LexState.Char:
                            char close = state == LexState.String ? '"' : '\'';
                            if (c == '\\' && i + 1 < line.Length)
                            {
                                code.Append(c).Append(next);
                                structure.Append("  ");
                                i++;
                            }
                            else if (c == close)
                            {
                                Both(code, structure, close.ToString());
                                state = LexState.Code;
                            }
                            else
                            {
                                code.Append(c);
                                structure.Append(' ');
                            }
                            break;

                        case LexState.Verbatim:
                            if (c == '"' && next == '"')
                            {
                                code.Append("\"\"");
                                structure.Append("  ");
                                i++;
                            }
                            else if (c == '"')
                            {
                                Both(code, structure, "\"");
                                state = LexState.Code;
                            }
                            else
                            {
                                code.Append(c);
                                structure.Append(' ');
                            }
                            break;
                    }
                }

                result.Add(new ScannedLine { Number = n + 1, Code = code.ToString(), Structure = structure.ToString() });
            }

            return result;
        }

        /// <summary>Non-blank code lines of <paramref name="path"/>, comments removed, trimmed.</summary>
        public static List<(int Number, string Code)> CodeLines(string path) => CodeLines(ReadLines(path));

        /// <inheritdoc cref="CodeLines(string)"/>
        public static List<(int Number, string Code)> CodeLines(IReadOnlyList<string> lines) =>
            Scan(lines)
                .Select(l => (l.Number, Code: l.Code.Trim()))
                .Where(l => l.Code.Length > 0)
                .ToList();

        /// <inheritdoc cref="MethodBody(IReadOnlyList{string}, string, string)"/>
        public static List<BodyLine> MethodBody(string path, string signature) =>
            MethodBody(ReadLines(path), signature, path);

        /// <summary>
        /// The code of the member whose declaration contains <paramref name="signature"/>, excluding its
        /// own braces. An expression-bodied member (<c>=&gt;</c>) returns its expression, up to its
        /// terminating <c>;</c>.
        /// </summary>
        /// <exception cref="SourceScanException">
        /// The signature is not found; it is a declaration or call with no body; or neither <c>{</c>
        /// nor <c>=&gt;</c> appears on the signature line or the next code line — the only safe
        /// places, since anything further could be the next member's body.
        /// </exception>
        public static List<BodyLine> MethodBody(IReadOnlyList<string> lines, string signature, string origin = "the given lines")
        {
            var scanned = Scan(lines);
            int sigLine = scanned.FindIndex(l => l.Code.Contains(signature));
            if (sigLine < 0)
            {
                throw new SourceScanException($"'{signature}' not found in {origin}. The member was renamed or " +
                    "removed, so this check can no longer see it — update the test.");
            }

            int searchFrom = scanned[sigLine].Code.IndexOf(signature, StringComparison.Ordinal) + signature.Length;
            int codeLinesAfterSignature = 0;
            for (int li = sigLine; li < scanned.Count; li++)
            {
                string s = scanned[li].Structure;
                if (li != sigLine)
                {
                    if (s.Trim().Length == 0) continue; // blank or comment-only
                    if (++codeLinesAfterSignature > 1) break;
                }

                for (int ci = li == sigLine ? searchFrom : 0; ci < s.Length; ci++)
                {
                    char c = s[ci];
                    if (c == '{') return BlockBody(scanned, li, ci, signature, origin);
                    if (c == '=' && ci + 1 < s.Length && s[ci + 1] == '>')
                        return ExpressionBody(scanned, li, ci + 2, signature, origin);
                    if (c == ';')
                    {
                        throw new SourceScanException($"'{signature}' in {origin} (line {scanned[li].Number}) is a " +
                            "declaration or a call, not a member with a body.");
                    }
                }
            }

            throw new SourceScanException($"'{signature}' in {origin} (line {scanned[sigLine].Number}): no '{{' or " +
                "'=>' on the signature line or the next code line, so its body cannot be located safely — " +
                "reading further could return the next member's body.");
        }

        private static List<BodyLine> BlockBody(List<ScannedLine> scanned, int openLine, int openCol, string signature, string origin)
        {
            var body = new List<BodyLine>();
            int depth = 1;
            for (int li = openLine; li < scanned.Count; li++)
            {
                string s = scanned[li].Structure;
                int from = li == openLine ? openCol + 1 : 0;
                int lineDepth = depth;
                int end = s.Length;
                bool closed = false;

                for (int ci = from; ci < s.Length; ci++)
                {
                    if (s[ci] == '{') depth++;
                    else if (s[ci] == '}' && --depth == 0) { end = ci; closed = true; break; }
                }

                string text = scanned[li].Code.Substring(from, end - from).Trim();
                if (text.Length > 0) body.Add(new BodyLine { Code = text, Depth = lineDepth - 1 });
                if (closed) return body;
            }

            throw new SourceScanException($"'{signature}' in {origin}: the body opened but never closed.");
        }

        private static List<BodyLine> ExpressionBody(List<ScannedLine> scanned, int startLine, int startCol, string signature, string origin)
        {
            var body = new List<BodyLine>();
            int depth = 0;
            for (int li = startLine; li < scanned.Count; li++)
            {
                string s = scanned[li].Structure;
                int from = li == startLine ? startCol : 0;
                int lineDepth = depth;
                int end = s.Length;
                bool done = false;

                for (int ci = from; ci < s.Length; ci++)
                {
                    if (s[ci] == '{') depth++;
                    else if (s[ci] == '}') depth--;
                    else if (s[ci] == ';' && depth == 0) { end = ci; done = true; break; }
                }

                string text = scanned[li].Code.Substring(from, end - from).Trim();
                if (text.Length > 0) body.Add(new BodyLine { Code = text, Depth = lineDepth });
                if (done) return body;
            }

            throw new SourceScanException($"'{signature}' in {origin}: the expression body never reached its ';'.");
        }

        /// <summary>
        /// The lines of the innermost block containing <paramref name="index"/>: the contiguous run
        /// around it whose depth is at least its own.
        /// </summary>
        public static List<BodyLine> EnclosingBlock(List<BodyLine> body, int index)
        {
            int d = body[index].Depth;
            int start = index;
            while (start > 0 && body[start - 1].Depth >= d) start--;
            int end = index;
            while (end < body.Count - 1 && body[end + 1].Depth >= d) end++;
            return body.GetRange(start, end - start + 1);
        }

        /// <summary>Index of the first body line containing <paramref name="fragment"/>; fails the test if none.</summary>
        public static int IndexOf(List<BodyLine> body, string fragment, string what)
        {
            int i = body.FindIndex(l => l.Code.Contains(fragment));
            Assert.GreaterOrEqual(i, 0, $"{what}: no code line contains '{fragment}'.");
            return i;
        }

        private static void Both(StringBuilder code, StringBuilder structure, string text)
        {
            code.Append(text);
            structure.Append(text);
        }
    }
}
