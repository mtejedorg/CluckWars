# CLAUDE.md — Cluck Wars Entry Point

Read this first. It's a thin index — the real content lives in the linked
docs below. Update each doc in place when its concern changes; this file
should rarely need edits.

---

## Project at a glance

**Cluck Wars** is a fast-paced 4-player free-for-all arena game. Players pick a chicken class, collect food from piles, deposit it at their base, sabotage rivals. First to 150 food units (or most at 3 minutes) wins.

- **Engine:** Unity 6000.3 LTS · **Pipeline:** URP
- **Networking:** Photon Fusion 2 — Shared Mode for demo (LAN/cloud-relay), Server Mode post-funding
- **DI:** Zenject (Extenject) · **Backend:** UGS behind `IUGSService` (`NullUGSService` in demo)
- **Architecture:** MonoBehaviour — no ECS/DOTS
- **Platforms:** Windows (primary dev), Android (mobile target), iOS (post-demo)
- **Target FPS:** 60 Windows, 30 stable on mid-range Android (2021+)

---

## Doc map — where to look

| If you want to know… | Read |
|---|---|
| **Where are we right now?** Current state, what works, latest tag, recent commits, open bugs, what's deferred. | `docs/STATE.md` |
| **How does the code fit together?** Scene graph, DI scopes, networking model, chicken architecture, ability system, asset locations. | `docs/ARCHITECTURE.md` |
| **What rules / patterns must I follow?** Self-injection, RPC patterns, networked-state defaults, footguns we hit and how to avoid them. | `docs/CONVENTIONS.md` |
| **How do I build / run / diagnose?** Build menu shortcuts, debug HUD (F1), adb logcat commands, source-tag reference, diagnostic flows per bug type. | `docs/TESTING.md` |
| **What's the plan, what's shipped, what's next?** Per-phase status, dedicated test session work list. | `docs/ROADMAP.md` |
| **Why does the design look like this?** GDD / TDD / ART. | `docs/GDD.md` / `docs/TDD.md` / `docs/ART.md` |
| **How does the open-source Unity MCP work?** MCP architecture, UI Toolkit rules, asset structure, and Claude integration. | `docs/UNITY_MCP_GUIDE.md` |

---

## Quick start for agents

When you begin a session:

1. `cat docs/STATE.md` — orient yourself to current state.
2. Skim `git log --oneline -10` — what just changed.
3. If the user asks for a change in a system you haven't touched before, check `docs/ARCHITECTURE.md` for how it's wired.
4. Before making nontrivial changes, skim `docs/CONVENTIONS.md` — the project has a few hard-won rules (self-injection pattern, RPC patterns, `LogLevel`-name collision with Fusion, `ChickenClass : byte` quirk, etc.).
5. After making changes, **update `docs/STATE.md`** so the next agent doesn't lose context. ROADMAP entries get ticked too when phases complete.

---

## Build + test

- `Cluck Wars / Build / Windows + Android` (`Ctrl+Shift+B`) in the Unity Editor builds both targets to `Builds/`.
- F1 in-game toggles the Debug HUD.
- `adb logcat -s Unity:* CluckWars:*` for Android device logs.
- **Unity MCP** (Open-source `IvanMurzak/Unity-MCP`) is configured — agents can call Unity Editor APIs directly (scene inspection, asset queries, etc.). See `docs/UNITY_MCP_GUIDE.md` for setup and architecture details.

Full diagnostic playbook: `docs/TESTING.md`.

---

## Latest tag

`v0.3.0-alpha` (commit `312f511`) — Pre-test polish + build tooling. See `docs/STATE.md` for the full list.

---

## Team

**Maestro** — Senior Engineer, telco background, Unity/C#/Android expertise, Madrid.
