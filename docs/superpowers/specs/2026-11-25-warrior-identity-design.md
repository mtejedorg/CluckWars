# Warrior identity: the Bully's hit-steal and the Trophy Hunter (design and simulator plan)

Date: 2026-11-25. Status: design; the simulator slices S1 to S6 are built from it (amended by Maestro's ruling on `BreakCooldown`, section 12 item 14). Inputs: Maestro's decisions of 2026-11-25 (section 1, closed), the code read on
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
- **Trophy Hunter** (working name; replaces Relentless, which is shelved, not deleted). "Integremos las dos en una: el 2 extra de cortar un peck es otro
  trophy. No entiendo en qué afecta el tamaño, va a ser confuso." Kit Peck, Speed, Steal, Slow. The break and the trophies are section 4.
- Overflow goes to the centre pile (ruled earlier today; the game path is a separate item; the simulator models it for these two kits).
- The Thief gets no Peck interplay change. `RelentlessPassiveSO` and `Relentless.asset` stay in the repo.

## 2. What exists (verified in code)

| Fact | Where |
|---|---|
| Bully kit today: Peck, Shadowstep (Jump), WingSlam (Stun), Snatch (Steal). The new kit swaps Snatch for CluckShock (Shove). | `Passives/Bully.asset` `Kit`; `WingSlamAbilitySO.Job` Stun, `CluckShockAbilitySO.Job` Shove |
| Relentless kit: Peck, SpeedBurst (Speed), Slow, Snatch (Steal): **already the Trophy Hunter's kit**. | `Passives/Relentless.asset` |
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

## 4. Rule 2: the Trophy Hunter

**In one line (for a child):** catch a bird while it pecks and you steal its bite; every catch and every steal is a trophy, and each trophy makes your
beak bigger. Get robbed and you lose one.

### 4.1 The break

| Case | Result |
|---|---|
| Press Peck with a rival mid Peck-cast within `BreakReach` (centre to centre, `MatchCombat.InRange`), no wall between, rival not break-immune | the press previews that rival; a rival beats a pile; the nearest casting rival is the one previewed |
| Release with the previewed rival still casting and still breakable | the break lands at once: his cast is cut (`CutCause.Break`), nothing lands for him, his Peck cooldown stays spent |
| What the Trophy Hunter gains | `min(victim's PeckAmount, food in the victim's pile, his free room)` from that pile; what does not fit **stays in the pile** (never the centre); no bonus food |
| After a break | only another break has to wait `BreakCooldown`; his next pile Peck runs on his normal Peck cycle (a break is never a sacrifice of his own foraging); the victim is immune to breaks for `BreakImmuneSeconds` |
| Release with the cast already over (landed, cut by someone else, pile empty) | a pile Peck if a pile with food is in reach (normal Peck, normal cooldown); else a visible cancel that spends nothing |
| No retarget | the release acts on the previewed rival only; another rival casting at release is not taken |
| Egg | refused (cannot happen today: a pecking chicken holds no live Shield; kept in the rule so a later change cannot open it) |
| Base wall between them | refused |
| Trophy Hunter stunned, dashing or removed | the press does nothing (as any Peck) |
| Trophy Hunter hidden or with a live Shield | the break is a move: it ends Hide and cancels the egg like any cast |
| Two Trophy Hunters on one victim in one tick | the first in the rotating order breaks; the second meets an ended cast and falls back |
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

Fields on the new `TrophyHunterPassiveSO` (`TrophyFacts.ClearOnBank` carries the bank rule) (served through `PassiveAbilitySO.PeckBreak` and `PassiveAbilitySO.Trophy`):

| Field | Type | Shipped (off) | Designed | Sweep |
|---|---|---|---|---|
| `BreakReach` | float `[Min(0)]`, 0 = no break | 0 | 2.5 | 2.0, 2.5, 3.0 |
| `BreakCooldown` | float `[Min(0)]` | 2.5 | 2.5 | 1.5, 2.5, 3.5 (gates breaks only) |
| `BreakImmuneSeconds` | float `[Min(0)]` | 2.5 | 2.5 | none |
| `TrophyRoom` | int `[Min(0)]`, 0 = trophies add nothing | 0 | 2 | 1, 2, 3 |
| `TrophyCap` | int `[Min(0)]` | 3 | 3 | 3, 4, 6 |
| `TrophiesClearOnBank` | bool | false | measured both ways | streak (true) against slow growth (false) |

The asset `Passives/TrophyHunter.asset`: `Subclass` Warrior_Relentless (1), `AllowedClasses` Warrior, `Kit` Relentless's four assets, no
multipliers. It is **not registered**: the game cannot pick it, so it is off whatever its numbers.

### 4.3 Arithmetic to watch (estimate)

`BreakCooldown` gates breaks only (Maestro, 2026-11-25): a break does not touch the Peck cycle, so it never costs him pecking he could do himself.
A break is free to the Trophy Hunter except for the walk to the rival and the press itself; what it costs is that a break is also a Peck press he did not
make at a pile (the press is either a break or a pile Peck, never both). The `BreakCooldown` sweep measures how often breaks come and what they add.

## 5. How a what-if reaches each kit (the deny list)

- **Bully:** a plain delta. The amounts and the lock are serialized fields of a registered passive; the kit is `kits.Bully`. Nothing denied.
- **Trophy Hunter:** a passive class cannot be swapped by a delta (`Subclass` denied, registry not delta-able). Least invasive legal way: a
  **simulator roster swap**. The what-if file gains one optional top-level key, `"roster": {"Relentless": "TrophyHunter"}` (asset names). The editor
  entry builds a temporary `AbilityRegistrySO` whose `All` is the shipped array with that one passive replaced, and runs the book and the simulator over
  it; deltas then name `TrophyHunter` fields as usual. The shipped registry, the game and `BalanceBook` are untouched; the swap is refused unless both
  passives allow the same single class.
- Rejected: registering TrophyHunter now (the game would offer a ninth kit, and `ChickenSubclass` has no free Warrior value); putting break and trophy
  fields on `PassiveAbilitySO` and turning Relentless into the Trophy Hunter by delta (pollutes every passive, labels the kit Relentless, needs
  Relentless's own multipliers zeroed in every scenario).
- Labels: `OracleKits.LabelOf(passive)` = `Subclass` name when the asset name is the subclass's suffix (every shipped passive), else
  `<Class>_<asset name>`, so the swapped kit reads `Warrior_TrophyHunter`.

## 6. What is seen and heard (game slice, later)

Bully, on a take: `+2` pop over the Bully, three food chunks from victim to Bully (`FoodChunkFlight`), the existing Steal hit sound; locked rivals
show a faint lock ring to the Bully only (so a child sees why a hit took nothing). Victim: `-2` pop on his cargo bar, a short buzz (existing haptic).

Trophy Hunter, Warrior: the press preview brackets the casting rival (gold brackets, H1a reuse) instead of the pile; on a break `BREAK!` pop, the bite
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

`BotBreakRules` (Trophy Hunter):
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

`TrophyRules.cs`: `struct TrophyFacts { bool IsTrophyHunter; int Room, Cap; static None }`; `static int Earn(int held, int cap)`, `static int LoseOne(int held)`,
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
- Steal branch of `Cast`: sums the takes of the cast; `TrophyRules.StealEarns` gives +1 when the caster's `Trophy.IsTrophyHunter`.
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
(`Trophy.IsTrophyHunter`).

### 8.4 Drift guards

- `MatchWarriorTests.Sim_CallsTheSharedRules_NotCopies`: source scan of `MatchSim.cs` finds `HitStealRules.Take`, `PeckBreakRules.CanBreak`,
  `PeckBreakRules.Bite`, `TrophyRules.Overflow`, and proves it saw them (CODE-TEST-13).
- `MatchWarriorTests.EveryCapacityRead_GoesThroughCapacityOf`: `Farm.CargoCapacity` occurs once in `MatchSim.cs`, inside `CapacityOf`; and zero times in
  `ScriptedPolicy.cs` and `UtilityPolicy.cs` (both read `SeatView.Capacity`).
- `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem` extended to the `BotHitStealRules` and `BotBreakRules` calls (the game-slice
  `BotController` must then make the same calls; the test lists both files).
- `MatchWarriorTests.ShippedOff_PlaysExactlyAsBefore`: the shipped Bully facts (amounts 0) against `HitStealFacts.None`, and the TrophyHunter facts as
  shipped against `TrophyFacts.None`: same seed, identical winners, end times and food counters.
- The "affected" test of a stun reuses `ControlReapplyRules.IncomingExtends`, the game's rule; no new mirror.

## 9. What-if grids, trimmed

Budget: about 25 s a scenario at `replicateScale` 0.4; one run is kept to at most 16 scenarios (about 7 min), as each stage of the loot-mark runs. The
full grids are 27 (Bully) and 54 (Trophy Hunter) scenarios, about 34 min. Read the shipped Warrior stats first: run only once `Warrior.asset`
(10.8, 20, 0.71, 0.4) is committed.

**Run A, Bully** (`whatif.json`, no roster key; every delta carries `"kits":{"Bully":["Peck","Shadowstep","WingSlam","CluckShock"]}`), 12 scenarios:
B0 kit only (amounts 0); the 3 x 3 Stun x Shove amounts at lock 3 (9); lock 2 and 4 at 2 / 2 (2). Why: the lock rarely binds alone, since one Bully's
two hitters come back 3.75 and 5.25 s apart, so the lock only matters when both hitters are ready at once or 2 to 4 s apart; it is measured at the
anchor and, in a second run only if the anchor is close, at the best amount cell. The baseline of this run is the shipped game (Relentless and the
old Bully): it is the reference for run B too.

**Run B, Trophy Hunter** (`"roster":{"Relentless":"TrophyHunter"}`; the baseline is the kit with no passive; the Thief values of Maestro's 2026-11-25
note, bounty 8, steal x1.6, capacity 16, are in every baseline), 15 scenarios: the 3 x 3 reach x cooldown grid at room 2, cap 3, kept through banking
(9; the cooldown axis spaces breaks only, it never slows his Peck); then the trophy lifetime, all at reach 2.5, cooldown 2.5 (Maestro, 2026-11-25: "la
racha deberia desaparecer al backear, o crecer lentamente para que sea bueno en late"): **streak** (cleared on bank) room 2 and 3 at cap 3 (2); **slow
growth** (kept) room 1 at cap 4 and 6 (2) and the original room 2 at cap 3 (the grid's centre, no extra run); break only, room 0 (1). That is 14; the
fifteenth, if budget allows, is trophies from steals only (reach 0, room 2). The room x cap corners of the earlier plan are dropped for the two readings.
Why: the break numbers decide how many trophies come; lifetime, room and cap decide what one is worth. The report adds per scenario: trophies held on
average and at match end, and the share of the added room that was actually filled when he banked (`TrophyRoomUsed`).

Example delta: `{"schema":1,"assets":{"TrophyHunter":{"BreakReach":2.5,"TrophyRoom":2}}}`.

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
`Abilities/Passives/BullyPassiveSO.cs` (four fields, `HitSteal` override); `Abilities/Passives/TrophyHunterPassiveSO.cs` and
`Data/Abilities/Passives/TrophyHunter.asset` (made in the Editor, not registered). Tests `HitStealRulesTests`: `Take_ClampsToRoomAndVictim`,
`Take_LockedOrZeroAmount_IsZero`, `AmountFor_PeckJumpAndEveryOtherJob_IsZero`; `PeckBreakRulesTests`: `CanBreak_NeedsCastReachNoWallNoEggNoImmunity`,
`CanBreak_ReachZero_IsNeverTrue`, `Bite_IsTheSmallestOfVictimBitePileAndRoom`, `Resolve_BreakThenPileThenCancel`, `Nearest_PicksTheSmallestDistance_MinusOneForNone`;
`TrophyRulesTests`: `Earn_StopsAtTheCap`, `LoseOne_NeverBelowZero`, `Room_IsTrophiesTimesRoom`, `StealEarns_AtOneFood_NotBelow`, `Overflow_IsTheExcessOnly`;
`WarriorPassiveDataTests`: `ShippedBully_HitStealIsOff`, `TrophyHunter_IsNotRegistered_AndItsKitIsLegal` (`FixedKit.Problems` empty, same four assets as
Relentless), `Delta_CanSetEveryNewField_AndTheBullyKit` (`BalanceBook.Evaluate` accepts, nothing ignored). Commit:
`feat(balance): hit-steal, peck break and trophy rules with their passive fields, shipped off (BL-xxx)`.

**S2. Roster swap for the what-if.**
Files: `Balance/OracleRoster.cs` (`static AbilityRegistrySO Swap(AbilityRegistrySO shipped, PassiveAbilitySO outgoing, PassiveAbilitySO incoming)`,
caller destroys the copy); `Balance/MatchWhatIf.cs` (`WhatIfSpec.Roster`, a name-to-name map, parsed from `"roster"`); `Balance/OracleKits.cs`
(`LabelOf`); `Editor/OracleMenu.cs` `RunWhatIf` (load the named passives with `AssetDatabase`, swap, build the book over the copy, write the swap into
the report header). Tests `OracleRosterTests`: `Swap_ReplacesOneSeat_KeepsEveryOtherEntry`, `Swap_LeavesTheShippedRegistryUntouched`,
`Swap_RefusesAPassiveOfAnotherClass_OrOneNotInTheRegistry`; `WhatIfSpecTests.Roster_ParsesAndRejectsAnythingButAnObjectOfNames`;
`OracleKitsTests.LabelOf_ShippedPassivesKeepTheirSubclassName_TheSwapReadsWarriorTrophyHunter`. Commit:
`feat(balance): what-if roster swap so an unregistered passive can take a seat (BL-xxx)`.

**S3. The Bully in the simulator.**
Files: `MatchKit.cs`, `MatchKits.cs`, `MatchSim.cs` (`HitSteal`, locks, `Robbed` stub with no trophies yet), `MatchCombat.cs` (counters). Tests
`MatchWarriorTests`: `Stun_TakesItsAmount_IntoTheBullysCargo`, `Shove_OnAnImmovableVictim_TakesNothing`, `TheLock_RefusesTheSecondHitter_UntilItEnds`,
`AStunThatDoesNotExtend_TakesNothing_AndStartsNoLock`, `OneCast_ThreeVictims_ThreeTakes_ClampedToTheRoomLeft`, `Egg_RefusesTheHit_AndTheTake`,
`EmptyVictimOrFullBully_NoTake_NoLock`, `Peck_NeverTakes`, `ShippedOff_PlaysExactlyAsBefore` (Bully half), `Sim_CallsTheSharedRules_NotCopies` (Bully
half). Commit: `feat(balance): Bully hit-steal in the match simulator (BL-xxx)`.

**S4. The Trophy Hunter in the simulator.**
Files: `MatchPolicy.cs` (`MatchIntent.Break/BreakTarget`, `SeatView` fields), `MatchSim.cs` (`CapacityOf`, trophies, `ResolveBreak`, `SpillOverflow`,
`CutCause.Break`, events), `MatchCombat.cs` (counters, `SlidingWindowSum`). Tests `MatchWarriorTests`: `Break_CutsTheCast_TakesTheBite_RestStaysInThePile`,
`Break_AfterTheCastEnded_FallsBackToAPilePeck_ElseCancelsAndSpendsNothing`, `Break_SetsBreakCooldown_AndTheVictimsImmunity`, `Break_WalledOrImmune_IsRefused`,
`TwoBreakersOneVictim_TheSecondFallsBack`, `Trophies_FromBreaksAndSteals_OnePerCast_CappedAtTheCap`, `Robbed_LosesOne_AndTheExcessGoesToTheCentrePile`,
`Executed_LosesAll_NothingOverflows`, `Banking_KeepsTrophies`, `EveryCapacityRead_GoesThroughCapacityOf`, `ShippedOff_PlaysExactlyAsBefore` (Trophy
half), `SlidingWindowSum_IsTheMostInAnyWindow`. Commit: `feat(balance): Trophy Hunter peck break, trophies and overflow in the match simulator (BL-xxx)`.

**S5. Bot rules and both policies.** Starts only after the BL-221 session has committed `ScriptedPolicy.cs`, `BotTactics.cs` and `BotController.cs`.
Files: `Gameplay/BotHitStealRules.cs`, `Gameplay/BotBreakRules.cs`; `Balance/ScriptedPolicy.cs`; `Balance/UtilityPolicy.cs`; `MatchSim.BuildView`.
Tests `BotWarriorRulesTests`: one per function of section 7 at its edges (`CastForFood_AtOneFood`, `PickHitter_TieGoesToTheShorterCooldown`,
`WorthHunting_SkipsAPreyLockedPastTheWalk`, `Candidate_NeedsTheCastToOutlastTheReaction`, `Approach_OnlyWhenTheWalkBeatsTheCast`,
`ShouldBreak_AtAPileOnlyWhileATrophyIsLeft`, `ReturnFraction_GrowsWithTheTrophyRoom`); `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem`
extended; `ScriptedPolicyTests.P0Bully_CastsStunAtALoadedRival_NotAtAnEmptyOne`, `P0TrophyHunter_BreaksARivalCastingInReach`;
`UtilityPolicyTests.P1_BreaksOnlyWhenTheValueBeatsItsOwnPeck`. Commit: `feat(balance): Bully and Trophy Hunter bot rules in P0 and P1 (BL-xxx)`.

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
- **Trophy Hunter:** a Peck press that previews a casting rival (CODE-DESIGN-08); a break request to the victim's authority, which checks it is still
  casting, in reach, not immune, cuts its cast and reports; then the Warrior asks the pile for the bite (pile authority debits and reports,
  ARCH-NET-07; the late-report cap `ChickenCargo.MaxPeck` is the Warrior's own bite, equal to every class's today). `[Networked]` trophies (byte),
  the victim's break-immunity `TickTimer` (ARCH-NET-15), a break event id; capacity from `ModifyCargoCapacity` reading trophies; overflow as a new
  cargo-to-centre-pile transfer, allow-listed in `FoodTransferRulesTests`. `NetworkProtocol.Version` 2 to 3 (ARCH-NET-21).
- **Roster:** TrophyHunter registered in place of Relentless; `ChickenSubclass` value 1 renamed (or a new value with `ChickenSubclassFlags`,
  `ContractsAndEnumsTests`, `ValidateRoster`); Relentless stays as an unregistered asset.
- **Bots:** `BotController` calls `BotHitStealRules` and `BotBreakRules` (the drift guard of 8.4).
- **Text and feedback:** perk lines with numbers from the fields (ARCH-DATA-12), `UiKeys`/`UiText.csv`, section 6, `docs/FEEDBACK.md`, the site
  (`docs/site/index.html`), GDD; `librarian` for `docs/FILE_STRUCTURE.md` and the rules docs. `tools/balance/schema.py` `DENIED` unchanged (no new denied field).

## 12. Decisions taken without Maestro

1. Trophy Hunter's slow ability: Slow (from the designer), as the brief said.
2. Trophy cap 3; reset each round. Banking: kept (shipped) or cleared by `TrophiesClearOnBank`, both measured (Maestro, 2026-11-25, replaces "kept through banking").
3. A break earns a trophy even when no food fits; a Steal earns one only at 1 food or more (as written).
4. A stun that does not extend a longer stun, a shove that moves nobody, a take of 0: none counts as "affected", and none starts the lock.
5. Slow Area aura ticks never steal; `HitStealSlow` covers the instant Slow only.
6. Hit-steal food counts as stolen food in the report, not as Steal casts.
7. No retarget at release: the break acts only on the previewed rival.
8. A landed break ends Hide and cancels a Shield egg like any move; it spends no ability slot.
9. A Trophy Hunter is not robbed of a trophy by a break against him (a break takes from the pile, not from him).
10. Overflow never raises the centre pile's maximum.
11. Simulator access to the Trophy Hunter through a what-if roster swap over an unregistered asset (section 5), not a game or `BalanceBook` change.
12. Bot: the Bully casts for food at 1 or more, Shove first on a tie; the Trophy Hunter breaks at a pile only while a trophy is left, and banks at
    12 plus its trophy room. The shipped TrophyHunter values (reach 0, room 0) make the run-B baseline "Relentless's kit with no passive".
13. Go measures of section 9 (20 to 30 % per Warrior kit, worst 5 s window at most 6).
14. **Maestro, 2026-11-25 (closes open question 1):** `BreakCooldown` gates breaks only. After a break the Trophy Hunter's next pile Peck is ready on his
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
