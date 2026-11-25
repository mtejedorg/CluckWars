# Warrior identity: the Bully's hit-steal and the Champion (design and simulator plan)

Date: 2026-11-25. Status: design; the simulator slices S1 to S6 are built (results in section 14). **Amended 2026-11-25 (afternoon):** the Champion is
redesigned by Maestro after run B (section 14.2): fresh rivals, per-enemy trophies that inflate the Steal (section 4); the rework
is planned as slices R1 to R4 (section 10.1). **Amended again (evening):** a hit on a fresh rival takes no food, it only stacks one more trophy
(section 1, last Champion bullet); the old bite stays as data (`FreshBiteShare`) so both readings can be measured. The Bully is unchanged. Inputs: Maestro's decisions of 2026-11-25 (section 1, closed), the code read on
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
- **Champion** (named by Maestro; replaces Relentless, which is shelved, not deleted). "Integremos las dos en una: el 2 extra de cortar un peck es otro
  trophy. No entiendo en qué afecta el tamaño, va a ser confuso." Kit Peck, Speed, Steal, Slow. The break and the trophies are section 4.
- **Champion, redesign (Maestro, 2026-11-25, after run B; supersedes the break and the capacity trophies).** The bite: "más simple aún: si le da
  un mamporrazo a un pollo que acaba de hacer peck (como 1 segundo, feedback visual), le roba el bocata (incluyendo mientras hace el propio peck)."
  The trophies: "El trofeo es por enemigo, y crece por cada habilidad acertada (y corte de peck), inflando el steal." The base: "Mantenemos la carga
  a 20 pero bajamos la capacidad de farmeo. Los cooldowns le obligan a compaginar pelea con farmeo." Closed: no Peck-press break and no timing
  window on the Champion's side; a rival is fresh mid Peck-cast and about 1 s after its own Peck landed, and both it and the Champion see it; any
  of his hits on a fresh rival takes the bite; trophies are per enemy, +1 per landed ability and per bite, and inflate his Steal on that enemy;
  cargo stays 20, the Warrior's foraging goes down (lever measured, section 9); no capacity trophies, no size change.
- **Champion, the bite takes no food (Maestro, 2026-11-25, evening; supersedes the bite food above).** "El champion no se lleva nada de cortar un peck, solo
  una stack para el robo." So a hit on a fresh rival (mid Peck-cast, or within `FreshSeconds` after its Peck landed) takes no food: it adds the extra
  trophy to that rival's count, on top of the +1 the hit itself gives. A mid-cast Stun or Shove cuts the cast as it does today; nothing moves from the
  pile or the victim's cargo, and all the food the Champion gets from a rival comes through the Steal, inflated by the count. The bite food stays as
  data, not a deletion: `FreshBiteShare` on the Champion passive (the share of the victim's `PeckAmount` taken, shipped 0 and 0 in the what-if anchor;
  1 is the old reading), so both can be measured.
- Overflow goes to the centre pile (ruled earlier today; the game path is a separate item; the simulator models it for these two kits).
- The Thief gets no Peck interplay change. `RelentlessPassiveSO` and `Relentless.asset` stay in the repo.

## 2. What exists (verified in code)

| Fact | Where |
|---|---|
| Bully kit today: Peck, Shadowstep (Jump), WingSlam (Stun), Snatch (Steal). The new kit swaps Snatch for CluckShock (Shove). | `Passives/Bully.asset` `Kit`; `WingSlamAbilitySO.Job` Stun, `CluckShockAbilitySO.Job` Shove |
| Relentless kit: Peck, SpeedBurst (Speed), Slow, Snatch (Steal): **already the Champion's kit**. | `Passives/Relentless.asset` |
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
| (rework) `Champion.asset` lives in `Data/Simulator/` (not `Passives/`), kit guids Peck `2b6f2fba…`, SpeedBurst `56e7e5a4…`, Slow `eca28bb3…`, Snatch `4c026dfa…`; WingSlam is `6bbb9c61252f875408fde062c994669c`, CluckShock `079c14ab0b344ca898f4be63fbf58374`. | `Data/Simulator/Champion.asset`, the `.meta` files |

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

## 4. Rule 2: the Champion (redesigned 2026-11-25)

**In one line (for a child):** hit a bird that has just pecked and you stack an extra trophy on it; every hit on the same bird is a trophy, and your
Steal on that bird takes 2 more food for each trophy.

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
- Fresh is a fact of the victim, not of a Champion: two Champions see the same state, and the first to bite spends it.
- Assassins never peck, so they are never fresh. A rival that is fresh and in its egg cannot be hit, so it cannot be bitten.
- `FreshSeconds` 0 is the switch: no rival is ever fresh, mid-cast included (shipped).

### 4.2 The fresh hit and the bite

**Amended (Maestro, evening): the shipped rule takes no food.** A hit on a fresh rival adds the extra trophy of 4.3 and spends the fresh state; it
takes nothing from the pile or the cargo. A Stun or Shove still cuts a casting Peck as its own effect (a Steal never cuts). The food rules below are
the **bite**: they apply only when `FreshBiteShare` is above 0, with the bite amount = `FreshBiteShare` x the victim's `PeckAmount` (1 = the old
reading). In the tables, "the bite" and "bite trophy" read as: the extra fresh-hit trophy always; the food only with the share.

A **hit** is one of the Champion's Stun, Shove or Steal affecting a rival, with the "affected" test the Bully's hit-steal uses:

| Hitter | Affected when |
|---|---|
| Stun | the victim's stun is set or extended (`ControlReapplyRules.IncomingExtends` true) |
| Shove | the impulse after the victim's `KnockbackFactor` is above 0 |
| Steal | the Steal reaches him: in reach, no base wall, not in his egg, and he carries food (`StealRules.Stealable` above 0, Snatch's own target filter) |
| Peck | never a hit |

Any hit on a fresh rival stacks the fresh-hit trophy and, with `FreshBiteShare` above 0, takes the **bite**, `FreshBiteShare` x the victim's own `PeckAmount` (3 today at share 1):

| Case | Result |
|---|---|
| Victim mid-cast | his cast is cut and nothing lands for him; his Peck cooldown stays spent (as any cut). The Champion takes `min(victim's PeckAmount, food in the victim's pile, his free room)` **from that pile**; what does not fit stays in the pile. The victim's cargo is untouched. Cut cause: the hit's own (`Stun`, or `Shove` when knocked above `ShoveCounts`), else the new `CutCause.Bite` (a Steal, or a shove too weak to cut by itself) |
| Victim just landed | the Champion takes `StealMath.Clamp(victim's PeckAmount, his free room, victim's stealable)` **from the victim's cargo** through the existing authority-confirmed steal path; what does not fit stays with the victim |
| Food that fits is 0 (Champion full, pile empty), or the share is 0 | the fresh hit still happens: the state is spent and the extra trophy is earned (the catch is the feat; as the old break, decision C3); with the share at 0 nothing is taken and a Steal's hit cuts nothing |
| Fresh state already bitten | no bite; the hit still lands and still earns its own hit trophy |
| Egg | the hit is refused, so no bite and no trophy |
| Shield walk phase | Stun, Shove and Steal land (the walk refuses only Slow), so the bite lands |
| Base wall between them | the hit is refused (`RivalCanBeHit`) |
| Victim hidden | Hide is no refusal: if the hit lands, the bite lands |
| Champion stunned, dashing or removed | he cannot cast: no hit, no bite; his trophy counts keep decaying |
| Champion hidden | his kit has no Hide; a cast ends any Hide as for every chicken |
| Two Champions on one victim | resolved in the rotating cast order of `ResolveCasts`: the first bites, the second's hit lands with its own hit trophy and no bite |
| Victim executed | the killer drains him as today; every Champion's count on him goes to 0; his fresh state ends with his removal |
| What the victim keeps | everything not taken: the rest of the pile, the rest of his cargo, and, if he is a Champion himself, his own trophies (being robbed no longer costs a trophy) |
| A Bully's hit on a Champion | unchanged (section 3) |

**Order inside one cast.** The cast walks its victims in the seat loop of `Cast` (`v = 0 .. Seats-1`, as today). Each victim is resolved
completely before the next, and every take is clamped to the room left after the previous ones:

1. Freshness is read **before** the hit's own effect (a landed Stun cuts the cast first; the snapshot keeps it mid-cast for the bite).
2. The hit's own effect (stun set, shove impulse); a Steal's take waits for step 4.
3. Fresh hit (if fresh): the food bite only when `FreshBiteShare` is above 0; then trophies on that victim: +1 for the fresh hit (the extra stack), +1 for
   a Stun or Shove hit, each capped at `TrophyCap`, each restarting that victim's decay timer.
4. Steal only: take `StealMath.Clamp(StealAmount x StealFactor + TrophySteal x count, free room, stealable)`, where `count` already holds the bite
   stack of step 3; when the take is above 0 that victim's count goes to 0 (the Steal adds no trophy); a take of 0 leaves the count.

### 4.3 Trophies (per enemy)

| Case | Result |
|---|---|
| Count | one number per Champion per rival, 0 to `TrophyCap` |
| +1 | a Stun or Shove that affected that rival; one more for any hit (Stun, Shove or Steal) on that rival while it is fresh, once per fresh state |
| At the cap | a gain adds nothing but restarts the decay timer |
| Decay | the count goes to 0 when `TrophySeconds` pass with no +1 on that rival (all at once, not one by one) |
| Cash | his Steal on that rival (section 4.2 step 4) |
| Champion executed | all his counts 0 |
| Victim executed | every count on that victim 0 |
| Banking | no effect (counts live on rivals, not in his beak) |
| Capacity | never changes: class capacity 20 |
| Round reset | all counts 0, every fresh state cleared |

### 4.4 Fields

On `ChampionPassiveSO` (served through `PassiveAbilitySO.Bite`, a `BiteFacts`, and `PassiveAbilitySO.Trophy`, a reshaped `TrophyFacts`; no code
asks "is this the Champion"):

| Field | Type | Shipped (off) | Designed | Sweep |
|---|---|---|---|---|
| `FreshSeconds` | float `[Min(0)]`, 0 = no rival is ever fresh | 0 | 1.0 | 0.6, 1.0, 1.5 |
| `FreshBiteShare` | float `[Range(0,1)]`, share of the victim's `PeckAmount` a fresh hit takes as food; 0 = none (Maestro, evening) | 0 | 0 | 0, 1 |
| `TrophySteal` | float `[Min(0)]`, 0 = trophies add nothing | 0 | 2 | 1, 2, 3 |
| `TrophyCap` | int `[Min(0)]` | 3 | 3 | 2, 3, 4 |
| `TrophySeconds` | float `[Min(0.5)]` | 6 | 6 | 4, 6, 9 |
| `Kit` | asset | Peck, WingSlam, CluckShock, Snatch | same | none |

**Foraging lever** (class, not passive): `Warrior.asset` `PeckCooldown` 0.71 and `PeckAmount` 3, a plain delta (`"assets":{"Warrior":{...}}`), swept
as levels F0 to F2 in section 9; the exact values come from the measurement running now. It is class-wide on purpose (no new code), so it also
moves the Bully and shrinks the bite taken **from** a Warrior; the grid reports both.

Off means `FreshSeconds` 0 and `TrophySteal` 0: the Champion plays as a plain Peck, Stun, Shove, Steal kit. `Champion.asset` stays in
`Data/Simulator/`, **not registered**, seated only by the what-if roster swap (section 5); the game cannot pick it.

### 4.5 Arithmetic to watch (shipped Steal 5, starting values)

A full combo on one carrier with the shipped no-food rule: Stun on a just-landed rival (count 2: the Stun and the fresh stack), Shove (count 3, the
cap), Steal 5 + 2 x 3 = 11. The victim loses **11** from his cargo inside about 2 s (needs 11 room in the Champion's 20 and 11 food on the victim).
With `FreshBiteShare` 1 the old reading adds the 3-food bite (14 in all; mid-cast 11 from cargo and 3 from the pile). Ceiling in one 5 s window: the
Steal is ready again 4 s later, so about 11 + 7 = 18 without the bite (24 with it). Both are above the go limit of 6 (section 9); the mean is what
the run measures, and `TrophySteal` 1 gives 8 (combo) and 13 (ceiling).

### 4.6 Superseded 2026-11-25

The Peck-press break (`BreakReach`, `BreakCooldown`, `BreakImmuneSeconds`, the press preview, fallback and cancel, `CutCause.Break`) and the
capacity trophies (`TrophyRoom`, `TrophiesClearOnBank`, one trophy lost when robbed, overflow of the room to the centre pile) are retired from the
design. Why: section 14.2 (the break fired 0.24 to 0.33 times a match because a 0.4 s cast leaves the bot about 0.1 s to press; the room was
8 to 11 % used because the bot banks at 12 in a beak of 20). Slices S1, S4 and S5 built them; R1 to R4 (section 10.1) remove them.

## 5. How a what-if reaches each kit (the deny list)

- **Bully:** a plain delta. The amounts and the lock are serialized fields of a registered passive; the kit is `kits.Bully`. Nothing denied.
- **Champion:** a passive class cannot be swapped by a delta (`Subclass` denied, registry not delta-able). Least invasive legal way: a
  **simulator roster swap**. The what-if file gains one optional top-level key, `"roster": {"Relentless": "Champion"}` (asset names). The editor
  entry builds a temporary `AbilityRegistrySO` whose `All` is the shipped array with that one passive replaced, and runs the book and the simulator over
  it; deltas then name `Champion` fields as usual. The shipped registry, the game and `BalanceBook` are untouched; the swap is refused unless both
  passives allow the same single class.
- Rejected: registering Champion now (the game would offer a ninth kit, and `ChickenSubclass` has no free Warrior value); putting break and trophy
  fields on `PassiveAbilitySO` and turning Relentless into the Champion by delta (pollutes every passive, labels the kit Relentless, needs
  Relentless's own multipliers zeroed in every scenario).
- Labels: `OracleKits.LabelOf(passive)` = `Subclass` name when the asset name is the subclass's suffix (every shipped passive), else
  `<Class>_<asset name>`, so the swapped kit reads `Warrior_Champion`.

## 6. What is seen and heard (game slice, later)

Bully, on a take: `+2` pop over the Bully, three food chunks from victim to Bully (`FoodChunkFlight`), the existing Steal hit sound; locked rivals
show a faint lock ring to the Bully only (so a child sees why a hit took nothing). Victim: `-2` pop on his cargo bar, a short buzz (existing haptic).

Champion (redesign):
- **Fresh marker.** A fresh rival wears a bright sandwich glint over its head from its cast start to `FreshSeconds` after a landing that took
  food, with a ring that drains over the just-landed seconds; it goes out the moment the state is bitten or ends. Seen by every Champion in the
  match and by the fresh chicken itself (its own glint plus a pulse on its Peck hex: "you just pecked, mind the Champion"); other players do not
  see it. Only shown while a Champion is in the match.
- **Bite.** `BITE!` pop over the Champion; the food flies from the pile (mid-cast) or from the victim (just landed) to his beak; a crunch sound.
  Victim: mid-cast, his cast bar shatters; just landed, a `-3` pop on his cargo bar; a short buzz.
- **Trophy pips, per enemy.** Over each rival the Champion has a count on: 0 to `TrophyCap` small trophy pips with a thin ring that drains over
  `TrophySeconds`; a +1 pops a pip in, decay pops them all off together, a cash flies them into the Steal. The victim sees the same pips over
  his own head ("the Champion has 2 on you"); other players do not.
- **Inflated Steal.** The Champion's Steal hex shows the amount for the rival it would take from (the nearest carrier in reach), `5`, `7`,
  `9`, `11` with starting values, growing with each pip, read from `StealAmount`, `StealFactor` and `TrophySteal` (ARCH-DATA-12, no number
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

`BotChampionRules` (Champion; replaces `BotBreakRules`):
- `float FreshLeft(bool casting, float castLeft, float sinceLanded, bool landedWithFood, bool bitten, float freshSeconds)`: how long the rival stays
  fresh (a hit on it stacks the extra trophy): `castLeft + freshSeconds` mid-cast (assuming the cast lands), `freshSeconds - sinceLanded` just landed, 0 when bitten, not fresh or
  `freshSeconds` 0. Built on `BiteRules.Fresh`.
- `bool HitFresh(float freshLeft, float reaction)`: a fresh rival in a ready hitter's reach is worth a hit now when `freshLeft > reaction`.
- `int PickBuilder(bool stunReady, bool shoveReady, float stunCooldown, float shoveCooldown)`: the hitter that builds a trophy: Stun or Shove,
  the shorter cooldown first (Shove), -1 when neither is ready.
- `bool Cash(int count, int cap, float decayLeft, float nextBuilderReadyIn, float stealGain)`: Steal that rival when `stealGain >= 1` and
  `count >= 1` and either `count >= cap` or no builder is back before the count decays (`nextBuilderReadyIn >= decayLeft`). A Steal at count 0
  stays the existing Steal rule.
- `float StealGain(float stealAmount, float stealFactor, float perTrophy, int count, float freeRoom, float victimStealable)` =
  `StealMath.Clamp(TrophyRules.StealAmount(...), freeRoom, victimStealable)`.
- `bool Fight(float fightGain, float fightSeconds, float peckAmount, float peckCycleSeconds)`: leave the pile when the fight's food per second
  (bite food, which is 0 at `FreshBiteShare` 0, plus the Steal gain on the count the fresh stack raised, over walk plus casts) beats the Peck's (`peckAmount / peckCycleSeconds`, cycle = `PeckCooldown + PeckCastSeconds`). This is
  where the foraging lever of section 9 bites: a slower or smaller Peck makes more fights worth it.
- No bank rule: the Champion banks at `ReturnAmount` 12 like every Warrior; the capacity never grows, so `ReturnFraction` goes.

P0 order each think: a fresh rival in a builder's reach with `HitFresh` true gets the builder (`PickBuilder`), or the Steal when no builder is
ready and he carries; else a due `Cash`; else forage. P1 (simulator only): `BestHit` rates Champion targets by `Fight`'s value (bite + the
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

`TrophyRules.cs` (reshaped): `struct TrophyFacts { bool IsChampion; float Steal; int Cap; float Seconds; static None }`;
`static int Add(int count, int cap)` (the old `Earn`); `static float StealAmount(float stealAmount, float stealFactor, float perTrophy, int count)`
= `stealAmount x stealFactor + perTrophy x max(count, 0)`; `static bool Decayed(float now, float until)`. `LoseOne`, `Room`, `StealEarns`,
`Overflow` are deleted.

`PassiveAbilitySO`: `virtual HitStealFacts HitSteal => HitStealFacts.None`, `virtual BiteFacts Bite => BiteFacts.None` (replaces `PeckBreak`),
`virtual TrophyFacts Trophy => TrophyFacts.None`.

### 8.2 Simulator types and call sites (`Assets/_Game/Scripts/Balance/`)

Kept from S3 (Bully): `MatchPassive.HitSteal`; `Chick.HitLockUntil`; `SeatView.HitLockLeft`; `HitSteal(c, v, job)` from `Control` and `Shove`;
`CapacityOf(int s)` as the only reader of `Farm.CargoCapacity` (now just `Farm.CargoCapacity`, the trophy term goes).

Removed (S4/S5 break and room): `MatchPassive.Break`; `MatchIntent.Break/BreakTarget`; `CastRequest.Break` and its branch in `ResolveCasts`/`Cast`;
`ResolveBreak`; `Chick.Trophies`, `NextBreakAt`, `BreakImmuneUntil`; `SeatView.Trophies`, `BreakImmuneLeft`, `BreakLeft`; `Robbed` and its three
calls (`DoSteal`, `TakeLoot`, `HitSteal`; it only did the trophy loss and the spill); `SpillOverflow`; `BankTrophies`; `EarnTrophy` (replaced);
the trophy drain in `Execute`; `TrophySecondsAcc`; the Steal branch's `StealEarns` line. `CutCause.Break` is renamed `CutCause.Bite` (`"bite"`);
`MatchEventKind.PeckBreak` and `Overflow` become `Bite` and `TrophyCash` (sim-only enums, appended order kept).

Added:
- `MatchPassive.Bite` (`BiteFacts`), `MatchPassive.Trophy` (reshaped), read in `MatchKits.ReadPassive`.
- `Chick`: `float LandedAt` (`-inf` until a landing), `bool LandedWithFood`, `bool FreshBitten`; `int[] TrophyOn` and `float[] TrophyUntil` (by
  victim, sized `Seats`). `StartPeck` clears `FreshBitten`; `Land` sets `LandedAt`, `LandedWithFood = take > 0` and clears `FreshBitten`.
- `bool IsFresh(int v, in BiteFacts f)` = `BiteRules.Fresh(ch.Pecking && _time < ch.PeckDoneAt, _time - ch.LandedAt, ch.LandedWithFood, ch.FreshBitten, f.FreshSeconds)`.
- `ChampionHit(int c, int v, AbilityJob job, bool castingBefore, bool freshBefore)`: no-op unless `PassiveOf(c).Trophy.IsChampion`. On
  `freshBefore`: `FreshHits++`, the state is spent (`FreshBitten`), and the fresh stack is added; the food bite below runs only when
  `Bite.FoodShare` is above 0: mid-cast (`castingBefore`) cuts the cast if it is still running (`Cut(v, CutCause.Bite)`), takes `BiteRules.FromCast` from
  `_pile[v.PeckPile]`; else `BiteRules.FromCargo` from the cargo (with `TakeLootShare`, `NoteLoss`, `LostToSteals`, as `HitSteal` does); sets
  `FreshBitten`, `Record(Bite)`. Then `AddTrophy(c, v)` for the bite and, for Stun or Shove, once more for the hit.
- Call sites: `Control` stun branch reads `castingBefore`/`freshBefore` **before** `Cut(victim, CutCause.Stun)` and calls `ChampionHit` after
  `HitSteal`; `Shove` the same around its cut; the Steal case of `Cast` (non-marked branch) calls `ChampionHit` after the target filter and before
  `DoSteal`, which gains a `bonus` argument: `TrophyRules.StealAmount(ab.StealAmount, factor, Trophy.Steal, c.TrophyOn[v])`, then the count is
  zeroed when the take is above 0 (counted as a cash).
- Each tick: a count whose `TrophyUntil` has passed goes to 0 (`TrophyRules.Decayed`, counted). `Execute`: the victim's row and column of every
  Champion's counts go to 0. Round reset: counts and fresh state cleared.
- `SeatView`: `float SinceLanded`, `bool LandedWithFood`, `bool FreshBitten` (every seat); `int[] TrophyOn`, `float[] TrophyLeft` (own seat only).
  The policies call `BiteRules.Fresh` with their own `FreshSeconds`; they never re-derive it.

### 8.3 Counters (`SeatCounters`) and report rows

Bully (kept): `HitTakes`, `HitFood`, `HitRefusedLock`, `HitRefusedNoFood`, `HitRefusedRoom`, `HitLostFood` (victim side). Hit-steal food also adds
to `Stolen`, `StolenFromSeat` and the victim's `LostToSteals`, not to `Steals`.

Champion (new `FreshHits`: hits on a fresh rival that stacked the extra trophy, whatever the food; then replace `Breaks`, `BreakFood`, `BreakLeftInPile`, `BreakFallbacks`, `BreakCancels`, `BreakRefused`, `PecksBroken`, `TrophiesLost`, `TrophyPeak`,
`TrophySeconds`, `TrophiesAtEnd`, `TrophyRoomFilled`, `TrophiesCleared`, `OverflowToCentre`):
`BitesFromCast`, `BitesFromCargo` (only at a bite share above 0), `BiteFoodFromCast`, `BiteFoodFromCargo`, `BiteLeftInPile`, `BiteLeftWithVictim`, `BitesSpent` (hits on a fresh
state already bitten), `PecksBitten` and `BiteLostFood` (victim side); `TrophiesEarned`, `TrophiesDecayed`, `TrophiesCleared` (executions);
`TrophyCashes` (Steals with a count above 0), `TrophiesCashed` (sum of counts consumed), `TrophyBonusFood` (the `TrophySteal x count` part that
fitted); `StealFood` (food from Steal casts, so mean Steal size = `StealFood / Steals`). Identity food = `BiteFoodFromCast + BiteFoodFromCargo +
TrophyBonusFood`. `WorstVictimLoss5s` stays (cargo bites and Steals feed `NoteLoss`; a mid-cast bite is not the victim's cargo, so it is reported
apart as `BiteFoodFromCast`).

`MatchReport` "Warrior kits" block, Champion row: win rate; bites per match from cast and from cargo, and their food; trophies earned and decayed
per match; trophies consumed per cash (`TrophiesCashed / TrophyCashes`); mean Steal size; identity food per match; worst 5 s window (mean, 95th
percentile). The break, trophies-held, room-used and overflow columns go. `MatchWhatIf.Numbers` group `warrior` follows; `WhatIfOutput.TrophyKit`
stays (`Trophy.IsChampion`).

### 8.4 Drift guards

- `MatchWarriorTests.Sim_CallsTheSharedRules_NotCopies`: source scan of `MatchSim.cs` finds `HitStealRules.Take`, `BiteRules.Fresh`,
  `BiteRules.FromCast`, `BiteRules.FromCargo`, `TrophyRules.Add`, `TrophyRules.StealAmount`, and proves it saw them (CODE-TEST-13); and finds no
  `PeckBreakRules`, `ResolveBreak`, `SpillOverflow`.
- `MatchWarriorTests.EveryCapacityRead_GoesThroughCapacityOf`: unchanged.
- `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem` lists the `BotHitStealRules` and `BotChampionRules` calls (and no
  `BotBreakRules`).
- `MatchWarriorTests.ShippedOff_PlaysExactlyAsBefore`: the shipped Bully facts against `HitStealFacts.None`; the Champion as shipped
  (`FreshSeconds` 0, `TrophySteal` 0) against `BiteFacts.None` and `TrophyFacts.None` on the same kit: same seed, identical winners, end times and
  food counters.
- The "affected" tests reuse `ControlReapplyRules.IncomingExtends` and the shove impulse, as the Bully does; no new mirror.

## 9. What-if grids, trimmed

Budget: about 25 s a scenario at `replicateScale` 0.4; one run is kept to at most 16 scenarios (about 7 min), as each stage of the loot-mark runs. The
full grids are 27 (Bully) and 54 (Champion) scenarios, about 34 min. Read the shipped Warrior stats first: run only once `Warrior.asset`
(10.8, 20, 0.71, 0.4) is committed.

**Run A, Bully** (`whatif.json`, no roster key; every delta carries `"kits":{"Bully":["Peck","Shadowstep","WingSlam","CluckShock"]}`), 12 scenarios:
B0 kit only (amounts 0); the 3 x 3 Stun x Shove amounts at lock 3 (9); lock 2 and 4 at 2 / 2 (2). Why: the lock rarely binds alone, since one Bully's
two hitters come back 3.75 and 5.25 s apart, so the lock only matters when both hitters are ready at once or 2 to 4 s apart; it is measured at the
anchor and, in a second run only if the anchor is close, at the best amount cell. The baseline of this run is the shipped game (Relentless and the
old Bully): it is the reference for run B too.

**Run B, Champion (superseded, results in 14.2).** The reach x cooldown and trophy-lifetime grid of the break design; kept only as the record of
section 14.2.

**Run C amended (Maestro, evening):** the foraging levels are `PeckCooldownMultiplier` 1.0, 1.25, 1.5 on the Champion passive (BL for step B), not the class-wide
`Warrior` Peck values; every cell uses `FreshBiteShare` 0 except the two bite-on scenarios (anchor with share 1 at multipliers 1.0 and 1.5). About 15 scenarios.

**Run C, the reworked Champion** (`whatif-warrior-champion.json`, `"roster":{"Relentless":"Champion"}`; the Thief values of Maestro's 2026-11-25
note, bounty 8, steal x1.6, capacity 16, in every baseline). Baseline: the Champion as shipped (kit Peck, WingSlam, CluckShock, Snatch;
`FreshSeconds` 0, `TrophySteal` 0) at the shipped foraging F0. Foraging levels (class `Warrior`, placeholders until the running measurement names
the lever; replace F1 and F2 with its two best values, keep F0):

| Level | `Warrior.PeckCooldown` | `Warrior.PeckAmount` |
|---|---|---|
| F0 shipped | 0.71 | 3 |
| F1 slower Peck | 0.95 | 3 |
| F2 smaller bite | 0.71 | 2 |

Champion cells, crossed with the three levels:

| Cell | `FreshSeconds` | `TrophySteal` | `TrophyCap` | `TrophySeconds` | Why |
|---|---|---|---|---|---|
| K kit only | 0 | 0 | 3 | 6 | the kit and the foraging alone (at F0 it is the baseline itself) |
| B bite on | 1.0 | 2 | 3 | 6 | the anchor with `FreshBiteShare` 1 (the old reading: the victim's `PeckAmount` as food); all the other cells use share 0 |
| H hits only | 0 | 2 | 3 | 6 | trophies from hits, no bite: what the bite adds |
| A anchor | 1.0 | 2 | 3 | 6 | the design |
| T1 | 1.0 | 1 | 3 | 6 | a gentler cash (worst combo 11, section 4.5) |
| T3 | 1.0 | 3 | 3 | 6 | a harder cash |

That is 14 scenarios plus the baseline (F0 x K), about 6 min. A second run (6 scenarios) only if a cell is within 5 points of the band: at that cell
and level, `FreshSeconds` 0.6 and 1.5, `TrophyCap` 2 and 4, `TrophySeconds` 4 and 9. The report adds per scenario the section 8.3 Champion row,
and the Bully's row too, because F1 and F2 change the Warrior class.

Example delta: `{"schema":1,"assets":{"Champion":{"FreshSeconds":1.0,"TrophySteal":2},"Warrior":{"PeckCooldown":0.95}}}`.

Go when all hold (the oracle is a guide; Maestro decides): each Warrior kit 20 to 30 % under P0, P1 and mixed; no non-Warrior kit moves more than 5
points; clock share at most 12 %; worst 5 s window mean at most 6 food (a forgiving children's game, CODE-DESIGN-05); P0 and P1 within 10 points of
each other for each Warrior kit. Shipped references (match report 2026-11-24): Bully 19.9 / 13.7 / 15.3, Relentless 6.1 / 7.1 / 8.9. For the
Champion also: bites at least 2 a match under mixed (the break managed 0.3), and identity food at least a fifth of his banked food, or the rule is
not his identity.

## 10. Build plan: simulator slices

Common to every slice: read `docs/ARCHITECTURE_RULES.md` ARCH-DATA-11, ARCH-DATA-14, ARCH-NET-07, ARCH-LAY-01/02 and CODE-TEST-13/15; other sessions share
the tree, so check `git status`, stage by explicit path, never `git add -A`, stash, reset or broad checkout. **Gate** (every slice): compile clean
(`EditorUtility.scriptCompilationFailed` false), then the slice's tests, then the full EditMode suite through `TestRunnerApi` from `script-execute`
with the polled result file; if no Editor is open, `Unity.exe -runTests -batchmode -projectPath . -testPlatform EditMode -testResults results.xml`. Never
both at once (the batch lock). Each commit ends with the co-author line. Backlog id: take one with `/backlog` before slice 1 and put it in every message.

**S1. Pure rules and passives** (no simulator change).
Files: `Gameplay/HitStealRules.cs`, `Gameplay/PeckBreakRules.cs`, `Gameplay/TrophyRules.cs`; `Abilities/PassiveAbilitySO.cs` (three virtuals);
`Abilities/Passives/BullyPassiveSO.cs` (four fields, `HitSteal` override); `Abilities/Passives/ChampionPassiveSO.cs` and
`Data/Abilities/Passives/Champion.asset` (made in the Editor, not registered). Tests `HitStealRulesTests`: `Take_ClampsToRoomAndVictim`,
`Take_LockedOrZeroAmount_IsZero`, `AmountFor_PeckJumpAndEveryOtherJob_IsZero`; `PeckBreakRulesTests`: `CanBreak_NeedsCastReachNoWallNoEggNoImmunity`,
`CanBreak_ReachZero_IsNeverTrue`, `Bite_IsTheSmallestOfVictimBitePileAndRoom`, `Resolve_BreakThenPileThenCancel`, `Nearest_PicksTheSmallestDistance_MinusOneForNone`;
`TrophyRulesTests`: `Earn_StopsAtTheCap`, `LoseOne_NeverBelowZero`, `Room_IsTrophiesTimesRoom`, `StealEarns_AtOneFood_NotBelow`, `Overflow_IsTheExcessOnly`;
`WarriorPassiveDataTests`: `ShippedBully_HitStealIsOff`, `Champion_IsNotRegistered_AndItsKitIsLegal` (`FixedKit.Problems` empty, same four assets as
Relentless), `Delta_CanSetEveryNewField_AndTheBullyKit` (`BalanceBook.Evaluate` accepts, nothing ignored). Commit:
`feat(balance): hit-steal, peck break and trophy rules with their passive fields, shipped off (BL-xxx)`.

**S2. Roster swap for the what-if.**
Files: `Balance/OracleRoster.cs` (`static AbilityRegistrySO Swap(AbilityRegistrySO shipped, PassiveAbilitySO outgoing, PassiveAbilitySO incoming)`,
caller destroys the copy); `Balance/MatchWhatIf.cs` (`WhatIfSpec.Roster`, a name-to-name map, parsed from `"roster"`); `Balance/OracleKits.cs`
(`LabelOf`); `Editor/OracleMenu.cs` `RunWhatIf` (load the named passives with `AssetDatabase`, swap, build the book over the copy, write the swap into
the report header). Tests `OracleRosterTests`: `Swap_ReplacesOneSeat_KeepsEveryOtherEntry`, `Swap_LeavesTheShippedRegistryUntouched`,
`Swap_RefusesAPassiveOfAnotherClass_OrOneNotInTheRegistry`; `WhatIfSpecTests.Roster_ParsesAndRejectsAnythingButAnObjectOfNames`;
`OracleKitsTests.LabelOf_ShippedPassivesKeepTheirSubclassName_TheSwapReadsWarriorChampion`. Commit:
`feat(balance): what-if roster swap so an unregistered passive can take a seat (BL-xxx)`.

**S3. The Bully in the simulator.**
Files: `MatchKit.cs`, `MatchKits.cs`, `MatchSim.cs` (`HitSteal`, locks, `Robbed` stub with no trophies yet), `MatchCombat.cs` (counters). Tests
`MatchWarriorTests`: `Stun_TakesItsAmount_IntoTheBullysCargo`, `Shove_OnAnImmovableVictim_TakesNothing`, `TheLock_RefusesTheSecondHitter_UntilItEnds`,
`AStunThatDoesNotExtend_TakesNothing_AndStartsNoLock`, `OneCast_ThreeVictims_ThreeTakes_ClampedToTheRoomLeft`, `Egg_RefusesTheHit_AndTheTake`,
`EmptyVictimOrFullBully_NoTake_NoLock`, `Peck_NeverTakes`, `ShippedOff_PlaysExactlyAsBefore` (Bully half), `Sim_CallsTheSharedRules_NotCopies` (Bully
half). Commit: `feat(balance): Bully hit-steal in the match simulator (BL-xxx)`.

**S4. The Champion in the simulator.**
Files: `MatchPolicy.cs` (`MatchIntent.Break/BreakTarget`, `SeatView` fields), `MatchSim.cs` (`CapacityOf`, trophies, `ResolveBreak`, `SpillOverflow`,
`CutCause.Break`, events), `MatchCombat.cs` (counters, `SlidingWindowSum`). Tests `MatchWarriorTests`: `Break_CutsTheCast_TakesTheBite_RestStaysInThePile`,
`Break_AfterTheCastEnded_FallsBackToAPilePeck_ElseCancelsAndSpendsNothing`, `Break_SetsBreakCooldown_AndTheVictimsImmunity`, `Break_WalledOrImmune_IsRefused`,
`TwoBreakersOneVictim_TheSecondFallsBack`, `Trophies_FromBreaksAndSteals_OnePerCast_CappedAtTheCap`, `Robbed_LosesOne_AndTheExcessGoesToTheCentrePile`,
`Executed_LosesAll_NothingOverflows`, `Banking_KeepsTrophies`, `EveryCapacityRead_GoesThroughCapacityOf`, `ShippedOff_PlaysExactlyAsBefore` (Trophy
half), `SlidingWindowSum_IsTheMostInAnyWindow`. Commit: `feat(balance): Champion peck break, trophies and overflow in the match simulator (BL-xxx)`.

**S5. Bot rules and both policies.** Starts only after the BL-221 session has committed `ScriptedPolicy.cs`, `BotTactics.cs` and `BotController.cs`.
Files: `Gameplay/BotHitStealRules.cs`, `Gameplay/BotBreakRules.cs`; `Balance/ScriptedPolicy.cs`; `Balance/UtilityPolicy.cs`; `MatchSim.BuildView`.
Tests `BotWarriorRulesTests`: one per function of section 7 at its edges (`CastForFood_AtOneFood`, `PickHitter_TieGoesToTheShorterCooldown`,
`WorthHunting_SkipsAPreyLockedPastTheWalk`, `Candidate_NeedsTheCastToOutlastTheReaction`, `Approach_OnlyWhenTheWalkBeatsTheCast`,
`ShouldBreak_AtAPileOnlyWhileATrophyIsLeft`, `ReturnFraction_GrowsWithTheTrophyRoom`); `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem`
extended; `ScriptedPolicyTests.P0Bully_CastsStunAtALoadedRival_NotAtAnEmptyOne`, `P0Champion_BreaksARivalCastingInReach`;
`UtilityPolicyTests.P1_BreaksOnlyWhenTheValueBeatsItsOwnPeck`. Commit: `feat(balance): Bully and Champion bot rules in P0 and P1 (BL-xxx)`.

**S6. Report, what-if numbers and the two runs.**
Files: `MatchReport.cs`, `MatchStats.cs` (if a label helper is needed), `MatchWhatIf.cs` (`warrior` group, `BullyKit`, `TrophyKit`); the two what-if
files in `Captures/oracle/` (not committed). Tests `MatchReportTests.WarriorBlock_ShowsEveryCounterOfSection8`, `MatchWhatIfTests.Numbers_CarryTheWarriorGroup`.
Then run A and run B (`-executeMethod CluckWars.EditorTools.OracleMenu.MatchWhatIfBatch -oracleWhatIf <file>`), and write the results into this spec
as a new section 14 (results). Commit (code and spec only): `feat(balance): Warrior kit rows in the match report and what-if (BL-xxx)`.

### 10.1 Rework slices R1 to R4 (the redesigned Champion, section 4)

For an implementer who has not seen the design talk: read sections 4, 7 and 8 of this spec first; the Bully (section 3, slice S3, `HitStealRules`,
`BotHitStealRules`) is **not touched**. Simulator only: `Champion.asset` stays unregistered, every new number ships off, the game plays exactly as
before. Same rules, gate and git discipline as the S slices above (the common paragraph of section 10); another session may be running Unity in
batch mode, so check for the project lock before any run and never start a second one. Backlog id: take one with `/backlog` (the S slices used
BL-222) and put it in every message. Each slice compiles and passes the full suite on its own.

**R1. Pure rules, bot rules and the passive (additive: nothing is deleted yet).**
Files: new `Gameplay/BiteRules.cs` (section 8.1); `Gameplay/TrophyRules.cs` gains `Add`, `StealAmount`, `Decayed` and `TrophyFacts` gains
`Steal`, `Seconds` (the old members stay until R2); new `Gameplay/BotChampionRules.cs` (every function of section 7); `Abilities/PassiveAbilitySO.cs`
gains `virtual BiteFacts Bite => BiteFacts.None`; `Abilities/Passives/ChampionPassiveSO.cs` gains `FreshSeconds` 0, `TrophySteal` 0,
`TrophySeconds` 6 (`TrophyCap` 3 stays) with tooltips, overrides `Bite` and fills the new `TrophyFacts` fields; `Data/Simulator/Champion.asset`:
kit Peck `2b6f2fba609676f439ec424dfd05e5f6`, WingSlam `6bbb9c61252f875408fde062c994669c`, CluckShock `079c14ab0b344ca898f4be63fbf58374`,
Snatch `4c026dfa11fd40cd9914aa8fb37090b6`, and the new fields at their shipped values (in the Editor; the class summary and its remarks name the new
kit and rule).
Tests: `BiteRulesTests.Fresh_MidCastOrJustLandedWithFood_NotBitten` (casting true; landed 0.9 s ago with food at window 1.0 true, 1.1 false; landed
with no food false; bitten false), `Fresh_FreshSecondsZero_IsNeverFresh_EvenMidCast`, `FromCast_IsTheSmallestOfBitePileAndRoom` (3 / 2 / 10 is 2;
3 / 10 / 1 is 1; any negative is 0), `FromCargo_IsClampedToRoomAndVictim` (3, room 2, cargo 10 is 2; cargo 1 is 1);
`TrophyRulesTests.Add_StopsAtTheCap`, `StealAmount_IsBaseTimesFactorPlusPerTrophyTimesCount` (5, 1, 2, 3 is 11; count -1 is 5), `Decayed_AfterTheUntilOnly`;
`BotChampionRulesTests.FreshLeft_IsCastLeftPlusWindow_OrWindowMinusSinceLanded_ZeroWhenBitten`, `HitFresh_NeedsTheWindowToOutlastTheReaction`,
`PickBuilder_ShoveFirst_MinusOneWhenNoneReady`, `Cash_AtTheCap_OrWhenNoBuilderBeatsTheDecay_NeverAtCountZero`, `StealGain_IsTheInflatedAmountClamped`,
`Fight_OnlyWhenTheFightBeatsThePeckRate` (a slower Peck flips the same fight from false to true); `WarriorPassiveDataTests.Champion_IsNotRegistered_AndItsKitIsLegal`
updated (the four assets above, `FixedKit.Problems` empty), `ShippedChampion_BiteAndTrophyStealAreOff` (`Bite.Active` false, `Trophy.Steal` 0),
`Delta_CanSetEveryNewField` (`FreshSeconds`, `TrophySteal`, `TrophyCap`, `TrophySeconds` through the roster swap as the existing test does, and
`Warrior.PeckCooldown` / `PeckAmount`; `BalanceBook.Evaluate` accepts, nothing ignored). An S4 test that depended on the old kit (Speed, Slow) is
fixed or left to R2, which rewrites it. Commit: `feat(balance): Champion bite and per-enemy trophy rules, kit Peck Stun Shove Steal, shipped off (BL-xxx)`.

**R2. The simulator swap: the bite and the counts in, the break and the room out.**
Delete: `Gameplay/PeckBreakRules.cs` and `Editor/Tests/PeckBreakRulesTests.cs`; `Gameplay/BotBreakRules.cs` and its four tests in
`BotWarriorRulesTests` (`Candidate_*`, `Approach_*`, `ShouldBreak_*`, `ReturnFraction_*`); `PassiveAbilitySO.PeckBreak`; `ChampionPassiveSO`
`BreakReach`, `BreakCooldown`, `BreakImmuneSeconds`, `TrophyRoom`, `TrophiesClearOnBank` (reserialize `Champion.asset` so the stale keys go);
`TrophyFacts.Room`, `ClearOnBank`; `TrophyRules.Earn`, `LoseOne`, `Room`, `StealEarns`, `Overflow`; everything listed as removed in section 8.2;
the break branches of `ScriptedPolicy` (`_candidate`, `_approachDist`, `_breakTarget`, `_approach`, the `ReturnFraction` call) and `UtilityPolicy`
(`PeckCut`'s break option), so the Champion bot plays its plain kit until R3; in `MatchWarriorTests` every `Break_*` test,
`TwoBreakersOneVictim_TheSecondFallsBack`, `BreakWithoutTheKit_IsARejectedIntent`, `Trophies_FromBreaksAndSteals_OnePerCast_CappedAtTheCap`,
`Robbed_LosesOne_AndTheExcessGoesToTheCentrePile`, `Executed_LosesAll_NothingOverflows`, `Banking_KeepsTrophies`, `Banking_ClearsTrophies_WhenThePassiveSaysSo`;
`ScriptedPolicyTests.P0Champion_BreaksARivalCastingInReach`; `UtilityPolicyTests.P1_BreaksOnlyWhenTheValueBeatsItsOwnPeck`.
Add: section 8.2 "Added" in `MatchKit.cs`, `MatchKits.cs`, `MatchPolicy.cs` (`SeatView`), `MatchSim.cs`; the section 8.3 counters in
`MatchCombat.cs`; the Champion row of the "Warrior kits" block in `MatchReport.cs` and the `warrior` group of `MatchWhatIf.Numbers`.
Tests (`MatchWarriorTests`, each on a hand-built seat layout with fixed positions):
- `Bite_MidCast_CutsTheCast_TakesFromThePile_RestStaysInThePile`: victim pecking a pile of 2, Champion's Shove lands: victim `Pecking` false,
  cargo unchanged, `NextPeckAt` unchanged; Champion +2; pile 0; `BitesFromCast` 1.
- `Bite_StunCutsFirst_TheSnapshotStillBites`: the Stun's own `Cut(Stun)` runs and the bite still comes from the pile (guards step 1 of 4.2).
- `Bite_JustLanded_TakesFromTheCargo_ClampedToRoom`: landed 0.5 s ago with food, Champion room 2: Champion +2, victim -2, `BiteLeftWithVictim` 1.
- `Bite_AfterTheWindow_OrAfterALandingWithNoFood_TakesNothing`: 1.1 s after a landing at window 1.0; a landing that took 0: no bite, hit trophy only.
- `Bite_OncePerFreshState_TheSecondHitOnlyEarnsItsTrophy`: Stun then Shove inside the window: one bite, count 3 (bite, Stun, Shove), `BitesSpent` 1.
- `Bite_EggRefuses_ShieldWalkLands`: egg: no bite, no trophy; walk phase: bite and trophies.
- `Steal_OnAFreshCarrier_BitesFirst_ThenTakesTheInflatedAmount`: count 2, fresh just-landed victim with 20 cargo, Champion empty: bite 3, count 3,
  Steal takes 5 + 2 x 3 = 11, count 0, `TrophyBonusFood` 6, victim -14.
- `Steal_ThatTakesNothing_LeavesTheCount`: Champion full: take 0, count kept.
- `Trophies_PerEnemy_CappedAndDecayAllAtOnce`: two rivals counted apart; the cap holds; a hit at the cap restarts the timer; `TrophySeconds` with no
  hit puts the count to 0 in one step (`TrophiesDecayed` = the count).
- `OneStun_ThreeFreshVictims_InSeatOrder_ClampedToTheRoomLeft`: room 5, three fresh victims: bites 3, 2, 0 in seat order, three counts of 2.
- `TwoChampions_OneFreshVictim_TheFirstBites_BothEarnTheHitTrophy`.
- `Executed_ClearsEveryCountOnAndOfTheDead`.
- `ShippedOff_PlaysExactlyAsBefore` (Champion half rewritten as in 8.4), `Sim_CallsTheSharedRules_NotCopies` (as in 8.4, including the "finds no
  `PeckBreakRules`" half), `EveryCapacityRead_GoesThroughCapacityOf` kept.
- `MatchReportTests.WarriorBlock_ShowsEveryCounterOfSection8` and `MatchWhatIfTests.Numbers_CarryTheWarriorGroup` updated to the 8.3 Champion row.
Commit: `refactor(balance): Champion bite and per-enemy trophies replace the peck break and the trophy room in the match simulator (BL-xxx)`.

**R3. Bot rules in P0 and P1.**
Files: `Balance/ScriptedPolicy.cs` (the P0 order of section 7), `Balance/UtilityPolicy.cs` (`BestHit` for the Champion), `MatchSim.BuildView`
(the 8.2 `SeatView` fields). Tests: `ScriptedPolicyTests.P0Champion_HitsAFreshRivalInReach_NotAStaleOne`, `P0Champion_CashesAtTheCap_AndBeforeTheDecay`,
`P0Champion_KeepsPeckingWhenNoFightBeatsThePeck`, `P0_CallsTheGamesOwnFunctions_NotCopiesOfThem` (lists `BotChampionRules`, no `BotBreakRules`);
`UtilityPolicyTests.P1Champion_PrefersAFreshRivalOverThePile_OnlyWhenThePeckIsSlower` (same layout at F0 and F1, the choice flips).
Commit: `feat(balance): Champion bot rules for fresh rivals and trophy cashing in P0 and P1 (BL-xxx)`.

**R4. Run C and its results.** Write `Captures/oracle/whatif-warrior-champion.json` (section 9 run C; not committed), run it
(`-executeMethod CluckWars.EditorTools.OracleMenu.MatchWhatIfBatch -oracleWhatIf <file>`, only when no other Unity holds the project), and add the
results as a new section 15 of this spec in the shape of section 14.2 (one table, the 8.3 Champion columns plus the Bully's win rates). Commit (spec
only): `docs(spec): run C results for the reworked Champion (BL-xxx)`.

## 11. Game slices (after Maestro approves; not planned here)

- **Bully hit-steal:** the take through `ChickenCargo.StealFrom` / the steal RPC from the Stun and Shove landing sites (ARCH-DATA-05, ARCH-NET-07); the
  magnitude bound `StealRules.MaxSingleSteal` must cover the passive's hit amounts, since WingSlam and CluckShock declare no steal (ARCH-NET-05); the
  lock on the Bully's authority, per victim (`TargetCooldowns<NetworkBehaviourId>` fits, which the loot-mark spec section 10 plans to delete: keep it);
  a hit-steal event id (ARCH-EVT-02) for the pops; the "affected" test of a stun must use the same `IncomingExtends` the simulator uses. Kit change in
  `Bully.asset`.
- **Champion (rework):** the hit sites of Stun, Shove and Steal on the caster's authority read the victim's fresh state (cast running, or a
  `[Networked]` landing tick and a bitten flag on the victim, ARCH-NET-15) and send a bite claim: mid-cast to the victim's authority, which cuts the
  cast and lets the pile authority debit the bite (ARCH-NET-07; `ChickenCargo.MaxPeck` bounds it); just landed through the steal path
  (`StealRules.MaxSingleSteal` must cover the bite and the inflated Steal, `StealAmount + TrophySteal x TrophyCap`, ARCH-NET-05). Per-enemy counts
  on the Champion's authority (a small `[Networked]` array of bytes with their timers), a bite and a cash event id (ARCH-EVT-02); no capacity
  change and no overflow transfer. `NetworkProtocol.Version` 2 to 3 (ARCH-NET-21).
- **Roster:** Champion registered in place of Relentless; `ChickenSubclass` value 1 renamed (or a new value with `ChickenSubclassFlags`,
  `ContractsAndEnumsTests`, `ValidateRoster`); Relentless stays as an unregistered asset.
- **Bots:** `BotController` calls `BotHitStealRules` and `BotChampionRules` (the drift guard of 8.4).
- **Text and feedback:** perk lines with numbers from the fields (ARCH-DATA-12), `UiKeys`/`UiText.csv`, section 6, `docs/FEEDBACK.md`, the site
  (`docs/site/index.html`), GDD; `librarian` for `docs/FILE_STRUCTURE.md` and the rules docs. `tools/balance/schema.py` `DENIED` unchanged (no new denied field).

## 12. Decisions taken without Maestro

Items 3, 7, 8, 9, 12 (the Champion half) and 14 concern the superseded break and capacity trophies (section 4.6) and are kept as the record only.

1. Champion's slow ability: Slow (from the designer), as the brief said.
2. Trophy cap 3; reset each round. Banking: kept (shipped) or cleared by `TrophiesClearOnBank`, both measured (Maestro, 2026-11-25, replaces "kept through banking").
3. A break earns a trophy even when no food fits; a Steal earns one only at 1 food or more (as written).
4. A stun that does not extend a longer stun, a shove that moves nobody, a take of 0: none counts as "affected", and none starts the lock.
5. Slow Area aura ticks never steal; `HitStealSlow` covers the instant Slow only.
6. Hit-steal food counts as stolen food in the report, not as Steal casts.
7. No retarget at release: the break acts only on the previewed rival.
8. A landed break ends Hide and cancels a Shield egg like any move; it spends no ability slot.
9. A Champion is not robbed of a trophy by a break against him (a break takes from the pile, not from him).
10. Overflow never raises the centre pile's maximum.
11. Simulator access to the Champion through a what-if roster swap over an unregistered asset (section 5), not a game or `BalanceBook` change.
12. Bot: the Bully casts for food at 1 or more, Shove first on a tie; the Champion breaks at a pile only while a trophy is left, and banks at
    12 plus its trophy room. The shipped Champion values (reach 0, room 0) make the run-B baseline "Relentless's kit with no passive".
13. Go measures of section 9 (20 to 30 % per Warrior kit, worst 5 s window at most 6).
14. **Maestro, 2026-11-25 (closes open question 1):** `BreakCooldown` gates breaks only. After a break the Champion's next pile Peck is ready on his
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
  and earns its trophy.
- C4. "Just landed" opens only on a landing that took food; a Peck cut by someone else opens nothing.
- C5. Freshness is read before the hit's own effect, so a Stun that cuts the cast still bites from the pile.
- C6. Trophy count: +1 per hit that affected that rival (Stun, Shove; the Steal that consumes them adds none), +1 per bite; the bite of a Steal
  counts toward that same Steal. Steal takes `StealAmount x StealFactor + TrophySteal x count` (the bonus is not multiplied), then the count is 0
  if the take was above 0. Cap `TrophyCap`; a hit at the cap restarts the timer; the count decays to 0 at once after `TrophySeconds` without a
  +1; all counts clear when the Champion is executed, and the counts on a victim clear when the victim is executed. Starting values:
  `FreshSeconds` 1.0, `TrophySteal` 2, `TrophyCap` 3, `TrophySeconds` 6.
- C7. Being robbed no longer costs a Champion a trophy (the "lose one" rule leaves with the room).
- C8. Multi-victim casts resolve victim by victim in the seat loop order already used, each take clamped to the room left.
- C9. The foraging lever is class-wide (`Warrior.asset`, a plain delta, no code), so it also moves the Bully and the bite taken from a Warrior.
- C10. The fresh glint and the trophy pips are seen by the Champions and by the chicken concerned, not by the other players.
- C11. The Bully and the Champion both exist; the Bully's hit-steal rule is unchanged by this amendment.

## 13. Open questions (real forks)

1. ~~Is a break meant to cost food?~~ **Closed by Maestro, 2026-11-25:** `BreakCooldown` gates breaks only; after a break his next pile Peck runs on the
   normal Peck cycle (sections 4.1, 4.3, 9, 12 item 14).
2. ~~Online timing of the break.~~ **Superseded** with the break. Its successor: the fresh window online. The window is the cast plus about 1 s, so a
   hit's round trip fits it on the relay, but a hit landing in the last tens of milliseconds will be judged after the state ended. Should the victim's
   authority judge freshness at the hit's sender tick (a lag allowance, as `ChickenCombat.SenderLatency` gives other claims)? Proposed: yes.
3. **The combo against the forgiving limit.** One full combo takes 14 food from one victim in about 2 s (section 4.5), 24 at the ceiling of a 5 s
   window, against the go limit of 6 for the mean. Is a rare 14 acceptable if the mean stays under 6, or should `TrophySteal` or `TrophyCap` be held
   so a combo never exceeds a number Maestro names (with `TrophySteal` 1: 11)?
4. **Whose foraging goes down.** The lever is written class-wide (C9), which also slows the Bully and makes Warrior victims give a smaller bite. If
   only the Champion should forage less, the lever moves to the passive (a Peck cooldown factor on `ChampionPassiveSO`, one new field and one hook);
   say which.

## 14. Results of the simulator runs (2026-11-25)

Both runs: `replicateScale` 0.4, seed `0x1352902`, 12 threads, `bal=5465b825`, with Maestro's Thief values of 2026-11-25 (bounty 8, steal x1.6, capacity 16) in every baseline.
Result files (not committed, `Captures/oracle/`): run A `match-whatif-20261125-114515.md` / `.json` from `whatif-warrior-bully.json` (12 scenarios, 118 560 matches, 5.4 min);
run B `match-whatif-20261125-115253.md` / `.json` from `whatif-warrior-trophy.json` (15 scenarios, 145 920 matches, 6.7 min). Each file holds every kit's win rate for P0, P1 and
mixed, the counters below, and the per-kit movement tables. The baseline of run A is the shipped game (Relentless and the old Bully, Snatch kit); the baseline of run B seats the
Champion with its shipped numbers (reach 0, room 0) in Relentless's seat.

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

### 14.2 Run B, the Champion (kit Peck, Speed, Steal, Slow)

Win rates, P0 / P1 / mixed: baseline (no break, room 0) 10.6 / 25.1 / 22.0; every cell of the reach x cooldown grid 9.8 / 23.9 to 25.0 / 19.5 to 20.0. **The break barely happens:** 0.24 to 0.33
a match under the mixed population, and apparently none in P0 (its win rate is identical in every grid cell, so the reach and cooldown never touched a match). The cast lasts 0.4 s and the bot needs a think plus its 0.3 s reaction, so
a candidate exists for about the first 0.1 s of a cast (`BotBreakRules.Candidate`: cast left above the reaction); the reach and cooldown axes change nothing measurable. The room
is barely used either: the bot banks at 12 plus its trophy room (at most 18 with room 2 and 3 trophies), under the class capacity of 20, so only a bite or a steal that overshoots uses room
(8 to 11 % of it). The trophy lifetime reading changes the held average, not the win rate: **streak** (cleared on bank) holds 0.39 trophies on average and 0.69 at the end of a match
(room 2) against 0.56 and 1.19 kept; room 3 is the same. **Slow growth** (room 1) holds 0.56 and 1.25 at cap 4, the same at cap 6 (nobody gets past 3). Win rates: streak room 2 9.4 / 24.6 / 20.2,
streak room 3 8.8 / 25.4 / 20.0, slow growth room 1 cap 4 12.5 / 24.8 / 21.5, cap 6 12.3 / 24.8 / 21.5, original (room 2, cap 3, kept) 9.8 / 24.4 / 20.0. No other kit moves more than about 2 points in any
Champion scenario. Worst 5 s window for a victim: mean 6.1 to 6.2, 95th percentile 10, against 5.79 and 10 without the break: the Steal in the kit sets it, not the break.

## Overview: the Champion (Warrior_Champion), per match in the mixed population unless said

| Scenario | P0 | P1 | mixed | breaks per match | break food per match | break presses that fell back to a Peck per match | break presses cancelled per match | trophies earned per match | trophies held (mean over the match) | trophies at the end of the match (mean) | share of the trophy room used when banking (%) | overflow to the centre pile per match | identity food per match | worst loss for a victim in 5 s (mean) | worst loss for a victim in 5 s (95th percentile) |
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
- Champion: as built (the bot rules of section 7, the 0.4 s cast) the break is a rare event and the kit plays as Relentless's, 10 / 24 / 20. To make the break matter, the window has to
  open (a longer cast for the victim, a reaction-free press online as in open question 2, or the bot pressing on the approach), and the bot has to bank later to fill the room.

## 15. Results of the reworked runs (2026-11-25, evening; BL-222)

Slice R4 as run after the bot fixes. Both runs: `replicateScale` 0.4, seed 20261122, 12 threads, the shipped game as baseline re-run after BL-224 (a Brawler with no Steal and no hit that pays forages) and BL-225 (P1 values a cast by its take), so the baselines are **not** comparable with section 14: P1 moved most (shipped Bully 33 to 44, Relentless 26 to 32). Files (not committed): `Captures/oracle/match-whatif-20261125-135008` (Bully, `whatif-e-bully.json`), `-140227` (Bully, stun 1 / shove 1 at the best multiplier), `-140450` (Bully kit only, hit-steal 0), `-140017` (Champion, `whatif-e-champion.json`, `"roster":{"Relentless":"Champion"}`), each `.md` and `.json`. Foraging is the new per-kit lever `PeckCooldownMultiplier` (BL-223), not the class-wide `Warrior` Peck values. Win rates are points over the seats a kit sat in; the Champion rows use `FreshBiteShare` 0 except the two B rows.

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

### 15.2 Run C, the reworked Champion (kit Peck, WingSlam, CluckShock, Snatch)

K kit only (fresh 0, trophy steal 0), H hits only (trophy steal 2, fresh 0), A anchor (fresh 1.0, trophy steal 2, cap 3, 6 s), T1 and T3 (trophy steal 1 and 3), B the anchor with `FreshBiteShare` 1 (the old reading, the victim's whole `PeckAmount` as food). The baseline is the Champion as shipped at multiplier 1.0, which is the K cell at 1.0. "Fresh hits" are the stacks gained from fresh rivals; bite food is 0 wherever the share is 0; identity food is the trophies' bonus food plus bite food.

| Scenario | P0 | P1 | mixed | fresh hits per match | bites per match | bite food per match | Pecks bitten per match | trophies earned per match | trophies consumed per cash | Steals per match | mean Steal size | identity food per match | worst loss for a victim in 5 s (mean) | worst loss for a victim in 5 s (95th percentile) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| baseline | 8.7 | 31.9 | 25.5 | 0.00 | 0.00 | 0.00 | 0.00 | 3.88 | 1.41 | 1.88 | 4.51 | 0.00 | 4.83 | 10.00 |
| K kit only, Peck x1.25 | 8.1 | 14.2 | 12.1 | 0.00 | 0.00 | 0.00 | 0.00 | 4.11 | 1.41 | 2.05 | 4.48 | 0.00 | 5.12 | 10.00 |
| K kit only, Peck x1.5 | 6.1 | 3.8 | 5.6 | 0.00 | 0.00 | 0.00 | 0.00 | 4.08 | 1.37 | 2.14 | 4.45 | 0.00 | 5.01 | 10.00 |
| H hits only (trophy steal 2, no fresh), Peck x1.0 | 28.0 | 33.5 | 29.9 | 0.00 | 0.00 | 0.00 | 0.00 | 3.45 | 1.38 | 1.70 | 5.51 | 1.58 | 5.82 | 14.00 |
| H hits only (trophy steal 2, no fresh), Peck x1.25 | 18.9 | 17.3 | 16.4 | 0.00 | 0.00 | 0.00 | 0.00 | 3.71 | 1.39 | 1.86 | 5.58 | 1.81 | 6.29 | 14.00 |
| H hits only (trophy steal 2, no fresh), Peck x1.5 | 17.9 | 5.5 | 10.8 | 0.00 | 0.00 | 0.00 | 0.00 | 3.76 | 1.34 | 1.98 | 5.56 | 2.03 | 6.38 | 14.00 |
| A anchor (fresh 1.0, trophy steal 2), Peck x1.0 | 33.6 | 35.6 | 31.2 | 0.71 | 0.00 | 0.00 | 0.00 | 4.16 | 1.62 | 1.69 | 5.95 | 2.22 | 6.24 | 14.00 |
| A anchor (fresh 1.0, trophy steal 2), Peck x1.25 | 22.0 | 17.5 | 18.9 | 0.82 | 0.00 | 0.00 | 0.00 | 4.50 | 1.66 | 1.85 | 5.96 | 2.48 | 6.57 | 14.00 |
| A anchor (fresh 1.0, trophy steal 2), Peck x1.5 | 19.9 | 5.2 | 12.5 | 0.88 | 0.00 | 0.00 | 0.00 | 4.64 | 1.63 | 1.95 | 5.89 | 2.64 | 6.60 | 14.00 |
| T1 (trophy steal 1), Peck x1.0 | 28.7 | 34.8 | 30.4 | 0.70 | 0.00 | 0.00 | 0.00 | 4.22 | 1.61 | 1.71 | 5.31 | 1.20 | 5.48 | 13.00 |
| T1 (trophy steal 1), Peck x1.5 | 14.2 | 6.0 | 8.7 | 0.87 | 0.00 | 0.00 | 0.00 | 4.72 | 1.62 | 2.00 | 5.30 | 1.51 | 5.88 | 13.00 |
| T3 (trophy steal 3), Peck x1.0 | 36.8 | 37.1 | 33.9 | 0.70 | 0.00 | 0.00 | 0.00 | 4.08 | 1.63 | 1.69 | 6.39 | 2.98 | 6.83 | 16.00 |
| T3 (trophy steal 3), Peck x1.5 | 23.0 | 7.4 | 13.9 | 0.87 | 0.00 | 0.00 | 0.00 | 4.60 | 1.63 | 1.96 | 6.42 | 3.67 | 7.29 | 16.00 |
| B bite on (share 1, old reading), Peck x1.0 | 36.0 | 39.9 | 33.6 | 0.75 | 0.75 | 2.17 | 0.00 | 4.18 | 1.61 | 1.59 | 5.87 | 4.17 | 6.65 | 14.00 |
| B bite on (share 1, old reading), Peck x1.5 | 24.2 | 8.7 | 16.2 | 0.94 | 0.94 | 2.73 | 0.00 | 4.73 | 1.59 | 1.79 | 5.76 | 4.92 | 6.81 | 14.00 |

### 15.3 What this says (for Maestro)

- **The P0 defect is closed.** A Brawler with no Steal and no hit that pays now forages: the Bully kit with the hit-steal at 0 plays 46 / 57 / 41 (P0 / P1 / mixed) at multiplier 1.0 instead of 1 to 7 %, and 27.5 / 32.9 / 26.9 at 1.4. The hit-steal on top of it adds nothing under P0 and costs about 6 points under P1 and 4 under mixed at 1.4 (26.9 / 27.1 / 22.4): at 2 food a hit it is a flavour, not a win-rate engine.
- **Bully: the best foraging lever is 1.4** (P0 26.9, P1 27.1, mixed 22.4: all three in the 20 to 30 band and within 0.2 points of each other; clock share under 3 %, worst 5 s loss 2.2 mean, 4 at the 95th percentile). 1.3 is a little high (28.1 / 35.7 / 28.5), 1.5 a little low (22.0 / 19.0 / 18.7). Stun 1 / Shove 1 at 1.4 is too weak (20.1 / 25.1 / 19.9; mean take 0.99); amounts 2 stay.
- **Champion, fresh hits and trophies: 1.0 is too strong, 1.25 too weak, so about 1.1.** At 1.0 the anchor is 33.6 / 35.6 / 31.2, T1 28.7 / 34.8 / 30.4 (worst 5 s mean 5.5, under the limit of 6) and H 28.0 / 33.5 / 29.9; at 1.25 the anchor falls to 22.0 / 17.5 / 18.9 and at 1.5 to 19.9 / 5.2 / 12.5. P1 collapses with a slower Peck because it never swaps the pile for a fight (kit only at 1.5: 3.8), P0 only halves, so the P0 / P1 gap re-opens at 1.5 (15 points at the anchor) and is within 5 at 1.0 and 1.25. T1 at a multiplier near 1.1 is the pick (trophy steal 1: worst loss 5.5 mean, 13 at the 95th percentile).
- **The trophies are the whole identity; the fresh stack is thin.** Hits on fresh rivals are only 0.7 to 0.9 a match (a 0.4 s cast and a 1 s window are rarely met by a bot), and the anchor with the stack on is only 1 to 6 points above H without it. The go measure "at least 2 bites a match" fails with or without food (the B rows, share 1, bite 0.75 a match, 2.2 to 2.7 food); identity food is 2.2 per match at the anchor against about 25 banked, short of a fifth.
- **The old bite is worth a few points, not a different kit.** B against A: +2.4 / +4.3 / +2.4 at 1.0, +4.3 / +3.5 / +3.7 at 1.5 (P0 / P1 / mixed), and 2 more food a match of identity; with the food off the Champion keeps most of it.
- **Gap against the other kits.** The shipped Steal kits keep a wide P0 / P1 gap (Relentless 10.2 / 32.3, the Champion kit only 8.7 / 31.9): P0, the game's bot, plays a plain Steal kit badly, and BL-225 lifted P1 for every Steal holder. Where P0 plays a rule that fights (H, A, T1, T3) the Champion's gap is 5 to 6 points. That is a bot question, not a number to tune.
- **Neighbours.** At multipliers up to 1.25 no non-Warrior kit moves more than about 4 points; at 1.5 Slippery, Featherfoot and Fatty_Bulwark gain 5 to 6 in P1 because a slow Champion leaves them the piles.
