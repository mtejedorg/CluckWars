# Warrior identity: the Bully's hit-steal and the Champion (design and simulator plan)

Date: 2026-11-25. Status: design; the simulator slices S1 to S6 are built (results in section 14) (amended by Maestro's ruling on `BreakCooldown`, section 12 item 14). Inputs: Maestro's decisions of 2026-11-25 (section 1, closed), the code read on
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

## 4. Rule 2: the Champion

**In one line (for a child):** catch a bird while it pecks and you steal its bite; every catch and every steal is a trophy, and each trophy makes your
beak bigger. Get robbed and you lose one.

### 4.1 The break

| Case | Result |
|---|---|
| Press Peck with a rival mid Peck-cast within `BreakReach` (centre to centre, `MatchCombat.InRange`), no wall between, rival not break-immune | the press previews that rival; a rival beats a pile; the nearest casting rival is the one previewed |
| Release with the previewed rival still casting and still breakable | the break lands at once: his cast is cut (`CutCause.Break`), nothing lands for him, his Peck cooldown stays spent |
| What the Champion gains | `min(victim's PeckAmount, food in the victim's pile, his free room)` from that pile; what does not fit **stays in the pile** (never the centre); no bonus food |
| After a break | only another break has to wait `BreakCooldown`; his next pile Peck runs on his normal Peck cycle (a break is never a sacrifice of his own foraging); the victim is immune to breaks for `BreakImmuneSeconds` |
| Release with the cast already over (landed, cut by someone else, pile empty) | a pile Peck if a pile with food is in reach (normal Peck, normal cooldown); else a visible cancel that spends nothing |
| No retarget | the release acts on the previewed rival only; another rival casting at release is not taken |
| Egg | refused (cannot happen today: a pecking chicken holds no live Shield; kept in the rule so a later change cannot open it) |
| Base wall between them | refused |
| Champion stunned, dashing or removed | the press does nothing (as any Peck) |
| Champion hidden or with a live Shield | the break is a move: it ends Hide and cancels the egg like any cast |
| Two Champions on one victim in one tick | the first in the rotating order breaks; the second meets an ended cast and falls back |
| Assassins | never victims (they do not peck) |

### 4.2 Trophies

| Case | Result |
|---|---|
| A break lands | +1, food or not (the cut is the feat) |
| A Steal cast whose takes add up to 1 or more | +1, once per cast however many birds it robbed |
| Cap | `TrophyCap`; at the cap a gain does nothing |
| Room | capacity = class capacity (after passive hooks) + `trophies x TrophyRoom`; no size change |
| A rival takes food from him (Steal, marked steal, hit-steal; take above 0) | -1, once per take event |
| Executed | all lost (after the killer drained his cargo, so nothing overflows) |
| Banking | `TrophiesClearOnBank` false (shipped): kept through banking (slow growth). true: all trophies cleared when he banks (a streak); the room they added leaves with them. Maestro, 2026-11-25: the lifetime is a measured choice |
| Capacity below cargo after a loss | the excess goes to the centre pile at once |
| Round reset | 0 |

Fields on the new `ChampionPassiveSO` (`TrophyFacts.ClearOnBank` carries the bank rule) (served through `PassiveAbilitySO.PeckBreak` and `PassiveAbilitySO.Trophy`):

| Field | Type | Shipped (off) | Designed | Sweep |
|---|---|---|---|---|
| `BreakReach` | float `[Min(0)]`, 0 = no break | 0 | 2.5 | 2.0, 2.5, 3.0 |
| `BreakCooldown` | float `[Min(0)]` | 2.5 | 2.5 | 1.5, 2.5, 3.5 (gates breaks only) |
| `BreakImmuneSeconds` | float `[Min(0)]` | 2.5 | 2.5 | none |
| `TrophyRoom` | int `[Min(0)]`, 0 = trophies add nothing | 0 | 2 | 1, 2, 3 |
| `TrophyCap` | int `[Min(0)]` | 3 | 3 | 3, 4, 6 |
| `TrophiesClearOnBank` | bool | false | measured both ways | streak (true) against slow growth (false) |

The asset `Passives/Champion.asset`: `Subclass` Warrior_Relentless (1), `AllowedClasses` Warrior, `Kit` Relentless's four assets, no
multipliers. It is **not registered**: the game cannot pick it, so it is off whatever its numbers.

### 4.3 Arithmetic to watch (estimate)

`BreakCooldown` gates breaks only (Maestro, 2026-11-25): a break does not touch the Peck cycle, so it never costs him pecking he could do himself.
A break is free to the Champion except for the walk to the rival and the press itself; what it costs is that a break is also a Peck press he did not
make at a pile (the press is either a break or a pile Peck, never both). The `BreakCooldown` sweep measures how often breaks come and what they add.

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

Champion, Warrior: the press preview brackets the casting rival (gold brackets, H1a reuse) instead of the pile; on a break `BREAK!` pop, the bite
flies pile to beak, a crack sound; trophy pips (0 to cap) beside the cargo bar, each pip adds its room segment to the bar; a lost pip pops off; overflow
food arcs to the centre pile; a release on nothing flashes the Peck hex (the visible cancel). Victim: his cast bar shatters with `BROKEN`, a shimmer
for the immunity seconds, a short buzz. Words by `narrative-designer`; colours in `FeedbackPaletteSO` (ARCH-LAY-15).

## 7. Bot rules (pure, `Assets/_Game/Scripts/Gameplay/`, facts in, decision out)

Called by `ScriptedPolicy` (P0) now and by `BotController` in the game slice; `UtilityPolicy` (P1) uses the same gates and adds its own value.

`BotHitStealRules` (Bully):
- `float Gain(float amount, float victimStealable, float freeRoom, bool locked)` = `HitStealRules.Take(...)` with no refusal: what one hit would take.
- `bool CastForFood(float bestGainInReach)`: cast a ready Stun or Shove when some rival in its reach would give at least 1.
- `int PickHitter(float stunGain, float shoveGain, float stunCooldown, float shoveCooldown)`: the larger total gain; a tie goes to the shorter cooldown (Shove), keeping the Stun for control.
- `bool WorthHunting(float gainIfUnlocked, float lockLeft, float walkSeconds)`: hunt a prey only if it would give at least 1 and its lock ends before the walk does.
- No new bank rule: at `ReturnAmount` 12 in a beak of 20 the Bully always has room for a take.

`BotBreakRules` (Champion):
- `bool Candidate(bool rivalCasting, float castLeft, float dist, float reach, bool walled, bool immune, float reaction)`: breakable now and still casting after the bot's reaction.
- `bool Approach(float dist, float reach, float castLeft, float moveSpeed, float reaction)`: outside reach, but walking the gap plus the reaction ends before the cast does.
- `bool ShouldBreak(bool candidate, float ownPileBite, int trophies, int cap)`: break when he cannot peck a pile himself now (`ownPileBite` 0), or while a trophy is still to win (`trophies < cap`).
- `float ReturnFraction(float profileFraction, float returnAmount, int trophies, int room, float capacity)`: `min(profileFraction, (returnAmount + trophies x room) / capacity)` when `returnAmount > 0`, else `profileFraction`. Without it the bot banks at 12 and the trophy room is never used.

P1 (simulator only): `PeckCut` gains a break option (gate `BotBreakRules.Candidate`, value bite + denial bite minus the own-peck loss of section 4.3);
`BestSteal`'s sibling `BestHit` rates Bully prey by `Gain / walk seconds`; capacity from `SeatView.Capacity`.

## 8. The simulator model

### 8.1 Pure rules (Gameplay, shared with the game later)

`HitStealRules.cs`: `struct HitStealFacts { float Stun, Shove, Slow, LockSeconds; float AmountFor(AbilityJob job); bool Active; static None }`
(`AmountFor` is 0 for every job but Stun, Shove, Slow); `static float Take(float amount, float freeRoom, float victimStealable, bool locked)`
through `StealMath.Clamp`; `static bool Locked(float now, float lockUntil)`.

`PeckBreakRules.cs`: `struct PeckBreakFacts { float Reach, Cooldown, ImmuneSeconds; bool Active => Reach > 0 }`;
`enum BreakOutcome : byte { None, Break, FallbackPeck, Cancel }`; `static bool CanBreak(bool casting, float dist, float reach, bool walled, bool egg, bool immune)`;
`static float Bite(float victimPeckAmount, float pileFood, float freeRoom)`; `static BreakOutcome Resolve(bool canBreak, bool pileInReach)`;
`static int Nearest(IReadOnlyList<float> distances)` (-1 for none).

`TrophyRules.cs`: `struct TrophyFacts { bool IsChampion; int Room, Cap; static None }`; `static int Earn(int held, int cap)`, `static int LoseOne(int held)`,
`static float Room(int held, in TrophyFacts f)`, `static bool StealEarns(float takenThisCast)` (at least 1), `static float Overflow(float cargo, float capacity)`.

`PassiveAbilitySO`: `virtual HitStealFacts HitSteal => HitStealFacts.None`, `virtual PeckBreakFacts PeckBreak => default`, `virtual TrophyFacts Trophy => TrophyFacts.None`.

### 8.2 Simulator types and call sites (`Assets/_Game/Scripts/Balance/`)

- `MatchPassive` (`MatchKit.cs`): `HitStealFacts HitSteal; PeckBreakFacts Break; TrophyFacts Trophy;` read in `MatchKits.ReadPassive`.
- `MatchIntent`: `bool Break; int BreakTarget;` (a bool flag, so an object initializer that leaves it out never means "break seat 0"); `Idle` and `SameAs` updated.
- `SeatView`: `float Capacity; int Trophies; float PeckCastLeft` (0 unless pecking); `float BreakImmuneLeft`; `float[] HitLockLeft` (own seat only, by rival).
- `Chick`: `int Trophies; float TrophySecondsAcc; float BreakImmuneUntil; float[] HitLockUntil` (by victim).
- `CutCause.Break` appended; `CauseName` gives `"break"`. `MatchEventKind` (sim enum in `MatchSim.cs`) gains `HitSteal, PeckBreak, Trophy, Overflow` at the end.
- `CapacityOf(int s)` = `Farm.CargoCapacity + TrophyRules.Room(...)`: the only reader of `Farm.CargoCapacity` in `MatchSim` (`Land`, `CanStartPeck`, `DoSteal`, `TakeLoot`, `Execute`, `BuildView`).
- `Control` (stun branch after `v.StunUntil` is set; slow branch after `ApplySlow`) and `Shove` (after the impulse, when above 0) call `HitSteal(caster, victim, job)`.
- `HitSteal(c, v, job)`: amount from `PassiveOf(c).HitSteal.AmountFor(job)`; take via `HitStealRules.Take`; moves cargo, `TakeLootShare` for a loot-carrying victim, sets the lock, counters, `Record(HitSteal)`, then `Robbed(v)`.
- `Robbed(v)`: `TrophyRules.LoseOne`, then `SpillOverflow(v)`; called from `DoSteal`, `TakeLoot` (take above 0) and `HitSteal`. `Execute` sets trophies to 0 after the drain.
- `SpillOverflow(s)`: `TrophyRules.Overflow(cargo, CapacityOf(s))` leaves the cargo into `_pile[OracleMapSO.CentreIndex]` (its max is not raised); counted.
- Steal branch of `Cast`: sums the takes of the cast; `TrophyRules.StealEarns` gives +1 when the caster's `Trophy.IsChampion`.
- Break: `Apply` turns `Intent.Break` into a `CastRequest { Break = true, Target }` resolved in the rotating pass of `ResolveCasts` with the casts.
  `ResolveBreak(s, target)`: gates as `CanCast` minus the slot (stun, dash, removed) plus `NextBreakAt` (the new per-seat break clock; `NextPeckAt` is
  neither required nor touched by a landed break, and a break is refused while his own Peck cast is running); `PeckBreakRules.CanBreak` on the target's
  `Pecking && _time < PeckDoneAt`, `InRange(BreakReach)`, `KeepOutBlocks`, `_egg`, `BreakImmuneUntil`; `Resolve` then either cuts the victim
  (`Cut(v, CutCause.Break)`), takes `Bite` from his pile into the caster, sets `NextBreakAt = _time + Break.Cooldown` and the victim's immunity,
  +1 trophy; or starts an ordinary pile Peck (its usual `NextPeckAt`, room and reach gates) on the nearest pile with food in reach (the intent's `PeckPile` first); or counts a cancel. A landed break runs
  the "own move" rules of `BeginCast` (Hide ends, egg cancelled) without spending an ability slot.
- Each tick: `TrophySecondsAcc += Trophies x tick` for the mean held.

### 8.3 Counters (`SeatCounters`) and report rows

Per seat: `HitTakes`, `HitFood`, `HitRefusedLock`, `HitRefusedNoFood`, `HitRefusedRoom`, `HitLostFood` (victim side); `Breaks`, `BreakFood`, `BreakLeftInPile`,
`BreakFallbacks`, `BreakCancels`, `BreakRefused`, `PecksBroken` (victim side); `TrophiesEarned`, `TrophiesLost`, `TrophyPeak`, `TrophySeconds`, `TrophiesAtEnd`;
`OverflowToCentre`; `WorstVictimLoss5s` (the most one victim lost to this seat's hit-steals, Steals and breaks inside any 5 s window, from a small
`SlidingWindowSum` per victim; the 5 s is a report constant `MatchReport.LossWindowSeconds`, not a game number). Hit-steal food also adds to
`Stolen`, `StolenFromSeat` and the victim's `LostToSteals` (it is a steal in the game), not to `Steals` (Steal casts).

`MatchReport` gets a "Warrior kits" block per population: per Warrior kit, win rate, hit takes and food per match, breaks, fallbacks and cancels per
match, mean and peak trophies, trophies lost, overflow per match, worst 5 s window (mean and 95th percentile over matches). `MatchWhatIf.Numbers`
adds them as group `warrior`; `WhatIfOutput` gains `BullyKit` (first kit with `HitSteal.Active` or holding the Bully asset) and `TrophyKit`
(`Trophy.IsChampion`).

### 8.4 Drift guards

- `MatchWarriorTests.Sim_CallsTheSharedRules_NotCopies`: source scan of `MatchSim.cs` finds `HitStealRules.Take`, `PeckBreakRules.CanBreak`,
  `PeckBreakRules.Bite`, `TrophyRules.Overflow`, and proves it saw them (CODE-TEST-13).
- `MatchWarriorTests.EveryCapacityRead_GoesThroughCapacityOf`: `Farm.CargoCapacity` occurs once in `MatchSim.cs`, inside `CapacityOf`; and zero times in
  `ScriptedPolicy.cs` and `UtilityPolicy.cs` (both read `SeatView.Capacity`).
- `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem` extended to the `BotHitStealRules` and `BotBreakRules` calls (the game-slice
  `BotController` must then make the same calls; the test lists both files).
- `MatchWarriorTests.ShippedOff_PlaysExactlyAsBefore`: the shipped Bully facts (amounts 0) against `HitStealFacts.None`, and the Champion facts as
  shipped against `TrophyFacts.None`: same seed, identical winners, end times and food counters.
- The "affected" test of a stun reuses `ControlReapplyRules.IncomingExtends`, the game's rule; no new mirror.

## 9. What-if grids, trimmed

Budget: about 25 s a scenario at `replicateScale` 0.4; one run is kept to at most 16 scenarios (about 7 min), as each stage of the loot-mark runs. The
full grids are 27 (Bully) and 54 (Champion) scenarios, about 34 min. Read the shipped Warrior stats first: run only once `Warrior.asset`
(10.8, 20, 0.71, 0.4) is committed.

**Run A, Bully** (`whatif.json`, no roster key; every delta carries `"kits":{"Bully":["Peck","Shadowstep","WingSlam","CluckShock"]}`), 12 scenarios:
B0 kit only (amounts 0); the 3 x 3 Stun x Shove amounts at lock 3 (9); lock 2 and 4 at 2 / 2 (2). Why: the lock rarely binds alone, since one Bully's
two hitters come back 3.75 and 5.25 s apart, so the lock only matters when both hitters are ready at once or 2 to 4 s apart; it is measured at the
anchor and, in a second run only if the anchor is close, at the best amount cell. The baseline of this run is the shipped game (Relentless and the
old Bully): it is the reference for run B too.

**Run B, Champion** (`"roster":{"Relentless":"Champion"}`; the baseline is the kit with no passive; the Thief values of Maestro's 2026-11-25
note, bounty 8, steal x1.6, capacity 16, are in every baseline), 15 scenarios: the 3 x 3 reach x cooldown grid at room 2, cap 3, kept through banking
(9; the cooldown axis spaces breaks only, it never slows his Peck); then the trophy lifetime, all at reach 2.5, cooldown 2.5 (Maestro, 2026-11-25: "la
racha deberia desaparecer al backear, o crecer lentamente para que sea bueno en late"): **streak** (cleared on bank) room 2 and 3 at cap 3 (2); **slow
growth** (kept) room 1 at cap 4 and 6 (2) and the original room 2 at cap 3 (the grid's centre, no extra run); break only, room 0 (1). That is 14; the
fifteenth, if budget allows, is trophies from steals only (reach 0, room 2). The room x cap corners of the earlier plan are dropped for the two readings.
Why: the break numbers decide how many trophies come; lifetime, room and cap decide what one is worth. The report adds per scenario: trophies held on
average and at match end, and the share of the added room that was actually filled when he banked (`TrophyRoomUsed`).

Example delta: `{"schema":1,"assets":{"Champion":{"BreakReach":2.5,"TrophyRoom":2}}}`.

Go when all hold (the oracle is a guide; Maestro decides): each Warrior kit 20 to 30 % under P0, P1 and mixed; no non-Warrior kit moves more than 5
points; clock share at most 12 %; worst 5 s window mean at most 6 food (a forgiving children's game, CODE-DESIGN-05); P0 and P1 within 10 points of
each other for each Warrior kit. Shipped references (match report 2026-11-24): Bully 19.9 / 13.7 / 15.3, Relentless 6.1 / 7.1 / 8.9.

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

## 11. Game slices (after Maestro approves; not planned here)

- **Bully hit-steal:** the take through `ChickenCargo.StealFrom` / the steal RPC from the Stun and Shove landing sites (ARCH-DATA-05, ARCH-NET-07); the
  magnitude bound `StealRules.MaxSingleSteal` must cover the passive's hit amounts, since WingSlam and CluckShock declare no steal (ARCH-NET-05); the
  lock on the Bully's authority, per victim (`TargetCooldowns<NetworkBehaviourId>` fits, which the loot-mark spec section 10 plans to delete: keep it);
  a hit-steal event id (ARCH-EVT-02) for the pops; the "affected" test of a stun must use the same `IncomingExtends` the simulator uses. Kit change in
  `Bully.asset`.
- **Champion:** a Peck press that previews a casting rival (CODE-DESIGN-08); a break request to the victim's authority, which checks it is still
  casting, in reach, not immune, cuts its cast and reports; then the Warrior asks the pile for the bite (pile authority debits and reports,
  ARCH-NET-07; the late-report cap `ChickenCargo.MaxPeck` is the Warrior's own bite, equal to every class's today). `[Networked]` trophies (byte),
  the victim's break-immunity `TickTimer` (ARCH-NET-15), a break event id; capacity from `ModifyCargoCapacity` reading trophies; overflow as a new
  cargo-to-centre-pile transfer, allow-listed in `FoodTransferRulesTests`. `NetworkProtocol.Version` 2 to 3 (ARCH-NET-21).
- **Roster:** Champion registered in place of Relentless; `ChickenSubclass` value 1 renamed (or a new value with `ChickenSubclassFlags`,
  `ContractsAndEnumsTests`, `ValidateRoster`); Relentless stays as an unregistered asset.
- **Bots:** `BotController` calls `BotHitStealRules` and `BotBreakRules` (the drift guard of 8.4).
- **Text and feedback:** perk lines with numbers from the fields (ARCH-DATA-12), `UiKeys`/`UiText.csv`, section 6, `docs/FEEDBACK.md`, the site
  (`docs/site/index.html`), GDD; `librarian` for `docs/FILE_STRUCTURE.md` and the rules docs. `tools/balance/schema.py` `DENIED` unchanged (no new denied field).

## 12. Decisions taken without Maestro

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

## 13. Open questions (real forks)

1. ~~Is a break meant to cost food?~~ **Closed by Maestro, 2026-11-25:** `BreakCooldown` gates breaks only; after a break his next pile Peck runs on the
   normal Peck cycle (sections 4.1, 4.3, 9, 12 item 14).
2. **Online timing of the break.** The victim's cast lasts 0.4 s (Warrior, Speedy); the break needs one round trip to the victim's authority before
   that cast lands. On the cloud relay a share of breaks will arrive late and turn into fallbacks. Acceptable, or should the victim's authority judge
   the break against the cast state at the Warrior's press tick (a lag allowance, as `ChickenCombat.SenderLatency` gives other claims)?
</content>
</invoke>

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
