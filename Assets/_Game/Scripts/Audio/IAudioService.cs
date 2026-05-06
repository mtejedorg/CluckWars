using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>
    /// Audio playback abstraction. Game logic never touches AudioSource directly.
    /// Bound to <see cref="UnityAudioService"/> in production and
    /// <see cref="NullAudioService"/> in headless / test contexts.
    /// </summary>
    public interface IAudioService
    {
        void PlaySFX(AudioClip clip, float volume = 1f);
        void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true);
        void StopMusic();
        void SetMasterVolume(float volume01);
    }
}
