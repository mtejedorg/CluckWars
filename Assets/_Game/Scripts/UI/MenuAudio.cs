using System.Collections.Generic;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// The menu's whole audio vocabulary: UI code says <c>Tap()</c>, <c>Equip()</c>,
    /// <c>MatchSting()</c> and never sees an AudioSource, a clip or a volume. Clips and default
    /// volumes come from <see cref="UiAudioSO"/> (the MANIFEST.md in <c>Audio/UI</c> says which
    /// sound belongs to which moment); playback goes through <see cref="IAudioService"/>, so the
    /// master volume applies and a <see cref="NullAudioService"/> silences everything.
    /// </summary>
    /// <remarks>
    /// A missing catalogue or clip is a wiring problem, not a game state: one Warning per cue
    /// through <see cref="ILogService"/>, then silence. Nothing here throws.
    /// </remarks>
    public sealed class MenuAudio
    {
        private const string Source = "MenuAudio";

        /// <summary>+/- fraction of random pitch on generic taps, so repeats don't sound robotic.</summary>
        private const float TapPitchJitter = 0.05f;
        /// <summary>The class cluck trails the select pop by this long (MANIFEST.md).</summary>
        private const float CluckDelaySeconds = 0.05f;
        /// <summary>Per-step pitch rise on the 3-2-1 ticks: 1.0 / 1.06 / 1.12 (MANIFEST.md).</summary>
        private const float CountdownPitchStep = 0.06f;
        private const float MusicFadeInSeconds = 0.3f;
        private const float MusicFadeOutSeconds = 0.4f;

        private readonly IAudioService _audio;
        private readonly UiAudioSO _catalog;
        private readonly ILogService _log;
        private readonly HashSet<string> _warned = new();

        public MenuAudio(IAudioService audio, UiAudioSO catalog, ILogService log)
        {
            _audio = audio ?? new NullAudioService();
            _catalog = catalog;
            _log = log;
        }

        /// <summary>A facade that plays nothing, for consumers resolved without the audio bindings.</summary>
        public static MenuAudio Silent() => new MenuAudio(null, null, null);

        // ---- Buttons ----------------------------------------------------------
        public void Tap() => Play("Tap", c => c.Tap, pitch: Random.Range(1f - TapPitchJitter, 1f + TapPitchJitter));
        public void Back() => Play("Back", c => c.Back);

        // ---- Class / perk -----------------------------------------------------
        /// <summary>Class tile became selected: the pop, then the class's own cluck a beat later.</summary>
        public void SelectClass(ChickenClass cls)
        {
            Play("Select", c => c.Select);
            Play("Cluck" + cls, c => c.CluckFor(cls), delay: CluckDelaySeconds);
        }

        /// <summary>A perk badge became selected.</summary>
        public void SelectPerk() => Play("Select", c => c.Select);

        // ---- Loadout ----------------------------------------------------------
        public void ArmSlot() => Play("SlotArm", c => c.SlotArm);
        public void Equip() => Play("EquipThunk", c => c.EquipThunk);
        public void Clear() => Play("ClearPop", c => c.ClearPop);
        public void Ready() => Play("ReadyStamp", c => c.ReadyStamp);

        // ---- Match start ------------------------------------------------------
        /// <summary>One 3-2-1 tick; <paramref name="n"/> is the number being shown (3, 2 or 1), pitched up as it counts down.</summary>
        public void CountdownTick(int n) =>
            Play("CountdownTick", c => c.CountdownTick, pitch: 1f + CountdownPitchStep * Mathf.Clamp(3 - n, 0, 2));
        public void CountdownGo() => Play("CountdownGo", c => c.CountdownGo);
        public void MatchSting() => Play("MatchSting", c => c.MatchSting);

        // ---- Music ------------------------------------------------------------
        /// <summary>Starts the menu loop unless it is already playing (page changes and a return from a match must not restart it).</summary>
        public void StartMenuMusic()
        {
            if (!TryGet("MenuLoop", c => c.MenuLoop, out var cue)) return;
            if (_audio.IsMusicPlaying(cue.Clip)) return;
            _audio.PlayMusic(cue.Clip, cue.Volume, loop: true, fadeInSeconds: MusicFadeInSeconds);
        }

        /// <summary>Fades the menu loop out so it does not bleed into the match; the sting (or the match's own music) takes over.</summary>
        public void StopMenuMusicForMatch() => _audio.FadeOutMusic(MusicFadeOutSeconds);

        // ---- Plumbing ---------------------------------------------------------
        private void Play(string name, System.Func<UiAudioSO, UiCue> pick, float pitch = 1f, float delay = 0f)
        {
            if (TryGet(name, pick, out var cue)) _audio.PlaySFX(cue.Clip, cue.Volume, pitch, delay);
        }

        private bool TryGet(string name, System.Func<UiAudioSO, UiCue> pick, out UiCue cue)
        {
            cue = _catalog != null ? pick(_catalog) : default;
            if (cue.Clip != null) return true;

            // Silent() has neither a catalogue nor a logger: that is a deliberate "no audio", not a fault.
            if (_log != null && _warned.Add(_catalog == null ? string.Empty : name))
                _log.Warn(Source, _catalog == null
                    ? "No UiAudioSO is bound; menu audio is silent."
                    : $"UiAudioSO has no clip for '{name}'; that menu cue is silent.");
            return false;
        }
    }
}
