using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>
    /// Default <see cref="IAudioService"/> implementation backed by Unity's
    /// <see cref="AudioSource"/>. One source for music (looping) plus a second one that only plays
    /// while <see cref="CrossfadeMusic"/> blends into a new clip, one for SFX
    /// (PlayOneShot), and a small round-robin pool for pitch-shifted SFX. All live on a child
    /// of the provided parent transform so they inherit lifetime - typically ProjectContext,
    /// so audio survives scene loads.
    /// </summary>
    /// <remarks>
    /// SFX calls accept a null <see cref="AudioClip"/> as a silent no-op so
    /// consumers don't have to null-check before every cue. Master volume is
    /// applied multiplicatively on top of each call's per-sound volume, including
    /// during music fades (a fade is a 0..1 multiplier, not a volume write).
    /// Fades advance in a tiny MonoBehaviour on the host, which stays disabled while idle.
    /// </remarks>
    public sealed class UnityAudioService : IAudioService
    {
        /// <summary>Pitch-shifted voices: a rapid run of taps or a 3-2-1 never needs more.</summary>
        private const int PitchedVoiceCount = 6;

        // Swapped with _incomingSource when a crossfade completes, so not readonly.
        private AudioSource _musicSource;
        private AudioSource _incomingSource;
        private readonly AudioSource _sfxSource;
        private readonly AudioSource[] _pitchedVoices = new AudioSource[PitchedVoiceCount];
        private readonly AudioTicker _ticker;
        private int _nextPitchedVoice;

        private float _masterVolume = 1f;
        // The Settings sliders (Part B). Read from the saved preferences at construction so the first cue already obeys them.
        private float _musicVolume = Settings.PlayerPreferences.MusicVolume;
        private float _sfxVolume   = Settings.PlayerPreferences.SfxVolume;
        private float _musicBaseVolume = 1f;

        // Music fade: _fade is a 0..1 multiplier moving toward _fadeTarget at _fadeRate per second.
        private float _fade = 1f, _fadeTarget = 1f, _fadeRate;
        private bool _stopWhenFadedOut;

        // Crossfade: _xfade moves 0 -> 1 at _xfadeRate; the outgoing source plays at (1 - _xfade).
        private float _xfade, _xfadeRate;
        private bool _crossfading;

        public UnityAudioService(Transform parent)
        {
            var host = new GameObject("AudioServiceHost");
            if (parent != null) host.transform.SetParent(parent, worldPositionStays: false);

            _musicSource = AddSource(host);
            _musicSource.loop = true;
            _incomingSource = AddSource(host);

            _sfxSource = AddSource(host);

            for (int i = 0; i < _pitchedVoices.Length; i++) _pitchedVoices[i] = AddSource(host);

            _ticker = host.AddComponent<AudioTicker>();
            _ticker.OnTick = TickFade;
            _ticker.enabled = false;
        }

        // 2D (spatialBlend 0): global music / UI / generic SFX.
        private static AudioSource AddSource(GameObject host)
        {
            var s = host.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;
            return s;
        }

        public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f, float delaySeconds = 0f)
        {
            if (clip == null || _sfxSource == null) return;
            float v = Mathf.Clamp01(volume) * _masterVolume * _sfxVolume;
            if (Mathf.Approximately(pitch, 1f) && delaySeconds <= 0f)
            {
                _sfxSource.PlayOneShot(clip, v);
                return;
            }

            // PlayOneShot shares the source's pitch with every sound already on it, and has no
            // delayed form, so a pitched or delayed cue gets its own voice.
            var voice = _pitchedVoices[_nextPitchedVoice];
            _nextPitchedVoice = (_nextPitchedVoice + 1) % _pitchedVoices.Length;
            if (voice == null) return;
            voice.clip = clip;
            voice.volume = v;
            voice.pitch = pitch;
            if (delaySeconds > 0f) voice.PlayDelayed(delaySeconds); else voice.Play();
        }

        public void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true, float fadeInSeconds = 0f)
        {
            if (_musicSource == null) return;
            if (clip == null)
            {
                StopMusic();
                _musicSource.clip = null;
                return;
            }
            CancelCrossfade();
            _musicBaseVolume = Mathf.Clamp01(volume);
            _musicSource.clip = clip;
            _musicSource.loop = loop;
            _stopWhenFadedOut = false;
            _fadeTarget = 1f;
            if (fadeInSeconds > 0f)
            {
                _fade = 0f;
                _fadeRate = 1f / fadeInSeconds;
                _ticker.enabled = true;
            }
            else
            {
                _fade = 1f;
                _ticker.enabled = false;
            }
            ApplyMusicVolume();
            _musicSource.Play();
        }

        public void FadeOutMusic(float seconds)
        {
            if (_musicSource == null || !_musicSource.isPlaying) return;
            if (seconds <= 0f) { StopMusic(); return; }
            _fadeTarget = 0f;
            _fadeRate = 1f / seconds;
            _stopWhenFadedOut = true;
            _ticker.enabled = true;
        }

        public void SetMusicLevel(float level01, float seconds)
        {
            if (_musicSource == null || !_musicSource.isPlaying) return;
            _stopWhenFadedOut = false;
            _fadeTarget = Mathf.Clamp01(level01);
            if (seconds <= 0f)
            {
                _fade = _fadeTarget;
                ApplyMusicVolume();
                return;
            }
            _fadeRate = Mathf.Abs(_fadeTarget - _fade) / seconds;
            _ticker.enabled = true;
        }

        public void CrossfadeMusic(AudioClip clip, float seconds)
        {
            if (clip == null || _musicSource == null || _incomingSource == null) return;
            if (!_musicSource.isPlaying || _musicSource.clip == null || seconds <= 0f)
            {
                PlayMusic(clip, _musicBaseVolume, _musicSource.loop);
                return;
            }
            if (_musicSource.clip == clip) return;

            FinishCrossfade();
            _incomingSource.clip = clip;
            _incomingSource.loop = _musicSource.loop;
            _incomingSource.timeSamples = Mathf.Min(_musicSource.timeSamples, clip.samples - 1);
            _xfade = 0f;
            _xfadeRate = 1f / seconds;
            _crossfading = true;
            ApplyMusicVolume();
            _incomingSource.Play();
            _ticker.enabled = true;
        }

        public void StopMusic()
        {
            CancelCrossfade();
            if (_musicSource != null) _musicSource.Stop();
            _stopWhenFadedOut = false;
            _fade = _fadeTarget = 1f;
            if (_ticker != null) _ticker.enabled = false;
        }

        public bool IsMusicPlaying(AudioClip clip) =>
            clip != null && _musicSource != null && _musicSource.isPlaying && !_stopWhenFadedOut
            && (_musicSource.clip == clip || (_crossfading && _incomingSource.clip == clip));

        public void SetMasterVolume(float volume01)
        {
            _masterVolume = Mathf.Clamp01(volume01);
            if (_musicSource != null && _musicSource.isPlaying) ApplyMusicVolume();
        }

        public void SetMusicVolume(float volume01)
        {
            _musicVolume = Mathf.Clamp01(volume01);
            if (_musicSource != null && _musicSource.isPlaying) ApplyMusicVolume();
        }

        public void SetSfxVolume(float volume01) => _sfxVolume = Mathf.Clamp01(volume01);

        private void ApplyMusicVolume()
        {
            float v = _musicBaseVolume * _masterVolume * _musicVolume * _fade;
            _musicSource.volume = _crossfading ? v * (1f - _xfade) : v;
            if (_crossfading) _incomingSource.volume = v * _xfade;
        }

        private void TickFade()
        {
            if (_musicSource == null) { _ticker.enabled = false; return; }
            float dt = Time.unscaledDeltaTime;
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, _fadeRate * dt);
            if (_crossfading) _xfade = Mathf.MoveTowards(_xfade, 1f, _xfadeRate * dt);
            ApplyMusicVolume();
            if (_crossfading && _xfade >= 1f) FinishCrossfade();

            if (!Mathf.Approximately(_fade, _fadeTarget) || _crossfading) return;
            _ticker.enabled = false;
            if (_stopWhenFadedOut) StopMusic();
        }

        /// <summary>Completes a running crossfade at once: the incoming source becomes the music source.</summary>
        private void FinishCrossfade()
        {
            if (!_crossfading) return;
            _musicSource.Stop();
            (_musicSource, _incomingSource) = (_incomingSource, _musicSource);
            _crossfading = false;
            ApplyMusicVolume();
        }

        private void CancelCrossfade()
        {
            if (!_crossfading) return;
            _crossfading = false;
            if (_incomingSource != null) _incomingSource.Stop();
        }
    }
}
