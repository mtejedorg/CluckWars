# Thief crown and clock prize: design (BL-217)

Date: 2026-11-23. Status: design for Maestro's agreement. No plan, no code.
Inputs: Maestro's rulings of 2026-11-23 (quoted below), `mechanics-designer` (opus) for the numbers, `ux-designer` (opus) for what each player sees, and the code read on `develop` at `3228fcf1`.

## 0. Why, and what is already ruled

The Thief kit wins 0.8 to 3.1 % against a 20 to 30 % target, and no numeric lever moves it past about +6 points
(`Captures/oracle/match-whatif-20261123-111254.md`, `Captures/oracle/match-report-20261123-001510.md`). In the mixed
population it makes 3.96 steals, steals 24.4 food and banks 15.2 per match; it forages nothing. In a mirror of four
Thieves every match is decided by the corner rule: nobody banks.

Maestro's rulings, binding:

- "Vamos a hacer corona + premio sólo para él (hay que meter feedback visual y demás para la recompensa). Pero nada que vaya directo a base, todo al bolsillo."
- "Yo esto lo haría por sorteo, como con el asesino. Que pese mucho la comida en base, algo menos la del bolsillo, y un plus a los que tengan más de 25."
- "Comida nueva, como con el asesino."
- "Pasa sólo al thief, ya veremos si lo recuperamos en un futuro."
- Simulator first.

Not touched here (another agent is applying it now): Hunt 5 s with the kill in the last 2 s, `ExecuteBounty` 15.
`MatchConfig.asset` already reads `ExecuteBounty: 15` in the working tree (uncommitted, theirs).

## 1. What exists today (verified in code)

- **Steal** is `SnatchAbilitySO` (`Assets/_Game/Data/Abilities/Snatch.asset`): 5 food from **every** rival carrier within
  5.16 m, cooldown 4 s. Only a carrier is a target (`ExtraTargetFilter`: `cargo.Stealable > 0`). The Thief's 1.6x comes
  from `ThiefPassiveSO.ModifyStealAmount` (`Thief.asset` `StealMultiplier` 1.6). The take is
  `StealMath.Clamp(value, free room, victim cargo + bag)` in `ChickenCargo.StealFrom`; the victim's authority drains and
  reports back (`RPC_DrainStolen`, `RPC_SettleSteal`), and only that report credits the thief.
- The Thief kit (`Thief.asset` `Kit`): Snatch (Steal), Shadowstep (Jump), Invisibility (Hide), FeatherAura (Slow Area).
  Assassin `CargoCapacity` 10 (`Classes/Assassin.asset`).
- **The bounty bag** is `ChickenCargo.BountyBag` (`[Networked]`): ignores capacity, is drained beak first then bag
  (`StealRules.SplitDrain`), banks at the base. The execute bounty is paid into it by
  `AssassinExecute.PayExecuteBounty`, on the Assassin's authority, after the victim's authority reported the kill.
- **The mark pick** is `MarkPickRules` (pure, Gameplay assembly): `Filter` (drop the last mark unless it is the only
  rival), per-candidate factors, `PickWeighted` (highest `roll * factors`, uniform fallback roll), rolls injected by the
  caller (`AssassinExecute.PickWeighted` uses `Random.value`; the simulator uses the mark RNG stream).
- **The clock prize** is `SpoilerPassiveSO.MatchEndBonusFood` (`Spoiler.asset` `BonusFood` 15). `GameManager.EndOnTimerExpiry`
  calls `AwardMatchEndBonuses` first, which banks each passive's bonus into the home base, then picks the winner by base
  `FoodTotal`. Beak and bag food are not counted at the clock. `MatchConfig.SpoilerBounty` 15 is read by no code.
- **The simulator** already reads `passive.MatchEndBonusFood` into `PassiveFacts.MatchEndBonusFood` (`MatchKits.cs`) and
  pays it in `MatchSim.Finish` before `PickTimerWinner`. Its Steal (`MatchSim.Cast`, `AbilityJob.Steal`) loops every
  rival in range with `Stealable > 0` and calls `DoSteal`, which uses `MatchCombat.StealTake`. RNG streams:
  `StreamComeback` 100, `StreamMark` 101, `StreamSetup` 102 (`MatchRng.cs`).
- What-if scenarios are balance deltas over asset fields (`MatchWhatIf.cs`, `BalanceBook.Evaluate`), so every new number
  below must be a serialized field on an asset to be swept.

## 2. The rule as a child hears it

1. One rival wears your crown. You can see who, and so can everyone.
2. Rob the crowned bird with your Steal and you take the crown: 10 new food drops in your bag.
3. A new crown appears on someone else 2 seconds later.
4. Birds with lots of food at home get the crown most often.
5. If nobody steals the crown in 12 seconds, it jumps to someone else.
6. If time runs out and nobody won, the Thief gets 20 extra food at home.

## 3. The crown rule, end to end

**Who holds the state.** Each Thief owns one crown. It is the Thief's state, decided on the Thief's authority, as the
mark is the Spoiler's (`AssassinExecute.MarkedTarget`).

**When it is drawn (recommended: automatic).**

- At GO of every round (after the round-start pause), each Thief draws a crown.
- 2 s after it is taken (`CrownGapSeconds` 2), a new one is drawn.
- If it is not taken within 12 s (`CrownSeconds` 12), it is redrawn at once (no gap).
- If the crowned bird leaves play (executed), the crown ends with no payout and the gap runs.

Why: no button, no fifth slot, no second meaning on Steal, and the bot needs no "when to draw" logic. Rejected:
"press Steal to draw" (Steal would mean two things and kids would not find the draw); "redraw when the crowned bird banks
or is robbed by someone else" (the leader banks every 5 to 8 s, so the crown would flicker and he could shed it by doing
his normal job). Arithmetic (estimate): 12 s covers three Steal casts (cooldown 4 s) plus the walk in; a cycle averages
about 7 s when taken plus the 2 s gap, 14 s when it times out; a 32 s match offers about 2.7 crowns and the Thief takes
about 1.6 to 2.

**Who can wear it.** Every rival of that Thief that is in play: not the Thief, not a same-corner ally, not a decoy, not
removed. The last crowned bird is filtered out unless it is the only rival (`MarkPickRules.Filter`, reused as is).

**Does it move.** No. It stays on one bird until taken, timed out or its wearer is removed. It does not follow the leader
between draws.

**Taking it.** A Thief Steal cast whose area contains the crowned bird takes the crown when all of these hold:
the bird is in Steal range (5.16 m), no claimed base wall lies between them (the existing ability pair rule), and the bird
is not steal-proof (`StealRules.CanStealFrom`). The crowned bird is a valid Steal target **even with an empty beak**
(the crown is worth stealing): `ExtraTargetFilter` accepts `Stealable > 0` **or** "crowned by this caster". The normal
clamped steal still runs on top, on every carrier in range, unchanged.

**Edge cases.**

| Case | What happens |
|---|---|
| Crowned bird at home, behind his wall | Steal cannot reach him (existing wall rule). The 12 s timer keeps running; no payout. |
| Crowned bird shelled (Egg Shell, steal-proof) | No payout, no steal; the Thief sees SAFE. Timer keeps running. |
| Crowned bird hidden (Hide) | Hide hides the crown too (bystanders and Thief lose it; the bot keeps the last seen spot per `BotSightRules`). A Steal that really covers him still takes it. Timer keeps running. |
| Crowned bird executed by a Spoiler | Crown ends, no payout, counts as the last crowned, gap runs. |
| Thief executed | His crown keeps its timer; his bag stays (it is his own food, as today). |
| Thief stunned | Cannot cast; timer keeps running. |
| Round reset (PLAY AGAIN) | Crown cleared without effect; drawn again at GO. |
| Two Thieves | One crown each, drawn independently; both may crown the same bird; each pays once to its own Thief. |
| Thief and Spoiler on one bird | Allowed, no exclusion. A kill ends the crown with no payout. |

## 4. The lottery: `CrownPickRules` (pure, Gameplay assembly)

A new static class next to `MarkPickRules`, with every roll injected, so the game and the simulator call the same rule.

- `Filter`: reuse `MarkPickRules.Filter` (last crowned out unless only one rival).
- `Weight(banked, carried, tuning) = Base + BankedWeight * banked + CarriedWeight * carried + (banked > OverThreshold ? OverBonus : 0)`.
  "Carried" is beak plus bounty bag (what a steal could take).
- `Pick(weights, roll)`: proportional. Walk the candidates in the caller's order and return the first whose running sum
  passes `roll * sum`. Chance = weight / sum. One roll in [0, 1). No ties are possible (a boundary goes to the earlier
  index). With no candidate it returns -1; with every weight 0 (cannot happen while `Base` > 0) it falls back to
  `MarkPickRules.Pick(count, roll)`.

Why proportional and not the mark's "highest roll x score": the numbers then read directly as chances, which Maestro can
tune. Max-of-rolls quietly over-favours: weights 2:1 give 75 %, not 67 %.

No position factor. The mark weighs distance from home so it lands on a reachable bird inside 5 s; the crown lasts 12 s
and times out, and a home factor would make hiding at home a way to dodge the crown.

**Starting numbers (mechanics-designer, estimates), fields on `Thief.asset`:**

| Field | Value | Why |
|---|---|---|
| `CrownBaseWeight` | 4 | Keeps an empty bird drawable; the early draw is near uniform. |
| `CrownBankedWeight` | 1.0 per food | "Pese mucho la comida en base." |
| `CrownCarriedWeight` | 0.5 per food | "Algo menos la del bolsillo." |
| `CrownOverThreshold` | 25 banked | "Más de 25." Stored as a fraction of `FoodTargetToWin` (0.625 x 40) so it follows `MatchConfig`. |
| `CrownOverBonus` | 10 | "Un plus." |

"Más de 25" counts **banked only** (recommended): banked is the real lead and the standings row shows it; carried food
drops to 0 at every deposit, so the bonus would flicker. Alternative in section 10.

Worked examples (three rivals):

- Early, banked 0/0/0, carried 3/0/6: weights 5.5 / 4 / 7, sum 16.5, chances 33 / 24 / 42 %.
- Late, banked 28/18/10, carried 4/6/0: weights 4+28+2+10 = 44, 4+18+3 = 25, 4+10 = 14, sum 83, chances 53 / 30 / 17 %.

## 5. The reward

- **10 new food per crown** (`CrownReward` on `Thief.asset`), paid **once per crown**: the steal that takes it pays, the
  crown is gone. Not once per steal: that would turn one leader into a 4 s tap.
- New food, not taken from the victim (as the execute bounty). It goes into the Thief's **bounty bag**: ignores capacity,
  stealable beak first then bag, banked at his base like the execute bag. Nothing goes straight to base.
- **Flat, not multiplied by 1.6.** The multiplier tunes the normal steal; 1.6 x 10 would couple two levers.
- **Independent of the clamp.** It pays when the Thief's beak is full (the bag ignores capacity) and when the crowned
  bird's beak is empty. It does not pay when the steal does not reach him (range, wall, shell).
- Arithmetic: the Thief keeps 15.2 of 24.4 stolen (about 62 %). Assume about 30 % of a bag is lost to rival steals.
  To gain about +13 banked: 1.75 crowns x R x 0.7 = 13, so R is about 10.6. Start at 10.
- Caveat: winners reach 40 at about 32 s. Lifting the Thief's mean bank to about 28 does not make him win most games; it
  lets his good games reach 40, which matches a 20 to 30 % target and not more.

## 6. The end-of-clock prize, Thief only

- `ThiefPassiveSO` gains `ClockPrize` = **20** and overrides `MatchEndBonusFood` to return it. Paid exactly as the
  Spoiler's is today: `GameManager.AwardMatchEndBonuses`, banked into the home base first, then the winner is picked.
  This is the moved passive, unchanged in mechanism; the "todo al bolsillo" ruling governs the crown reward, and at the
  clock there is no pocket left to fill.
- Stall check (the Spoiler's old rule: beat a stalemate leader, never win from zero): an idle Thief at 0 gets 20; a
  clock-ended match has a leader at 28 to 39 (estimate), so idling loses. A mid-pack Thief at 15 + 20 = 35 beats a
  30 leader. 25 is borderline: in a heavily disrupted match a leader at 22 to 24 loses to an idler.
- Limit: only 5 to 10 % of matches reach the clock, so the prize moves the win rate by at most that much. The crown is
  the main lever.
- Food in the bag at the clock is not banked (unchanged), so a Thief who never banks loses.

**The Spoiler's passive after the move (recommended).** Keep the signature passive (`Spoiler.asset`, display name
"Assassin"), set `BonusFood` 0 (or remove the override), and re-text it around a behaviour that already ships:
"Your Hunt picks the leader {x} times as often", where `{x}` reads `MarkKill.asset` `LeaderPickWeight` (2). Zero balance
effect, the perk line stays true, and the number comes from the data. The Spoiler's specialization text loses the
clock-prize sentence (`UiKeys.PerkSpoilerLine` / `PerkSpoilerDetail` and their `UiText.csv` rows; GDD 5.3 table row).
The Thief's specialization text gains the crown and the prize: "Rob the crowned bird for +{reward}. +{prize} at home if
time runs out." (numbers from `Thief.asset`).

## 7. The kit

No slot changes. Slot 1 stays Steal (income); the crown is earned through it, so income still lives on slot 1. Jump,
Hide and Slow Area keep slots 2 to 4. No fifth button: the draw is automatic. `FixedKitTests` slot-order guards are
unaffected.

## 8. Feedback (ux-designer)

Verified by the ux-designer: pop words `SAFE`, `MISSED`, `HUNT!`, `ROB -{n}`, `+{n}` in
`Assets/_Game/Resources/Text/UiText.csv` with keys in `UiKeys.cs`; standings skull `LbSkull{i}` in `MatchHudController`;
`MarkHudController` (marker disc, `HunterSkull` badge on the hunter's arrow); `FloorRivalArrows.TryRepresentEmphasised`;
`ChickenStatusBadges` (badge stack over the nameplate); `FeedbackPaletteSO` has a Death Mark block and no crown colour.
Not verified: whether the cargo bar shows the bounty bag (no file under `UI/` or `Visuals/` reads `BountyBag`).
Everything else here is proposal. All of it is local presentation (ChangeDetector + Render), driven by the replicated
crown state.

| Party | What they see and hear | Reuses |
|---|---|---|
| **Crowned rival** | Gold crown on his own head (badge slot). `CROWNED!` pop when it lands, one short buzz. The Thief's floor arrow carries a small crown badge, so he knows who wants it and from where (two Thieves: two arrows). No tint. Timeout: the crown fades in 0.3 s, no word. Taken: a local crown-pop puff, no word (he loses no food to the crown). Counterplay needs no extra cue: run home, shell, hide, stun the Thief. | `ChickenStatusBadges`; victim's `HunterSkull` on the arrow pattern; Buzz When Hit (H8a); mark fade end rule. |
| **Thief** | Target outside the floor circle: its arrow forced on, in gold, crown badge on the arrow. Inside: a crown disc over the head (paw swapped for crown). Gold ground brackets only when the target is in Steal range ("fire now"). Steal hex: gold crown dot while a crown is live, pulse when the crowned bird is within 5.16 m; no ring (no window to drain). Take: `CROWN +10` over the Thief, hit-confirm sound, strong buzz; the `+{n}` grab pop is suppressed for that 10 so it is not counted twice. Gap: empty crown outline on the badge, then the new target's arrow lights. Refused (wall, shell): `SAFE`. Timed out: `MISSED`, then the redraw. | Hunt hunter spec items H1a, H2a (dot), H4a (end words), H5a revision (badge on the floor arrow), H8a; `MarkHudController` disc. |
| **Bystanders** | The crown on the crowned bird's head (public: the lottery favours the leader and everyone should know). At a take, a gold feather burst on the crowned bird and the Thief's `CROWN +10`, drawn for rivals too. No arrows, hexes or tint. | Kill feather burst (FEEDBACK 3.4) as the template. |
| **Standings row** | A gold crown icon by the crowned bird's name; two crowns on one row draw one icon with "2". Skull first, crown after, both at 0.8 scale when both are present. | `LbSkull{i}` slot logic in `MatchHudController`. |
| **Clock prize** | At time-up, before the winner banner, a 1.2 s beat: `THIEF +20` flies from the Thief's row into his base total, which ticks up. Everyone sees it; the banner waits. Strong buzz on the Thief's phone. New (today's Spoiler +15 has no beat at all). | Standings row. |
| **Spoiler** | Nothing new in the match; only the passive line changes. | - |
| **Crown and Hunt mark on one bird** | Crown stays on the head, the Hunt disc sits above it. One arrow per relationship: the Spoiler's carries the paw, the Thief's the crown; no player sees both badges on one arrow. The victim's own arrows separate the threats (skull vs crown). The K13 tint exemption stays Hunt-only. | K13; H13a badge placement. |

PC and phone: the same elements; the hex dot and pulse are on the touch hex and the desktop strip alike.

New words (`UiKeys` + `UiText.csv`): `hud.pop.crowned` CROWNED!, `hud.pop.crownTake` CROWN +{n}, `hud.prize.thief`
THIEF +{n}, `hud.badge.crown` CROWN. New palette fields in `FeedbackPaletteSO`: `CrownGoldColor`, `CrownEmptyColor`
(never literals in `FeedbackTuning`, D3a).

Not needed: crown tint or vignette, crown clock pill, countdown, distance chip, new SFX authoring (reuse hit-confirm and
the Hunt sting, BL-133), a pop on the victim at the take.

## 9. Counterplay and abuse

| Concern | Answer |
|---|---|
| Kingmaking | The crown leans to the leader, so the Thief robs the leader and helps the trailers: the reverse of kingmaking. The residual risk is the crown landing on second place and giving the leader a free run; the over-25 bonus damps it. Measure: the leader's win rate with and without a Thief in the match. |
| A Thief that never banks | The bag is stealable (beak first, then bag) and bag food does not count at the clock, so hoarding loses. |
| Stalling to the clock | 20 cannot win from zero (section 6). Watch the share of matches on the clock (go/no-go below). |
| Crown plus Hunt on one bird | Allowed; both threats are readable apart (section 8). A kill ends the crown without payout, so the two Assassins compete for the same bird rather than stacking on it. |
| Leader hiding at home | The crown times out after 12 s, and the leader forages nothing meanwhile: a cost he chose. If the sim or playtest shows it is common, try 8 s. |
| Two Thieves farming one bird | Each Thief pays once per own crown; the last-crowned filter keeps either from crowning the same bird twice running. |

## 10. Simulator slice first (the smallest version that measures the idea)

**Where the rules live.** `CrownPickRules` (lottery, section 4) and `CrownRules` (pure: `CanTake(inRange, walled,
stealProof)`, `NextDrawTime`, `Pays`) in `Assets/_Game/Scripts/Gameplay/`, so `MatchSim` (Balance folder, same Gameplay
assembly through its `.asmref`) and later the game call the same code. Numbers on `ThiefPassiveSO` fields:
`CrownReward`, `CrownSeconds`, `CrownGapSeconds`, `CrownBaseWeight`, `CrownBankedWeight`, `CrownCarriedWeight`,
`CrownOverFraction`, `CrownOverBonus`, `ClockPrize`. That makes every one a what-if delta.

**New facts and state.**

- `PassiveFacts` (`MatchKit.cs`): a `Crown` block with the fields above (zero reward = no crown); `MatchKits` reads them
  from `ThiefPassiveSO`. `MatchEndBonusFood` already flows through, so the prize needs no sim code.
- `MatchSim.Chick`: `CrownTarget` (-1 none), `CrownEnd`, `CrownNextDraw`, `HasLastCrown`, `LastCrown`.
- A new RNG stream `StreamCrown` (103), so the mark stream and the common random numbers of every other draw stay as
  they are.
- `Cast`, `AbilityJob.Steal`: the crowned bird is a valid target with an empty beak; when the cast covers it and
  `CrownRules.CanTake` holds, add `CrownReward` to the caster's `Bag`, end the crown, set the gap.
  A crown upkeep step beside `MarkUpkeep`: draw at GO, after the gap, on timeout, end on removal.
- `Remove`: a removed wearer ends every crown on him; `Respawn` unchanged.
- `SeatView`: the seat's own `CrownTarget` and, per rival, "crowned by a Thief" (for P1's flee logic later; P0 ignores it).
- Counters: `CrownsOffered`, `CrownsTaken`, `CrownFood`, `CrownTimeouts`, `CrownBlocked` (wall or shell);
  `MatchEventKind.Crown` in the event list.
- `MatchReport` / `MatchWhatIf`: Thief rows for crowns offered and taken, crown food, bag food lost to steals, clock prize
  paid, and the Thief-mirror corner-decided share.

**P0, the game's bot (it must be taught too).** The bot's prey choice is `BotTactics.TargetPriority` (shared by
`BotController` and `ScriptedPolicy`). Add a crown term: the bot's own crowned rival scores as a full load
(`cargoFraction` 1) whatever it carries, and stays prey inside `HuntRadius`. The cast plan treats "crowned rival in
reach" like "carrier in reach". Nothing else: no fleeing from a crown, no crown-aware banking. The game bot will do the
same through the same `BotTactics` change.

**P1, the utility policy.** `BestSteal`: for the crowned rival, the value adds `CrownReward` to the take and the
`take < 1` skip does not apply; the bag-at-risk part of banking already covers the reward. Optionally the crowned rival
weighs running home higher (measure separately).

**What-if runs** (P0, P1 and mixed; every row with `ExecuteBounty` 15 and `Spoiler.asset` `BonusFood` 0):

- Stage A, 18 scenarios: `CrownReward` {0, 6, 8, 10, 12, 15} x `ClockPrize` {15, 20, 25}; weights W1 = (4, 1, 0.5, 10);
  `CrownSeconds` 12. The task's 4 is covered by 6 and 0 at the ends; add {4} if 6 already overshoots.
- Stage B, 9 scenarios on the best Stage A row: weights W0 uniform (1, 0, 0, 0), W1, W2 leader-heavy (2, 1.5, 0.5, 15)
  x `CrownSeconds` {8, 12, 16}; clock prize 30 once as a stall probe.

**Go when all hold:** the Thief at 20 to 30 % under P0, P1 and mixed (inside the margins); the Spoiler stays 22 to 30 %;
no other kit moves more than 5 points; matches on the clock at 12 % or less; mean length no more than 2 s shorter;
the Thief mirror no longer 100 % corner-decided; 1.2 to 2.5 crowns taken per match.
**No-go when any holds:** the Thief above 35 % under any policy; any kit moves more than 8 points; clock share above 15 %.
The oracle is a guide: Maestro decides on the numbers.

## 11. The game slice after that (list, not a plan)

- **Networked state and authority.** A `ThiefCrown` NetworkBehaviour on the chicken (added only for the Thief, as
  `AssassinExecute` is for the Spoiler): `[Networked] CrownedTarget`, `CrownTimer`, `GapTimer`; last crowned is
  authority-local. Drawn on the Thief's authority with `CrownPickRules` (rolls from its RNG). Assembly: Gameplay; no
  new Fusion assembly, so `AssembliesToWeave` is unchanged.
- **Reward, authority-confirmed.** At the cast, the Thief's authority records a crown expectation for the crowned bird
  (a `PendingStealLedger`, as `ExpectExecuteLoot`). The crowned bird's authority confirms the take (not steal-proof,
  within `StealRules.IsWithinStealRange` plus margin, no wall) through a new receiver-validated RPC; only a confirmation
  that matches a pending expectation inside its window pays `CrownReward` into `BountyBag`, once. A refused confirm
  leaves the crown in place. `StealRules.MaxSingleSteal` is unaffected (the reward is not a drain).
- **Rules.** `SnatchAbilitySO.ExtraTargetFilter` accepts the caster's crowned bird; `CrownPickRules`, `CrownRules`;
  removal (`ChickenCombat` execute) and round reset clear crowns; decoys never drawn.
- **Passives.** `ThiefPassiveSO`: crown fields, `ClockPrize`, `MatchEndBonusFood` override, `PerkArgs`.
  `SpoilerPassiveSO`: `BonusFood` 0 or override removed, new text args (`LeaderPickWeight`). `GameManager`
  `AwardMatchEndBonuses` comment ("Only Spoiler returns anything today") updated.
- **BalanceBook.** New fields are tunable by delta; none goes on the deny list; `tools/balance/schema.py` mirrors that.
- **HUD and VFX.** Section 8: badge, arrow badge, disc, brackets, hex dot and pulse, standings icon, pops, prize beat,
  palette fields, haptics. Check the cargo bar shows the bag.
- **Bots.** `BotTactics.TargetPriority` crown term (shared with P0), `BotController` cast plan.
- **Text.** `UiKeys` and `UiText.csv` rows (section 8), Thief and Spoiler perk lines with numbers from the data.
- **Tests.** EditMode: `CrownPickRules` (weights, proportional pick, filter, fallback), `CrownRules`, the empty-beak
  target filter, the ledger confirm (forged and late confirms refused), the clock prize paid before the winner, the
  Spoiler paying nothing; `FixedKitTests` unchanged; simulator parity tests for the crown.
- **Docs.** GDD 5.3 (Sneaky table rows), 6.2 (crown), 6.4 (Spoiler no prize), `docs/FEEDBACK.md`, `docs/site/index.html`
  (republish), `docs/STATE.md`; `librarian` for `docs/FILE_STRUCTURE.md` and the rules docs.

## 12. Open decisions for Maestro

1. **Crown reward.** A) 8, B) 10, C) 12. **Recommend B**: it closes the +13 gap in the arithmetic; the sim will tell.
2. **Thief clock prize.** A) 15 (a straight move), B) 20, C) 25. **Recommend B**: it passes the stall check with margin; 25 is borderline.
3. **What "más de 25" counts.** A) banked, B) banked plus carried. **Recommend A**: the real lead, shown in the standings, does not flicker at each deposit.
4. **The Spoiler's passive.** A) keep it, prize 0, text "Your Hunt picks the leader {x} times as often" (existing `LeaderPickWeight`), B) remove it, C) a new small perk. **Recommend A**: zero balance change and no empty perk line.
5. **Show `CROWN +10` to rivals too** (an exception to D7b, "self-pops are never drawn for rivals"). A) yes, B) only the Thief. **Recommend A**: a 10-food swing everyone needs to read.

Decided in this design without asking (say so if wrong): automatic draw (no button), once per crown, flat reward not
multiplied, pays on an empty beak, Hide hides the crown, no position factor in the lottery, 12 s timeout and 2 s gap as
starting values for the sim to sweep.

## 13. Assumptions and what was not verified

- All numbers in sections 3 to 6 are estimates for the simulator to test, not measurements.
- The 30 % bag-loss share and the 1.6 to 2 crowns taken per match are estimates.
- Whether the cargo bar shows the bounty bag is not verified.
- The simulator does not model a placed Slow Area zone or body collision (its own list); neither changes the crown.
- `MatchConfig.ExecuteBounty` 15 is uncommitted work of another agent at the time of writing.
