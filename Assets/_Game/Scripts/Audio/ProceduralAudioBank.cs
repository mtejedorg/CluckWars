using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>
    /// Synthesises all gameplay SFX procedurally at runtime so the game has sound
    /// without any recorded audio assets — the audio counterpart to the procedural
    /// placeholder meshes. <see cref="FillMissing"/> populates every null clip on an
    /// <see cref="AudioRegistrySO"/>; authored clips are left untouched, so dropping
    /// real `.wav`s into the registry later transparently overrides these.
    /// </summary>
    /// <remarks>
    /// Everything is pure math (sine/square/noise + ADSR-ish envelopes) rendered into
    /// a mono <see cref="AudioClip"/> at 44.1 kHz. Every clip gets short fade in/out
    /// so there are no click/pop artefacts. Cheap: a dozen clips of &lt;0.6 s each is
    /// a few hundred KB of float buffers built once at startup.
    /// </remarks>
    public static class ProceduralAudioBank
    {
        private const int SampleRate = 44100;

        /// <summary>Fills every null clip on the registry with a synthesised one.</summary>
        public static void FillMissing(AudioRegistrySO reg)
        {
            if (reg == null) return;

            // Combat
            if (reg.Hit  == null) reg.Hit  = Impact("SFX_Hit", 150f, 0.13f, noise: 0.6f);
            if (reg.Stun == null) reg.Stun = Wobble("SFX_Stun", 440f, 0.45f, 14f, 0.35f);
            if (reg.Swing == null) reg.Swing = Sweep("SFX_Swing", 900f, 300f, 0.12f, Wave.Noise, 0.30f);

            // Cargo
            if (reg.Collect   == null) reg.Collect   = Blip("SFX_Collect", 660f, 0.05f, Wave.Sine,   0.18f);
            if (reg.Pickup    == null) reg.Pickup    = Blip("SFX_Pickup", 880f, 0.08f, Wave.Square, 0.30f);
            if (reg.Deposit   == null) reg.Deposit   = Arp("SFX_Deposit", new[] { 523f, 659f, 784f, 1047f }, 0.085f, 0.38f);
            if (reg.CargoFull == null) reg.CargoFull = Arp("SFX_CargoFull", new[] { 784f, 988f }, 0.10f, 0.32f);

            // Abilities
            if (reg.AbilityActivate == null) reg.AbilityActivate = Sweep("SFX_Cast", 360f, 920f, 0.18f, Wave.Saw, 0.34f);
            if (reg.AbilityExpire   == null) reg.AbilityExpire   = Sweep("SFX_CastEnd", 620f, 300f, 0.12f, Wave.Sine, 0.20f);

            // Match
            if (reg.MatchStart   == null) reg.MatchStart   = Arp("SFX_MatchStart", new[] { 523f, 659f, 784f }, 0.12f, 0.40f);
            if (reg.MatchEnd     == null) reg.MatchEnd     = Arp("SFX_MatchEnd", new[] { 587f, 440f }, 0.16f, 0.36f);
            if (reg.MatchVictory == null) reg.MatchVictory = Arp("SFX_Victory", new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.11f, 0.42f);
        }

        // ---- Waveforms --------------------------------------------------------

        private enum Wave { Sine, Square, Saw, Noise }

        private static float Sample(Wave w, float phase, ref uint rng)
        {
            switch (w)
            {
                case Wave.Square: return Mathf.Sin(phase) >= 0f ? 1f : -1f;
                case Wave.Saw:    return Mathf.Repeat(phase / (2f * Mathf.PI), 1f) * 2f - 1f;
                case Wave.Noise:  return WhiteNoise(ref rng);
                default:          return Mathf.Sin(phase);
            }
        }

        // Fast deterministic xorshift noise so clips are stable per session.
        private static float WhiteNoise(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state / (float)uint.MaxValue) * 2f - 1f;
        }

        // ---- Generators -------------------------------------------------------

        /// <summary>Single fixed-pitch note.</summary>
        private static AudioClip Blip(string name, float freq, float dur, Wave wave, float vol)
            => Render(name, dur, (t, n) =>
            {
                uint rng = 0x9E3779B9u;
                float phase = 2f * Mathf.PI * freq * t;
                return Sample(wave, phase, ref rng) * vol * Env(t, dur, 0.004f, 0.6f);
            });

        /// <summary>Pitch glide from <paramref name="f0"/> to <paramref name="f1"/>.</summary>
        private static AudioClip Sweep(string name, float f0, float f1, float dur, Wave wave, float vol)
        {
            // Integrate instantaneous frequency so the glide has no phase discontinuity.
            int count = Mathf.Max(1, (int)(dur * SampleRate));
            var data = new float[count];
            float phase = 0f;
            uint rng = 0x1234567u;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float k = t / dur;
                float freq = Mathf.Lerp(f0, f1, k);
                phase += 2f * Mathf.PI * freq / SampleRate;
                data[i] = Sample(wave, phase, ref rng) * vol * Env(t, dur, 0.004f, 0.5f);
            }
            return FromData(name, data);
        }

        /// <summary>Percussive impact: low sine thump mixed with a noise transient.</summary>
        private static AudioClip Impact(string name, float baseFreq, float dur, float noise)
            => Render(name, dur, (t, n) =>
            {
                uint rng = 0xC0FFEEu;
                float thump = Mathf.Sin(2f * Mathf.PI * baseFreq * t) * (1f - noise);
                float crack = WhiteNoise(ref rng) * noise;
                // Noise decays much faster than the thump for a punchy "thwack".
                float nEnv = Mathf.Exp(-t * 55f);
                float bEnv = Mathf.Exp(-t * 14f);
                return (thump * bEnv + crack * nEnv) * 0.6f * Env(t, dur, 0.001f, 0.2f);
            });

        /// <summary>Vibrato tone that droops in pitch — a woozy "stunned" cue.</summary>
        private static AudioClip Wobble(string name, float freq, float dur, float vibHz, float vol)
            => Render(name, dur, (t, n) =>
            {
                float droop = Mathf.Lerp(1f, 0.6f, t / dur);
                float vib   = 1f + 0.12f * Mathf.Sin(2f * Mathf.PI * vibHz * t);
                float phase = 2f * Mathf.PI * freq * droop * vib * t;
                return Mathf.Sin(phase) * vol * Env(t, dur, 0.01f, 0.5f);
            });

        /// <summary>Sequence of notes played one after another (rising = rewarding).</summary>
        private static AudioClip Arp(string name, float[] freqs, float noteDur, float vol)
        {
            float total = noteDur * freqs.Length;
            int count = Mathf.Max(1, (int)(total * SampleRate));
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t  = i / (float)SampleRate;
                int   ni = Mathf.Min(freqs.Length - 1, (int)(t / noteDur));
                float lt = t - ni * noteDur;                 // time within the current note
                float phase = 2f * Mathf.PI * freqs[ni] * lt;
                // Soft square (sine + a touch of 3rd harmonic) reads as chiptune-bright.
                float s = Mathf.Sin(phase) + 0.25f * Mathf.Sin(3f * phase);
                data[i] = s * vol * 0.7f * Env(lt, noteDur, 0.004f, 0.55f);
            }
            return FromData(name, data);
        }

        // ---- Envelope + plumbing ---------------------------------------------

        /// <summary>
        /// Amplitude envelope: linear fade-in over <paramref name="attack"/> seconds,
        /// exponential decay over the back portion, plus a hard 3 ms fade-out at the
        /// very end so the buffer always returns to zero (no end-of-clip click).
        /// </summary>
        private static float Env(float t, float dur, float attack, float decayFrac)
        {
            float a = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
            float decayStart = dur * (1f - decayFrac);
            float d = t <= decayStart ? 1f : Mathf.Exp(-(t - decayStart) * 9f / Mathf.Max(0.01f, dur));
            float tail = Mathf.Clamp01((dur - t) / 0.003f); // last 3 ms ramp to zero
            return a * d * tail;
        }

        private static AudioClip Render(string name, float dur, System.Func<float, int, float> fn)
        {
            int count = Mathf.Max(1, (int)(dur * SampleRate));
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = Mathf.Clamp(fn(i / (float)SampleRate, i), -1f, 1f);
            return FromData(name, data);
        }

        private static AudioClip FromData(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
