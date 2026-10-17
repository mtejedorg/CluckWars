# Handoff — Phase 8 chunk D remainder (3D environment polish) — 2026-10-17

**For:** a future Sonnet agent (Maestro's request). Execute top to bottom. One Unity Editor, so one agent drives it at a time.
**Repo:** `C:/Users/MARCO/Documents/GitHub/CluckWars`, branch `develop`.
**Read first:** `CLAUDE.md`, `docs/STATE.md` (sections "PHASE 8 chunk D", "PHASE 8 FINAL AUDIT", "PHASE 8 chunk C"), `docs/CONVENTIONS.md`, `docs/ART.md` §2–§6.14.
**Binding design decisions:** memory `env3d-decisions-2026-10-15.md` (D1–D9, E1–E5, follow-ups). Don't re-litigate them.
**Audit page with all findings:** https://claude.ai/artifact/8j5NN2zzB1rWAecqMoJbjm (v3). Findings L01–L22 map to the items below. Evidence is in `Captures/env3d_final/`.

## Already done in chunk D (don't redo)

Each item has a row in the STATE.md chunk D table.

| Item | Commit |
|---|---|
| D16 capture tools follow the tier | `7df0d9a` |
| D1 mound gold | `53121b1` |
| E4 Fatty buff hen | `64aba06` |
| D2 PC dark tones | `5ba9d42` |
| E3 grass −20 % chroma, tiling 5, detail map | `419d2d0` |
| E1 rival ring + chevron in player colour | `a76fb6e` |
| E2 fence holes for own pad + home piles | `c4a29d6` |
| D7 pennant | `3fac1ac` |
| D8 ring ghost arc | `7e2556b` |
| D9 ring ink edge | `574e7d1` |
| D10 fence-hole core clear | `2d9c21d` |
| D3 mound volume + cost-tool Play-mode guard | `7d6f822` |
| D4 PC surround grass texture + hedge contrast/scalloped top (haze copies re-baked) | `3f89619` |
| D5 flower decals recoloured, mud decals dark wet blue-grey | `e3b8e27` |
| D6 dirt paths below grass luma, edge-only wear | `7fce027` |

Last full EditMode run at `a533bc1`: **1808/1808**. Performance Mode pref is 1 in this Editor (as found).

## How to drive Unity (no MCP tools — use the HTTP bridge)
- Python: `import sys; sys.path.insert(0, "C:/Users/MARCO/Documents/GitHub/CluckWars/tools/ui_capture"); import bridge; bridge.init(); bridge.call("<tool>", {...}, timeout=...)`.
- Probe with `editor-application-get-state`. Useful tools: `script-execute`, `assets-refresh`, `console-get-logs`, `editor-application-set-state` (`{"isPlaying":bool}`). Working C# examples: `tools/ui_capture/capture_env3d.py`.
- EditMode tests: `python tools/ui_capture/run_editmode_tests.py` prints a SUMMARY plus failures and reopens Bootstrap. Every open scene must be saved first. The baseline is the latest count in the STATE.md chunk D table, all green.
- **Launch** Unity: `"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe" -projectPath "C:\Users\MARCO\Documents\GitHub\CluckWars"`.
- **Gotchas seen in this phase:**
  - **"Recovering Scene Backups" on launch:** a stale `Temp/__Backupscenes` folder, even an empty one, causes it. Close Unity, move the folder out of the project (don't delete it), relaunch.
  - **Bridge times out while Unity is alive:** usually the Editor is stuck in Play mode, often on the match-end screen after `measure_phase8_cost.py`. Exit Play mode. If it won't respond, restart Unity; Play-mode state is never saved.
  - **Performance Mode:** the PlayerPrefs `CluckWars.PerformanceMode` was 1 in this Editor. D16 makes the capture tools set, log and restore it per pass. Default captures run with it OFF.
  - **Before any Play-mode capture, check that Bootstrap is the open scene.** The test run can leave an empty Untitled scene open.
- **Map.unity is BINARY:** verify changes by an object inventory dump, not by git diff.
  - **Image generation (ComfyUI):** only with Unity CLOSED. Prefer PIL or headless Blender edits of the existing textures and meshes. Headless Blender: `"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b --python <script> -- <args>`.

## Rules (from CLAUDE.md, enforce)
- `ILogService` only (no `Debug.Log`); URP materials only; UXML/USS for menu UI; `HasStateAuthority` gate in `FixedUpdateNetwork`.
- Don't change gameplay or balance values (MatchConfig, ability values, PinwheelLayout geometry, base inset, class stats — the SCT oracle test at ±0.5 s guards these).
- Never pass model "fable". No cravify or Antigravity. No `mcp__unity-mcp__*`.
- After each item:
  - run the full EditMode suite, all green;
  - take a MATCH-CAMERA capture for anything visual (lesson: overviews hid a fence regression);
  - assert features and relationships, not just numbers;
  - commit and push, with explicit-path `git add` and a message ending "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>";
  - add a STATE.md chunk D row.
- If `docs/site/index.html` (the compendium) changes, republish it to https://claude.ai/artifact/1eP5RtYLXhCBmVziDe9SNZ.
- If something would change the look or design a lot beyond this document, stop that item and ask Maestro.

## Remaining items (in this order)
1. **D11 Prop holes: work in progress is in `git stash`** ("Phase 8 D11 WIP …"; its STATE row is in `a533bc1`). Run `git stash pop` and REVIEW it. It repairs feed_sack, trough, milk_churn, wheelbarrow, scarecrow, nest_basket and oak_tree, and adds a see-through survey over every used generated prop. It adds about 30k tris across the map, so measure Mobile cost before committing. If the cost is too high, prefer a liner only where holes face the match camera. Run the full suite (its own run reported 1817), then commit. The original spec, if anything is missing:
   - Run the C14 repair (weld 0.5–1 mm, fill small loops, outward normals, inner liner) on `feed_sack`, `trough`, `milk_churn`, `wheelbarrow` and `scarecrow`. Keep names and bounds.
   - Add a see-through-pixel survey over EVERY `Assets/Generated` prop, not a hand-picked list.
2. **D12 Overlaps.**
   - Props outside the fence (fence–hedge gap, hay bales and sack) clip into the hedge: move them clear and extend the overlap test to the HedgeStrip bounds.
   - At each base the scarecrow touches the coop: slide each scarecrow about 1.2 m along its fence. Keep it 4-fold, and keep the A5/C9 rules (≥ 4 m from arm tips; tall dressing out of the rim openings).
3. **D13 Rim paths.** Each base's rim path hugs its two home fences, so the owner never sees it. Re-bake the first stretch 2–2.5 m off those fences (`GroundDressingBaker`), then bring it back into the gap. No colliders, so the NavMesh is unaffected.
4. **D14 Pile slow-band marking.** Chickens slow to 0.8× within 1 m (`_collectReach`) of a pile surface, and only the centre pile shows it. Add a faint worn-grain apron about 1 m around each pile (fades with fill, off when empty), or keep paths ≥ 1.5 m off piles they don't serve. Pick the cheaper readable one.
5. **D15 Mirrored piles.** `PileFootprintShape` folds yaw with `%= 180`, so opposite irregular piles are mirror images. Use the full atan2 yaw for the VISUAL only (the collider and NavMesh ellipse are symmetric, so they don't change), then re-bake the discs (`ZoneMarkerBaker`).
6. **D17 Leftovers.**
    - Paths show through the straw pad as thin lines (both at y 0.012): cut the paths at the pad radius 2.5 m, or lift the pad to 0.016 (under the ring at 0.02).
    - Trim `GeneratedGround` to the fence + 0.1 m so no lime strip shows in the fence–hedge gap.
7. **D18 Pipeline and docs.**
    - `PC_RPAsset`: turn off light layers, reflection-probe blending and box projection. Edit via Unity APIs or SerializedObject, diff the YAML, and check for pink shaders (history: the Mobile_RPAsset corruption).
    - Update ART.md §2: PC = Neutral TM + bloom + haze pass (depth via the SSAO prepass); Mobile = LDR + baked haze.
8. **Shader warm-up**, only if cheap and safe. Record a `ShaderVariantCollection` (or a Unity 6 GraphicsStateCollection) from one Play session with the chicken ghost (Invisibility / Smoke Roost / stun) and fence holes (own, rival, E2 pad) active, plus the newer variants: E3 grass detail map, D4 hedge crown + textured surround grass, D3 mound occlusion map, D8 ring ghost. Warm it up in Bootstrap. The Pixel 9 session then only verifies there is no first-use hitch.
9. **Final re-capture**, Performance Mode OFF and logged:
    - `python tools/ui_capture/capture_env3d.py Captures/env3d_after/D/final`;
    - original / re-audit / final / D-final pairs vs `Captures/env3d_final/`;
    - Mobile cost, 3 runs (`measure_phase8_cost.py`), plus one centre-pile run with live bots if the tool supports it;
    - a STATE.md "chunk D COMPLETE" summary and ART.md;
    - a short report to Maestro.

## Open questions for Maestro (ask before doing either)
1. **Doppelganger decoy colours.** Decoys have no player seat, so after E1 their rival ring and edge arrow are grey, matching their already-grey nameplate dot. Giving them the caster's colour would mean touching gameplay lookups. *Recommendation: leave them grey.* It matches today's nameplate and reveals nothing extra about the decoy.
2. **Edge arrows (screen-edge chevrons).** An arrow can draw on top of a rival that is still partly on screen, and the bottom-left arrow spot sits inside the mobile joystick area. *Recommendation: fix both as UI polish:* hide a rival's arrow while the rival is on screen, and keep arrows out of the joystick and other control safe-zones. Code: `Assets/_Game/Scripts/Visuals/RivalIndicator.cs`. Route through ui-designer.

## Still for the Pixel 9 + gamepad session (not this handoff)
- GPU frame time at the fixed corner and at the centre pile with 4 live chickens, Performance Mode on and off. That data decides E5 (Performance Mode scope: soft shadows, render scale, outside props, a ~4.5k-tri chicken LOD).
- Mobile shadow bias and distance (normal 1 → 0.4, depth 1 → 0.5, distance 30 → 26).
- Performance Mode twin-fence memory.
- The ghost, fence-hole and haze shaders on a real GLES3/Vulkan device.
- Haptics, edge cancel, Android Back, two-peer (coop-test skill).
