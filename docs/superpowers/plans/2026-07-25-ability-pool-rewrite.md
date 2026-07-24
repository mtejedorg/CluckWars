# Ability Pool Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Rebuild the ability pool for the control+steal model — Steal/Control/Defense/Utility categories, every ex-damage ability converted to steal, the new roster (Warrior as the ability class, Fatty gains Cluck Shock), and the three new abilities (Ambush, Wing Slam, Shadowstep) plus the Mark/Kill slot wiring.

**Architecture:** Abilities are `AbilityBaseSO` subclasses with `OnActivate(AbilityContext)`, using `SearchMask` + `Physics.OverlapSphere` and cross-authority RPCs. The steal cap already exists (`SneakySteal` + `RPC_DrainStolen`); this plan extracts it to `StealMath` (combat plan Task 2) and reuses it everywhere. Class assignment is data: `AbilityBaseSO.SlotKind` (Common/Character) + `AllowedClasses` bitmask (Warrior=1, Speedy=2, Fatty=4, Assassin=8). This plan closes the temporary no-op stubs the combat plan's Task 5 left on the ex-damage abilities.

**Tech Stack:** C# ScriptableObjects, Fusion RPCs, Unity MCP (`assets-modify` on `.asset` SOs, `assets-find`, `console-get-logs`, play-mode), NUnit EditMode (`AbilitySystemTests`, `DataIntegrityTests`).

## Global Constraints

- **Depends on the combat plan** (needs `StealMath`, `RPC_ApplyStun`, `CurrentControlState`, `AssassinExecute`, and the retired HP path). Do this plan **after** the combat rewrite.
- **Category model (spec ability-plan §2):** `Steal / Control / Defense / Utility`. `Damage` is deleted. `BotRole` re-derives from the new categories (Steal→Hunt, Defense→Flee).
- **Targeting north star (spec §3.0):** every ability self-centered or directional. **The Assassin Mark/Kill is the ONLY single-target ability**; all other formerly single-target abilities (Peck, Sneaky Steal, Ambush) become **AoE-around-self**.
- **Steal cap everywhere:** `StealMath.Clamp(abilityValue, attackerFreeSpace, defenderCargo)` — no ability re-implements the min.
- **Stun sources = exactly two classes:** Assassin `Ambush` (1.0 s) + Warrior `Wing Slam` (longer). Both AoE-around-self.
- **Roster (spec ability-plan §3):** Common = Peck·Egg Shell·Speed Burst; Speedy = Feather Aura·Feather Trap·Invisibility; Fatty = Roll & Push·Root Egg·**Cluck Shock**·Turtle Mode; Warrior = Flying Peck·**Wing Slam**·Spine Coat; Assassin = 🔒Mark/Kill·Ambush·Sneaky Steal·Shadowstep·Invisibility·Doppelganger.
- **New abilities register in `AbilityRegistrySO`**; bot `_botLoadouts` re-validated against the new `AllowedClasses` masks (STATE.md flags stale presets).
- **`SO` suffix, live in `Assets/_Game/Data/`**; extend `AbilityBaseSO`, author `AbilityCategory` + `BotRole` + traversal (ADR 0003).
- **Update GDD §5/§7** (they still describe the damage model + flat pool).
- Branch `develop`; one commit per task; prefix `feat(ability):` / `refactor(ability):`.

---

## File Structure

- Modify `Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs` — `AbilityCategory` enum: replace `Damage` with the four-value model; update `BotRole` auto-derivation.
- Modify ability SOs: `PeckAbilitySO` (damage→AoE steal), `CluckShockAbilitySO` (Warrior damage→Fatty AoE knockback), `FlyingPeck`/`RollTrample` (damage-dash→steal-dash), `SpineCoatAbilitySO` (reflect→steal-back), `SneakyStealAbilitySO` (single→AoE).
- Create `Assets/_Game/Scripts/Abilities/AmbushAbilitySO.cs`, `WingSlamAbilitySO.cs`, `ShadowstepAbilitySO.cs`, `MarkKillAbilitySO.cs` (thin SO wrapper over the combat plan's `AssassinExecute`).
- Create matching `.asset` files under `Assets/_Game/Data/Abilities/`.
- Modify `Assets/_Game/Data/Abilities/*.asset` — `SlotKind`/`AllowedClasses`/`Category` per roster; move Cluck Shock's mask Warrior(1)→Fatty(4).
- Modify `Assets/_Game/Data/AbilityRegistry.asset` — register the new abilities.
- Modify `Assets/_Game/Scripts/Editor/Tests/AbilitySystemTests.cs`, `DataIntegrityTests.cs` — new categories/masks.
- Modify `docs/GDD.md` §5, §7.

---

### Task 1: Category enum → Steal/Control/Defense/Utility (EditMode-TDD)

**Files:**
- Modify: `Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs` (`AbilityCategory` enum + `BotRole` derivation)
- Modify: `Assets/_Game/Scripts/Editor/Tests/AbilitySystemTests.cs` (category/role assertions)

**Interfaces:**
- Produces: `enum AbilityCategory : byte { Steal = 0, Control = 1, Defense = 2, Utility = 3 }` and an updated `BotRole ResolveBotRole()` mapping Steal→Hunt, Control→(Hunt/Flee dual), Defense→Flee, Utility→per-ability.

- [ ] **Step 1: Write/adjust the failing test** in `AbilitySystemTests.cs`: assert `AbilityCategory` has no `Damage` value, has `Steal`, and that a Steal-category ability derives `BotRole.Hunt`. Run EditMode → FAIL.
- [ ] **Step 2:** Replace the `Damage` enum value with `Steal` (keep byte ordering stable per project enum convention — append if needed to avoid renumbering serialized assets; if `Damage` was 0, map 0→Steal and migrate assets in Task 4). Update `ResolveBotRole`.
- [ ] **Step 3:** EditMode green. **Step 4:** `git commit -m "refactor(ability): Steal/Control/Defense/Utility categories"`

---

### Task 2: Convert ex-damage abilities to steal (closes combat-plan stubs)

**Files:**
- Modify: `PeckAbilitySO.cs`, `RollTrampleAbilitySO.cs`, `FlyingPeck` (whichever SO backs Flying Peck), `CluckShockAbilitySO.cs`, `SpineCoatAbilitySO.cs`

**Change-spec (follow the `SneakySteal` pattern exactly):** each `OnActivate` replaces its old `RPC_ApplyDamage(...)` call with the steal pattern — find target(s) via `Physics.OverlapSphere(pos, range, SearchMask, Ignore)`, compute `take = StealMath.Clamp(abilityValue, thiefCargo.Capacity - thiefCargo.Cargo, target.Cargo)`, then `thiefCargo.Cargo += take; target.RPC_DrainStolen(take);`. Specifically:
- **Peck** → AoE-around-self: steal a small value from *every* rival in a small radius (loop all hits, clamp per target). Keep the minor knockback. Category Steal.
- **Flying Peck** (Warrior) → directional dash (keeps `Vault` traversal) that steals on contact with the first rival in the lane. Category Steal.
- **Cluck Shock** → **no steal**: AoE knockback push only (shove every rival away, interrupt their collection). Category Control. (Reassigned to Fatty in Task 4.)
- **Spine Coat** → uses the combat plan's `StealBackActive`/`StealBackAmount` hook (steal-back + knockback on contact). Category Defense.
- **Roll & Push / RollTrample** → knockback only, no damage, no steal. Category Control. Keeps `Barge` traversal.

- [ ] **Step 1:** Rewrite each `OnActivate` per the change-spec (this also *removes* the temporary no-op stubs the combat plan's Task 5 inserted).
- [ ] **Step 2: Compile clean** — `assets-refresh`; zero errors.
- [ ] **Step 3: Play-mode** — for each converted ability: two chickens, one loaded; cast; confirm cargo transfers by the clamp (loaded thief steals nothing; empty target yields nothing) and knockback/interrupt where specified. `console-get-logs` + `screenshot-game-view`.
- [ ] **Step 4:** `git commit -m "feat(ability): convert ex-damage abilities to steal/control"`

---

### Task 3: New abilities — Ambush, Wing Slam, Shadowstep

**Files:**
- Create: `AmbushAbilitySO.cs`, `WingSlamAbilitySO.cs`, `ShadowstepAbilitySO.cs` + their `.asset` files

**Change-spec:**
- **Ambush** (Assassin, Control): AoE-around-self stun → for each rival in radius, `target.Controller.RPC_ApplyStun(1.0f)`. Small radius (it's a precision setup near the isolated mark). `DefaultIcon` per ART. `AllowedClasses = Assassin(8)`, `SlotKind = Character`.
- **Wing Slam** (Warrior, Control): same AoE-self stun mechanic, longer duration (e.g. 1.5 s, Oracle/playtest tunes). `AllowedClasses = Warrior(1)`. (Mechanically identical SO subclass to Ambush; author as its own asset with different name/duration/mask — or a shared `StunBurstAbilitySO` base with two assets; prefer the shared base to avoid duplicate code.)
- **Shadowstep** (Assassin, Utility): short **Blink** dash (`TerrainTraversal.Blink`) — the class-gated gap-closer. Directional; move the caster a fixed distance along facing, phasing over walls via the existing `ChickenTraversal` window. `AllowedClasses = Assassin(8)`.

- [ ] **Step 1:** Create the shared `StunBurstAbilitySO` base + `Ambush`/`Wing Slam` subclasses (or two assets of one class), and `Shadowstep`.
- [ ] **Step 2:** Author the `.asset` files via MCP `assets-material-create`-style creation (`assets-modify`/`GenerateAsset`); set category/mask/traversal/duration.
- [ ] **Step 3: Compile + EditMode** — `DataIntegrityTests` sees the new assets; green.
- [ ] **Step 4: Play-mode** — Ambush/Wing Slam stun rivals in radius (confirm stun VFX + the Task-4 combat gates); Shadowstep blinks the Assassin over a Standard wall Speedy must run around. `screenshot-game-view`.
- [ ] **Step 5:** `git commit -m "feat(ability): Ambush, Wing Slam, Shadowstep"`

---

### Task 4: Reassign the roster (SlotKind / AllowedClasses / Category)

**Files:**
- Modify: every `Assets/_Game/Data/Abilities/*.asset` per the roster (Global Constraints)

**Change-spec (data only, via MCP `assets-modify`):** set each ability's `SlotKind`, `AllowedClasses`, `Category` to the roster. Key changes vs current:
- **Cluck Shock**: `AllowedClasses` 1 (Warrior) → 4 (Fatty); `Category` Damage → Control.
- **Peck**: `Category` Damage → Steal (stays Common, mask 15).
- **Flying Peck**: `Category` Damage → Steal (stays Warrior).
- **Spine Coat**: `Category` Damage → Defense (stays Warrior).
- New assets (Ambush/Wing Slam/Shadowstep) get their masks from Task 3.
- **Mark/Kill**: authored as a locked signature (Task 5).

- [ ] **Step 1:** Apply masks/categories via `assets-modify`.
- [ ] **Step 2:** Update `DataIntegrityTests`/`AbilitySystemTests` pinned masks; EditMode green (this catches any off-class assignment).
- [ ] **Step 3: Play-mode character-select** — each class shows exactly its roster (1 Common + Character pool); Fatty offers Cluck Shock, Warrior offers Wing Slam, Speedy cannot see Shadowstep. `screenshot-game-view` of each class card.
- [ ] **Step 4:** `git commit -m "feat(ability): reassign roster masks and categories"`

---

### Task 5: Mark/Kill slot wiring + Assassin loadout lock

**Files:**
- Create: `MarkKillAbilitySO.cs` + `.asset` (thin wrapper delegating to `AssassinExecute.Press()` from the combat plan)
- Modify: `AbilityController.cs` (lock Assassin slot 0 to Mark/Kill; the button's two-press Mark→Kill routes through `AssassinExecute`)
- Modify: character-select loadout composition (`ComposeDefaultLoadout` / the Assassin path in `MenuUiController`)

**Change-spec:** The Assassin's signature slot is **fixed** to Mark/Kill (spec §4 — "can't change the mark"). The other two slots are free picks (stun/Shadowstep/steal/invis/decoy). Mark/Kill's activation calls `AssassinExecute.Press()`; the same button surfaces the KILL prompt when `AssassinExecute.KillReady`. It is the ONLY single-target ability (all others AoE).

- [ ] **Step 1:** Author `MarkKillAbilitySO` delegating to `AssassinExecute`; lock it into the Assassin's slot 0 in loadout composition.
- [ ] **Step 2: Compile + EditMode** — loadout tests: every Assassin has Mark/Kill locked + 2 legal picks; every other class 1 Common + 2 Character (STATE.md's slot-composition rules stay green).
- [ ] **Step 3: Play-mode full execute** — re-run the combat plan's Task 6 combo through the actual button UI: press Mark, arm, Ambush-stun, press Kill; verify bounty bag + removal + both counterplays. `screenshot-game-view`.
- [ ] **Step 4:** `git commit -m "feat(ability): Mark/Kill locked signature slot"`

---

### Task 6: Registry, bot loadouts, and GDD sync

**Files:**
- Modify: `Assets/_Game/Data/AbilityRegistry.asset` (register Ambush/Wing Slam/Shadowstep/Mark-Kill)
- Modify: `Assets/_Game/Scenes/Game.unity` `_botLoadouts` presets (re-validate vs new masks)
- Modify: `docs/GDD.md` §5 (classes/passives), §7 (ability pool)

- [ ] **Step 1:** Add the new abilities to `AbilityRegistry.asset` via `assets-modify`.
- [ ] **Step 2:** Re-validate every `_botLoadouts` preset against the new `AllowedClasses` (STATE.md flags e.g. Flying Peck mask mismatches); fix off-class entries. EditMode `DataIntegrityTests` green.
- [ ] **Step 3: Play-mode** — solo match with 3 bots; each bot rolls a legal loadout, uses steal/control abilities, no off-class errors in `console-get-logs`.
- [ ] **Step 4:** Update GDD §5/§7 to the control+steal model and the new roster; note the damage model is retired.
- [ ] **Step 5:** `git commit -m "feat(ability): register new abilities, fix bot loadouts, sync GDD"`

---

## Self-Review

**Spec coverage (ability-plan):** four categories (Task 1) · every ex-damage → steal/control, stubs closed (Task 2) · Ambush/Wing Slam/Shadowstep (Task 3) · roster masks incl. Cluck Shock→Fatty (Task 4) · Mark/Kill locked, only single-target (Task 5) · registry + bots + GDD (Task 6). ✓ Steal cap via `StealMath` (Task 2). ✓ Stun in exactly two classes (Task 3). ✓
**Placeholder scan:** Task 1 (enum/derivation) is EditMode-TDD. Ability SO rewrites carry change-specs that name the exact existing pattern to copy (`SneakySteal`'s `OnActivate` + `StealMath.Clamp` + `RPC_DrainStolen`) rather than fabricated bodies that might drift from the real base class — verified against the real `PeckAbilitySO`/`SneakyStealAbilitySO` read during planning. Play-mode acceptance for each, the correct surface for `NetworkBehaviour`-driven abilities.
**Type consistency:** `AbilityCategory` (Task 1) used by Tasks 2–4. `StealMath.Clamp` (combat Task 2) used by Task 2. `AssassinExecute.Press()`/`KillReady` (combat Task 6) used by Task 5. `RPC_ApplyStun` (combat Task 3) used by Task 3.
**Cross-plan ordering:** this plan MUST follow the combat plan (it consumes `StealMath`, `RPC_ApplyStun`, `AssassinExecute`, and closes Task-5 stubs) — stated in Global Constraints and Tasks 2/3/5.
