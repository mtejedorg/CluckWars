# Death Mark audio cues

Four local 2D one-shots, each heard only by the player concerned. Registry fields live under the "Death Mark" header in
`AudioRegistrySO` (`Assets/_Game/Data/AudioRegistry.asset`). Play through `IAudioService.PlaySFX(clip, volume, pitch, delay)`:
`_audio?.PlaySFX(_audioReg != null ? _audioReg.MarkLock : null, vol, pitch)`. The registry has no per-cue volume fields, so
volume and pitch are call-site constants (put them in `FeedbackTuning`, like `FizzleSfxVolume`).

Common: mono, 48 kHz 16-bit WAV, peak -3 dBFS or lower, 1 ms fade-in / 8 ms fade-out, spatial blend 0, mixer group
`SFX -> UI` (until a dedicated Ability/Combat route exists; these are feedback cues, not world sounds). Decompress On Load,
Load In Background off. Reference loudness: `ready_stamp` and `equip_thunk` sit near -15 LUFS (short-term); `slot_arm` is
about -23 LUFS.

**All four wired clips are PLACEHOLDERS** reusing existing UI wavs. None is a production asset.

| Cue | Who hears it | Trigger | Placeholder now | Call volume | Call pitch |
|---|---|---|---|---|---|
| `MarkDrawTick` | Assassin | each step of the ~0.6 s highlight down the 4 standings rows | `Audio/UI/slot_arm.wav` | 0.8 | 1.00 + 0.05 per step (cap 1.35) |
| `MarkLock` | Assassin | highlight lands on the target | `Audio/UI/equip_thunk.wav` | 0.9 | 1.00 (1.05 optional) |
| `MarkVictim` | Victim | becomes the Death Mark target | `Audio/UI/ready_stamp.wav` | 0.9 | 0.75 (drops the jingle into a minor, ominous register; about 0.8 s) |
| `MarkKillReady` | Assassin | Kill becomes possible (target stunned and in reach) | `Audio/UI/countdown_tick.wav` | 0.8 | 1.30 (sharp, short) |

Each placeholder is distinct, because `DataIntegrityTests.AudioRegistry_Exists_AndNoClipIsWiredToTwoDifferentCues` fails
if one clip sits on two registry slots. The wavs are also played by `UiAudioSO` cues; sharing a file between `UiAudioSO` and
the registry is fine for that test, and `UiAudioTests.ShippedCatalogue_UsesEveryWavInTheAudioFolder` is unaffected.
When a real clip replaces a placeholder, drop it in `Audio/Abilities/` (not `Audio/UI/`, which that test sweeps) and update
the `Audio/Abilities/MANIFEST.md`.

## Production briefs

### MarkDrawTick
- Purpose: audible ratchet while the highlight steps down the standings rows, so the draw feels like a roulette landing.
- Length: 40-60 ms. Up to 6-8 plays in 0.6 s, so it must be dry (no tail) and quiet.
- Character: soft dry wooden or finger-snap tick, mid pitch (about 1.5-2.5 kHz body), no sustained tone. It reads as a click, not a note.
- Layering: single layer. The caller raises pitch about 5% per step so the sequence climbs; do not bake a pitch into the file.
- Loudness: quietest cue of the set, near `slot_arm` (about -23 LUFS), well under the existing stings so 8 repeats stay unfatiguing.

### MarkLock
- Purpose: the highlight stops on the target; confirms "this is your mark".
- Length: 250-350 ms.
- Character: low wooden thunk with a short metallic "ka-chk" latch on top, like a crosshair snapping on. Confident, not triumphant.
- Layering: two layers, a thunk (80-150 Hz body) and a high latch click (3-5 kHz, under 30 ms). Land the latch about 40 ms after the thunk onset.
- Loudness: about -15 LUFS short-term, equal to `equip_thunk`; it is the loudest of the Assassin cues, because it is the payoff of the draw.

### MarkVictim
- Purpose: tell the victim "you are marked" immediately and unmistakably; it is the only warning they get.
- Length: 600-900 ms.
- Character: ominous short sting. A low, falling minor third (two notes) under a dry rubber-stamp or gavel hit, with a faint breathy hiss tail. Menacing, a little comic, in the farmyard tone, never horror.
- Layering: stamp transient, low pad or bass note, thin hiss tail. Keep 1-4 kHz energy modest so it sits under the match music's 2.5 kHz dip but still cuts through.
- Loudness: top of the range, about -14 to -15 LUFS, matching `ready_stamp` and `match_sting` peak impact (but far shorter than `match_sting`'s 1.75 s). It plays once per mark.

### MarkKillReady
- Purpose: "now press Kill". Fires when the target is stunned and in reach, and may re-fire several times per mark as that state toggles.
- Length: 100-150 ms.
- Character: sharp, bright two-note rising blip (a fourth or fifth up), blade-ring flavour, with a clean decay. Urgent, not melodic enough to be sung back.
- Layering: single layer. Avoid any low-end so a toggling state stays light.
- Loudness: about -17 to -18 LUFS, a notch under `MarkLock` so repeats are not annoying. Rate-limit at the call site (for example no re-fire within 0.4 s of the last play) in addition to keeping the clip short.

## Open items for senior-dev
- Add the volume and pitch constants to `FeedbackTuning`; there is no per-cue volume in the registry.
- Gate every call to the local player (Assassin or victim) and play in `Render()`/change-detection, not in an RPC.
- Replace placeholders with the real wavs when synthesised (generator: `tools/audio/menu/synth.py`).
