using System;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>One menu sound: the clip and the volume it plays at (before master volume).</summary>
    [Serializable]
    public struct UiCue
    {
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume;

        public UiCue(float volume) { Clip = null; Volume = volume; }
    }

    /// <summary>
    /// Catalogue of the menu sound set in <c>Assets/_Game/Audio/UI/</c> (see its MANIFEST.md, which
    /// is the authority for what plays when). Default volumes are the manifest's suggestions; the
    /// shipped asset is <c>Assets/_Game/Data/UiAudio.asset</c>. Bound app-wide in
    /// <c>ProjectInstaller</c> and played through <c>MenuAudio</c>.
    /// </summary>
    /// <remarks>
    /// Sibling of <see cref="AudioRegistrySO"/> (gameplay cues), kept separate so the menu set can
    /// be re-voiced without touching the match bank. Any clip may be null: <c>MenuAudio</c> warns
    /// once and stays silent.
    /// </remarks>
    [CreateAssetMenu(fileName = "UiAudio", menuName = "Cluck Wars/UI Audio", order = 6)]
    public sealed class UiAudioSO : ScriptableObject
    {
        [Header("Buttons")]
        public UiCue Tap = new UiCue(0.7f);
        public UiCue Back = new UiCue(0.7f);
        public UiCue Select = new UiCue(0.8f);

        [Header("Class cluck (plays with Select)")]
        public UiCue CluckWarrior = new UiCue(0.9f);
        public UiCue CluckSpeedy = new UiCue(0.9f);
        public UiCue CluckFatty = new UiCue(0.9f);
        public UiCue CluckAssassin = new UiCue(0.9f);

        [Header("Loadout")]
        public UiCue SlotArm = new UiCue(0.6f);
        public UiCue EquipThunk = new UiCue(0.9f);
        public UiCue ClearPop = new UiCue(0.7f);
        public UiCue ReadyStamp = new UiCue(1f);

        [Header("Match start")]
        public UiCue CountdownTick = new UiCue(0.8f);
        public UiCue CountdownGo = new UiCue(1f);
        public UiCue MatchSting = new UiCue(1f);

        [Header("Music")]
        public UiCue MenuLoop = new UiCue(0.6f);

        /// <summary>The cluck for a picked class. Classes are a closed set, so an unknown value is a bug upstream.</summary>
        public UiCue CluckFor(ChickenClass cls) => cls switch
        {
            ChickenClass.Warrior => CluckWarrior,
            ChickenClass.Speedy => CluckSpeedy,
            ChickenClass.Fatty => CluckFatty,
            ChickenClass.Assassin => CluckAssassin,
            _ => default,
        };
    }
}
