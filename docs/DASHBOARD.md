---
status: live
owner: Maestro
type: dashboard
---

# Dashboard

Links and commands only. No numbers are copied here, so nothing on this page goes stale. Obsidian guide: [OBSIDIAN.md](OBSIDIAN.md).

## Where are we

- [STATE.md](STATE.md): live snapshot, read it first after `CLAUDE.md`.
- [ROADMAP.md](ROADMAP.md): phases shipped and next.
- [BACKLOG.md](BACKLOG.md): open work, one line per item.

List open backlog items (run in the repo root):

```powershell
Select-String -Path docs/BACKLOG.md -Pattern '^- \[ \]'
```

Add, close or re-prioritise items with the `/backlog` skill, not by hand.

## How it fits together

- Map: [architecture.canvas](architecture.canvas).
- [ARCHITECTURE.md](ARCHITECTURE.md): scene graph, DI scopes, networking.
- [ARCHITECTURE_RULES.md](ARCHITECTURE_RULES.md) and [CODE_RULES.md](CODE_RULES.md): rules with stable IDs and what enforces them.
- [CONVENTIONS.md](CONVENTIONS.md): footguns and patterns.
- [FILE_STRUCTURE.md](FILE_STRUCTURE.md): where each thing lives.

## Design

- [GDD.md](GDD.md), [TDD.md](TDD.md), [ART.md](ART.md), [FEEDBACK.md](FEEDBACK.md).
- Class essence: [design/class-essence-and-signatures.md](design/class-essence-and-signatures.md).

## Test and build

- [TESTING.md](TESTING.md): build menu, debug HUD, diagnostic flows.
- [PLAY_STORE.md](PLAY_STORE.md): store listing limits and upload steps.

## All docs by status

Open [docs.base](docs.base) in Obsidian for a table of every doc grouped by `status`.

## History

- [notion-import/](notion-import/README.md): pages archived from Notion on 2026-11-20. History only.
