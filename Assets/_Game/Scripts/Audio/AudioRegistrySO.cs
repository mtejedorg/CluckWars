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
        [Tooltip("Soft airy 'pfft': a target-gated move released with nobody in range (local only).")]
        public AudioClip AbilityFizzle;
        [Tooltip("Soft low tick: a held move cancelled (edge band, Esc, right mouse) (local only).")]
        public AudioClip AbilityCancel;

        [Header("Ability feedback")]
        [Tooltip("Short rising tick (~90 ms) when the local player's own cast lands on at least one rival (HitFeedback; local only).")]
        public AudioClip HitConfirm;
        [Tooltip("Muted swish (~120 ms) when the local player's own cast hits nobody; played at WhiffSfxVolumeMultiplier (HitFeedback; local only).")]
        public AudioClip CastWhiff;
        [Tooltip("Muted click (~40 ms) when a press is refused, with the hex bump (TouchControlsController; local only).")]
        public AudioClip DeniedClick;
        [Tooltip("Wooden clack (~120 ms) on the Egg Shell close edge, on every peer that sees the egg; silent while faded (ChickenShellVisuals; local only).")]
        public AudioClip ShellClose;
        [Tooltip("Two-note crack-pop (~200 ms) on the Egg Shell hatch edge, on every peer that sees the egg; silent while faded (ChickenShellVisuals; local only).")]
        public AudioClip ShellOpen;
        [Tooltip("Short sting (~250 ms) on the hunter's own peer when the Death Mark kill goes live (the HUNT! pop); never on the victim (MarkFeedback; local only).")]
        public AudioClip HuntLive;

        [Header("Match")]
        public AudioClip MatchStart;
        public AudioClip MatchEnd;
        public AudioClip MatchVictory;

        [Header("Death Mark")]
        [Tooltip("Assassin: short tick per step of the ~0.6 s standings-row highlight; caller raises pitch per step (local 2D).")]
        public AudioClip MarkDrawTick;
        [Tooltip("Assassin: lock-on sting when the highlight lands on the target (local 2D).")]
        public AudioClip MarkLock;
        [Tooltip("Victim: ominous 'MARKED!' sting when they become the target (local 2D).")]
        public AudioClip MarkVictim;
        [Tooltip("Assassin: short sharp sting when the Kill becomes possible; may re-fire as the state toggles (local 2D).")]
        public AudioClip MarkKillReady;

        [Header("Music")]
        public AudioClip MenuMusic;
        public AudioClip MatchMusic;
        [Tooltip("Same length, tempo and phase as MatchMusic: crossfaded in at the current play position " +
                 "when the comeback event fires (GameManager, MatchAudioCueTracker.Cue.Intensify).")]
        public AudioClip MatchMusicIntense;

        /// <summary>The clip for a typed sound id, or null when the slot is empty (or the id is <see cref="SfxId.None"/>).</summary>
        public AudioClip Resolve(SfxId id) => id switch
        {
            SfxId.AbilityActivate => AbilityActivate,
            SfxId.Deposit => Deposit,
            SfxId.AbilityCancel => AbilityCancel,
            SfxId.AbilityFizzle => AbilityFizzle,
            SfxId.CargoFull => CargoFull,
            SfxId.AbilityExpire => AbilityExpire,
            SfxId.HitConfirm => HitConfirm,
            SfxId.CastWhiff => CastWhiff,
            SfxId.DeniedClick => DeniedClick,
            SfxId.ShellClose => ShellClose,
            SfxId.ShellOpen => ShellOpen,
            SfxId.HuntLive => HuntLive,
            _ => null,
        };

        /// <summary>
        /// Null-safe one-shot: plays <paramref name="clip"/> through <paramref name="audio"/> and returns true, or returns
        /// false without throwing when either is null (an unfilled registry slot is a legal silent no-op). Callers that must
        /// report a missing service do so themselves, once.
        /// </summary>
        public static bool TryPlay(IAudioService audio, AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (audio == null || clip == null) return false;
            audio.PlaySFX(clip, volume, pitch);
            return true;
        }
    }
}
