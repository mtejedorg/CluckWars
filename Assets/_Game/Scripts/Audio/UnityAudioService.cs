using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>
    /// Default <see cref="IAudioService"/> implementation backed by Unity's
    /// <see cref="AudioSource"/>. One source for music (looping), one for SFX
    /// (PlayOneShot). Both live on a child of the provided parent transform so
    /// they inherit lifetime — typically ProjectContext, so audio survives
    /// scene loads.
    /// </summary>
    /// <remarks>
    /// SFX calls accept a null <see cref="AudioClip"/> as a silent no-op so
    /// consumers don't have to null-check before every cue. Master volume is
    /// applied multiplicatively on top of each call's per-sound volume.
    /// </remarks>
    public sealed class UnityAudioService : IAudioService
    {
        private readonly AudioSource _musicSource;
        private readonly AudioSource _sfxSource;

        private float _masterVolume = 1f;
        private float _musicBaseVolume = 1f;

        public UnityAudioService(Transform parent)
        {
            var host = new GameObject("AudioServiceHost");
            if (parent != null) host.transform.SetParent(parent, worldPositionStays: false);

            _musicSource = host.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f; // 2D — global music

            _sfxSource = host.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f; // 2D — UI / generic SFX
        }

        public void PlaySFX(AudioClip clip, float volume = 1f)
        {
            if (clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume) * _masterVolume);
        }

        public void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true)
        {
            if (_musicSource == null) return;
            if (clip == null)
            {
                _musicSource.Stop();
                _musicSource.clip = null;
                return;
            }
            _musicBaseVolume = Mathf.Clamp01(volume);
            _musicSource.clip = clip;
            _musicSource.loop = loop;
            _musicSource.volume = _musicBaseVolume * _masterVolume;
            _musicSource.Play();
        }

        public void StopMusic()
        {
            if (_musicSource != null) _musicSource.Stop();
        }

        public void SetMasterVolume(float volume01)
        {
            _masterVolume = Mathf.Clamp01(volume01);
            if (_musicSource != null && _musicSource.isPlaying)
            {
                _musicSource.volume = _musicBaseVolume * _masterVolume;
            }
        }
    }
}
