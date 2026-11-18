# Unity Pipeline 1.0 upgrade (run AFTER alpha)

Researched 2026-11-17/18. Backlog item points here. Source of truth for the plan is this file; re-check the release notes before running, they move fast.

## Versions and dates
- Now: `com.unity.pipeline` 0.9.1-exp.1 (adopted 2026-10-30, see `docs/STATE.md`). Editor 6000.3.14f1.
- Target: `com.unity.pipeline` **1.0.0** stable, released 2026-11-12 (forum post 2:24pm). Promoted from 1.0.0-pre.1 (2026-11-10) with no code changes.
- Unity CLI 1.0.0 also 2026-11-12; CLI 1.0.1 on 2026-11-16; announced out of beta at Unite Tokyo (blog dated 2026-11-17). Unity promises commands, flags and output stay compatible across 1.x, with deprecation warnings first.
- No minimum Editor version is stated for the package.
- Install: `unity pipeline install --package-version 1.0.0 --force`; CLI: `unity self-update` (keeps last 3 versions, `--rollback` available).

## Benefits for Cluck Wars
1. **Tests**: `unity test` with `--affected`, `--rerun-failed`, `--junit-output`; `test status --detail` returns only failures. Fixed: exit 0 on failed tests, runs continuing after `test cancel`, duplicate or missing results around domain reloads. Targets the ~3000-test suite that hits the MCP `tests-run` 5-minute timeout, and the wedged-runner hangs.
2. **Builds**: `unity build` goes through the already-open Editor (no batchmode lock conflict). A missing or wrong-platform Build Profile is refused instead of silently building with current settings.
3. **Captures**: `capture game_view` works in batchmode and says when there is no new frame. Relative `--output-path` resolves from the project root, not `Assets/` (removes the save_path-under-Assets gotcha and stray `.meta`).
4. **Profiling**: `diagnostics profiler_record`, `memory_snapshot`, slowest-frame ranking by CPU/GC, capture analysis. For the 30 fps mid-range Android target. UNVERIFIED against an Android dev Player (runtime workflow is marked desktop-only).
5. **Project Auditor**: `unity projects audit` with a URP or optimization focus (Unity 6+).
6. **VCS**: `unity vcs diff` / `summarize` names the asset behind each scene or prefab change. Helps `Map.unity` diffs and parallel sessions.
7. **CI**: `unity ci init` (GitHub, GitLab).
8. **Small**: `component set_field` handles `[Serializable]` structs and `[Flags]` enums by name; object paths can point inside prefabs (`Assets/UI/Screen.prefab:Canvas/Header`); `editor.playmode start` fails on compile errors.

Not useful to us: Scene Preview, Multiplayer Play Mode fixes (need 6000.5+), Multiplayer Roles (6000.6+).

MCP: Unity de-emphasises its own MCP (`unity setup all` no longer registers it for shell-capable agents). Ours is the third-party `ai-game-developer`, unaffected. Keep both, per the 2026-10-30 decision in `docs/STATE.md`.

## Risks and breaking changes (audit first)
- Removed, no redirect: `capture screenshot`, `script eval_file`. Use `capture game_view` / `scene_view --output-path` and `script eval --file`.
- Refused: old parameter names `outputPath`, `save_path`, `since_session`, `group_by`, and any unknown parameter (`INVALID_COMMAND_ARGS`). The CLI must be updated too.
- Exit codes changed (server's own start at 100; a batch timeout exits 14). Scripts that check specific codes need updating.
- `test run` / `test status` replies are camelCase (`summary.total`, `results[].fullName`); `test run` defaults to editor mode.
- Editor server settings moved from `ProjectSettings/` to `UserSettings/`.
- Hot patching only reads sources under `Assets/` and loaded packages.

## Steps when run
1. Grep `docs/TESTING.md`, `docs/STATE.md`, `tools/`, `.claude/` for the old command names and parameter spellings; fix them.
2. `unity self-update`, then `unity pipeline install --package-version 1.0.0 --force` on a branch.
3. Rerun the EditMode suite with `unity test` and compare against the TestRunnerApi result. Judge `Phase8ChunkD1Tests.FullMound_...Mobile_centre` by a batch run.
4. Try `unity build`, `capture game_view --output-path`, `projects audit`, and a profiler recording on a desktop dev build.
5. Update `docs/STATE.md` and the "Tools in scope" gotchas in `.claude/CLAUDE.md`.

## Sources
- https://unity.com/the-unity-cli-1.0-is-here
- https://docs.unity.com/en-us/unity-cli/release-notes/2026/november
- https://discussions.unity.com/t/unity-pipeline-1-0-0-is-out/1741740
- https://discussions.unity.com/t/unity-pipeline-1-0-0-pre-1-is-out/1741564
- https://discussions.unity.com/t/unity-pipeline-0-10-0-exp-1-is-out/1741046
