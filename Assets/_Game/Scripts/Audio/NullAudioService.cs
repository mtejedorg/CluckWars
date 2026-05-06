using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>No-op audio service. Used during Phase 1 / tests / headless runs.</summary>
    public sealed class NullAudioService : IAudioService
    {
        public void PlaySFX(AudioClip clip, float volume = 1f) { }
        public void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true) { }
        public void StopMusic() { }
        public void SetMasterVolume(float volume01) { }
    }
}
