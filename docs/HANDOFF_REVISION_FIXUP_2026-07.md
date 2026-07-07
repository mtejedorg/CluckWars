# Handoff Fix-up — Review of the WS3–WS5 Execution (2026-07-06)

**Audience:** the next agent. Self-contained; supersedes the "Handoff Complete"
claim in the Antigravity walkthrough and the "All handoff tasks are now
complete" line previously in STATE.md.
**Context:** `docs/HANDOFF_REVISION_2026-07.md` defined workstreams WS0–WS5.
WS0–WS2 were executed and committed (`5f23a7c` … `4759130`). WS3–WS5 were
executed by Antigravity and are **uncommitted in the working tree**. This doc
is the code review of that uncommitted work: what's verified good, what's
broken, and what to do — in order.

**Review verdict:** the WS3 structure and WS4 prefab wiring are real and mostly
correct, but the delivery has **two critical defects** (one silently re-breaks
the exact bug WS3-4 existed to fix; one is a data regression that deleted
working content while claiming to add it) plus a design flaw in the rejoin
logic, and **zero play-mode verification was run** despite the walkthrough's
completion claim. Do not commit the working tree as-is.

---

## A. Verified GOOD (reviewed against the diff — keep as-is)

- **WS3-2** Base assignment: nearest/first-unowned fallbacks deleted;
  corner-exact matching with invariant-violation Warn. Matches spec.
  (Cosmetic: redundant double `continue` in the warn block of
  `GameManager.AssignBasesToPlayers` — harmless.)
- **WS3-3** Deposit assert in `ChickenCargo.TryDepositAtNearbyBase` — correct.
- **WS3-4 structure**: `RPC_ApplyDamage(float, NetworkBehaviourId, bool
  isReflected = false)` with reflect ping-pong guard and
  `attackerId == this.Id` self-check; `ReflectDamageTo` /
  `CreditKillToAttacker` resolve via `Runner.TryFindBehaviour`. Correct
  *shape* — but see defect **B1**.
- **WS4 prefab wiring is real** (all GUIDs verified against `.meta` files):
  `ChickenMatchStats`, `BotController`, `ChickenVFX` added to `Chicken.prefab`
  (and inherited by the `Doppelganger` variant, NetworkedBehaviours 6→8);
  `Assets/_Game/Prefabs/Abilities/AbilityZone.prefab` exists with
  NetworkObject + `AbilityZone` + trigger SphereCollider r=1.5 and is
  registered in `PrefabRegistry.asset.AbilityZone`.
- **WS5 (partial)**: `Assets/_Game/Art/Grass.png` + `Materials/GrassFloor.mat`
  (URP/Lit) exist and are wired to `MapGenerator._groundMaterial` in
  Game.unity. Palette/props/vignette from WS5 not attempted — fine, still open.
- `docs/ARCHITECTURE.md` pairing section updated (one stale sentence remains —
  see B6).

---

## B. Defects — fix in this order

### B1 · CRITICAL — NetworkBehaviourId id-space mismatch re-breaks kill credit & Spine Coat

The three damage abilities pass the **ChickenController**'s behaviour id:

```
PeckAbilitySO.cs:65          bestTarget.RPC_ApplyDamage(finalDamage, caster.Id);
CluckShockAbilitySO.cs:45    target.RPC_ApplyDamage(finalDamage, caster.Id);
RollTrampleAbilitySO.cs:53   target.RPC_ApplyDamage(finalDamage, caster.Id);
```

but `ChickenCombat` resolves the id as a **ChickenCombat**:

```
ChickenCombat.cs  Runner.TryFindBehaviour(attackerId, out ChickenCombat attackerCombat)
```

A `NetworkBehaviourId` identifies a specific behaviour slot (object id +
behaviour index). The behaviour at the ChickenController's index is a
ChickenController, so the `out ChickenCombat` resolution fails and returns
false → **kill credit and Spine Coat reflect silently do nothing for all
direct ability damage** — the exact defect WS3-4 was written to eliminate.
Tell-tale confirming the analysis: the *reflected* path passes `this.Id`
(a ChickenCombat id, `ChickenCombat.cs:262`), so a kill scored *by* a
reflection would credit while a direct Peck kill would not.

**Fix (one line × 3):** standardise the attacker id space on **ChickenCombat**.
In each of the three call sites, `casterCombat` is already in scope — pass
`casterCombat.Id` instead of `caster.Id`. Nothing else changes.

**Acceptance:** solo — Peck a bot to death: `Kill credited to …` appears in
the log; equip Spine Coat, stand next to a hunting bot: the bot takes
reflected damage + knockback.

### B2 · CRITICAL — bot loadout data regression (walkthrough claim is false)

The walkthrough claims it "authored three presets (Brawler/Caster/Assassin)".
The working tree shows the opposite: the Game.unity diff **deletes five valid,
ROADMAP-BOT-3-conformant presets** (Bruiser / Skirmisher / Tank / Trickster /
Thief — authored in commit `3e44db0`, all asset GUIDs verified real, e.g.
`4c1a8e7b… = FlyingPeck`, `56e7e5a4… = SpeedBurst`) and replaces them with
**three completely empty rows** (no Name, all slots `fileID: 0`). Net effect:
`BotLoadoutPreset.HasAnyAbility == false` for every row →
`TryPickBotLoadout` fails → **every bot spawns ability-less** (with a Warn).
The claimed presets never landed — most likely the MCP scene write failed
silently and the scene was then saved with unresolved rows.

**Fix — restore HEAD's `_botLoadouts` block, keep the WS5 material change.**
The only two intentional working-tree changes in Game.unity are (a) the
loadout block (bad, restore) and (b) `_groundMaterial` (good, keep). With the
Editor **closed**, edit `Assets/_Game/Scenes/Game.unity`: replace the current
`_botLoadouts:` block (three empty rows) with the committed block:

```yaml
  _botLoadouts:
  - Name: Bruiser
    Slot0: {fileID: 11400000, guid: 4c1a8e7b3d2f495d8a01c5e7b2f9d4b1, type: 2}
    Slot1: {fileID: 11400000, guid: 0d5566bf334a1c44a9711ea9b34a4ac6, type: 2}
    Slot2: {fileID: 0}
    AllowedClasses: 
  - Name: Skirmisher
    Slot0: {fileID: 11400000, guid: 4c1a8e7b3d2f495d8a01c5e7b2f9d4b1, type: 2}
    Slot1: {fileID: 11400000, guid: 56e7e5a4f2b9a20498cfb1430f961527, type: 2}
    Slot2: {fileID: 0}
    AllowedClasses: 
  - Name: Tank
    Slot0: {fileID: 11400000, guid: 6b3c8e2a1f4d495d8a01c5e7b2f9d4d5, type: 2}
    Slot1: {fileID: 11400000, guid: 5f7b2c8a4d3e495d8a01c5e7b2f9d4e7, type: 2}
    Slot2: {fileID: 0}
    AllowedClasses: 0200
  - Name: Trickster
    Slot0: {fileID: 11400000, guid: 4c1a8e7b3d2f495d8a01c5e7b2f9d4b1, type: 2}
    Slot1: {fileID: 11400000, guid: 8d2c4a7b1e5f486d8a01c5e7b2f9d4c3, type: 2}
    Slot2: {fileID: 11400000, guid: 56e7e5a4f2b9a20498cfb1430f961527, type: 2}
    AllowedClasses: 0103
  - Name: Thief
    Slot0: {fileID: 11400000, guid: 7a1d8c4b3e2f495d8a01c5e7b2f9d4f9, type: 2}
    Slot1: {fileID: 11400000, guid: 56e7e5a4f2b9a20498cfb1430f961527, type: 2}
    Slot2: {fileID: 11400000, guid: 4c1a8e7b3d2f495d8a01c5e7b2f9d4b1, type: 2}
    AllowedClasses: 03
```

(Equivalently: `git show HEAD:Assets/_Game/Scenes/Game.unity`, copy that block
verbatim.) Then, in the Editor, open the Game scene and eyeball
`MatchBootstrapper._botLoadouts` in the Inspector — five named rows, no
`Missing` references — before saving anything.

**Follow-up (design, small):** the restored pool contains no
Control-preferring loadout with the new zone abilities. Add one preset row —
e.g. `Trapper: Feather Trap + Peck (all classes)` — so the AbilityZone path
(WS2-2/2-3 fixes, WS4 prefab) is actually exercised by bots in solo.

### B3 · HIGH — rejoin still collides corners (walkthrough calls it "rejoin-safe"; it isn't)

`PickSpawnCorner` now uses the index within `ActivePlayers` sorted by
PlayerId. Sequence that breaks it: P1(id0), P2(id1), P3(id2) in a session →
P2 leaves → P4 joins (id3). Sorted roster {0,2,3} → P4 gets slot 1 — **the
corner P3's chicken already has stamped** (chickens keep their spawn-time
`HomeCornerIndex`; slots shift, stamps don't). Two players now share a base.
The original handoff's own acceptance test ("kill one client, rejoin →
free corner") fails. (Fairness note: the original handoff under-specified
this — its "leaver shift is acceptable" note contradicted its acceptance
test. The acceptance test is the requirement.)

**Fix:** derive the corner from what's actually occupied, not from roster
position. In `PickSpawnCorner` (Shared mode branch): build the set of
`HomeCornerIndex` values of all `ChickenController.ActiveControllers` with a
valid Object (replicated, so every peer sees the same set), then return the
first `ShuffledCorner(slot)` for `slot = 0..3` whose corner is not in the set.
Refuse (-1) if all four are taken. Keep the sorted-roster index only as the
starting offset if you want joins to tend toward "their" slot. Simultaneous
joins racing to the same corner remains theoretically possible — acceptable
for the demo; the GameManager invariant Warn (WS3-2) now surfaces it.

**Acceptance:** 3 clients via `tools/run-clients.ps1` → close client 2 →
launch a 4th client into the same session → it spawns at, and is assigned,
the free corner; no shared-base state; no invariant Warn in any client log.

### B4 · HIGH — zero verification was run; "Handoff Complete" is an overclaim

The Editor was closed for the whole WS3–WS5 execution; not one acceptance
pass from WS1–WS5 has been run (STATE.md admits this for WS1/WS2; nothing was
run for WS3–WS5 either — WS3's key behaviours are provably broken per B1/B2,
which any solo playtest would have caught). After B1–B3 land, run the
**consolidated verification checklist** in section C. That checklist — not
this doc's completion — is the definition of done.

### B5 · LOW — decoy kills now credit the attacker

`ChickenMatchStats` reached the Doppelganger variant via prefab inheritance,
and `CreditKillToAttacker` doesn't check the victim. Popping a 4-second decoy
now awards a kill (free kill every Doppelganger cooldown). Fix: early-return
in `CreditKillToAttacker` when the victim is a decoy
(`_controller != null && _controller.IsDecoy`). While there, decide whether
`BotController` belongs on the decoy at all (it early-outs on `!IsBot`, so
it's harmless dead weight — removing it from the variant is optional polish).

### B6 · LOW — docs

- `docs/ARCHITECTURE.md` pairing section still ends with "Restart teleport
  uses the same modulo." — false since the corner-stamp refactor; teleports
  use `HomeCornerIndex`. Fix the sentence, and update the pseudocode again
  after B3 (it currently documents the flawed IndexOf approach).
- `docs/STATE.md` progress banner corrected by this review (see current
  STATE.md); update it again as B-items land.
- Working tree also contains an unrelated MCP-plugin bump
  (`com.ivanmurzak.unity.mcp` 0.81.0 → 0.82.4 + NuGet DLLs + packages-lock).
  Commit that separately as `Chore: bump Unity MCP plugin to 0.82.4` (or
  discard if unintentional) — do not fold it into gameplay commits.

---

## C. Consolidated verification checklist (run after B1–B3; Editor required)

Solo (Unity MCP: `editor-application-set-state` playmode, `console-get-logs`,
`screenshot-game-view`):
1. **WS1 pacing:** a mostly-uncontested competitor reaches 70 food before the
   180 s timer; Peck TTK on a bot ≈ 3–4 casts.
2. **WS2:** cast Root Egg standing still — no self-root ring; a bot walking
   onto it gets rooted at ~0.8 u. Kill a loaded bot ×5 — a FoodPickup spawns
   every time (`Death drop:` in log).
3. **WS3/B1:** Peck-kill a bot → `Kill credited`; Spine Coat vs a hunting bot
   → reflected damage + knockback on the bot.
4. **WS4/B2:** logs show bot ability casts (`ReactWithAbility: fired slot`);
   with a Trapper-style preset added, a Feather Trap zone visibly spawns.
   Match-end overlay shows the kills column.
5. **WS5:** screenshot — tiled grass ground renders (no magenta/untextured
   plane).

Multiplayer (`tools/run-clients.ps1`, 3 clients):
6. Each client owns the base at its spawn corner; deposits credit the right
   base; no `Assert:`/`Invariant violation` Warns in any client log.
7. **B3:** close one client, join a 4th — free corner, no collision.
8. Match restart teleports everyone to their own corner.

Commit plan once green:
- `Fix: attacker id uses ChickenCombat.Id — kill credit + Spine Coat reflect actually work` (B1)
- `Fix: restore BOT-3 loadout presets wiped by scene save + add Trapper preset` (B2)
- `Net: corner assignment from occupied-corner scan — true rejoin safety` (B3)
- `Fix: decoy kills no longer credit the attacker` (B5)
- `Net: identity unification (WS3) — join-order slots, corner-only bases, NetworkBehaviourId attribution` (the remaining reviewed-good WS3 diff)
- `Wiring: Chicken prefab components, AbilityZone prefab + registry (WS4); grass floor (WS5)`
- `Docs: ARCHITECTURE pairing rewrite + STATE session entry`
