using UnityEngine;

namespace CluckWars.Audio
{
    /// <summary>
    /// Centralized audio clip registry. Game systems request cues by reference
    /// (e.g., <c>_audioReg.Swing</c>) instead of holding scattered SerializeField
    /// AudioClip slots. Bound app-wide in <c>ProjectInstaller</c>.
    /// </summary>
    /// <remarks>
    /// Any field can be null and audio calls handle that gracefully (silent).
    /// Phase 9 ships the contract + plumbing; Maestro fills the clips in the
    /// Inspector. Sibling-SO to <c>PrefabRegistrySO</c> and <c>ColorSchemeSO</c>
    /// — the asset-consolidation pattern from TDD §6.6.
    /// </remarks>
    [CreateAssetMenu(fileName = "AudioRegistry", menuName = "Cluck Wars/Audio Registry", order = 5)]
    public sealed class AudioRegistrySO : ScriptableObject
    {
        [Header("Combat")]
        public AudioClip Swing;
        public AudioClip Hit;
        public AudioClip Stun;

        [Header("Cargo")]
        public AudioClip Collect;       // optional — fires per discrete unit, can get noisy
        public AudioClip Deposit;
        public AudioClip Pickup;        // ground-dropped food picked up
        public AudioClip CargoFull;

        [Header("Abilities")]
        public AudioClip AbilityActivate;
        public AudioClip AbilityExpire;

        [Header("Match")]
        public AudioClip MatchStart;
        public AudioClip MatchEnd;
        public AudioClip MatchVictory;

        [Header("Music")]
        public AudioClip MenuMusic;
        public AudioClip MatchMusic;
    }
}
