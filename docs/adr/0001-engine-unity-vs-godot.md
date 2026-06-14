# ADR 0001 — Game engine: stay on Unity (vs. Godot)

- **Status:** Accepted
- **Date:** 2026-06-13
- **Deciders:** Maestro
- **Context tags:** engine, networking, AI-driven workflow

## Context

Cluck Wars is built in **Unity 6 + URP**, networked with **Photon Fusion 2**
(Shared Mode now, Server Mode post-funding), DI via Zenject, auth/lobby via UGS.
It targets Windows + Android (+ iOS post-demo) and is developed **almost entirely
through an AI agent driving the Unity MCP** (`unity-mcp-cli`).

The question was raised: would **Godot** be a better fit, given the project is
online, multiplatform, AI-built, and has an existing Supercell-style UI design?

## Decision

**Stay on Unity.** Do not migrate to Godot at this stage.

## Rationale

Honest weighing of the dimensions that matter for this project:

| Dimension | Verdict |
|---|---|
| **Online netcode** (competitive 4-player FFA) | **Unity wins decisively.** Photon Fusion 2 gives tick simulation, client prediction, relay — already integrated and working. Godot's high-level multiplayer API has no built-in prediction/rollback; competitive feel would mean rolling our own netcode or adding Nakama. This is the hardest part of the project and it's already solved on Unity. |
| **AI-driven development** | **Godot has a real edge** (text `.tscn`/`.gd`, no GUID/`.meta`/compile-registration friction — exactly the pain hit in the 2026-06-13 session; see [[unity-import-gotcha lesson]]). **But** the mature Unity MCP pipeline this project depends on has no equal in Godot's ecosystem yet. Net: friction favours Godot, tooling maturity favours Unity. |
| **Multiplatform** | Roughly a wash. Godot builds are lighter; Unity's are proven on the Pixel 9. Not decisive. |
| **UI design** | Slight Godot edge (single coherent Control+Theme stack vs. the UGUI/UI-Toolkit split this project bled over). Design intent in `Design/*.jsx` is engine-agnostic. Not worth a rewrite alone. |
| **Migration cost** | **Decisive against switching.** ~80 C# scripts, working networked loop, abilities, bots, UGS, build + coop-test tooling. Even Godot C# wouldn't port `NetworkBehaviour`/Fusion or the node model — it's a near-total rewrite, and the part rewritten first/hardest is the netcode already working. |

**Bottom line:** switching trades away working code + best-in-class netcode + a
mature AI pipeline to gain editor-friction reduction and a cleaner UI stack — a
clearly negative trade at this stage. (If starting from scratch today, Godot would
deserve a serious look, primarily for the AI-editing friction reduction.)

## Consequences

- Continue investing in the Unity + Fusion + UGS + Unity-MCP stack.
- Accept the Unity asset-pipeline friction in the AI workflow; mitigate per the
  import-gotcha lesson (create new `.cs` via `script-update-or-create`, not raw
  disk writes, when the editor may be mid-reload).

## Reconsider trigger

Re-open this decision **only if** real-device multiplayer testing shows Fusion
Shared Mode can't deliver acceptable competitive feel and forces a large netcode
rebuild anyway. At that point the sunk-cost argument weakens (we'd be rewriting the
hard part regardless), and a fresh Unity-vs-Godot evaluation is warranted.
