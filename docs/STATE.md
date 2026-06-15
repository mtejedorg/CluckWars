# Project State — Snapshot

**Live source of truth for "where are we right now."** Updated each session.
Designed to be the first doc an agent reads after `CLAUDE.md` to understand
what's shipped, what's in flight, and what's blocked on testing.

---

## Latest tag

`v0.3.1-alpha` — Debug logging, deposit fix, cargo feedback, base tinting. Pinned at commit `7fe855e`.

Earlier tags:
- `v0.3.0-alpha` (`312f511`) — Pre-test polish + build tooling.
- `v0.2.0-alpha` (`7ee1742`) — Phase 9 polish (audio service, registries, reconnection).
- `v0.1.0-alpha` (`10e527c`) — Core gameplay loop closed.

All tags pushed to origin.

---

## What works (Phases 1–9 complete + Phase 10 complete + DCBA polish + v0.3.1 fixes + Phase R Part A + Phase R Part B)

### Core loop
- 4 classes (Warrior / Speedy / Fatty / Assassin) — selectable in Bootstrap menu.
  Each class has a named passive: Warrior=Tough, Speedy=Slippery, Fatty=Immovable, Assassin=Combo.
- Movement: keyboard (WASD) + touch (joystick). `CompositeInputProvider` ORs both.
  Keys: Q = Ability1, E = Ability2, R = Ability3 (Assassin/Combo only).
- **No basic attack** (v0.3). All combat is ability-driven.
  HP, hit reaction, 5-sec death stun still fully networked.
- Food collect from piles → carry → deposit at base.
  Pile slow: standing on a pile applies a 0.80× speed penalty (GDD §6.2).
  Collision slow: brushing another chicken applies a 0.75× speed penalty (GDD §6.1).
- Food drop on death; pickup-on-overlap.
- **13 abilities in the global pool** (all classes can equip any):
  Speed Burst, Egg Shell, Flying Peck (fka Roll & Trample), Invisibility,
  Spine Coat (now also knockbacks attacker), Turtle Mode, Sneaky Steal, Doppelganger.
  **New in Part B:** Cluck Shock, Peck, Roll & Push, Feather Trap, Feather Aura, Root Egg.
- 2 ability slots for all classes; 3 slots for Assassin (Combo passive gates slot 2).
- Global ability pool via `AbilityRegistrySO` — no per-class restrictions.
- All 4 control states now exercised by real abilities:
  - Knockback: Peck, Roll & Push, Spine Coat reflect
  - Root: Root Egg zone
  - Slow: Feather Trap zone, Feather Aura broadcast
  - Speed boost: Speed Burst, Roll & Push (caster-side)
- `AbilityCategory` and `BotRole` authored on every AbilityBaseSO subclass.

### DCBA polish (code complete — pending Maestro prefab wiring for B + A)
- **D — Dead visual states**: `ChickenVisuals` greys out + goes semi-transparent (0.40 alpha) when `IsStunned`. `ChickenNameplate` shows red "☠" while dead; restores the normal label on respawn.
- **C — Screen shake**: `MatchCamera.Instance.ApplyShake()` called on HP decrease (0.12 mag / 0.25s) and on death (0.35 mag / 0.45s). Linear decay, y-axis damped 0.3×. Only fires on `HasInputAuthority` (local chicken).
- **B — Match stats**: New `ChickenMatchStats` NetworkBehaviour (`[Networked] Kills` + `FoodDeposited`). `ChickenCombat.CreditKillToAttacker()` credits kills via RPC. `ChickenCargo.TryDepositAtNearbyBase()` records deposits. `GameManager.RestartMatch()` resets stats. Match-end overlay shows `food | kills` per player. **Maestro: add `ChickenMatchStats` component to Chicken prefab.**
- **A — AI bots (solo mode)**: `ChickenController.IsBot` [Networked] + `BotTick()`. `BotController` NetworkBehaviour (FSM: **Idle / CollectFood / ReturnToBase / Flee / Hunt**; throttled Think @ 0.3s). Phase R-Bot: 5-tier decision priority, `FindNearestRival` perception, `ReactWithAbility()` role-preference dispatch (Defense→Escape→Control on Flee; Steal→Offense→Control on Hunt), per-class personality (`ApplyClassPersonality()`). `MatchBootstrapper._soloBotsToSpawn = 3`; bots roll a random eligible loadout from the `_botLoadouts` preset pool (BOT-3, `TryPickBotLoadout` — class restrictions are bot-only flavor). **Maestro: add `BotController` component to Chicken prefab + author the `_botLoadouts` preset rows.**

### Match lifecycle
- `GameManager` state machine: `WaitingForPlayers` → `Active` → `Ended` → restart.
- Host-controlled match start (lobby with "START MATCH" button for master client).
- Intro countdown ("3, 2, 1, GO!") before play begins each round.
- Win condition: first to `MatchConfigSO.FoodTargetToWin` OR most food when timer expires.
- Match restart loop: bases zeroed, piles refilled, pickups despawned, chickens reset + teleported to their corners.

### Networking
- Photon Fusion 2, Shared Mode. Solo mode = single-player Fusion runner.
- Photon Cloud relay already enables internet play (not LAN-only).
- Bootstrap menu: Solo / Host / Join. Host creates a UGS Lobby → unique 6-char join code. Joiner enters code or picks from lobby browser. Code becomes the Fusion session name.
- Photon AppId configured: `PhotonAppSettings.AppIdFusion = 259bda28-…`.
- Reconnection: `OnShutdown` → "SESSION ENDED" overlay → auto-return to Bootstrap.
- Connect / Disconnect / ConnectRequest / ConnectFailed callbacks all log explicitly.

### UGS (Phase 10 — code complete, pending Editor setup)
- `UGSService` (Auth + Lobby) bound in `ProjectInstaller` via `#if UGS_DISABLED` guard.
- `NullUGSService` used when `UGS_DISABLED` is defined (demo/offline fallback → "cluck-lan").
- Host: `CreateLobbyAsync` → join code displayed in Bootstrap + MatchHud lobby panel.
- Join: `JoinLobbyByCodeAsync` (type code) or `QueryLobbiesAsync` (browse list).
- Join code = Fusion session name — one code serves both UGS and Photon matchmaking.
- Lobby heartbeat (15s) runs automatically while host is in-session; stops on `LeaveLobbyAsync`.
- **Requires Unity Dashboard project link** — see "Outstanding before next test session" below.

### UI (Phase 10 visual redesign — Clash Royale/Supercell warm palette)
- **Design tokens** (ART.md §6, `cluckwars-tokens-v2`): panel `#3a2210` warm dark wood, gold `#f5c842`, green CTA `#33a332`, text `#fef5e0`. All inline in code; independent of serialised SO.
- **Player palette** updated to colorblind-safe Okabe-Ito set: Orange `#E8751A`, Blue `#1A7FC4`, Pink `#C4286F`, Teal `#0D9E7A`.
- **Bootstrap menu**: full-screen `#0e0804` background, warm wood panel, "CLUCK WARS" title in gold, section labels in gold accent. Keyboard shortcuts (1-4, S/H/J, SPACE). UGS lobby browser with scrollable list.
  - **Class stat cards** (Phase 11): flat class buttons replaced with tall cards showing a class-tint top strip, bold name, SPD/HP/CGO stat bars (normalized across all 4 classes from `ChickenClassRegistrySO`; falls back to design-time percentages when registry not bound), and ability-pool hint. Entire card is a `Button`.
  - **Ability slot picker** (B3): "ABILITIES" section replaced by a slot-selector row (Slot 1 / Slot 2 / Slot 3★) + scrollable ability grid grouped by `AbilityCategory` (Damage/Control/Defense/Utility), with cooldown tier badge (Short/Medium/Long). Click a card to equip it in the selected slot. Dedup: equipping an ability already in another slot swaps them. Slot 3 visible only for Assassin.
- **MatchHud** (UGUI): ranked leaderboard top-left (4 rows sorted live by food score, with player-color dot + progress bar + score). Timer badge top-right in gold. HP + cargo bars bottom-left above joystick. Centered overlays: lobby (with green Start button), match-end (ranked results), session-end, intro countdown (gold "3, 2, 1, GO!").
- **TouchControlsHud** (B4): joystick + 3 ability buttons. Attack button removed (v0.3); former attack position is now Ability3 (R key / Assassin slot). Ability tint = equipped `AbilityBaseSO.AccentColor`. Cooldown radial fill for all 3 slots. **Buttons grey out (alpha 0.45) while on cooldown** — v0.3 §10 hard UI requirement.
- **DebugHud** (F1 toggle): FPS, network state, GameManager state, local chicken stats, base ownership, pickup count.

### v0.3.1 — Debug logging, deposit fix, cargo feedback, base tinting

- **Debug logging**: `BotController`, `ChickenCombat`, `AbilityController`, `ChickenCargo`, `GameManager` fully instrumented. Bot FSM logs every state transition (Idle/CollectFood/ReturnToBase) with cargo fraction.
- **Deposit fix**: `GameManager.AssignBasesToPlayers` now uses nearest-position matching — fixes the 75% base-ownership failure caused by `MatchBootstrapper`'s Fisher-Yates corner shuffle vs. the old `PlayerId % 4` corner selection. `ChickenCargo.FindNearestBaseInRange` removed `Physics.OverlapSphere` dependency — uses `FindObjectsByType<PlayerBase>` with a 1.5 s cache; `PlayerBase` prefab no longer needs a trigger collider.
- **Cargo feedback**: `MatchHud` cargo bar shifts gold→orange→red, shows "FULL → RETURN TO BASE!" label at capacity. `ChickenNameplate` adds a world-space cargo line (`3/10`, orange ≥70%, red "■ FULL!"). `ChickenVFX` adds a looping gold orbit ring (`VFX_CargoFull`) while cargo is at capacity.
- **Base tinting**: `PlayerBase.LateUpdate` polls `Owner` each frame and applies corner-indexed colors (Orange/Blue/Pink/Teal) via `MaterialPropertyBlock` + direct material fallback. Replaced a broken `ChangeDetector + Render()` approach that silently skipped locally-written `[Networked]` props in `GameMode.Single`.

### Playtest fix (2026-06-01) — food collection range (game-breaking)
- **Food collection was completely broken** (found via MCP playtest): no chicken — bot or human — could ever drain a pile, so every match ran the full 3 min and ended 0–0–0–0 on the timer. The match-end + auto-restart path handled the all-zero tie without errors, which is why it had gone unnoticed.
- **Root cause:** `ChickenCargo.FindNearestPileInRange` / `FindNearestPickupInRange` / `FindNearestBaseInRange` compared the **full 3D distance** (`sqrMagnitude`) against the tuned radius, but a chicken's pivot floats ~1 unit above pile/base pivots (capsule centre). Bots parked at their arrival distance (~1.45 horizontal) from a 1.6-radius pile, yet the 3D distance was ~1.8 → always out of range. (Deposit's generous 2.5 radius happened to absorb the ~1 u Y offset, masking the same latent bug there — which is why the v0.3.1 "deposit fix" looked fine but collection never got the same treatment.)
- **Fix:** added `HorizontalSqr(a,b)` (XZ-only squared distance) and routed all three proximity gates through it — Y no longer gates interaction. Verified live in play mode: bots immediately began draining piles, filling cargo, and depositing; leaderboard climbed to 27 / 18 / 9 within ~20 s. Clean compile, no new errors.

### Menu UI pass (2026-06-01) — lobby redesign + char-select polish
- **Match Lobby rebuilt to the landscape design** (`Design/cluckwars-overlays-v3.jsx` `CWLobbyV3L`): 300px settings rail (MATCH LOBBY ribbon, host-only INVITE CODE card with gold letter tiles + SHARE/COPY, MATCH SETTINGS card, status pill, START + BACK) and a **2×2 player-card grid**. Cards are player-colored (Okabe-Ito P1 orange / P2 blue / P3 pink / P4 teal): accent bar, baked chicken sprite, name + Pn + HOST/CPU badge, `CLASS · PASSIVE`, ability mini-hexes (dashed for empty slots), and READY/PICKING badge. Solo fills 3 CPU bots (DashFox/BrunoB/PeckNoir); Host/Join show "WAITING FOR Pn" seats. Host pre-creates the UGS lobby and populates the code tiles; COPY/SHARE write the code to the clipboard. New markup `Assets/UI/Lobby.uxml`, styles in `CluckWarsTheme.uss` (`.cw-lobby-*`, `.cw-player-*`, `.cw-code-tile`, `.cw-status-*`), data wiring in `MenuUiController` (`BuildPlayerGrid`/`MakeLobbyCard`/`MakeMiniHex`/`SetCodeTiles`/`UpdateLobbyStatus`).
- **Character Select polish**: selected class chip now carries a faint class-color wash (not just a tinted border); equipped ability cards show a numbered slot badge (1/2/3) tinted to the ability accent — both per design `CWCharacterSelectV3L`. Softened the shared Gloss sheen on lobby rail cards so they read as dark surfaces instead of a bright grey band.
- Verified live (play mode, MCP screenshots): Solo lobby renders the 2×2 grid with correct per-class data; Assassin char-select shows ★S3 + numbered badges; no runtime exceptions.

### On-device fix (2026-06-01) — menu UI overflowed on the phone (PanelSettings scale mode)
- **Symptom:** on a Pixel 9 (real-device test), the menu UI rendered ~2.6× too large — Character Select's whole right column (ability grid + READY) was pushed off-screen, unusable. In the Editor at the same 2424×1080 resolution it looked perfect, which isolated it to **scaling, not layout**.
- **Root cause:** `Assets/Resources/PanelSettings.asset` had `m_ScaleMode: 1` = **ConstantPhysicalSize**, which scales the UI by **screen DPI** (Editor ≈96 dpi → ×1.0; Pixel 9 ≈420 dpi → ×~2.6). The `m_ReferenceResolution 1920×1080` + `m_Match 0.5` were set but **ignored** in that mode.
- **Fix:** `m_ScaleMode: 2` = **ScaleWithScreenSize** → UI now scales by resolution, so the phone renders like the Editor at the same resolution. Verified in-Editor at 2424×1080 (Pixel 9 landscape): Character Select and Lobby both fit fully. **Requires an APK rebuild to verify on device** (PanelSettings is baked into the player build).
- Tooling note: a co-op test skill was added at `.claude/skills/coop-test/SKILL.md` (`/coop-test`) for PC-Editor + Android-phone Photon testing. Driving the phone menu via `adb input tap` is unreliable (landscape app on a portrait `ROTATION_0` display → coordinate-space mismatch); have a human do phone-side taps, or drive only single gestures (joystick swipes).

### Map / camera
- `MapGenerator`: procedural plane + 4 invisible boundary walls + 4 corner bases + 1 large center pile + N small piles on a jittered ring. All master-spawned.
- Spawn points coincide with base positions (`Vector3.Lerp(corner, origin, 0.15)`); chickens spawn at their base.
- `MatchCamera`: orthographic isometric (45° yaw + 30° pitch), smooth-follows the local chicken with configurable `_followSmoothTime` (default 0.15s) and `_orthoSize` (default 8 — closer than ART.md §2's full-map fixed view).

### Registries (consolidated assets)
- `PrefabRegistrySO`: Chicken, Doppelganger, FoodPile, FoodPickup, PlayerBase, GameManager.
- `AudioRegistrySO`: Combat / Cargo / Ability / Match / Music clip refs.
- `ColorSchemeSO`: HUD palette, food-pile states, button colors, cooldown dim.
- `ChickenClassRegistrySO`: per-class stats + tint.

All bound app-wide in `ProjectInstaller`. Empty asset slots bind a runtime-empty instance with defaults so consumers never crash.

### Editor tooling
- `Cluck Wars / Build / Windows` (`Ctrl+Shift+W`) — `StandaloneWindows64`, output `Builds/Windows/CluckWars.exe`.
- `Cluck Wars / Build / Android` (`Ctrl+Shift+A`) — IL2CPP + ARM64 + minSdk 24, `Builds/Android/CluckWars-<version>.apk`.
- `Cluck Wars / Build / Windows + Android` (`Ctrl+Shift+B`) — sequential.
- `Cluck Wars / Build / Reveal Builds Folder`.
- `Cluck Wars / Balance / Balance Editor` — Editor window: editable table of all 4 `ChickenStatsSO` assets (HP / Spd / Turn / Cap / Rate — Attack fields removed in v0.3) + all 8 `AbilityBaseSO` assets (Duration / Cooldown / AccentColor). "Save All ★" flushes dirty assets. Auto-discovers assets — no list to maintain.
- **F2 in-game toggle** in `DebugHud` — runtime Balance panel (right side of screen): class stats table from `ChickenClassRegistrySO` + local chicken's equipped ability timings (Duration / Cooldown, active slot indicator).
- **MemPalace integration**: Local AI memory system installed in `.venv`. Configured with `mempalace.yaml`, `entities.json`, and `C:\Users\MARCO\.mempalace\identity.txt`. Mined 111 files including gitignored ones (like `.claude/skills/*/SKILL.md`) using `.venv\Scripts\mempalace mine --include-ignored .claude .`. Run `.venv\Scripts\mempalace wake-up` to load context.

### v0.3.2 — UI v3 design pass (Design.zip)

Applied the newest design wireframes (`cluckwars-hud-v3` / `-charselect-v3` / `-ability-ref` / `-tokens-v3`) to the code-driven UGUI. ART.md replaced with the design author's richer v0.3 spec and reconciled to the v3 wireframes (no attack button, control-state overlays, ability icons). GDD §6.4/§7.2 annotated with icons + overlay note.

- **Ability icons**: `AbilityBaseSO` gained an `Icon` (emoji glyph) field + `ResolveIcon()` + per-subclass `DefaultIcon`. All 14 ability subclasses carry their design-v3 glyph (🪽⚡🐦🌀🪤💨🌱🥚🐢🦔💨👻👥🤏). Existing `.asset`s need no re-authoring — blank `Icon` falls back to the subclass default.
- **TouchControlsHud**: hex ability buttons rebuilt to design v3 — centered icon glyph + short label, top-left slot-index badge (`1`/`2`/gold `★3`), centered seconds-remaining cooldown number, accent tint from `AbilityBaseSO.AccentColor`, icon dims on cooldown. Slot 3 hidden unless an ability is equipped in slot 2 (Assassin). MOBA arc positions: 2-ability stack / 3-ability triangle. Per-slot refs refactored into an `AbilityBtn[3]` struct array.
- **MatchHud**: leaderboard rows now show ordinal rank (`1st`–`4th`); added a `★ FIRST TO N` win-target badge under the panel (N from `MatchConfigSO.FoodTargetToWin`).
- **CharacterSelectController**: ability cards prefix the icon glyph; each class card shows its passive (name + one-line desc — Tough/Slippery/Immovable/Combo). Cards row height bumped to fit.

**Real fonts shipped**: `Assets/_Game/Resources/Fonts/` now contains the actual **Lilita One** (headings/labels), **Nunito** (body/descriptions), and **Noto Emoji monochrome** (ability icon glyphs) `.ttf`s, loaded at runtime via `Resources.Load<Font>`. No OS-font dependency. `UiGfx.ChunkyFont/BodyFont/EmojiFont` resolve them (OS/built-in fallback only if the Resources load fails).

**TMP for emoji icons**: legacy `UnityEngine.UI.Text` cannot render supplementary-plane emoji (🪽 = U+1FABD, 🪤, etc.), so the **ability icon glyphs use TextMeshPro** (`UiGfx.AddIcon` builds a runtime `TMP_FontAsset` from Noto Emoji and renders the glyph, accent-tinted). A `[InitializeOnLoad]` editor script (`Editor/TmpEssentialsAutoImport.cs`) auto-imports **TMP Essential Resources** on first compile so this works without the manual `Window ▸ TextMeshPro ▸ Import…` step. All other (BMP) text stays on legacy Text with the real Lilita One / Nunito.

**Procedural chicken art**: `UiGfx.Chicken(classKey)` bakes a per-class chicken figure (gradient body, head, comb, beak, eye, legs, shadow; class silhouette + colors from design v3) into a cached sprite — shown on every class card.

**Glossy visual rebuild (`UiGfx`)**: `Assets/_Game/Scripts/UI/UiGfx.cs` bakes rounded-rect / hexagon / circle / gloss-gradient / chicken sprites **procedurally at runtime** (no imported sprite assets needed) + the spec's text drop-shadow. Applied across all three code-driven UIs:
- **CharacterSelectController**: glossy rounded wood panel + gold ribbon title, glossy gradient class cards (class-color strip + gold selection glow), glossy accent-tinted ability cards, gold/green gradient buttons, rounded input fields, glossy lobby-browser panel. Class cards now use ignored-layout `GlossyBg` children + manual selection visuals (Button transition = None, clicks bubble from the bg frame).
- **TouchControlsHud**: ability buttons are now **pointy-top hexagons** (procedural hex sprite) with hex-masked radial cooldown; joystick base/knob and slot badges are real circles; chunky font + shadows.
- **MatchHud**: rounded glossy panels (leaderboard, timer, local stats, overlays), rounded target badge + buttons, chunky font + shadows.

**Still deferred from the full design** (next steps, not blockers): on-character control-state overlays (stars/vines/motion-lines/nameplate badges — ART §6.10), match-end/lobby/intro overlay art (crown, medals, ribbons, winner chicken), and moving HP/cargo bars fully onto the world chicken (§6.3 — `ChickenNameplate` already carries the world-space cargo line). A couple of the newest emoji (🪽 wing, 🪤 trap) may show a fallback box if the bundled Noto Emoji build lacks that codepoint; everything else renders.

**Pending Maestro (optional polish)**: align each ability `.asset`'s `AccentColor` to the ART.md §3 v3 hexes if desired; import Lilita One + Nunito + an emoji sprite asset as TMP fonts to render glyphs and the glossy type.

### Juice pass — stages 2 & 3: input fix, cooldowns, aggressive bots (2026-06-13)

- **Ability buttons "not working" — root-caused + fixed (commit `54d3a75`).** Input
  was a one-frame edge (`HoldButton.WasPressedThisFrame` / keyboard) but Fusion's
  `OnInput` runs at tick rate (~30 Hz) not per render frame (~60 Hz), so presses on
  non-tick frames were silently dropped (~half of taps did nothing). Movement was fine
  (continuous). `FusionNetworkService.Update` now latches each press edge and `OnInput`
  consumes it, so no press is lost. Touch + keyboard. **Pre-existing latent bug** — only
  surfaced when a human actually tapped (scripted activation bypassed this path). User
  confirmed fixed + feels better.
- **Cooldowns trimmed ~25–35% (commit `1bcd204`)** per feedback ("a bit too high").
  Short tier now 3s (Peck/RollPush), most Medium 6–8s; Doppelganger 15→11, CluckShock
  10→7. Edited the 14 ability `.asset`s directly.
- **Aggressive bots (commit `21195cf`)**: bots only hunted *loaded* rivals → passive vs
  an empty player. Warrior/Assassin/Speedy now engage any rival inside a tighter engage
  radius (8.5/6.5/6.0) even unloaded, when not hauling; Fatty stays the farming foil.
  Verified: empty player next to Assassin → it Hunts and closes in.

**Next (juice plan stage 3 — design/pacing):** still open — match length, map/food
density, win-target vs the 220 food on the map, fight incentives. Plus the deferred
MP item: network the control-state fields (SlowMultiplier/Rooted/ExternalDisplacement)
so the new control-state rings show on remote peers, not just solo.

### Game-feel / juice pass — stage 1 of 3 (2026-06-13)

Maestro feedback: "0 feedback in abilities, game ultra boring." Agreed the ability
feedback was too narrow (only 3 of 13 abilities) and the game felt dead. Plan
chosen: **juice first, then bots, then design.** This is the juice stage.

- **Procedural audio (commit `…ProceduralAudioBank`)**: AudioRegistry was empty →
  total silence. `ProceduralAudioBank.FillMissing` synthesises every SFX at startup
  (sine/square/saw/noise + ADSR, click-free) — cast, hit, stun, deposit/victory arps,
  pickups, match cues. Wired in `ProjectInstaller`; only null fields are filled, so
  real `.wav`s override transparently. The game now has sound everywhere it had none.
- **Control-state visuals (commit `…ControlStateVFX`)**: the biggest gap — slow/root/
  knockback produced NO on-target visual. `ControlStateVFX` (Chicken prefab) draws a
  colour-coded ground ring on any affected chicken: yellow=stun, green=root, cyan=slow
  (+ white knockback shockwave). Reads StateAuthority control-state fields (solo-correct;
  **MP needs SlowMultiplier/Rooted/ExternalDisplacement networked** — noted in class doc).
  Verified: slowed→cyan, rooted→green, unaffected→none.
- **Cast/hit-connect shake (commit `f2080dd`)**: `ChickenVFX` shakes the local camera on
  cast — light for any ability, punchy for Damage. Range-gated Damage abilities can't
  fire without a target, so a Damage cast that lands here = a confirmed hit (no extra
  networking). Pairs with the audible cast SFX + accent burst.

**Next juice-pass stages (not yet done):** (2) aggressive bots that actually pressure
you so fights happen; (3) pacing/design tuning (cooldowns, map/food density, fight
incentives) — "boring" is partly a real design issue, acknowledged with Maestro.
Also still deferred: networking the control-state fields for MP feedback parity.

### Ability range/usability feedback (2026-06-13, commit `b86da7e`)

Maestro: targeted abilities were "super confusing" — no visible reach, no signal
for when a press would do anything. Added a layered feedback model:

- `AbilityBaseSO` gained `IndicatorRange` / `RequiresEnemyInRange` / `IsUsable(caster)`
  + a `HasEnemyInRange` helper. **Peck**, **Cluck Shock**, **Sneaky Steal** override
  them (Steal additionally requires the target to be carrying cargo).
- `AbilityController.TryActivate` now refuses to fire a range-gated ability with no
  valid target — the cooldown isn't burned on a guaranteed whiff.
- `TouchControlsHud` ability buttons have **three states**: accent+dimmed on cooldown,
  desaturated grey when ready-but-no-target, full accent when actually usable.
- New `AbilityRangeIndicator` (on the Chicken prefab): a local on-ground **range ring**
  that's faint when no target is in reach and **brightens + thickens** the instant an
  enemy enters, plus an expanding **cast flash** at the ability's true radius when it
  fires. Local VFX only (no RPCs), same `ActiveSlot`-poll pattern as `ChickenVFX`.
- Verified live in solo: ring + both buttons grey with no enemy near; ring turns gold
  and buttons colour (PCK red, CSK yellow) when a bot is inside range. `IsUsable`
  confirmed `False`@far / `True`@1.4u.

> **Tooling gotcha (next agent, read this):** a new `.cs` written to disk *while the
> Unity Editor was busy/MCP-disconnected* imported with a `MonoImporter` + `.meta` but
> was **silently excluded from Assembly-CSharp's source list** — `assets-refresh`,
> `ImportAsset(ForceUpdate)`, deleting the `.meta`, and `RequestScriptCompilation` all
> failed to add it (type stayed MISSING, `scriptCompilationFailed=False`). Diagnosis:
> `CompilationPipeline.GetAssemblies()` → `Assembly-CSharp.sourceFiles` didn't contain
> the path. Fix that worked: `AssetDatabase.DeleteAsset(path)` then **recreate the file
> through Unity's own `script-update-or-create` tool** (param keys: `filePath` +
> `content`) so Unity registers it cleanly. Prefer creating new scripts via that tool
> rather than the raw Write tool when the editor may be mid-reload.

### Solo-first polish pass (2026-06-12) — scoring fix, GDD map, bot AI, placeholder models

Direction set by Maestro: perfect solo mode before returning to multiplayer.

- **Solo scoring fixed (commit `e03b106`) — bots are real competitors now.**
  Root cause: bots share `[Player:None]` input authority, so they deposited at *any*
  unowned base (pooled, misattributed scores) and both win checks skipped
  non-real-player bases — bots could never win, so solo matches carried no balance
  signal. Fix: `[Networked] int HomeCornerIndex` on `ChickenController` (stamped at
  spawn for humans + bots), corner-gated deposits, `PlayerBase.BotClaimed` +
  `IsClaimed`, bot-inclusive win checks, `GameManager.WinnerCorner`, corner-true
  restart teleports (the old PlayerId-modulo mapping ignored the corner shuffle).
  Identity unified on corner numbers (P1–P4 + "(CPU)") across leaderboard,
  nameplates, base tints, and the winner banner. Verified live end-to-end.
- **Map now builds the GDD §3 layout (commit `4fc3036`)**: center pile (60) + 4
  personal islands (15, on each base→center line) + 4 contested islands (25,
  between adjacent corners), positions jittered per match. Replaces the old
  center-80 + ring-of-4×30. Total food 220.
- **Bot AI (same commit)**: BOT-8 hysteresis (state exit radii ×1.35), Hunt re-scans
  for a *loaded* rival when the nearest is empty, bots perceive ground pickups
  (scoop death-drops instead of walking off), contest-pile Control cast range-gated.
- **Placeholder models (commit `1f419b9`)**: `PlaceholderMeshFactory` +
  `PlaceholderModel` swap meshes at Awake — chunky chicken (GDD §4 silhouette),
  grain-mound piles, nest-with-eggs bases. Tints/scale feedback/colliders untouched.
  Remove the component per-prefab when real art lands.
- **Log floor raised**: `ProjectContext` `_logMinLevel` Verbose → Debug (per-tick
  cargo Verbose spam filled the entire console buffer and masked Info diagnostics).
- **Still open in the solo track**: collision/zone polish verification (BUG: none
  observed — needs an ability-heavy play session), Editor/runtime balance pass with
  the new map totals, ground/visual treatment for the arena (bare grey plane).

### Project reorganization (2026-06-11) — UI consolidation, BUG-4 fix, test infra

- **UI consolidated on UI Toolkit (Path B of `docs/UI_HANDOFF.md` — now marked RESOLVED).**
  The Bootstrap menu is solely `MenuUI` (`UIDocument` + `MenuUiController`) + `Assets/UI/*.uxml`
  + `CluckWarsTheme.uss`. Deleted the failed atomic-prefab experiment: inactive `MenuCanvas`
  removed from Bootstrap.unity, `CharacterSelectController` component removed from the
  `Bootstrap` GO, 6 dead UI scripts (`CharacterSelectController`, 5 `Ui*View`), 5 one-shot
  editor scripts (`UiPrefabBuilder`/`2`, `WireMenuScene`, `FixPrefabsAndCanvas`, `MenuUiWiring`),
  and 5 broken prefabs in `Assets/_Game/Prefabs/UI/`. `UiGfx`/`SDFImageEffect`/SDF shader kept —
  the in-game HUD still uses them. **Verified live**: clean compile (0 errors), MainMenu +
  Character Select render the v3 design correctly in play mode (MCP screenshots).
  New rule in `docs/CONVENTIONS.md` § UI rules: menu UI is UXML/USS only; never procedural UGUI.
- **BUG-4 FIXED + verified**: removed `UGS_DISABLED` from Standalone scripting defines —
  all platforms now bind the real `UGSService`. Verified in Editor play mode:
  `IUGSService` resolves to `UGSService`, `InitializeAsync` + `SignInAnonymouslyAsync` succeed
  (project IS Dashboard-linked). The fixed `cluck-lan` session is gone; hosts always get a
  real lobby code. Editor↔Android both-HOST mismatch eliminated.
  **Windows EXE must be rebuilt** (`Ctrl+Shift+W`) to pick this up.
- **Test infra**: `tools/run-clients.ps1` launches N windowed Windows clients with per-client
  logs (`Builds/Windows/logs/clientN.log`) — the standard multiplayer loop (emulators remain
  dead). TESTING.md updated (launcher + real-UGS multiplayer flow).
- **Repo hygiene**: root scratch files purged + gitignored (`args*.json`, `*_output.txt`,
  `scratch/`, `.agent/`, `*.cs.bak`), `Design/` wireframes now tracked,
  `UI_HANDOFF.md` moved to `docs/`.

---

## Outstanding before next test session

### Pending Maestro (Editor work — Phase R Part A + Part B)

These steps must be done in the Unity Editor after the code compiles cleanly:

1. **Four class `.asset`s** (`Assets/_Game/Data/Classes/`): set the new `Passive` field for each
   (Warrior=Tough, Speedy=Slippery, Fatty=Immovable, Assassin=Combo).
   The old `Attack`/`AttackRange`/`AttackCooldown`/`AvailableAbilities` fields will be gone
   after recompile — Unity will strip them automatically.

2. **AbilityRegistry**: create an `AbilityRegistrySO` asset via `Cluck Wars / Ability Registry`.
   Drag all 7 ability assets (`Assets/_Game/Data/Abilities/`) into its `All` array.
   Assign it in `ProjectInstaller._abilityRegistry` on the `ProjectContext` prefab.

3. **Chicken prefab — `AbilityController`**: the `_slot2` field is now exposed.
   For Assassin builds, assign a default ability to `_slot2` (e.g., Sneaky Steal).

4. **Chicken prefab — AnimatorController**: delete the `Attack` (trigger) parameter,
   add `AbilityCast` (trigger). `ChickenAnimator` now fires `TriggerAbilityCast()`.

5. **Flying Peck `.asset`** (was Roll & Trample): the asset file itself is unchanged (same GUID).
   Open it and update `DisplayName` = "Flying Peck", `ShortLabel` = "FP".

6. **ProjectInstaller**: remove any reference to the old `_attackButtonSize`/`_attackAnchoredPosition`
   inspector values on `TouchControlsHud` — those fields were removed; the GameObject will
   reset to the new `_ability3AnchoredPosition` default.

**New Phase R Part B Maestro tasks:**

7. **AbilityZone prefab**: create a NetworkObject prefab with the `AbilityZone` script + a trigger sphere collider at `_triggerRadius` = 1.5. Assign to `PrefabRegistrySO.AbilityZone`. (Without this, FeatherTrap and RootEgg log a warning and do nothing.)

8. **Six new ability `.asset`s** — create in `Assets/_Game/Data/Abilities/`:
   - Cluck Shock (via `Cluck Wars/Ability/Damage/Cluck Shock`): Category=Damage, Cooldown≈10s
   - Peck (`Cluck Wars/Ability/Damage/Peck`): Category=Damage, Cooldown≈4s
   - Roll & Push (`Cluck Wars/Ability/Control/Roll and Push`): Category=Control, Cooldown≈4s
   - Feather Trap (`Cluck Wars/Ability/Control/Feather Trap`): Category=Control, Cooldown≈10s
   - Feather Aura (`Cluck Wars/Ability/Control/Feather Aura`): Category=Control, Cooldown≈10s
   - Root Egg (`Cluck Wars/Ability/Control/Root Egg`): Category=Control, Cooldown≈10s
   Set `AccentColor` and `DisplayName` on each.

9. **AbilityRegistry**: add all 6 new `.asset`s to `AbilityRegistrySO.All`.

10. **Sneaky Steal `.asset`**: set `BotRole = Steal` in Inspector.

11. **MatchBootstrapper** (Game scene): author the `_botLoadouts` preset rows (BOT-3). Recommended set from ROADMAP Phase R-Bot BOT-3 — Bruiser (Flying Peck + Egg Shell, all), Skirmisher (Flying Peck + Speed Burst, all), Tank (Spine Coat + Turtle Mode, Fatty/Warrior), Trickster (Flying Peck + Invisibility, Speedy/Assassin), Thief (Sneaky Steal + Speed Burst + Flying Peck slot2, Assassin). Each row: set `Name`, the slot `.asset`s, and `AllowedClasses` (empty = all). Until authored, bots spawn ability-less (a Warn is logged).

Pre-existing Maestro tasks still pending:
- **UGS Dashboard link** (Phase 10): Unity → Project Settings → Services → link org. Enable Auth + Lobby.
- **ColorScheme.asset**: hit "Reset" in Inspector for Phase 10 warm palette on `TouchControlsHud`.
- Optional: fill `AudioRegistry` clips.

### Pending agents (code)
- Phase R Part A: **complete**. See ROADMAP.md § Phase R for what's done.
- Phase R Part B: **complete** (code). Remaining: Maestro steps below + B6 balance pass (test session) + B7 VFX (test session).
- Phase R-Bot: **complete** (code), including BOT-3 randomized loadout. `MatchBootstrapper` has a `BotLoadoutPreset[] _botLoadouts` pool; `TrySpawnBots` rolls a random eligible preset per bot via `TryPickBotLoadout` and equips it through `SetSlots`. Class restrictions on presets are bot-AI flavor only (do not gate the player UI — GDD §7.1). Remaining: Maestro authors the preset rows (step 11 below) + BOT-8 tuning (test session).
- `ISessionSelectionService` carries `Ability0`/`Ability1`/`Ability2`; `MatchBootstrapper`
  calls `AbilityController.SetSlots(slot0, slot1, slot2)` in `onBeforeSpawned`.

---

## Known bugs / open questions (need device testing)

1. ~~**Join case-sensitivity**~~ **FIXED (2026-06-03)** — `NullUGSService.JoinLobbyByCodeAsync` was uppercasing the join code to "CLUCK-LAN" while host used lowercase "cluck-lan". Photon room names are case-sensitive so the two clients never met. Fix: removed `.ToUpper()` from `JoinLobbyByCodeAsync` — both sides now use the code verbatim (lowercase).
2. ~~**RestartMatch Y=0 fall-through**~~ **FIXED (2026-06-04, commit d69fad3)** — `GameManager.RestartMatch()` called `RPC_TeleportTo(spawnPoints[i])` with raw Y=0 positions. `MatchBootstrapper` initial spawn adds `+0.05f Y` jitter; restart did not. On Android (IL2CPP real device) the chicken clipped into the floor mesh and fell infinitely on every round restart. Fix: added `Vector3.up * 0.05f` to both real-player and bot teleport targets in `RestartMatch()`.
3. ~~**ChickenCargo 3D distance**~~ **FIXED (2026-06-04, commit 61c46d8)** — See "Playtest fix" above. Previously `FindNearestPileInRange` used a 3D `sqrMagnitude` check, but the code was duplicated in the original commit without the XZ-only fix being applied to all three proximity methods. All three now use `HorizontalSqr()`.
4. ~~**UGS mismatch: Android real UGSService vs Editor NullUGSService**~~ **FIXED (2026-06-11)** — removed `UGS_DISABLED` from Standalone defines; all platforms use the real `UGSService`. Verified in Editor play mode (init + anonymous sign-in OK). Rebuild the Windows EXE to pick it up.
5. **Third player can't connect** (reported 2026-05-08). **Partially retested 2026-06-04** — 2-player session confirmed working. 3-player test not yet run this session.
6. **Solo-on-Android movement** (reported 2026-05-08). Not explicitly retested this session.

---

## Deferred work — bundled for the dedicated test session

All require real-device or playtest data; queued so they don't get done piecemeal.

- Network desync hunting (cross-device LAN).
- On-device FPS / draw-call / memory profiling.
- AnimatorController state authoring — **state machine needs update** (replace `Attack` trigger with `AbilityCast` trigger per Phase R A10). States: Idle/Walk/AbilityCast/Hit/Stunned. Needs `.anim` clip assets once artwork is recorded/imported.
- VFX particle systems — **code complete** (`ChickenVFX.cs`: hit sparks, death burst, stun orbit, deposit gold shower, **ability accent burst**). `VFX_Ability` PS: 16-particle sphere, tinted dynamically from `AbilityBaseSO.AccentColor` at activation, floats upward. Maestro: add `ChickenVFX` component to Chicken prefab.
- Audio clip recording / mixing.
- Balance pass (food rates, ability cooldowns, attack damage, HP).
- Mobile layout fine-tune for actual phone aspects.
- `MatchCamera._orthoSize` / `_followSmoothTime` final tuning on device.

---

## Testing strategy (updated 2026-06-04)

**Android x86_64 emulators are not viable** for Unity 6 URP on this machine (Hyper-V blocks
every GPU mode that supports ES 3.1+). See `docs/TESTING.md` § Known gotchas for the full
failure matrix.

**Co-op test completed 2026-06-04 — Editor + Pixel 9:**
- Solo mode (Editor): ✅ 4 chickens, bots, food loop, zero errors.
- 2-client multiplayer: ✅ Editor joined Pixel 9's real UGS session (`PNDMWK`) via JOIN-by-code flow. 2 chickens confirmed in same Photon room (`players=2`).
- Join-case fix (508f6e6): ✅ `JoinLobbyByCodeAsync("PNDMWK")` returned verbatim → correct session joined.
- Round restart: 🐛 Fixed (d69fad3) — Y=0 teleport caused Android fall-through. Now +0.05f Y.
- Food collection: ✅ Cargo draining confirmed in both solo and 2-player.
- **Key discovery:** Android APK uses real `UGSService` (Dashboard linked); Editor uses `NullUGSService`. Both-HOST fails. Connect via JOIN-by-code instead. See bug #4 above.

**Next test priorities:**
1. Rebuild Android APK (to pick up d69fad3 + 61c46d8 fixes), retest round restart on Pixel 9.
2. Test 3-player connect (BUG-5).
3. Add `UGS_DISABLED` to Android build defines OR fully enable real UGS on all platforms.

**Infrastructure:**
- ADB v40/v41 war resolved: SDK platform-tools ADB replaced with v41 copy.
- `cluck_emu2` AVD created but not useful (Hyper-V + ES 3.1+ conflict).

---

## Recent commits (most recent first)

```
126ee4b Docs: add Antigravity CLI (agy) guide
e8c326c Chore: update packages and NuGet DLLs
b5ede4a UI v3: SDF shader system, atomic view components, UXML + Bootstrap wiring
61c46d8 Fix: ChickenCargo proximity checks use XZ distance instead of 3D
d69fad3 Fix: RestartMatch teleports chicken to Y=0 causing Android fall-through
5d67dd0 Docs: record emulator failure matrix + pivot to Windows-first testing
f53aaef Docs: update STATE.md recent commits after co-op test session fixes
508f6e6 Fix: NullUGSService join case bug + build menu restore-target + test docs
0772810 Part B — B3/B4/B5: bot AI, ability grid UI, cooldown grey-out
c3f70c9 Part B — B2: new ability SOs + SpineCoat knockback
b120170 Part B — B1: interaction primitives (knockback, root, placed zones)
578a2ce Phase R Part A: v0.3 parity refactor
7fe855e Debug logging, deposit fix, cargo feedback, base tinting  ← v0.3.1-alpha
1aa3b93 Fix: multiple chickens spawning at same base
540797a Visuals: per-class scale applied at spawn
90a071f Balance: faster movement + closer camera (test feedback)
ca39dfb Spawn: random starting edge per session
312f511 Build menu                                           ← v0.3.0-alpha
7ee1742 Phase 9 wiring                                       ← v0.2.0-alpha
```

`git log --oneline -20` for more.

---

## Branch / push status

- All work on `develop`.
- `main` has not been merged since project start (per Maestro's decision — wait for stable test pass before promoting).
- `develop` and `v0.3.1-alpha` pushed to origin. Branch is clean.
