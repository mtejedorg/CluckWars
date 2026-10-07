# Menu audio — MANIFEST

Menu UI sound set for the menu overhaul, Phase 3 (`docs/superpowers/specs/2026-10-06-menu-ui-overhaul.md`).
Every file was **procedurally synthesised** (numpy/scipy). There are no samples, so there are no licence
obligations. The generators live in `Source~/`, which Unity ignores. To regenerate, run
`python Source~/synth.py` for the SFX and `python Source~/music.py` for the menu loop and `python tools/audio/match_music.py` (repo root; tracked, unlike `Source~/`) for the match loops. Run
`python Source~/analyse.py` to print peak/loudness numbers and a spectrogram sheet.

Format: WAV, 48 kHz, 16-bit PCM. SFX are mono and the music is stereo.
Mastering: SFX peak at -3 dBFS or lower. Each one's loudest 100 ms (K-weighted) is matched to about
-15 LUFS. `ui_back` (-18.5) and `slot_arm` (-23) are quieter on purpose. Each SFX has a 1 ms fade-in and
an 8 ms fade-out. The music is at -16.0 LUFS integrated with a -3.2 dBFS peak.

Play everything through `IAudioService`. Use `PlaySFX(clip, volume)` for SFX and
`PlayMusic(clip, volume, loop: true)` for the loop. All SFX are 2D UI sounds, so set spatial blend to 0
and route them to the `SFX → UI` mixer group. The music goes to `Music`.

| File | Purpose | Duration | Suggested volume | Trigger |
|---|---|---|---|---|
| `ui_tap.wav` | Generic button press: wooden "tok" | 70 ms | 0.7 | `clicked` on any menu button with no more specific sound below. Pitch-randomise ±5% so repeated taps don't sound robotic. |
| `ui_back.wav` | Back / Home: softer, lower tok | 90 ms | 0.7 | Back, Home, Cancel, closing a modal, and Esc / Android back. |
| `ui_select.wav` | Pick a class tile or perk: bright pop with an upward sweep | 160 ms | 0.8 | Class tile or perk tile becomes selected. On a class pick, play it together with the class cluck (cluck at about 0.05 s later). |
| `cluck_warrior.wav` | Warrior picked: gruff "buk-BAWK" | 520 ms | 0.9 | Warrior selected on chicken select. Also usable for the live-stage hop. |
| `cluck_speedy.wav` | Speedy picked: quick high chirp run | 410 ms | 0.9 | Speedy selected. |
| `cluck_fatty.wav` | Fatty picked: low, round "bwok… bwooo" | 590 ms | 0.9 | Fatty selected. |
| `cluck_assassin.wav` | Assassin picked: hiss, then a sly falling cluck | 600 ms | 0.9 | Assassin selected. |
| `slot_arm.wav` | Arm a loadout slot: soft click | 45 ms | 0.6 | A loadout slot enters the "armed / waiting for ability" state. |
| `equip_thunk.wav` | Ability lands in a slot: wooden thunk | 280 ms | 0.9 | When the fly-to-slot animation **lands**, not when it starts. |
| `clear_pop.wav` | Ability removed: small falling pop | 90 ms | 0.7 | A slot is cleared. |
| `ready_stamp.wav` | READY pressed: rubber-stamp thump plus two-note jingle | 610 ms | 1.0 | At the frame the READY stamp animation hits. Don't play it again on un-ready; use `ui_back` for that. |
| `countdown_tick.wav` | 3-2-1 tick (C6 marimba tick) | 140 ms | 0.8 | Once on each of 3, 2 and 1. Optionally raise pitch by 1.0 / 1.06 / 1.12. |
| `countdown_go.wav` | GO burst (A-major chord stab, scoop and thump) | 600 ms | 1.0 | At GO. |
| `match_sting.wav` | Match starting: 4-note rising fanfare into a held C-major chord with a cymbal | 1.75 s | 1.0 | When the match is confirmed to start, after the START MATCH countdown or at scene handoff. Duck or stop `menu_loop` first. |
| `menu_loop.wav` | Menu music: G major, 120 BPM, 16 bars. Banjo-ish boom-chick with a plucked lead (bars 1–8) and a whistle lead (bars 9–16) | 32.00 s (exactly 16 bars) | 0.6 | Bootstrap menu, chicken select, loadout and lobby. Set loop on. Fade in over about 0.3 s on first start. Fade out over about 0.4 s before `match_sting`. |
| `match_loop.wav` | Match music: A major, 128 BPM, 16 bars. Four-on-the-floor kick, clap on 2/4, 16th shaker, bouncing octave bass, banjo offbeat chicks, marimba lead (bars 1–8), banjo lead + marimba arps (bars 9–16). Master dip at 2.5 kHz (−4 dB) so it sits under SFX; 1–4 kHz is 0.7% of its energy (menu_loop: 7.6%). −16.0 LUFS, −3.2 dBFS peak | 30.000 s (exactly 16 bars, 1,440,000 frames) | 0.6 | `AudioRegistry.MatchMusic`; `GameManager` plays it at GO (loop on). Generator: `tools/audio/match_music.py`. |
| `match_loop_intense.wav` | Optional last-10-s layer: same grid, same length and rotation as `match_loop` (sample-aligned, so it can be crossfaded at the same playback position), adds tambourine, louder 16ths, octave-doubled lead, pickup kick. Same gain stage, so −14.9 LUFS, −3.2 dBFS peak | 30.000 s | 0.6 | Not wired: no registry field or runtime switch exists yet. |

## Unity import notes
- SFX: use **Decompress On Load** with ADPCM or PCM, and keep **Load In Background** off.
  They are short, and the latency matters more than the memory.
- `menu_loop.wav`: use **Streaming** or **Compressed In Memory** with Vorbis at quality about 70.
  The audio is circular: every note and reverb tail that runs past the end is wrapped onto the start.
  The seam jump (0.0005 / 0.0014) and its second difference are far below what the track does
  between ordinary samples (99.9th-percentile step is 0.09). The file starts on a near-zero sample.
  **Listen to the seam in the Editor after import**, because Vorbis encoding can occasionally add a
  tiny gap. If it does, switch this one clip to ADPCM.
- Verified: no file has a non-zero first or last sample, and no SFX is above -3 dBFS.
