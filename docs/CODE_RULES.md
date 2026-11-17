# Cluck Wars Code Rules

Generated from commit `b7c2616e` (branch `simplify-abilities`), 2026-11-13. Committed code only: every path, line, test and type named here was read with `git show b7c2616e:<path>` or found with `git grep b7c2616e`. The next commit, `f6309c3e`, changes only `docs/BACKLOG.md` (it adds BL-162 and BL-163, which this document cites).

**Purpose.** The consolidated coding, text and data, UI, audio, asset, testing, build, process and design-constraint rules of Cluck Wars, each paired with how it is enforced. A rule that no test, compiler or configuration enforces rots silently, so every such rule says so plainly and gives the check an auditor can run and the guard that should be written. The repeatable architecture audit (`architecture-audit` skill) reads this file and its sibling.

**Sibling documents.** `docs/ARCHITECTURE_RULES.md` (layering and assemblies, DI and self-injection, networking and authority, events, data-driven abilities, logging and error handling; IDs `ARCH-...`) and `docs/FILE_STRUCTURE.md` (where things live). This file does not repeat them; it points at them by name. `docs/CONVENTIONS.md` remains the narrative source; **where this document and a stale line elsewhere disagree, this document wins until the stale line is fixed** (Appendix A lists them).

### How to read an entry

```
### CODE-AREA-nn — imperative title
- Rule:          the rule, with the source's strength words ("never", "must")
- Why:           one sentence, or "not stated"
- Enforced by:   TEST `Class.Method` | COMPILER | CONFIG | CONVENTION (review only) | NONE
- Audit check:   (CONVENTION or NONE parts only) the grep or review step that finds violations
- Proposed guard:(NONE only) the test that would enforce it
- Source:        path:line, ...; owner thread label + date when the owner said it in a session
- Status:        CURRENT | PARTIAL (what is and is not enforced)
```

**Enforcement vocabulary.** TEST: an EditMode test fails (names verified to exist at `b7c2616e`; tests live in `Assets/_Game/Scripts/Editor/Tests/`). COMPILER: the code does not compile. CONFIG: a configuration file or a project tool refuses mechanically when it runs (`.gitattributes`, asmdefs, the permission system, `tools/test-session/verify.ps1` exit codes). CONVENTION (review only): written down, checked only by a human or agent reading the diff (the index says REVIEW). NONE: nothing checks it. When one part of a rule is TEST-enforced and another is not, the entry says "TEST ...; rest CONVENTION/NONE" and the status is PARTIAL. **No test runs automatically** (no git hook, no test CI; gap G-10), so even TEST means "fails when someone runs the suite".

**IDs.** `CODE-STYLE` naming, placement, language-level conventions, line endings; `CODE-TEXT` player-facing text, localization, numbers from data; `CODE-UI` UI Toolkit, UXML/USS, screenshots; `CODE-AUD` audio; `CODE-ASSET` assets, scenes, prefabs, GUIDs, URP, rigs; `CODE-TEST` testing and verification; `CODE-BUILD` builds, tooling, Unity MCP/CLI, suites, environment; `CODE-PROC` agents, models, branching, commits, docs, backlog; `CODE-DESIGN` balance and design constraints that bind code; `CODE-FOOT` footguns. IDs are permanent: never renumber; retire an ID instead of reusing it. Gaps are `G-nn` (section 7), open questions `Qn` (section 10).

**Source notation.** Repo paths are relative to the repo root at `b7c2616e`. `.claude/CLAUDE.md` is the git-ignored project agent config, read from disk. `~/.claude/CLAUDE.md` is the owner's global config. `M:<file>.md` is a file in the project memory folder (`~/.claude/projects/C--Users-MARCO-Documents-GitHub-CluckWars/memory/`). Owner rulings stated in a Claude session are labelled by thread (UI thread, balance thread, audit thread, ...) and date; quotes are the owner's words. A rule whose only provenance is the 2026-11-12/13 architecture audit (a lesson learned that no repo file states) cites "the 2026-11-12/13 architecture audit; ratified as a rule by Maestro on 2026-11-14 (ruling D13)".

### Contents

1. Header (this section) · 2. Index · 3. Rules (3.1 STYLE, 3.2 TEXT, 3.3 UI, 3.4 AUD, 3.5 ASSET, 3.6 TEST, 3.7 BUILD, 3.8 PROC, 3.9 DESIGN, 3.10 FOOT) · 4. Worked example: numbers in ability text · 5. Test pattern catalogue · 6. Rules the tests enforced before any document stated them · 7. Unenforced and partly enforced rules, ranked · 8. Appendix A: stale sources · 9. Appendix B: superseded rules · 10. Open questions for the owner

## 2. Index

142 rules. Enforced-by is the first mechanism of the entry (REVIEW = convention, checked only by reading); Status PARTIAL means part of the rule is not enforced.

| Rule ID | Title | Enforced by | Status |
|---|---|---|---|
| CODE-STYLE-01 | Name every ScriptableObject type with the `SO` suffix and keep its assets in `Assets/_Game/Data/` | TEST | PARTIAL |
| CODE-STYLE-02 | Keep `Assets/_Game/Resources/` for what must load by name | REVIEW | CURRENT |
| CODE-STYLE-03 | Keep the repo LF-only | CONFIG | CURRENT |
| CODE-STYLE-04 | Alias `LogLevel` in files that import both `CluckWars.Logging` and `Fusion` | COMPILER | CURRENT |
| CODE-STYLE-05 | Alias NUnit's `Assert` in any test file that imports `Fusion` | COMPILER | CURRENT |
| CODE-STYLE-06 | Keep `ChickenClass : byte`; never use a `-1` sentinel | TEST | CURRENT |
| CODE-STYLE-07 | Use `var` or `static readonly` for platform enums and kill switches, not `const` | REVIEW | CURRENT |
| CODE-STYLE-08 | Split an oversized MonoBehaviour only as a partial class | TEST | PARTIAL |
| CODE-STYLE-09 | Do not introduce shared UXML element-name constants | REVIEW | CURRENT |
| CODE-STYLE-10 | Keep code names when a display name changes | REVIEW | CURRENT |
| CODE-TEXT-01 | Put every player-facing string in the wording dictionary | TEST | CURRENT |
| CODE-TEXT-02 | Name a key by its `UiKeys` constant, never a string literal | TEST | PARTIAL |
| CODE-TEXT-03 | Write no player-facing literal into UI code | TEST | PARTIAL |
| CODE-TEXT-04 | Leave UXML text empty or a `@key` reference | TEST | PARTIAL |
| CODE-TEXT-05 | Match every template's placeholders to its `[UiArgs]` contract and every call site | TEST | PARTIAL |
| CODE-TEXT-06 | Fill every number in ability and perk text from the SO field it describes | TEST | PARTIAL |
| CODE-TEXT-07 | Keep an active ability's `.asset` `Description` equal to its rendered text | TEST | CURRENT |
| CODE-TEXT-08 | Write ability text for kids: plain words, short, no jargon | TEST | PARTIAL |
| CODE-TEXT-09 | Make a missing key or argument visible and report it once | TEST | CURRENT |
| CODE-TEXT-10 | Read every number a player sees from the data that drives the behaviour | TEST | PARTIAL |
| CODE-TEXT-11 | Leave class taglines as flavour; do not "fix" them into stats | REVIEW | CURRENT |
| CODE-TEXT-12 | Keep the generic ability names until the owner picks new ones | REVIEW | CURRENT |
| CODE-UI-01 | Build menu and screen UI in UI Toolkit; never procedurally | REVIEW | CURRENT |
| CODE-UI-02 | Do not call UI work done without a screenshot | CONFIG | PARTIAL |
| CODE-UI-03 | Keep `PanelSettings.asset` on Scale With Screen Size | TEST | CURRENT |
| CODE-UI-04 | Show, hide, look up and wire the EventSystem through `UiElements` | TEST | PARTIAL |
| CODE-UI-05 | Keep every UXML file well-formed XML | TEST | CURRENT |
| CODE-UI-06 | Give every USS class the code emits a rule | TEST | PARTIAL |
| CODE-UI-07 | Drive menu motion only through `MenuJuice`, gated by Reduced Motion read live | TEST | CURRENT |
| CODE-UI-08 | Give colours and timings of gameplay meaning one named home | TEST | PARTIAL |
| CODE-UI-09 | Draw the local player's status stack above everyone else's | NONE | CURRENT |
| CODE-UI-10 | Make anything touchable UI Toolkit; IMGUI is read-only telemetry | REVIEW | CURRENT |
| CODE-UI-11 | Size for Android first; Performance Mode defaults on for phones | TEST | PARTIAL |
| CODE-UI-12 | Clear `background-color` on a runtime Button that wears a sprite | REVIEW | CURRENT |
| CODE-AUD-01 | Play every sound through `IAudioService` by typed `SfxId` | NONE | CURRENT |
| CODE-AUD-02 | Report a missing clip once; stay silent only where silence is designed | TEST | PARTIAL |
| CODE-AUD-03 | Give every `SfxId` its own registry slot and every clip one cue | TEST | CURRENT |
| CODE-AUD-04 | Append new `SfxId` values; never reorder | NONE | CURRENT |
| CODE-AUD-05 | Treat an empty registry slot with no authored clip as expected | REVIEW | CURRENT |
| CODE-AUD-06 | Let only the human who drives a chicken hear its own sounds | TEST | CURRENT |
| CODE-AUD-07 | Keep audio local; no voice or free-text chat | REVIEW | CURRENT |
| CODE-ASSET-01 | Use URP materials only, and end every fallback chain in a project material | TEST | PARTIAL |
| CODE-ASSET-02 | Look twice at render-pipeline and ProjectSettings diffs; run the suite after them | TEST | PARTIAL |
| CODE-ASSET-03 | Build runtime ground and floors from a thin Cube, never a Plane | NONE | CURRENT |
| CODE-ASSET-04 | Destroy only what you created | TEST | PARTIAL |
| CODE-ASSET-05 | Move or recreate a script only together with its `.meta`; commit an FBX with its `.meta` | TEST | PARTIAL |
| CODE-ASSET-06 | Check the Doppelganger variant before restructuring `Chicken.prefab` | TEST | CURRENT |
| CODE-ASSET-07 | Import chicken FBX rigs as Generic, never Humanoid | TEST | CURRENT |
| CODE-ASSET-08 | Do not "fix" the skeletal-animation invariants | TEST | PARTIAL |
| CODE-ASSET-09 | Keep build scenes in order: Bootstrap, Game, Map, Ability Lab | TEST | CURRENT |
| CODE-ASSET-10 | Keep chickens on layer 8 and interactables on layer 9, on every collider | TEST | CURRENT |
| CODE-ASSET-11 | Keep `Chicken.prefab` complete, without duplicate behaviours, and with its listeners | TEST | CURRENT |
| CODE-ASSET-12 | Share line and FX materials; never instance one per chicken | TEST | CURRENT |
| CODE-ASSET-13 | Add no serialized field to a shipped palette asset without re-authoring it | REVIEW | CURRENT |
| CODE-ASSET-14 | Attach no carried prop under a chicken root | TEST | CURRENT |
| CODE-ASSET-15 | Re-check scene-baked data after a roster or slot change | TEST | PARTIAL |
| CODE-ASSET-16 | Rescale the arena only with `MapPropsRescaler`, after every fence post agrees | CONFIG | CURRENT |
| CODE-ASSET-17 | Never hand-author sprite `.meta` YAML; never add sprite generation back | REVIEW | CURRENT |
| CODE-ASSET-18 | Generate icons in the locked style and pass the readability sheet first | REVIEW | CURRENT |
| CODE-TEST-01 | Run the EditMode suite green before every commit batch, build and playtest; know what it cannot prove | NONE | CURRENT |
| CODE-TEST-02 | Accept a claim only when a before/after state sample shows it | CONFIG | CURRENT |
| CODE-TEST-03 | Close a bug only with a check that fails on the old build | REVIEW | CURRENT |
| CODE-TEST-04 | Leave zero untriaged findings | CONFIG | CURRENT |
| CODE-TEST-05 | Run the mode x round matrix; name every mode you did not run | CONFIG | PARTIAL |
| CODE-TEST-06 | Trust a green session only for the paths it exercised; force combat with four clients and `-cw_perk` | NONE | CURRENT |
| CODE-TEST-07 | Make round N start equal round 1 start, on every peer | CONFIG | PARTIAL |
| CODE-TEST-08 | Prove the harness first; treat suspiciously clean numbers as bugs; say what you did not check | REVIEW | CURRENT |
| CODE-TEST-09 | Build every platform from one clean commit | CONFIG | PARTIAL |
| CODE-TEST-10 | After adding an RPC with authority checks, run `shared4` and count rejections | CONFIG | PARTIAL |
| CODE-TEST-11 | Test on PC EXE clients unless the owner reconnects a phone; never on emulators | REVIEW | CURRENT |
| CODE-TEST-12 | Read a source guard over the whole class, never weaken a guard for a refactor | REVIEW | PARTIAL |
| CODE-TEST-13 | Make every scan prove it saw something and self-test its pattern | REVIEW | CURRENT |
| CODE-TEST-14 | Populate and clean up test objects the standard way | REVIEW | CURRENT |
| CODE-TEST-15 | Confirm the compile and the result file before trusting a suite run | NONE | CURRENT |
| CODE-TEST-16 | Keep diagnostic log-line shapes that `merge.ps1` parses stable | TEST | CURRENT |
| CODE-TEST-17 | Excuse in a probe only what the game itself refuses, and count the excuses | TEST | PARTIAL |
| CODE-TEST-18 | Assert features and relationships, not bounding boxes; trust the render over the metric | REVIEW | CURRENT |
| CODE-TEST-19 | Verify live what EditMode cannot prove | REVIEW | CURRENT |
| CODE-BUILD-01 | Drive the Editor through the `ai-game-developer` MCP; never Unity's own MCP | REVIEW | CURRENT |
| CODE-BUILD-02 | Run the full suite through `TestRunnerApi` or in batch mode; judge render tests by a batch run | REVIEW | CURRENT |
| CODE-BUILD-03 | Use one Unity process per project at a time | CONFIG | CURRENT |
| CODE-BUILD-04 | Treat a "hung" Editor with a dead bridge as a wedged test runner first | REVIEW | CURRENT |
| CODE-BUILD-05 | After an MCP test run, reopen Bootstrap before any Play check | REVIEW | CURRENT |
| CODE-BUILD-06 | Exit Play mode before any capture; capture at time scale 1 | REVIEW | CURRENT |
| CODE-BUILD-07 | Build Android as IL2CPP, ARM64 only, minSdk 24, APK | CONFIG | CURRENT |
| CODE-BUILD-08 | Sign Play Store bundles from environment variables; never commit a key | TEST | CURRENT |
| CODE-BUILD-09 | Keep bundle identifiers, branding and the cloud project id pinned | TEST | CURRENT |
| CODE-BUILD-10 | Keep iOS export off by default; Build All never builds iOS | CONFIG | CURRENT |
| CODE-BUILD-11 | Use the phone on USB or Windows builds; never emulators; one adb | REVIEW | CURRENT |
| CODE-BUILD-12 | Keep production builds free of dev tools; dev-tools builds for sessions | REVIEW | CURRENT |
| CODE-BUILD-13 | Back up dirty URP and ProjectSettings files before a batch or Android build, and restore build side effects | REVIEW | CURRENT |
| CODE-BUILD-14 | Delete `Builds/Windows` before a release build | REVIEW | CURRENT |
| CODE-BUILD-15 | Bump the four Unity MCP packages together and match the DEPS define | REVIEW | CURRENT |
| CODE-BUILD-16 | Test sessions run at log level Debug, never Warn | REVIEW | CURRENT |
| CODE-BUILD-17 | After a hard crash, verify state and resume; do not chase hardware | REVIEW | CURRENT |
| CODE-BUILD-18 | Check ComfyUI's port before diagnosing a generation failure | REVIEW | CURRENT |
| CODE-PROC-01 | Orient before changing anything non-trivial | REVIEW | CURRENT |
| CODE-PROC-02 | Route non-trivial work through `code-architect`; let specialists implement; `librarian` looks up before and maintains the docs after | REVIEW | CURRENT |
| CODE-PROC-03 | Never pass `model: "fable"` to any agent unless the owner names it | REVIEW | CURRENT |
| CODE-PROC-04 | Plan with opus, implement with sonnet | REVIEW | CURRENT |
| CODE-PROC-05 | Do not use cravify or Antigravity unless the owner asks for it by name | REVIEW | CURRENT |
| CODE-PROC-06 | Run slow batches from the main session, one foreground specialist at a time | REVIEW | CURRENT |
| CODE-PROC-07 | Build every agent brief with the prompt-master skill | REVIEW | CURRENT |
| CODE-PROC-08 | Give an implementation agent a complete, verifiable brief | REVIEW | CURRENT |
| CODE-PROC-09 | Review risky batches independently, pinned to a commit range | REVIEW | CURRENT |
| CODE-PROC-10 | Verify a reviewer's finding against intent before accepting it | REVIEW | CURRENT |
| CODE-PROC-11 | Commit one concern per commit, with the why in the body | REVIEW | CURRENT |
| CODE-PROC-12 | Update docs/STATE.md after every change, and update docs in place | REVIEW | CURRENT |
| CODE-PROC-13 | Republish the compendium whenever design docs or gameplay `.asset` values change | NONE | CURRENT |
| CODE-PROC-14 | Branch fixes from `main` and features from `develop` | REVIEW | CURRENT |
| CODE-PROC-15 | Agree the design before the plan; present designs and audits as interactive pages | REVIEW | CURRENT |
| CODE-PROC-16 | Treat recorded owner decisions as binding | REVIEW | CURRENT |
| CODE-PROC-17 | Fix every audit finding, before and after the alpha | REVIEW | CURRENT |
| CODE-PROC-18 | Park small findings in the backlog | REVIEW | CURRENT |
| CODE-PROC-19 | Keep secrets and junk out of git | TEST | PARTIAL |
| CODE-PROC-20 | Do not edit `.claude/CLAUDE.md` from an agent; propose the line instead | CONFIG | CURRENT |
| CODE-PROC-21 | On an unattended run, keep going without asking | REVIEW | CURRENT |
| CODE-DESIGN-01 | Solve stats against the SCT axiom; never hand-tune them | TEST | CURRENT |
| CODE-DESIGN-02 | Keep the shipped SCT tolerance at ±0.5 s; never widen it | TEST | PARTIAL |
| CODE-DESIGN-03 | Trust `MatchConfig.asset` over any document for target and time | TEST | CURRENT |
| CODE-DESIGN-04 | Leave numbers alone until the owner's test | NONE | CURRENT |
| CODE-DESIGN-05 | Keep it easy and forgiving: it is a children's game | TEST | PARTIAL |
| CODE-DESIGN-06 | Keep cooldowns in the bands: low 1-3 s, medium 4-6 s, high 7-8 s | TEST | CURRENT |
| CODE-DESIGN-07 | Make abilities targeted: they cannot be dodged, only counterplayed | REVIEW | CURRENT |
| CODE-DESIGN-08 | Pressing previews the range, releasing casts; holding never freezes the chicken | TEST | PARTIAL |
| CODE-DESIGN-09 | Let no ability cross a base wall; never let a shielded chicken be killed | TEST | PARTIAL |
| CODE-DESIGN-10 | Steal the minimum of the ability's amount, the thief's free room and the victim's cargo | TEST | CURRENT |
| CODE-DESIGN-11 | Decide a jump by its length, not obstacle height; Speedy never jumps | TEST | PARTIAL |
| CODE-DESIGN-12 | Keep chickens inside the boundary in code, not by trusting colliders | TEST | PARTIAL |
| CODE-DESIGN-13 | Keep fixed kits behind `LoadoutMode`; keep the picking code | TEST | CURRENT |
| CODE-DESIGN-14 | One icon means one effect: Slow and Slow Area are separate jobs | TEST | CURRENT |
| CODE-DESIGN-15 | Respect the locked 3D-environment and feedback decisions | REVIEW | CURRENT |
| CODE-DESIGN-16 | Hold 60 fps on Windows and a stable 30 fps on mid-range Android | NONE | CURRENT |
| CODE-DESIGN-17 | Ship the dev tools in builds behind Developer Mode, off by default | TEST | CURRENT |
| CODE-DESIGN-18 | Keep the retired ability classes until alpha feedback | REVIEW | CURRENT |
| CODE-FOOT-01 | When "the chicken doesn't move", check the camera and the stats first | REVIEW | CURRENT |
| CODE-FOOT-02 | Write C#, Python and paths with the Write tool, not shell heredocs | REVIEW | CURRENT |
| CODE-FOOT-03 | Give every PC client its own `-cw_profile`; restart Android with `am start -S` | REVIEW | CURRENT |
| CODE-FOOT-04 | Expect a black Game-view capture when the Editor is unfocused | REVIEW | CURRENT |
| CODE-FOOT-05 | Remember that tests and dev builds write PlayerPrefs and the registry | REVIEW | CURRENT |
| CODE-FOOT-06 | Capture short visual windows with ffmpeg, not homemade burst scripts | REVIEW | CURRENT |
| CODE-FOOT-07 | Delete a stray legacy EventSystem; let `UiElements` create the right one | REVIEW | CURRENT |

## 3. Rules

## 3.1 STYLE: naming, placement, language-level conventions

### CODE-STYLE-01 — Name every ScriptableObject type with the `SO` suffix and keep its assets in `Assets/_Game/Data/`
- Rule: ScriptableObjects use the `SO` suffix (`ChickenStatsSO`, `AbilityBaseSO`, `MatchConfigSO`); their `.asset` files live in `Assets/_Game/Data/`.
- Why: one place to find tuned data; not stated beyond that.
- Enforced by: TEST `ProjectConfigTests.EveryProjectScriptableObjectType_UsesTheSoSuffix` and `ProjectConfigTests.EveryProjectScriptableObjectAsset_LivesInTheDataFolder` (Assets/_Game/Scripts/Editor/Tests/ProjectConfigTests.cs); rest CONVENTION
- Audit check: the suffix test only sees SO types that already have an asset under Data. List every SO type: `git grep -n -E "class \w+ *: *(ScriptableObject|\w+SO)\b" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` and flag class names not ending in `SO`.
- Source: docs/CONVENTIONS.md:12, .claude/CLAUDE.md:74
- Status: PARTIAL (a SO type with no asset in Data, or created only with `CreateInstance`, is never suffix-checked; gap G-08)

### CODE-STYLE-02 — Keep `Assets/_Game/Resources/` for what must load by name
- Rule: Unity loads anything in `Resources/` on demand; "Don't dump general assets here": it bloats the build.
- Why: everything under `Resources/` ships in every build.
- Enforced by: CONVENTION (review only)
- Audit check: `git ls-files Assets/_Game/Resources | grep -v '\.meta$'`; every entry must be loaded by name at runtime (`Resources.Load`). At HEAD it holds `ProjectContext.prefab`, `Text/UiText.csv` and four fonts.
- Source: docs/CONVENTIONS.md:510
- Status: CURRENT (the doc line names only `ProjectContext.prefab`; see Appendix A)

### CODE-STYLE-03 — Keep the repo LF-only
- Rule: The repo is LF-only, enforced by `.gitattributes`, and `.editorconfig` keeps IDEs from re-introducing CRLF. If a file shows as entirely changed, run `git add --renormalize .` and commit the normalization pass on its own.
- Why: Unity on Windows and IDEs sometimes save CRLF, producing whole-file diffs.
- Enforced by: CONFIG (`.gitattributes`, `.editorconfig`)
- Source: docs/CONVENTIONS.md:389-397, docs/CONVENTIONS.md:539, .gitattributes:1-5
- Status: CURRENT

### CODE-STYLE-04 — Alias `LogLevel` in files that import both `CluckWars.Logging` and `Fusion`
- Rule: Add `using LogLevel = CluckWars.Logging.LogLevel;` at the top of any file that uses both namespaces and names `LogLevel` unqualified.
- Why: `Fusion.LogLevel` collides (CS0104), hit in `8fca391`.
- Enforced by: COMPILER (CS0104 ambiguous reference)
- Source: docs/CONVENTIONS.md:353-361, .claude/CLAUDE.md:69
- Status: CURRENT

### CODE-STYLE-05 — Alias NUnit's `Assert` in any test file that imports `Fusion`
- Rule: An EditMode test file that imports `Fusion` must declare `using Assert = NUnit.Framework.Assert;`.
- Why: `Fusion.Assert` makes every assertion CS0104 (hit building `ProjectConfigTests.cs`, 2026-07-22).
- Enforced by: COMPILER (CS0104)
- Source: docs/CONVENTIONS.md:363-373, docs/TESTING.md:235-237
- Status: CURRENT

### CODE-STYLE-06 — Keep `ChickenClass : byte`; never use a `-1` sentinel
- Rule: `enum ChickenClass : byte` is the backing type for Fusion serialisation: "Don't change." `(ChickenClass)(-1)` throws CS0221: use a separate `bool _valid` flag instead of a sentinel.
- Why: wire size; hit in `85a96b3`.
- Enforced by: TEST `ContractsAndEnumsTests.NetworkedEnums_AreByteBacked` (Assets/_Game/Scripts/Editor/Tests/ContractsAndEnumsTests.cs); COMPILER (CS0221 for the sentinel)
- Source: docs/CONVENTIONS.md:375-379, .claude/CLAUDE.md:70
- Status: CURRENT (ordinal pins of networked enums: ARCH-NET-13)

### CODE-STYLE-07 — Use `var` or `static readonly` for platform enums and kill switches, not `const`
- Rule: `const AndroidSdkVersions x = ...` is legal but trips some C# compilers: use `var` or `static readonly` for platform / Unity enums. A kill switch is `static readonly`, not `const`, so guard readers do not hit CS0162.
- Why: compiler warnings and errors on some toolchains.
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n -E "const (Android|iOS|UnityEditor\.|BuildTarget)\w*" HEAD -- Assets/_Game/Scripts` returns nothing.
- Source: docs/CONVENTIONS.md:381-383, M:visual-feedback-systems.md:54
- Status: CURRENT

### CODE-STYLE-08 — Split an oversized MonoBehaviour only as a partial class
- Rule: A component a scene or prefab references keeps its type, name, namespace, attributes, serialized fields and public API. The only way to split one is a partial class (`Name.Concern.cs` beside it), never moving its serialized state and never adding a component to a prefab.
- Why: a moved serialized field or a renamed type silently breaks scene and prefab references.
- Enforced by: TEST `MissingScriptsTests.NoPrefab_HasAMissingScript` and `MissingScriptsTests.NoBuildScene_ReferencesAScriptThatDoesNotResolve` (catch a lost type only); rest CONVENTION
- Audit check: for a split commit, `git show --stat <sha>` touches no `.prefab`/`.unity`, and `git diff <sha>^ <sha> -- '*.cs' | grep -E "^\-.*\[SerializeField\]"` shows no removed serialized field.
- Source: docs/CONVENTIONS.md:271-273
- Status: PARTIAL (a moved or renamed serialized field loses its value without any test failing; same rule as ARCH-LAY-13)

### CODE-STYLE-09 — Do not introduce shared UXML element-name constants
- Rule: No shared element-name constants exist, on the measurement (of 159 distinct `Q("...")` names one appears in two runtime files, as two different elements). A name a test also spells out is a deliberate two-sided check; a shared constant would collapse it to one side.
- Why: the measurement and the two-sided check.
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n -E "const string \w*(Name|Element)\w* *= *\"" HEAD -- Assets/_Game/Scripts/UI Assets/_Game/Scripts/Input` should list only names shared by design.
- Source: docs/CONVENTIONS.md:269
- Status: CURRENT

### CODE-STYLE-10 — Keep code names when a display name changes
- Rule: Player-facing renames change the wording (`UiText.csv`, `DisplayName`), not the type names: `MarkKillAbilitySO` and `AssassinExecute` stay while the UI says Hunt / HUNT then POUNCE; `TurtleMode.asset` shows "Egg Shell" / EGG.
- Why: owner rulings D2 and D5 (icons thread, 2026-11-07); a type rename risks scene and asset references.
- Enforced by: CONVENTION (review only)
- Audit check: a commit that renames a `*SO` type or an ability `.asset` must cite an owner request; `git log --diff-filter=R --name-status -10` lists renames.
- Source: M:feedback-icons-decisions-2026-11-07.md:13-43; icons thread 2026-11-07
- Status: CURRENT ("rename assassin to sneaky in code" is a backlog item, not a rule: balance thread 2026-11-05)

## 3.2 TEXT: player-facing text, localization, numbers taken from data

The worked example for CODE-TEXT-06 and CODE-TEXT-10 (what is enforced, what is not, the guards owed) is section 4.

### CODE-TEXT-01 — Put every player-facing string in the wording dictionary
- Rule: Every player-facing string is a `UiKeys` constant with a row in `Assets/_Game/Resources/Text/UiText.csv` (a `key,en` table with room for language columns); the constants and the rows are a bijection (no constant without a row, no row without a constant).
- Why: owner, UI thread 2026-10-06: wording "in like a wording dictionary, to prepare to future changes or languages".
- Enforced by: TEST `UiTextTests.EveryUiKeysConstant_HasACsvRow`, `UiTextTests.EveryCsvRow_HasAUiKeysConstant`, `UiTextTests.ShippedCsv_Loads_WithNoDuplicateKeysAndNoProblems` (Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs)
- Source: Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs:161-174; UI thread 2026-10-06 (owner)
- Status: CURRENT (not written in docs/CONVENTIONS.md until this document; a constant that no code reads still passes, gap G-05)

### CODE-TEXT-02 — Name a key by its `UiKeys` constant, never a string literal
- Rule: Code passes `UiKeys.X` to `UiText.Get/Format/TryGet/HasKey`, never `"some.key"`.
- Why: a typo in a constant is a compile error; a typo in a literal is a missing-key marker on screen.
- Enforced by: TEST `UiTextTests.CodeNeverPassesAKeyLiteral_ToUiText` (scans 8 listed files plus `Abilities/Passives/*PassiveSO.cs`, UiTextTests.cs:193-207); rest CONVENTION
- Audit check: `git grep -n -E 'UiText\.(Get|Format|TryGet|HasKey)\(\s*"' HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` must return nothing.
- Source: Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs:193-219
- Status: PARTIAL (every other runtime file, including the A21 partials, is unscanned; gap G-04)

### CODE-TEXT-03 — Write no player-facing literal into UI code
- Rule: No `.text = "..."`, `new Label("...")` or `new Button("...")` with player-facing text in menu or HUD code; the text comes from `UiText`.
- Why: a literal escapes translation and the wording review.
- Enforced by: TEST `UiTextTests.MenuController_HasNoPlayerFacingStringLiterals`, `UiTextTests.MatchHud_HasNoPlayerFacingStringLiterals` (single files `MenuUiController.cs`, `MatchHudController.cs`, `MatchHud.cs`, read with `File.ReadAllText`); rest CONVENTION
- Audit check: `git grep -n -E '\.text\s*=\s*\$?"[^"]|new (Label|Button)\(\s*\$?"[^"]' HEAD -- 'Assets/_Game/Scripts/*.cs' ':!Assets/_Game/Scripts/Editor'`; at b7c2616e the only hits are `AbilityLab/AbilityLabPanel.cs:448,454` (dev tool) and the glyph at `Visuals/ChickenNameplate.cs:264`.
- Source: Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs:221-247; UI thread 2026-10-06 (owner)
- Status: PARTIAL (the six `MenuUiController.*.cs` and three `MatchOverlaysController.*.cs` partials, `TouchControlsController*.cs`, `MatchStandings.cs` are not scanned for literal text; gap G-01)

### CODE-TEXT-04 — Leave UXML text empty or a `@key` reference
- Rule: A UXML `text` attribute is empty (the controller fills it) or `@key` naming an existing row, never literal wording.
- Why: same as CODE-TEXT-01, for markup.
- Enforced by: TEST `UiTextTests.MenuUxml_TextIsEitherEmptyOrAKeyReference_NeverALiteral`, `UiTextTests.MenuUxml_EveryKeyReference_ExistsInTheDictionary`, `UiTextTests.MatchOverlays_HaveNoLiteralText_AndTheirKeysExist`, `UiTextTests.ResolvedMenuUxml_ShowsNoMarkers`; rest CONVENTION
- Audit check: `git grep -c -E 'text="[^"@][^"]*"' HEAD -- 'Assets/UI/*.uxml'`; at b7c2616e: AbilityLab.uxml 25 (dev tool), TouchControls.uxml 4 (slot badges "1".."4").
- Source: Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs:25-28, Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs:343
- Status: PARTIAL (covers MainMenu, CharacterSelectClass, CharacterSelect, Lobby and MatchOverlays; MatchTopBar, MarkHud, TouchControls and AbilityLab UXML are unchecked)

### CODE-TEXT-05 — Match every template's placeholders to its `[UiArgs]` contract and every call site
- Rule: A template's `{placeholders}` equal the `[UiArgs(...)]` declared on its `UiKeys` constant; every `UiText.Format` call site supplies exactly those names; `UiText.Get` is used only for templates with no placeholder.
- Why: a missing or extra argument renders a marker or drops a number.
- Enforced by: TEST `UiTextTests.EveryTemplate_PlaceholdersMatchItsUiArgsContract`, `UiTextTests.EveryFormatCallSite_SuppliesExactlyTheArgsItsTemplateContract_Declares`, `UiTextTests.EveryPlainGetCallSite_UsesATemplateWithNoPlaceholders`
- Source: Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs:176-189
- Status: PARTIAL (the call-site parse reads the listed files only, the same list as CODE-TEXT-02)

### CODE-TEXT-06 — Fill every number in ability and perk text from the SO field it describes
- Rule: No number in player-facing ability or passive text is typed. Every number is a named placeholder (`{r}`, `{secs}`, `{pct}`, `{window}`, `{live}`, ...) filled from the field of the ability's own `.asset` (or `MatchConfig` for Spoiler's goal); the csv template holds only words. Change the asset, and the text follows.
- Why: owner, balance thread 2026-11-13: "Text should use the values in SO variables to be always updated."
- Enforced by: TEST `AbilityDescriptionTests.EveryRegisteredAbilityAndPassive_TemplatesHoldNoLiteralDigits_AndFillEveryPlaceholder`, `AbilityDescriptionTests.Templates_ContainNoHandTypedNumbers`, `AbilityDescriptionTests.EveryNumberInTheRenderedText_IsAnArgument_AndEveryArgumentIsUsed`, `AbilityDescriptionTests.EveryArgument_IsARealFieldValue_ToDisplayPrecision`, `PerkTextTests.PerkLines_CarryTheNumbersTheAssetFieldsHold`; rest NONE
- Audit check: run the checks of section 4.2 (the guards named there) and walk the gaps of section 4.3; `git grep -n -E "Description: .*[0-9]" HEAD -- 'Assets/_Game/Data/Abilities/Passives/*.asset'` lists the hand-typed passive prose.
- Proposed guard: G-02 (placeholder bound to a named field) and G-03 (passive Description rendered or pinned for every passive); section 4.
- Source: Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs:162-196, Assets/_Game/Scripts/Abilities/PassiveAbilitySO.cs:74-104; balance thread 2026-11-13 (owner); UI thread 2026-10-06 (owner); M:slow-split-2026-11-13.md
- Status: PARTIAL (worked example in section 4 lists exactly what is and is not enforced; same rule as ARCH-DATA-12)

### CODE-TEXT-07 — Keep an active ability's `.asset` `Description` equal to its rendered text
- Rule: The `Description` string stored in an active ability's `.asset` is a mirror of `DescriptionText`; when a template or a field changes, paste the rendered text into the asset.
- Why: the raw field is the fallback for an ability with no template and is read by tools; a stale copy misleads.
- Enforced by: TEST `AbilityDescriptionTests.AssetDescriptionField_IsTheRenderedText`
- Source: Assets/_Game/Scripts/Editor/Tests/AbilityDescriptionTests.cs:60-65, Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs:162-169
- Status: CURRENT (actives under `TestAssets.AbilitiesDir` only; passives: see CODE-TEXT-06 and G-03)

### CODE-TEXT-08 — Write ability text for kids: plain words, short, no jargon
- Rule: Describe every move "in the simplest way (including second cast)"; an active description is at most 140 characters and never says "cast", "target" or "cooldown"; the csv never uses "equipped", "pre-equipped" or "pre-filled" (say Starter / Perk / Moves).
- Why: owner, balance thread 2026-11-13 (simplest wording) and 2026-11-05 ("this is still a game for children").
- Enforced by: TEST `AbilityDescriptionTests.EveryShippedActiveAbility_HasATemplate_ThatRendersCleanAndFits` (length, banned words), `UiTextTests.ShippedCsv_UsesPerkStarterMovesVocabulary_NeverEquippedOrPreFilled`; rest CONVENTION
- Audit check: for each `ability.*.desc` row whose ability has a second press (Hunt, Egg Shell), read that the row says "press again" (`UiText.csv:67`, `UiText.csv:80`).
- Source: Assets/_Game/Scripts/Editor/Tests/AbilityDescriptionTests.cs:42-58, Assets/_Game/Scripts/Editor/Tests/UiTextTests.cs:98-110; balance thread 2026-11-13 and 2026-11-05 (owner)
- Status: PARTIAL ("simplest words" and "mention the second press" are review-only; the 140 cap was raised from 110 on 2026-11-13 and the detail strip fit is unverified, BL-159)

### CODE-TEXT-09 — Make a missing key or argument visible and report it once
- Rule: A missing template, key or argument renders a visible marker (`#goal#`) and is reported once through `UiText.Problems`; an ability with no template falls back to its raw `Description` and says so once.
- Why: a blank or silently dropped number hides the bug on device.
- Enforced by: TEST `PerkTextTests.SpoilerDetail_WithoutAMatchConfig_IsLoudNotBlank`, `AbilityDescriptionTests.AbilityWithNoTemplate_FallsBackToItsDescription_AndSaysSoOnce`
- Source: Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs:176-189, Assets/_Game/Scripts/Editor/Tests/PerkTextTests.cs:205-216
- Status: CURRENT (written only in code comments and tests before this document)

### CODE-TEXT-10 — Read every number a player sees from the data that drives the behaviour
- Rule: Any number shown to a player (HUD, cards, settings, banners, menus) is read at runtime from the asset, config or live state that drives the behaviour, never typed into wording or code; the generalisation of CODE-TEXT-06.
- Why: the owner's 2026-11-13 ruling and his concern that unguarded rules rot ("we have no way to ensure that is done in the future", audit thread 2026-11-13).
- Enforced by: TEST `LobbyMatchSettingsTests.MatchSettingsText_RendersTheLiveConfig_InTheCardsFormat` (MATCH SETTINGS card) and the CODE-TEXT-06 tests; rest NONE
- Audit check: run `python -I` over `UiText.csv` stripping `{...}` and listing values with a digit (section 4.3, item 3); `git grep -n -E 'UiText\.Format\([^;]*\("[a-z]+", *[0-9]' HEAD -- Assets/_Game/Scripts` for literal arguments; read `docs/site/*.html` numbers against the `.asset` files.
- Proposed guard: G-06 (csv-wide digit rule with an allow-list, roster numbers templated from `MatchConfigSO.MaxPlayers` / `ChickenClasses.SoloBotRoster`) and G-07 (no numeric literal as a `UiText.Format` argument).
- Source: Assets/_Game/Resources/Text/UiText.csv:3, Assets/_Game/Resources/Text/UiText.csv:4, Assets/_Game/Resources/Text/UiText.csv:146, Assets/_Game/Resources/Text/UiText.csv:148; audit thread 2026-11-13 (owner)
- Status: PARTIAL (inventory of every surface in section 4.4)

### CODE-TEXT-11 — Leave class taglines as flavour; do not "fix" them into stats
- Rule: Class taglines such as Fatty's "Slowest waddle" are the spirit, not literal stats; keep them even when not exactly true.
- Why: owner, balance thread 2026-11-13: "Keep it, it might not be true always, but it is the spirit."
- Enforced by: CONVENTION (review only)
- Audit check: a diff that rewrites a `class.*` tagline row in `UiText.csv` must cite an owner request.
- Source: M:slow-split-2026-11-13.md; balance thread 2026-11-13 (owner)
- Status: CURRENT

### CODE-TEXT-12 — Keep the generic ability names until the owner picks new ones
- Rule: Display names and labels stay generic (Steal/ROB, Stun/STUN, Shove/PUSH, Slow/SLOW, Speed/FAST, Jump/JUMP, Hide/HIDE, Peck/PECK; Egg Shell/EGG; Hunt/HUNT); do not propose new ability names unprompted (the old names are saved for later).
- Why: owner rulings D1/D2/D5, icons thread 2026-11-07, and the 2026-11-05 merge to generic jobs.
- Enforced by: CONVENTION (review only)
- Audit check: `git log -p -S DisplayName -- 'Assets/_Game/Data/Abilities/*.asset'` shows no rename without an owner request.
- Source: M:feedback-icons-decisions-2026-11-07.md:13-22, M:ability-merge-2026-11-05.md; icons thread 2026-11-07, balance thread 2026-11-05
- Status: CURRENT

## 3.3 UI: UI Toolkit, UXML/USS, verification by screenshot

### CODE-UI-01 — Build menu and screen UI in UI Toolkit; never procedurally
- Rule: All menu/screen UI is UI Toolkit: layout in `Assets/UI/*.uxml`, styling in USS, the controller binds data and navigation only ("no layout code in C#"). Never build menu UI procedurally in C# (RectTransforms / LayoutGroups from code) and never generate UGUI prefabs via editor scripts.
- Why: both approaches were tried and failed (settled 2026-06-11, "do not re-litigate"); UXML/USS is text and verifiable by screenshot.
- Enforced by: CONVENTION (review only); the literal-text tests of CODE-TEXT-03/04 touch only wording
- Audit check: `git grep -n -E "typeof\(Canvas\)|AddComponent<(Canvas|RectTransform|LayoutGroup|HorizontalLayoutGroup|VerticalLayoutGroup)>" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`; at b7c2616e the only hit is the residual `MatchHud` canvas (Assets/_Game/Scripts/UI/MatchHud.cs:164-165), see open question Q1.
- Source: docs/CONVENTIONS.md:514-522, .claude/CLAUDE.md:73, M:ui-toolkit-decision.md:10-14, M:ui-rebuild-2026-07.md:15
- Status: CURRENT for menus; in-game HUD scope is Q1 (the CONVENTIONS HUD bullet is stale, Appendix A)

### CODE-UI-02 — Do not call UI work done without a screenshot
- Rule: After any UXML/USS change capture the Game view in play mode (`ai-game-developer` `screenshot-game-view` with the Editor open) and look at it: "UI work without a screenshot check is not done." In a test session every `scope=hud` `ui offenders=N` line with N > 0 is a finding that fails `verify.ps1`.
- Why: text-based UI is verifiable only if someone looks; owner, UI thread 2026-09-18: "Dont think your testing was good enough".
- Enforced by: CONFIG `tools/test-session/verify.ps1` (layout probe in sessions); rest CONVENTION
- Audit check: a UI commit's report links or names the screenshot it was judged by; for a session, `grep "ui offenders=" merged report` shows only 0.
- Source: docs/CONVENTIONS.md:526-528, docs/TESTING.md:82-84; UI thread 2026-09-18 (owner)
- Status: PARTIAL (the screenshot step is review-only; Game view captures go black when the Editor is unfocused, CODE-FOOT-04)

### CODE-UI-03 — Keep `PanelSettings.asset` on Scale With Screen Size
- Rule: `Assets/Resources/PanelSettings.asset` must stay `ScaleWithScreenSize` (`m_ScaleMode: 2`).
- Why: `ConstantPhysicalSize` re-broke mobile scaling once already (2026-06-01).
- Enforced by: TEST `Phase8ChunkDRivalArrowTests.PanelSize_MatchesThePanelSettingsAsset` (Assets/_Game/Scripts/Editor/Tests/Phase8ChunkDRivalArrowTests.cs:144-148, asserts `PanelScaleMode.ScaleWithScreenSize` as part of an arrow-layout test)
- Source: docs/CONVENTIONS.md:529-530
- Status: CURRENT (enforced incidentally; a rename of that test would lose it)

### CODE-UI-04 — Show, hide, look up and wire the EventSystem through `UiElements`
- Rule: `UiElements.SetShown(element, shown)` instead of a local copy or an inline `style.display = cond ? Flex : None`; `UiElements.EnsureEventSystem()` in `Awake`; `UiElements.Bind` / `Query` instead of a bare `root.Q<T>(name)`, so a renamed UXML element is reported once per name through `ILogService`.
- Why: a bare `Q` skips a missing element in silence.
- Enforced by: TEST `SceneObjectAccessTests.FindCalls_ExistOnlyInTheListedBootstrapSites_AndTheDevTools` (pins the one `EnsureEventSystem` find); rest NONE
- Audit check: `git grep -n -E "style\.display\s*=|\.Q<\w+>\(" HEAD -- Assets/_Game/Scripts/UI Assets/_Game/Scripts/Input Assets/_Game/Scripts/Visuals ':!Assets/_Game/Scripts/UI/UiElements.cs'`; five `style.display` comparisons in `MenuUiController` are left on purpose (they compare the `StyleEnum`).
- Proposed guard: G-11 (same rule as ARCH-ERR-08), a RuntimeFiles scan of `UI/`, `Input/`, `Visuals/` for `style.display\s*=` and `.Q<` outside `UiElements.cs`, with a counted allow-list like `SceneObjectAccessTests.AllowedFinds`.
- Source: docs/CONVENTIONS.md:265-267
- Status: PARTIAL

### CODE-UI-05 — Keep every UXML file well-formed XML
- Rule: Every `.uxml` must parse as XML (for example no `--` inside a comment).
- Why: a malformed file fails to import and the screen renders empty.
- Enforced by: TEST `UxmlWellFormedTests.EveryUxmlFile_IsWellFormedXml`
- Source: Assets/_Game/Scripts/Editor/Tests/UxmlWellFormedTests.cs:17
- Status: CURRENT (implicit rule, written down here)

### CODE-UI-06 — Give every USS class the code emits a rule
- Rule: A USS class name that C# adds to an element must have a rule in the stylesheet that element uses.
- Why: a class with no rule silently renders unstyled.
- Enforced by: TEST `HudFeedbackStyleTests.EveryGlyphClassHudFeedbackStyleUses_HasARuleInTouchControlsUss`; rest CONVENTION
- Audit check: for each `EnableInClassList("x"` / `AddToClassList("x"` in a touched file, `git grep -n "\.x" HEAD -- 'Assets/UI/**/*.uss'` finds a rule.
- Source: Assets/_Game/Scripts/Editor/Tests/HudFeedbackStyleTests.cs:327
- Status: PARTIAL (one emitter covered)

### CODE-UI-07 — Drive menu motion only through `MenuJuice`, gated by Reduced Motion read live
- Rule: Menu animation goes through `MenuJuice`, and every entry point checks the Reduced Motion gate; effects read the preference at runtime, never latch it in `Awake`/`Start`.
- Why: a latched or second animation path ignores the player's accessibility setting.
- Enforced by: TEST `MenuJuiceGuardTests.EveryMenuJuiceEntryPoint_ChecksTheReducedMotionGate`, `MenuJuiceGuardTests.UiCode_DrivesMotionOnlyThroughMenuJuice`, `ReducedMotionCoverageTests.NoEffectDecidesThePreferenceInAwakeOrStart`, `ReducedMotionCoverageTests.EffectFiles_ReadThePreferenceAtRuntime`
- Source: Assets/_Game/Scripts/Editor/Tests/MenuJuiceGuardTests.cs:24, Assets/_Game/Scripts/Editor/Tests/ReducedMotionCoverageTests.cs:111-121
- Status: CURRENT (implicit rule, written down here)

### CODE-UI-08 — Give colours and timings of gameplay meaning one named home
- Rule: A colour that carries gameplay meaning (carry level, health, a status, a removed chicken) and a juice timing is a named constant in `FeedbackTuning`, used by every consumer; player colours have one definition (`Gameplay/PlayerColors.cs`, `PlayerPalette` forwards). A Gameplay file cannot name `FeedbackTuning`: its colour stays local with a comment. Owner D3 (2026-11-07): per-state ability accent colours live in a config ScriptableObject so they are easy to change.
- Why: copies drift; a serialized field added to a shipped palette asset reads black until re-authored (CODE-ASSET-13).
- Enforced by: TEST `PlayerPaletteTests.NoOtherFile_DefinesItsOwnCopyOfThePlayerColours`, `GameplayPresentationBoundaryTests.TheTwoSimulationConstants_LiveInGameplay_AndFeedbackTuningIsTheSameValue`, `FeedbackPaletteTests.AccentKey_KeepsItsSerializedNumbers_AndLivesWithTheAbilities`; rest CONVENTION
- Audit check: `git grep -n -E "new Color(32)?\(" HEAD -- Assets/_Game/Scripts/Visuals Assets/_Game/Scripts/UI Assets/_Game/Scripts/Input` outside `FeedbackTuning.cs` and palette files; each hit is a texture pixel, a prefab default or an alpha rebuild, or a finding.
- Source: docs/CONVENTIONS.md:259-263, M:feedback-icons-decisions-2026-11-07.md:15; icons thread 2026-11-07 (owner)
- Status: PARTIAL (only player colours and two aliases are guarded; code-constant vs palette-SO split is open question Q3; same rule as ARCH-LAY-15)

### CODE-UI-09 — Draw the local player's status stack above everyone else's
- Rule: The local player's badges and stack are always drawn on top of other chickens' (E9c).
- Why: owner, icons thread 2026-11-10: "Always draw player above others".
- Enforced by: NONE found (no test name matches; not verified further)
- Audit check: read the sort-order code of the badge row and world bars for a local-player branch; confirm in a 4-client screenshot with overlapping chickens.
- Proposed guard: a pure rule (`sortKey(isLocal, depth)`) tested so the local entry always sorts last, plus a source guard that the badge row uses it.
- Source: M:feedback-icons-decisions-2026-11-07.md; icons thread 2026-11-10 (owner)
- Status: CURRENT (unenforced)

### CODE-UI-10 — Make anything touchable UI Toolkit; IMGUI is read-only telemetry
- Rule: IMGUI receives no input in a player build (Input System `activeInputHandler: 1`), so anything touchable is UI Toolkit; IMGUI only shows read-only telemetry. The Ability Lab drives the real input path, and press-to-cast latency is measured through `IInputProvider` (rising edge of `GetAbilityHeld(slot)`), never `Keyboard.current` or the one-shot press getters.
- Why: reading a press getter consumes the latch and swallows the cast.
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n -E "GUI\.(Button|Toggle)|GUILayout\.(Button|Toggle)" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` returns nothing.
- Source: docs/TESTING.md:326-331, docs/TESTING.md:359-362, docs/TESTING.md:374-379
- Status: CURRENT

### CODE-UI-11 — Size for Android first; Performance Mode defaults on for phones
- Rule: Android is the main target: buttons and text are sized for a phone; Performance Mode defaults ON on mobile platforms and OFF elsewhere, and a player's choice sticks.
- Why: owner, Android UI thread 2026-07-23: "buttons and text are too small for android, which is the main target".
- Enforced by: TEST `PerformanceModePreferenceTests.PerformanceMode_DefaultsToOnForMobilePlatforms_OffOtherwise_AndAChoiceSticks`; rest CONVENTION
- Audit check: capture the changed screen at phone size (`python tools\ui_capture\capture_orientation.py`, docs/TEST_STRATEGY.md:85) and check touch targets.
- Source: docs/TEST_STRATEGY.md:85; Android UI thread 2026-07-23 (owner)
- Status: PARTIAL (touch-target size is review-only)

### CODE-UI-12 — Clear `background-color` on a runtime Button that wears a sprite
- Rule: When a menu surface wears an exported sprite, it must clear `background-color` to transparent.
- Why: Unity's default runtime-Button grey shows through the sprite's transparent margin.
- Enforced by: CONVENTION (review only)
- Audit check: in USS, every selector that sets `background-image` on a Button also sets `background-color: rgba(0,0,0,0)` (or transparent).
- Source: M:ui-rebuild-2026-07.md:25
- Status: CURRENT

## 3.4 AUD: audio

### CODE-AUD-01 — Play every sound through `IAudioService` by typed `SfxId`
- Rule: Code plays sound only through `IAudioService.PlaySFX(SfxId, volume, pitch)` (or `TryPlaySFX` where silence is the designed fallback); a clip is resolved by `AudioRegistrySO.Resolve(SfxId)`; nothing outside the audio service calls `AudioSource.PlayOneShot` / `PlayClipAtPoint`, and no event payload carries an `AudioClip`.
- Why: the service is the swappable boundary (FMOD later) and the only place a missing clip is reported; ids let a language pack swap voice without touching gameplay (owner D2a, audit thread 2026-11-12).
- Enforced by: NONE for the service boundary (at b7c2616e `PlayOneShot` appears only in `Assets/_Game/Scripts/Audio/UnityAudioService.cs:116`, by convention); the `AudioClip`-in-payload half is ARCH-EVT-04
- Audit check: `git grep -n -E "PlayOneShot|PlayClipAtPoint|AudioSource\.Play\(" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Audio' ':!Assets/_Game/Scripts/Editor'` returns nothing.
- Proposed guard: G-09b, a `RuntimeSourceGuardTests.RuntimeFiles()` scan banning `PlayOneShot|PlayClipAtPoint|\.Play\(\)` on an `AudioSource` outside `Scripts/Audio/`, with a regex self-test.
- Source: docs/CONVENTIONS.md:11, docs/CONVENTIONS.md:128, docs/CONVENTIONS.md:165, .claude/CLAUDE.md:65
- Status: CURRENT (unenforced)

### CODE-AUD-02 — Report a missing clip once; stay silent only where silence is designed
- Rule: `UnityAudioService.PlaySFX(null)` plays nothing and Warns once per call site (source `Audio`); a typed `SfxId` with no clip Warns once, by name. Own sounds use `PlaySFX(SfxId)`; do not pass clips through null guards or `AudioRegistrySO.TryPlay` unless silence is the designed behaviour of an optional slot, and say so at the call site.
- Why: audit A08: a missing clip is a wiring bug, invisible on device.
- Enforced by: TEST `MissingSfxTests.EverySoundId_WithNoClip_IsReportedOnce_ByName`; rest CONVENTION
- Audit check: `git grep -n "TryPlaySFX(" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` (7 call sites at b7c2616e); each needs a comment saying why silence is designed.
- Source: docs/CONVENTIONS.md:72, docs/CONVENTIONS.md:165 (same rule as ARCH-ERR-07)
- Status: PARTIAL (the "say why" half is review-only; docs/STATE.md:61 records three call sites moved from clips to `SfxId` / `TryPlaySFX`)

### CODE-AUD-03 — Give every `SfxId` its own registry slot and every clip one cue
- Rule: Every `SfxId` resolves to its own named slot in `AudioRegistry.asset` (no sharing) and no clip is wired to two different cues.
- Why: copy-paste slips play the wrong sound.
- Enforced by: TEST `MissingSfxTests.EverySoundId_ResolvesToItsOwnRegistrySlot`, `DataIntegrityTests.AudioRegistry_Exists_AndNoClipIsWiredToTwoDifferentCues`
- Source: Assets/_Game/Scripts/Editor/Tests/MissingSfxTests.cs:91, Assets/_Game/Scripts/Editor/Tests/DataIntegrityTests.cs:625
- Status: CURRENT (implicit rule, written down here)

### CODE-AUD-04 — Append new `SfxId` values; never reorder
- Rule: New `SfxId` values (`enum SfxId : byte`, Assets/_Game/Scripts/Audio/SfxId.cs:8) are appended.
- Why: not stated; any serialized or networked use of the id would be renumbered.
- Enforced by: NONE (no ordinal pin for `SfxId`)
- Audit check: `git log -p -- Assets/_Game/Scripts/Audio/SfxId.cs` shows only additions at the end of the enum.
- Proposed guard: add `SfxId` to the pinned-enum table (`EffectWindowTests` `Pin<T>` pattern), fails on a renumber or an unpinned new value.
- Source: docs/STATE.md:61
- Status: CURRENT (stated only as practice: docs/STATE.md:61 records new ids as "appended"; no repo line gives the reason)

### CODE-AUD-05 — Treat an empty registry slot with no authored clip as expected
- Rule: Slots that are empty because no clip exists (`HitConfirm`, nine others; `HuntLive` deliberately optional) are not wiring bugs: do not "wire" them. `AudioRegistry.asset` is touched only when a clip is authored.
- Why: BL-147: nothing to wire.
- Enforced by: CONVENTION (review only)
- Audit check: a diff to `Assets/_Game/Data/AudioRegistry.asset` must add a new clip file in the same commit.
- Source: docs/STATE.md:27, docs/STATE.md:31
- Status: CURRENT

### CODE-AUD-06 — Let only the human who drives a chicken hear its own sounds
- Rule: A chicken's own sounds (cast, deposit, hit-confirm, whiff, cancel, fizzle, cargo full, expire) follow `OwnSoundRules`: only the human driving that chicken hears them, never a bot's, a rival's or a decoy's; no sound is played from `FixedUpdateNetwork`.
- Why: a sound played from gameplay plays on the state-authority peer, which for bots is the master: the bug this rule replaced.
- Enforced by: TEST `CastPresentationTests.OwnSounds_AreHeardOnlyByTheHumanWhoDrivesTheChicken`, `CastPresentationTests.TheDepositSound_IsRaisedAsAnEvent_NotPlayedFromTheSettleRpc_SourceGuard`, `CastPresentationTests.Gameplay_NoLongerPlaysTheCastAnimationOrSound_SourceGuard`
- Source: docs/CONVENTIONS.md:163, Assets/_Game/Scripts/Visuals/OwnSoundRules.cs (same rule as ARCH-EVT-10)
- Status: CURRENT

### CODE-AUD-07 — Keep audio local; no voice or free-text chat
- Rule: Sounds are triggered by each client from observed state; "No voice chat, no chat system."
- Why: TDD design; kids' game.
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n -i -E "vivox|voicechat|ChatMessage" HEAD -- Assets/_Game/Scripts` returns nothing.
- Source: docs/TDD.md:389-391
- Status: CURRENT (the TDD's `PlaySFX(AudioClip, Vector3)` sketch at docs/TDD.md:371-378 is superseded by `SfxId`; Appendix A)

## 3.5 ASSET: assets, scenes, prefabs, GUIDs, URP, rigs

### CODE-ASSET-01 — Use URP materials only, and end every fallback chain in a project material
- Rule: URP materials only, no Built-in RP shaders (Lit / Unlit / URP Shader Graph). A material fallback chain must bottom out in a real project material (such as `_groundMaterial`), never the default of `GameObject.CreatePrimitive`.
- Why: the primitive default uses the Built-in Standard shader, which URP renders magenta.
- Enforced by: TEST `ProjectConfigTests.EveryGameMaterial_UsesAUrpShader` (every `.mat` under `Assets/_Game`); rest CONVENTION
- Audit check: `git grep -n "CreatePrimitive" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`; each hit assigns a project material before the object renders.
- Source: docs/CONVENTIONS.md:16, docs/CONVENTIONS.md:420, .claude/CLAUDE.md:71
- Status: PARTIAL (runtime-built materials and primitives are review-only)

### CODE-ASSET-02 — Look twice at render-pipeline and ProjectSettings diffs; run the suite after them
- Rule: Treat any diff touching `Assets/Settings/*RPAsset*.asset`, `*UniversalRenderPipelineGlobalSettings.asset`, `ProjectSettings/GraphicsSettings.asset` or `QualitySettings.asset` as worth a second look, especially a same-size binary diff; run the EditMode suite after such a change.
- Why: `Mobile_RPAsset.asset` sat zeroed (same byte count) from `22642fe` for a month; every URP material rendered pink on the Mobile tier.
- Enforced by: TEST `ProjectConfigTests.EveryQualityLevel_ResolvesToANonNullRenderPipelineAsset`; rest CONVENTION
- Audit check: `git log --stat -5 -- Assets/Settings ProjectSettings` and inspect any same-size binary change; `git show <sha> -- <file> | head` must be readable YAML.
- Source: docs/CONVENTIONS.md:422-447, M:pink-shader-mobile-rpasset-corruption.md:19-21
- Status: PARTIAL (only the null-RP-asset class of corruption is guarded)

### CODE-ASSET-03 — Build runtime ground and floors from a thin Cube, never a Plane
- Rule: Any new runtime-generated ground or floor geometry defaults to `PrimitiveType.Cube` scaled thin (a real `BoxCollider`), not `Plane`.
- Why: a Plane is a paper-thin `MeshCollider`; at Android's lower framerate a `CharacterController` tunnels through it (chickens fell forever, fixed `5032ae9`).
- Enforced by: NONE
- Audit check: `git grep -n "PrimitiveType.Plane" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` returns nothing.
- Proposed guard: a RuntimeFiles source scan banning `PrimitiveType.Plane` (and `MeshCollider` on generated floors) with a self-test.
- Source: docs/CONVENTIONS.md:415-419
- Status: CURRENT (unenforced)

### CODE-ASSET-04 — Destroy only what you created
- Rule: `Destroy` / `DestroyImmediate` on an asset is refused by the Editor (one log line) but real in a player. A helper that may return a shared asset or a fresh instance says which in its signature (`ChickenVFX.BuildMaterial(out bool ownedByCaller)`) and the caller destroys only what it owns; read `Editor.log` for "Destroying assets is not permitted" after any cleanup change and verify cleanup in a player build (`nullMat=0`).
- Why: `ArenaWarmupProxies` destroyed the wired `Fx_Particles.mat`; every chicken particle in a build had a null material (fixed `0fd115ef`).
- Enforced by: TEST `ChickenParticleBudgetTests.WarmupProxies_NeverDestroyTheSharedFxMaterial` (one site); rest CONVENTION
- Audit check: `git grep -n -E "Destroy(Immediate)?\(\s*\w*(Material|material|mesh|Mesh|texture|Texture|sharedMaterial)" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`; each hit must destroy an instance it created.
- Source: docs/CONVENTIONS.md:449-464
- Status: PARTIAL

### CODE-ASSET-05 — Move or recreate a script only together with its `.meta`; commit an FBX with its `.meta`
- Rule: Never delete and recreate a `.cs` file without its `.cs.meta` (scene and prefab YAML reference the GUID); a new script comes with a committed meta holding a unique GUID; moves are `git mv` of the `.cs` and its `.meta`. Commit an `.fbx` and its `.meta` together, always (rig type, material remap, clip loop flags live in the meta).
- Why: a lost meta breaks script references or resets the rig, remap and loop flags.
- Enforced by: TEST `MissingScriptsTests.NoPrefab_HasAMissingScript`, `MissingScriptsTests.NoPrefabFile_ReferencesAScriptThatDoesNotResolve`, `MissingScriptsTests.NoBuildScene_ReferencesAScriptThatDoesNotResolve`; rest CONVENTION
- Audit check: `git show --stat <sha> | grep -E "\.(cs|fbx)$"` and confirm each added or renamed file has its `.meta` in the same commit.
- Source: docs/CONVENTIONS.md:468-471, M:chicken-fbx-rig-import-generic-not-humanoid.md:19
- Status: PARTIAL (FBX metas are covered only through the avatar test, CODE-ASSET-07)

### CODE-ASSET-06 — Check the Doppelganger variant before restructuring `Chicken.prefab`
- Rule: `Doppelganger.prefab` is a variant of `Chicken.prefab` with `ChickenCargo` stripped; do not restructure the base prefab without verifying the variant.
- Why: a variant silently inherits base changes.
- Enforced by: TEST `ProjectConfigTests.DoppelgangerPrefab_IsAChickenVariant_WithCargoStripped`, `ChickenListenerWiringTests.TheDecoyPrefab_KeepsTheListeners_ItOnlyDropsTheCargo`
- Source: docs/CONVENTIONS.md:472
- Status: CURRENT

### CODE-ASSET-07 — Import chicken FBX rigs as Generic, never Humanoid
- Rule: The class chicken FBX rigs use Animation Type Generic. Verify Generic explicitly after any regeneration or re-import; "don't just check that Humanoid mapping succeeds"; do not silence the guardrails, fix the import setting.
- Why: Humanoid retargeting rebuilds the pose in human muscle space and sinks the bird through the floor.
- Enforced by: TEST `DataIntegrityTests.ClassRegistry_EveryEntry_HasItsModelAndGenericAvatar` (asserts `!isHuman`)
- Source: M:chicken-fbx-rig-import-generic-not-humanoid.md:11-21, Assets/_Game/Scripts/Editor/Tests/DataIntegrityTests.cs:161-195
- Status: CURRENT

### CODE-ASSET-08 — Do not "fix" the skeletal-animation invariants
- Rule: `ChickenAnimator.ApplySkeletalOffsets` writes rest position and scale back unchanged; add no whole-body bob to the skeletal path (raise clip amplitudes instead); `ChickenAnimationSetup.FixClipLoopFlags` runs before `PopulateRegistry`; in any clip the Neck rotation stays negative; after an FBX re-export check texture binding explicitly.
- Why: a positive neck swings the head into the chest; a fresh FBX has `clipAnimations: []`, so loops play once and freeze.
- Enforced by: TEST `DataIntegrityTests.PeckClip_ActuallyDipsTheHead_AndNeverRotatesTheNeckPositive` (Peck clip only); rest CONVENTION
- Audit check: a diff to `ChickenAnimator.cs` that touches `ApplySkeletalOffsets` or adds a body offset needs a capture; check every clip's Neck `m_LocalRotation.x` curve sign after an import.
- Source: M:chicken-models-wired-2026-08-06.md:24-29
- Status: PARTIAL (written 2026-08; the neck rule is pinned for the Peck clip only)

### CODE-ASSET-09 — Keep build scenes in order: Bootstrap, Game, Map, Ability Lab
- Rule: `Bootstrap.unity` is build index 0, then Game, the baked Map, and the Ability Lab last and enabled; `CluckWarsBuildMenu.ValidateScenes` warns otherwise.
- Why: otherwise the player starts in the Game scene with no class selection.
- Enforced by: TEST `ProjectConfigTests.BuildSettings_StartAtBootstrap_ThenGame_ThenBakedMap_ThenAbilityLab`, `ProjectConfigTests.BuildSettings_EveryListedScene_ExistsOnDisk`
- Source: docs/CONVENTIONS.md:399-401, docs/TESTING.md:391-398
- Status: CURRENT

### CODE-ASSET-10 — Keep chickens on layer 8 and interactables on layer 9, on every collider
- Rule: Physics layers 8 (chickens) and 9 (interactables) match the masks baked into code, and every collider of an interactable prefab (children included) sits on layer 9.
- Why: scanners use hard masks; an off-layer child collider is invisible to them.
- Enforced by: TEST `ProjectConfigTests.PhysicsLayers_MatchTheMasksBakedIntoCode`, `ProjectConfigTests.ChickenPrefab_IsOnTheChickensLayer`, `ProjectConfigTests.InteractablePrefabs_PutEveryColliderOnTheInteractablesLayer`
- Source: Assets/_Game/Scripts/Editor/Tests/ProjectConfigTests.cs:160-280
- Status: CURRENT (implicit rule, written down here)

### CODE-ASSET-11 — Keep `Chicken.prefab` complete, without duplicate behaviours, and with its listeners
- Rule: The chicken prefab carries every component the gameplay loop resolves, no behaviour twice, and the listener components (`HitFeedback`, `ChickenAnimator`) enabled.
- Why: a missing component is a silent no-op mid-match.
- Enforced by: TEST `ProjectConfigTests.ChickenPrefab_HasEveryComponentTheGameplayLoopResolves`, `ProjectConfigTests.ChickenPrefab_HasNoDuplicateBehaviourComponents`, `ChickenListenerWiringTests.TheChickenPrefab_CarriesEveryListenerTheGameplayEventsRelyOn`
- Source: Assets/_Game/Scripts/Editor/Tests/ProjectConfigTests.cs:177-202, Assets/_Game/Scripts/Editor/Tests/ChickenListenerWiringTests.cs:48
- Status: CURRENT

### CODE-ASSET-12 — Share line and FX materials; never instance one per chicken
- Rule: Per-chicken line components share one material; no `BuildLineMaterial()` per instance; a static that caches an object is cleared on `SubsystemRegistration`.
- Why: per-chicken materials cost draw calls and leak on mobile.
- Enforced by: TEST `TelegraphMaterialAndInjectionTests.PerChickenLineComponents_ShareOneMaterial_NoPerChickenInstances`, `ChickenParticleBudgetTests.TheSharedFxMaterial_IsClearedOnSubsystemRegistration`
- Source: Assets/_Game/Scripts/Editor/Tests/TelegraphMaterialAndInjectionTests.cs:77, docs/CONVENTIONS.md:461-462
- Status: CURRENT (four line components covered)

### CODE-ASSET-13 — Add no serialized field to a shipped palette asset without re-authoring it
- Rule: A new serialized field on a shipped palette asset (`FeedbackPaletteSO`, `ColorSchemeSO`) reads as black until somebody re-authors the asset: author the value in the same commit.
- Why: Unity deserialises a missing colour field as zero.
- Enforced by: CONVENTION (review only)
- Audit check: a diff adding a `Color` field to a `*PaletteSO.cs` / `ColorSchemeSO.cs` also changes the matching `.asset` under `Assets/_Game/Data`.
- Source: docs/CONVENTIONS.md:262
- Status: CURRENT

### CODE-ASSET-14 — Attach no carried prop under a chicken root
- Rule: Among the chicken visuals only `ChickenShellVisuals` holds a `GameObject` field, and the class model is the only `Instantiate` under the chicken (ChickenController); no carried prop is attached.
- Why: a prop under the root outlives the Egg Shell hide.
- Enforced by: TEST `ShellBodyHiddenTests.NoCarriedPropIsAttachedToTheChicken_SoNoneCanOutliveTheHide`
- Source: Assets/_Game/Scripts/Editor/Tests/ShellBodyHiddenTests.cs:444
- Status: CURRENT (implicit rule, written down here)

### CODE-ASSET-15 — Re-check scene-baked data after a roster or slot change
- Rule: Scene-baked data (the `Game.unity` bot loadout presets) is not touched by C# refactors: re-check it after any roster or `SlotKind` change. More generally, re-audit old code's side effects before reusing it for a new purpose (the boundary-only `_wallsVisible` flag inherited by interior walls).
- Why: the bot loadout staleness bug (2026-07).
- Enforced by: TEST `DataIntegrityTests.BotLoadoutPresets_EveryAssignedSlot_IsAGenuineClassLegalAbility`; rest CONVENTION
- Audit check: for a roster or slot change, run the suite (the test above) and open the bot spawners in `Assets/_Game/Scenes/Game.unity` to compare their baked loadouts with the new roster.
- Source: M:bot-loadout-staleness-bug.md:15-17
- Status: PARTIAL

### CODE-ASSET-16 — Rescale the arena only with `MapPropsRescaler`, after every fence post agrees
- Rule: When arena dimensions change use the one-pass `MapPropsRescaler` (Cluck Wars menu, Bake Map): it measures every `Wall_N_*` post, refuses loudly if they disagree, then scales all props once. Derive angles, never hand-pick them (`YawAlong = atan2(dir.x, dir.z)`).
- Why: trusting the first post was a bug; a failed bake must not strand a raw `Game.unity` clone.
- Enforced by: CONFIG (the tool verifies itself and prints `ALIGNED` / `STILL WRONG`)
- Source: M:map-props-rescaler.md:15-25, M:art-metrics-that-passed-while-broken.md:23
- Status: CURRENT

### CODE-ASSET-17 — Never hand-author sprite `.meta` YAML; never add sprite generation back
- Rule: Let Unity generate sprite metas and `UiSpriteImportSettings.cs` stamp import settings; UI sprites come from the design export (`tools/export-design-assets.ps1`), USS tokens from `tools/generate-uss-tokens.ps1`. "Never add sprite generation back": `UiGfx` survives only as colour tokens, `Hex32`, two fonts and `AddShadow`.
- Why: hand-written metas corrupted three ways; runtime-baked sprites were the drift that sank three UI attempts.
- Enforced by: CONVENTION (review only)
- Audit check: `git log --diff-filter=A --name-only -5 -- '*.png.meta'` added together with their `.png`; `git grep -n -E "Texture2D\(|Sprite\.Create" HEAD -- Assets/_Game/Scripts/UI` returns nothing new.
- Source: M:design-project-claude-ai.md:18, M:ui-rebuild-2026-07.md:30
- Status: CURRENT

### CODE-ASSET-18 — Generate icons in the locked style and pass the readability sheet first
- Rule: Icons use the locked style (cream #FEF5E0 fill, ink #1A0E04 outline, glyph only, 256 px) via Flux plus a two-tone PIL pass; the readability sheet (112/56/40/24 px) is the gate before copying into `Assets/`. Use `brainstorm` or `build_flux_prompt`, not the assetforge `image`/`model` ops (they append a T-pose suffix).
- Why: dead ends of the 2026-11-06 session.
- Enforced by: CONVENTION (review only)
- Audit check: a new `Icon_*.png` commit references its readability sheet.
- Source: M:icon-and-fx-generation-pipeline.md:11-23
- Status: CURRENT

## 3.6 TEST: how things are tested and verified

### CODE-TEST-01 — Run the EditMode suite green before every commit batch, build and playtest; know what it cannot prove
- Rule: The EditMode suite is green before any build or play session (layer 0), and an implementation batch commits only on a green suite. A green suite means "the build is healthy enough to start a playtest", never "the game works": it cannot exercise a live `NetworkRunner` (movement, control stack, RPC round trips, match ticks, bots, replication, weaving at spawn, Zenject in a player, IL2CPP, presentation timing). Quote the baseline from docs/STATE.md, never a fixed count.
- Why: catches the unassigned-slot class of bug before it burns a playtest; the suite has zero `[UnityTest]` methods.
- Enforced by: NONE (no git hook; the only CI workflow, `.github/workflows/pages.yml`, deploys `docs/site`)
- Audit check: the report of every batch quotes the suite result of the actual run (passed/total, 0 compile errors) and the commit it ran on; `git log` shows no batch commit after a red run.
- Proposed guard: G-10, a pre-push hook or a self-hosted CI job running `unity test --mode EditMode` (or batch `-runTests`) and refusing on red or on a missing result XML.
- Source: docs/TESTING.md:197-202, docs/TESTING.md:241-258, docs/TEST_STRATEGY.md:44-46
- Status: CURRENT (unenforced; the counts in TESTING.md and TEST_STRATEGY.md are stale, Appendix A)

### CODE-TEST-02 — Accept a claim only when a before/after state sample shows it
- Rule: A claim ("reset", "teleported", "banked") is accepted only when a before/after STATE sample shows it (positions, cargo, base totals, pile amounts, timers). Logs are for finding where to look.
- Why: CW-221: a "Teleported" line was printed for a chicken that never moved; an unattended session ran past a round-reset bug for hours.
- Enforced by: CONFIG (`tools/test-session/merge.ps1` INVARIANT rules, the in-game `Invariant` probe, `tools/test-session/verify.ps1` exit code)
- Source: docs/TEST_STRATEGY.md:21-22, M:feedback-verify-state-not-logs.md:16; test-session thread 2026-10-20 (owner: "prevent similar ones in the future with a better testing strategy")
- Status: CURRENT

### CODE-TEST-03 — Close a bug only with a check that fails on the old build
- Rule: A bug is not closed until an invariant (in `merge.ps1`, the `Invariant` probe or an EditMode test) fails on the old build and passes on the new one. For a guard test, revert the fix (or remove the config line) and watch the test fail before trusting it.
- Why: CW-221; a guard never seen failing may test nothing (WeavingGuardTests "was checked to fail when CluckWars.Gameplay is removed").
- Enforced by: CONVENTION (review only)
- Audit check: the fix commit's message or report names the test and states it failed on the parent commit; for a sample, `git checkout <fix>^ -- <runtime file>` in a scratch worktree and run that one test.
- Source: docs/TEST_STRATEGY.md:23-24, docs/CONVENTIONS.md:495; pink-shader thread 2026-09-14 (owner: "Fix it and prevent it to happen again")
- Status: CURRENT (see open question Q6 on how far the owner endorsed the strict form)

### CODE-TEST-04 — Leave zero untriaged findings
- Rule: Every category in `findings.txt` (`ERROR EXCEPTION SYNC GHOST INVARIANT`) is 0, or each remaining item has a backlog entry saying why it is acceptable; `verify.ps1` turns this into PASS/FAIL, nobody decides by skimming.
- Why: SYNC=93..266 appeared in every report and was never triaged.
- Enforced by: CONFIG (`tools/test-session/verify.ps1` exit code)
- Source: docs/TEST_STRATEGY.md:25-27
- Status: CURRENT

### CODE-TEST-05 — Run the mode x round matrix; name every mode you did not run
- Rule: Every scenario runs at least 3 rounds; modes `solo`, `shared2`, `shared4`, `hostleave` (`scenario-pc.ps1 -Mode ...`); a change in GameManager, spawning, Fusion or round-reset code runs all four. A report states mode coverage (solo / host / join / late-join / host-leave / reconnect): "Missing mode = untested mode; say so in the report."
- Why: "round 1 is fine" proves nothing about reset code.
- Enforced by: CONFIG (`tools/test-session/scenario-pc.ps1` exit code, `VERDICT: PASS`); rest CONVENTION
- Audit check: the report lists the modes run and the verdict line of each.
- Source: docs/TEST_STRATEGY.md:28-30, docs/TEST_STRATEGY.md:48, docs/TEST_STRATEGY.md:66
- Status: PARTIAL (which modes a change requires is review-only)

### CODE-TEST-06 — Trust a green session only for the paths it exercised; force combat with four clients and `-cw_perk`
- Rule: Before trusting a green verdict ask "did this run exercise the changed path?" and check the log counts of the specific events. The 2-client session cannot produce combat (personalities runner + balanced, no bots); to exercise steal / shove / stun run four PC clients, one `-cw_profile` each, with `-cw_perk` (not `-cw_class`).
- Why: `AutoPlayPersonality.For(playerId)` is playerId mod 3; default perks carry no shove or stun; a shared anonymous UGS identity makes the second join fail.
- Enforced by: NONE
- Audit check: in `Builds/Windows/logs/client*.log`, count the lines of each event the change touches (by its source tag, docs/TESTING.md:519-541); a zero count means the path was not exercised and the verdict says nothing about it.
- Proposed guard: a `verify.ps1` option `-Require steal,shove,stun` that fails when the merged report shows zero of a named event.
- Source: M:autoplay-never-fights.md:8-12
- Status: CURRENT (unenforced)

### CODE-TEST-07 — Make round N start equal round 1 start, on every peer
- Rule: Positions, cargo, bases, piles, cooldowns and timers at round N start equal round 1; every peer holds the same world (`Sync` heartbeats plus the `World` hash). A new state transition writes down its expected state and adds it to `RoundInvariants` / `merge.ps1` before the code is "done".
- Why: catches every "state leaks across rounds" bug without knowing what to look for.
- Enforced by: CONFIG (`RoundInvariants`, `merge.ps1`, 0 SYNC/GHOST and one SHA); rest CONVENTION
- Audit check: a commit adding a state transition also changes `RoundInvariants` or `tools/test-session/merge.ps1`.
- Source: docs/TEST_STRATEGY.md:31-34, docs/TEST_STRATEGY.md:59-60
- Status: PARTIAL

### CODE-TEST-08 — Prove the harness first; treat suspiciously clean numbers as bugs; say what you did not check
- Rule: If a number looks wrong (0 food, nobody moves) prove the harness works before concluding anything about the game. Identical totals, identical positions and zero counts are bugs until explained. Read the whole report and say what was not checked; a session summary states what was verified, what was not, and what needs the owner.
- Why: `total=15,0` repeated for four rounds was the end-of-round bonus; nothing had ever been banked (CW-216).
- Enforced by: CONVENTION (review only)
- Audit check: every test report has a "not verified" list.
- Source: docs/TEST_STRATEGY.md:35-40
- Status: CURRENT

### CODE-TEST-09 — Build every platform from one clean commit
- Rule: Build every platform of a session from one commit (`merge.ps1` flags an SHA mismatch); never build a player from a dirty tree; CLI builds need the cloud project id (CW-200).
- Why: a dirty tree bakes another session's work in; mixed SHAs make cross-peer results meaningless.
- Enforced by: CONFIG (`merge.ps1` SHA-mismatch flag); rest CONVENTION
- Audit check: `git status --porcelain` is empty (apart from the known `ProjectSettings/` churn) before the build command runs.
- Source: docs/TEST_STRATEGY.md:52, M:test-session-2026-10-20-learnings.md:15
- Status: PARTIAL

### CODE-TEST-10 — After adding an RPC with authority checks, run `shared4` and count rejections
- Rule: Run `shared4` and grep for `rejected RPC`; a non-zero count with honest clients is a bug.
- Why: sender checks must not refuse honest peers.
- Enforced by: CONFIG (`merge.ps1` files `rejected RPC_Apply*` lines as INVARIANT findings); rest CONVENTION
- Audit check: `grep -c "rejected RPC_" merged log` is 0 after a `shared4` run on the new build.
- Source: docs/TEST_STRATEGY.md:62-64
- Status: PARTIAL (only `RPC_Apply*` is gated automatically)

### CODE-TEST-11 — Test on PC EXE clients unless the owner reconnects a phone; never on emulators
- Rule: Test with PC EXE clients only until Maestro reconnects a phone; Android emulators are not an option on this machine.
- Why: owner, test-session thread 2026-10-20: "test with PC only"; Hyper-V blocks the GPU modes URP needs.
- Enforced by: CONVENTION (review only)
- Audit check: a test plan that names a phone or an emulator must cite a reconnected device.
- Source: M:feedback-verify-state-not-logs.md:11-18, M:android_testing_infra.md:28-29; test-session thread 2026-10-20 (owner)
- Status: CURRENT

### CODE-TEST-12 — Read a source guard over the whole class, never weaken a guard for a refactor
- Rule: A source-scan guard about a class reads it with `TestSources.ReadClass(path)` (the file plus its `Name.*.cs` partial siblings); a guard deliberately about one file keeps `File.ReadAllText`. "Splitting a file must never be what makes a guard pass": repoint a broken guard, never loosen its assertion.
- Why: an A21 partial split moved code out of single-file guards.
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n "File.ReadAllText" HEAD -- Assets/_Game/Scripts/Editor/Tests` and, for each path read, `git ls-files '<dir>/<Name>.*.cs'`; any partial sibling means the guard is blind to it (today `UiTextTests.LiteralTextHits` on `MenuUiController.cs`).
- Source: docs/CONVENTIONS.md:275, Assets/_Game/Scripts/Editor/Tests/TestSources.cs:28-40
- Status: PARTIAL (no guard-the-guards test; gap G-01)

### CODE-TEST-13 — Make every scan prove it saw something and self-test its pattern
- Rule: A guard that scans files, types or assets asserts it saw enough of them (`Assert.Greater(scanned, N)`) and has a self-test proving its regex matches the forbidden form and not the legal one.
- Why: a wrong root or a rotted regex passes on nothing.
- Enforced by: CONVENTION (pattern used across the suite, e.g. `RuntimeSourceGuardTests.TheScan_SeesTheRuntimeScripts`, `RuntimeSourceGuardTests.TheDebugLogGuard_MatchesWhatItIsMeantToCatch`)
- Audit check: a new test that calls `RuntimeFiles()` / `ReadClass` / `LoadAllIn` also asserts a minimum count and has a regex self-test.
- Source: Assets/_Game/Scripts/Editor/Tests/RuntimeSourceGuardTests.cs:43, Assets/_Game/Scripts/Editor/Tests/RuntimeSourceGuardTests.cs:65
- Status: CURRENT (implicit rule from the tests, written down here)

### CODE-TEST-14 — Populate and clean up test objects the standard way
- Rule: Populate private serialized fields with `ScriptableObject.CreateInstance` plus reflection and always `Object.DestroyImmediate` in a `finally` or `[TearDown]`; a test needing a never-spawned chicken in a registry uses `RegistryTestAccess`; assert against shipped assets through `TestAssets.Load` / `LoadAllIn`, arena geometry through `ShippedMap`.
- Why: leaked instances and hand-copied fixtures make tests lie.
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n "CreateInstance<" HEAD -- Assets/_Game/Scripts/Editor/Tests` hits sit in a method with `DestroyImmediate` in `finally` or a `[TearDown]`.
- Source: docs/TESTING.md:238-239, docs/CONVENTIONS.md:86, Assets/_Game/Scripts/Editor/Tests/TestAssets.cs:78
- Status: CURRENT

### CODE-TEST-15 — Confirm the compile and the result file before trusting a suite run
- Rule: After a refresh check `EditorUtility.scriptCompilationFailed` before trusting a suite run or a capture; a failed compile writes NO result XML and the runners execute the OLD assemblies and report green. Confirm the result XML exists, is newer than the change, contains the new fixtures, and compare the test count with the expected one.
- Why: a green run on stale assemblies.
- Enforced by: NONE
- Audit check: the run report quotes the XML path, its timestamp, the total and one new fixture name.
- Proposed guard: make the suite runner (the `TestRunnerApi` script or `unity test` wrapper) fail when compilation failed, the XML is missing or older than the newest `.cs`, or the count dropped.
- Source: M:bridge-tests-run-times-out-use-testrunnerapi.md:17-19
- Status: CURRENT (unenforced)

### CODE-TEST-16 — Keep diagnostic log-line shapes that `merge.ps1` parses stable
- Rule: The round-start count suffix and the invariant verdict line keep the exact shape `tools/test-session/merge.ps1` reads; the round-start probe gates on a live match (a base exists), never on a scene name.
- Why: a reworded line silently zeroes a session check.
- Enforced by: TEST `RoundResetSignalTests.RoundStart_CountsSuffix_IsWhatMergeParses`, `RoundInvariantsTests.TheVerdictLine_KeepsTheShapeMergeReads`, `RoundInvariantsTests.TheProbe_GatesTheRoundStartCheck_OnAMatch_NotOnASceneName`
- Source: Assets/_Game/Scripts/Editor/Tests/RoundResetSignalTests.cs:269, Assets/_Game/Scripts/Editor/Tests/RoundInvariantsTests.cs:210
- Status: CURRENT (implicit rule, written down here)

### CODE-TEST-17 — Excuse in a probe only what the game itself refuses, and count the excuses
- Rule: A probe that flags a failure excuses exactly what the game refuses (`IsSlowImmune` windows, a claimed base wall between aura centre and victim), counts excused frames under their excuse and prints them; a check that cannot apply skips on a positive condition (`RoundInvariants.RoundStartIsCheckable`). "If a future run still FAILs with `excused=0`, it is a real bug."
- Why: BL-145 / BL-146 false FAILs from the aura probe and the Ability Lab.
- Enforced by: TEST `RoundInvariantsTests.TheProbe_GatesTheRoundStartCheck_OnAMatch_NotOnASceneName`; rest CONVENTION
- Audit check: a new probe exclusion prints an `excused=` count.
- Source: docs/STATE.md:25-30
- Status: PARTIAL

### CODE-TEST-18 — Assert features and relationships, not bounding boxes; trust the render over the metric
- Rule: On any art, mesh or animation change name the feature that must be present ("is the head present") and assert the relationship (bone to bone, not bounding boxes). "When a metric and a render disagree, the render is right."
- Why: four art bugs beat every numeric check; two scored better than the fix.
- Enforced by: CONVENTION (review only)
- Audit check: an art-change report includes the capture it was judged by and the feature assertion added.
- Source: M:art-metrics-that-passed-while-broken.md:20-29
- Status: CURRENT

### CODE-TEST-19 — Verify live what EditMode cannot prove
- Rule: After a batch that touches spawning, injection, weaving, RPCs or presentation timing, run a live check (PC clients, `scenario-pc.ps1`, a player build, screenshots or 60 fps captures of short windows) before calling it verified; tester agents read every script before running it and give each client its own `-cw_profile`.
- Why: batch EditMode cannot prove weaving at spawn, Zenject in a player, IL2CPP or presentation timing; batches 3-6 of the audit were owed live checks (docs/STATE.md) and the first live run found two real defects.
- Enforced by: CONVENTION (review only)
- Audit check: docs/STATE.md entries that say "not verified in game" have a matching backlog item (for example BL-157, BL-160).
- Source: docs/TESTING.md:241-258; the tester-agent practice: the 2026-11-12/13 architecture audit; ratified as a rule by Maestro on 2026-11-14 (ruling D13)
- Status: CURRENT

## 3.7 BUILD: builds, tooling, Unity MCP/CLI, running suites, environment

### CODE-BUILD-01 — Drive the Editor through the `ai-game-developer` MCP; never Unity's own MCP
- Rule: `mcp__ai-game-developer__*` is the Unity bridge for every live-Editor task (scene/asset/component edits, `script-execute`, `tests-run`, screenshots, RQL). "Do NOT use Unity's own MCP" (`mcp__unity-mcp__*`, the `.unity/relay` server). The `unity` CLI / Pipeline only for what the MCP lacks (`build run`, `diagnostics run_audit`, transactional `batch`, headless/CI, Editor/license management) or as a fallback when the bridge is down; plugin skills (`/unity:ui-uitk`, `unity:urp-postprocessing`, `unity:validate-urp-render-graph-renderer-feature`, `unity:ui`) for UI Toolkit, URP post-processing and render-graph work. Restart the Editor when needed without asking.
- Why: owner, modelgen thread 2026-08-07: Unity's MCP needs a Pro licence he does not have; it returns "Connection revoked".
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n -i "unity-mcp\|com.unity.ai.assistant" HEAD -- docs CLAUDE.md Packages/manifest.json` lists only stale docs (Appendix A).
- Source: .claude/CLAUDE.md:83-108, M:unity-plugin-vs-mcp-decision.md:11-15; modelgen thread 2026-08-07 and 2026-08-09 (owner)
- Status: CURRENT (docs/TESTING.md:106-154 still documents Unity's MCP; Appendix A)

### CODE-BUILD-02 — Run the full suite through `TestRunnerApi` or in batch mode; judge render tests by a batch run
- Rule: The MCP `tests-run` call times out (5 min) on the full suite: run it through `TestRunnerApi` from a `script-execute` call that writes a result file and poll the file, or use batch mode (`unity test`) with the Editor closed. A filtered `tests-run` on one class still works; prefer an unfiltered run over a filter that can match nothing. Judge render tests (`Phase8ChunkD1Tests` mound captures) by a batch run, without `-nographics`.
- Why: the bridge call is synchronous with a fixed timeout; an empty filter can wedge the runner; render tests depend on live Editor state.
- Enforced by: CONVENTION (review only)
- Audit check: the run report names the runner used and quotes the result file.
- Source: .claude/CLAUDE.md:106-108, M:bridge-tests-run-times-out-use-testrunnerapi.md:11-15, M:unity-hang-is-usually-a-wedged-test-runner.md:31-34
- Status: CURRENT

### CODE-BUILD-03 — Use one Unity process per project at a time
- Rule: Headless `-batchmode -runTests` exits 1 with no results while the Editor holds the project (`Temp/UnityLockfile`): check `tasklist | grep Unity.exe` first. Tester agents build only when no batch agent is running.
- Why: the project lock; two Unity processes collide.
- Enforced by: CONFIG (Unity's project lock refuses the second process)
- Source: M:unity-mcp-bridge-over-http.md:11-15
- Status: CURRENT

### CODE-BUILD-04 — Treat a "hung" Editor with a dead bridge as a wedged test runner first
- Rule: Before concluding the Editor needs a restart, check the console for "Cannot run tests: another test run is already in progress"; reload the scene (plus reimport) and retry.
- Why: an agent lost ~40 minutes on 2026-08-14; the recorded remedy (restart) was heavier than the fix.
- Enforced by: CONVENTION (review only)
- Audit check: n/a (operational step); a report that restarted the Editor says what the console showed first.
- Source: M:unity-hang-is-usually-a-wedged-test-runner.md:11-34
- Status: CURRENT

### CODE-BUILD-05 — After an MCP test run, reopen Bootstrap before any Play check
- Rule: After `tests-run` the Editor may sit on an empty Untitled scene or leave `AbilityLab.unity` open: open `Assets/_Game/Scenes/Bootstrap.unity` (through `script-execute`; the 2.0 bridge has no `scene-*` tools), wait ~4 s and confirm, using the `Auto-load suppressed` line in `Editor.log` as the oracle.
- Why: a black Game view with no camera is this, not a regression.
- Enforced by: CONVENTION (review only)
- Audit check: n/a (operational step).
- Source: M:mcp-tests-run-leaves-empty-scene.md:11-17, M:unity-mcp-2-0-upgrade.md:15
- Status: CURRENT

### CODE-BUILD-06 — Exit Play mode before any capture; capture at time scale 1
- Rule: Before a capture probe `EditorApplication.isPlaying`, set it false and wait ~2 s, reopen Bootstrap, then capture; judge draw calls by averages and a clean re-run; capture at `timeScale` 1 (Fusion ignores time scale).
- Why: a capture started in Play mode shows a one-sample draw-call spike (470 vs 85).
- Enforced by: CONVENTION (review only)
- Audit check: a capture report states Play mode was off and gives averages.
- Source: M:lab-capture-run-start-spike.md:11-15, M:feedback-icons-decisions-2026-11-07.md:26
- Status: CURRENT

### CODE-BUILD-07 — Build Android as IL2CPP, ARM64 only, minSdk 24, APK
- Rule: The normal Android build forces IL2CPP, ARM64 only (no ARMv7; `X86_64` is Magic Leap in 6000.3 and is rejected), `minSdk = 24` and an APK (`buildAppBundle = false`).
- Why: small APK for 2021+ devices.
- Enforced by: CONFIG (the build menu sets them, Assets/_Game/Scripts/Editor/CluckWarsBuildMenu.cs)
- Source: docs/TESTING.md:21-25, docs/TESTING.md:89-92
- Status: CURRENT

### CODE-BUILD-08 — Sign Play Store bundles from environment variables; never commit a key
- Rule: Signing comes from `CW_KEYSTORE_PATH`, `CW_KEYSTORE_PASS`, optional `CW_KEY_ALIAS` and `CW_KEY_PASS`; a missing one fails the build naming the variable and never falls back to the debug key; `*.jks` / `*.keystore` are git-ignored; `versionCode` is set for the build only.
- Why: never commit secrets; every bundle must outrank the last.
- Enforced by: TEST `PlayStoreBuildTests.Keystores_AreGitIgnored`, `PlayStoreBuildTests.Signing_MissingPathOrPassword_FailsAndNamesTheVariable`
- Source: docs/TESTING.md:36-48
- Status: CURRENT

### CODE-BUILD-09 — Keep bundle identifiers, branding and the cloud project id pinned
- Rule: Android and iOS identifiers, branding and icons are pinned in ProjectSettings; a CLI or headless build resolves a valid cloud project id (env override trimmed, malformed refused) or Host/Join fails.
- Why: headless builds once lacked the UGS project id and installed as `com.DefaultCompany.CluckWars`.
- Enforced by: TEST `BuildGuardsTests.ProjectSettings_AndroidAndIosIdentifiersArePinned`, `BuildGuardsTests.ProjectSettings_BrandingAndIconsAreSet`, `BuildGuardsTests.CloudProjectId_RejectsEmptyOrMalformed`, `BuildGuardsTests.Resolve_MalformedEnvOverrideIsNotSilentlyReplaced`
- Source: M:test-session-2026-10-20-learnings.md:15-17, Assets/_Game/Scripts/Editor/Tests/BuildGuardsTests.cs:24-103
- Status: CURRENT

### CODE-BUILD-10 — Keep iOS export off by default; Build All never builds iOS
- Rule: iOS Xcode export is off by default (toggle `Cluck Wars/Build/Enable iOS Export (needs a Mac)`), and Build All never builds iOS.
- Why: owner, iOS thread 2026-08-18: "Build all should never build iOS too"; there is no Mac to finish it.
- Enforced by: CONFIG (`CluckWarsBuildMenu`, Assets/_Game/Scripts/Editor/CluckWarsBuildMenu.cs:28-40); no test
- Source: CLAUDE.md:17; iOS thread 2026-08-18 (owner)
- Status: CURRENT

### CODE-BUILD-11 — Use the phone on USB or Windows builds; never emulators; one adb
- Rule: Do not attempt emulator testing; always use the v41 `adb` on PATH and never switch between adb copies; build with `Android + Restore Windows Target` to keep the MCP responsive.
- Why: Hyper-V blocks every GPU mode URP needs; two adb servers fight; a target switch floods the MCP command cache for ~2 minutes.
- Enforced by: CONVENTION (review only)
- Audit check: n/a (environment step).
- Source: docs/TESTING.md:61-102, M:android_testing_infra.md:9-29
- Status: CURRENT (the device model in the docs is stale, Appendix A)

### CODE-BUILD-12 — Keep production builds free of dev tools; dev-tools builds for sessions
- Rule: `CLUCKWARS_DEV_TOOLS` turns on probes, auto-test and the overlay; production builds have none, and a production build given a `cw_*` key logs one Warn and does nothing. On Android always `am start -S` (extras are read once per process); give each EXE its own `-cw_profile`.
- Why: dev surfaces must not ship active; extras are consumed once.
- Enforced by: CONVENTION (review only); `DevTools.Available` gate in code
- Audit check: `git grep -n "CLUCKWARS_DEV_TOOLS" HEAD -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` hits sit in Diagnostics or behind `DevTools.Available`.
- Source: docs/TESTING.md:462-464, docs/TESTING.md:490-493, docs/TESTING.md:513
- Status: CURRENT

### CODE-BUILD-13 — Back up dirty URP and ProjectSettings files before a batch or Android build, and restore build side effects
- Rule: Batch and Android builds re-serialize `UniversalRenderPipelineGlobalSettings.asset`, `PC_RPAsset.asset`, `ProjectSettings.asset` and `UnityConnectSettings.asset`: back up dirty files first and restore the build's side-effect files afterwards (exact files only).
- Why: build side effects pollute the tree (the three modified `ProjectSettings/` files in the working tree are this churn).
- Enforced by: CONVENTION (review only)
- Audit check: after a build, `git status --porcelain` shows no new change to those files.
- Source: M:test-session-2026-10-20-learnings.md:15, M:test-session-2026-10-20-learnings.md:19
- Status: CURRENT

### CODE-BUILD-14 — Delete `Builds/Windows` before a release build
- Rule: Delete `Builds/Windows` before a release build.
- Why: a stale `Assembly-CSharp.dll` from before the assembly split stays in `Managed/` (not loaded, not cleaned).
- Enforced by: CONVENTION (review only)
- Audit check: `ls Builds/Windows/CluckWars_Data/Managed/Assembly-CSharp.dll` is absent in a release build folder.
- Source: docs/STATE.md:117
- Status: CURRENT

### CODE-BUILD-15 — Bump the four Unity MCP packages together and match the DEPS define
- Rule: After any `com.ivanmurzak.unity.mcp*` bump, bump all four together and check `NuGetConfig.DependencyGenerationDefine` against `UNITY_MCP_DEPS_N` in `ProjectSettings.asset` on all platforms; revert with Unity closed; a `BadImageFormatException` on varying assemblies means a corrupt shared Bee cache (`%LOCALAPPDATA%\Unity\Caches\bee`).
- Why: the add-ons are version-locked to core; a stale define skips core.
- Enforced by: CONVENTION (review only)
- Audit check: `git show HEAD:Packages/manifest.json | grep ivanmurzak` shows one version family.
- Source: M:unity-mcp-bump-and-bee-cache.md:11-19, M:unity-mcp-2-0-upgrade.md:11-15
- Status: CURRENT

### CODE-BUILD-16 — Test sessions run at log level Debug, never Warn
- Rule: `Debug` is the level for a test session; drop to `Verbose` only to chase a specific bug; "Never test at `Warn`".
- Why: Warn loses the state-transition breadcrumbs that make a failure diagnosable.
- Enforced by: CONVENTION (review only)
- Audit check: the session log's first lines show `MinLevel` Debug or Verbose.
- Source: docs/TESTING.md:278-288
- Status: CURRENT

### CODE-BUILD-17 — After a hard crash, verify state and resume; do not chase hardware
- Rule: After a HYPERVISOR_ERROR / Kernel-Power 41 crash verify state (`git fsck`, the shared Bee cache for zero-filled entries, half-written assets missing `.meta`, a stale `Temp/UnityLockfile`) and resume; "don't spend effort on hardware theories".
- Why: a known incompatibility on this PC.
- Enforced by: CONVENTION (review only)
- Audit check: n/a (recovery step).
- Source: M:hypervisor-crash-known-pc-issue.md:11-13
- Status: CURRENT

### CODE-BUILD-18 — Check ComfyUI's port before diagnosing a generation failure
- Rule: If assetforge/modelgen report that ComfyUI did not start, check `curl http://127.0.0.1:8188/system_stats` first; the SageAttention `.pyd` files stay renamed and ComfyUI is launched without `--use-sage-attention`; leave Play mode before a generation batch.
- Why: SageAttention hijacks attention process-wide and corrupts conditioning; Flux is twice as slow during Play.
- Enforced by: CONVENTION (review only)
- Audit check: n/a (environment step).
- Source: M:comfyui-sageattention-pyd-bug.md:11-41, M:icon-and-fx-generation-pipeline.md:15
- Status: CURRENT as of 2026-08-21 (not re-verified)

## 3.8 PROC: agents, models, branching, commits, docs upkeep, backlog

### CODE-PROC-01 — Orient before changing anything non-trivial
- Rule: Begin a session with `docs/STATE.md` and `git log --oneline -10`; check `docs/ARCHITECTURE.md` for an unfamiliar system; skim `docs/CONVENTIONS.md` (and now this document and ARCHITECTURE_RULES.md) before non-trivial changes.
- Why: hard-won rules live there.
- Enforced by: CONVENTION (review only)
- Audit check: an agent report names the STATE section and HEAD it started from.
- Source: CLAUDE.md:40-45
- Status: CURRENT (docs/ARCHITECTURE.md is partly stale: docs/ARCHITECTURE.md:15-16 still name `CharacterSelectController` and `TouchControlsHud`, which no longer exist)

### CODE-PROC-02 — Route non-trivial work through `code-architect`; let specialists implement; `librarian` looks up before and maintains the docs after
- Rule: `code-architect` (opus) is the default entry point for any non-trivial task: it decomposes, delegates to the specialist in the routing table and reviews the specialist's diff before signoff. Skip it only for a trivial single-file fix or a doc-only change. "Don't implement inline when a specialist applies." `librarian` (sonnet, read-only apart from the docs) has two roles in the same route: before work it answers "where / which rule / what was decided / what depends on" so nobody re-derives it; after signoff `code-architect` passes it the commit range and the specialist's "Docs upkeep" line, and it brings `docs/FILE_STRUCTURE.md` and the rules docs in line. It is the only agent that edits the rules docs (ruling D13a, 2026-11-14); `code-architect` reviews its diff like any other.
- Why: one owner of structure and review.
- Enforced by: CONVENTION (review only)
- Audit check: a multi-file commit's report names the specialist that wrote it and the reviewer that signed it off.
- Source: .claude/CLAUDE.md:14-26 (routing and lifecycle), .claude/CLAUDE.md:49 (routing table), .claude/agents/librarian.md, .claude/agents/code-architect.md:57-61 (docs upkeep), .claude/agents/senior-dev.md:29 ("Docs upkeep" line), ~/.claude/CLAUDE.md (Agent routing)
- Status: CURRENT

### CODE-PROC-03 — Never pass `model: "fable"` to any agent unless the owner names it
- Rule: No agent or sub-agent is spawned with the fable model unless Maestro names Fable in the request; default `opus` or `sonnet`.
- Why: Fable's credit pool is exhausted; the agent dies instantly. Owner, feedback thread 2026-08-01: "never use Fable unless explicitly stated".
- Enforced by: CONVENTION (review only)
- Audit check: `grep -rn "fable" .claude/agents` (disk) finds no model pin; agent briefs carry no `model: "fable"`.
- Source: .claude/CLAUDE.md:20-24, ~/.claude/CLAUDE.md (Model overrides); feedback thread 2026-08-01 (owner)
- Status: CURRENT

### CODE-PROC-04 — Plan with opus, implement with sonnet
- Rule: Planning and architecture agents run on opus; implementation agents (senior-dev and similar) on sonnet.
- Why: owner, progression thread 2026-09-15: "opus is for plans and sonnet for coding"; opus coding agents hit the usage limit three times.
- Enforced by: CONVENTION (review only)
- Audit check: an implementation brief passes `model: "sonnet"` or an agent whose frontmatter says sonnet.
- Source: M:agent-model-routing.md:10-13; progression thread 2026-09-15, arena thread 2026-07-29 (owner)
- Status: CURRENT (`.claude/agents/senior-dev.md:3` now pins `model: sonnet`; the audit check holds on disk; the memory M:agent-model-routing.md still offers cravify as a coding path, superseded by CODE-PROC-05)

### CODE-PROC-05 — Do not use cravify or Antigravity unless the owner asks for it by name
- Rule: "Do NOT use cravify / Antigravity unless the user explicitly asks for it by name"; this applies to all agents and sub-agents; implementation goes through Claude implementers or directly.
- Why: its usage limit is exhausted (since 2026-09-24).
- Enforced by: CONVENTION (review only)
- Audit check: no `agy` process or `cravify` skill call in an agent's transcript without a named request.
- Source: ~/.claude/CLAUDE.md (cravify)
- Status: CURRENT (fixed 2026-11-17: `.claude/CLAUDE.md:123` now allows cravify only when Maestro asks by name and `.claude/agents/senior-dev.md:22` repeats the ban; only the memory M:agent-model-routing.md still lists it as a coding path, Appendix A)

### CODE-PROC-06 — Run slow batches from the main session, one foreground specialist at a time
- Rule: A batch with slow items (artist icons, lab captures, five-minute suite runs) is run by the main session: one foreground agent at a time, verify each hand-back (git status, the capture or sheet, the suite), commit per item. Keep `code-architect` for code-only batches and design/triage. When an orchestrator dies, check `git status` first and never redo work already in the tree; resume a stalled agent with `SendMessage` rather than starting over.
- Why: a background code-architect died twice on API timeouts on 2026-11-10, taking its child with it.
- Enforced by: CONVENTION (review only)
- Audit check: after an agent failure, the next action in the transcript is `git status` / `git log`.
- Source: .claude/CLAUDE.md:26-33, CLAUDE.md:48, M:architect-agent-stalls-waiting.md:11-17
- Status: CURRENT

### CODE-PROC-07 — Build every agent brief with the prompt-master skill
- Rule: Before any `Agent` call, invoke the `prompt-master` skill with the draft brief and the target agent and spawn with the optimised prompt; keep the facts (paths, decisions, rules) in a brief file the prompt points at.
- Why: owner, icons thread 2026-11-07: "as a rule, when spawning agents you should use /prompt-master"; a brief is the only context a sub-agent gets.
- Enforced by: CONVENTION (review only)
- Audit check: each `Agent` call in a transcript is preceded by a `prompt-master` invocation.
- Source: M:feedback-use-prompt-master-for-agent-prompts.md:8-15; icons thread 2026-11-07 (owner)
- Status: CURRENT (stated in `.claude/CLAUDE.md:28` since 2026-11-17)

### CODE-PROC-08 — Give an implementation agent a complete, verifiable brief
- Rule: An implementation brief states: context (branch, HEAD, dirty files that are not the agent's, validation method, the current baseline from docs/STATE.md, binding rules); numbered items with exact sites (re-grep them, line numbers drift); constraints (no gameplay number, networked layout, enum, prefab or scene change unless stated); process (one commit per green item, no push, no amend); stop conditions; a report format with real counts and an "unverified" list.
- Why: stale line numbers and missing stop conditions caused rework; a commit per green item loses at most one item to a timeout.
- Enforced by: CONVENTION (review only)
- Audit check: read the written brief of each implementation agent and confirm it names the context (branch, HEAD, foreign dirty files, validation method, baseline), numbered items with exact verified sites, the constraints, the process (commit per green item, no push, no amend), the stop conditions and the report format.
- Source: .claude/CLAUDE.md:26-33 (commit per item); the rest: the 2026-11-12/13 architecture audit; ratified as a rule by Maestro on 2026-11-14 (ruling D13)
- Status: CURRENT

### CODE-PROC-09 — Review risky batches independently, pinned to a commit range
- Rule: A risky batch (networking, food, injection, assemblies) gets a read-only reviewer (`qa-reviewer`) pinned to a commit range (`git show <sha>:path`, never the working tree), with explicit questions (lost or doubled events, forgery, conservation, feel, tests that would still pass if the fix were reverted). Fix its findings before stacking more work.
- Why: the reviewed audit batches 1 and 2 had real defects the author missed; batches 3-6 were not reviewed.
- Enforced by: CONVENTION (review only)
- Audit check: each risky batch in docs/STATE.md names its review and the commit range reviewed.
- Source: .claude/CLAUDE.md:14-18
- Status: CURRENT

### CODE-PROC-10 — Verify a reviewer's finding against intent before accepting it
- Rule: Treat agent reports as evidence to check, not truth: re-read the lines behind every high-severity claim, and check git history and docs/STATE.md before accepting a "drift" or "duplication" finding; a fact can be right and the finding still wrong (the deliberate menu class tints).
- Why: during the 2026-11-12/13 audit the orchestrating session's own diagnosis was wrong twice (Egg Shell timing, the cargo chest) and was corrected by an agent that looked at the captured frames.
- Enforced by: CONVENTION (review only)
- Audit check: the findings ledger labels each finding "confirmed in code" or "reviewer's reading".
- Source: M:audit-method-that-landed.md:11-22
- Status: CURRENT

### CODE-PROC-11 — Commit one concern per commit, with the why in the body
- Rule: One concern per commit (no "fix bug + add feature + refactor"); the subject is the what, the body the why; a behaviour change found while refactoring is reported and committed on its own so it can be reverted alone. Agent contributions carry a `Co-Authored-By` trailer. Commit or push only when asked.
- Why: reviewable, revertible history.
- Enforced by: CONVENTION (review only)
- Audit check: `git log --stat -20` shows no commit mixing a refactor with a behaviour change; each agent commit ends with a `Co-Authored-By` line.
- Source: docs/CONVENTIONS.md:534-538
- Status: CURRENT (the trailer's model name is open question Q2)

### CODE-PROC-12 — Update docs/STATE.md after every change, and update docs in place
- Rule: After making changes update `docs/STATE.md` (and tick `docs/ROADMAP.md` when phases complete); update each doc in place when its concern changes; record final decisions in the repo docs and rewrite the GDD destructively rather than appending. The Notion mirror is retired (ruling D16, 2026-11-14); an Obsidian vault is an open option, not a rule.
- Why: so the next agent does not lose context; owner, design-reset thread 2026-07-25: "update the GDD, this time in a destructive way".
- Enforced by: CONVENTION (review only)
- Audit check: `git log -10 --stat` shows docs/STATE.md in every session's last commit.
- Source: CLAUDE.md:46, CLAUDE.md:3-5; design-reset thread 2026-07-25, playtest-balance thread 2026-08-24 (owner)
- Status: CURRENT

### CODE-PROC-13 — Republish the compendium whenever design docs or gameplay `.asset` values change
- Rule: If a session changes `docs/GDD.md`, `docs/ART.md`, class or ability `.asset` files or map/jump constants, update `docs/site/index.html` and republish to the same Artifact URL; its numbers come from the shipped `.asset` files and gameplay constants, "never from GDD prose".
- Why: owner, compendium thread 2026-09-04: "update it every time the docs are updated"; a stale site defeats its purpose.
- Enforced by: NONE
- Audit check: `git log --since=<last site commit> --name-only -- 'Assets/_Game/Data/**/*.asset' docs/GDD.md docs/ART.md` is empty, or a later commit touches `docs/site/`.
- Proposed guard: G-12 (section 4.4): generate the site's numbers block from the assets and diff it in a test.
- Source: CLAUDE.md:47, M:mechanics-compendium-site.md:21-40; compendium thread 2026-09-04 (owner)
- Status: CURRENT (unenforced and stale now: BL-158)

### CODE-PROC-14 — Branch fixes from `main` and features from `develop`
- Rule: Fixes for the released playtest build: `fix/<topic>` from `main`, merge into `main`, tag a patch, then merge `main` into `develop`. Features and progression: `feature/<topic>` from `develop`. Balance work: a branch from `main`, merged into `main`, then rebase `develop` onto `main`. Ask first: "is this a fix for the released build or new work?"
- Why: owner, release thread 2026-10-26: "Fixes for the main build will be done in branches coming from main".
- Enforced by: CONVENTION (review only)
- Audit check: `git merge-base --is-ancestor main <branch>` for a fix branch; `git branch --show-current` matches the type of work.
- Source: M:branching-model-v0-4-release.md:13-26; release thread 2026-10-26, README thread 2026-10-28, balance thread 2026-10-31 (owner)
- Status: CURRENT (recorded only in memory although that memory says root CLAUDE.md has a Branches section; Appendix A. Current work is on `simplify-abilities`, to be merged into `balance` per BL-162)

### CODE-PROC-15 — Agree the design before the plan; present designs and audits as interactive pages
- Rule: Present designs, plans and audits as an interactive page (separate views, widgets), not a long markdown plan; ask for plan approval only once the owner agrees the design; an audit uses real captured evidence, narrow parallel read-only lenses, verified synthesis and a decision page with one recommended option per decision, and nothing is implemented before signoff. A meta layer (progression) uses what gameplay exposes and does not modify or judge core systems.
- Why: owner, UI thread 2026-10-06: "Now THIS is an audit"; progression thread 2026-09-06: "you are getting ahead of what I want".
- Enforced by: CONVENTION (review only)
- Audit check: an audit or design hand-back links an Artifact page and lists open decisions.
- Source: M:design-review-working-style.md:11-14, M:audit-method-that-landed.md:11-22; UI thread 2026-10-06, progression thread 2026-09-06 (owner)
- Status: CURRENT

### CODE-PROC-16 — Treat recorded owner decisions as binding
- Rule: Decisions recorded as answered (UI rules settled 2026-06-11; 3D environment D1-D9; feedback and icon D1-D8, E1-E13; audit D1-D5) override earlier recommendations: "don't re-litigate them".
- Why: binding design answers.
- Enforced by: CONVENTION (review only)
- Audit check: a proposal that contradicts a recorded decision cites a new owner request.
- Source: docs/CONVENTIONS.md:514, M:env3d-decisions-2026-10-15.md:36, M:feedback-icons-decisions-2026-11-07.md:13, M:arch-audit-decisions-2026-11-12.md
- Status: CURRENT

### CODE-PROC-17 — Fix every audit finding, before and after the alpha
- Rule: All audit findings are fixed, pre- and post-alpha (D1); the retired ability classes wait for alpha feedback (D4a); assemblies come before the alpha (D5b). The architecture rulings themselves are ARCH-EVT-04 (ids-only events), ARCH-NET-07 (authority-confirmed transfers) and ARCH-LAY-01 (five assemblies).
- Why: owner, audit thread 2026-11-12: "Fix all A, pre and post alpha".
- Enforced by: CONVENTION (review only)
- Audit check: `grep "^- \[ \]" docs/BACKLOG.md` holds an item for every audit finding not yet fixed.
- Source: M:arch-audit-decisions-2026-11-12.md; audit thread 2026-11-12 (owner)
- Status: CURRENT

### CODE-PROC-18 — Park small findings in the backlog
- Rule: Small findings and deferred items go to `docs/BACKLOG.md` (one line per item, `BL-nnn`) through the `/backlog` skill, instead of being fixed on the side or forgotten.
- Why: owner, balance thread 2026-11-13: "Note the small things I'm /backlog".
- Enforced by: CONVENTION (review only)
- Audit check: every "not done" or "not verified" line in a docs/STATE.md entry has a matching `BL-` id.
- Source: CLAUDE.md:73; balance thread 2026-11-13 (owner)
- Status: CURRENT

### CODE-PROC-19 — Keep secrets and junk out of git
- Rule: `.gitignore` must keep build output, keystores and local agent config out of the repo; scan for exposed keys before the repo is public.
- Why: owner, first-test thread 2026-07-21: "ensure that gitignore is properly configured and no bullshit is uploaded".
- Enforced by: TEST `PlayStoreBuildTests.Keystores_AreGitIgnored`; rest CONVENTION
- Audit check: `git ls-files | grep -i -E "\.(jks|keystore|pem|p12)$|secret|apikey"` returns nothing; `git check-ignore Builds .claude` both match.
- Source: docs/CONVENTIONS.md:539, .gitignore:130; first-test thread 2026-07-21 (owner)
- Status: PARTIAL

### CODE-PROC-20 — Do not edit `.claude/CLAUDE.md` from an agent; propose the line instead
- Rule: An agent may not edit `.claude/CLAUDE.md`; it proposes the needed line in its hand-back.
- Why: the file is the owner's local config (git-ignored, `.gitignore:130`), so its edits leave no history.
- Enforced by: CONFIG (the permission system refuses the edit)
- Source: docs/STATE.md:118
- Status: CURRENT

### CODE-PROC-21 — On an unattended run, keep going without asking
- Rule: When the owner says he is away, do not stop to ask: move forward, record decisions and open questions in the report.
- Why: owner, test-session thread 2026-10-20: "Dont ask me anything as im going to be afk".
- Enforced by: CONVENTION (review only)
- Audit check: n/a (behavioural).
- Source: test-session thread 2026-10-20 (owner)
- Status: CURRENT (situational: applies when the owner says so)

## 3.9 DESIGN: balance and design constraints that bind code

### CODE-DESIGN-01 — Solve stats against the SCT axiom; never hand-tune them
- Rule: "Never pick a Cluck Wars stat because it 'feels right'." `MoveSpeed`, `CargoCapacity`, `PeckAmount`, `PeckCooldown` are dependent variables solved against the Solo Clear Time axiom (a naked Speedy alone wins in a fixed time via a few base trips; targets in `SctTargets`). To change a stat, run the Oracle; arena geometry, pile footprints, deposit radius and collect reach are inputs: if one changes, re-solve.
- Why: owner, design-reset thread 2026-07-24: create "a way to test this automatically with a bot"; hand-copied fixtures once hid +20% speed drift.
- Enforced by: TEST `BalanceOracleTests` (Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs, reads the real assets and `ShippedMap.OracleMap(seat)`, all four seats)
- Source: M:sct-axiom-is-the-balance-authority.md:11-31; design-reset thread 2026-07-24 (owner)
- Status: CURRENT

### CODE-DESIGN-02 — Keep the shipped SCT tolerance at ±0.5 s; never widen it
- Rule: The shipped-asset test uses `SctTargets.ShippedAssetToleranceSeconds` (±0.5 s); ±1.5 s is the GDD design band only. "If shipped values drift past ±0.5, re-solve; never widen." The Assassin is exempt (`SctTargets.Exempt`); passives sit outside the naked axiom.
- Why: proof that a corner moved 25.65 to 27 turns the test red at ±0.5.
- Enforced by: TEST `BalanceOracleTests` (uses the tolerance); rest CONVENTION
- Audit check: `git log -p -S ShippedAssetToleranceSeconds -- Assets/_Game/Scripts/Balance` shows no widening.
- Source: M:sct-axiom-is-the-balance-authority.md:42-47
- Status: PARTIAL (the constant's value is not pinned)

### CODE-DESIGN-03 — Trust `MatchConfig.asset` over any document for target and time
- Rule: `MatchConfig.asset` is the authority (40 food / 45 s at b7c2616e): "trust the asset over any doc". A winner is the first to bank the target, else the most banked at the timer.
- Why: the GDD and older docs carry stale numbers.
- Enforced by: TEST `DataIntegrityTests.MatchConfig_ValuesAreInTheirDocumentedRanges`, `DataIntegrityTests.MatchConfig_MaxPlayers_CoversTheFourPlayerDesign`
- Source: CLAUDE.md:11, Assets/_Game/Data/MatchConfig.asset:15-16
- Status: CURRENT

### CODE-DESIGN-04 — Leave numbers alone until the owner's test
- Rule: When the owner says a value waits for testing, do not retune it, and do not change a balance number he did not ask to change (an unrequested bounty change 5 to 8 was reverted).
- Why: owner, balance thread 2026-11-05: "No, wrong answer, watit for testing".
- Enforced by: NONE
- Audit check: `git log -p -- 'Assets/_Game/Data/**/*.asset'` for the batch: every changed numeric field is named in the owner's request or the agent's written task.
- Proposed guard: a "balance freeze" fixture: a test that hashes the gameplay numbers of the shipped assets and fails until the expected hash is updated in the same commit (making every number change explicit and reviewable).
- Source: balance thread 2026-11-05 (owner)
- Status: CURRENT (unenforced)

### CODE-DESIGN-05 — Keep it easy and forgiving: it is a children's game
- Rule: One ability per job (Peck, Steal, Stun, Shove, Slow, Slow Area, Speed, Jump, Shield, Hide, Bank, Death Mark), the same range for similar effects, generic names, no skill-gated mechanics (no skill shots, previews instead of lockouts).
- Why: owner, balance thread 2026-11-05: "this is still a game for children. I want it to be easy."
- Enforced by: TEST `AbilityTraitTests.TheShippedPool_HasOneAbilityPerJob` (the one-per-job half is ARCH-DATA-04); rest CONVENTION
- Audit check: a new mechanic proposal says how a child discovers and uses it without timing skill.
- Source: M:ability-merge-2026-11-05.md; balance thread 2026-11-05, UI thread 2026-10-08 (owner)
- Status: PARTIAL

### CODE-DESIGN-06 — Keep cooldowns in the bands: low 1-3 s, medium 4-6 s, high 7-8 s
- Rule: For a 45-second match no cooldown exceeds 8 s; bands are low 1-3, medium 4-6, high 7-8.
- Why: owner, balance thread 2026-11-05: "I would target low 1-3 medium 4-6 high 7-8".
- Enforced by: TEST `CooldownsKitsSlipperyTests.ShippedCooldowns_AreTheNewBands`
- Source: M:ability-merge-2026-11-05.md; balance thread 2026-11-05 (owner)
- Status: CURRENT

### CODE-DESIGN-07 — Make abilities targeted: they cannot be dodged, only counterplayed
- Rule: No skill shots: area around the caster or range-only selection; the caster's endpoint does not have the last word, the host validates with tolerance (the RPC mechanics: ARCH-NET-04, ARCH-NET-05, ARCH-NET-10).
- Why: owner, design-reset thread 2026-07-24: "targeted casting instead of skill shot and that abilities cannot be dodged".
- Enforced by: CONVENTION (review only); the receiver-side validation is TEST-guarded (ARCH-NET-10)
- Audit check: a new ability's `OnActivate` selects victims by range from the caster, never by a projectile path.
- Source: docs/GDD.md; design-reset thread 2026-07-24 (owner)
- Status: CURRENT

### CODE-DESIGN-08 — Pressing previews the range, releasing casts; holding never freezes the chicken
- Rule: Pressing an ability always shows its range until release; release casts; never block an ability because nothing is in range (no target is not illegal); holding never stops the chicken; a cancel must exist and be visible.
- Why: owner, UI thread 2026-10-08: "if the user press the ability it previews the range until it releases"; balance thread 2026-10-31: holding "currently stops the chicken from moving. That should not happen".
- Enforced by: TEST `Phase6Chunk5Tests.HoldingAnAbility_NeverStopsThePlayersMovement_SourceGuard`, `AbilityPreviewRulesTests.LocalPreview_SingleHeldAvailableSlot_IsThatSlot`; rest CONVENTION
- Audit check: for a new gate in `AbilityHoldStateMachine.Decide` / `CanActivate`, confirm it refuses only a wiring failure or a rule (cooldown, base wall), not "nobody in range", and that the press still previews.
- Source: M:ability-input-press-previews.md; UI thread 2026-10-08, balance thread 2026-10-31 (owner)
- Status: PARTIAL (the "visible cancel" and "never block out of range" halves are review-only; the Slow is gated with `RequiresEnemyInRange` per docs/STATE.md:13, see open question Q5)

### CODE-DESIGN-09 — Let no ability cross a base wall; never let a shielded chicken be killed
- Rule: Base walls block every ability; Turtle / Egg Shell chickens are unkillable by design (tell the owner where that does not apply).
- Why: owner, balance thread 2026-10-31: "No abilities can cross base wall."; 2026-11-03: "cannot be killed by design".
- Enforced by: TEST `SimplifiedAbilitiesTests.TheBaseWall_IsStillAskedForEveryTarget_SourceGuard`; rest CONVENTION
- Audit check: a new targeting path calls the base-wall check (`git grep -n "WallBetween\|BaseWallRules.Blocks" HEAD -- Assets/_Game/Scripts/Abilities`).
- Source: balance thread 2026-10-31 and 2026-11-03 (owner)
- Status: PARTIAL (whether the wall also blocks across the caster's own base is BL-152)

### CODE-DESIGN-10 — Steal the minimum of the ability's amount, the thief's free room and the victim's cargo
- Rule: A steal takes min(ability value, thief's missing cargo, victim's cargo); no extra victim immunity windows, natural caps only.
- Why: owner, design-reset thread 2026-07-24.
- Enforced by: TEST `StealFromThiefTests` (Assets/_Game/Scripts/Editor/Tests/StealFromThiefTests.cs, `StealMath.Clamp`)
- Source: design-reset thread 2026-07-24 (owner)
- Status: CURRENT

### CODE-DESIGN-11 — Decide a jump by its length, not obstacle height; Speedy never jumps
- Rule: All jumps are teleports: the engine checks whether the jump length clears walls or piles (with tolerance), else stops at the nearest point. Speedy gets no jump.
- Why: owner, arena thread 2026-07-28; playtest-balance thread 2026-08-21: "speedy should never have jumps".
- Enforced by: TEST `SimplifiedAbilitiesTests.EveryKitAbility_IsAllowedForItsClass_AndJumpIsNeverSpeedys`; rest CONVENTION
- Audit check: jump traversal changes are play-checked (BL-157 lists Blink traversal as unverified).
- Source: arena thread 2026-07-28, playtest-balance thread 2026-08-21 (owner)
- Status: PARTIAL (the length rule is review-only)

### CODE-DESIGN-12 — Keep chickens inside the boundary in code, not by trusting colliders
- Rule: Chickens always spawn inside the boundary and cannot exit; containment is enforced in code, not left to Unity physics colliders.
- Why: owner, playtest-balance thread 2026-08-19: "dont trust unity physics collider".
- Enforced by: TEST `SpawnSafetyTests.EverySpawnPoint_IsInsideTheBoundary_WithRoomForTheBody` (spawn points only); rest CONVENTION
- Audit check: a movement or traversal change keeps a code-side clamp to the arena bounds.
- Source: playtest-balance thread 2026-08-19 (owner)
- Status: PARTIAL (escape after spawn is unguarded)

### CODE-DESIGN-13 — Keep fixed kits behind `LoadoutMode`; keep the picking code
- Rule: Classes use fixed preselected kits for the demo (`LoadoutMode.Fixed`); the free-picking code stays, behind the switch. Ability SOs carry a flags enum of the classes that may equip them.
- Why: owner, balance thread 2026-10-31: "Keep code as is, and put some rule there to keep it flexible".
- Enforced by: TEST `FixedKitTests` (Assets/_Game/Scripts/Editor/Tests/FixedKitTests.cs), `CooldownsKitsSlipperyTests.TheSwappedKits_StayLegal`, `SimplifiedAbilitiesTests.EveryKitAbility_IsAllowedForItsClass_AndJumpIsNeverSpeedys`
- Source: M:fixed-kits-baseline-2026-10-31.md; balance thread 2026-10-31, playtest-balance thread 2026-08-21 (owner)
- Status: CURRENT (the exact kit slot order is unpinned, BL-156; the null-config fallback is Free, BL-153)

### CODE-DESIGN-14 — One icon means one effect: Slow and Slow Area are separate jobs
- Rule: Slow (instant, timed) and Slow Area (aura or placed zone, chosen by `AreaKind` on the SO) are separate jobs; both shapes stay implemented; a new slow-like effect needs its own job.
- Why: owner, balance thread 2026-11-13: "slow (instant cast, 3s) and slow area (should last longer than instant...)".
- Enforced by: TEST `SlowAbilityTests` (Assets/_Game/Scripts/Editor/Tests/SlowAbilityTests.cs)
- Source: M:slow-split-2026-11-13.md; balance thread 2026-11-13 (owner)
- Status: CURRENT (not play-checked, BL-160)

### CODE-DESIGN-15 — Respect the locked 3D-environment and feedback decisions
- Rule: The 3D environment decisions D1a-D9a (matte plus lighting, the new mound, all outer fences fade for any chicken, full surround art, PC post ON, pile jitter 0) and the feedback/icon decisions D1-D8 and E1-E13 are binding as recorded.
- Why: the owner's answers on the audit pages; "don't re-litigate them".
- Enforced by: CONVENTION (review only)
- Audit check: a visual change that touches one of these areas cites the decision it keeps.
- Source: M:env3d-decisions-2026-10-15.md:11-36, M:feedback-icons-decisions-2026-11-07.md:13-43
- Status: CURRENT

### CODE-DESIGN-16 — Hold 60 fps on Windows and a stable 30 fps on mid-range Android
- Rule: Target 60 fps on Windows and 30 fps stable on mid-range Android (2021+); fixed tick 30/s; any device can host in Shared Mode.
- Why: mobile is first-class.
- Enforced by: NONE (no frame-time test; lab captures measure draw calls by hand)
- Audit check: per-frame code (Update, LateUpdate, Render) allocates nothing and does no scene search (`SceneObjectAccessTests.NoPerFrameMethod_SearchesTheScene` covers the search half).
- Proposed guard: a dev-tools probe that logs p95 frame time per session and a `verify.ps1` threshold on PC builds.
- Source: CLAUDE.md:18, docs/TDD.md:407-415
- Status: CURRENT (unenforced)

### CODE-DESIGN-17 — Ship the dev tools in builds behind Developer Mode, off by default
- Rule: The Ability Lab ships in player builds so it can be tested on the phone, gated by Developer Mode (default off; `MenuUiController.OpenAbilityLab` refuses even if the row is shown); because it ships it is held to shipped-code standards (`ILogService`, self-injection, no `Debug.Log`).
- Why: owner, ability-lab thread 2026-09-04: "Keep it to in builds ... Bound it to a develop setting".
- Enforced by: TEST `DeveloperModeTests.DeveloperMode_DefaultsToOff_AndAnExplicitChoiceReachesDisk`, `ProjectConfigTests.AbilityLabShips_SoDeveloperModeMustDefaultOff`, `RuntimeSourceGuardTests.NoRuntimeFile_CallsDebugLog_OutsideTheLoggingService`
- Source: docs/TESTING.md:391-403; ability-lab thread 2026-09-04 (owner)
- Status: CURRENT

### CODE-DESIGN-18 — Keep the retired ability classes until alpha feedback
- Rule: The retired ability classes stay in the code until alpha feedback decides (D4a); do not delete them in a cleanup.
- Why: owner decision D4a, audit thread 2026-11-12.
- Enforced by: CONVENTION (review only)
- Audit check: a diff deleting a `*AbilitySO.cs` cites an owner request.
- Source: M:arch-audit-decisions-2026-11-12.md; audit thread 2026-11-12 (owner); same rule as ARCH-DATA-13
- Status: CURRENT

## 3.10 FOOT: footguns (real bugs hit, with the avoidance)

Footguns that are layering, networking or injection traps live in ARCHITECTURE_RULES.md: raw `GetPlayerRtt` reading 0 in Shared Mode (ARCH-NET-06), `ProjectContext.HasInstance` (ARCH-DI-05), Fusion not re-weaving after a config-only change (ARCH-LAY-06), exact-boundary `Planar` maths (ARCH-DATA-07). Spawn-collision drift (docs/CONVENTIONS.md:411-413) and a returning Zenject `OptionalExtras` folder (docs/CONVENTIONS.md:385-387) have no rule in either document yet. Compile-time traps (`LogLevel`, `Assert`, `ChickenClass : byte`, `const` platform enums) are CODE-STYLE-04..07.

### CODE-FOOT-01 — When "the chicken doesn't move", check the camera and the stats first
- Rule: First check whether the camera is following (the closer `MatchCamera._orthoSize`, the less screen motion); then whether `ChickenController.Spawned` resolved `_activeStats` from `ChickenClassRegistrySO` or the prefab's `_fallbackStats` (both null: `_movement` is never created; watch the verbose log at FUN entry).
- Why: both were hit in testing and looked like movement bugs.
- Enforced by: CONVENTION (review only)
- Audit check: n/a (diagnosis order).
- Source: docs/CONVENTIONS.md:403-409
- Status: CURRENT

### CODE-FOOT-02 — Write C#, Python and paths with the Write tool, not shell heredocs
- Rule: On this Windows machine write source files with the editor tools (Write/Edit), not Bash heredocs; do not use `sed` with `\c...` in a replacement (GNU sed turns `\cl` into a control character): use Python for path edits.
- Why: heredocs mangle backslashes and quotes in C# and Python.
- Enforced by: CONVENTION (review only)
- Audit check: `git diff --check` and a compile; look for stray control characters (`grep -P "[\x00-\x08]"`) in changed files.
- Source: M:test-session-2026-10-20-learnings.md:18 (sed); the heredoc half: the 2026-11-12/13 architecture audit; ratified as a rule by Maestro on 2026-11-14 (ruling D13)
- Status: CURRENT

### CODE-FOOT-03 — Give every PC client its own `-cw_profile`; restart Android with `am start -S`
- Rule: Each EXE on one PC gets its own `-cw_profile` (a shared anonymous UGS identity makes the second join fail with `LobbyConflict 16003`, "player is already a member of the lobby"); on Android always `am start -S` because intent extras are read once per process.
- Why: both broke multi-client sessions.
- Enforced by: CONVENTION (review only); `tools/run-clients.ps1` passes distinct profiles
- Audit check: the client command lines in a session report show distinct `-cw_profile` values.
- Source: docs/TESTING.md:490-493, M:autoplay-never-fights.md:8-12, M:test-session-2026-10-20-learnings.md:17
- Status: CURRENT

### CODE-FOOT-04 — Expect a black Game-view capture when the Editor is unfocused
- Rule: Game view captures go black when the Editor window is not focused; a black capture or a missing camera after an MCP test run is CODE-BUILD-05, not a regression.
- Why: hit repeatedly during UI verification.
- Enforced by: CONVENTION (review only)
- Audit check: n/a (capture step).
- Source: M:mcp-tests-run-leaves-empty-scene.md:17
- Status: CURRENT

### CODE-FOOT-05 — Remember that tests and dev builds write PlayerPrefs and the registry
- Rule: Dev-tools builds set Developer Mode and tests write PlayerPrefs (on Windows, the registry); reset them before judging a "fresh install" behaviour, and delete registry keys with PowerShell, not the sandboxed shell.
- Why: not stated beyond being listed among the failure modes met during the 2026-11-12/13 audit.
- Enforced by: CONVENTION (review only)
- Audit check: a default-behaviour check states the prefs were cleared first.
- Source: Assets/_Game/Scripts/Editor/Tests/DeveloperModeTests.cs:49; the rest: the 2026-11-12/13 architecture audit; ratified as a rule by Maestro on 2026-11-14 (ruling D13)
- Status: CURRENT

### CODE-FOOT-06 — Capture short visual windows with ffmpeg, not homemade burst scripts
- Rule: For 0.1 s visual windows use a 60 fps `ffmpeg` `gdigrab` of the game window and the project's own capture tools; antivirus blocks homemade burst-capture scripts.
- Why: antivirus blocks the homemade scripts (failure mode met during the 2026-11-12/13 audit).
- Enforced by: CONVENTION (review only)
- Audit check: n/a (tool choice).
- Source: the 2026-11-12/13 architecture audit; ratified as a rule by Maestro on 2026-11-14 (ruling D13)
- Status: CURRENT

### CODE-FOOT-07 — Delete a stray legacy EventSystem; let `UiElements` create the right one
- Rule: A stray `EventSystem` with the legacy `StandaloneInputModule` is a no-op under the new Input System (touch never reaches the UI): delete it; the runtime EventSystem comes from `UiElements.EnsureEventSystem()` with `InputSystemUIInputModule`.
- Why: touch input not reaching the UI.
- Enforced by: CONVENTION (review only)
- Audit check: `git grep -n "StandaloneInputModule" HEAD -- Assets/_Game/Scenes Assets/_Game/Prefabs` returns nothing.
- Source: docs/TESTING.md:551, docs/CONVENTIONS.md:267
- Status: CURRENT (the TESTING.md line names `TouchControlsHud`, which no longer exists; Appendix A)

## 4. Worked example: numbers in ability text come from the ability's data (CODE-TEXT-06, CODE-TEXT-10)

This is the owner's own example of a rule that can rot: "ability numbers used in description must be taken from the ability values but we have no way to ensure that is done in the future" (audit thread, 2026-11-13). Below is what holds today, what does not, and the guard for each hole.

### 4.1 The rule and its history

- **2026-10-06, UI thread (owner):** wording may change, "but note that it requires to be updated if the values change in future sessions (percentages, etc)".
- **2026-10-09, commit `3fab0823`:** active descriptions templated like the perk lines: `AbilityBaseSO.DescriptionKey` / `DescriptionArgs()` / `DescriptionText`, the `Num()` (one decimal) and `Pct()` formatters, `ability.*.desc` rows in `UiText.csv`, and `AbilityDescriptionTests` including `Templates_ContainNoHandTypedNumbers`. Passives already used `PassiveAbilitySO.PerkLineKey` / `PerkDetailKey` / `PerkArgs(match)` under `PerkTextTests`.
- **2026-11-13, balance thread:** a conformance audit found two texts that misled (the Hunt text quoted the 6.2 m kill range where the Stun reaches 5.4 m; the Slow text read as a lasting slow). Owner ruling: "Text should use the values in SO variables to be always updated." Simplest words, mention the second press.
- **2026-11-13 20:28, commits `2341c869` (texts rewritten) and `b404d8ac` (tests):** `AbilityDescriptionTests.EveryRegisteredAbilityAndPassive_TemplatesHoldNoLiteralDigits_AndFillEveryPlaceholder` extends the no-digit check to every ability in `AbilityRegistrySO` and to every passive's line and detail template. The owner's "we have no way to ensure" message came a few minutes later, in another thread; he had most likely not seen this guard.

### 4.2 What is enforced today (read at b7c2616e)

Code path: `AbilityBaseSO.DescriptionText` (Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs:180-190) renders `UiText.Format(DescriptionKey, DescriptionArgs())`, falling back to the raw `Description` field (reported once) only when `DescriptionKey` is null. `PassiveAbilitySO.PerkLine` / `PerkDetail` (Assets/_Game/Scripts/Abilities/PassiveAbilitySO.cs:98-104) render `PerkLineKey` / `PerkDetailKey` with `PerkArgs(match)`. Templates: `Assets/_Game/Resources/Text/UiText.csv:50-95`.

| A typo or drift of this kind... | ...fails this test (Assets/_Game/Scripts/Editor/Tests/) |
|---|---|
| A digit typed into an active template | `AbilityDescriptionTests.Templates_ContainNoHandTypedNumbers` (actives under `TestAssets.AbilitiesDir`) and the registry-wide guard `EveryRegisteredAbilityAndPassive_TemplatesHoldNoLiteralDigits_AndFillEveryPlaceholder` (strips `{name}` tokens, fails on any `\d`) |
| A digit typed into a perk line or detail template | the registry-wide guard (every registered passive's `PerkLineKey` and `PerkDetailKey`) |
| A placeholder the fields do not supply, or an unfilled brace | the registry-wide guard (`CheckTemplate`, AbilityDescriptionTests.cs:204-215) |
| A shown number that is not an argument, or an argument the text never shows | `AbilityDescriptionTests.EveryNumberInTheRenderedText_IsAnArgument_AndEveryArgumentIsUsed` (multiset compare) |
| An argument equal to no field of the asset | `AbilityDescriptionTests.EveryArgument_IsARealFieldValue_ToDisplayPrecision` (public float/int fields, their x100, `IndicatorRange`, `AimRadius`, Hunt's computed `LiveSeconds`; tolerance 0.05) |
| A stale `Description` mirror on an active `.asset` | `AbilityDescriptionTests.AssetDescriptionField_IsTheRenderedText` |
| A new active with no template | `AbilityDescriptionTests.EveryShippedActiveAbility_HasATemplate_ThatRendersCleanAndFits` (non-null key, exact count 30) and the registry guard (`Assert.IsNotNull(key)`) |
| A perk line or detail that disagrees with its fields | `PerkTextTests.PerkLines_CarryTheNumbersTheAssetFieldsHold` (expected text computed independently from the NAMED fields; unknown passive hits `Assert.Fail`, PerkTextTests.cs:54-57; exact count 8) |
| A passive with no perk line | `PerkTextTests.EveryShippedPassive_HasAPerkLine_WithNoUnresolvedPlaceholder` |
| Text not following a field change | `AbilityDescriptionTests.ChangingAField_ChangesTheText_WithoutTouchingAnyWording`, `AbilityDescriptionTests.SlowAndSlowArea_TextsReadTheirOwnFields`, `PerkTextTests.ChangingAField_ChangesTheText_WithoutTouchingAnyWording` |
| Passive raw `Description` prose: Spoiler, Bully, Relentless, Slippery, Featherfoot | pinned by equality: `PerkTextTests.SpoilerAsset_Description_IsTheDetailTemplate_WithTheCurrentNumbers`, `PerkTextTests.BullyAsset_Description_IsTheLinePlusTheDetail_WithTheCurrentNumbers`, `PerkTextTests.RelentlessAsset_Description_IsTheDetail_WithTheCurrentNumbers`, and `AreEqual` lines inside `PerkTextTests.PassiveDescriptions_ThatQuoteNumbers_QuoteTheCurrentOnes` |

So for **active abilities and perk lines** the answer to "would a test fail if someone typed a number?" is yes, and a field change can never leave stale text behind.

### 4.3 The remaining gaps, precisely

1. **Passive `Description` prose is typed by hand, and three passives are only spot-checked.** `Assets/_Game/Data/Abilities/Passives/Hoarder.asset:17` holds "Carries at least 40 food"; `PassiveAbilitySO.DescriptionText` (Assets/_Game/Scripts/Abilities/PassiveAbilitySO.cs:82) returns that field as is. `PerkTextTests.PassiveDescriptions_ThatQuoteNumbers_QuoteTheCurrentOnes` checks it with a `switch` per passive type that has **no default arm** (PerkTextTests.cs:227-267), and for Hoarder, Thief and Bulwark only with `StringAssert.Contains`: the current number must appear, but an extra stale number beside it passes, and a ninth passive's prose is not checked at all (the count assert of 8 forces someone to edit the test, nothing forces a Description row). Exposure today is latent: the only runtime reader of `DescriptionText` is the gear-up detail strip (Assets/_Game/Scripts/UI/MenuUiController.GearUp.cs:514), whose pool excludes passives (MenuUiController.GearUp.cs:389), and the pick-bird screen shows `PerkLine` / `PerkDetail` (Assets/_Game/Scripts/UI/MenuUiController.PickBird.cs:258-268); docs/STATE.md:16 also says the prose is "not shown to players". Any new reader of `DescriptionText` on a passive would show hand-typed numbers.
   - **Guard G-03 (S):** make `PassiveAbilitySO.DescriptionText` return the rendered perk text (line + detail) and add `PerkTextTests.EveryRegisteredPassive_DescriptionIsItsRenderedPerkText` asserting `p.Description` equals one of `PerkLine`, `PerkDetail` or `PerkLine + " " + PerkDetail` for EVERY registered passive (fails on an unknown type by construction). Shipped prose already has those three shapes.
2. **A placeholder is not bound to the field it names.** `EveryArgument_IsARealFieldValue_ToDisplayPrecision` accepts a match with ANY public field (or its x100) within 0.05. A template whose `{r}` reads the wrong range (the Hunt class of error) passes whenever the wrong value equals some field, and the only defence is `AbilityDescriptionTests.PinnedNumbers_MatchTheShippedAssets` for three abilities. Perk lines do not have this gap (their expectation names the field).
   - **Guard G-02 (M):** change `DescriptionArgs()` entries to carry the source field (`("r", nameof(StunRadius), Num(StunRadius))` or an attribute), and assert by reflection that each argument equals THAT field (or its x100 for a `Pct`). One-time edit of the 30 active classes.
3. **Csv rows outside the ability and perk keys hard-code numbers.** A `python -I` scan of `UiText.csv` (strip `{...}`, find `\d`) lists ten rows at b7c2616e: `main.subtitle` "4-PLAYER FARMYARD FREE-FOR-ALL" (UiText.csv:3), `main.soloHint` "you vs 3 CPU chickens" (:4), `btn.pickMore.one` "PICK 1 MORE" (:21), `lobby.status.solo` "Solo · 3 CPU" (:146), `lobby.hint.solo` "3 CPU rivals are waiting" (:148), `postmatch.tiedKos` "Tied 1st" (:186), `hud.rank.1..4` (:202-205). Ranks and "PICK 1 MORE" are ordinals; "3 CPU" restates the length of `ChickenClasses.SoloBotRoster` (Assets/_Game/Scripts/Gameplay/ChickenClassInfo.cs:131) and "4-PLAYER" restates `MatchConfigSO.MaxPlayers` (default 4, Assets/_Game/Scripts/Gameplay/MatchConfigSO.cs:69). Nothing fails if either changes.
   - **Guard G-06 (S):** `UiTextTests.PlayerFacingText_HasNoLiteralDigits_OutsideAllowList`: every csv value, `{...}` stripped, holds no digit unless its key is on an explicit allow-list (`hud.rank.*`, `btn.pickMore.one`, `postmatch.tiedKos`), with the message "use a placeholder fed from the SO or MatchConfig"; convert the "3 CPU" rows to `{bots}` from `SoloBotRoster.Length` and the subtitle to `{n}` from `MaxPlayers`.
4. **The rule was not written where agents look.** It lived in tests, code comments, docs/STATE.md:16 and memory, not in docs/CONVENTIONS.md or a CLAUDE file. This document (CODE-TEXT-06, CODE-TEXT-10) is now its written home; a one-line pointer in docs/CONVENTIONS.md is owed.
5. **Nothing runs these tests automatically.** There is no git hook and no test CI (`.github/workflows/pages.yml` only deploys `docs/site`). Every guard above bites only when someone runs the EditMode suite (CODE-TEST-01, gap G-10).

### 4.4 The general rule: every place a number reaches a player

| Surface | Where its numbers come from | Enforced? | How to enforce |
|---|---|---|---|
| Ability detail strip (gear-up) | `DescriptionText` templates from SO fields | TEST (section 4.2); wrong-field gap | G-02 |
| Perk line and detail (pick bird) | `PerkArgs` from SO fields + `MatchConfig` | TEST | none needed |
| Passive raw `Description` | hand-typed prose in `.asset` | PARTIAL, latent | G-03 |
| Ability card cooldown word (SHORT / MED) | literal threshold `ab.Cooldown <= 6f` (MenuUiController.GearUp.cs:508) | NONE | name the threshold once (the medium band's ceiling) and pin it beside `CooldownsKitsSlipperyTests.ShippedCooldowns_AreTheNewBands`; covered by G-09 |
| Ability card slot badge | `(slot + 1).ToString()` (ordinal) | n/a | exempt |
| MATCH SETTINGS card (lobby, overlays) | `MatchConfig.asset` through `MatchSettingsText` | TEST `LobbyMatchSettingsTests.MatchSettingsText_RendersTheLiveConfig_InTheCardsFormat`, `LobbyMatchSettingsTests.LobbyUxml_SettingValues_AreLeftBlankForTheControllerToFill`, `LobbyMatchSettingsTests.MatchOverlaysUxml_SettingValues_AreBlank_AndBoundFromTheLiveConfig` | none needed |
| HUD top bar (timer, banked food, target) and comeback banner "FINAL {n}!" | live state (`GameManager.ComebackEventSecondsLeft`, Assets/_Game/Scripts/UI/MatchHud.cs:216-217) | literals banned in three files only (CODE-TEXT-03) | G-01, G-07 |
| Lobby player count | `("n", 1)` literal for a host alone (Assets/_Game/Scripts/UI/MenuUiController.Coop.cs:460), a fact | NONE | allow-list entry in G-07 |
| Menu and lobby copy ("4-PLAYER", "3 CPU") | typed into csv | NONE | G-06 |
| Ranks, "PICK 1 MORE", "Tied 1st", touch slot badges "1".."4" (Assets/UI/TouchControls.uxml:114) | ordinals | n/a | allow-list |
| Ability Lab labels | live values in format strings (Assets/_Game/Scripts/AbilityLab/AbilityLabPanel.cs:448) | n/a | exempt (dev tool) |
| Class taglines | qualitative by design | n/a | exempt (CODE-TEXT-11) |
| Compendium `docs/site/index.html`, `docs/site/mechanics.html` | hand-maintained; rule: from the shipped `.asset` files, never GDD prose (CLAUDE.md:47) | NONE (stale now: BL-158) | CODE-PROC-13; proposed guard G-12: an editor script emits the numbers block from the assets and a test diffs the committed site against it |
| `docs/GDD.md`, `docs/balance/human-test-baseline.md` | hand prose; the GDD carries known-stale figures (CLAUDE.md:47) | NONE | not a player surface; the asset wins (CODE-DESIGN-03); audit numbers when docs change |

## 5. Test pattern catalogue

The suite is EditMode only: zero `[UnityTest]` methods, no Play-mode tests, no `NetworkRunner`. Anything that needs a runner becomes a pure rule tested directly plus a source guard on the wiring. Paths below are under `Assets/_Game/Scripts/Editor/Tests/` unless written in full.

| # | Pattern | Use when | Helper and example | Cannot catch |
|---|---|---|---|---|
| P1 | **Pure `*Rules` static, tested without a runner** | A decision a NetworkBehaviour makes (who wins a claim, how much food moves, which chicken is local) | Put the decision in a static pure class (`ComebackRules`, `FoodTransferRules`, `LocalChickenRules`, `RoundInvariants`), test it directly, and pin the behaviour's call to it with a source guard: Assets/_Game/Scripts/Editor/Tests/ComebackRulesTests.cs:18-38, Assets/_Game/Scripts/Editor/Tests/LocalChickenRulesTests.cs:16-47 | Wiring at spawn, replication order, timing across peers: needs CODE-TEST-19 live runs |
| P2 | **Source guard over a class** (`TestSources.ReadClass`) | "This must not reappear" / "only this file may do X" about a class that may be split into partials | `TestSources.ReadClass(path)` returns the file plus `Name.*.cs` siblings: Assets/_Game/Scripts/Editor/Tests/TestSources.cs:32; used at Assets/_Game/Scripts/Editor/Tests/ChickenLocalEventsTests.cs:443 | Code outside the named files; semantics (a renamed call slips past a regex). Blind spot: `UiTextTests` reads `MenuUiController.cs` with `File.ReadAllText` (UiTextTests.cs:209-210, :236), so the six partials are unscanned (G-01) |
| P3 | **Runtime-wide source scan** | A project-wide ban (`Debug.Log`, `Find*`, container injection, raw RTT) | `RuntimeSourceGuardTests.RuntimeFiles()` and `.Code(path)` (comments and string literals stripped): Assets/_Game/Scripts/Editor/Tests/RuntimeSourceGuardTests.cs:18-29; used at Assets/_Game/Scripts/Editor/Tests/RpcClaimTests.cs:128-131 | Calls built by reflection or string; banned behaviour written in a different form |
| P4 | **One-method body guard, gate before write** | An authority or sender check must precede the write in one method | `AssassinExecuteCreditTests.MethodBody(path, signature)` (brace matching) then `IndexOf(gate) > 0` and `IndexOf(write) > gate`: Assets/_Game/Scripts/Editor/Tests/AssassinExecuteCreditTests.cs:327, Assets/_Game/Scripts/Editor/Tests/ComebackRulesTests.cs:101-115 | A gate that is present but wrong (inverted condition); a write in a helper the method calls |
| P5 | **Allow-list with counts plus a staleness test** | A ban with justified exceptions | Dictionary file -> (count, reason) and a test that each count is still exact: Assets/_Game/Scripts/Editor/Tests/SceneObjectAccessTests.cs:96-130, Assets/_Game/Scripts/Editor/Tests/AbilityTraitTests.cs:137-145 | A new exception added to the list without review (the list is code; review the diff) |
| P6 | **Anti-vacuity assertion and regex self-test** (CODE-TEST-13) | Every scan | `Assert.Greater(scanned, N)` and a test that the regex matches the forbidden sample and not the legal one: Assets/_Game/Scripts/Editor/Tests/RuntimeSourceGuardTests.cs:43, Assets/_Game/Scripts/Editor/Tests/RuntimeSourceGuardTests.cs:65, Assets/_Game/Scripts/Editor/Tests/WeavingGuardTests.cs:80 | Forbidden forms the sample set did not imagine |
| P7 | **Equivalence: encode the old mapping in the test once and compare for every value** | Replacing a switch, a table, a type check or a trait with a new structure | Write the replaced mapping once inside the test and assert the new one agrees for every enum value or asset: Assets/_Game/Scripts/Editor/Tests/StatusEffectTableTests.cs:240-280, Assets/_Game/Scripts/Editor/Tests/ChickenClassTableTests.cs:72, Assets/_Game/Scripts/Editor/Tests/AbilityTraitTests.cs:61 | A bug that was already in the old mapping (the test freezes it) |
| P8 | **Totality** | A table keyed by an enum | Iterate `Enum.GetValues`, require a row or an explicit "no counterpart" marker: Assets/_Game/Scripts/Editor/Tests/StatusEffectTableTests.cs:33-96, Assets/_Game/Scripts/Editor/Tests/ChickenClassTableTests.cs:183, Assets/_Game/Scripts/Editor/Tests/MissingSfxTests.cs:91 | Whether the row's content is right |
| P9 | **Ordinal pin, append only** | Any enum serialized in an `.asset` or sent on the wire | `Pin<T>((value, ordinal)...)`: Assets/_Game/Scripts/Editor/Tests/EffectWindowTests.cs:88-93; byte backing at Assets/_Game/Scripts/Editor/Tests/ContractsAndEnumsTests.cs:33 | An enum nobody added to a pin (`MatchEventKind` beyond zero, `ComebackGrant`, `CastArchetype`, `SfxId`: G-13) |
| P10 | **Reflection test** | Payload shape, setter visibility, attribute presence, weaving | `ChickenLocalEventsTests.TheCastEvent_HoldsNoAssetReference_OnlyIdsEnumsAndNumbers` (Assets/_Game/Scripts/Editor/Tests/ChickenLocalEventsTests.cs:250), `ComebackRulesTests.Flag_SetterIsPrivate` (ComebackRulesTests.cs:43), private seams invoked at Assets/_Game/Scripts/Editor/Tests/ContractsAndEnumsTests.cs:186-205 | Behaviour; types in assemblies the scan does not load |
| P11 | **Shipped-asset scan** | Data integrity: every asset complete, in range, wired | `TestAssets.Load<T>` / `LoadAllIn<T>` / `SceneFloat` / `PrivateField` with a clear "not found" failure: Assets/_Game/Scripts/Editor/Tests/TestAssets.cs:78-169; arena geometry from `ShippedMap` (Assets/_Game/Scripts/Editor/Tests/ShippedMap.cs:23), never restated literals | Values that are in range but wrong for design (that is CODE-DESIGN-01's Oracle) |
| P12 | **YAML / GUID asset scan** | Missing scripts, references that do not resolve | Prefab load plus YAML regex plus `MonoScript.GetClass`: Assets/_Game/Scripts/Editor/Tests/MissingScriptsTests.cs:23-57 | A moved serialized field (value lost, script fine) |
| P13 | **Assembly layout and weaving guards** | Any change to asmdefs, folders or the weave list | `AssemblyLayoutTests` (folder -> assembly, pinned references, compiled references, weave list) and `WeavingGuardTests` (weaver attributes matched by type name): Assets/_Game/Scripts/Editor/Tests/AssemblyLayoutTests.cs:132-255, Assets/_Game/Scripts/Editor/Tests/WeavingGuardTests.cs:93-117. Rules: ARCH-LAY-01 to ARCH-LAY-06 | Batch mode not re-weaving after a config-only change (delete `Library/Bee`) |
| P14 | **Recording fakes** | Assert something is reported once, not swallowed and not flooded; drive audio or input consumers without devices | A private nested recording `ILogService` per test file (Assets/_Game/Scripts/Editor/Tests/MissingSfxTests.cs:16, Assets/_Game/Scripts/Editor/Tests/SelfInjectTests.cs:26); fake `IAudioService` (Assets/_Game/Scripts/Editor/Tests/AudioRegistryTests.cs:24) and `IInputProvider` (Assets/_Game/Scripts/Editor/Tests/ServicesAndInputTests.cs:163); `LogAssert.Expect` when no logger is bound (Assets/_Game/Scripts/Editor/Tests/LogErrorFormatTests.cs:20-29) | Anything through `INetworkService`: no fake exists |
| P15 | **Registry seeding** | A test needs a never-spawned chicken in a private registry | `RegistryTestAccess.Register/Unregister` (reflection, the only allowed path): Assets/_Game/Scripts/Editor/Tests/RegistryTestAccess.cs:12-23 | Lifecycle (Spawned/Despawned ordering) |
| P16 | **Text-follows-data test** | Any text with numbers | Change a field on a `CreateInstance` copy and assert the rendered text changes with no wording edit: `AbilityDescriptionTests.ChangingAField_ChangesTheText_WithoutTouchingAnyWording` (Assets/_Game/Scripts/Editor/Tests/AbilityDescriptionTests.cs:129-146) | A placeholder bound to the wrong field when the two values happen to agree (G-02) |
| P17 | **Revert the fix and confirm the test fails** (CODE-TEST-03) | Every new guard or bug-fix test | Not mechanised: revert the runtime change (or remove the config line) in a scratch tree, run the one test, see red, restore. Recorded for `WeavingGuardTests` (docs/CONVENTIONS.md:495) and the audit's A04, R-1, R-2 | Nothing, if done; skipped, a guard may test nothing |
| P18 | **Confirm the result file before trusting green** (CODE-TEST-15) | Every suite run | A failed compile writes NO result XML and the runners report the old assemblies green: check `EditorUtility.scriptCompilationFailed`, that the XML exists, is newer than the change and contains the new fixture names, and the count | n/a: this is a check on the run itself |
| P19 | **A green session proves only what it exercised** (CODE-TEST-06) | Every live session | Count the specific events in the logs; the 2-client session cannot produce combat (personalities runner + balanced, no bots): use 4 clients with distinct `-cw_profile` and `-cw_perk` | Paths no client personality takes |

## 6. Rules the tests enforced before any document stated them

The enforcement inventory (b7c2616e) found 21 rules that tests enforce but `docs/CONVENTIONS.md` did not state. Each is now an entry, here or in the sibling document:

| # | Rule enforced by tests | Now written as |
|---|---|---|
| 1 | Every player-facing string is a `UiKeys` constant with a csv row; bijection; no key literals; UXML `@key` or empty; `[UiArgs]` match | CODE-TEXT-01, CODE-TEXT-02, CODE-TEXT-03, CODE-TEXT-04, CODE-TEXT-05 |
| 2 | Ability and perk text templated from the ability's own fields; no digit in a template; the `.asset` Description mirrors the render | CODE-TEXT-06, CODE-TEXT-07 |
| 3 | Kid-facing text: no "cast", "target", "cooldown"; at most 140 characters; no "equipped" / "pre-filled" | CODE-TEXT-08 |
| 4 | `Debug.Assert*` is banned too, not only `Debug.Log*` | ARCH-ERR-01 |
| 5 | Food moves are authority-confirmed (request id, owner debits, receiver credits the report) | ARCH-NET-07 |
| 6 | `AbilityController`'s unrolled slot fields have one writer and one reader path | no ARCH rule states it (enforced by `AbilitySlotFanOutTests`; nearest is ARCH-NET-11, spawners set slots only through `SetSlots`) |
| 7 | Only `ChickenController` writes the comeback flags | ARCH-NET-08 |
| 8 | Shared line materials, never one per chicken | CODE-ASSET-12 |
| 9 | Reduced Motion read live; every menu-juice entry gated | CODE-UI-07 |
| 10 | Every UXML file is well-formed XML | CODE-UI-05 |
| 11 | One player palette, no copy of its colours anywhere | CODE-UI-08 |
| 12 | Physics layers 8 and 9 on every collider, matching code masks | CODE-ASSET-10 |
| 13 | Build scene order; Developer Mode off by default | CODE-ASSET-09, CODE-DESIGN-17 |
| 14 | Keystores git-ignored; no debug-key fallback; bundle ids pinned | CODE-BUILD-08, CODE-BUILD-09 |
| 15 | Every serialized or networked enum is append-only with an ordinal pin | ARCH-NET-13; `SfxId`: CODE-AUD-04 |
| 16 | Event payloads are primitive or enum fields only | ARCH-EVT-04 |
| 17 | Log-line shapes parsed by `merge.ps1` are frozen | CODE-TEST-16 |
| 18 | A spawning ability overrides `CanActivate` | ARCH-DATA-06 |
| 19 | No carried prop under a chicken root | CODE-ASSET-14 |
| 20 | No `ProjectContext.HasInstance` gate | ARCH-DI-05 |
| 21 | Guards are non-vacuous and self-test their regexes | CODE-TEST-13 |

Also first written here from tests or code comments: CODE-TEXT-09 (visible marker, report once), CODE-AUD-03 (one slot per `SfxId`, one cue per clip), CODE-UI-06 (every emitted USS class has a rule), CODE-ASSET-11 (chicken prefab completeness).

## 7. Unenforced and partly enforced rules, ranked

Highest regression risk first. Each gap was re-checked at b7c2616e (the evidence column says how). Effort: S = one test in an existing file, M = a test plus a small code change across several files, L = new infrastructure.

| Gap | Rule | What is not enforced (verified how) | Proposed guard | Effort |
|---|---|---|---|---|
| G-10 | CODE-TEST-01 (every guard) | Nothing runs the suite automatically: `.git/hooks` holds only samples, `core.hooksPath` is unset, and the only workflow (`.github/workflows/pages.yml`) deploys `docs/site`. Every TEST entry here bites only when someone runs the suite | A pre-push hook running batch EditMode (`unity test --mode EditMode`) that refuses on red, on a compile failure or on a missing result XML; later a self-hosted CI job on push | M |
| G-01 | CODE-TEXT-03, CODE-TEST-12 | `UiTextTests.LiteralTextHits` reads `MenuUiController.cs`, `MatchHudController.cs`, `MatchHud.cs` with `File.ReadAllText` (UiTextTests.cs:209-247); the six `MenuUiController.*.cs` and three `MatchOverlaysController.*.cs` partials, `TouchControlsController*.cs`, `MatchStandings.cs`, `ChickenNameplate.cs` are never scanned (`git ls-files` lists them; a literal there passes) | Run `LiteralTextHits` over `TestSources.ReadClass` of every file under `UI/`, `Input/`, `Visuals/`, with an allow-list for glyph-only literals and `AbilityLab/`; plus a guard-the-guards test failing when a test `File.ReadAllText`s a path that has partial siblings unless allow-listed | S |
| G-03 | CODE-TEXT-06 | Passive `.asset` `Description` prose is hand-typed and returned as is by `PassiveAbilitySO.DescriptionText` (PassiveAbilitySO.cs:82); Hoarder, Thief, Bulwark are checked only by `Contains` and the switch has no default arm (PerkTextTests.cs:227-267) | Render `DescriptionText` from the perk templates, and assert `Description` is the rendered perk text for every registered passive (section 4.3) | S |
| G-02 | CODE-TEXT-06 | `EveryArgument_IsARealFieldValue_ToDisplayPrecision` accepts a match with any field (AbilityDescriptionTests.cs:89-113), so a placeholder reading the wrong field passes when values coincide | Declare each argument's source field (`nameof`) and assert equality with that field | M |
| G-06 | CODE-TEXT-10 | Ten csv values hold literal digits outside ability/perk keys; "3 CPU" and "4-PLAYER" restate `SoloBotRoster` and `MaxPlayers` (python scan of `UiText.csv`, section 4.3) | `UiTextTests.PlayerFacingText_HasNoLiteralDigits_OutsideAllowList`; template the roster rows | S |
| G-13 | CODE-AUD-04; ARCH-NET-13 | Ordinals unpinned: `MatchEventKind` (only byte backing and `None == 0`, ContractsAndEnumsTests.cs:41), `ComebackGrant` (Gameplay/ComebackRules.cs:5, an RPC parameter), `CastArchetype` (indexes a serialized clip array), `SfxId` (`git grep` of Tests finds no pin) | Reflect over every `[Networked]` property, `[Rpc]` parameter and serialized field of an enum type in `CluckWars.*` and require each enum to appear in one central `Pin<T>` table | S |
| G-09b | CODE-AUD-01 | The `IAudioService`-only rule has no guard; `PlayOneShot` appears only in `Audio/UnityAudioService.cs:116` today (`git grep`) | RuntimeFiles scan banning `PlayOneShot`, `PlayClipAtPoint`, `AudioSource.Play` outside `Scripts/Audio/` | S |
| G-11 | CODE-UI-04 | `UiElements.SetShown` / `Bind` / `Query` over inline `style.display =` and bare `root.Q<T>`: no test (`git grep` of Tests for `style.display`, `SetShown`, `UiElements.Bind` finds none) | RuntimeFiles scan of `UI/`, `Input/`, `Visuals/` with a counted allow-list (the five deliberate `StyleEnum` comparisons) | S |
| G-12 | CODE-PROC-13, CODE-TEXT-10 | Compendium numbers are hand-copied; the site is stale after the Slow split (BL-158) | An editor script emits the site's numbers block (JSON) from the shipped assets; a test fails when the committed block differs | M |
| G-09 | CODE-TEXT-10; ARCH-DATA-11 | Magic-number tunables: no scan; example `ab.Cooldown <= 6f` decides the SHORT/MED badge (MenuUiController.GearUp.cs:508) apart from the band test | Source scan of `Gameplay/`, `Abilities/`, `UI/` for float literals other than 0, 1, 0.5 in comparisons and arithmetic outside `const` / `static readonly` / SO field initialisers, with a counted allow-list | M |
| G-04 | CODE-TEXT-02, CODE-TEXT-05 | The key-literal ban and the call-site contract parse read 8 listed files plus passives (UiTextTests.cs:193-207) | Run both over `RuntimeSourceGuardTests.RuntimeFiles()` | S |
| G-07 | CODE-TEXT-10 | A numeric literal passed as a `UiText.Format` argument is unchecked (example `("n", 1)` at MenuUiController.Coop.cs:460, a legitimate fact) | Source scan: a `Format` argument value is an identifier or member access, never a literal, with an allow-list | S |
| G-15 | CODE-TEST-15 | Nothing refuses a suite run on a failed compile or a missing/stale result XML | Make the suite runner check `scriptCompilationFailed`, the XML timestamp and the count before reporting | S |
| G-08 | CODE-STYLE-01 | The suffix test only sees SO types with an asset under Data (ProjectConfigTests.cs:405-423) | Reflect over `ScriptableObject` subclasses in `CluckWars.*` and require the suffix, allow-listing Unity framework subclasses (the renderer features `CWHazeFeature`, `MarkScreenFeature` are `ScriptableRendererFeature`s) | S |
| G-14 | CODE-TEXT-04 | UXML text test covers 4 menu UXML plus MatchOverlays (UiTextTests.cs:25-28, :343); `MatchTopBar`, `MarkHud`, `TouchControls` unchecked | Iterate every `Assets/UI/*.uxml` except `AbilityLab.uxml`, allow-listing slot-number labels | S |
| G-05 | CODE-TEXT-01 | A `UiKeys` constant that no code or UXML reads still passes the bijection | Every constant is named in some runtime `.cs`, a UXML `@key`, or returned by a `*Key` property | S |
| G-16 | CODE-DESIGN-04 | Retuning before the owner's test has no guard | Balance-freeze hash of shipped gameplay numbers, updated deliberately in the same commit | M |
| G-17 | CODE-UI-09 | Local player's stack drawn on top: no test found | Pure sort rule plus a source guard | M |
| G-18 | CODE-ASSET-03 | `PrimitiveType.Plane` ban: no test (`git grep` of Tests finds none) | RuntimeFiles scan | S |
| G-19 | CODE-TEST-06 | A green session that never exercised the changed path | `verify.ps1 -Require <events>` failing on zero counts | S |
| G-20 | CODE-UI-08 | Gameplay-meaning colours outside `FeedbackTuning`: only player colours guarded | Scan `Visuals/`, `UI/`, `Input/` for `new Color(` outside the palette files, counted allow-list | M |
| G-21 | CODE-DESIGN-16 | No frame-time check on the 30 fps Android target | Dev-tools p95 frame-time probe and a `verify.ps1` threshold | M |

**The ten highest-risk unenforced rules, one line each:** G-10 nothing runs the suite; G-01 literal-text guard blind to nine partial files; G-03 passive description prose hand-typed; G-02 placeholders not bound to their field; G-06 csv roster numbers typed; G-13 four wire/serialized enums without ordinal pins; G-09b audio service boundary unguarded; G-11 `UiElements` plumbing unguarded; G-12 compendium numbers hand-copied (stale now); G-09 magic-number thresholds and tunables unscanned.

## 8. Appendix A: stale sources for the rules in this document

Each line was confirmed at b7c2616e (repo files with `git show HEAD:`, `.claude/CLAUDE.md` and memory from disk). Until a line is fixed, this document wins. "Who" is the agent that owns the file's concern under the routing table; the owner decides for CLAUDE files and memory.

| Source path:line | What it says | What is true now | Who should fix |
|---|---|---|---|
| docs/TESTING.md:199 | "101 EditMode tests", per-file table at :205-213 | The suite is far larger (docs/STATE.md:11 quotes the current baseline); quote STATE, never a count | librarian (doc pass) |
| docs/TESTING.md:229-231 | "Baseline as of 2026-07-22: 99 passed / 2 failed ... Do not weaken them" | Current baseline is all green (docs/STATE.md:11) | librarian |
| docs/TESTING.md:260-263 | Do not add a runtime `.asmdef`: it can break Fusion's weaver; code is in `Assembly-CSharp` | Five asmdef assemblies with the weave-list rule (docs/CONVENTIONS.md:476-502); nothing compiles into `Assembly-CSharp` | librarian |
| docs/TESTING.md:106-154 | The project's MCP is Unity's own (`com.unity.ai.assistant`, relay) | Unity's own MCP is banned; the bridge is `ai-game-developer` (CODE-BUILD-01). Not a contradiction: root CLAUDE.md:57 correctly names the open-source IvanMurzak Unity-MCP | librarian |
| docs/TESTING.md:223 | Agents run the suite with MCP `tests-run` | The full suite times out there; use `TestRunnerApi` or batch (CODE-BUILD-02) | librarian |
| docs/TESTING.md:535-538 | Source tags `TouchHud`, `CharacterSelect`, `MatchHud` "UGUI HUD", `CargoHud` | `TouchControlsHud`, `CharacterSelectController`, `CargoHud` no longer exist; the HUD is mostly UI Toolkit (Q1) | librarian |
| docs/TESTING.md:551 | Check the stray EventSystem, naming `TouchControlsHud` | The rule stands (CODE-FOOT-07); the class name is gone | librarian |
| docs/TESTING.md:712 | Time-to-target "≈ 70 % of the 180 s timer" | `MatchConfig.asset` is 45 s (CODE-DESIGN-03) | librarian |
| docs/TESTING.md:77, docs/TESTING.md:745 | All Android testing on the Pixel 9 | The device is the Pixel 10 Pro (ruling D16, 2026-11-14); PC-only while no phone is reconnected; the phone is a Pixel 10 Pro (M:test-session-2026-10-20-learnings.md:14) | librarian |
| docs/TEST_STRATEGY.md:46 | Layer 0 "≈ 1 960 tests" | Quote docs/STATE.md:11 | librarian |
| docs/TEST_STRATEGY.md:50 | Layer 4 uses a phone | PC-only until a phone is reconnected (CODE-TEST-11) | librarian |
| docs/CONVENTIONS.md:523-525 | The in-game HUD (`MatchHud`, `TouchControlsHud`, `DebugHud`, `UiGfx`) is a legacy procedural-UGUI island | Top bar, touch controls and overlays are UI Toolkit; `TouchControlsHud` is gone; `MatchHud` keeps only a hit-flash canvas; `DebugHud` is IMGUI (Q1) | librarian after the owner rules on Q1 |
| docs/CONVENTIONS.md:509 | Source SO `.cs` definitions live in `Scripts/Gameplay/` | They live across layers (`Scripts/Abilities/AbilityBaseSO.cs`, `Scripts/Audio/AudioRegistrySO.cs`, ...); see FILE_STRUCTURE.md | librarian |
| docs/CONVENTIONS.md:510 | `Resources/` is used for `ProjectContext.prefab` | It also holds `Text/UiText.csv` and four fonts (CODE-STYLE-02) | librarian |
| docs/CONVENTIONS.md:538 | `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>` | The model has changed; the attribution line in use differs (Q2) | owner (Q2), then librarian |
| CLAUDE.md:65 | Latest tag `v0.3.0-alpha` | `v0.4.0-alpha` is the latest tag (`git tag --sort=-creatordate`) | FIXED 2026-11-17 (CLAUDE.md:68 names `v0.1+26458919` / `v0.1+26458925`) |
| M:branching-model-v0-4-release.md:26 | The model is "Recorded in `CLAUDE.md` (Branches)" | Root CLAUDE.md has no Branches section and no "branch" string; the rule lives only in memory (CODE-PROC-14) | owner (Q4) |
| .claude/CLAUDE.md:111 | "cravify skill: hybrid Claude+Antigravity for any task touching 3+ scripts/prefabs" | Forbidden unless the owner names it (~/.claude/CLAUDE.md, CODE-PROC-05) | FIXED 2026-11-17 (.claude/CLAUDE.md:123 now allows cravify only when Maestro names it) |
| M:agent-model-routing.md:10, M:agent-model-routing.md:13 | `cravify` is a coding path | Same as above | owner (memory) |
| M:ui-toolkit-decision.md:14 | In-game HUD stays UGUI | Superseded by M:ui-rebuild-2026-07.md:15 (Q1) | owner (memory) |
| M:ui-rebuild-2026-07.md:40 | `code-architect` is pinned to Fable | It runs on opus; Fable is banned (CODE-PROC-03) | owner (memory) |
| M:mcp-tests-run-leaves-empty-scene.md:11-17 | Reopen Bootstrap with `scene-open` / `scene-list-opened` | The 2.0 bridge has no `scene-*` tools; use `script-execute` (CODE-BUILD-05) | owner (memory) |
| docs/TDD.md:371-378 | `IAudioService.PlaySFX(AudioClip clip, Vector3 position)` | `PlaySFX(SfxId, volume, pitch)` (CODE-AUD-01) | librarian |
| docs/TDD.md:419-421 | Fixed camera, no player-following, no zoom | `MatchCamera` follows the local chicken (docs/CONVENTIONS.md:407-409) | librarian |

## 9. Appendix B: superseded rules

| Superseded rule | Replaced by |
|---|---|
| No ability sharing between classes; each class its own abilities (playtest-balance thread 2026-08-21) | Fixed kits (2026-10-31) and the merge into one ability per generic job (2026-11-05): CODE-DESIGN-05, CODE-DESIGN-13 |
| Free ability picking for every class | Fixed kits behind `LoadoutMode.Fixed`; free picking kept behind the switch: CODE-DESIGN-13 |
| Old ability names (Snatch, Dive Bomb, Mark Kill, Wing Slam, Cluck Shock, Feather Trap, Sneaky Steal, Turtle Mode, Death Mark) in memory and briefs | Generic jobs and labels (Steal, Stun, Shove, Slow, Slow Area, Speed, Jump, Shield, Hide, Peck, Bank; Egg Shell, Hunt); old type names kept in code: CODE-TEXT-12, CODE-STYLE-10 |
| One Slow aura (2026-11-05 merge) | Slow (instant, timed) and Slow Area (aura or placed zone), 2026-11-13: CODE-DESIGN-14 |
| Ability description cap 110 characters | 140 characters (2026-11-13): CODE-TEXT-08 |
| Reach x1.8 guideline and per-ability reach numbers in memory | Reach +20% (2026-11-05) and the numbers in the shipped assets: the asset wins (CODE-DESIGN-03, CODE-TEXT-06) |
| The 2026-11-05 numbers (Speed 1.3x, Hide 2 s) as rules | Data in the assets, changed only on the owner's request (CODE-DESIGN-04); Speed 1.3x is pinned by `AbilityDescriptionTests.PinnedNumbers_MatchTheShippedAssets` |
| In-game HUD is UGUI | UI Toolkit for all screen-space UI (2026-07 rebuild), pending Q1 for the residual canvases |
| Do not add runtime asmdefs | Five assemblies with the weave rule (ARCH-LAY-01, ARCH-LAY-06) |
| Use Unity's own MCP | `ai-game-developer` MCP plus the `unity` CLI for what it lacks: CODE-BUILD-01 |
| Use cravify / Antigravity for 3+ file tasks; run agy in a visible PowerShell window, prompt last; watch its empty "fix" commits | Banned unless the owner names it (since 2026-09-24): CODE-PROC-05; the agy launch rules are dormant |
| `code-architect` pinned to Fable | opus; Fable never: CODE-PROC-03 |
| All Android testing on the Pixel 9 | The device is the Pixel 10 Pro (ruling D16, 2026-11-14); PC EXE clients only while no phone is connected: CODE-TEST-11 |
| Fixed, non-following camera (TDD) | Follow camera: Appendix A |
| `IAudioService.PlaySFX(AudioClip, Vector3)` | Typed `SfxId`: CODE-AUD-01 |
| Progression rules (one `IMatchEventSink`, `ChickenCargo.ReceiveStolen` chokepoint, two wallets, commit each slice on QA plus green suite) | Not on this branch: `IMatchEventSink` does not exist at b7c2616e; the rules apply on `develop` / `feature/progression` |
| Assassin execute counts as a steal for transferred cargo only (2026-09-12) | Reworked with the Death Mark / Hunt kit; check against current code before relying on it |

## 10. Open questions for the owner

> **Answered 2026-11-14.** Maestro ruled on these via the decision page (https://claude.ai/artifact/PtyqzDvoGh6ZoF5N1zgGcJ); the rulings are folded into the rules above and recorded in the project memory `rule-decisions-2026-11-14`. The questions are kept here for the reasoning that produced them. All of them are now ruled on; the last, branching on `SessionMode`, became `ARCH-NET-20` in the architecture rules.


- **Q1 In-game HUD technology.** docs/CONVENTIONS.md:523-525 says the HUD is a UGUI island to leave alone; the 2026-07 rebuild memory says UI Toolkit for all screen-space UI. At b7c2616e: `MatchHudController` (top bar) and `TouchControlsController` use a `UIDocument` (Assets/_Game/Scripts/UI/MatchHudController.cs:20, Assets/_Game/Scripts/Input/TouchControlsController.cs:89), the overlays are UXML, `TouchControlsHud` is gone, `MatchHud` still builds a procedural UGUI canvas for the full-screen hit flash (Assets/_Game/Scripts/UI/MatchHud.cs:162-173) and `DebugHud` draws with IMGUI `OnGUI` (Assets/_Game/Scripts/UI/DebugHud.cs:159). Is the rule "all screen-space UI is UI Toolkit, with the hit-flash canvas and the debug HUD as named exceptions"?
- **Q2 Commit co-author line.** docs/CONVENTIONS.md:538 names "Claude Opus 4.7"; sessions now attribute a different model. Name the model per commit, or a generic line?
- **Q3 Where colours live.** docs/CONVENTIONS.md:262 keeps gameplay-meaning colours as `FeedbackTuning` code constants, "not fields on `FeedbackPaletteSO`"; decision D3 (2026-11-07) puts ability-state accent colours in a config ScriptableObject. Confirm the split: gameplay-meaning colours in code, ability accents in the palette SO.
- **Q4 Branching rule home.** The branching model lives only in memory. Add a Branches section to root CLAUDE.md? And does "everything on develop, tested and pushed" (2026-10-22) still apply now that work flows `simplify-abilities` to `balance` to `main` (BL-162, BL-163)?
- **Q5 Gated presses.** Slow and the stun bursts are gated with `RequiresEnemyInRange` (docs/STATE.md:13). Does a gated press still show the range preview, as the "never block, always preview" ruling requires? Not verified in game.
- **Q6 Assistant lessons not yet confirmed as rules.** (a) Every bug fix adds a check that fails on the old build (written in docs/TEST_STRATEGY.md:23-24; you asked for "a better testing strategy"); (b) before any build or batch run check `git status`, never run a broad `git checkout --` or `git stash`, build only from a clean commit (memory `parallel-sessions-share-the-tree.md`); (c) a test that restates its inputs as literals cannot detect drift; (d) four clients with `-cw_perk` for combat sessions. This document applies (a) and (d) as CODE-TEST-03 and CODE-TEST-06; confirm or strike, and say whether (b) and (c) are rules.
- **Q7 Passive descriptions.** Keep the hand-typed passive `Description` prose (not shown to players today) or render it from the perk templates (G-03)?
- **Q8 Roster numbers in wording.** Template "4-PLAYER" and "3 CPU" from `MatchConfig` and the solo roster, or allow-list them (G-06)?
- **Q9 Automation.** Should the guards run automatically (pre-push hook or CI, G-10)? Today nothing blocks a red commit.
- **Q10 Tracked home for agent rules.** `.claude/CLAUDE.md` is git-ignored (`.gitignore:130`), so the routing, model and slow-batch rules have no history. Move the standing process rules into a tracked file (this document holds them now as CODE-PROC-*)?
