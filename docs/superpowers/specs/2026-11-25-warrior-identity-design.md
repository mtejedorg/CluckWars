# Warrior identity: the Bully's hit-steal and the Enforcer (design and simulator plan)

Renamed from Champion to Enforcer on 2026-11-25; trophies are now called debts. Maestro's quoted sentences keep his original words.

Date: 2026-11-25. Status: design; the simulator slices S1 to S6 are built (results in section 14). **Amended 2026-11-25 (afternoon):** the Enforcer is
redesigned by Maestro after run B (section 14.2): fresh rivals, per-enemy debts that inflate the Steal (section 4); the rework
is planned as slices R1 to R4 (section 10.1). **Amended again (evening):** a hit on a fresh rival takes no food, it only stacks one more debt
(section 1, last Enforcer bullet); the old bite stays as data (`FreshBiteShare`) so both readings can be measured. The Bully is unchanged. Inputs: Maestro's decisions of 2026-11-25 (section 1, closed), the code read on
`develop` at `0230d380` plus the uncommitted BL-221 work of another session (`BotController`, `ScriptedPolicy`, `BotTactics`, `Warrior.asset`).
Method, as in `2026-11-23-thief-loot-mark-design.md`: simulator first, every number on the passive SO, shipped off, the game built only after
Maestro approves the measured rule.

## 1. Maestro's decisions (closed)

- Warrior class base (applied separately, BL-221 session): `Warrior.asset` `MoveSpeed` 10.8, `CargoCapacity` 20, `PeckCooldown` 0.71, `PeckCastSeconds` 0.4.
- **Bully, new version.** "Que el de los cooldowns reducidos no pueda robar per se, pero pueda utilizar sus cooldowns reducidos para machacar a todo
  el mundo. Eso sí que es de bully." Keeps `CooldownMultiplier` 0.75 (not Peck). Kit Peck, Jump, Stun, Shove; no Steal. Every rival his Stun or Shove
  actually affects loses 2 food into his ordinary cargo through the existing authority-confirmed steal path. One take per victim per 3 s across all his
  hitters. Clamped to his free room and the victim's cargo; the rest stays with the victim. Refused by the egg, a base wall, an empty victim, the lock.
  Peck never steals. Amounts per ability are data (Shove may later become Slow at 2). Fallback if it must be split again: hit-steal lives in fields,
  not in Bully-specific code.
- **Enforcer** (named by Maestro; replaces Relentless, which is shelved, not deleted). "Integremos las dos en una: el 2 extra de cortar un peck es otro
  trophy. No entiendo en qué afecta el tamaño, va a ser confuso." Kit Peck, Speed, Steal, Slow. The break and the debts are section 4.
- **Enforcer, redesign (Maestro, 2026-11-25, after run B; supersedes the break and the capacity debts).** The bite: "más simple aún: si le da
  un mamporrazo a un pollo que acaba de hacer peck (como 1 segundo, feedback visual), le roba el bocata (incluyendo mientras hace el propio peck)."
  The debts: "El trofeo es por enemigo, y crece por cada habilidad acertada (y corte de peck), inflando el steal." The base: "Mantenemos la carga
  a 20 pero bajamos la capacidad de farmeo. Los cooldowns le obligan a compaginar pelea con farmeo." Closed: no Peck-press break and no timing
  window on the Enforcer's side; a rival is fresh mid Peck-cast and about 1 s after its own Peck landed, and both it and the Enforcer see it; any
  of his hits on a fresh rival takes the bite; debts are per enemy, +1 per landed ability and per bite, and inflate his Steal on that enemy;
  cargo stays 20, the Warrior's foraging goes down (lever measured, section 9); no capacity debts, no size change.
- **Enforcer, the bite takes no food (Maestro, 2026-11-25, evening; supersedes the bite food above).** "El champion no se lleva nada de cortar un peck, solo
  una stack para el robo." So a hit on a fresh rival (mid Peck-cast, or within `FreshSeconds` after its Peck landed) takes no food: it adds the extra
  debt to that rival's count, on top of the +1 the hit itself gives. A mid-cast Stun or Shove cuts the cast as it does today; nothing moves from the
  pile or the victim's cargo, and all the food the Enforcer gets from a rival comes through the Steal, inflated by the count. The bite food stays as
  data, not a deletion: `FreshBiteShare` on the Enforcer passive (the share of the victim's `PeckAmount` taken, shipped 0 and 0 in the what-if anchor;
  1 is the old reading), so both can be measured.
- Overflow goes to the centre pile (ruled earlier today; the game path is a separate item; the simulator models it for these two kits).
- The Thief gets no Peck interplay change. `RelentlessPassiveSO` and `Relentless.asset` stay in the repo.

## 2. What exists (verified in code)

| Fact | Where |
|---|---|
| Bully kit today: Peck, Shadowstep (Jump), WingSlam (Stun), Snatch (Steal). The new kit swaps Snatch for CluckShock (Shove). | `Passives/Bully.asset` `Kit`; `WingSlamAbilitySO.Job` Stun, `CluckShockAbilitySO.Job` Shove |
| Relentless kit: Peck, SpeedBurst (Speed), Slow, Snatch (Steal): **already the Enforcer's kit**. | `Passives/Relentless.asset` |
| Bully cooldowns after x0.75: Stun 7 to 5.25 s (stun 1.3 s), Shove 5 to 3.75 s, Jump 2 to 1.5 s. | `WingSlam.asset`, `CluckShock.asset`, `Shadowstep.asset`, `BullyPassiveSO.ModifyCooldown` |
| Every foraging class pecks 3 a bite; casts last 0.4 s (Warrior, Speedy) and 0.7 s (Fatty). | `Data/Classes/*.asset` |
| A delta can change a kit: `"kits":{"Bully":[...]}`, same length, judged by `FixedKit.Problems`. | `BalanceBook.EvaluateKit` |
| A delta cannot touch `PassiveAbilitySO.Subclass` nor `AbilityBaseSO.Retired` (deny list); the roster is the registry's passives, one kit each. | `BalanceBook.DeniedFields`; `OracleKits.All`, `MatchKits.All` read `inputs.Abilities.Passives` |
| The book indexes only registered assets (`AbilityRegistrySO.All`, class stats, match config). | `BalanceBook.TryBuildIndex` |
| `ChickenSubclass` has two Warrior values (1 Relentless, 2 Bully); `FixedKit.ValidateRoster` wants a passive for every value. | `ChickenSubclass.cs`, `FixedKit.cs` |
| In the sim the Peck is not a cast: it is `MatchIntent.PeckPile`, started by `CanStartPeck`/`StartPeck`, landed by `Land`, cut by `Cut(CutCause)`. Capacity is `Farm.CargoCapacity`, a constant per seat. | `MatchSim.cs` |
| The game's Peck asks the pile only when its cast ends naturally; a cut cast asks nothing (ARCH-NET-07). | `PeckAbilitySO.OnDeactivate`, `PeckCastTests` |
| A pecking chicken never holds a live Shield (`CanStartPeck` refuses one, a Shield cast cuts the Peck). | `MatchSim.CanStartPeck`, `BeginCast` |
| The Warrior bot banks at 12 food whatever its beak (`BotProfile.ReturnAmount` 12, BL-221, in flight). | `BotTactics.ReturnThreshold(profile, phase, leading, capacity)` |
| (rework) In the sim a landed Stun cuts the victim's Peck **before** the hit hook runs (`Control`: `Cut(victim, CutCause.Stun)` then `HitSteal`); `Shove` cuts only above `ShoveCounts`; the Steal branch never cuts and only targets a carrier (`Stealable > 0`), egg refused, shield walk refuses only Slow, Hide is no refusal in `RivalCanBeHit`. | `MatchSim.Control`, `Shove`, `Cast` (Steal case), `MatchCombat.Refused` |
| (rework) Snatch: `StealAmount` 5, `Cooldown` 4, `SnatchRange` 5.16; WingSlam: `Cooldown` 7, `StunRadius` 5.4; CluckShock: `Cooldown` 5, `ShockRadius` 5.4. Every foraging class: `PeckAmount` 3, `PeckCooldown` 0.71. | `Data/Abilities/*.asset`, `Data/Classes/*.asset` |
| (rework) `Enforcer.asset` lives in `Data/Simulator/` (not `Passives/`), kit guids Peck `2b6f2fba…`, SpeedBurst `56e7e5a4…`, Slow `eca28bb3…`, Snatch `4c026dfa…`; WingSlam is `6bbb9c61252f875408fde062c994669c`, CluckShock `079c14ab0b344ca898f4be63fbf58374`. | `Data/Simulator/Enforcer.asset`, the `.meta` files |

## 3. Rule 1: the Bully's hit-steal

**In one line (for a child):** when your Stun or Shove hits a bird, you grab 2 of its food; the same bird only once every 3 seconds.

Edges:

| Case | Result |
|---|---|
| Stun lands (the victim's stun is set or extended: `ControlReapplyRules.IncomingExtends` true) | take |
| Stun that does not extend a longer stun already on the victim | no take, no lock (it did not affect him) |
| Shove lands and moves the victim (`impulse > 0` after his `KnockbackFactor`) | take |
| Shove on a victim whose knockback factor is 0 | no take, no lock |
| Slow lands (when a kit holds Slow) | take of `HitStealSlow` (0 shipped and designed: off) |
| Slow Area aura ticks | never a take |
| Peck, Jump | never a take |
| Egg (Shield egg phase) | refused (the egg already refuses stun and shove) |
| Shield walk phase | the stun or shove lands, so the take lands |
| Base wall between Bully and victim | refused (the hit itself is refused, `RivalCanBeHit`) |
| Victim empty (`StealRules.Stealable` 0) or Bully full | no take, no lock |
| Victim locked for this Bully | no take; the lock is not renewed |
| One cast hits three rivals | up to three takes, each through its own lock, in seat order, each clamped to the room left |
| Stun and Shove on one victim in one tick | the first takes, the second meets the lock |
| Two Bullies | independent locks; a victim can lose to each |
| Take | `StealMath.Clamp(amount, bully free room, victim stealable)`; the victim loses only what the Bully gains |
| Lock | starts only on a take above 0; lasts `HitStealLockSeconds` from that tick |
| Round reset | locks cleared |

Fields on `BullyPassiveSO` (served to every reader through `PassiveAbilitySO.HitSteal`, a `HitStealFacts` struct; no code asks "is this the Bully"):

| Field | Type | Shipped (off) | Designed | Sweep |
|---|---|---|---|---|
| `HitStealStun` | float `[Min(0)]` | 0 | 2 | 1, 2, 3 |
| `HitStealShove` | float `[Min(0)]` | 0 | 2 | 1, 2, 3 |
| `HitStealSlow` | float `[Min(0)]` | 0 | 0 | none |
| `HitStealLockSeconds` | float `[Min(0)]` | 3 | 3 | 2, 3, 4 |
| `Kit` | delta `kits.Bully` | Peck, Shadowstep, WingSlam, Snatch | Peck, Shadowstep, WingSlam, CluckShock | none |

Off means every amount 0: the shipped Bully plays exactly as today.

## 4. Rule 2: the Enforcer (redesigned 2026-11-25)

**In one line (for a child):** hit a bird that has just pecked and you stack an extra debt on it; every hit on the same bird is a debt, and your
Steal on that bird takes 2 more food for each debt.

**Kit:** Peck, WingSlam (Stun), CluckShock (Shove), Snatch (Steal); no cooldown multiplier, no other multiplier. Legal under `FixedKit.Problems`:
four slots, a Peck (the Warrior forages), no ability twice, no passive in a slot, `AllowedClasses` exactly Warrior, no Hunt. It is the old Bully
kit with Shove in place of Jump, so every ability is already a registered pool asset.

### 4.1 Fresh

A rival is **fresh** while either holds, and its current fresh state has not been bitten yet:

| State | Starts | Ends |
|---|---|---|
| Mid-cast | its Peck cast starts (`Pecking`, `_time < PeckDoneAt`) | the cast lands or is cut (by anyone, the bite included) |
| Just landed | its own Peck **landed with food** (take above 0) at time L | `_time > L + FreshSeconds`, or its next Peck cast starts (a new state) |

- A Peck cut by someone else opens no "just landed" state; a landing that took 0 (full beak, empty pile) opens none either.
- A bite consumes the state: one bite per victim per fresh state, whoever bites. A new cast start or a new landing opens a new, unbitten state.
- Fresh is a fact of the victim, not of a Enforcer: two Enforcers see the same state, and the first to bite spends it.
- Assassins never peck, so they are never fresh. A rival that is fresh and in its egg cannot be hit, so it cannot be bitten.
- `FreshSeconds` 0 is the switch: no rival is ever fresh, mid-cast included (shipped).

### 4.2 The fresh hit and the bite

**Amended (Maestro, evening): the shipped rule takes no food.** A hit on a fresh rival adds the extra debt of 4.3 and spends the fresh state; it
takes nothing from the pile or the cargo. A Stun or Shove still cuts a casting Peck as its own effect (a Steal never cuts). The food rules below are
the **bite**: they apply only when `FreshBiteShare` is above 0, with the bite amount = `FreshBiteShare` x the victim's `PeckAmount` (1 = the old
reading). In the tables, "the bite" and "bite debt" read as: the extra fresh-hit debt always; the food only with the share.

A **hit** is one of the Enforcer's Stun, Shove or Steal affecting a rival, with the "affected" test the Bully's hit-steal uses:

| Hitter | Affected when |
|---|---|
| Stun | the victim's stun is set or extended (`ControlReapplyRules.IncomingExtends` true) |
| Shove | the impulse after the victim's `KnockbackFactor` is above 0 |
| Steal | the Steal reaches him: in reach, no base wall, not in his egg, and he carries food (`StealRules.Stealable` above 0, Snatch's own target filter) |
| Peck | never a hit |

Any hit on a fresh rival stacks the fresh-hit debt and, with `FreshBiteShare` above 0, takes the **bite**, `FreshBiteShare` x the victim's own `PeckAmount` (3 today at share 1):

| Case | Result |
|---|---|
| Victim mid-cast | his cast is cut and nothing lands for him; his Peck cooldown stays spent (as any cut). The Enforcer takes `min(victim's PeckAmount, food in the victim's pile, his free room)` **from that pile**; what does not fit stays in the pile. The victim's cargo is untouched. Cut cause: the hit's own (`Stun`, or `Shove` when knocked above `ShoveCounts`), else the new `CutCause.Bite` (a Steal, or a shove too weak to cut by itself) |
| Victim just landed | the Enforcer takes `StealMath.Clamp(victim's PeckAmount, his free room, victim's stealable)` **from the victim's cargo** through the existing authority-confirmed steal path; what does not fit stays with the victim |
| Food that fits is 0 (Enforcer full, pile empty), or the share is 0 | the fresh hit still happens: the state is spent and the extra debt is earned (the catch is the feat; as the old break, decision C3); with the share at 0 nothing is taken and a Steal's hit cuts nothing |
| Fresh state already bitten | no bite; the hit still lands and still earns its own hit debt |
| Egg | the hit is refused, so no bite and no debt |
| Shield walk phase | Stun, Shove and Steal land (the walk refuses only Slow), so the bite lands |
| Base wall between them | the hit is refused (`RivalCanBeHit`) |
| Victim hidden | Hide is no refusal: if the hit lands, the bite lands |
| Enforcer stunned, dashing or removed | he cannot cast: no hit, no bite; his debt counts keep decaying |
| Enforcer hidden | his kit has no Hide; a cast ends any Hide as for every chicken |
| Two Enforcers on one victim | resolved in the rotating cast order of `ResolveCasts`: the first bites, the second's hit lands with its own hit debt and no bite |
| Victim executed | the killer drains him as today; every Enforcer's count on him goes to 0; his fresh state ends with his removal |
| What the victim keeps | everything not taken: the rest of the pile, the rest of his cargo, and, if he is a Enforcer himself, his own debts (being robbed no longer costs a debt) |
| A Bully's hit on a Enforcer | unchanged (section 3) |

**Order inside one cast.** The cast walks its victims in the seat loop of `Cast` (`v = 0 .. Seats-1`, as today). Each victim is resolved
completely before the next, and every take is clamped to the room left after the previous ones:

1. Freshness is read **before** the hit's own effect (a landed Stun cuts the cast first; the snapshot keeps it mid-cast for the bite).
2. The hit's own effect (stun set, shove impulse); a Steal's take waits for step 4.
3. Fresh hit (if fresh): the food bite only when `FreshBiteShare` is above 0; then debts on that victim: +1 for the fresh hit (the extra stack), +1 for
   a Stun or Shove hit, each capped at `DebtCap`, each restarting that victim's decay timer.
4. Steal only: take `StealMath.Clamp(StealAmount x StealFactor + DebtSteal x count, free room, stealable)`, where `count` already holds the bite
   stack of step 3; when the take is above 0 that victim's count goes to 0 (the Steal adds no debt); a take of 0 leaves the count.

### 4.3 Debts (per enemy)

| Case | Result |
|---|---|
| Count | one number per Enforcer per rival, 0 to `DebtCap` |
| +1 | a Stun or Shove that affected that rival; one more for any hit (Stun, Shove or Steal) on that rival while it is fresh, once per fresh state |
| At the cap | a gain adds nothing but restarts the decay timer |
| Decay | the count goes to 0 when `DebtSeconds` pass with no +1 on that rival (all at once, not one by one) |
| Cash | his Steal on that rival (section 4.2 step 4) |
| Enforcer executed | all his counts 0 |
| Victim executed | every count on that victim 0 |
| Banking | no effect (counts live on rivals, not in his beak) |
| Capacity | never changes: class capacity 20 |
| Round reset | all counts 0, every fresh state cleared |

### 4.4 Fields

On `EnforcerPassiveSO` (served through `PassiveAbilitySO.Bite`, a `BiteFacts`, and `PassiveAbilitySO.Debt`, a reshaped `DebtFacts`; no code
asks "is this the Enforcer"):

| Field | Type | Shipped (off) | Designed | Sweep |
|---|---|---|---|---|
| `FreshSeconds` | float `[Min(0)]`, 0 = no rival is ever fresh | 0 | 1.0 | 0.6, 1.0, 1.5 |
| `FreshBiteShare` | float `[Range(0,1)]`, share of the victim's `PeckAmount` a fresh hit takes as food; 0 = none (Maestro, evening) | 0 | 0 | 0, 1 |
| `DebtSteal` | float `[Min(0)]`, 0 = debts add nothing | 0 | 2 | 1, 2, 3 |
| `DebtCap` | int `[Min(0)]` | 3 | 3 | 2, 3, 4 |
| `DebtSeconds` | float `[Min(0.5)]` | 6 | 6 | 4, 6, 9 |
| `Kit` | asset | Peck, WingSlam, CluckShock, Snatch | same | none |

**Foraging lever** (class, not passive): `Warrior.asset` `PeckCooldown` 0.71 and `PeckAmount` 3, a plain delta (`"assets":{"Warrior":{...}}`), swept
as levels F0 to F2 in section 9; the exact values come from the measurement running now. It is class-wide on purpose (no new code), so it also
moves the Bully and shrinks the bite taken **from** a Warrior; the grid reports both.

Off means `FreshSeconds` 0 and `DebtSteal` 0: the Enforcer plays as a plain Peck, Stun, Shove, Steal kit. `Enforcer.asset` stays in
`Data/Simulator/`, **not registered**, seated only by the what-if roster swap (section 5); the game cannot pick it.

### 4.5 Arithmetic to watch (shipped Steal 5, starting values)

A full combo on one carrier with the shipped no-food rule: Stun on a just-landed rival (count 2: the Stun and the fresh stack), Shove (count 3, the
cap), Steal 5 + 2 x 3 = 11. The victim loses **11** from his cargo inside about 2 s (needs 11 room in the Enforcer's 20 and 11 food on the victim).
With `FreshBiteShare` 1 the old reading adds the 3-food bite (14 in all; mid-cast 11 from cargo and 3 from the pile). Ceiling in one 5 s window: the
Steal is ready again 4 s later, so about 11 + 7 = 18 without the bite (24 with it). Both are above the go limit of 6 (section 9); the mean is what
the run measures, and `DebtSteal` 1 gives 8 (combo) and 13 (ceiling).

### 4.6 Superseded 2026-11-25

The Peck-press break (`BreakReach`, `BreakCooldown`, `BreakImmuneSeconds`, the press preview, fallback and cancel, `CutCause.Break`) and the
capacity debts (`DebtRoom`, `DebtsClearOnBank`, one debt lost when robbed, overflow of the room to the centre pile) are retired from the
design. Why: section 14.2 (the break fired 0.24 to 0.33 times a match because a 0.4 s cast leaves the bot about 0.1 s to press; the room was
8 to 11 % used because the bot banks at 12 in a beak of 20). Slices S1, S4 and S5 built them; R1 to R4 (section 10.1) remove them.

## 5. How a what-if reaches each kit (the deny list)

- **Bully:** a plain delta. The amounts and the lock are serialized fields of a registered passive; the kit is `kits.Bully`. Nothing denied.
- **Enforcer:** a passive class cannot be swapped by a delta (`Subclass` denied, registry not delta-able). Least invasive legal way: a
  **simulator roster swap**. The what-if file gains one optional top-level key, `"roster": {"Relentless": "Enforcer"}` (asset names). The editor
  entry builds a temporary `AbilityRegistrySO` whose `All` is the shipped array with that one passive replaced, and runs the book and the simulator over
  it; deltas then name `Enforcer` fields as usual. The shipped registry, the game and `BalanceBook` are untouched; the swap is refused unless both
  passives allow the same single class.
- Rejected: registering Enforcer now (the game would offer a ninth kit, and `ChickenSubclass` has no free Warrior value); putting break and debt
  fields on `PassiveAbilitySO` and turning Relentless into the Enforcer by delta (pollutes every passive, labels the kit Relentless, needs
  Relentless's own multipliers zeroed in every scenario).
- Labels: `OracleKits.LabelOf(passive)` = `Subclass` name when the asset name is the subclass's suffix (every shipped passive), else
  `<Class>_<asset name>`, so the swapped kit reads `Warrior_Enforcer`.

## 6. What is seen and heard (game slice, later)

Bully, on a take: `+2` pop over the Bully, three food chunks from victim to Bully (`FoodChunkFlight`), the existing Steal hit sound; locked rivals
show a faint lock ring to the Bully only (so a child sees why a hit took nothing). Victim: `-2` pop on his cargo bar, a short buzz (existing haptic).

Enforcer (redesign):
- **Fresh marker.** A fresh rival wears a bright sandwich glint over its head from its cast start to `FreshSeconds` after a landing that took
  food, with a ring that drains over the just-landed seconds; it goes out the moment the state is bitten or ends. Seen by every Enforcer in the
  match and by the fresh chicken itself (its own glint plus a pulse on its Peck hex: "you just pecked, mind the Enforcer"); other players do not
  see it. Only shown while a Enforcer is in the match.
- **Bite.** `BITE!` pop over the Enforcer; the food flies from the pile (mid-cast) or from the victim (just landed) to his beak; a crunch sound.
  Victim: mid-cast, his cast bar shatters; just landed, a `-3` pop on his cargo bar; a short buzz.
- **Debt pips, per enemy.** Over each rival the Enforcer has a count on: 0 to `DebtCap` small debt pips with a thin ring that drains over
  `DebtSeconds`; a +1 pops a pip in, decay pops them all off together, a cash flies them into the Steal. The victim sees the same pips over
  his own head ("the Enforcer has 2 on you"); other players do not.
- **Inflated Steal.** The Enforcer's Steal hex shows the amount for the rival it would take from (the nearest carrier in reach), `5`, `7`,
  `9`, `11` with starting values, growing with each pip, read from `StealAmount`, `StealFactor` and `DebtSteal` (ARCH-DATA-12, no number
  in text). On the cash: a bigger `+11` pop and the pips flying in.
- Words by `narrative-designer`; colours in `FeedbackPaletteSO` (ARCH-LAY-15). The superseded break feedback (brackets, `BREAK!`, `BROKEN`,
  room segments, overflow arc) is dropped.

## 7. Bot rules (pure, `Assets/_Game/Scripts/Gameplay/`, facts in, decision out)

Called by `ScriptedPolicy` (P0) now and by `BotController` in the game slice; `UtilityPolicy` (P1) uses the same gates and adds its own value.

`BotHitStealRules` (Bully):
- `float Gain(float amount, float victimStealable, float freeRoom, bool locked)` = `HitStealRules.Take(...)` with no refusal: what one hit would take.
- `bool CastForFood(float bestGainInReach)`: cast a ready Stun or Shove when some rival in its reach would give at least 1.
- `int PickHitter(float stunGain, float shoveGain, float stunCooldown, float shoveCooldown)`: the larger total gain; a tie goes to the shorter cooldown (Shove), keeping the Stun for control.
- `bool WorthHunting(float gainIfUnlocked, float lockLeft, float walkSeconds)`: hunt a prey only if it would give at least 1 and its lock ends before the walk does.
- No new bank rule: at `ReturnAmount` 12 in a beak of 20 the Bully always has room for a take.

`BotEnforcerRules` (Enforcer; replaces `BotBreakRules`):
- `float FreshLeft(bool casting, float castLeft, float sinceLanded, bool landedWithFood, bool bitten, float freshSeconds)`: how long the rival stays
  fresh (a hit on it stacks the extra debt): `castLeft + freshSeconds` mid-cast (assuming the cast lands), `freshSeconds - sinceLanded` just landed, 0 when bitten, not fresh or
  `freshSeconds` 0. Built on `BiteRules.Fresh`.
- `bool HitFresh(float freshLeft, float reaction)`: a fresh rival in a ready hitter's reach is worth a hit now when `freshLeft > reaction`.
- `int PickBuilder(bool stunReady, bool shoveReady, float stunCooldown, float shoveCooldown)`: the hitter that builds a debt: Stun or Shove,
  the shorter cooldown first (Shove), -1 when neither is ready.
- `bool Cash(int count, int cap, float decayLeft, float nextBuilderReadyIn, float stealGain)`: Steal that rival when `stealGain >= 1` and
  `count >= 1` and either `count >= cap` or no builder is back before the count decays (`nextBuilderReadyIn >= decayLeft`). A Steal at count 0
  stays the existing Steal rule.
- `float StealGain(float stealAmount, float stealFactor, float perDebt, int count, float freeRoom, float victimStealable)` =
  `StealMath.Clamp(DebtRules.StealAmount(...), freeRoom, victimStealable)`.
- `bool Fight(float fightGain, float fightSeconds, float peckAmount, float peckCycleSeconds)`: leave the pile when the fight's food per second
  (bite food, which is 0 at `FreshBiteShare` 0, plus the Steal gain on the count the fresh stack raised, over walk plus casts) beats the Peck's (`peckAmount / peckCycleSeconds`, cycle = `PeckCooldown + PeckCastSeconds`). This is
  where the foraging lever of section 9 bites: a slower or smaller Peck makes more fights worth it.
- No bank rule: the Enforcer banks at `ReturnAmount` 12 like every Warrior; the capacity never grows, so `ReturnFraction` goes.

P0 order each think: a fresh rival in a builder's reach with `HitFresh` true gets the builder (`PickBuilder`), or the Steal when no builder is
ready and he carries; else a due `Cash`; else forage. P1 (simulator only): `BestHit` rates Enforcer targets by `Fight`'s value (bite + the
inflated Steal it builds toward) with the same gates; `PeckCut` loses its break option; capacity from `SeatView.Capacity`.

## 8. The simulator model

### 8.1 Pure rules (Gameplay, shared with the game later)

`HitStealRules.cs`: `struct HitStealFacts { float Stun, Shove, Slow, LockSeconds; float AmountFor(AbilityJob job); bool Active; static None }`
(`AmountFor` is 0 for every job but Stun, Shove, Slow); `static float Take(float amount, float freeRoom, float victimStealable, bool locked)`
through `StealMath.Clamp`; `static bool Locked(float now, float lockUntil)`.

`BiteRules.cs` (replaces `PeckBreakRules.cs`): `struct BiteFacts { float FreshSeconds; float FoodShare; bool Active => FreshSeconds > 0; static None }`
(`FoodShare` is the passive's `FreshBiteShare`; the callers multiply the victim's `PeckAmount` by it before `FromCast` / `FromCargo`);
`static bool Fresh(bool casting, float sinceLanded, bool landedWithFood, bool bitten, float freshSeconds)` (`freshSeconds > 0 && !bitten &&
(casting || (landedWithFood && sinceLanded <= freshSeconds))`); `static float FromCast(float victimPeckAmount, float pileFood, float freeRoom)`
(the old `Bite`); `static float FromCargo(float victimPeckAmount, float freeRoom, float victimStealable)` through `StealMath.Clamp`.

`DebtRules.cs` (reshaped): `struct DebtFacts { bool IsEnforcer; float Steal; int Cap; float Seconds; static None }`;
`static int Add(int count, int cap)` (the old `Earn`); `static float StealAmount(float stealAmount, float stealFactor, float perDebt, int count)`
= `stealAmount x stealFactor + perDebt x max(count, 0)`; `static bool Decayed(float now, float until)`. `LoseOne`, `Room`, `StealEarns`,
`Overflow` are deleted.

`PassiveAbilitySO`: `virtual HitStealFacts HitSteal => HitStealFacts.None`, `virtual BiteFacts Bite => BiteFacts.None` (replaces `PeckBreak`),
`virtual DebtFacts Debt => DebtFacts.None`.

### 8.2 Simulator types and call sites (`Assets/_Game/Scripts/Balance/`)

Kept from S3 (Bully): `MatchPassive.HitSteal`; `Chick.HitLockUntil`; `SeatView.HitLockLeft`; `HitSteal(c, v, job)` from `Control` and `Shove`;
`CapacityOf(int s)` as the only reader of `Farm.CargoCapacity` (now just `Farm.CargoCapacity`, the debt term goes).

Removed (S4/S5 break and room): `MatchPassive.Break`; `MatchIntent.Break/BreakTarget`; `CastRequest.Break` and its branch in `ResolveCasts`/`Cast`;
`ResolveBreak`; `Chick.Debts`, `NextBreakAt`, `BreakImmuneUntil`; `SeatView.Debts`, `BreakImmuneLeft`, `BreakLeft`; `Robbed` and its three
calls (`DoSteal`, `TakeLoot`, `HitSteal`; it only did the debt loss and the spill); `SpillOverflow`; `BankDebts`; `EarnDebt` (replaced);
the debt drain in `Execute`; `DebtSecondsAcc`; the Steal branch's `StealEarns` line. `CutCause.Break` is renamed `CutCause.Bite` (`"bite"`);
`MatchEventKind.PeckBreak` and `Overflow` become `Bite` and `DebtCollect` (sim-only enums, appended order kept).

Added:
- `MatchPassive.Bite` (`BiteFacts`), `MatchPassive.Debt` (reshaped), read in `MatchKits.ReadPassive`.
- `Chick`: `float LandedAt` (`-inf` until a landing), `bool LandedWithFood`, `bool FreshBitten`; `int[] DebtOn` and `float[] DebtUntil` (by
  victim, sized `Seats`). `StartPeck` clears `FreshBitten`; `Land` sets `LandedAt`, `LandedWithFood = take > 0` and clears `FreshBitten`.
- `bool IsFresh(int v, in BiteFacts f)` = `BiteRules.Fresh(ch.Pecking && _time < ch.PeckDoneAt, _time - ch.LandedAt, ch.LandedWithFood, ch.FreshBitten, f.FreshSeconds)`.
- `EnforcerHit(int c, int v, AbilityJob job, bool castingBefore, bool freshBefore)`: no-op unless `PassiveOf(c).Debt.IsEnforcer`. On
  `freshBefore`: `FreshHits++`, the state is spent (`FreshBitten`), and the fresh stack is added; the food bite below runs only when
  `Bite.FoodShare` is above 0: mid-cast (`castingBefore`) cuts the cast if it is still running (`Cut(v, CutCause.Bite)`), takes `BiteRules.FromCast` from
  `_pile[v.PeckPile]`; else `BiteRules.FromCargo` from the cargo (with `TakeLootShare`, `NoteLoss`, `LostToSteals`, as `HitSteal` does); sets
  `FreshBitten`, `Record(Bite)`. Then `AddDebt(c, v)` for the bite and, for Stun or Shove, once more for the hit.
- Call sites: `Control` stun branch reads `castingBefore`/`freshBefore` **before** `Cut(victim, CutCause.Stun)` and calls `EnforcerHit` after
  `HitSteal`; `Shove` the same around its cut; the Steal case of `Cast` (non-marked branch) calls `EnforcerHit` after the target filter and before
  `DoSteal`, which gains a `bonus` argument: `DebtRules.StealAmount(ab.StealAmount, factor, Debt.Steal, c.DebtOn[v])`, then the count is
  zeroed when the take is above 0 (counted as a cash).
- Each tick: a count whose `DebtUntil` has passed goes to 0 (`DebtRules.Decayed`, counted). `Execute`: the victim's row and column of every
  Enforcer's counts go to 0. Round reset: counts and fresh state cleared.
- `SeatView`: `float SinceLanded`, `bool LandedWithFood`, `bool FreshBitten` (every seat); `int[] DebtOn`, `float[] DebtLeft` (own seat only).
  The policies call `BiteRules.Fresh` with their own `FreshSeconds`; they never re-derive it.

### 8.3 Counters (`SeatCounters`) and report rows

Bully (kept): `HitTakes`, `HitFood`, `HitRefusedLock`, `HitRefusedNoFood`, `HitRefusedRoom`, `HitLostFood` (victim side). Hit-steal food also adds
to `Stolen`, `StolenFromSeat` and the victim's `LostToSteals`, not to `Steals`.

Enforcer (new `FreshHits`: hits on a fresh rival that stacked the extra debt, whatever the food; then replace `Breaks`, `BreakFood`, `BreakLeftInPile`, `BreakFallbacks`, `BreakCancels`, `BreakRefused`, `PecksBroken`, `DebtsLost`, `DebtPeak`,
`DebtSeconds`, `DebtsAtEnd`, `DebtRoomFilled`, `DebtsCleared`, `OverflowToCentre`):
`BitesFromCast`, `BitesFromCargo` (only at a bite share above 0), `BiteFoodFromCast`, `BiteFoodFromCargo`, `BiteLeftInPile`, `BiteLeftWithVictim`, `BitesSpent` (hits on a fresh
state already bitten), `PecksBitten` and `BiteLostFood` (victim side); `DebtsEarned`, `DebtsDecayed`, `DebtsCleared` (executions);
`DebtCollects` (Steals with a count above 0), `DebtsCollected` (sum of counts consumed), `DebtBonusFood` (the `DebtSteal x count` part that
fitted); `StealFood` (food from Steal casts, so mean Steal size = `StealFood / Steals`). Identity food = `BiteFoodFromCast + BiteFoodFromCargo +
DebtBonusFood`. `WorstVictimLoss5s` stays (cargo bites and Steals feed `NoteLoss`; a mid-cast bite is not the victim's cargo, so it is reported
apart as `BiteFoodFromCast`).

`MatchReport` "Warrior kits" block, Enforcer row: win rate; bites per match from cast and from cargo, and their food; debts earned and decayed
per match; debts consumed per cash (`DebtsCollected / DebtCollects`); mean Steal size; identity food per match; worst 5 s window (mean, 95th
percentile). The break, debts-held, room-used and overflow columns go. `MatchWhatIf.Numbers` group `warrior` follows; `WhatIfOutput.DebtKit`
stays (`Debt.IsEnforcer`).

### 8.4 Drift guards

- `MatchWarriorTests.Sim_CallsTheSharedRules_NotCopies`: source scan of `MatchSim.cs` finds `HitStealRules.Take`, `BiteRules.Fresh`,
  `BiteRules.FromCast`, `BiteRules.FromCargo`, `DebtRules.Add`, `DebtRules.StealAmount`, and proves it saw them (CODE-TEST-13); and finds no
  `PeckBreakRules`, `ResolveBreak`, `SpillOverflow`.
- `MatchWarriorTests.EveryCapacityRead_GoesThroughCapacityOf`: unchanged.
- `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem` lists the `BotHitStealRules` and `BotEnforcerRules` calls (and no
  `BotBreakRules`).
- `MatchWarriorTests.ShippedOff_PlaysExactlyAsBefore`: the shipped Bully facts against `HitStealFacts.None`; the Enforcer as shipped
  (`FreshSeconds` 0, `DebtSteal` 0) against `BiteFacts.None` and `DebtFacts.None` on the same kit: same seed, identical winners, end times and
  food counters.
- The "affected" tests reuse `ControlReapplyRules.IncomingExtends` and the shove impulse, as the Bully does; no new mirror.

## 9. What-if grids, trimmed

Budget: about 25 s a scenario at `replicateScale` 0.4; one run is kept to at most 16 scenarios (about 7 min), as each stage of the loot-mark runs. The
full grids are 27 (Bully) and 54 (Enforcer) scenarios, about 34 min. Read the shipped Warrior stats first: run only once `Warrior.asset`
(10.8, 20, 0.71, 0.4) is committed.

**Run A, Bully** (`whatif.json`, no roster key; every delta carries `"kits":{"Bully":["Peck","Shadowstep","WingSlam","CluckShock"]}`), 12 scenarios:
B0 kit only (amounts 0); the 3 x 3 Stun x Shove amounts at lock 3 (9); lock 2 and 4 at 2 / 2 (2). Why: the lock rarely binds alone, since one Bully's
two hitters come back 3.75 and 5.25 s apart, so the lock only matters when both hitters are ready at once or 2 to 4 s apart; it is measured at the
anchor and, in a second run only if the anchor is close, at the best amount cell. The baseline of this run is the shipped game (Relentless and the
old Bully): it is the reference for run B too.

**Run B, Enforcer (superseded, results in 14.2).** The reach x cooldown and debt-lifetime grid of the break design; kept only as the record of
section 14.2.

**Run C amended (Maestro, evening):** the foraging levels are `PeckCooldownMultiplier` 1.0, 1.25, 1.5 on the Enforcer passive (BL for step B), not the class-wide
`Warrior` Peck values; every cell uses `FreshBiteShare` 0 except the two bite-on scenarios (anchor with share 1 at multipliers 1.0 and 1.5). About 15 scenarios.

**Run C, the reworked Enforcer** (`whatif-warrior-enforcer.json`, `"roster":{"Relentless":"Enforcer"}`; the Thief values of Maestro's 2026-11-25
note, bounty 8, steal x1.6, capacity 16, in every baseline). Baseline: the Enforcer as shipped (kit Peck, WingSlam, CluckShock, Snatch;
`FreshSeconds` 0, `DebtSteal` 0) at the shipped foraging F0. Foraging levels (class `Warrior`, placeholders until the running measurement names
the lever; replace F1 and F2 with its two best values, keep F0):

| Level | `Warrior.PeckCooldown` | `Warrior.PeckAmount` |
|---|---|---|
| F0 shipped | 0.71 | 3 |
| F1 slower Peck | 0.95 | 3 |
| F2 smaller bite | 0.71 | 2 |

Enforcer cells, crossed with the three levels:

| Cell | `FreshSeconds` | `DebtSteal` | `DebtCap` | `DebtSeconds` | Why |
|---|---|---|---|---|---|
| K kit only | 0 | 0 | 3 | 6 | the kit and the foraging alone (at F0 it is the baseline itself) |
| B bite on | 1.0 | 2 | 3 | 6 | the anchor with `FreshBiteShare` 1 (the old reading: the victim's `PeckAmount` as food); all the other cells use share 0 |
| H hits only | 0 | 2 | 3 | 6 | debts from hits, no bite: what the bite adds |
| A anchor | 1.0 | 2 | 3 | 6 | the design |
| T1 | 1.0 | 1 | 3 | 6 | a gentler cash (worst combo 11, section 4.5) |
| T3 | 1.0 | 3 | 3 | 6 | a harder cash |

That is 14 scenarios plus the baseline (F0 x K), about 6 min. A second run (6 scenarios) only if a cell is within 5 points of the band: at that cell
and level, `FreshSeconds` 0.6 and 1.5, `DebtCap` 2 and 4, `DebtSeconds` 4 and 9. The report adds per scenario the section 8.3 Enforcer row,
and the Bully's row too, because F1 and F2 change the Warrior class.

Example delta: `{"schema":1,"assets":{"Enforcer":{"FreshSeconds":1.0,"DebtSteal":2},"Warrior":{"PeckCooldown":0.95}}}`.

Go when all hold (the oracle is a guide; Maestro decides): each Warrior kit 20 to 30 % under P0, P1 and mixed; no non-Warrior kit moves more than 5
points; clock share at most 12 %; worst 5 s window mean at most 6 food (a forgiving children's game, CODE-DESIGN-05); P0 and P1 within 10 points of
each other for each Warrior kit. Shipped references (match report 2026-11-24): Bully 19.9 / 13.7 / 15.3, Relentless 6.1 / 7.1 / 8.9. For the
Enforcer also: bites at least 2 a match under mixed (the break managed 0.3), and identity food at least a fifth of his banked food, or the rule is
not his identity.

## 10. Build plan: simulator slices

Common to every slice: read `docs/ARCHITECTURE_RULES.md` ARCH-DATA-11, ARCH-DATA-14, ARCH-NET-07, ARCH-LAY-01/02 and CODE-TEST-13/15; other sessions share
the tree, so check `git status`, stage by explicit path, never `git add -A`, stash, reset or broad checkout. **Gate** (every slice): compile clean
(`EditorUtility.scriptCompilationFailed` false), then the slice's tests, then the full EditMode suite through `TestRunnerApi` from `script-execute`
with the polled result file; if no Editor is open, `Unity.exe -runTests -batchmode -projectPath . -testPlatform EditMode -testResults results.xml`. Never
both at once (the batch lock). Each commit ends with the co-author line. Backlog id: take one with `/backlog` before slice 1 and put it in every message.

**S1. Pure rules and passives** (no simulator change).
Files: `Gameplay/HitStealRules.cs`, `Gameplay/PeckBreakRules.cs`, `Gameplay/DebtRules.cs`; `Abilities/PassiveAbilitySO.cs` (three virtuals);
`Abilities/Passives/BullyPassiveSO.cs` (four fields, `HitSteal` override); `Abilities/Passives/EnforcerPassiveSO.cs` and
`Data/Abilities/Passives/Enforcer.asset` (made in the Editor, not registered). Tests `HitStealRulesTests`: `Take_ClampsToRoomAndVictim`,
`Take_LockedOrZeroAmount_IsZero`, `AmountFor_PeckJumpAndEveryOtherJob_IsZero`; `PeckBreakRulesTests`: `CanBreak_NeedsCastReachNoWallNoEggNoImmunity`,
`CanBreak_ReachZero_IsNeverTrue`, `Bite_IsTheSmallestOfVictimBitePileAndRoom`, `Resolve_BreakThenPileThenCancel`, `Nearest_PicksTheSmallestDistance_MinusOneForNone`;
`DebtRulesTests`: `Earn_StopsAtTheCap`, `LoseOne_NeverBelowZero`, `Room_IsDebtsTimesRoom`, `StealEarns_AtOneFood_NotBelow`, `Overflow_IsTheExcessOnly`;
`WarriorPassiveDataTests`: `ShippedBully_HitStealIsOff`, `Enforcer_IsNotRegistered_AndItsKitIsLegal` (`FixedKit.Problems` empty, same four assets as
Relentless), `Delta_CanSetEveryNewField_AndTheBullyKit` (`BalanceBook.Evaluate` accepts, nothing ignored). Commit:
`feat(balance): hit-steal, peck break and debt rules with their passive fields, shipped off (BL-xxx)`.

**S2. Roster swap for the what-if.**
Files: `Balance/OracleRoster.cs` (`static AbilityRegistrySO Swap(AbilityRegistrySO shipped, PassiveAbilitySO outgoing, PassiveAbilitySO incoming)`,
caller destroys the copy); `Balance/MatchWhatIf.cs` (`WhatIfSpec.Roster`, a name-to-name map, parsed from `"roster"`); `Balance/OracleKits.cs`
(`LabelOf`); `Editor/OracleMenu.cs` `RunWhatIf` (load the named passives with `AssetDatabase`, swap, build the book over the copy, write the swap into
the report header). Tests `OracleRosterTests`: `Swap_ReplacesOneSeat_KeepsEveryOtherEntry`, `Swap_LeavesTheShippedRegistryUntouched`,
`Swap_RefusesAPassiveOfAnotherClass_OrOneNotInTheRegistry`; `WhatIfSpecTests.Roster_ParsesAndRejectsAnythingButAnObjectOfNames`;
`OracleKitsTests.LabelOf_ShippedPassivesKeepTheirSubclassName_TheSwapReadsWarriorEnforcer`. Commit:
`feat(balance): what-if roster swap so an unregistered passive can take a seat (BL-xxx)`.

**S3. The Bully in the simulator.**
Files: `MatchKit.cs`, `MatchKits.cs`, `MatchSim.cs` (`HitSteal`, locks, `Robbed` stub with no debts yet), `MatchCombat.cs` (counters). Tests
`MatchWarriorTests`: `Stun_TakesItsAmount_IntoTheBullysCargo`, `Shove_OnAnImmovableVictim_TakesNothing`, `TheLock_RefusesTheSecondHitter_UntilItEnds`,
`AStunThatDoesNotExtend_TakesNothing_AndStartsNoLock`, `OneCast_ThreeVictims_ThreeTakes_ClampedToTheRoomLeft`, `Egg_RefusesTheHit_AndTheTake`,
`EmptyVictimOrFullBully_NoTake_NoLock`, `Peck_NeverTakes`, `ShippedOff_PlaysExactlyAsBefore` (Bully half), `Sim_CallsTheSharedRules_NotCopies` (Bully
half). Commit: `feat(balance): Bully hit-steal in the match simulator (BL-xxx)`.

**S4. The Enforcer in the simulator.**
Files: `MatchPolicy.cs` (`MatchIntent.Break/BreakTarget`, `SeatView` fields), `MatchSim.cs` (`CapacityOf`, debts, `ResolveBreak`, `SpillOverflow`,
`CutCause.Break`, events), `MatchCombat.cs` (counters, `SlidingWindowSum`). Tests `MatchWarriorTests`: `Break_CutsTheCast_TakesTheBite_RestStaysInThePile`,
`Break_AfterTheCastEnded_FallsBackToAPilePeck_ElseCancelsAndSpendsNothing`, `Break_SetsBreakCooldown_AndTheVictimsImmunity`, `Break_WalledOrImmune_IsRefused`,
`TwoBreakersOneVictim_TheSecondFallsBack`, `Debts_FromBreaksAndSteals_OnePerCast_CappedAtTheCap`, `Robbed_LosesOne_AndTheExcessGoesToTheCentrePile`,
`Executed_LosesAll_NothingOverflows`, `Banking_KeepsDebts`, `EveryCapacityRead_GoesThroughCapacityOf`, `ShippedOff_PlaysExactlyAsBefore` (Debt
half), `SlidingWindowSum_IsTheMostInAnyWindow`. Commit: `feat(balance): Enforcer peck break, debts and overflow in the match simulator (BL-xxx)`.

**S5. Bot rules and both policies.** Starts only after the BL-221 session has committed `ScriptedPolicy.cs`, `BotTactics.cs` and `BotController.cs`.
Files: `Gameplay/BotHitStealRules.cs`, `Gameplay/BotBreakRules.cs`; `Balance/ScriptedPolicy.cs`; `Balance/UtilityPolicy.cs`; `MatchSim.BuildView`.
Tests `BotWarriorRulesTests`: one per function of section 7 at its edges (`CastForFood_AtOneFood`, `PickHitter_TieGoesToTheShorterCooldown`,
`WorthHunting_SkipsAPreyLockedPastTheWalk`, `Candidate_NeedsTheCastToOutlastTheReaction`, `Approach_OnlyWhenTheWalkBeatsTheCast`,
`ShouldBreak_AtAPileOnlyWhileADebtIsLeft`, `ReturnFraction_GrowsWithTheDebtRoom`); `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem`
extended; `ScriptedPolicyTests.P0Bully_CastsStunAtALoadedRival_NotAtAnEmptyOne`, `P0Enforcer_BreaksARivalCastingInReach`;
`UtilityPolicyTests.P1_BreaksOnlyWhenTheValueBeatsItsOwnPeck`. Commit: `feat(balance): Bully and Enforcer bot rules in P0 and P1 (BL-xxx)`.

**S6. Report, what-if numbers and the two runs.**
Files: `MatchReport.cs`, `MatchStats.cs` (if a label helper is needed), `MatchWhatIf.cs` (`warrior` group, `BullyKit`, `DebtKit`); the two what-if
files in `Captures/oracle/` (not committed). Tests `MatchReportTests.WarriorBlock_ShowsEveryCounterOfSection8`, `MatchWhatIfTests.Numbers_CarryTheWarriorGroup`.
Then run A and run B (`-executeMethod CluckWars.EditorTools.OracleMenu.MatchWhatIfBatch -oracleWhatIf <file>`), and write the results into this spec
as a new section 14 (results). Commit (code and spec only): `feat(balance): Warrior kit rows in the match report and what-if (BL-xxx)`.

### 10.1 Rework slices R1 to R4 (the redesigned Enforcer, section 4)

For an implementer who has not seen the design talk: read sections 4, 7 and 8 of this spec first; the Bully (section 3, slice S3, `HitStealRules`,
`BotHitStealRules`) is **not touched**. Simulator only: `Enforcer.asset` stays unregistered, every new number ships off, the game plays exactly as
before. Same rules, gate and git discipline as the S slices above (the common paragraph of section 10); another session may be running Unity in
batch mode, so check for the project lock before any run and never start a second one. Backlog id: take one with `/backlog` (the S slices used
BL-222) and put it in every message. Each slice compiles and passes the full suite on its own.

**R1. Pure rules, bot rules and the passive (additive: nothing is deleted yet).**
Files: new `Gameplay/BiteRules.cs` (section 8.1); `Gameplay/DebtRules.cs` gains `Add`, `StealAmount`, `Decayed` and `DebtFacts` gains
`Steal`, `Seconds` (the old members stay until R2); new `Gameplay/BotEnforcerRules.cs` (every function of section 7); `Abilities/PassiveAbilitySO.cs`
gains `virtual BiteFacts Bite => BiteFacts.None`; `Abilities/Passives/EnforcerPassiveSO.cs` gains `FreshSeconds` 0, `DebtSteal` 0,
`DebtSeconds` 6 (`DebtCap` 3 stays) with tooltips, overrides `Bite` and fills the new `DebtFacts` fields; `Data/Simulator/Enforcer.asset`:
kit Peck `2b6f2fba609676f439ec424dfd05e5f6`, WingSlam `6bbb9c61252f875408fde062c994669c`, CluckShock `079c14ab0b344ca898f4be63fbf58374`,
Snatch `4c026dfa11fd40cd9914aa8fb37090b6`, and the new fields at their shipped values (in the Editor; the class summary and its remarks name the new
kit and rule).
Tests: `BiteRulesTests.Fresh_MidCastOrJustLandedWithFood_NotBitten` (casting true; landed 0.9 s ago with food at window 1.0 true, 1.1 false; landed
with no food false; bitten false), `Fresh_FreshSecondsZero_IsNeverFresh_EvenMidCast`, `FromCast_IsTheSmallestOfBitePileAndRoom` (3 / 2 / 10 is 2;
3 / 10 / 1 is 1; any negative is 0), `FromCargo_IsClampedToRoomAndVictim` (3, room 2, cargo 10 is 2; cargo 1 is 1);
`DebtRulesTests.Add_StopsAtTheCap`, `StealAmount_IsBaseTimesFactorPlusPerDebtTimesCount` (5, 1, 2, 3 is 11; count -1 is 5), `Decayed_AfterTheUntilOnly`;
`BotEnforcerRulesTests.FreshLeft_IsCastLeftPlusWindow_OrWindowMinusSinceLanded_ZeroWhenBitten`, `HitFresh_NeedsTheWindowToOutlastTheReaction`,
`PickBuilder_ShoveFirst_MinusOneWhenNoneReady`, `Cash_AtTheCap_OrWhenNoBuilderBeatsTheDecay_NeverAtCountZero`, `StealGain_IsTheInflatedAmountClamped`,
`Fight_OnlyWhenTheFightBeatsThePeckRate` (a slower Peck flips the same fight from false to true); `WarriorPassiveDataTests.Enforcer_IsNotRegistered_AndItsKitIsLegal`
updated (the four assets above, `FixedKit.Problems` empty), `ShippedEnforcer_BiteAndDebtStealAreOff` (`Bite.Active` false, `Debt.Steal` 0),
`Delta_CanSetEveryNewField` (`FreshSeconds`, `DebtSteal`, `DebtCap`, `DebtSeconds` through the roster swap as the existing test does, and
`Warrior.PeckCooldown` / `PeckAmount`; `BalanceBook.Evaluate` accepts, nothing ignored). An S4 test that depended on the old kit (Speed, Slow) is
fixed or left to R2, which rewrites it. Commit: `feat(balance): Enforcer bite and per-enemy debt rules, kit Peck Stun Shove Steal, shipped off (BL-xxx)`.

**R2. The simulator swap: the bite and the counts in, the break and the room out.**
Delete: `Gameplay/PeckBreakRules.cs` and `Editor/Tests/PeckBreakRulesTests.cs`; `Gameplay/BotBreakRules.cs` and its four tests in
`BotWarriorRulesTests` (`Candidate_*`, `Approach_*`, `ShouldBreak_*`, `ReturnFraction_*`); `PassiveAbilitySO.PeckBreak`; `EnforcerPassiveSO`
`BreakReach`, `BreakCooldown`, `BreakImmuneSeconds`, `DebtRoom`, `DebtsClearOnBank` (reserialize `Enforcer.asset` so the stale keys go);
`DebtFacts.Room`, `ClearOnBank`; `DebtRules.Earn`, `LoseOne`, `Room`, `StealEarns`, `Overflow`; everything listed as removed in section 8.2;
the break branches of `ScriptedPolicy` (`_candidate`, `_approachDist`, `_breakTarget`, `_approach`, the `ReturnFraction` call) and `UtilityPolicy`
(`PeckCut`'s break option), so the Enforcer bot plays its plain kit until R3; in `MatchWarriorTests` every `Break_*` test,
`TwoBreakersOneVictim_TheSecondFallsBack`, `BreakWithoutTheKit_IsARejectedIntent`, `Debts_FromBreaksAndSteals_OnePerCast_CappedAtTheCap`,
`Robbed_LosesOne_AndTheExcessGoesToTheCentrePile`, `Executed_LosesAll_NothingOverflows`, `Banking_KeepsDebts`, `Banking_ClearsDebts_WhenThePassiveSaysSo`;
`ScriptedPolicyTests.P0Enforcer_BreaksARivalCastingInReach`; `UtilityPolicyTests.P1_BreaksOnlyWhenTheValueBeatsItsOwnPeck`.
Add: section 8.2 "Added" in `MatchKit.cs`, `MatchKits.cs`, `MatchPolicy.cs` (`SeatView`), `MatchSim.cs`; the section 8.3 counters in
`MatchCombat.cs`; the Enforcer row of the "Warrior kits" block in `MatchReport.cs` and the `warrior` group of `MatchWhatIf.Numbers`.
Tests (`MatchWarriorTests`, each on a hand-built seat layout with fixed positions):
- `Bite_MidCast_CutsTheCast_TakesFromThePile_RestStaysInThePile`: victim pecking a pile of 2, Enforcer's Shove lands: victim `Pecking` false,
  cargo unchanged, `NextPeckAt` unchanged; Enforcer +2; pile 0; `BitesFromCast` 1.
- `Bite_StunCutsFirst_TheSnapshotStillBites`: the Stun's own `Cut(Stun)` runs and the bite still comes from the pile (guards step 1 of 4.2).
- `Bite_JustLanded_TakesFromTheCargo_ClampedToRoom`: landed 0.5 s ago with food, Enforcer room 2: Enforcer +2, victim -2, `BiteLeftWithVictim` 1.
- `Bite_AfterTheWindow_OrAfterALandingWithNoFood_TakesNothing`: 1.1 s after a landing at window 1.0; a landing that took 0: no bite, hit debt only.
- `Bite_OncePerFreshState_TheSecondHitOnlyEarnsItsDebt`: Stun then Shove inside the window: one bite, count 3 (bite, Stun, Shove), `BitesSpent` 1.
- `Bite_EggRefuses_ShieldWalkLands`: egg: no bite, no debt; walk phase: bite and debts.
- `Steal_OnAFreshCarrier_BitesFirst_ThenTakesTheInflatedAmount`: count 2, fresh just-landed victim with 20 cargo, Enforcer empty: bite 3, count 3,
  Steal takes 5 + 2 x 3 = 11, count 0, `DebtBonusFood` 6, victim -14.
- `Steal_ThatTakesNothing_LeavesTheCount`: Enforcer full: take 0, count kept.
- `Debts_PerEnemy_CappedAndDecayAllAtOnce`: two rivals counted apart; the cap holds; a hit at the cap restarts the timer; `DebtSeconds` with no
  hit puts the count to 0 in one step (`DebtsDecayed` = the count).
- `OneStun_ThreeFreshVictims_InSeatOrder_ClampedToTheRoomLeft`: room 5, three fresh victims: bites 3, 2, 0 in seat order, three counts of 2.
- `TwoEnforcers_OneFreshVictim_TheFirstBites_BothEarnTheHitDebt`.
- `Executed_ClearsEveryCountOnAndOfTheDead`.
- `ShippedOff_PlaysExactlyAsBefore` (Enforcer half rewritten as in 8.4), `Sim_CallsTheSharedRules_NotCopies` (as in 8.4, including the "finds no
  `PeckBreakRules`" half), `EveryCapacityRead_GoesThroughCapacityOf` kept.
- `MatchReportTests.WarriorBlock_ShowsEveryCounterOfSection8` and `MatchWhatIfTests.Numbers_CarryTheWarriorGroup` updated to the 8.3 Enforcer row.
Commit: `refactor(balance): Enforcer bite and per-enemy debts replace the peck break and the debt room in the match simulator (BL-xxx)`.

**R3. Bot rules in P0 and P1.**
Files: `Balance/ScriptedPolicy.cs` (the P0 order of section 7), `Balance/UtilityPolicy.cs` (`BestHit` for the Enforcer), `MatchSim.BuildView`
(the 8.2 `SeatView` fields). Tests: `ScriptedPolicyTests.P0Enforcer_HitsAFreshRivalInReach_NotAStaleOne`, `P0Enforcer_CashesAtTheCap_AndBeforeTheDecay`,
`P0Enforcer_KeepsPeckingWhenNoFightBeatsThePeck`, `P0_CallsTheGamesOwnFunctions_NotCopiesOfThem` (lists `BotEnforcerRules`, no `BotBreakRules`);
`UtilityPolicyTests.P1Enforcer_PrefersAFreshRivalOverThePile_OnlyWhenThePeckIsSlower` (same layout at F0 and F1, the choice flips).
Commit: `feat(balance): Enforcer bot rules for fresh rivals and debt cashing in P0 and P1 (BL-xxx)`.

**R4. Run C and its results.** Write `Captures/oracle/whatif-warrior-enforcer.json` (section 9 run C; not committed), run it
(`-executeMethod CluckWars.EditorTools.OracleMenu.MatchWhatIfBatch -oracleWhatIf <file>`, only when no other Unity holds the project), and add the
results as a new section 15 of this spec in the shape of section 14.2 (one table, the 8.3 Enforcer columns plus the Bully's win rates). Commit (spec
only): `docs(spec): run C results for the reworked Enforcer (BL-xxx)`.

## 11. Game slices (after Maestro approves; not planned here)

- **Bully hit-steal:** the take through `ChickenCargo.StealFrom` / the steal RPC from the Stun and Shove landing sites (ARCH-DATA-05, ARCH-NET-07); the
  magnitude bound `StealRules.MaxSingleSteal` must cover the passive's hit amounts, since WingSlam and CluckShock declare no steal (ARCH-NET-05); the
  lock on the Bully's authority, per victim (`TargetCooldowns<NetworkBehaviourId>` fits, which the loot-mark spec section 10 plans to delete: keep it);
  a hit-steal event id (ARCH-EVT-02) for the pops; the "affected" test of a stun must use the same `IncomingExtends` the simulator uses. Kit change in
  `Bully.asset`.
- **Enforcer (rework):** the hit sites of Stun, Shove and Steal on the caster's authority read the victim's fresh state (cast running, or a
  `[Networked]` landing tick and a bitten flag on the victim, ARCH-NET-15) and send a bite claim: mid-cast to the victim's authority, which cuts the
  cast and lets the pile authority debit the bite (ARCH-NET-07; `ChickenCargo.MaxPeck` bounds it); just landed through the steal path
  (`StealRules.MaxSingleSteal` must cover the bite and the inflated Steal, `StealAmount + DebtSteal x DebtCap`, ARCH-NET-05). Per-enemy counts
  on the Enforcer's authority (a small `[Networked]` array of bytes with their timers), a bite and a cash event id (ARCH-EVT-02); no capacity
  change and no overflow transfer. `NetworkProtocol.Version` 2 to 3 (ARCH-NET-21).
- **Roster:** Enforcer registered in place of Relentless; `ChickenSubclass` value 1 renamed (or a new value with `ChickenSubclassFlags`,
  `ContractsAndEnumsTests`, `ValidateRoster`); Relentless stays as an unregistered asset.
- **Bots:** `BotController` calls `BotHitStealRules` and `BotEnforcerRules` (the drift guard of 8.4).
- **Text and feedback:** perk lines with numbers from the fields (ARCH-DATA-12), `UiKeys`/`UiText.csv`, section 6, `docs/FEEDBACK.md`, the site
  (`docs/site/index.html`), GDD; `librarian` for `docs/FILE_STRUCTURE.md` and the rules docs. `tools/balance/schema.py` `DENIED` unchanged (no new denied field).

## 12. Decisions taken without Maestro

Items 3, 7, 8, 9, 12 (the Enforcer half) and 14 concern the superseded break and capacity debts (section 4.6) and are kept as the record only.

1. Enforcer's slow ability: Slow (from the designer), as the brief said.
2. Debt cap 3; reset each round. Banking: kept (shipped) or cleared by `DebtsClearOnBank`, both measured (Maestro, 2026-11-25, replaces "kept through banking").
3. A break earns a debt even when no food fits; a Steal earns one only at 1 food or more (as written).
4. A stun that does not extend a longer stun, a shove that moves nobody, a take of 0: none counts as "affected", and none starts the lock.
5. Slow Area aura ticks never steal; `HitStealSlow` covers the instant Slow only.
6. Hit-steal food counts as stolen food in the report, not as Steal casts.
7. No retarget at release: the break acts only on the previewed rival.
8. A landed break ends Hide and cancels a Shield egg like any move; it spends no ability slot.
9. A Enforcer is not robbed of a debt by a break against him (a break takes from the pile, not from him).
10. Overflow never raises the centre pile's maximum.
11. Simulator access to the Enforcer through a what-if roster swap over an unregistered asset (section 5), not a game or `BalanceBook` change.
12. Bot: the Bully casts for food at 1 or more, Shove first on a tie; the Enforcer breaks at a pile only while a debt is left, and banks at
    12 plus its debt room. The shipped Enforcer values (reach 0, room 0) make the run-B baseline "Relentless's kit with no passive".
13. Go measures of section 9 (20 to 30 % per Warrior kit, worst 5 s window at most 6).
14. **Maestro, 2026-11-25 (closes open question 1):** `BreakCooldown` gates breaks only. After a break the Enforcer's next pile Peck is ready on his
    normal Peck cycle (`NextPeckAt` is not touched by a landed break); only another break waits `BreakCooldown`. A break must not be a sacrifice of his own
    foraging. A break press that falls back to a pile Peck is an ordinary Peck and starts the ordinary cycle.

15. **Maestro, 2026-11-25, evening (supersedes C3 and the bite food of C5):** a hit on a fresh rival takes no food, only the extra stack; the old bite is the
    data `FreshBiteShare` (shipped 0). Taken without him: the field is a share of the victim's `PeckAmount` (0 to 1, so 1 is exactly the old reading, no new
    amount to invent); the extra stack goes to a Steal's hit too (any hit on a fresh rival), once per fresh state; a Steal's hit never cuts a cast.

**Rework (2026-11-25, afternoon), taken where Maestro left a gap; each can be overruled:**

- C1. Kit Peck, WingSlam (Stun), CluckShock (Shove), Snatch (Steal), the three of his example; legal under `FixedKit.Problems`. Speed and Slow leave.
- C2. "A hit" is his Stun, Shove or Steal affecting the rival, with the Bully's "affected" test; for the Steal, reaching a carrier (Snatch's own
  target filter). Peck is not a hit. So a Steal on an **empty** fresh rival does nothing, not even the bite; Stun and Shove bite him.
- C3. The bite is the victim's `PeckAmount`: from the pile when mid-cast (the cast is cut, the rest stays in the pile), from the cargo when just
  landed (the rest stays with the victim). One bite per fresh state, whoever bites; a bite that fits 0 food still spends the state, cuts the cast
  and earns its debt.
- C4. "Just landed" opens only on a landing that took food; a Peck cut by someone else opens nothing.
- C5. Freshness is read before the hit's own effect, so a Stun that cuts the cast still bites from the pile.
- C6. Debt count: +1 per hit that affected that rival (Stun, Shove; the Steal that consumes them adds none), +1 per bite; the bite of a Steal
  counts toward that same Steal. Steal takes `StealAmount x StealFactor + DebtSteal x count` (the bonus is not multiplied), then the count is 0
  if the take was above 0. Cap `DebtCap`; a hit at the cap restarts the timer; the count decays to 0 at once after `DebtSeconds` without a
  +1; all counts clear when the Enforcer is executed, and the counts on a victim clear when the victim is executed. Starting values:
  `FreshSeconds` 1.0, `DebtSteal` 2, `DebtCap` 3, `DebtSeconds` 6.
- C7. Being robbed no longer costs a Enforcer a debt (the "lose one" rule leaves with the room).
- C8. Multi-victim casts resolve victim by victim in the seat loop order already used, each take clamped to the room left.
- C9. The foraging lever is class-wide (`Warrior.asset`, a plain delta, no code), so it also moves the Bully and the bite taken from a Warrior.
- C10. The fresh glint and the debt pips are seen by the Enforcers and by the chicken concerned, not by the other players.
- C11. The Bully and the Enforcer both exist; the Bully's hit-steal rule is unchanged by this amendment.

## 13. Open questions (real forks)

1. ~~Is a break meant to cost food?~~ **Closed by Maestro, 2026-11-25:** `BreakCooldown` gates breaks only; after a break his next pile Peck runs on the
   normal Peck cycle (sections 4.1, 4.3, 9, 12 item 14).
2. ~~Online timing of the break.~~ **Superseded** with the break. Its successor: the fresh window online. The window is the cast plus about 1 s, so a
   hit's round trip fits it on the relay, but a hit landing in the last tens of milliseconds will be judged after the state ended. Should the victim's
   authority judge freshness at the hit's sender tick (a lag allowance, as `ChickenCombat.SenderLatency` gives other claims)? Proposed: yes.
3. **The combo against the forgiving limit.** One full combo takes 14 food from one victim in about 2 s (section 4.5), 24 at the ceiling of a 5 s
   window, against the go limit of 6 for the mean. Is a rare 14 acceptable if the mean stays under 6, or should `DebtSteal` or `DebtCap` be held
   so a combo never exceeds a number Maestro names (with `DebtSteal` 1: 11)?
4. **Whose foraging goes down.** The lever is written class-wide (C9), which also slows the Bully and makes Warrior victims give a smaller bite. If
   only the Enforcer should forage less, the lever moves to the passive (a Peck cooldown factor on `EnforcerPassiveSO`, one new field and one hook);
   say which.

## 14. Results of the simulator runs (2026-11-25)

Both runs: `replicateScale` 0.4, seed `0x1352902`, 12 threads, `bal=5465b825`, with Maestro's Thief values of 2026-11-25 (bounty 8, steal x1.6, capacity 16) in every baseline.
Result files (not committed, `Captures/oracle/`): run A `match-whatif-20261125-114515.md` / `.json` from `whatif-warrior-bully.json` (12 scenarios, 118 560 matches, 5.4 min);
run B `match-whatif-20261125-115253.md` / `.json` from `whatif-warrior-debt.json` (15 scenarios, 145 920 matches, 6.7 min). Each file holds every kit's win rate for P0, P1 and
mixed, the counters below, and the per-kit movement tables. The baseline of run A is the shipped game (Relentless and the old Bully, Snatch kit); the baseline of run B seats the
Enforcer with its shipped numbers (reach 0, room 0) in Relentless's seat.

**Go measures** (section 9) read off these tables: no scenario of either run puts its Warrior kit at 20 to 30 % under all three populations.

### 14.1 Run A, the Bully (kit Peck, Shadowstep, WingSlam, CluckShock)

Win rates, P0 / P1 / mixed: the shipped Bully 23.2 / 33.2 / 27.5; **the new kit with every amount 0 (B0) 5.2 / 58.3 / 32.9**; Stun 2, Shove 2, lock 3 (the anchor) 40.4 / 43.2 / 38.1.
The kit alone is the problem for P1 (58 %): Stun, Shove and Jump at x0.75 cooldowns cut pecks and P1 farms; P0 does not know what to do with the kit (5 %). The hit-steal moves P0 up
to 34 to 47 % and P1 down to 41 to 47 %, so the two populations agree (within 10 points) at every amount, but at a level above the band. The smallest cell (1, 1) still gives
34.3 / 47.3 / 34.8. The lock hardly matters (anchor at lock 2 / 3 / 4: 39.6 / 40.4 / 38.3 in P0). Other kits move: Speedy_Slippery and Speedy_Featherfoot lose 4 to 7 points and Fatty_Bulwark
up to 6 in P0, so the 5-point rule also fails. Worst 5 s window for a victim: mean 2.05, 95th percentile 4 at the anchor (inside the 6 limit; 3.16 and 6 at 3 / 3).

## Overview: the Bully (Warrior_Bully), per match in the mixed population unless said

| Scenario | P0 | P1 | mixed | hit takes per match | hit food per match | hits that met the lock per match | identity food per match | worst loss for a victim in 5 s (mean) | worst loss for a victim in 5 s (95th percentile) |
|---|---|---|---|---|---|---|---|---|---|
| baseline | 23.2 | 33.2 | 27.5 | n/a | n/a | n/a | 0.00 | 6.37 | 10.00 |
| B0 kit only (amounts 0) | 5.2 (-18.0*) | 58.3 (+25.1*) | 32.9 (+5.4*) | n/a | n/a | n/a | 0.00 | 0.00 | 0.00 |
| stun 1 shove 1 lock 3 | 34.3 (+11.1*) | 47.3 (+14.0*) | 34.8 (+7.3*) | 2.02 | 2.01 | 0.68 | 2.01 | 1.03 | 2.00 |
| stun 1 shove 2 lock 3 | 38.1 (+14.9*) | 43.3 (+10.1*) | 38.2 (+10.7*) | 2.07 | 3.49 | 0.71 | 3.49 | 1.89 | 4.00 |
| stun 1 shove 3 lock 3 | 42.9 (+19.6*) | 41.2 (+8.0*) | 36.9 (+9.4*) | 2.32 | 5.53 | 0.78 | 5.53 | 2.89 | 6.00 |
| stun 2 shove 1 lock 3 | 37.4 (+14.2*) | 41.2 (+8.0*) | 36.3 (+8.8*) | 2.09 | 3.35 | 0.82 | 3.35 | 1.73 | 3.00 |
| stun 2 shove 2 lock 3 | 40.4 (+17.1*) | 43.2 (+10.0*) | 38.1 (+10.6*) | 2.12 | 4.14 | 0.74 | 4.14 | 2.05 | 4.00 |
| stun 2 shove 3 lock 3 | 44.6 (+21.4*) | 43.3 (+10.1*) | 40.1 (+12.6*) | 2.28 | 6.07 | 0.79 | 6.07 | 3.03 | 6.00 |
| stun 3 shove 1 lock 3 | 41.4 (+18.2*) | 42.1 (+8.9*) | 37.6 (+10.1*) | 2.16 | 4.91 | 0.85 | 4.91 | 2.52 | 4.00 |
| stun 3 shove 2 lock 3 | 45.1 (+21.9*) | 42.3 (+9.0*) | 38.3 (+10.8*) | 2.16 | 5.61 | 0.86 | 5.61 | 2.77 | 5.00 |
| stun 3 shove 3 lock 3 | 46.5 (+23.3*) | 44.6 (+11.4*) | 40.6 (+13.1*) | 2.29 | 6.67 | 0.76 | 6.67 | 3.16 | 6.00 |
| stun 2 shove 2 lock 2 | 39.6 (+16.4*) | 41.0 (+7.7*) | 36.7 (+9.2*) | 2.25 | 4.39 | 0.67 | 4.39 | 2.25 | 4.00 |
| stun 2 shove 2 lock 4 | 38.3 (+15.1*) | 44.8 (+11.5*) | 39.0 (+11.5*) | 1.99 | 3.88 | 0.81 | 3.88 | 1.81 | 4.00 |

### 14.2 Run B, the Enforcer (kit Peck, Speed, Steal, Slow)

Win rates, P0 / P1 / mixed: baseline (no break, room 0) 10.6 / 25.1 / 22.0; every cell of the reach x cooldown grid 9.8 / 23.9 to 25.0 / 19.5 to 20.0. **The break barely happens:** 0.24 to 0.33
a match under the mixed population, and apparently none in P0 (its win rate is identical in every grid cell, so the reach and cooldown never touched a match). The cast lasts 0.4 s and the bot needs a think plus its 0.3 s reaction, so
a candidate exists for about the first 0.1 s of a cast (`BotBreakRules.Candidate`: cast left above the reaction); the reach and cooldown axes change nothing measurable. The room
is barely used either: the bot banks at 12 plus its debt room (at most 18 with room 2 and 3 debts), under the class capacity of 20, so only a bite or a steal that overshoots uses room
(8 to 11 % of it). The debt lifetime reading changes the held average, not the win rate: **streak** (cleared on bank) holds 0.39 debts on average and 0.69 at the end of a match
(room 2) against 0.56 and 1.19 kept; room 3 is the same. **Slow growth** (room 1) holds 0.56 and 1.25 at cap 4, the same at cap 6 (nobody gets past 3). Win rates: streak room 2 9.4 / 24.6 / 20.2,
streak room 3 8.8 / 25.4 / 20.0, slow growth room 1 cap 4 12.5 / 24.8 / 21.5, cap 6 12.3 / 24.8 / 21.5, original (room 2, cap 3, kept) 9.8 / 24.4 / 20.0. No other kit moves more than about 2 points in any
Enforcer scenario. Worst 5 s window for a victim: mean 6.1 to 6.2, 95th percentile 10, against 5.79 and 10 without the break: the Steal in the kit sets it, not the break.

## Overview: the Enforcer (Warrior_Enforcer), per match in the mixed population unless said

| Scenario | P0 | P1 | mixed | breaks per match | break food per match | break presses that fell back to a Peck per match | break presses cancelled per match | debts earned per match | debts held (mean over the match) | debts at the end of the match (mean) | share of the debt room used when banking (%) | overflow to the centre pile per match | identity food per match | worst loss for a victim in 5 s (mean) | worst loss for a victim in 5 s (95th percentile) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| baseline | 10.6 | 25.1 | 22.0 | 0.00 | 0.00 | 0.00 | 0.00 | 2.30 | 0.50 | 1.05 | n/a | 0.00 | 0.00 | 5.79 | 10.00 |
| kept reach 2.0 cooldown 1.5 room 2 cap 3 | 9.8 (-0.8) | 23.9 (-1.2) | 19.9 (-2.1) | 0.27 | 0.79 | 0.01 | 0.00 | 2.54 | 0.55 | 1.17 | 7.98 | 0.00 | 0.79 | 6.17 | 10.00 |
| kept reach 2.0 cooldown 2.5 room 2 cap 3 | 9.8 (-0.8) | 23.9 (-1.2) | 19.9 (-2.1) | 0.27 | 0.79 | 0.01 | 0.00 | 2.54 | 0.55 | 1.17 | 7.98 | 0.00 | 0.79 | 6.17 | 10.00 |
| kept reach 2.0 cooldown 3.5 room 2 cap 3 | 9.8 (-0.8) | 23.9 (-1.2) | 19.5 (-2.5) | 0.24 | 0.71 | 0.01 | 0.00 | 2.54 | 0.54 | 1.15 | 8.15 | 0.00 | 0.71 | 6.12 | 10.00 |
| kept reach 2.5 cooldown 1.5 room 2 cap 3 | 9.8 (-0.8) | 24.4 (-0.7) | 20.0 (-2.0) | 0.30 | 0.87 | 0.01 | 0.01 | 2.56 | 0.56 | 1.19 | 8.00 | 0.00 | 0.87 | 6.18 | 10.00 |
| kept reach 2.5 cooldown 2.5 room 2 cap 3 | 9.8 (-0.8) | 24.4 (-0.7) | 20.0 (-2.0) | 0.30 | 0.87 | 0.01 | 0.01 | 2.56 | 0.56 | 1.19 | 8.00 | 0.00 | 0.87 | 6.18 | 10.00 |
| kept reach 2.5 cooldown 3.5 room 2 cap 3 | 9.8 (-0.8) | 24.4 (-0.7) | 19.6 (-2.4) | 0.27 | 0.78 | 0.01 | 0.01 | 2.56 | 0.55 | 1.17 | 8.16 | 0.00 | 0.78 | 6.13 | 10.00 |
| kept reach 3.0 cooldown 1.5 room 2 cap 3 | 9.8 (-0.8) | 25.0 (-0.1) | 20.0 (-2.0) | 0.33 | 0.96 | 0.01 | 0.01 | 2.58 | 0.56 | 1.20 | 8.72 | 0.00 | 0.96 | 6.24 | 10.00 |
| kept reach 3.0 cooldown 2.5 room 2 cap 3 | 9.8 (-0.8) | 25.0 (-0.1) | 20.0 (-2.0) | 0.33 | 0.95 | 0.01 | 0.01 | 2.58 | 0.56 | 1.20 | 8.72 | 0.00 | 0.95 | 6.24 | 10.00 |
| kept reach 3.0 cooldown 3.5 room 2 cap 3 | 9.8 (-0.8) | 25.0 (-0.1) | 19.6 (-2.4) | 0.29 | 0.86 | 0.01 | 0.01 | 2.58 | 0.55 | 1.18 | 8.89 | 0.00 | 0.86 | 6.19 | 10.00 |
| streak room 2 cap 3 | 9.4 (-1.2) | 24.6 (-0.5) | 20.2 (-1.8) | 0.31 | 0.92 | 0.01 | 0.01 | 2.61 | 0.39 | 0.69 | 9.87 | 0.00 | 0.92 | 6.18 | 10.00 |
| streak room 3 cap 3 | 8.8 (-1.8) | 25.4 (+0.2) | 20.0 (-2.0) | 0.30 | 0.88 | 0.01 | 0.01 | 2.60 | 0.40 | 0.68 | 11.26 | 0.00 | 0.88 | 6.22 | 10.00 |
| slow growth room 1 cap 4 | 12.5 (+1.9) | 24.8 (-0.4) | 21.5 (-0.5) | 0.30 | 0.87 | 0.01 | 0.01 | 2.59 | 0.56 | 1.25 | 9.02 | 0.00 | 0.87 | 6.19 | 10.00 |
| slow growth room 1 cap 6 | 12.3 (+1.7) | 24.8 (-0.4) | 21.5 (-0.5) | 0.30 | 0.87 | 0.01 | 0.01 | 2.60 | 0.56 | 1.26 | 9.13 | 0.00 | 0.87 | 6.19 | 10.00 |
| break only room 0 | 11.5 (+1.0) | 25.8 (+0.7) | 22.1 (+0.1) | 0.25 | 0.74 | 0.01 | 0.01 | 2.44 | 0.55 | 1.18 | n/a | 0.00 | 0.74 | 6.07 | 10.00 |
| steals only reach 0 room 2 cap 3 | 8.7 (-1.9) | 23.7 (-1.4) | 19.6 (-2.4) | 0.00 | 0.00 | 0.00 | 0.00 | 2.36 | 0.50 | 1.04 | 7.81 | 0.00 | 0.00 | 5.81 | 10.00 |

### 14.3 What this says (for Maestro)

- Bully: no value of the grid fits the band. The hit-steal brings P0 and P1 together but at 34 to 47 %, and the kit without it is already 58 % in P1. A lower take cannot be tried through
  the bot's own rule (it casts for food at 1 or more); the lever is probably elsewhere (the cooldown multiplier, the Jump or the Shove's reach).
- Enforcer: as built (the bot rules of section 7, the 0.4 s cast) the break is a rare event and the kit plays as Relentless's, 10 / 24 / 20. To make the break matter, the window has to
  open (a longer cast for the victim, a reaction-free press online as in open question 2, or the bot pressing on the approach), and the bot has to bank later to fill the room.

## 15. Results of the reworked runs (2026-11-25, evening; BL-222)

Slice R4 as run after the bot fixes. Both runs: `replicateScale` 0.4, seed 20261122, 12 threads, the shipped game as baseline re-run after BL-224 (a Brawler with no Steal and no hit that pays forages) and BL-225 (P1 values a cast by its take), so the baselines are **not** comparable with section 14: P1 moved most (shipped Bully 33 to 44, Relentless 26 to 32). Files (not committed): `Captures/oracle/match-whatif-20261125-135008` (Bully, `whatif-e-bully.json`), `-140227` (Bully, stun 1 / shove 1 at the best multiplier), `-140450` (Bully kit only, hit-steal 0), `-140017` (Enforcer, `whatif-e-enforcer.json`, `"roster":{"Relentless":"Enforcer"}`), each `.md` and `.json`. Foraging is the new per-kit lever `PeckCooldownMultiplier` (BL-223), not the class-wide `Warrior` Peck values. Win rates are points over the seats a kit sat in; the Enforcer rows use `FreshBiteShare` 0 except the two B rows.

### 15.1 Run A, the Bully (kit Peck, Shadowstep, WingSlam, CluckShock)

Hit-steal Stun 2, Shove 2, lock 3 unless the row says otherwise; the baseline is the shipped game (old Bully with Snatch). Worst loss is for one victim inside 5 s.

| Scenario | P0 | P1 | mixed | hit takes per match | hit food per match | mean hit-steal take | hits that met the lock per match | identity food per match | worst loss for a victim in 5 s (mean) | worst loss for a victim in 5 s (95th percentile) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| baseline | 23.2 | 43.7 | 32.9 | n/a | n/a | n/a | n/a | 0.00 | 5.79 | 10.00 |
| Bully stun 2 shove 2 lock 3, Peck x1.0 | 40.4 | 50.6 | 39.0 | 1.90 | 3.72 | 1.96 | 0.66 | 3.72 | 1.91 | 4.00 |
| Bully stun 2 shove 2 lock 3, Peck x1.2 | 33.0 | 41.2 | 30.8 | 2.07 | 4.05 | 1.96 | 0.72 | 4.05 | 2.04 | 4.00 |
| Bully stun 2 shove 2 lock 3, Peck x1.3 | 28.1 | 35.7 | 28.5 | 2.18 | 4.27 | 1.96 | 0.76 | 4.27 | 2.19 | 4.00 |
| Bully stun 2 shove 2 lock 3, Peck x1.4 | 26.9 | 27.1 | 22.4 | 2.18 | 4.26 | 1.96 | 0.73 | 4.26 | 2.19 | 4.00 |
| Bully stun 2 shove 2 lock 3, Peck x1.5 | 22.0 | 19.0 | 18.7 | 2.23 | 4.37 | 1.96 | 0.76 | 4.37 | 2.22 | 4.00 |
| Bully stun 1 shove 1 lock 3, Peck x1.4 | 20.1 | 25.1 | 19.9 | 2.12 | 2.11 | 0.99 | 0.69 | 2.11 | 1.09 | 2.00 |
| Bully kit only (amounts 0), Peck x1.0 | 46.4 | 56.8 | 41.0 | 0 | 0 | n/a | 0 | 0 | 0 | 0 |
| Bully kit only (amounts 0), Peck x1.4 | 27.5 | 32.9 | 26.9 | 0 | 0 | n/a | 0 | 0 | 0 | 0 |

### 15.2 Run C, the reworked Enforcer (kit Peck, WingSlam, CluckShock, Snatch)

K kit only (fresh 0, debt steal 0), H hits only (debt steal 2, fresh 0), A anchor (fresh 1.0, debt steal 2, cap 3, 6 s), T1 and T3 (debt steal 1 and 3), B the anchor with `FreshBiteShare` 1 (the old reading, the victim's whole `PeckAmount` as food). The baseline is the Enforcer as shipped at multiplier 1.0, which is the K cell at 1.0. "Fresh hits" are the stacks gained from fresh rivals; bite food is 0 wherever the share is 0; identity food is the debts' bonus food plus bite food.

| Scenario | P0 | P1 | mixed | fresh hits per match | bites per match | bite food per match | Pecks bitten per match | debts earned per match | debts consumed per cash | Steals per match | mean Steal size | identity food per match | worst loss for a victim in 5 s (mean) | worst loss for a victim in 5 s (95th percentile) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| baseline | 8.7 | 31.9 | 25.5 | 0.00 | 0.00 | 0.00 | 0.00 | 3.88 | 1.41 | 1.88 | 4.51 | 0.00 | 4.83 | 10.00 |
| K kit only, Peck x1.25 | 8.1 | 14.2 | 12.1 | 0.00 | 0.00 | 0.00 | 0.00 | 4.11 | 1.41 | 2.05 | 4.48 | 0.00 | 5.12 | 10.00 |
| K kit only, Peck x1.5 | 6.1 | 3.8 | 5.6 | 0.00 | 0.00 | 0.00 | 0.00 | 4.08 | 1.37 | 2.14 | 4.45 | 0.00 | 5.01 | 10.00 |
| H hits only (debt steal 2, no fresh), Peck x1.0 | 28.0 | 33.5 | 29.9 | 0.00 | 0.00 | 0.00 | 0.00 | 3.45 | 1.38 | 1.70 | 5.51 | 1.58 | 5.82 | 14.00 |
| H hits only (debt steal 2, no fresh), Peck x1.25 | 18.9 | 17.3 | 16.4 | 0.00 | 0.00 | 0.00 | 0.00 | 3.71 | 1.39 | 1.86 | 5.58 | 1.81 | 6.29 | 14.00 |
| H hits only (debt steal 2, no fresh), Peck x1.5 | 17.9 | 5.5 | 10.8 | 0.00 | 0.00 | 0.00 | 0.00 | 3.76 | 1.34 | 1.98 | 5.56 | 2.03 | 6.38 | 14.00 |
| A anchor (fresh 1.0, debt steal 2), Peck x1.0 | 33.6 | 35.6 | 31.2 | 0.71 | 0.00 | 0.00 | 0.00 | 4.16 | 1.62 | 1.69 | 5.95 | 2.22 | 6.24 | 14.00 |
| A anchor (fresh 1.0, debt steal 2), Peck x1.25 | 22.0 | 17.5 | 18.9 | 0.82 | 0.00 | 0.00 | 0.00 | 4.50 | 1.66 | 1.85 | 5.96 | 2.48 | 6.57 | 14.00 |
| A anchor (fresh 1.0, debt steal 2), Peck x1.5 | 19.9 | 5.2 | 12.5 | 0.88 | 0.00 | 0.00 | 0.00 | 4.64 | 1.63 | 1.95 | 5.89 | 2.64 | 6.60 | 14.00 |
| T1 (debt steal 1), Peck x1.0 | 28.7 | 34.8 | 30.4 | 0.70 | 0.00 | 0.00 | 0.00 | 4.22 | 1.61 | 1.71 | 5.31 | 1.20 | 5.48 | 13.00 |
| T1 (debt steal 1), Peck x1.5 | 14.2 | 6.0 | 8.7 | 0.87 | 0.00 | 0.00 | 0.00 | 4.72 | 1.62 | 2.00 | 5.30 | 1.51 | 5.88 | 13.00 |
| T3 (debt steal 3), Peck x1.0 | 36.8 | 37.1 | 33.9 | 0.70 | 0.00 | 0.00 | 0.00 | 4.08 | 1.63 | 1.69 | 6.39 | 2.98 | 6.83 | 16.00 |
| T3 (debt steal 3), Peck x1.5 | 23.0 | 7.4 | 13.9 | 0.87 | 0.00 | 0.00 | 0.00 | 4.60 | 1.63 | 1.96 | 6.42 | 3.67 | 7.29 | 16.00 |
| B bite on (share 1, old reading), Peck x1.0 | 36.0 | 39.9 | 33.6 | 0.75 | 0.75 | 2.17 | 0.00 | 4.18 | 1.61 | 1.59 | 5.87 | 4.17 | 6.65 | 14.00 |
| B bite on (share 1, old reading), Peck x1.5 | 24.2 | 8.7 | 16.2 | 0.94 | 0.94 | 2.73 | 0.00 | 4.73 | 1.59 | 1.79 | 5.76 | 4.92 | 6.81 | 14.00 |

### 15.3 What this says (for Maestro)

- **The P0 defect is closed.** A Brawler with no Steal and no hit that pays now forages: the Bully kit with the hit-steal at 0 plays 46 / 57 / 41 (P0 / P1 / mixed) at multiplier 1.0 instead of 1 to 7 %, and 27.5 / 32.9 / 26.9 at 1.4. The hit-steal on top of it adds nothing under P0 and costs about 6 points under P1 and 4 under mixed at 1.4 (26.9 / 27.1 / 22.4): at 2 food a hit it is a flavour, not a win-rate engine.
- **Bully: the best foraging lever is 1.4** (P0 26.9, P1 27.1, mixed 22.4: all three in the 20 to 30 band and within 0.2 points of each other; clock share under 3 %, worst 5 s loss 2.2 mean, 4 at the 95th percentile). 1.3 is a little high (28.1 / 35.7 / 28.5), 1.5 a little low (22.0 / 19.0 / 18.7). Stun 1 / Shove 1 at 1.4 is too weak (20.1 / 25.1 / 19.9; mean take 0.99); amounts 2 stay.
- **Enforcer, fresh hits and debts: 1.0 is too strong, 1.25 too weak, so about 1.1.** At 1.0 the anchor is 33.6 / 35.6 / 31.2, T1 28.7 / 34.8 / 30.4 (worst 5 s mean 5.5, under the limit of 6) and H 28.0 / 33.5 / 29.9; at 1.25 the anchor falls to 22.0 / 17.5 / 18.9 and at 1.5 to 19.9 / 5.2 / 12.5. P1 collapses with a slower Peck because it never swaps the pile for a fight (kit only at 1.5: 3.8), P0 only halves, so the P0 / P1 gap re-opens at 1.5 (15 points at the anchor) and is within 5 at 1.0 and 1.25. T1 at a multiplier near 1.1 is the pick (debt steal 1: worst loss 5.5 mean, 13 at the 95th percentile).
- **The debts are the whole identity; the fresh stack is thin.** Hits on fresh rivals are only 0.7 to 0.9 a match (a 0.4 s cast and a 1 s window are rarely met by a bot), and the anchor with the stack on is only 1 to 6 points above H without it. The go measure "at least 2 bites a match" fails with or without food (the B rows, share 1, bite 0.75 a match, 2.2 to 2.7 food); identity food is 2.2 per match at the anchor against about 25 banked, short of a fifth.
- **The old bite is worth a few points, not a different kit.** B against A: +2.4 / +4.3 / +2.4 at 1.0, +4.3 / +3.5 / +3.7 at 1.5 (P0 / P1 / mixed), and 2 more food a match of identity; with the food off the Enforcer keeps most of it.
- **Gap against the other kits.** The shipped Steal kits keep a wide P0 / P1 gap (Relentless 10.2 / 32.3, the Enforcer kit only 8.7 / 31.9): P0, the game's bot, plays a plain Steal kit badly, and BL-225 lifted P1 for every Steal holder. Where P0 plays a rule that fights (H, A, T1, T3) the Enforcer's gap is 5 to 6 points. That is a bot question, not a number to tune.
- **Neighbours.** At multipliers up to 1.25 no non-Warrior kit moves more than about 4 points; at 1.5 Slippery, Featherfoot and Fatty_Bulwark gain 5 to 6 in P1 because a slow Enforcer leaves them the piles.

## 16. Game build (planned 2026-11-25, after Maestro's go)

Maestro, 2026-11-25: both kits go into the game now ("Haz todo, da igual el orden"); Relentless leaves the roster and stays on the shelf ("vamos a
guardar al Relentless como idea posible futura"). This section is the build plan; it supersedes section 11 and, for the shipped game, the "simulator
only, not registered" lines of sections 4.4 and 5 and of the `EnforcerPassiveSO` and `Enforcer.asset` comments. Read on `develop` at `f53cec54`.

### 16.1 What ships

Measured in `Captures/oracle/match-whatif-20261125-145655.md` (Bully) and `-145019.md` (Enforcer, roster swap), seed `0x1352902`, the shipped game
as baseline; win rates P0 / P1 / mixed.

| Kit | Kit slots | Passive values | Measured |
|---|---|---|---|
| Bully (`Bully.asset`) | Peck, Shadowstep (Jump), WingSlam (Stun), CluckShock (Shove); no Steal | `CooldownMultiplier` 0.75 (kept), `HitStealStun` 2, `HitStealShove` 2, `HitStealSlow` 0, `HitStealLockSeconds` 3, `PeckCooldownMultiplier` 1.4 | 26.9 / 27.1 / 22.4; worst 5 s loss for a victim 2.19 mean, 4 at p95 |
| Enforcer (`Enforcer.asset`) | Peck, WingSlam (Stun), CluckShock (Shove), Snatch (Steal) | `FreshSeconds` 1.5, `FreshBiteShare` 0, `DebtSteal` 2, `DebtCap` 3, `DebtSeconds` 6, `PeckCooldownMultiplier` 1.2 | 23.5 / 22.5 / 21.2; 0.90 fresh hits a match; worst 5 s loss 6.73 mean, 14 at p95 (above the go limit of 6, open question 16.9-5) |
| Relentless (`Relentless.asset`) | unchanged | unchanged | leaves the registry; asset and `RelentlessPassiveSO` stay (ARCH-DATA-13, CODE-DESIGN-18), `Retired` true |

The values go into the `.asset` files in a build (ARCH-DATA-11), not as a balance delta: a kit change and a new registered passive need a build anyway
(`Subclass`, the registry and the deny list, CLAUDE.md "What a delta cannot change"). The food bite (`FreshBiteShare` above 0) is **not** built in the
game: Maestro ruled the fresh hit takes no food (section 1), the mid-cast half has no authority-confirmed path (the Enforcer is not in the victim's pile
reach, so `RPC_SettlePeck` would refuse him), and a delta must not switch on a rule the game does not play, so `FreshBiteShare` joins the deny list
(slice G7; open question 16.9-4).

### 16.2 Authority and networked state (decisions)

The game runs Shared Mode: each chicken's state authority is its owner (bots: the master). The simulator decides "affected" (section 3, section 4.2)
inside one tick; in the game only the **victim's** authority applies a stun or a shove (`ChickenController.RPC_ApplyStun`,
`RPC_ApplyKnockbackFrom` then `ApplyKnockback`). Rather than adding a receipt round trip, the **caster predicts** "affected" from the victim's
replicated state with the same primitives the victim's RPC uses, and every food transfer stays authority-confirmed through the existing steal path.

| State | Lives on | Written by | Read by | Rules |
|---|---|---|---|---|
| "Affected" verdict of one Stun / Shove | nowhere (computed per hit) | the caster's authority, `HitAffectRules` over the victim's replicated `StunRemaining`, `IsControlImmune`, its passive's `ModifyControlDuration` / `ModifyKnockback`, `PlayerBase.WallBetween` | `ChickenHitSteal`, `EnforcerDebts` | ARCH-NET-10, ARCH-DATA-05 |
| Bully lock per victim | `ChickenHitSteal` on the Bully, authority-local (`HitStealLedger<NetworkBehaviourId>`, plain C#, like `ChickenController._stealLocks`) | the Bully's authority: locked at the ask (take above 0), released if the settle credits 0 | the Bully's bot, the Bully's lock ring (the local human is the authority) | ARCH-NET-02 |
| Hit-steal food | the victim's `ChickenCargo.Cargo` | the victim's authority drains (`RPC_DrainStolen`), the Bully's authority credits on `RPC_SettleSteal` | everyone | ARCH-NET-07, ARCH-NET-05, CODE-DESIGN-10 |
| Pecker's fresh state: `FedTick`, `FedStateId`, `FreshStateId`, `BittenStateId` (`[Networked]` on `AbilityController`) | every chicken | its own authority: `FreshStateId++` at a Peck cast start (`PeckAbilitySO.OnActivate`) and at a fed settle; `FedTick`/`FedStateId` in `ChickenCargo.RPC_SettlePeck` when `ToCargo > 0`; `BittenStateId` by `RPC_MarkBitten` | every peer through `BiteRules` | ARCH-NET-02, ARCH-NET-03, ARCH-NET-15 (see note) |
| Mid-cast | already replicated (`AbilityController.ActiveMask` + `ActiveTimer*` of the Peck slot) | the pecker's authority | new reads `IsPeckCasting`, `PeckCastLeft` | none new |
| Debts per rival (count and decay timer) | `EnforcerDebts` on the Enforcer: `[Networked, Capacity(4)]` `NetworkArray<NetworkBehaviourId> DebtVictims`, `NetworkArray<byte> DebtCounts`, `NetworkArray<TickTimer> DebtTimers` | the Enforcer's authority | the Enforcer (HUD, pips, bot) and each victim ("you owe" pips) | ARCH-NET-02, ARCH-NET-15 (`EffectWindow`), ARCH-NET-13, CODE-DESIGN-19 by analogy |
| Inflated Steal | the victim's cargo | Snatch asks `DebtRules.StealAmount(...)` through `ChickenCargo.StealFrom`; the victim drains; the Enforcer credits and cashes on the settle | everyone | ARCH-NET-07, ARCH-NET-05 |
| One-shot moments: hit-steal take, fresh hit, debt gained, debt cashed | `[Networked] byte` event ids on `ChickenHitSteal` / `EnforcerDebts` with their payload fields written first | the hitter's authority | `ChickenLocalEvents` (one observer per chicken) | ARCH-EVT-02, ARCH-EVT-03, ARCH-EVT-04 |

Decisions in the table, each overrulable:

- **Freshness is judged on the hitter's view.** The Enforcer's authority reads the victim's replicated state at the hit, through
  `BiteRules.Fresh`. What he sees is what counts; since a fresh hit moves no food, no victim-side check or lag allowance is needed (this closes open
  question 2 of section 13 with "no allowance"). The read happens before the victim's own cut, which lands one relay hop later, so the order of
  section 4.2 step 1 holds by itself.
- **The bitten flag is the victim's.** `AbilityController.RPC_MarkBitten(byte stateId, NetworkBehaviourId hitterId, RpcInfo info)` sets
  `BittenStateId = stateId` when `stateId == FreshStateId`; it is accepted only when `RpcClaim.SenderSpeaksFor(hitter)` and the hitter's passive
  has `Debt.IsEnforcer`, else one rate-limited Warn with the prefix `rejected RPC_MarkBitten` (ARCH-NET-03, ARCH-NET-04, ARCH-ERR-06). Two Enforcers
  hitting one fresh rival inside one round trip can both stack (16.9-7).
- **ARCH-NET-15 note.** The fresh state is a tick stamp plus state ids, not a `TickTimer`, because the window length is the hitter's
  `FreshSeconds`, not the pecker's. `librarian` records this as a named exception under ARCH-NET-15. The debt timers are windows on the Enforcer
  and go through `EffectWindow`.
- **"Landed with food" is the settle, not the cast end.** `PeckLandedEventId` bumps when the cast ends naturally, before the pile has answered;
  the fresh "just landed" state opens only in `RPC_SettlePeck` when `settled.ToCargo > 0` (section 4.1: a landing that took 0 opens none).
- **Debts cash by what the Steal consumed.** Snatch records the count it used at the ask; a settle crediting above 0 subtracts that number
  (`max(0, count - consumed)`); a settle of 0 leaves the count. Equal to the simulator's "count to 0" unless a Stun lands inside the round trip,
  which then keeps its debt.
- **Prediction misses are tolerated, not trusted.** A shell that goes up inside the round trip refuses the drain on the victim (`DrainForThief`),
  so no food moves on a wrong "affected"; a debt granted on a wrong prediction moves no food either.
- **Decoys.** A hit on a Doppelganger decoy (`IsDecoy`) is no take and no debt (the simulator has no decoys).
- **Protocol.** The first slice that adds networked members bumps `NetworkProtocol.Version` 2 to 3 and refreshes `NetworkProtocolTests`'
  fingerprint (ARCH-NET-21); later slices in this plan only refresh the fingerprint, unless a build was cut on 3 in between, then they bump again.

### 16.3 Hook points in the game today

| Moment | Where it is resolved now | What the build adds |
|---|---|---|
| Stun lands | `StunBurstAbilitySO.OnActivate` (WingSlam and Ambush) loops `GatherTargets` and calls `SendControl(caster, victim)`; the victim applies it in `ChickenController.RPC_ApplyStun` (`ControlReapplyRules.IncomingExtends`) | after each `SendControl`: `ReportHit(caster, victim, AbilityJob.Stun, caster.PredictStunLands(victim, EmittedControl))` |
| Shove lands | `CluckShockAbilitySO.OnActivate` loops `GatherTargets` and calls `caster.KnockAway(victim, KnockbackForce)`; the victim applies `ApplyKnockback` (`IsControlImmune`, `ModifyKnockback`) | after each `KnockAway`: `ReportHit(caster, victim, AbilityJob.Shove, caster.PredictShoveMoves(victim, KnockbackForce))` |
| Steal lands | `SnatchAbilitySO.OnActivate`: the Thief's marked rival first, then `StealFrom(thief, victim, StealAmount)` for each carrier `GatherTargets` found (the target filter keeps carriers only, as the simulator's) | before each plain `StealFrom`: `ReportHit(thief, victim, AbilityJob.Steal, true)` (the fresh stack counts toward this Steal, section 4.2 step 3 before 4), then the debt-inflated amount |
| Instant Slow | `SlowAbilitySO` sends `ControlEmission` through `SendControl` | `ReportHit(..., AbilityJob.Slow, ...)` for `HitStealSlow` (0 shipped and designed); Slow Area aura ticks never report (section 3) |
| Peck cast start / cut / landing | `PeckAbilitySO.OnActivate` / `OnDeactivate` (cut: no food, ARCH-NET-07), `ChickenCargo.RPC_SettlePeck` credits | `FreshStateId++` at `OnActivate`; `NotePeckFed()` from `RPC_SettlePeck` |
| Food transfer | `ChickenCargo.StealFrom` → `RequestSteal` → `RPC_DrainStolen` → `DrainForThief` → `RPC_SettleSteal` | a `StealFrom(victim, amount, out ushort requestId)` overload; `RPC_SettleSteal` tells `ChickenHitSteal` and `EnforcerDebts` what a request credited |
| Receiver bounds | `StealRules.MaxSingleSteal(pool)` (largest nominal steal times largest steal multiplier), `StealRules.MaxReach(pool)` (steal abilities only) | the hit-steal amounts and the full debt bonus in the magnitude bound; the reach of every hitter of a hit-steal passive in the range bound (WingSlam and CluckShock reach 5.4 m, Snatch 5.16 m: without this an honest hit-steal at the edge is refused) |
| Peck cooldown per kit | `PeckAbilitySO.ResolveCooldown` already calls `PeckCastRules.Cooldown(class, passive.PeckCooldownMultiplier)` (BL-223) | nothing: the 1.4 and 1.2 values work as soon as they are in the assets |
| Bot | `BotController.MayEngage` already calls `BotEngageRules.HitPays` / `MayEngage` | the P0 branches `HitForFood`, `WorthAHit`, `WorthAFight`, `EnforcerOverlay` (`ScriptedPolicy.cs`) mirrored in `BotController.Think` |

The game calls the same pure functions as `MatchSim`: `HitStealRules.Take`, `HitStealRules.Locked`, `BiteRules.Fresh`, `DebtRules.Add`,
`DebtRules.StealAmount`, `DebtRules.Decayed`, the new `HitAffectRules`, and the bot rules. No copy. Drift guards: `MatchWarriorTests.Sim_CallsTheSharedRules_NotCopies`
and `_Enforcer` (simulator side, extended in G2), the new `WarriorGameRulesTests.Game_CallsTheSharedRules_NotCopies` (game side, G4 and G5),
`ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem` (bot side, G6). Every scan proves it saw its pattern (CODE-TEST-13).

### 16.4 Slices

Common to every slice, as in section 10: other sessions share the tree, so `git status` first, stage by explicit path, never `git add -A`, stash,
reset or broad checkout, never stage `BUILD_NUMBER`; gate = compile clean (`EditorUtility.scriptCompilationFailed` false), the slice's tests, then
the full EditMode suite through `TestRunnerApi` from `script-execute` with the polled result file, or batch mode when no Editor is open, never both
(CODE-BUILD-02, CODE-BUILD-03, CODE-TEST-15). One concern per commit, the why in the body (CODE-PROC-11), every message names its BL id. Briefs follow
CODE-PROC-07 and CODE-PROC-08; `senior-dev` implements G1 to G8, `code-architect` reviews, `qa-reviewer` reviews G3 to G5 (networked, CODE-PROC-09).
Slices G1 to G6 ship nothing a player sees: every game value stays off until G7. **No build is cut between G7 and G9** (G7 turns the rules on before
the HUD can show them).

**G1. Simulator counters the oracle asked for** (simulator only; BL-228).
Files: `Balance/MatchCombat.cs` (`SeatCounters.PeckCasts`, `int`); `Balance/MatchSim.cs` (`StartPeck` counts every start, cut or landed);
`Balance/MatchReport.cs` (`EconomyShareLine` gains `Banked`, `Trips`, `PeckCasts`, per appearance, filled in the same loop as `Foraged`, with
`run.Results[i].Banked[s]` and `c.Trips`; the "Economy per kit" markdown table and the JSON `economy` array carry them); `Balance/MatchWhatIf.cs`
(`Numbers` gains group `economy`: per kit label, "foraged per match", "stolen per match", "banked per match", "trips home per match", "Peck casts per
match", for each population, from `MatchReportSection.Economy`).
Interface: `public sealed class EconomyShareLine { string Label; float Foraged, Stolen, Bounty, Banked, Trips, PeckCasts; }`.
Tests: `MatchWarriorTests.PeckCasts_CountsEveryStart_CutOrLanded` (one cast cut by a Stun, one landed: 2); `MatchReportTests.Economy_CarriesBankedTripsAndPeckCasts`
(a two-match hand-built run: the means per appearance); `MatchWhatIfTests.Numbers_CarryTheEconomyGroupPerKit` (every kit label, five lines per
population). Done when: a what-if report lists the five economy lines for every kit. Commit: `feat(balance): banked, trips and Peck casts per kit in the match report and what-if (BL-228)`.

**G2. Shared rules for the game** (pure; BL-226).
Files: new `Gameplay/HitAffectRules.cs`; `Gameplay/StealRules.cs`; `Balance/MatchSim.cs` (its "affected" tests call `HitAffectRules`).
Interfaces:
- `public static class HitAffectRules { static bool StunAffects(bool victimControlImmune, float victimStunLeft, float incomingSeconds); static bool ShoveMoves(bool victimControlImmune, float strength, float victimKnockbackFactor); }`
  (`StunAffects` = not immune, incoming above 0 and `ControlReapplyRules.IncomingExtends(left, incoming)`; `ShoveMoves` = not immune and
  `strength * max(0, factor) > 0`).
- `StealRules.MaxSingleSteal(pool)` = the larger of (largest nominal steal x largest steal multiplier + largest `Debt.Steal x Debt.Cap`) and the
  largest hit-steal amount of any passive (`HitStealFacts.Stun`, `Shove`, `Slow`); still a conservative superset, still derived (ARCH-NET-05).
- `StealRules.MaxReach(pool)` = the larger of today's value and, for each passive with `HitSteal.Active`, `HitReach(a)` of every pool ability whose
  `Job` has an amount above 0; `public static float HitReach(AbilityBaseSO ability)` = `AimRadius + AimForwardOffset` (the same shape reach `Reach` uses).
Tests: `HitAffectRulesTests.Stun_AffectsOnlyWhenItExtends_NeverAnImmuneVictim`, `Shove_MovesOnlyAboveZeroAfterTheFactor`,
`AgreesWithTheVictimsRpcPrimitives` (table: for each case the prediction equals `IncomingExtends` after the passive's `ModifyControlDuration`, and the
immune early return of `ApplyKnockback`); `StealRulesTests.MaxSingleSteal_CoversTheHitStealAndTheFullDebtBonus`,
`MaxSingleSteal_WithNoHitStealOrDebt_IsTheOldBound`, `MaxReach_CoversEveryHitterOfAHitStealPassive`, `MaxReach_IgnoresHittersWhenHitStealIsOff`;
`MatchWarriorTests.Sim_CallsTheSharedRules_NotCopies` finds `HitAffectRules.StunAffects` and `HitAffectRules.ShoveMoves`. Done when: shipped play is
unchanged (the shipped passives are still off, so both bounds equal today's). Commit: `feat(gameplay): hit-affect prediction and steal bounds that cover hit-steal and debts (BL-226)`.

**G3. The Peck's fresh state on every chicken** (networked, inert; BL-226).
Files: `Gameplay/AbilityController.cs`, `Gameplay/ChickenCargo.cs` (`RPC_SettlePeck`), `Abilities/PeckAbilitySO.cs` (`OnActivate`),
`Gameplay/BiteRules.cs`, `Networking/NetworkProtocol.cs`, `Editor/Tests/NetworkProtocolTests.cs`.
Interfaces on `AbilityController`: `[Networked] public int FedTick { get; private set; }`, `[Networked] public byte FedStateId`,
`[Networked] public byte FreshStateId`, `[Networked] public byte BittenStateId` (private set); `public bool IsPeckCasting`, `public float PeckCastLeft`,
`public float SecondsSinceFed` (infinity with no fed landing); `internal void NoteFreshStart()`, `internal void NotePeckFed()` (authority only);
`public bool IsFreshFor(float freshSeconds)`; `RPC_MarkBitten` as in 16.2. On `BiteRules`:
`public static bool LandedWithFood(int fedTick, byte fedStateId, byte freshStateId)` (`fedTick != 0 && fedStateId == freshStateId`: a newer cast ends
the "just landed" state) and `IsFreshFor` composes `Fresh(IsPeckCasting, SecondsSinceFed, LandedWithFood(...), BittenStateId == FreshStateId, freshSeconds)`.
Round reset leaves the ids alone (byte counters are never reset, ARCH-EVT-02) and sets `FedTick` 0.
Tests: `BiteRulesTests.LandedWithFood_OnlyForTheCurrentState`; `PeckFreshStateTests.SettlePeck_NotesFed_OnlyWhenFoodWasCredited` (source scan,
inside the `ToCargo > 0` branch), `PeckOnActivate_StartsANewFreshState`, `MarkBitten_ChecksTheSenderBeforeAnyWrite` (ARCH-NET-03 scan),
`IsFreshFor_GoesThroughBiteRulesFresh`; `NetworkProtocolTests` fingerprint and version 3. Done when: no reader yet, the suite is green, version 3.
Commit: `feat(gameplay): replicate a chicken's Peck fresh state for the Enforcer (BL-226)`.

**G4. The Bully's hit-steal in the game** (shipped off; BL-226).
Files: new `Gameplay/HitStealLedger.cs` (pure, generic over the victim key, like `PendingStealLedger`); new `Gameplay/ChickenHitSteal.cs`
(`NetworkBehaviour`, sibling of `ThiefLoot`, built the same way: `SelfInject.Ensure(this, _log != null)` in `Spawned`, ARCH-DI-03); `Gameplay/ChickenController.cs`
(`ReportHit`, `PredictStunLands`, `PredictShoveMoves`, the `Hits` accessor; no other change, ARCH-LAY-14); `Abilities/AbilityBaseSO.cs`
(`protected static void ReportHit(...)`); `Abilities/StunBurstAbilitySO.cs`, `Abilities/CluckShockAbilitySO.cs`, `Abilities/SlowAbilitySO.cs`;
`Gameplay/ChickenCargo.cs` (`StealFrom` overload, settle notice); `Gameplay/ChickenLocalEvents.cs` (`HitStealTaken`); `Chicken.prefab` gains the
component (CODE-ASSET-11; check the Doppelganger variant, CODE-ASSET-06); `ProjectConfigTests` component list.
Interfaces:
- `HitStealLedger<TKey>`: `bool Locked(TKey victim, float now)`, `void Ask(TKey victim, ushort requestId, float now, float lockSeconds)`,
  `void Settled(TKey victim, ushort requestId, float credited)` (0 releases the lock started by that ask), `float LockLeft(TKey victim, float now)`, `void Clear()`.
- `ChickenController`: `public void ReportHit(ChickenController victim, AbilityJob job, bool affected)` (forwards to `ChickenHitSteal.OnHit`
  and, from G5, `EnforcerDebts.OnHit`; no-op on a decoy victim); `public bool PredictStunLands(ChickenController victim, in ControlEmission e)`
  (`HitAffectRules.StunAffects` over the victim's replicated state and `ControlClaimRules.OutgoingSeconds` then the victim passive's
  `ModifyControlDuration`); `public bool PredictShoveMoves(ChickenController victim, float strength)`; `public ChickenHitSteal Hits`.
- `ChickenHitSteal`: `public void OnHit(ChickenController victim, AbilityJob job, bool affected)` (state authority only; no-op unless
  `Passive.HitSteal.Active`; refused by a wall or a shell exactly as `StealFrom`); amount `HitStealRules.Take(facts.AmountFor(job), free room, victim stealable, locked)`;
  asks through `ChickenCargo.StealFrom(victim, take, out requestId)`; `public float LockLeftOn(ChickenController victim)`;
  `internal void OnStealSettled(NetworkBehaviourId victim, ushort requestId, float credited)`; `[Networked] byte HitStealEventId`,
  `NetworkBehaviourId LastHitVictim`, `float LastHitTake` (payload first, then the id); the ledger clears at round start and when this chicken is removed.
- `ChickenCargo`: `public float StealFrom(ChickenController victim, float amount, out ushort requestId)` (the old overload calls it).
Tests: `HitStealLedgerTests.AskLocks_EveryHitterOfTheCaster`, `ZeroSettle_ReleasesTheLock_PositiveSettleKeepsIt`, `Lock_EndsAfterItsSeconds`,
`Clear_EmptiesEveryLock`; `WarriorGameRulesTests.StunBurst_ReportsEveryVictim_AfterSendControl`, `CluckShock_ReportsEveryVictim_AfterKnockAway`,
`HitSteal_MovesFoodOnlyThroughChickenCargoStealFrom` (scan: no `Cargo` write and no `RPC_DrainStolen` in `ChickenHitSteal`),
`Game_CallsTheSharedRules_NotCopies` (Bully half: `HitStealRules.Take`, `HitStealRules.Locked`, `HitAffectRules.StunAffects`,
`HitAffectRules.ShoveMoves`), `ReportHit_IsANoOpForAPassiveWithoutFacts`, `ChickenHitSteal_FixedUpdateNetwork_OpensWithTheAuthorityGate` (ARCH-NET-01);
`StealRulesTests` drain range: an honest 5.4 m hit-steal is inside `MaxReach` once a hit-steal passive is in the pool. Done when: the shipped Bully
(amounts 0) plays as before; a test registry with amounts 2 drains and credits through the one steal path. Commit:
`feat(gameplay): the Bully's hit-steal through the authority-confirmed steal path, shipped off (BL-226)`.

**G5. The Enforcer's debts in the game** (shipped off; BL-226).
Files: new `Gameplay/DebtBook.cs` (pure); new `Gameplay/EnforcerDebts.cs` (`NetworkBehaviour`, sibling of `ChickenHitSteal`);
`Gameplay/ChickenController.cs` (`Debts` accessor; `ReportHit` forwards); `Abilities/AbilityBaseSO.cs` (a `StealFrom` overload taking the debt
term); `Abilities/SnatchAbilitySO.cs`; `Gameplay/ChickenCargo.cs` (settle notice); `Gameplay/ChickenLocalEvents.cs` (`FreshHit`, `DebtGained`,
`DebtCashed`); `Chicken.prefab`; `Networking/NetworkProtocol.cs` and the fingerprint.
Interfaces:
- `DebtBook`: `struct DebtSlot { int Count; float Until; }`; `static DebtSlot Gain(DebtSlot s, int cap, float now, float seconds)` (`DebtRules.Add`,
  a gain at the cap restarts the timer); `static DebtSlot Decay(DebtSlot s, float now, out int lost)` (`DebtRules.Decayed`, all at once);
  `static DebtSlot Cash(DebtSlot s, int consumed, float credited)`.
- `EnforcerDebts`: the networked arrays of 16.2 plus `[Networked] byte FreshHitEventId, DebtEventId, DebtCashEventId`, `NetworkBehaviourId LastDebtVictim`,
  `float LastCashBonus`; `public int CountOn(ChickenController victim)`, `public float DecayLeftOn(ChickenController victim)`;
  `public void OnHit(ChickenController victim, AbilityJob job, bool affected)` (authority only; no-op unless `Passive.Debt.IsEnforcer`; fresh read
  `victim.Abilities.IsFreshFor(Passive.Bite.FreshSeconds)`; a fresh hit: one `Gain`, `FreshHitEventId++`, `victim.Abilities.RPC_MarkBitten(...)`; an
  affected Stun or Shove: one more `Gain`); `public float StealAmountOn(ChickenController victim, float resolvedSteal)` =
  `DebtRules.StealAmount(resolvedSteal, 1f, Debt.Steal, CountOn(victim))`; `internal void OnStealAsked(victim, requestId, int consumed)`;
  `internal void OnStealSettled(victimId, requestId, float credited)`. `FixedUpdateNetwork`: authority gate, decay, a removed victim's slot to 0, all
  slots 0 when this chicken is removed or the round is not running (as `ThiefLoot`'s `Off`).
- Capacity 4 covers `MatchConfigSO.MaxPlayers - 1` rivals; a test pins it.
Tests: `DebtBookTests.Gain_StopsAtTheCap_AndRestartsTheTimer`, `Decay_PutsTheCountToZeroAtOnce`, `Cash_SubtractsWhatTheStealConsumed_OnlyOnAPositiveSettle`;
`WarriorGameRulesTests.Snatch_ReportsTheHitBeforeItsSteal_AndAsksTheInflatedAmount` (scan order), `Game_CallsTheSharedRules_NotCopies` (Enforcer half:
`BiteRules.Fresh`, `DebtRules.Add`, `DebtRules.StealAmount`, `DebtRules.Decayed`), `EnforcerDebts_FixedUpdateNetwork_OpensWithTheAuthorityGate`,
`DebtCapacity_HoldsEveryRivalOfMaxPlayers`, `DebtTimers_AreReadThroughEffectWindow`; `StealRulesTests.MaxSingleSteal_CoversTheFullyInflatedSteal`.
Done when: the shipped Enforcer asset (off) changes nothing; a test passive with `DebtSteal` 2 asks 5 + 2 x count. Commit:
`feat(gameplay): the Enforcer's per-rival debts and the inflated Steal, shipped off (BL-226)`.

**G6. The game bot plays both kits** (BL-226).
Files: `Gameplay/BotController.cs` (mirrors of `ScriptedPolicy.HitForFood`, `WorthAHit`, `WorthAFight`, `EnforcerOverlay`, in the same place of
`Think` as P0's; rival facts from `ChickenHitSteal.LockLeftOn`, `EnforcerDebts.CountOn` / `DecayLeftOn`, the rival's `IsPeckCasting`,
`PeckCastLeft`, `SecondsSinceFed`, `FreshBitten`; a rival is only considered when `BotSightRules` sees it, ARCH-DATA-15); `Editor/Tests/ScriptedPolicyTests.cs`.
Tests: `P0_CallsTheGamesOwnFunctions_NotCopiesOfThem`: the bot list gains `BotHitStealRules.Gain(`, `CastForFood(`, `PickHitter(`, `WorthHunting(`,
`BotEnforcerRules.FreshLeft(`, `HitFresh(`, `PickBuilder(`, `Cash(`, `StealGain(`, `Fight(`; `BotWarriorOrderTests.BotController_WarriorBranches_RunInP0sOrder`
(scan: the four branches appear in `Think` in the order `ScriptedPolicy.Think` calls them). Done when: the guard is green; no new tunable without a
P0 mirror (`BotController_Tunables_StillMatchP0sMirrors`). Commit: `feat(bot): the game bot casts the Bully's hits for food and plays the Enforcer's debts, as P0 (BL-226)`.

**G7. Roster and data: the kits go live** (BL-226).
Data (in the Editor or by `git mv`, each with its `.meta`, CODE-ASSET-05):
- `git mv Assets/_Game/Data/Simulator/Enforcer.asset(.meta) Assets/_Game/Data/Abilities/Passives/` (guid kept); delete the empty
  `Data/Simulator/` folder and its `.meta`.
- `AbilityRegistry.asset`: Enforcer in Relentless's place; Relentless out. `Relentless.asset`: `Retired` true (its kit stays, so a what-if can swap it back).
- `ChickenSubclass` / `ChickenSubclassFlags`: member 1 renamed `Warrior_Relentless` to `Warrior_Enforcer` (value and bit unchanged, so
  `CluckShock.asset SignaturePreEquippedBy: 1` now names the Enforcer, which holds CluckShock); `ClassOf` updated; ARCH-NET-13 pin in
  `ContractsAndEnumsTests` updated (open question 16.9-3). `Enforcer.asset` keeps `Subclass: 1`; `Relentless.asset` keeps `Subclass: 1` (unregistered, read by nobody).
- `Bully.asset`: kit Peck, Shadowstep, WingSlam, CluckShock (`079c14ab0b344ca898f4be63fbf58374` in place of Snatch `4c026dfa…`); the values of 16.1.
- `Enforcer.asset`: the values of 16.1, `PeckCooldownMultiplier` 1.2 serialized.
Code: `EnforcerPassiveSO` (`IsSignature => true`, open question 16.9-2; perk text keys and `PerkArgs` from `FreshSeconds`, `DebtSteal`, `DebtCap`,
`DebtSeconds`, `PeckCooldownMultiplier`; the "simulator only" remarks rewritten); `BullyPassiveSO` (perk args gain the takes, the lock and the Peck
slowdown; the remarks "farms exactly like a naked Warrior" and "Applies to every move except Peck" rewritten: the 0.75 still skips Peck, the 1.4 slows it);
`Localization/UiKeys.cs` and `Resources/Text/UiText.csv` (`perk.enforcer.line`, `perk.enforcer.detail`, new `perk.bully.*` args with `[UiArgs]`, words from
`narrative-designer`, CODE-TEXT-01/05/06/08); `Balance/BalanceBook.cs` `DeniedFields` and `tools/balance/schema.py` `DENIED` and the Kit Lab page's
`DP_DENY` gain `FreshBiteShare` in the same commit (ARCH-DATA-14; the CLAUDE.md deny list line is proposed to Maestro, not edited, CODE-PROC-20);
`Diagnostics/AutoTestConfig.cs` needs nothing (it reads the enum names), but `tools/test-session/scenario-bots.ps1`, `scenario-pc.ps1` and their
fixtures pass `-cw_perk Warrior_Enforcer`; `Data/Balance/OracleReference.asset` re-recorded (`Cluck Wars / Oracle / Record`), so the reference
holds `Warrior_Enforcer` and the new Bully.
Tests changed, not weakened (CODE-TEST-12): `WarriorPassiveDataTests.Enforcer_IsRegistered_RelentlessIsShelved` (was `Enforcer_IsNotRegistered...`),
`ShippedBully_HitStealIsOn_AndItsKitHoldsNoSteal`, `ShippedEnforcer_FreshAndDebtsOn_NoFoodBite` (relations, not restated numbers);
`MatchWarriorTests.ShippedOff_PlaysExactlyAsBefore` becomes `OffFacts_PlayExactlyAsTheNakedKit` (it builds the off facts by hand; the shipped assets
are on now); `OracleRosterTests` and `WhatIfSpecTests` swap Enforcer for Relentless (`{"Enforcer":"Relentless"}`), `OracleKitsTests.LabelOf_...`
reads `Warrior_Relentless` for the swapped-in shelf kit; every test under `Editor/Tests` that reads Relentless from the registry or by
`Warrior_Relentless` (grep: `BotEngageRulesTests`, `CasterStunCountdownTests`, `CooldownsKitsSlipperyTests`, `DataIntegrityTests`, `FixedKitTests`,
`MarkPickTests`, `PerkTextTests`, `PlayAgainFlowTests`, `PreEquippedLoadoutTests`, `SimplifiedAbilitiesTests`, `SlowAbilityTests`,
`WarriorSpecializationTests`) either loads `Relentless.asset` by path or moves to the Enforcer; `BalanceBookTests.DeniedFields_MirrorSchemaPy`
(or its existing equivalent) and `tools/balance/tests/test_kitlab.py` see `FreshBiteShare`; `OracleReferenceTests` green on the new record;
`FixedKit.ValidateRoster` and `PreEquippedLoadout.ValidateRoster` over the shipped registry stay empty. Done when: character select offers
Bully and Enforcer for the Warrior, the fixed-kit lineup seats both, the suite is green. Commit:
`feat(roster): the Enforcer replaces Relentless and the Bully takes Stun and Shove, with their measured values (BL-226)`.

**G8. Balance tools and the Kit Lab** (BL-226).
Files: `tools/balance/kitlab_map.py` (`PASSIVE` gains `peckSlow` = `PeckCooldownMultiplier` as a percentage, `1 + v / 100`; plain-number keys for
`HitStealStun`, `HitStealShove`, `HitStealLockSeconds`, `FreshSeconds`, `DebtSteal`, `DebtCap`, `DebtSeconds`); `tools/balance/kitlab/kitlab.template.html`
(`DP_PASSIVE` mirror, the fields shown on the Bully and Enforcer cards; `DP_DENY` from G7); `tools/balance/export_kitlab_data.py` (nothing if the
scalars already export; check `lim` reads `[Min]` and `[Range]` of the new fields); `tools/balance/tests/test_kitlab.py`. `schema.py` already reads
fields from the C# source and the registry, so the new fields become addressable when G7 registers the Enforcer. Then `/balance kitlab` rebuilds the
Kit Lab artifact `https://claude.ai/artifact/BfxAferJSCtfC7qPi6Gagb` (same URL, capabilities `db` + `user` unchanged). No delta is published.
Tests: `test_kitlab.py` round trip for every new key (Kit Lab to delta to Kit Lab), `make_delta` refuses `FreshBiteShare`. Done when: a Kit Lab
edit of the Bully's take or the Enforcer's `DebtSteal` turns into a valid delta. Commit: `feat(balance): the Kit Lab edits the Warrior kits' new fields (BL-226)`.

**G9. Feedback, text and icons** (specialist sub-slices, BL-227; each briefed by `code-architect`, built by the named agent, screenshot before done, CODE-UI-02).
Animation and VFX are local, read from the event ids and the replicated state through `ChickenLocalEvents` and `ChangeDetector` + `Render()`,
never RPC'd (ARCH-EVT-01, ARCH-EVT-06); colours and timings get names in `FeedbackPaletteSO` / `FeedbackTuning` (ARCH-LAY-15, CODE-UI-08), and a new
palette field re-authors the shipped palette asset (CODE-ASSET-13).
- **G9a Fresh-window marker** (`vfx-artist` with `ui-designer`). A rival is marked for the local Enforcer only while `IsFreshFor(FreshSeconds)` is
  true on his own view: from its Peck cast start (so it shows mid-Peck), through the cast, and for `FreshSeconds` after a Peck that landed food, with a
  ring that drains over those seconds and goes out the moment the state is bitten (`BittenStateId`) or ends. Bright, readable at phone size, never on
  the local player's own chicken, never for non-Enforcers; a short pop when the local Enforcer's hit spends it (`FreshHitEventId`). Maestro asked for
  "como 1 segundo, feedback visual"; the shipped window is 1.5 s.
- **G9b Debt pips and "you owe"** (`ui-designer`, words `narrative-designer`). Over each rival the local Enforcer has a count on: 0 to `DebtCap` pips
  and a thin ring that drains over `DecayLeftOn`; a gain pops one in, decay pops all off together, a cash flies them into the Steal. The victim sees the
  same pips over his own head with a small cue naming the Enforcer's colour; other players see nothing (CODE-DESIGN-19 by analogy, C10). The local
  stack draws on top (CODE-UI-09).
- **G9c Bully take and lock** (`vfx-artist`, `audio-designer`). On `HitStealEventId`: `+n` pop over the Bully (n from `LastHitTake`), food chunks from
  victim to Bully (`FoodChunkFlight`), the existing Steal hit sound (by `SfxId`, CODE-AUD-01); victim: `-n` pop on his cargo bar and the existing
  short haptic. A locked rival shows a faint lock ring to the Bully only, draining over `LockLeftOn`, so a child sees why a hit took nothing.
- **G9d Ability and HUD numbers** (`ui-designer`, `narrative-designer`). The Enforcer's Steal hex shows the amount it would take from the nearest
  carrier in reach, `StealAmountOn(victim, ...)` (5, 7, 9, 11 with these values), never a typed number; perk lines and the character-select cards
  read every number from the fields (ARCH-DATA-12, CODE-TEXT-06, CODE-TEXT-10); a bigger `+n` pop on a cash. Kid wording, no jargon (CODE-TEXT-08).
- **G9e Icons** (`artist-2d`). `Icon_EnforcerPassiveSO.png` is a placeholder: it is byte-identical to `Icon_RelentlessPassiveSO.png` (same md5),
  and `AbilityIconStyle` says it shares Relentless's art. A new icon in the locked style, past the readability sheet (CODE-ASSET-18); the Bully icon
  stays. The pips and the fresh marker use simple shapes, not new icons, unless the readability sheet says otherwise.
- **G9f Feedback check** (the `audit` skill, feedback specialization, on both kits): captured frames on PC clients, verified against these briefs.

**G10. Docs, live check and close** (BL-226).
- A four-client session (`tools/run-clients.ps1`, `shared4`), one client `-cw_perk Warrior_Enforcer`, one `-cw_perk Warrior_Bully`, at log level
  Debug (CODE-BUILD-16, CODE-TEST-06): hit-steals and inflated Steals happen, zero `rejected RPC_DrainStolen` / `rejected RPC_MarkBitten` from honest
  clients (CODE-TEST-10), the world hash agrees on every client. Then `/backlog` marks BL-226 untested-to-tested only after Maestro's playtest (CODE-TEST-19).
- Docs upkeep (16.7), then the `housekeeping` skill.

### 16.5 Tests: what they never do

No test asserts a win rate or another oracle output: the oracle is a guide (CODE-DESIGN-01). No test restates a shipped number as a literal; the
data tests assert relations (on or off, a Steal in the kit or not, a bound covering a derived maximum). `OracleReferenceTests.Measured_EqualsTheAcceptedReference`
is a drift guard on the economy oracle and is re-recorded in G7, not loosened.

### 16.6 The balance hash

`bal=` is the hash of the **delta** JSON (`BalanceBook.BaselineHash = HashOf("{}")`), not of the shipped assets: after G7 a build with no delta
still prints `bal=5465b825`. Two builds with different shipped values are told apart by the build number and `NetworkProtocol.Version`, not by
`bal=`. What pins the old numbers: `Data/Balance/OracleReference.asset` (records stamped `BalanceHash: 5465b825` and the label
`Warrior_Relentless`), re-recorded in G7; the docs that quote "the shipped numbers" (`docs/TESTING.md` "Publishing a balance delta", the compendium)
get the new kit values, not a new hash.

### 16.7 Docs upkeep (for `librarian`, `docs/STATE.md`, the backlog and the site)

- `librarian` (the only editor of the rules docs and the file map, ruling D13): `docs/FILE_STRUCTURE.md` (new `HitAffectRules.cs`,
  `HitStealLedger.cs`, `ChickenHitSteal.cs`, `DebtBook.cs`, `EnforcerDebts.cs`; `Enforcer.asset` under `Data/Abilities/Passives/`; `Data/Simulator/`
  gone; `python -I tools/docs/file_structure.py check` green); `docs/ARCHITECTURE_RULES.md`: ARCH-NET-05 (the bound now covers hit-steal and the
  debt bonus, and the reach of hit-steal hitters; enforced by the G2 tests), ARCH-NET-07 (hit-steal and the inflated Steal in the food-transfer list),
  ARCH-NET-15 (the fresh-state tick stamp as a named exception), ARCH-EVT-02 (the new event ids), ARCH-DATA-14 (deny list gains `FreshBiteShare`),
  ARCH-NET-21 (version 3); `docs/CODE_RULES.md`: CODE-DESIGN-10 (the steal minimum also governs hit-steal), CODE-TEXT-06 (the new perk args);
  Appendix A for the stale lines of sections 4.4, 5 and 11 of this spec.
- `docs/STATE.md`: the two kits live, Relentless shelved, protocol 3, the open questions of 16.9.
- `docs/ARCHITECTURE.md`: the chicken's component list gains `ChickenHitSteal` and `EnforcerDebts`; the fresh state on `AbilityController`.
- `docs/GDD.md` Warrior section, `docs/FEEDBACK.md` (G9), `docs/TESTING.md` (`-cw_perk` names, the G10 session recipe).
- The compendium `docs/site/index.html` (CODE-PROC-13), numbers read from the shipped assets after G7: the Warrior's two kits (Bully: Peck,
  Shadowstep, WingSlam, CluckShock, cooldowns x0.75 except Peck, Peck x1.4, take 2 per Stun or Shove, lock 3 s; Enforcer: Peck, WingSlam, CluckShock,
  Snatch, fresh window 1.5 s, +2 Steal food per debt, cap 3, debts last 6 s, Peck x1.2), Relentless off the roster; republished to its Artifact URL.
- CLAUDE.md "What a delta cannot change" gains `FreshBiteShare`: proposed to Maestro as a line, not edited by an agent (CODE-PROC-20).
- Backlog (next free numbers; added with `/backlog`): **BL-226** P1 task, game build of the Bully hit-steal and the Enforcer (G2 to G8, G10);
  **BL-227** P1 task, Warrior kit feedback, text and the Enforcer icon (G9); **BL-228** P2 task, banked, trips and Peck casts per kit in the what-if
  (G1); **BL-229** P3 idea, the fresh bite as food in the game (`FreshBiteShare` above 0), parked until Maestro asks, needs a pile path for a hitter
  outside the pile's reach. BL-222 closes when G7 lands.

### 16.8 Where the code disagrees with this spec (found while planning)

1. `bal=` does not change when the shipped assets change (16.6); the brief's "the balance hash changes" holds only for a published delta.
2. The game resolves "affected" on the victim's authority only; the simulator resolves it in one tick. Section 16.2 settles it with a caster-side
   prediction on the same primitives.
3. `StealRules.MaxReach` counts only steal abilities: Snatch's 5.16 m is below WingSlam's and CluckShock's 5.4 m, so an honest hit-steal at the edge
   would be refused by `RPC_DrainStolen` (fixed in G2).
4. `PeckLandedEventId` bumps at the cast end, before the pile confirms food; "landed with food" needs the settle (G3).
5. `Icon_EnforcerPassiveSO.png` is a byte copy of Relentless's (placeholder, G9e).
6. Relentless is the Warrior's `IsSignature` (the default pick); with it shelved the Warrior has no signature until G7 gives it to the Enforcer.
7. `BullyPassiveSO`'s remarks ("farms exactly like a naked Warrior") and the perk detail "Applies to every move except Peck" are false once
   `PeckCooldownMultiplier` is 1.4 (G7).
8. `Snatch.asset SignaturePreEquippedBy: 130` still names the Bully (bit 2): Free mode only, harmless in Fixed mode; left as is (16.9-6).
9. Section 11 said `tools/balance/schema.py` `DENIED` stays unchanged; this plan adds `FreshBiteShare` (16.1).
10. Section 15.3 recommended debt Steal 1 near Peck x1.1; the shipped pick is the later measurement (debt Steal 2, fresh 1.5 s, Peck x1.2), whose
    worst 5 s loss mean (6.73) is above the go limit of 6 (16.9-5).

### 16.9 Open questions for Maestro (the build uses the default and does not wait)

1. **Who sees the fresh marker?** Today's brief: the Enforcer only. Section 6 and C10 also showed it to the pecking chicken ("mind the
   Enforcer"). Default: the Enforcer only.
2. **Which Warrior kit is the default pick** (`IsSignature`, Relentless's role)? Default: the Enforcer, which takes Relentless's seat.
3. **Subclass identity:** rename enum value 1 to `Warrior_Enforcer` (default: no new value, `ValidateRoster` stays as it is), or append value 9
   and teach the roster checks about shelved subclasses.
4. **The food bite in the game:** default no (shipped 0, deny-listed, BL-229 parked); the simulator can still measure it only by a build that lifts the deny.
5. **The Enforcer's worst 5 s loss:** debt Steal 2 measures a mean of 6.7 (go limit 6, a combo takes up to 11 from one victim). Default: ship 2 as
   chosen and watch the playtest; the fallback is debt Steal 1 (5.5 mean in section 15), a delta, no build.
6. **The Bully's Free-mode signature** stays Snatch, which his fixed kit no longer holds. Default: leave it (Free mode is not shipped).
7. **Two Enforcers on one fresh rival inside one round trip** can both stack the fresh debt. Default: accept (no food moves; the simulator's
   "first bites, second does not" holds whenever the hits are further apart than a round trip).
