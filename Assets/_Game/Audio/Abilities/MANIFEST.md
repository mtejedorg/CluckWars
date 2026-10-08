# Ability cue audio - MANIFEST

In-match cues for the hold / release / cancel input model (Phase 6). Procedurally synthesised (numpy/scipy), no
samples, no licence obligations. Generator: `tools/audio/menu/synth.py` (`python synth.py ability_fizzle.wav
ability_cancel.wav`; it writes these two into this folder). Format and mastering as the menu SFX
(`../UI/MANIFEST.md`): WAV, 48 kHz, 16-bit mono, peak at or below -3 dBFS, loudest 100 ms matched to about -15 LUFS,
so the play volumes below are what keeps them soft. These are not UI cues: they are played by `AbilityController`
through `IAudioService` for the LOCAL player only, from `AudioRegistrySO`.

| File | Purpose | Duration | Volume | Trigger |
|---|---|---|---|---|
| `ability_fizzle.wav` | Soft airy "pfft" | 120 ms | 0.55 (`FeedbackTuning.FizzleSfxVolume`) | A target-gated move released with nobody in range. `AudioRegistry.AbilityFizzle`. |
| `ability_cancel.wav` | Soft low tick | 60 ms | 0.5 (`FeedbackTuning.CancelSfxVolume`) | A held move cancelled on purpose (touch edge band, Esc, right mouse). `AudioRegistry.AbilityCancel`. |
