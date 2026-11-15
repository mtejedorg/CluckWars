# Cluck Wars v0.1 (alpha playtest)

**Package:** `com.wugnar.games.wars.cluck` · **Builds:** `26458919` (Windows, Android APK) and `26458925` (Android Play bundle) · **Base:** since `v0.4.0-alpha`, 354 commits on `balance`

This is the first alpha for human testing. The ability set is simplified, and every kit is fixed. Free picking is off.

## What's new

**Fixed kits.** Each chicken specialization has one set of four moves. You don't pick abilities.

**Eleven abilities, one per job.** Each move has the same name and icon for every chicken that has it:

| Name | Short label | What it does |
|---|---|---|
| Peck | PECK | Take food from the pile you stand on |
| Steal | ROB | Rob food from every rival close to you |
| Stun | STUN | Stun every rival close to you |
| Shove | PUSH | Push every rival close to you away |
| Slow | SLOW | Slow every rival close to you for a few seconds, even after they leave |
| Slow Area | AREA | A longer slow: follows you, or leaves a patch on the ground |
| Speed | FAST | A short sprint |
| Jump | JUMP | A short dash, over walls |
| Egg Shell | EGG | Walk slowly, then hide in an egg: nothing can touch you |
| Hide | HIDE | Turn almost invisible for a moment |
| Hunt | HUNT, then POUNCE | Mark a rival; in the last second, press again to take their food |

**Range-only targeting.** Abilities hit everyone inside their circle. There are no cones and no aiming.

**Cooldowns** are short: 2 to 8 seconds.

**Classes and specializations.** Bully, Relentless, Featherfoot, Slippery, Bulwark, Hoarder, Spoiler and Thief each have a passive that changes how their kit plays. Featherfoot banks food a bit faster. Slippery shrugs off ability slows.

**Feedback.** Badges above each bird show its state: stunned, slowed, shelled, marked. Egg Shell has its own egg visuals. Ability hexes show cooldowns and timers.

**Ability text** is written in plain words and reads its numbers from the game data.

## What to test

- Pick a chicken and play a full match. Do the four moves match the names on screen?
- Use **Slow** on a group. Do they stay slowed after leaving the circle?
- Use **Slow Area**. Does it follow you, or stay where you cast it?
- Use **Hunt**. Does the pounce work in the last second, and does the text make it clear?
- Use **Egg Shell**. Can anything reach you during the egg? Can you still move during the first, walking part?
- Does any ability description run off the card on a phone?

## Known issues

- **Not yet played:** the new Slow and Slow Area, and their placed-patch mode, have not been run in the game.
- **Long texts:** Hunt and Egg Shell descriptions are long. Check that they fit the detail panel.
- **Ability sounds:** the six ability-feedback sounds (hit, miss, denied, shell open and close, hunt) are not made yet, so most of them are silent.
- **Speedy and Steal:** in free-pick mode only (not in this build), a Speedy can pick Steal.

## Install

- **Windows:** run `CluckWars.exe` from the `Windows` folder.
- **Android (sideload):** `adb install -r CluckWars-0.1.apk`. Uninstall any older Cluck Wars first if the signature differs.
- **Android (Play, internal testing):** use the join link on your phone, then install from Play. Uninstall the sideloaded APK first.

Please report what you see with the build number from the title screen or the build info.
