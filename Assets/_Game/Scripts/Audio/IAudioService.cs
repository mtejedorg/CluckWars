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

        /// <summary>Ramps the playing music's level (a 0..1 multiplier on its volume) to
        /// <paramref name="level01"/> over <paramref name="seconds"/> and keeps it playing: the menu
        /// loop ducks to a low bed under GET READY. No-op when nothing is playing.</summary>
        void SetMusicLevel(float level01, float seconds);

        /// <summary>Crossfades the playing music into <paramref name="clip"/>, which starts at the same
        /// playback position (for loops cut to the same length and tempo, e.g. match_loop ->
        /// match_loop_intense). Plain <see cref="PlayMusic"/> when nothing is playing.</summary>
        void CrossfadeMusic(AudioClip clip, float seconds);

        void StopMusic();

        /// <summary>True while <paramref name="clip"/> is the music track and is not on its way out.</summary>
        bool IsMusicPlaying(AudioClip clip);

        void SetMasterVolume(float volume01);
    }
}
