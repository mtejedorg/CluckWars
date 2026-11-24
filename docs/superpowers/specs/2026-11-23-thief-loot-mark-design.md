# Thief loot mark and clock prize: design (BL-217)

Date: 2026-11-23, revised 2026-11-24 with Maestro's answers (two rounds and a third note: the loot is ordinary cargo, section 1b). Status: simulator built; game slice A built 2026-11-24 (section 21), slice B (HUD, feedback, words) not built.
Inputs: Maestro's rulings of 2026-11-23 and his answers of 2026-11-24 (both quoted below), `mechanics-designer` (opus)
for the first numbers, `ux-designer` (opus, twice) for what each player sees, and the code read on `develop` at
`c7349422`. The working name was "crown" until 2026-11-24; it is now the **loot mark** (`LootMark` in code).

## 0. Why, and what is already ruled

The Thief kit wins 2.4 / 0.6 / 3.0 % (P0 / P1 / mixed) against a 20 to 30 % target
(`Captures/oracle/match-report-20261123-151552.md`); no numeric lever moved it past about +9 points
(`Captures/oracle/match-whatif-20261123-111254.md`, best row T-C: multiplier 2.5 and cargo 14, mixed 11.8 %). In the
mixed population it robs 3.86 victims, steals 24.18 food and banks 15.16 per match; it forages nothing. In a mirror of
four Thieves every match is decided by the corner rule: nobody banks. The Spoiler, since commit `c7349422` (Hunt 5 s
with a 2 s kill window, `ExecuteBounty` 15), measures 25.0 / 21.6 / 29.5 %.

Maestro's rulings of 2026-11-23, binding:

- "Vamos a hacer corona + premio sólo para él (hay que meter feedback visual y demás para la recompensa). Pero nada que vaya directo a base, todo al bolsillo."
- "Yo esto lo haría por sorteo, como con el asesino. Que pese mucho la comida en base, algo menos la del bolsillo, y un plus a los que tengan más de 25."
- "Comida nueva, como con el asesino."
- "Pasa sólo al thief, ya veremos si lo recuperamos en un futuro."
- Simulator first.

## 1. Maestro's answers, 2026-11-24

Given on the decision page `https://claude.ai/artifact/GN8NGtC569gTouuZ3ahPzT`. His notes are quoted as he wrote them.

| # | Answer | His note | What it means here |
|---|---|---|---|
| D1 | (a) one payout per mark | - | A marked steal pays once; the mark is consumed and moves on. |
| D2 | (a) automatic | "Depende del estado de la habilidad Robar. Si han pasado los 12 segundos, sólo cambia de objetivo si está en cooldown. Una vez consumida, aparece 1 segundo antes de que acabe el cooldown." | No button. The mark's life follows the Thief's Steal: after a marked steal, the next mark appears 1 s before Steal is ready; after 12 s the mark moves only while Steal is on cooldown, never while Steal is ready. Section 4. |
| D3 | (b) banked plus carried | "más de 25" | The over-25 bonus tests banked + carried food. Section 5. |
| D4 | (a) banked at the clock | - | The clock prize is banked into the base at timer expiry, as the Spoiler's is today. The one exception to "todo al bolsillo". Section 8. |
| D5 | (c) a Hunt feature as passive | "La habilidad Mark/kill es tan compleja que parte de sus 'features' se pueden considerar pasiva." | The Spoiler's passive presents something Hunt already does. No new mechanic, no balance change. Section 9. |
| D6 | (b) only Thief and victim | "Sólo thief y víctima. No debería ser una corona, lo que es confuso si no le cae al líder. Debería ser como una bolsa de monedas." | Only the Thief and the marked rival see the mark and the reward pop. The symbol is a bag of coins, not a crown. Section 11. |
| D7 | (b) prudent values, then a table | "De todas formas, hay que modificar la carga. Mi idea es que tenga que robar 3 veces y 'backear' para ganar, parecido al asesino. Puede tener una tabla similar. Así también evitamos que robe dos veces seguidas al mismo persiguiéndole, remplazando el confuso cooldown por objetivo." | About three marked steals, carried home and banked, should win. The Thief's carrying changes so that works. A steals-to-win table like the Spoiler's `KillTargets`. The per-victim steal cooldown goes. Sections 6, 7 and 10. |

## 1b. Maestro's third note, 2026-11-24 (the loot is ordinary cargo)

Received while round 2 was being built; it replaces the bounty-bag part of the design for the Thief (the Spoiler's execute bag was left for a separate question). His words:

> "lo que dices de que la bolsa no ocupa lugar en la carga. Eso no me acaba de gustar... es como hacer trampas... no nos deja limitar la cantidad que tiene el pollo por back. Te podria estar campeando y si hace suficientes robos, pues a lo mejor podria ganar sin backear... Y eso le quita la identidad un poco al Fatty. Entonces yo prefiero no hacerlo asi. Prefiero subirle un poco la carga y que la bolsa vaya al bolsillo y se lo pueda robar y sea completamente normal."

> **Note, 2026-11-24 (later the same day): the Spoiler's bag no longer exists either.** Maestro answered the separate question ("Sí, eso además le balancea en caso de que mate a un fatty. Pon 25 o así."):
> an execute's loot (the victim's cargo, then `ExecuteBounty`) goes into the Spoiler's ordinary cargo, limited by free space, and his capacity is 25 through `SpoilerPassiveSO.MinimumCapacity`.
> `ChickenCargo.BountyBag` is gone, so every mention below of "the Spoiler's bag" or "beak first, then bag" describes the design as it was when the section was written, not the game.

What it means: no capacity-free bag. Everything a marked steal yields, the food taken and the new-food bounty, goes into the Thief's ordinary cargo, limited by free
space, stealable, banked like any cargo (section 6: the stolen food first, then the bounty into the room left; the rest is lost). The Thief's carrying goes up a
little so a trip holds about one to two marked steals, never three, so he must go home between marks and cannot win by camping. Every Thief value lives on the
Thief passive, so the capacity is `ThiefPassiveSO.MinimumCapacity`, a floor like the Hoarder's, and not the shared class stat.

## 2. What exists today (verified in code)

- **Steal** is `SnatchAbilitySO` (`Assets/_Game/Data/Abilities/Snatch.asset`): `StealAmount` 5 from **every** rival carrier
  within `SnatchRange` 5.16 m, `Cooldown` 4 s. Only a carrier is a target (`ExtraTargetFilter`: `cargo.Stealable > 0`).
  A Steal with no valid target cannot fire (`RequiresEnemyInRange` true, `AbilityBaseSO.IsUsable`, line 304), so the
  Thief cannot "burn" his cooldown on nothing.
- **The 1.6x** is `ThiefPassiveSO.ModifyStealAmount` (`Passives/Thief.asset` `StealMultiplier` 1.6, `[Min(1f)]`). Perk text
  `perk.thief.line` "Every steal grabs {steal}% more." The take is `StealMath.Clamp(nominal * factor, thief free room,
  victim cargo + bag)` (`MatchCombat.StealTake`; in the game `ChickenCargo.StealFrom`), drained beak first then bag
  (`StealRules.SplitDrain`), and lands in the thief's **beak** (capacity-limited). Assassin `CargoCapacity` 10.
- **Kits with Steal** (they reference `Snatch.asset`): Thief, Relentless, Bully, Hoarder (`Passives/*.asset` `Kit`).
- **The bounty bag** (removed 2026-11-24, see the note in section 1b) was `ChickenCargo.BountyBag` (`[Networked]`): ignored capacity, stealable (beak first, then bag),
  banked at the base before the beak. The execute now pays into the ordinary cargo, held to the Spoiler's 25 (`AssassinExecute.PayExecuteBounty`).
- **The mark lottery** is `MarkPickRules` (pure, Gameplay assembly): `Filter` drops the last mark unless it is the only
  rival; `PickWeighted` takes the highest `roll * home^2 * assassin * leader`; `LeaderWeight` is `MarkKill.asset`
  `LeaderPickWeight` 2 for the rival with the most banked food. Rolls are injected.
- **The clock prize** is `SpoilerPassiveSO.MatchEndBonusFood` (`Spoiler.asset` `BonusFood` 15); `GameManager.EndOnTimerExpiry`
  banks every passive's bonus first, then picks the winner by base total. Beak and bag are not counted at the clock.
  `MatchConfig.SpoilerBounty` 15 is read by no code.
- **The simulator** (`Assets/_Game/Scripts/Balance/`): `MatchSim.Cast` loops every rival in range with `Stealable > 0`
  and calls `DoSteal` (into `Cargo`, the beak; counts one steal per robbed victim). `PassiveFacts.MatchEndBonusFood`
  is paid in `MatchSim.Finish` before `PickTimerWinner`. RNG streams `StreamComeback` 100, `StreamMark` 101,
  `StreamSetup` 102 (`MatchRng.cs`). What-if scenarios are balance deltas over serialized asset fields
  (`MatchWhatIf.cs`, `BalanceBook.Evaluate`), so every new number must be a serialized field to be swept.
- **Kill targets** are `OracleReferenceSO.KillTargets` (`List<SpoilerKillTarget>`: `Kills`, `WithPassive`, `MinWinRate`,
  `MaxWinRate`) in `Assets/_Game/Data/Balance/OracleReference.asset` (4: 0.90 to 1.00; 3: 0.80 to 1.00; 2 with passive:
  0.00 to 0.20). `MatchStats.Label` turns them into IN / OUT report labels; no test reads them.

### 2.1 The per-victim steal cooldown Maestro wants gone

What it is: `SneakyStealAbilitySO.SameVictimCooldownSeconds` (10 s on `SneakySteal.asset`). After a Sneaky Steal took
something from a bird, `ChickenController.LockStealOn` writes a lock into `_stealLocks` (a `TargetCooldowns<NetworkBehaviourId>`,
`Gameplay/TargetCooldowns.cs`, authority-local, with a 0.5 s `StealLockFeedbackGrace`), and `SneakyStealAbilitySO.ExtraTargetFilter`
refuses that bird through `IsStealLockedOn` until the lock ends. Round reset clears it (`ChickenController.cs` line 1857).
Its text is `ability.sneakysteal.desc` "... Same bird: every {secs}s." (`UiText.csv` line 78).

For which kits: **none that ship.** `SneakySteal.asset` has `Retired: 1` and no kit, registry or prefab references its GUID
(checked with `git grep`). `SnatchAbilitySO` says so in its summary: "No per-victim lock (that lived in the retired
Sneaky Steal)." The simulator has no per-victim lock either. In today's game a Thief, a Relentless, a Bully or a Hoarder
can rob the same bird every 4 s; only the clamp (what that bird still carries) limits it. One related sim-only detail:
P1 (`UtilityPolicy.BestSteal`) multiplies the value of its last victim by `Stickiness` 1.1, a small push to keep robbing
the same bird.

So Maestro's "cooldown por objetivo" is the old Sneaky Steal rule (perhaps remembered from its text or older builds). The
decision is in section 10.

## 3. The rule as a child hears it

1. One rival carries your loot bag. Only you and that rival can see it.
2. Rob the bird with the bag: you take its food and 10 new food falls into your beak, as far as it fits.
3. A new bag appears on another bird just before your Steal is ready again.
4. Birds with lots of food, at home or in their beak, get the bag most often.
5. What you took is yours only once you bring it home, and your beak holds only so much: a trip home after one or two bags. Rob three bags and bank them, and you win.
6. If time runs out and nobody won, the Thief gets 20 extra food at home.

## 4. The loot mark's life cycle (D2)

**Who holds the state.** Each Thief owns one loot mark, decided on the Thief's state authority, as the Death Mark is
(`AssassinExecute`). It reads two facts of the Thief's own Steal slot: `ready` (cooldown over) and `readyAt` (the time it
will be). Fields on `Thief.asset`: `LootSeconds` 12 (the timeout), `LootLeadSeconds` 1 (how early the next mark appears).

**States.**

| State | Meaning |
|---|---|
| `Off` | Before GO, during the round-start pause, after a round reset. No mark. |
| `Waiting` | No mark. Wakes at `drawAt = max(now, readyAt - LootLeadSeconds)`; when Steal is already ready, at once. |
| `Live` | Mark on rival X, age below `LootSeconds`. Takeable. |
| `Overdue` | Mark on X, age at least `LootSeconds`, Steal ready. Stays on X; still takeable. |

**Transitions.**

| From | Event | To | Effect |
|---|---|---|---|
| `Off` | GO | `Waiting` | Steal is normally ready at GO, so the first mark is drawn at once. |
| `Waiting` | `now >= drawAt` and a candidate exists | `Live` | Draw with `LootPickRules` (section 5); age 0. No candidate (every rival removed): stay, retry each tick. |
| `Live` or `Overdue` | A Thief Steal cast covers X and `LootRules.CanTake` holds | `Waiting` | Pay (section 6). X becomes the last marked. Steal is now on cooldown, so `drawAt = readyAt - 1 s` = 3 s after the cast. |
| `Live` | Age reaches `LootSeconds` while Steal is on cooldown | `Waiting` | Mark ends, no pay. X is the last marked. `drawAt = max(now, readyAt - 1 s)`, so the new mark lands at once or within 3 s. |
| `Live` | Age reaches `LootSeconds` while Steal is ready | `Overdue` | Nothing visible changes. |
| `Overdue` | Steal goes on cooldown without taking X, whatever caused it: it robbed someone else, or the cast on X was refused (wall, shell) | `Waiting` | Mark ends, no pay, X is the last marked; `drawAt = readyAt - 1 s`. The next mark never lands on X. Maestro, 2026-11-24 (second round): "Cuando la marca caduca pero no esta en cooldowns, si por lo que sea se pone en cooldown, hay que cambiar la marca." |
| `Live` or `Overdue` | X removed (executed) or leaves the match | `Waiting` | Mark ends, no pay, X is the last marked. |
| any | Round reset (PLAY AGAIN) | `Off` | Cleared without effect; the last-marked memory is cleared too. |

Rule in one line: **the timeout is checked only while Steal is on cooldown**, an overdue mark included, and a new mark is drawn at
`max(now, readyAt - 1 s)`. In code it is `LootRules.Advance(state, age, seconds, stealReady)` (Stay, GoOverdue, Move), one rule for the simulator and the game.
A Steal with no valid target cannot fire in the game (`RequiresEnemyInRange`, `AbilityBaseSO.IsUsable`), so an overdue mark cannot be ended by a cast on
nobody; the cases that spend the cooldown are a cast on other carriers and a cast on the marked bird that a wall or a shell refuses. Both of Maestro's sentences follow from it. A Steal cast on other carriers before 12 s does
not move the mark (the timeout has not passed).

**The marked rival's situations.**

| Case | What happens |
|---|---|
| At home, behind his claimed base wall | Steal cannot reach him (the existing ability pair rule). The clock runs. If the Thief keeps Steal ready, the mark stays on him (`Overdue`) until he leaves or the Thief robs someone else. Hiding at home costs the victim his foraging. |
| Shelled (Egg Shell, steal-proof) | The cast on him is refused (`StealRules.CanStealFrom`): no take, no pay, the Thief sees `SAFE`. The cast still spends Steal's cooldown, so after 12 s the mark moves. |
| Hidden (Hide) | Hide hides the bag badge from the Thief as it hides the bird; the victim still sees his own badge. A Steal that really covers him still takes it. The clock runs. |
| Executed by a Spoiler | Removed: the mark ends without pay (see the table). |
| Empty beak | Still a valid target for this Thief: `ExtraTargetFilter` accepts `Stealable > 0` **or** "marked by this caster". The take is 0, the bounty pays. |

**The Thief's own situations.** Executed: his mark keeps running (nobody else can take it); his beak is emptied at the respawn as every beak is. Stunned: he cannot cast; the mark keeps running. Two Thieves: one mark each, independent; both
may mark the same bird; each pays once to its own Thief. A same-corner ally and a decoy are never candidates.

**Cycle arithmetic (estimate).** Mark 1 at GO; the Assassin's first contact has a median of 3.2 s (report, "Time to first
contact"), the marked bird is not always the nearest, so say 4 to 5 s. Mark 2 appears 3 s after the steal; a walk of 3
to 4 s (Assassin 11.1 m/s). With a trip home after each (3 s walk plus 14 / 9 = 1.6 s to deposit at
`DepositRatePerSecond` 9), a cycle is about 8 to 10 s, so three banked marks land at about 26 to 32 s. Matches end at
31.7 to 35.6 s on average. Three marks per match is therefore a good game, not a given; expect about 2 to 2.5 on
average once shells, walls, stuns and Hide cost time.

## 5. The lottery: `LootPickRules` (pure, Gameplay assembly)

A static class next to `MarkPickRules`, every roll injected, so the game and the simulator call the same rule.

- `Filter`: `MarkPickRules.Filter` as is (last marked out unless only one rival).
- `Weight(banked, carried, t) = t.Base + t.BankedWeight * banked + t.CarriedWeight * carried + (banked + carried > t.OverThreshold ? t.OverBonus : 0)`.
  "Carried" is the beak (the Thief's loot is ordinary cargo). D3 (b): the bonus tests banked **plus** carried.
- `Pick(weights, roll)`: proportional. Walk the candidates in the caller's order; return the first whose running sum
  passes `roll * sum`. Chance = weight / sum. -1 with no candidate; all weights 0 cannot happen while `Base` > 0, and
  falls back to `MarkPickRules.Pick(count, roll)`.

Proportional and not the mark's "highest roll x score", so the numbers read as chances (max-of-rolls turns 2:1 into 75 %).
No position factor: the mark moves on a timeout, and a home factor would make hiding at home a way to dodge it.

| Field on `Thief.asset` | Start | Why |
|---|---|---|
| `LootBaseWeight` | 4 | An empty bird stays drawable; the early draw is near uniform. |
| `LootBankedWeight` | 1.0 per food | "Que pese mucho la comida en base." |
| `LootCarriedWeight` | 0.5 per food | "Algo menos la del bolsillo." |
| `LootOverFraction` | 0.625 of `FoodTargetToWin` (25 of 40) | "Más de 25", follows `MatchConfig`. |
| `LootOverBonus` | 10 | "Un plus." |

Worked example (three rivals, late): banked 28 / 18 / 10, carried 4 / 6 / 0. Totals 32 / 24 / 10, so only the first
passes 25. Weights 4+28+2+10 = 44, 4+18+3 = 25, 4+10 = 14; sum 83; chances 53 / 30 / 17 %. With D3 (b) a bird at
20 banked and 6 carried (26) gets the bonus too: 4+20+3+10 = 37. The bonus can flicker at a deposit only for a bird whose
carried food is all that holds it over 25; the draw happens once per mark, so a flicker never moves a live mark.

## 6. "Three marked steals and bank to win": the numbers (D7)

**What a marked steal pays** (revised, Maestro's third note, section 1b). Two parts, both into the Thief's **ordinary cargo**, limited by its free space like any steal:

1. **The steal first**: Snatch's normal `StealAmount` 5 through the clamp: `take = min(5 * StealMultiplier, the Thief's free room, victim cargo + bag)`.
   The bird loses only what the Thief gains.
2. **The bounty second**: `LootBounty` new food, flat, not multiplied, paid even when the take is 0, into whatever room is left
   (`min(LootBounty, free room - take)`). What does not fit is lost, never held back. Order chosen because it reads simplest: a marked steal is an ordinary
   steal plus a bonus that fills what is left (`LootRules.MarkedGain`).

A full Thief gains nothing from a marked steal, and the mark is still spent. Every other bird the same cast covers is robbed normally.

**The 1.6x goes** (recommended `StealMultiplier` 1.0; the field stays so the sim can sweep it). Reasons: the Thief's
identity becomes the loot bag; a multiplier on every steal pays for chasing any carrier, which is what Maestro wants to
stop; and "three bags win" only reads true if the bag is most of his income. The perk line changes to the loot mark
(section 9).

**Arithmetic (estimates, to be measured).**

- Today, per robbed victim: 24.18 / 3.86 = 6.26 food mixed (P0 22.30 / 3.86 = 5.78, P1 21.77 / 3.39 = 6.42) at a nominal
  8 (5 x 1.6). At a nominal 5 the clamp binds less often; assume about 4.2 per robbed carrier. A marked bird is drawn
  toward the rich, but some are empty: assume a marked take of about 4.
- A marked steal is then worth `V = 4 + LootBounty`. With `LootBounty` 10, V is about 14.
- Kept share. Today the Thief banks 15.16 of 24.18 (63 %). The loot is a lump the Thief walks straight home with; assume 85 % reaches the
  base (a rival's Steal takes 5 a hit from the beak, more for a Fatty's free room).
- Other income: incidental steals on carriers near the marked bird, about 1.5 victims x 4.2 = 6 stolen, about 4 banked.
- **3 marks banked**: 3 x 14 x 0.85 + 4 = 39.7, about the 40 target. **2 marks**: 2 x 14 x 0.85 + 4 = 27.8: no win on
  the target; with the clock prize 27.8 + 20 = 47.8 beats a typical clock leader (28 to 39), but only 5 to 7.5 % of
  matches reach the clock. **4 marks**: 51.6, wins unless someone is faster.
- Mean income at 2 to 2.5 marks: about 28 to 34 banked, against 15.16 today. Winners reach 40 at about 32 to 36 s, so
  the Thief wins his good games, which is what a 20 to 30 % rate needs.

**The beak (revised, third note).** The first draft put the marked yield into a capacity-free bag so a full beak could not clamp it to 0. Maestro
rejected it (section 1b): it lets a Thief win by camping without going home, and takes identity from the Fatty. The yield is ordinary cargo; instead the
Thief's capacity goes up a little through his passive (`ThiefPassiveSO.MinimumCapacity`, a floor like the Hoarder's `ModifyCargoCapacity`; 0 shipped = the class's
10). The Sneaky class stat `CargoCapacity` is shared with the Spoiler and does not move. The floor is swept (12 to 20) so that one trip holds about one to two
marked steals, never three.

**Why he goes home.** Only banked food wins; the beak is full after one or two marked steals, so the next one would gain less or nothing; the loot is
stealable by every Steal kit (Relentless, Bully, Hoarder, another Thief), and the bots prize a carrier (`BotTactics.TargetPriority` uses the stealable share).
A Thief walking with a full beak is the best prey on the map. "Robar 3 veces y backear" is two or three trips.

**A rival's theft of the loot.** The loot is plain cargo: a rival's Steal takes from the Thief's beak at the rival's normal take
(5, or 5 x multiplier for another Thief, clamped by the rival's free room). The food goes to the rival's beak; it does
not carry the bounty with it (the bounty was new food once, now it is plain food). If that rival is another Thief whose
mark is on this Thief, it is a marked steal for him: the same gain rule, into his beak, with his own bounty.

**Recommended starting set and sweep ranges.**

| Field | Start | Sweep |
|---|---|---|
| `Thief.asset` `LootBounty` | 10 | 6, 8, 10, 12, 14 |
| `Thief.asset` `StealMultiplier` | 1.0 | 1.0, 1.3, 1.6 |
| `Thief.asset` `MinimumCapacity` (the Thief's capacity floor; 0 = class capacity 10) | 14 | 12, 14, 16, 18, 20 |
| `Thief.asset` `LootSeconds` | 12 | 8, 12, 16 |
| `Thief.asset` `LootLeadSeconds` | 1 | 0, 1, 2 |
| `Thief.asset` `ClockPrize` | 20 | 15, 20, 25 |
| Lottery weights (base, banked, carried, bonus) | W1 = 4, 1, 0.5, 10 | W0 uniform 1, 0, 0, 0; W1; W2 = 2, 1.5, 0.5, 15 |
| `Snatch.asset`, `Assassin.asset` | unchanged | none |

`MinimumCapacity` is a serialized float on `Thief.asset` (a `[Min(0)]` floor, 0 shipped), so each capacity is a delta. The probe "marked take into the bag" was removed with the bag.

## 7. The steals-to-win table (D7)

**These rows are my proposal, starting values for Maestro to change.** They mirror the Spoiler's `KillTargets`.

| Marked steals in the match | Passive counted | Win rate | Reading |
|---|---|---|---|
| 4 | no | at least 90 % | Four bags is a won match. |
| 3 | no | at least 75 % | "Robar 3 veces y backear": three bags banked usually win. Below the Spoiler's 80 % because a bag is cheaper to get than a kill and the Thief must still walk it home. |
| 2 | yes (clock prize) | at most 20 % | Two bags win only through the clock prize or a weak field. |

Data: a new list `ThiefLootTargets` on `OracleReference.asset`. Recommended form: generalise `SpoilerKillTarget` into one
`WinTarget` struct (`Count`, `WithPassive`, `MinWinRate`, `MaxWinRate`) used by both lists, so `MatchStats.Label` has one
home (field names keep `Kills` serialised via `FormerlySerializedAs` if renamed). Report labels only, never a test
(as for the Spoiler).

"Marked steals" counts marks taken, not banked: banking is the behaviour the row measures through the win rate. The
report also shows, per row, the Thief's final banked and the share of his loot that reached the base.

## 8. The end-of-clock prize, Thief only (D4)

- `ThiefPassiveSO` gains `ClockPrize` **20** and overrides `MatchEndBonusFood` to return it. Paid exactly as the
  Spoiler's today: `GameManager.AwardMatchEndBonuses` banks it into the home base, then the winner is picked. It is the
  one thing that goes straight to the base; the loot bounty goes to the Thief's ordinary cargo.
- Stall check: an idle Thief at 0 gets 20 and loses to a clock leader at 28 to 39. A Thief with two banked bags (about
  28) plus 20 beats most clock leaders, which is the "2 marks: at most 20 %" row through the clock share (5 to 7.5 %).
- Food in the beak at the clock is not banked (unchanged).

## 9. The Spoiler's passive after the move (D5)

The Spoiler keeps a signature passive (`Spoiler.asset`, display name "Assassin"), `BonusFood` 0 (or the override
removed), and the text presents one thing Hunt already does.

**Chosen: the leader pull** (`MarkKill.asset` `LeaderPickWeight` 2, applied in `MarkPickRules.LeaderWeight`).

- New `perk.spoiler.line`: "Your Hunt picks the leader {x} times as often."
- New `perk.spoiler.detail`: "The bird with the most food at home is {x} times as likely to be hunted."
- `{x}` reads `LeaderPickWeight` from the Hunt asset (numbers come from the data, never typed).

Why this one, for a child: it works with no button (it is truly passive); it is the Spoiler's alone (the bag that
ignores capacity is now the Thief's too, so it would not set the Spoiler apart); "the leader" is the bird on top of the
standings, which every child can see; and it tells the leader why he keeps being hunted.

What moves from the Hunt text: **nothing has to leave it.** `ability.markkill.desc` names the hunt time, the kill
window, the stun, the reach and "take all their food"; the leader pull is in no player text today. The Hunt line stays
as it is; the passive line names a behaviour that was silent. Rejected: moving "take all their food" or the
`ExecuteBounty` 15 into the passive. That is the reward of pressing Hunt; out of the Hunt line, the Hunt text would no
longer say what a pounce does.

Text rows to change: `UiKeys.PerkSpoilerLine` / `PerkSpoilerDetail` args (`food`, `goal` become `x`) and their
`UiText.csv` rows; GDD 5.3 table row. Zero balance effect.

The Thief's perk line becomes the loot mark: "Rob the bird with your loot bag for +{bounty}. +{prize} at home if time
runs out." (numbers from `Thief.asset`; final words by `narrative-designer`).

## 10. The per-victim steal cooldown: removal (D7)

Section 2.1: today it binds no shipped kit; it lives only in the retired Sneaky Steal and its support code.

**Decision: it goes, for every kit, and no new one is added.** Steal is one job with one rule (Maestro 2026-11-05, "one
ability per job"); the four kits that hold it share `Snatch.asset`, and a lock for the Thief alone would bring back the
two-rule confusion Maestro names. The Thief's farming problem is about the bonus, and the bonus is now protected by the
mark itself. Removal in the game slice: `SameVictimCooldownSeconds` and the lock calls in `SneakyStealAbilitySO`, its
`{secs}` arg and the "Same bird" sentence of `ability.sneakysteal.desc`, `ChickenController._stealLocks`,
`IsStealLockedOn`, `LockStealOn`, `StealLockFeedbackGrace` and the reset line, `TargetCooldowns` and their tests. The
Sneaky Steal asset stays retired and otherwise as it is (the 2026-11-12 ruling keeps retired abilities for the alpha
feedback; this removes only a rule Maestro has now ruled out).

**What protects a victim from being farmed afterwards.**

- The bonus: `MarkPickRules.Filter` never re-marks the last marked rival (unless he is the only one), so one Thief never
  gets two bounties in a row from one bird. Two Thieves can each mark him once; the bounty is new food, so he loses only
  the two takes.
- The take: as today for every Steal kit, at most 5 per Steal cooldown (4 s), and the clamp shrinks it as the victim
  empties. With the 1.6x gone, a Thief chasing one bird gets 5 per 4 s, less than that bird forages.
- The bot and P1 follow the mark (section 12), so the rotation also moves the chase. P1's `Stickiness` 1.1 on the last
  victim is kept for other kits and does not apply to the Thief's marked target (it is the mark that pulls him).

## 11. Feedback with the coin-bag identity (D6, ux-designer)

Only the Thief and the marked rival see the mark. All of it is local presentation (`ChangeDetector` + `Render`) driven by
the replicated loot state; nothing is sent by RPC. PC and phone show the same elements.

| Old crown element | New element | Who sees it | Reuses | Why |
|---|---|---|---|---|
| Crown on the crowned bird's head | Coin-bag badge over the marked bird's nameplate | Thief, victim | `ChickenStatusBadges` slot | The victim must know he is wanted. |
| `CROWNED!` pop | `MARKED!` pop and a short buzz | Victim | Hunt pop path, haptic H8a | A self-pop on his own bird, so D7b holds. |
| Crown badge on the Thief's floor arrow | Coin-bag badge on the Thief's arrow | Victim | `FloorRivalArrows.TryRepresentEmphasised`, the `HunterSkull` pattern | Who wants it and from where. |
| Crown disc over the target | Coin-bag disc over the target (paw swapped for a bag); target's arrow forced on when off the floor circle | Thief | `MarkHudController` disc | Same job, new icon. |
| Gold ground brackets | Kept: gold, only when the target is in Steal range | Thief | H1a brackets | "Fire now." |
| Steal hex crown dot and pulse | Gold bag dot while a mark is live; pulse when the marked bird is within 5.16 m | Thief | H2a dot | Also carries the "coming" cue below. |
| `CROWN +10` take pop | `LOOT +10` over the Thief, hit-confirm sound, strong buzz; the `+{n}` grab pop is suppressed for that 10 | Thief | Grab pop | A self-pop. |
| Gap outline | Dropped as its own state; folded into "mark is coming" | - | - | The gap is now "Steal recharging", which the hex already shows. |
| `SAFE` / `MISSED` | `SAFE` kept (wall, shell); `MISSED` dropped | Thief | H4a words | A mark no longer just times out; a move is shown by the bag hopping. |
| Bystander crown and feather burst | Dropped | - | - | Bystanders see nothing (D6). |
| Standings crown icon | Dropped | - | - | It is a public surface. `LbSkull{i}` untouched. |
| Clock prize beat | Kept for everyone: `THIEF +20` flies into his base total, 1.2 s, the banner waits; strong buzz on the Thief's phone | All | Standings row | It changes the winner; an unexplained jump reads as a bug. A match-end result, not the mark. |
| Crown and Hunt on one bird | Bag badge under the Hunt disc; one arrow per relationship; the K13 tint stays Hunt-only | Thief, victim, Spoiler | H13a | Unchanged logic. |

New elements a child needs:

- **Mark is coming** (Thief): on the Steal hex the empty bag dot fills with the cooldown sweep and turns solid gold 1 s
  before Steal is ready, the instant the new target's disc and arrow light. No countdown, no word.
- **Mark moves** (12 s, Steal on cooldown): the bag pops off the old bird (0.2 s) and onto the new one; the old victim's
  badge fades in 0.3 s with no word; the new victim gets `MARKED!`. `Overdue` needs no element: nothing changes, which
  is the truth.

**What a bystander can still infer.** That the Thief robbed someone, from the existing steal chunks (`FoodChunkFlight`,
`FeedbackTuning.StealChunksPerVictim` 3), and that he is rich, from his cargo. Since the third note the loot is ordinary cargo, so the cargo bar shows it like any food and the old gap (no UI reads `BountyBag`) no longer concerns the Thief. No mark element for bystanders.

New words (`UiKeys` and `UiText.csv`): `hud.pop.marked` MARKED!, `hud.pop.lootTake` LOOT +{n}, `hud.prize.thief`
THIEF +{n}, `hud.badge.loot` LOOT. New `FeedbackPaletteSO` fields next to the Mark block: `LootGoldColor`,
`LootEmptyColor` (never literals in `FeedbackTuning`, D3a). The final words wait for `narrative-designer`.

**Player-facing name candidates** (for `narrative-designer`): Loot Bag, Jackpot, Fat Purse. "Mark" in `MARKED!` may clash
with the Spoiler's old "Death Mark" naming; `narrative-designer` should pick one word for both the pop and the badge.

## 12. Counterplay and abuse

| Concern | Answer |
|---|---|
| Kingmaking | The lottery leans to the rich, so the Thief robs the leader most and helps the trailers. Measure the leader's win rate with and without a Thief in the match. |
| A Thief that never banks | His beak holds only so much, it does not count at the clock and it is stealable; camping loses. |
| Stalling to the clock | 20 cannot win from zero (section 8). Watch the clock share. |
| Leader hiding at home | He stops foraging; the Thief may keep Steal ready and wait (`Overdue`), or rob others and let the mark move. Both cost one of them time. |
| Farming one bird | Section 10. |
| Bag and Hunt on one bird | Allowed; a kill ends the mark without pay, so the two Assassins compete for the bird rather than stack on it. |

## 13. Simulator slice first

**Where the rules live.** `LootPickRules` (section 5) and `LootRules` (pure: `CanTake(inRange, walled, stealProof)`,
`DrawAt(now, readyAt, lead)`, `TimedOut(age, seconds, stealReady)`, `Advance(state, age, seconds, stealReady)`, `MarkedGain(nominal, factor, freeRoom, victimCargo, victimBag, bounty)`)
in `Assets/_Game/Scripts/Gameplay/`, so `MatchSim` (Balance folder, same Gameplay assembly through its `.asmref`) and
later the game call the same code. Numbers on `ThiefPassiveSO`: `LootBounty`, `LootSeconds`, `LootLeadSeconds`,
`MinimumCapacity` (the Thief's capacity floor), `LootBaseWeight`, `LootBankedWeight`, `LootCarriedWeight`, `LootOverFraction`, `LootOverBonus`,
`ClockPrize`; `StealMultiplier` stays and moves to 1.0. Every one is a what-if delta.

**Facts and state.**

- `PassiveFacts` (`MatchKit.cs`): a `Loot` block with those fields (`LootBounty` 0 = no mark); `MatchKits` reads them from
  `ThiefPassiveSO`. `MatchEndBonusFood` already flows through, so the prize needs no sim code.
- `MatchSim.Chick`: `LootState`, `LootTarget` (-1 none), `LootSince`, `LootDrawAt`, `HasLastLoot`, `LastLoot`.
- A new RNG stream `StreamLoot` (103), so the mark stream and every other draw keep their common random numbers.
- `Cast`, `AbilityJob.Steal`: the caster's marked bird is a valid target with an empty beak; for that bird, when
  `LootRules.CanTake` holds, the stolen food and then the bounty go into the caster's beak through `LootRules.MarkedGain` (held to the free room),
  the mark ends, `LootDrawAt` is set. Other birds: `DoSteal`
  unchanged. A loot upkeep step beside `MarkUpkeep` runs the state machine of section 4 each tick, reading the caster's
  Steal `CooldownLeft`.
- `Remove`: a removed bird ends every mark on him. Round reset: `Off`.
- `SeatView`: the seat's own `LootTarget` and, per rival, "marked by a Thief" (P1's flee option; P0 ignores it).
- Counters: `LootOffered`, `LootTaken`, `LootTakeFood`, `LootBountyFood`, `LootTimeouts`, `LootRefused` (wall or shell),
  `LootLostToRoom`, `LootBankedFood`, `LootLostToSteals`, `Trips`, `MaxMarksPerTrip`; `MatchEventKind.Loot`. The loot food in a cargo is tracked
  pro rata (a deposit or a theft takes the loot share of what it moves).
- `MatchReport` / `MatchWhatIf`: Thief rows for marks offered and taken, take and bounty food, bag food lost, clock prize
  paid, the Thief-mirror corner-decided share, and the **steals-to-win rows** of section 7 labelled against
  `ThiefLootTargets` (played rows under P0, P1 and mixed). A forced design (exactly k marks on schedule, as `ForcedKills`)
  is a second step, only if the played rows are too thin.

**P0, the game's bot.** First plan (superseded by section 18): a loot term in `BotTactics.TargetPriority`. Round 2 replaced it with `BotLootRules` (pure, Gameplay, next to `BotTactics`), called by `ScriptedPolicy` now and by `BotController` in the game task.

**P1, the utility policy.** `BestSteal`: for the marked rival the value adds `LootBounty` plus the marked take, the
`take < 1` skip does not apply (the gain, take plus the bounty that fits, must be at least 1), and `Stickiness` does not apply to him. A Thief whose beak cannot take another bounty banks first. The
marked rival weighing a run home higher is a separate, optional measure.

**What-if runs** (P0, P1, mixed; every row with `ExecuteBounty` 15, `Spoiler.asset` `BonusFood` 0, `StealMultiplier`
1.0 unless swept):

- Stage A, 15 scenarios: `LootBounty` {6, 8, 10, 12, 14} x `ClockPrize` {15, 20, 25}; W1; `LootSeconds` 12; lead 1.
- Stage B, on the best Stage A row: `StealMultiplier` {1.0, 1.3, 1.6}; weights W0, W1, W2; `LootSeconds` {8, 12, 16};
  lead {0, 1, 2}; the Thief's capacity floor 12 to 20; clock prize 30 once as a stall probe.

**Go when all hold:** the Thief at 20 to 30 % under P0, P1 and mixed (inside the margins); the Spoiler stays 21 to 30 %;
no other kit moves more than 5 points; matches on the clock at 12 % or less; mean length no more than 2 s shorter; the
Thief mirror no longer 100 % corner-decided; 1.8 to 3.0 marks taken per match; the steals-to-win rows IN or within their
margin of the band (3 marks at least 75 %, 2 marks with the prize at most 20 %).
**No-go when any holds:** the Thief above 35 % under any policy; any kit moves more than 8 points; clock share above 15 %;
the 2-mark row above 35 % (the prize, not the bags, would be winning).
The oracle is a guide: Maestro decides on the numbers.

## 14. The game slice after that (list, not a plan)

- **Networked state and authority.** A `ThiefLoot` NetworkBehaviour on the chicken (added only for the Thief, as
  `AssassinExecute` is for the Spoiler): `[Networked] LootTarget`, `LootSince`, `LootDrawAt`, state; last marked is
  authority-local. It reads its own Steal slot's cooldown. Drawn on the Thief's authority with `LootPickRules`. Gameplay
  assembly; no new Fusion assembly, `AssembliesToWeave` unchanged.
- **Reward, authority-confirmed.** At the cast, the Thief's authority records a loot expectation for the marked bird (a
  `PendingStealLedger`, as `ExpectExecuteLoot`). The marked bird's authority drains the take (`RPC_DrainStolen` path,
  free room ignored for this bird) and confirms the mark (not steal-proof, within `StealRules.IsWithinStealRange` plus
  margin, no wall) through a receiver-validated RPC; only a confirmation that matches a pending expectation inside its
  window pays the take and `LootBounty` into the Thief's cargo (held to its free room), once. A refused confirm leaves the mark. `StealRules.MaxSingleSteal`
  must cover the marked take (5 x multiplier); the bounty is not a drain.
- **Rules.** `SnatchAbilitySO.ExtraTargetFilter` accepts the caster's marked bird; `LootPickRules`, `LootRules`; removal
  (`ChickenCombat` execute) and round reset clear marks; decoys never drawn.
- **Per-victim lock removal** (section 10).
- **Passives.** `ThiefPassiveSO`: loot fields, `ClockPrize`, `MatchEndBonusFood` override, `StealMultiplier` 1.0, new
  `PerkArgs`. `SpoilerPassiveSO`: `BonusFood` 0 or override removed, new text args (`LeaderPickWeight`). The
  `GameManager.AwardMatchEndBonuses` comment updated.
- **BalanceBook.** New fields tunable by delta; none on the deny list; `tools/balance/schema.py` mirrors that.
- **Oracle data.** `ThiefLootTargets` on `OracleReference.asset` (section 7).
- **HUD and VFX.** Section 11.
- **Bots.** `BotTactics.TargetPriority` loot term, `BotController` cast plan.
- **Text.** `UiKeys` and `UiText.csv` rows (section 11), Thief and Spoiler perk lines with numbers from the data,
  `ability.sneakysteal.desc` without "Same bird".
- **Tests.** EditMode: `LootPickRules` (weights, the banked-plus-carried bonus, proportional pick, filter, fallback),
  `LootRules` (each transition of section 4, including `Overdue` and the 1 s lead), the empty-beak target filter, the
  ledger confirm (forged and late confirms refused), the clock prize paid before the winner, the Spoiler paying nothing;
  `FixedKitTests` unchanged; simulator parity tests for the loot mark.
- **Docs.** GDD 5.3, 6.2 (loot mark), 6.4 (Spoiler no prize), `docs/FEEDBACK.md`, `docs/site/index.html` (republish),
  `docs/STATE.md`; `librarian` for `docs/FILE_STRUCTURE.md` and the rules docs (TargetCooldowns removed, new Gameplay files).

## 15. Open decisions for Maestro

None. Every point above follows from his answers or is a starting value the simulator sweeps. Decided here without
asking (say so if wrong): `StealMultiplier` to 1.0; (the marked take went to a bag; reversed by the third note) the loot is ordinary cargo and the Thief's capacity comes from his passive; the class `CargoCapacity` unchanged; the
per-victim lock removed for every kit; the steals-to-win rows of section 7; the leader pull as the Spoiler's passive;
the clock prize beat shown to everyone.

## 16. Assumptions and what was not verified

- All numbers in sections 4 to 8 are estimates for the simulator, not measurements: the marked take of about 4, the 85 %
  of a bag that reaches home, the 1.5 incidental victims, the 2 to 2.5 marks per match, the 8 to 10 s cycle.
- Steal is assumed ready at GO (no round-start cooldown on Snatch); not traced in the game.
- Whether the steal chunks draw for every client is assumed (ux-designer).
- The simulator does not model a placed Slow Area zone or body collision (its own list); neither changes the mark.

## 17. Simulator results, round 1 (2026-11-24, BL-217; the capacity-free bag design, superseded by sections 1b and 19)

Built as section 13 says; reports `Captures/oracle/match-whatif-20261124-124228.md` (Stage A, 15 scenarios) and
`match-whatif-20261124-125852.md` (Stage B, 15 scenarios), 22800 matches per scenario at the report's own replicates, common
random numbers, seed `0x1352902`, every scenario with `Spoiler.BonusFood` 0 and `StealMultiplier` 1.0 unless swept. Not committed.

**Verdict: no scenario passes.** Thief win rate P0 / P1 / mixed, marked steals consumed per match (P0 / P1), the Spoiler (shipped 25.0 / 21.6 / 29.5):

| Scenario | Thief | Consumed | Spoiler | Worst other kit (P1) |
|---|---|---|---|---|
| bounty 10, prize 20 (anchor) | 12.3 / 27.1 / 21.9 | 1.52 / 3.07 | 19.2 / 18.3 / 24.4 | Featherfoot -9.6 |
| bounty 11, prize 20 | 13.5 / 33.2 / 24.9 | 1.52 / 3.03 | 19.5 / 17.4 / 24.4 | Featherfoot -12.7 |
| bounty 10, steal x1.6 | 20.9 / 35.3 / 28.7 | 1.46 / 2.87 | 18.1 / 17.3 / 24.4 | Featherfoot -10.1 |
| bounty 12, prize 20 | 15.6 / 42.7 / 31.7 | 1.51 / 2.97 | 19.1 / 17.0 / 23.8 | Featherfoot -14.0 |
| bounty 14, prize 20 | 20.7 / 54.8 / 39.2 | 1.50 / 2.89 | 19.2 / 15.5 / 22.8 | Featherfoot -17.9 |

- The Thief's rate moves with the bounty (6: 5 %, 8: 11 %, 10: 22 %, 12: 32 %, 14: 39 % mixed) and **the two policies never agree**:
  P1 plays the rule as designed (about 3 marked steals, bag banked), P0 consumes half as many (1.5 of 3.6 drawn). Where P0 reaches 20 %
  (bounty 14) P1 is at 55 %, above the no-go bar of 35 %. No bounty puts P0, P1 and mixed in 20 to 30 together.
- The clock prize does nothing measurable (15, 20, 25, 30: 0 to 2 points); 1 to 5 % of matches are won only through it. Stall probe 30: no stall.
- Timeout (8 / 16 s), lead (0 / 2 s), weights (W0, W2), taking into the beak, cargo 14: each within 1 point of the anchor for mixed. Steal x1.3 / x1.6: +2.5 / +6.8 mixed.
- "Three marked steals win" does not hold: with 3 marked steals the Thief wins 29 to 35 % (anchor, no prize) against the 75 % row, 62 to 70 % at bounty 14. A marked steal is worth about 12 to 13 food at bounty 10, so three bags bank about 37, short of 40.
- The Spoiler falls out of its band in every scenario (17 to 20 % P0 / P1), because the Thief now takes the food it would have taken. Featherfoot (P1) loses 9.6 to 18 points: more than the 8-point no-go bar from bounty 10 up.
- Clock share stays 2 to 5 % (go bar 12 %), mean length 1 to 2 s shorter, the Thief mirror is corner-decided 0 to 8 % under P1 and mixed (24 to 60 % under P0, 25 matches per kit) against 100 % before.
- The loot mark and a Hunt mark sit on the same bird in 19 to 28 % of the Thief's matches (P1 19, P0 27, mixed 23 at the anchor).
- Marks per match at the anchor: 3.65 drawn, 2.33 consumed (mixed), 0.5 timed out or lost with their bird, 4.0 bag food lost to rivals' steals of 22.7 banked.

Which rule, not number, would have to change: (1) the P0 / P1 gap (a bot that reaches the marked bird twice as often is not a number in this design: either the game bot gets a real chase for the marked bird or the target is read from P1 only); (2) the win threshold against the bag (three bags are about 37, so "three steals win" needs the bag to be worth 13.3 each, which is the 14 bounty that already sends P1 to 55 %); (3) the Spoiler's loss, which comes from sharing victims, not from its own numbers.

**Deviations from this design.** The target rows are a separate `ThiefLootTarget` list (not a generalised `WinTarget`), so `KillTargets` stays byte for byte; "mechanic off" is `LootBounty` 0 (no extra switch); the loot stream is one shared stream `StreamLoot` 103 drawn in a rotating seat order, not per seat; P1's flee option is not built (the `SeatView.LootMarkedBy` field exists, no policy reads it); `BotTactics` and `BotController` are untouched: P0 passes the marked rival's load as 1.0 from its own perception code; a mark ended by its bird's removal is counted with the timeouts.

## 18. Maestro's answers of the second round (2026-11-24) and what they changed

Decision page answers, binding for this task. His notes as the brief quoted them:

- "Una vez aprobado en el simulador, que este en el juego real." Not in the game yet: keep iterating in the simulator; the game task comes after it passes.
- "Todos estos valores deben ir en la pasiva de thief y no en steal dado que warrior tambien equipa steal." Every loot value lives on the Thief passive, never on the Steal ability.
- Three marked steals plus some loose steal to win is accepted; the bounty stays 10 as the anchor.
- The bot must be taught to chase the marked rival and carry the loot home ("ensenar al bot"), as part of the mechanic.
- Measure and raise the Spoiler's execute bounty just enough to bring it back into its band with the loot mark on.
- The Featherfoot drop is an intended effect: accepted. The clock prize stays at 20 with no feedback of its own for now.
- The life-cycle correction, in section 4: an overdue mark ends the moment Steal goes on cooldown, whatever caused it.

**Where each loot value lives (checked in code, 2026-11-24).** On `ThiefPassiveSO` (`Thief.asset`): `LootBounty`, `LootSeconds`, `LootLeadSeconds`,
`LootBaseWeight`, `LootBankedWeight`, `LootCarriedWeight`, `LootOverFraction`, `LootOverBonus`, `ClockPrize`, `StealMultiplier` (the Thief-only multiplier: confirmed on the passive,
`ModifyStealAmount`) and, since the third note, `MinimumCapacity`. Read from elsewhere, none of them a loot value: the Steal ability's own `StealAmount`, `Cooldown` and reach
(the marked take is "Steal's normal take", the next mark follows Steal's own cooldown; nothing was added to `Snatch.asset`), `MatchConfig.FoodTargetToWin` (the
over-bonus threshold is `LootOverFraction` of the target: the passive holds the fraction, the match holds the target), and the reference targets `ThiefLootTargets`
on `OracleReference.asset` (report labels only, never read by a rule). Hard-coded: none (the simulator's `Eps` and tick are not loot numbers). The bot rule's one constant is
a rule, not a balance value: `BotLootRules.BankNow` reads the bounty and the capacity from the passive.

## 19. The bot rule (round 2)

`BotLootRules` (`Assets/_Game/Scripts/Gameplay/BotLootRules.cs`, pure, next to `BotTactics`), called by `ScriptedPolicy` (P0) now and by `BotController` in the game task:

- `Pursue(markLive, known, shelled, behindWall)`: go to the marked rival at any distance, by the wall-aware path, to where it was last seen when hidden (the same `BotSightRules`
  through `MatchSight`: `known` is false once the bot gave a hidden rival up). Abandon (play the ordinary rules, let Steal go elsewhere and the mark move) when the rival is
  shelled or `BehindWall` (inside the keep-out circle of its own base, the game's `BaseWallRules.IsInside`).
- `HoldSteal(pursuing, distToMarked, moveSpeed, stealCooldown)`: do not spend Steal on an unmarked rival while the marked one is closer than one cooldown's walk
  (a cast elsewhere would still be cooling down on arrival); farther than that the cast is free.
- `BankNow(carried, capacity, bounty, banked, winTarget, distHome, moveSpeed, depositRate, timeLeft)`: bank when the clock would run out on the walk, when banking reaches the win
  target, or when the next marked steal would lose part of its bounty (`capacity - carried < bounty`). That last test is the threshold, derived from the numbers: bounty 10, capacity 20:
  the bot carries on with 10 or less and banks with more. A Thief with the loot mark on uses it in place of the class's cargo-fraction threshold.

Why it was needed (420 Thief matches under P0, the capacity-free design, before the rule): marks are drawn 34 m from the Thief and only 13 % inside the bot's 16 m hunt radius; 51 % of
marks saw Steal spent on an unmarked rival; while a mark was live the bot walked toward the marked bird 6 % of the time (home 25 %, elsewhere 69 %); 72 % of the unconsumed marks were
lost to a cast elsewhere. P0 consumed 1.47 of 3.34 marks drawn per match, P1 3.07 of 3.99.

## 20. Simulator results, round 2 (2026-11-24, BL-217)

Built as sections 1b, 4 and 19 say: the loot is ordinary cargo, the Thief's capacity is `MinimumCapacity` on his passive, an overdue mark ends with the Steal cooldown, the bot plays
`BotLootRules`. Reports (not committed): `Captures/oracle/match-whatif-20261124-135351.md` and `-141436.md` (the capacity sweep, the second with the "three in one load"
columns, identical numbers), `-140744.md` (bounty 8 to 12, steal x1.6, execute bounty 16 to 20, all on capacity 20), `-141844.md` (the best three again with another seed, 777).
22800 matches per scenario at the report's own replicates, common random numbers (seed `0x1352902`), every scenario with `Spoiler.BonusFood` 0, clock prize 20, steal multiplier 1.0 unless swept.

**Why P0 consumed so few marks (before the rule, 420 Thief matches under P0).** Marks are drawn 34 m from the Thief (the arena is large) and only 13 % inside the bot's 16 m hunt radius, so a
hunt-radius bot never saw most of them; 51 % of the marks saw Steal spent on an unmarked rival (72 % of the unconsumed marks); while a mark was live the bot's goal was the marked
bird 6 % of the time (home 25 %, elsewhere 69 %); and it went home after every steal. Marks consumed per match: P0 1.47 of 3.34 drawn (1.52 in the round 1 report), P1 3.07 of 3.99.
After the rule, on the same matches with the cargo design: P0 3.71 of 4.53 at capacity 14 and 3.08 of 4.01 at capacity 20; P1 3.12 and 3.07. P0 spends Steal on an unmarked rival in
6 % of marks (was 51 %) and walks to the marked bird 64 to 67 % of the time.

**Capacity sweep (anchor bounty 10, prize 20, multiplier 1.0).** Thief win P0 / P1 / mixed; marked steals consumed per match (mixed); trips home per match; wins on the target with two or more
marked steals in one load; with three or more.

| Capacity | Thief | Marks | Trips | Two in one load, of wins | Three, of wins | Spoiler P0 / P1 / mixed |
|---|---|---|---|---|---|---|
| 12 | 22.1 / 10.6 / 13.3 | 3.37 | 2.73 | 4.7 % | 0.0 % | 24.3 / 21.1 / 30.9 |
| 14 | 29.3 / 18.0 / 21.6 | 3.29 | 2.57 | 5.9 % | 0.2 % | 23.2 / 21.6 / 30.8 |
| 16 | 35.2 / 25.9 / 26.6 | 3.23 | 2.43 | 9.7 % | 0.2 % | 22.4 / 21.8 / 30.0 |
| 18 | 36.0 / 25.9 / 26.8 | 3.23 | 2.42 | 9.4 % | 0.2 % | 22.3 / 21.6 / 29.9 |
| 20 | 22.1 / 25.3 / 23.6 | 2.99 | 2.03 | 13.3 % | 0.2 % | 23.1 / 21.8 / 30.0 |

A win with three marked steals in one load is essentially never (0 to 0.2 %: a mark that lands while the load before it is still being deposited in the base zone). Two in one
load, the "one to two per trip" the note asks for, grows with the capacity: 5 to 13 % of the wins. P0 falls from 36 to 22 % between 18 and 20: its marks drawn per match drop from 4.5 to 4.0
(more partial loads, so more flee and walk-home thinks of the game's own bot rules): a bot artefact of the cycle, not a rule of the mark.

**Scenarios on capacity 20.** Thief win P0 / P1 / mixed; Spoiler P0 / P1 / mixed; Speedy pair P1; the other kit that moved most (any population); on the clock P1; the 3-marked-steals row (win without the prize).

| Scenario | Thief | Spoiler | Slippery / Featherfoot P1 | Other kit, most | Clock | 3 marks |
|---|---|---|---|---|---|---|
| anchor, bounty 10 | 22.1 / 25.3 / 23.6 | 23.1 / 21.8 / 30.0 | -8.0 / -9.1 | Bulwark P0 -5.8, Bully P1 -5.5 | 2.0 | 16.5 / 26.7 / 22.7 |
| bounty 8 | 7.9 / 9.0 / 13.0 | 24.1 / 22.9 / 30.6 | -4.8 / -4.3 | Bulwark P1 +4.5 | 2.1 | 0.0 / 3.6 / 4.3 |
| bounty 9 | 15.0 / 16.5 / 16.6 | 23.8 / 20.8 / 29.9 | -5.6 / -8.3 | Bulwark P1 +5.0 | 2.1 | 6.1 / 13.7 / 11.9 |
| bounty 11 | 39.6 / 32.0 / 32.0 | 22.7 / 21.5 / 31.0 | -9.3 / -11.7 | Bulwark P0 -9.6 | 2.0 | 34.6 / 35.1 / 32.4 |
| bounty 12 | 48.6 / 40.4 / 39.0 | 21.2 / 22.5 / 29.4 | -12.6 / -14.1 | Bulwark P0 -10.1 | 1.9 | 53.5 / 46.8 / 44.4 |
| steal x1.6 | 33.4 / 31.3 / 31.5 | 23.2 / 22.3 / 29.5 | -9.2 / -9.7 | Bulwark P0 -7.8 | 2.0 | 38.7 / 37.6 / 38.5 |
| execute bounty 16 | 21.7 / 25.0 / 23.7 | 26.5 / 24.5 / 32.6 | -8.7 / -10.0 | Bully P1 -6.1 | 2.0 | 16.0 / 26.4 / 22.8 |
| execute bounty 17 | 20.9 / 25.1 / 22.9 | 29.1 / 27.8 / 34.5 | -9.0 / -10.7 | Bully P1 -6.6 | 2.0 | 15.7 / 26.9 / 22.1 |
| execute bounty 18 | 21.0 / 24.4 / 23.1 | 31.0 / 31.8 / 37.2 | -10.3 / -11.2 | Bully P1 -6.5 | 2.0 | 16.1 / 26.1 / 22.8 |
| execute bounty 20 | 20.6 / 23.9 / 22.7 | 35.8 / 40.3 / 43.5 | -12.0 / -13.1 | Bully P1 -7.5 | 1.9 | 15.8 / 25.9 / 22.8 |

Spoiler baseline 25.0 / 21.6 / 29.5. The Spoiler no longer falls out of its band (the Thief robs less food from the same victims: 2.4 steals a match against 3.9): at the shipped execute bounty 15 it is
23.1 / 21.8 / 30.0 (round 1: 19.2 / 18.3 / 24.4), a hair under 22 for P1 and on 30 for mixed, inside the margins (a second seed: 22.6 / 22.4 / 27.9). Raising the execute bounty to 16 puts mixed at 32.6 (and 31.0 on the
second seed): **no raise is needed, 15 is the lowest and the right value**; the sweep gives no value that is inside 22 to 30 under all three populations on both seeds better than 15.

**Confirmation, seed 777.** Anchor capacity 20: Thief 22.6 / 25.0 / 23.7; Spoiler 22.6 / 22.4 / 27.9; Bulwark P0 -6.2, Bully P1 -5.5. Capacity 14: Thief 31.6 / 20.0 / 22.9. Execute bounty 16: Spoiler 25.3 / 24.9 / 31.0.

**Verdict against Maestro's measures (the anchor: capacity 20, bounty 10, prize 20, multiplier 1.0, execute bounty 15).**

- Thief 20 to 30 % under P0, P1 and mixed together: **passes** (22.1 / 25.3 / 23.6; 22.6 / 25.0 / 23.7 on another seed). No other capacity or bounty does: capacity 14 has P1 at 18 to 20 and P0 at 29 to 32, 16 and 18 have P0 at 35 to 36.
- Thief under 35 % under every population: passes.
- Spoiler 22 to 30 under all three: **misses by 0.2** on the main seed (P1 21.8) and passes on the second; mixed sits on 30.0.
- Kits other than the Speedy pair within 5 points: **misses by 0.5 to 1.2**: Bulwark P0 -5.8 and Bully P1 -5.5 (-6.2 and -5.5 on the second seed); every mixed number is within 3.2.
- Clock share at most 12 %: passes (2.0 %).
- The Speedy drop is accepted: Slippery -8.0 and Featherfoot -9.1 at P1 (-5.4 and -8.1 mixed).
- Information: three marked steals win 16.5 / 26.7 / 22.7 %, four win 43 to 46 %; the Thief mirror is corner-decided 0 % of the time.

Nothing passes every measure; the anchor is the closest, and what blocks it is the Thief now robbing the Bulwark (P0) and the Bully (P1) a point or so more than the band allows. The levers that stay inside
the ranges move the wrong way (a lower capacity takes the Thief out of its band, a lower bounty likewise). The oracle is a guide: Maestro decides.

**What the simulator cannot tell.** The real bot's own timing and navmesh (P0 is a transcription); that humans will play the loot differently from P0 and P1; the feedback of a mark (the HUD pieces of section 11);
whether a mark drawn 34 m away feels fair; Fusion latency on the confirmed take; the pro-rata loot attribution in a mixed cargo is an estimate.

**Deviations.** `BotController` and the game are untouched; P0's flee priority (a loaded bot with a rival inside 6.5 m goes home) is the game's rule and was left in place, so a Thief carrying a partial load can still break off a chase.


## 21. Game slice A (2026-11-24, BL-217): what was built

Maestro, after the round 2 simulator runs: "Una vez aprobado en el simulador, que esté en el juego real." Slice A is the game side of the rules, the networked state, the authority, the bot and the data. Slice B (HUD, feedback, words) is not built: in slice A the mark works and is testable through state, events and logs only.

**Approved values** (the design's defaults plus Maestro's choices): Thief `LootBounty` 10, `ClockPrize` 20, `StealMultiplier` 1.0, `MinimumCapacity` 20, weights and timings as section 5 and 4 (12 s, 1 s lead); `Spoiler.asset` `BonusFood` 0 (his passive text becomes the leader pull and the capacity); `MatchConfig.ExecuteBounty` 17.

**Built** (files under `Assets/_Game/Scripts/Gameplay/` unless noted):

| Piece | Where | Authority |
|---|---|---|
| The mark's state machine | `ThiefLoot.cs` (new NetworkBehaviour on `Chicken.prefab`; the Doppelganger variant inherits it and is skipped by `IsDecoy`) | the Thief's state authority, in `FixedUpdateNetwork` behind `HasStateAuthority`; draws with `LootPickRules` (`Filter`, `Weight`, `Pick`), advances with `LootRules.Advance` and the new `LootRules.DrawDue` |
| The marked steal's request and settlement | `ChickenCargo.RequestMarkedSteal`, `RPC_DrainStolenMarked`, `RPC_SettleMarkedSteal`; the pure rules in `MarkedStealBook.cs` | Thief asks and reserves; the victim's authority drains and reports; the Thief's authority credits |
| One drain body | `ChickenCargo.DrainForThief`, shared by `RPC_DrainStolen` and `RPC_DrainStolenMarked` | the victim's authority |
| The Steal ability | `SnatchAbilitySO`: the caster's marked rival is a valid target even with an empty beak; it is routed through `ThiefLoot.TakeMarked` first, every other rival as before | the caster's authority |
| Event and telemetry | `ThiefLoot.LootTakenEventId` (+ `LastLootVictim`, `LastLootTake`, `LastLootBounty`), `ChickenLocalEvents.LootTaken`, `MatchTelemetry.LootDraw/LootTake/LootMove` | the Thief's authority |
| Round reset | `ChickenController.RPC_ResetControlStates` calls `ThiefLoot.ResetForNewRound`; `ChickenCargo.RPC_ResetForNewMatch` clears the open marked steal | the match authority's reset, executed on each chicken's authority |
| Clock prize | unchanged hook: `PassiveAbilitySO.MatchEndBonusFood`, one call site in `GameManager.AwardMatchEndBonuses`; `ThiefPassiveSO` overrides it (already did) and the data now says 20; the Spoiler's says 0 | the master, before the winner is picked |
| The bot | `BotController`: `BotLootRules.BankNow`, `Pursue`, `HoldSteal`, `BehindWall`, and `BotTactics.LoadedToBank` (the Death Mark path, unchanged) | the bot's authority (the master) |

**The marked steal, step by step.** (1) Thief's authority: `SnatchAbilitySO.OnActivate` finds the caster's marked rival among the targets and calls `ThiefLoot.TakeMarked(victim, amount)`; it requires a live mark on that exact victim, else it returns false and the bird is robbed like any rival (no mark, no bounty). (2) `ChickenCargo.RequestMarkedSteal` refuses a shelled victim locally (the mark stays), then `MarkedStealBook.TryOpen` reserves room for the take (a steal's normal take, held to the free room and to what the victim carries) and then for the bounty in the room left, records the one open request and sends `RPC_DrainStolenMarked(take, thiefId, id)`; a second marked steal is not started while one is open. (3) Victim's authority: validates the thief exactly like an ordinary steal (plausible amount, the thief is the sender's, in reach), drains through `DrainForThief` (a shell, a wall, an empty beak behave as for any steal; the `steal` telemetry line and the steal event are written there) and answers `RPC_SettleMarkedSteal(stolen, reached, victimId, id)`, `reached` false for a refusal. (4) Thief's authority: honoured only from the victim's own authority, against the open request it names, once, inside the 2.5 s settle window; the reservations are released and the take is credited first, then the bounty into the room left (`Cargo +=`, the only new site, allow-listed in `FoodTransferRulesTests`); `ThiefLoot.OnMarkedStealPaid` writes the event fields, bumps `LootTakenEventId`, schedules the next mark and writes the `loottake` line. A refusal credits nothing and the mark stays. While the request is open the mark does not time out.

**Networked layout (a protocol change, ARCH-NET-14).** New NetworkBehaviour `ThiefLoot` on the chicken prefab, in `NetworkObject.NetworkedBehaviours`, with seven `[Networked]` properties in this order: `MarkedTarget` (NetworkBehaviourId of the rival's `ChickenController`), `State` (`LootState : byte`), `AgeTimer` (TickTimer), `LootTakenEventId` (byte), `LastLootVictim`, `LastLootTake`, `LastLootBounty`. Nothing existing was reordered or retyped; `ChickenCargo` and `AbilityController` gained no property. Every peer must run the same build. Live check owed: the four-client session below.

**Deviations from the design.** (a) The marked victim is robbed first in the cast, so the take and the bounty have first call on the beak's room (the simulator robs in seat order). (b) The draw is due when Steal's cooldown left is at most the lead (`LootRules.DrawDue`), read from the replicated slot timer each tick, instead of a stored draw time; the same instant. (c) The mark does not time out while its marked steal is in flight (the answer decides it). (d) A refusal by the victim's authority (a shell that went up inside one round trip) leaves the mark and the Steal cooldown spent, so an overdue mark then moves, as the design's table says for a refused cast. (e) The per-victim steal lock removal (section 10) is NOT done in this slice (it binds no shipped kit); it stays a separate cleanup. (f) `P0` keeps its own think loop: the bot's priority list and P0's are mirrored by the drift guard `ScriptedPolicyTests.P0_CallsTheGamesOwnFunctions_NotCopiesOfThem`, extended to every loot call in both; no shared pure "think" function was extracted (the two loops read different views of the world). (g) The perk text of the Thief and the Spoiler is rewritten (no promise the kit no longer keeps); the words are provisional: slice B and `narrative-designer` own them.

**What a proxy can read for slice B.** `ThiefLoot.MarkedTarget`, `State` (`Live` / `Overdue` / `Waiting` / `Off`), `MarkAgeSeconds`, `MarkLive`, `IsMarking(rival)`, `Facts` (the numbers), and, for "the mark is coming", the Thief's Steal slot cooldown with `State == Waiting`; the event `ChickenLocalEvents.LootTaken` (victim id, take, bounty gained). A "mark moved" cue is the edge of `MarkedTarget` changing with the Thief unchanged.

**Simulator against the shipped game.** The match simulator reads the shipped assets, so the match report of the shipped state (`Captures/oracle/match-report-20261124-172703.md`) is the what-if scenario "Thief cap 20, execute bounty 17" of `match-whatif-20261124-152207.md`, and reproduces it inside the margins (win rate, P0 / P1 / mixed, report against what-if): Slippery 26.3 / 38.2 / 33.2 (26.3 / 38.7 / 33.2), Featherfoot 29.0 / 40.0 / 33.4 (29.1 / 40.1 / 33.5), Hoarder 49.7 / 15.7 / 24.9 (49.7 / 15.6 / 25.0), Bulwark 25.7 / 35.9 / 30.4 (25.7 / 36.0 / 30.6), Relentless 6.1 / 7.1 / 8.9 (6.1 / 7.0 / 8.9), Bully 19.9 / 13.7 / 15.3 (19.8 / 13.9 / 15.3), Spoiler 20.9 / 24.5 / 30.0 (20.9 / 23.8 / 29.5), Thief 22.3 / 24.9 / 23.9 (22.5 / 25.0 / 23.9); every margin is 1.0 to 2.1 points. The part 2 reference holds a fourth match record of this state (a record, not an acceptance); part 1's entries and prices are unchanged.

**Live check (four clients, Windows dev-tools build, 2026-11-24).** The host played the Thief as the fighter personality, taught to rob its loot-marked rival (tooling in `FighterTactics`, not gameplay code). 8 rounds: 21 marks drawn, 13 marked steals paid, each seen on all four peers; one marked steal with a take (victim `steal ... amount=5.00` once, Thief `loottake ... stolen=5.00 bounty=10.00 unfit=0.00` once, 0.18 s later), twelve on an empty beak (bounty once each); `lootdraw candidates=2` after every take; the clock prize banked in all 8 rounds; no sync, ghost, invariant or error finding. Not seen live: a mark that timed out or moved (the fighter keeps Steal ready, so an old mark stays overdue); covered by the `LootRules.Advance` tests only.
