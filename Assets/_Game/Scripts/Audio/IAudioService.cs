using System.Runtime.CompilerServices;
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
        /// <remarks>A null <paramref name="clip"/> plays nothing and is reported once per call site (the two caller
        /// parameters are filled in by the compiler; do not pass them).</remarks>
        void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f, float delaySeconds = 0f,
                     [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0);

        /// <summary>One-shot 2D sound by typed id, resolved through the audio registry. <see cref="SfxId.None"/> is a
        /// silent no-op; an id with no clip plays nothing and is reported once per id. A <paramref name="pitch"/>
        /// other than 1 plays on a pooled voice, as the clip overload does.</summary>
        void PlaySFX(SfxId id, float volume = 1f, float pitch = 1f);

        /// <summary>
        /// Plays <paramref name="id"/> and returns whether it had a clip, WITHOUT reporting an empty slot: for the
        /// few sounds whose slot is optional by design and whose caller has its own fallback (the Assassin's HUNT!
        /// sting). Every other caller uses <see cref="PlaySFX(SfxId, float, float)"/>, so an empty slot is heard about.
        /// </summary>
        bool TryPlaySFX(SfxId id, float volume = 1f, float pitch = 1f);

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

        /// <summary>Music level 0..1 (the Settings slider), multiplied with the master volume and any fade.</summary>
        void SetMusicVolume(float volume01);

        /// <summary>Sound-effects level 0..1 (the Settings slider), multiplied with the master volume and each cue's own volume.</summary>
        void SetSfxVolume(float volume01);
    }
}
