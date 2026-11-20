# CLAUDE.md — Cluck Wars Entry Point

Read this first. It is the single project config (the former `.claude/CLAUDE.md` was merged into it on 2026-11-18): an index to the docs below, plus the agent routing, the hard constraints, the tools and the live-ops balance publishing. Update each doc in place when its concern changes.

---

## Project at a glance

**Cluck Wars** is a fast-paced 4-player free-for-all arena game. Players pick a chicken class, collect food from piles, deposit it at their base, sabotage rivals. First to bank 40 food units (or most banked when the 45-second timer expires) wins — the authority is `MatchConfig.asset`, not this line.

- **Engine:** Unity 6000.3 LTS · **Pipeline:** URP
- **Networking:** Photon Fusion 2 — Shared Mode for demo (LAN/cloud-relay), Server Mode post-funding
- **DI:** Zenject (Extenject) · **Backend:** UGS behind `IUGSService` (`NullUGSService` in demo)
- **Architecture:** MonoBehaviour — no ECS/DOTS
- **Platforms:** Windows (primary dev), Android (mobile target), iOS (Xcode-export supported, off by default — no Mac to finish it; `Build/Enable iOS Export` to turn on)
- **Target FPS:** 60 Windows, 30 stable on mid-range Android (2021+)
- **Main scenes:** `Assets/_Game/Scenes/Bootstrap.unity` → `Assets/_Game/Scenes/Game.unity` (`Assets/Scenes/SampleScene.unity` is leftover template cruft, not used)

---

## Doc map — where to look

| If you want to know… | Read |
|---|---|
| **Where are we right now?** Current state, what works, latest tag, recent commits, open bugs, what's deferred. | `docs/STATE.md` |
| **How does the code fit together?** Scene graph, DI scopes, networking model, chicken architecture, ability system, asset locations. | `docs/ARCHITECTURE.md` |
| **What rules / patterns must I follow?** Self-injection, RPC patterns, networked-state defaults, footguns we hit and how to avoid them. | `docs/CONVENTIONS.md` |
| **Which rule covers this, and what enforces it?** Every architecture rule with a stable ID (`ARCH-…`), what test or compiler boundary enforces it (or `NONE`), the pattern catalogue, the audit lens criteria, and the table of unenforced rules. Where it disagrees with an older doc, it wins; its Appendix A lists the stale lines. | `docs/ARCHITECTURE_RULES.md` |
| **Same, for code, text, UI, tests, process and design constraints?** Rules `CODE-…` with their enforcement, the test pattern catalogue, the worked example "numbers in ability text come from the data". | `docs/CODE_RULES.md` |
| **Where does X live, which assembly owns it, where does a new one go?** Assembly table, folder map, key files, data map, "adding X goes in Y". Its generated blocks are checked by `python -I tools/docs/file_structure.py check`. | `docs/FILE_STRUCTURE.md` |
| **How do I publish a balance change without an update?** The `/balance` skill, the scripts, the safety rules. | "Balance publishing" below; `docs/TESTING.md` "Publishing a balance delta" |
| **How do I build / run / diagnose?** Build menu shortcuts, debug HUD (F1), adb logcat commands, source-tag reference, diagnostic flows per bug type. | `docs/TESTING.md` |
| **What's the plan, what's shipped, what's next?** Per-phase status, dedicated test session work list. | `docs/ROADMAP.md` |
| **Why does the design look like this?** GDD / TDD / ART. | `docs/GDD.md` / `docs/TDD.md` / `docs/ART.md` |
| **How do I show the game to a human?** Interactive mechanics site — classes, abilities, arena, traversal, tech stack. Doubles as portfolio. | `docs/site/index.html` — **keep in sync, see below** |
| **How do I read the docs in Obsidian?** Start-here guide, link dashboard, docs-by-status table, architecture canvas. | `docs/OBSIDIAN.md`, `docs/DASHBOARD.md` |
| **How does the open-source Unity MCP work?** MCP architecture, UI Toolkit rules, asset structure, and Claude integration. | `docs/UNITY_MCP_GUIDE.md` |
| **Why this engine / other big choices?** Architecture decision records (e.g. Unity vs Godot). | `docs/adr/` |
| **What are the Google Play listing limits (short description 80, full 4000, release notes 500, graphics), and where are the store graphics?** | `docs/PLAY_STORE.md` |

---

## Quick start for agents

When you begin a session:

1. `cat docs/STATE.md` — orient yourself to current state.
2. Skim `git log --oneline -10` — what just changed.
3. If the user asks for a change in a system you haven't touched before, check `docs/ARCHITECTURE.md` for how it's wired.
4. Before making nontrivial changes, check the rules that cover the area you are touching in `docs/ARCHITECTURE_RULES.md` (layering, DI, networking, events, data-driven design, errors) and `docs/CODE_RULES.md` (text, UI, tests, process, design constraints): rules have stable IDs and say what enforces them or that nothing does. Ask the `librarian` agent which rules cover an area instead of reading both docs whole. `docs/CONVENTIONS.md` still holds the hard-won detail (self-injection pattern, RPC patterns, `LogLevel`-name collision with Fusion, `ChickenClass : byte` quirk, etc.); where it disagrees with the rules docs, they win. Where to put a new file: `docs/FILE_STRUCTURE.md`.
5. After making changes, **update `docs/STATE.md`** so the next agent doesn't lose context. If the change added, moved or deleted scripts/folders/assemblies or changed what enforces a rule, have `librarian` update `docs/FILE_STRUCTURE.md` and the rules docs (it is their only editor, ruling D13). ROADMAP entries get ticked too when phases complete.
6. **If you changed the design docs or any gameplay `.asset` value, update `docs/site/index.html` too.** Standing instruction from Maestro (2026-09-04): the compendium is a living document, not a snapshot. It is one self-contained HTML file with no build step. Its numbers must be read from the shipped `.asset` files and gameplay constants, **never** from GDD prose — the GDD carries known-stale figures. Republish it to the same Artifact URL (see `memory/mechanics-compendium-site.md`).
7. **Slow batches (artist icons, lab captures, five-minute suite runs) run their specialists from the main session, one foreground agent at a time**, not through a background orchestrator: on 2026-11-10 a background `code-architect` died twice on API timeouts mid-item, taking its child with it. If an orchestrator dies, check `git status` before redoing anything.

---

## Agent routing

`code-architect` (runs on `opus`) is the default entry point for any non-trivial task —
it owns project structure and architecture, decomposes the request into a high-level task,
delegates execution to the right specialist below, and reviews the specialist's diff
before signoff. Only skip it for a trivial single-file fix or doc-only change. Don't
implement inline when a specialist applies.

**Lifecycle of a non-trivial task.** (1) *Look up*: `librarian` answers "where / which rule /
what was decided / what depends on" before anyone re-derives it. (2) *Plan*: `code-architect`
writes the spec, naming the covering rule IDs. (3) *Build*: the specialist implements and ends
its report with a "Docs upkeep" line. (4) *Review*: `code-architect`, or `qa-reviewer` for
risky diffs. (5) *Docs*: `librarian` brings `docs/FILE_STRUCTURE.md` and the rules docs in line
(it is the only agent that edits them). (6) *Close*: `docs/STATE.md`, then the `housekeeping`
skill (commit, MemPalace wing `cluckwars`, graphify flag).

**Every agent brief is built with the `prompt-master` skill** before the `Agent` call
(CODE-PROC-07), and an implementation brief follows CODE-PROC-08.

**Models: Fable is an escalation, never a default.** The policy has one home, the "Model
policy" paragraph of `~/.claude/CLAUDE.md` (tracked copy of the rule: CODE-PROC-03). In short:
every agent here has its model in its frontmatter (`opus` for `code-architect`, `qa-reviewer`,
`mechanics-designer`; `sonnet` for the rest), and `fable` is passed per call only for (1) a
problem `opus` already got wrong or stuck on, (2) a cross-system decision that is costly to
reverse, or (3) the final verdict of a high-stakes synthesis. Never for implementation, lookups,
docs upkeep, art or capture batches, slow batches or scheduled runs. One Fable agent at a time,
its brief starts with "Fable because: ...", and its sub-agents get an explicit model. On this
project the usual cases are: a networking or authority bug that survived one `opus` hunt, an
assembly or Fusion-layout decision, the verdict of a full game audit before a release.

**Routine work runs on haiku** (same policy paragraph). Git handling (status, diffs, staging by
explicit path, commits whose grouping and message are decided, tags), batches of renames and
known check runs go to `developer-haiku` with the exact files and commands; the caller keeps
the decisions. One or two commands stay inline. Other sessions share this tree, so a git brief
always lists the files by path and forbids `git add -A`, stash, reset and broad checkout.

**Slow batches run from the main session.** A background `code-architect` orchestrator that waits
on long artist or capture sub-agents (procedural PIL icons, lab captures, five-minute suite runs)
dies on API timeouts (twice on 2026-11-10, mid-I9 and mid-I14) and takes its child with it, leaving
the child's edits uncommitted in the tree. For a batch with such items, the main session runs the
specialists itself, one foreground agent at a time, verifies each hand-back (git status, the capture
or sheet, the suite) and commits per item. When an orchestrator does die: check `git status` first
and never redo work that is already in the tree. Keep `code-architect` for code-only batches and
for design/triage.

| Request type | Agent |
|---|---|
| Orientation, docs, ambiguous/cross-cutting, architecture review | `code-architect` |
| Knowledge lookup ("where is X", "what was decided about Y", "what depends on Z"); upkeep of the rules docs | `librarian` |
| Multi-file feature, new system, networked bugfix | `senior-dev` |
| Deep-dive code review, diff review, convention/error-handling/type-design check | `qa-reviewer` |
| Test plan, regression check, QA scenario | `tester` |
| Feel, input timing, juice, hit reactions | `ux-designer` |
| UI Canvas, HUD, UXML/USS, menus | `ui-designer` |
| Mechanic design, ability spec, balance values | `mechanics-designer` |
| Scene layout, food pile placement, map pacing | `level-designer` |
| 2D sprites, icons, UI textures | `artist-2d` |
| 3D meshes, chicken models, .fbx/.glb | `artist-3d` |
| Particles, VFX Graph, ShaderGraph hit effects | `vfx-artist` |
| URP pipeline, post-processing, camera, lighting, mobile render budget | `render-pipeline-artist` |
| ShaderGraph, custom materials, hit flash, dissolve, zone shaders | `shader-artist` |
| SFX briefs, AudioMixer, music direction | `audio-designer` |
| UI copy, ability names, class flavor, tone | `narrative-designer` |

`qa-reviewer` now also covers what `pr-review-toolkit`'s `silent-failure-hunter`,
`type-design-analyzer`, `comment-analyzer`, and `pr-test-analyzer` would — those plugin
agents don't need separate invocation on this project. `code-architect` covers what
`feature-dev`'s `code-architect`/`code-explorer` would (pattern tracing before a blueprint).
`senior-dev` self-applies `pr-review-toolkit:code-simplifier` and silent-failure discipline
before reporting back. Other plugins (`code-modernization`, `agent-sdk-dev`, `coderabbit`,
etc.) aren't relevant to this Unity/C# project and aren't routed to.

---

## Key constraints (enforce in every agent)

- **All services behind interfaces**: `INetworkService`, `IUGSService`, `IAudioService`, `IInputProvider`, `ILogService`. Never call Fusion/UGS/Input directly from gameplay.
- **Self-injection** in `Spawned()` / `Awake()`: the one line `SelfInject.Ensure(this, _log != null);` and no other form (ARCH-DI-03) — never `ProjectContext.Instance.Container.Inject(this)` by hand, never gate on `HasInstance` (ARCH-DI-05).
- **`HasStateAuthority` gate** at top of every `FixedUpdateNetwork`. Cross-authority writes via `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`.
- **`ILogService` only** — no `Debug.Log`.
- **`LogLevel` alias** in files that import both `CluckWars.Logging` and `Fusion`: `using LogLevel = CluckWars.Logging.LogLevel;`
- **`ChickenClass : byte`** — backing type is byte for Fusion serialisation. Don't change.
- **URP materials only** — no Built-in RP shaders.
- **Animation + VFX are local** — `ChangeDetector` + `Render()`, never RPC'd.
- **UXML/USS only** for menu UI — no procedural UGUI.
- **SOs use `SO` suffix**, live in `Assets/_Game/Data/`.
- **New abilities**: extend `AbilityBaseSO`, register in `AbilityRegistrySO`, author `AbilityCategory` + `BotRole`.
- **`onBeforeSpawned`** for setting `[Networked]` properties from tick zero.
- **Five runtime assemblies**, Core < Gameplay < Presentation < Diagnostics < App (`docs/CONVENTIONS.md` "Assemblies"): never add an upward reference; a new `Scripts/` folder needs an `.asmref`; an assembly that references Fusion must be in `AssembliesToWeave` of `NetworkProjectConfig.fusion` in the same commit.

---

## Tools in scope

- **AI Game Developer MCP** (`mcp__ai-game-developer__*`) — the Unity bridge for this
  project: scene inspection, asset queries, component editing, prefab staging, screenshots,
  `script-execute`, and `tests-run` for the EditMode suite. Configured in `.mcp.json`.
- **Do NOT use Unity's own MCP** (`mcp__unity-mcp__*`, the `.unity/relay` server). It is
  gated behind a Unity Pro/AI subscription that this project does not have — it returns
  "Connection revoked" and cannot compile, inspect, or run tests. It was removed from the
  global MCP config on 2026-08-01. Use `ai-game-developer` for everything instead.
- **Unity plugin + Unity CLI** (`unity@unity-agent-plugin`, `unity` command; adopted
  2026-10-30 alongside the MCP, Pipeline package `com.unity.pipeline` 0.9.1-exp.1 in the
  manifest on `main`). All three are beta. Use both, each for:
  - **Plugin skills** (`/unity:ui-uitk`, `unity:urp-postprocessing`,
    `unity:validate-urp-render-graph-renderer-feature`, `unity:ui`): consult them when writing
    UI Toolkit, URP post-processing or a render-graph feature — they carry current-Unity
    correctness and need no Editor connection or the Pipeline package. Skip them for uGUI
    basics and gameplay code.
  - **`ai-game-developer` MCP**: the default for every live-Editor task (scene/asset/component
    edits, `script-execute`, `tests-run`, screenshots, RQL queries). Keep using it.
  - **`unity` CLI / Pipeline** (`unity command "<group> <name>" ...`): only for what the MCP
    lacks — `build run` from the CLI, `diagnostics run_audit` (Project Auditor), transactional
    `batch`, headless/CI runs, Editor/license management — or as a fallback when the MCP
    bridge is down. Gotchas: quote the command name; `script eval` takes `--code` and must end
    in a `return`; `game_view --save_path` resolves under `Assets/` (delete the file and its
    `.meta`); the Editor must be focused before it loads a freshly added package.
  - Both stay: the MCP is not being replaced. `Phase8ChunkD1Tests.FullMound_...Mobile_centre`
    can fail inside a live Editor (render-state dependent) but passes in batch mode
    (`unity test`), so judge render tests by a batch run.
- **Blender MCP** (`mcp__blender__*`) — 3D mesh work, rigging, LOD reduction.
- **assetforge skill** — 2D/3D art generation via ComfyUI (Flux, SDXL, TripoSR).
- **cravify skill** — hybrid Claude+Antigravity. **Only when Maestro asks for it by name** (its usage limit is exhausted since 2026-09-24); otherwise implement via `senior-dev` and the `developer-sonnet` / `developer-haiku` implementers.
- **housekeeping skill** — end-of-session sync (commit, MemPalace, graphify flag). Wing, rooms and what counts as structural: "Knowledge routing and housekeeping" below.
- **MemPalace** (`mcp__mempalace__*`, wing `cluckwars`) for decisions and history; **graphify** (`graphify-out/graph.json`) for C# structure. Routing table: "Knowledge routing and housekeeping" below.

---

## Balance publishing (live ops)

Numbers can change without an app update. `BalanceBook` (`Assets/_Game/Scripts/Balance/BalanceBook.cs`, ARCH-DATA-14) applies a schema-1 JSON **delta** from UGS Remote Config key `balance` over the shipped ScriptableObjects at runtime. No delta, or an empty one, plays the shipped numbers (`bal=5465b825`); `bal=<hash>` shows in the Session log line and the F1 HUD, so a build proves which delta it runs.

- **Driver:** the user-level `/balance` skill (`~/.claude/skills/balance/SKILL.md`): `kitlab` (build/refresh the Kit Lab artifact `https://claude.ai/artifact/BfxAferJSCtfC7qPi6Gagb`, capabilities `db` + `user`, never change them), `publish [dev|production]`, `reset`, `read`, `apply`. All logic is in `tools/balance/` (scripts table: `docs/TESTING.md` "Publishing a balance delta"); never reimplement a script.
- **Flow:** Kit Lab database → `kitlab_to_proposal.py` → `make_delta.py` (validates type, whole numbers, `[Range]`/`[Min]`, sign, magnitude, deny list, kit legality; only values that differ from the shipped assets survive) → `publish_delta.py` dry run → `--apply` to **dev** → start a build and compare `bal=` → production only as its own deliberate step. Every publish writes `tools/balance/history/<UTC>-<env>.json` plus `latest-<env>.json`; rollback restores from a history file. History files are not committed unless Maestro asks.
- **Production** needs Maestro's explicit ask in that same message, a second "publish this to PRODUCTION: yes?" confirmation and the human flag `--confirm-production`. An agent never passes the flag on its own.
- **Credentials** are the user's environment variables only: `UGS_SERVICE_KEY_ID`, `UGS_SERVICE_SECRET`, `UGS_PROJECT_ID` (the `cloudProjectId` in `ProjectSettings/ProjectSettings.asset`), optional `UGS_ENV_DEV_ID` / `UGS_ENV_PRODUCTION_ID`. Never ask for, print or store them. Gotcha (2026-11-18): the two `UGS_ENV_*_ID` variables must be environment **UUIDs**; set to the names `dev` / `production` they give HTTP 403 "Not authorized". Leave them unset and the tool looks the ids up by name. The service account is created at organization level (Account, Manage organization, Administration, Service Accounts) and given a Remote Config role per project (Admin, Editor, Viewer).
- **What a delta cannot change** (needs a build): the `BalanceBook` deny list: `AllowedClasses`, `PeckSlotPreEquippedBy`, `SignaturePreEquippedBy`, `Category`, `BotRole`, `AccentKey`, `TerrainTraversal`, `JumpTier`, `CastMotion`, `Retired`, `Subclass`, `MaxPlayers`, `TargetingMode`, `LoadoutMode`. `tools/balance/schema.py` `DENIED` mirrors it: change both together.
- **Class stats and cooldowns** are solved against the SCT axiom with `BalanceOracle` before they go into a delta: a delta bypasses the SCT tests that run on the assets (`mechanics-designer` item 8). `apply_delta.py` promotes a tested delta into the `.asset` files (BL-183); after that the published delta is redundant.
- **Not yet done:** no real delta has been published; the `dev` key holds `{}`. First publish, the `bal=` check in a build and whether the Remote Config PUT needs a separate publish step are all unverified.

---

## Google Play

Limits (name 30, short description 80, full description 4000, release notes 500 per language), graphic sizes, the store graphics in `art-src/play-store/`, upload and update steps and the console gotchas live in `docs/PLAY_STORE.md`. Read it before writing store text or cutting a Play bundle. The Play bundle is `Cluck Wars / Build / Play Store` (batch: `CluckWars.EditorTools.CluckWarsBuildMenu.BuildPlayStore`); its version code is the build number in the tag.

---

## Build + test

- `Cluck Wars / Build / Windows + Android` (`Ctrl+Shift+B`) in the Unity Editor builds both targets to `Builds/`.
- F1 in-game toggles the Debug HUD.
- `adb logcat -s Unity:* CluckWars:*` for Android device logs.
- **Unity MCP** (Open-source `IvanMurzak/Unity-MCP`) is configured — agents can call Unity Editor APIs directly (scene inspection, asset queries, etc.). See `docs/UNITY_MCP_GUIDE.md` for setup and architecture details.

Full diagnostic playbook: `docs/TESTING.md`.

---

## Branches

Since 2026-11-19 (Maestro): `develop` is the same commit as `balance`, locally and on origin. `main` is the released playtest build and stays as it is until `balance` has passed the human test (BL-124); then `balance` is merged into `main` (BL-163) and `develop` is fast-forwarded to `main`. The old `develop`, which carried the progression merge (slices 0-4) and the site and docs commits, lives on `feature/progression` (origin, tip `f9a72a3e`) and returns later by merging it into the then-current branch. Fixes for the released build branch from `main`; everything else works on `balance`/`develop`. Never rebase or force-push `develop` without Maestro saying so.

---

## Latest tag

`v0.1+26458919` and `v0.1+26458925` — the v0.1 alpha playtest builds, cut from `balance` (package `com.wugnar.games.wars.cluck`). Tags are `v<semver>+<build number>`; `git describe --tags --abbrev=0` gives the newest. `simplify-abilities` was merged into `balance` and deleted on 2026-11-14. See `docs/STATE.md` and `docs/RELEASE_NOTES_v0.1.md`.

---

## Knowledge routing and housekeeping

Where to look, by kind of question. Spawn `librarian` for a lookup you would otherwise re-derive.

| Question | Source |
|---|---|
| Current truth: how X works now, a value, a rule | The files: code, `.asset`, the rules docs. Always wins over the stores below. |
| Why / what was decided / what happened in an earlier session | MemPalace, wing `cluckwars` (`mempalace_search`, `mempalace_kg_timeline`). Rooms: `decisions`, `lessons`, `technical`, `architecture`, `diary`. |
| Shape of the C# code: module clusters, shortest path between two types, what a type calls or inherits (overview only; for every user of X use Grep, the graph under-counts references) | `graphify-out/graph.json` via `graphify query` / `path` / `explain`. C# is AST-extracted, so the graph is valid for code structure here (the global "never use the graph for code flow" note is about Godot). Check `built_at_commit` in `graph.json` against `git log` first. |
| Open work | `docs/BACKLOG.md` |

**Housekeeping** (overrides for the `housekeeping` skill):
- MemPalace: file into wing `cluckwars` (lower case, never `CluckWars`), one drawer per decision or lesson, `[LOCKED YYYY-MM-DD] title` format. Rationale and pointers only; never copy file content, never run `mempalace mine` here, never file anything containing a key or token.
- **Filing knowledge (Maestro, 2026-11-20): the agent decides and runs `/housekeeping` itself, no hooks, no schedules.** Run the `housekeeping` skill without being asked when a natural milestone is reached: the final commit of a piece of work, a merge (including a fast-forward of `develop`), a release or tag, a finished audit or decision round, or when the big work of the session is done. Skip it, and say so, when the session made no decision or lesson. Housekeeping only commits and files; it never pushes. MemPalace's own Claude Code hooks are NOT installed (they mine the whole transcript folder into one wing and mis-derive the wing on Windows), and the automatic raw sweep and curation agent that were built are paused (BL-198, both scheduled tasks disabled). `cleanupPeriodDays` is 3650 in `~/.claude/settings.json` (default 30).
- **Usage rules:** every curated MemPalace lookup passes `wing=cluckwars` (an unfiltered search is allowed only for a deliberate cross-project question). Raw captures live only in the raw palace; read them only when asked "what happened in session N", never as a source of decisions. A drawer holds a rationale plus a pointer (path, commit hash), never structure ("X calls Y"), state or values: the files, `git log` and `docs/STATE.md` answer those, and the graph is rebuilt on every commit. Never write MemPalace exports or session captures into the repo or `docs/` (graphify would ingest them). Never run `mempalace mine`, `sync` or `repair` without Maestro asking. Drawers are never linked to graph node ids. Review `~/.mempalace/curation/proposals-*.md` when `/housekeeping` runs and file the approved ones.
- The graph updates itself: graphify's git hooks (`python -m graphify hook install`, in `.git/hooks`, per clone) re-extract changed code after every commit and checkout, detached, no LLM. If `graph_state.py` still says stale, run `python -m graphify update .` (about 2 minutes).
- Structural change (what makes the graph stale if the hook did not run): a `.cs` file added, deleted or moved, an `.asmdef`/`.asmref` change, a new `Scripts/` folder. Flag the token-costing `/graphify . --update` only when docs the graph reads changed a lot.
- Test command: the EditMode suite through `TestRunnerApi` from `script-execute` (polled result file); the MCP `tests-run` times out on the full suite.

---

## Team

**Maestro** — Senior Engineer, telco background, Unity/C#/Android expertise, Madrid.

<!-- backlog-skill --> Open work lives in `docs/BACKLOG.md` (one line per item; list with `grep "^- \[ \]" docs/BACKLOG.md`; add/close via the `/backlog` skill).
