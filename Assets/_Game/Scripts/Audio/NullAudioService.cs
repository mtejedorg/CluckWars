using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>No-op audio service. Used during Phase 1 / tests / headless runs.</summary>
    public sealed class NullAudioService : IAudioService
    {
        public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f, float delaySeconds = 0f) { }
        public void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true, float fadeInSeconds = 0f) { }
        public void FadeOutMusic(float seconds) { }
        public void StopMusic() { }
        public bool IsMusicPlaying(AudioClip clip) => false;
        public void SetMasterVolume(float volume01) { }
    }
}
