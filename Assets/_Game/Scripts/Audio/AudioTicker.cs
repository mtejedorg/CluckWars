using System;
using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>Per-frame hook for <see cref="UnityAudioService"/> music fades: the service itself is a plain class.</summary>
    internal sealed class AudioTicker : MonoBehaviour
    {
        public Action OnTick;
        private void Update() => OnTick?.Invoke();
    }
}
