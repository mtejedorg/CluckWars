namespace CluckWars.Gameplay
{
    /// <summary>
    /// Pure local-presentation rules for the match's music and the countdown scrim (Phase 6 chunk 7b,
    /// round-3 findings 7 and 17). Nothing here is networked: each peer drives it from state it already sees.
    /// </summary>
    public static class MatchPresentationRules
    {
        /// <summary>The match loop fades out over this long at the end of a round (it used to cut dead).</summary>
        public const float EndMusicFadeOutSeconds = 0.6f;

        /// <summary>The match loop fades in over this long at GO, so the GO sound leads.</summary>
        public const float GoMusicFadeInSeconds = 0.25f;

        /// <summary>The menu loop starts as a bed under the podium once the match loop has finished fading out.</summary>
        public static bool StartsPodiumBed(bool podiumShown, bool bedStarted, float secondsSincePodium) =>
            podiumShown && !bedStarted && secondsSincePodium >= EndMusicFadeOutSeconds;

        /// <summary>The 45% countdown scrim dims GET READY and the 3-2-1 only: gone on the GO cue,
        /// while the player can already move.</summary>
        public static bool IntroScrimVisible(bool getReady, bool introActive) => getReady || introActive;
    }
}
