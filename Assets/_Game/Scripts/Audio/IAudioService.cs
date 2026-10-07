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
        /// <summary>One-shot 2D sound. A <paramref name="pitch"/> other than 1 or a positive
        /// <paramref name="delaySeconds"/> plays on a pooled voice, so it never bends (or is cut by)
        /// the other sounds already playing.</summary>
        void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f, float delaySeconds = 0f);

        /// <summary>Starts the (single) music track, replacing whatever played. A positive
        /// <paramref name="fadeInSeconds"/> ramps it up from silence.</summary>
        void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true, float fadeInSeconds = 0f);

        /// <summary>Fades the music to silence over <paramref name="seconds"/>, then stops it.
        /// No-op when nothing is playing; a later <see cref="PlayMusic"/> cancels the fade.</summary>
        void FadeOutMusic(float seconds);

        void StopMusic();

        /// <summary>True while <paramref name="clip"/> is the music track and is not on its way out.</summary>
        bool IsMusicPlaying(AudioClip clip);

        void SetMasterVolume(float volume01);
    }
}
