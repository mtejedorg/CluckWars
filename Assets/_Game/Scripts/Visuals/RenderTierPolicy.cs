using UnityEngine.Rendering;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Which render-quality tier gets which camera features. Keyed on the ACTIVE render pipeline asset (not the
    /// platform or the quality-level index) because that is what actually decides the frame: the capture and
    /// frame-cost tools swap <c>QualitySettings.renderPipeline</c> to preview the Mobile tier in the Editor.
    /// </summary>
    /// <remarks>
    /// Post-processing (Neutral tonemapping, mild bloom + vignette from <c>CW_MatchProfile</c>, the RP assets'
    /// default volume profile) is a PC-tier-only cost: the Mobile tier keeps the match camera's
    /// <c>renderPostProcessing</c> off, which is also the serialized default in Game.unity, so an unknown or
    /// missing pipeline asset falls back to the cheap path.
    ///
    /// <see cref="PcPostProcessingEnabled"/> is OFF pending Maestro's call (Phase 8 A7): with the profile as
    /// authored, the Neutral tonemapper alone darkens the grass ~12% (luminance 195 -> 170) and the food ~16%
    /// and drops saturation ~13%, a bigger look change than the brief allows without sign-off. Evidence:
    /// Captures/env3d_after/A7/post_preview/. Flipping the flag is the whole change.
    /// </remarks>
    public static class RenderTierPolicy
    {
        /// <summary>Asset name of the PC tier's URP asset (Assets/Settings/PC_RPAsset.asset).</summary>
        public const string PcRpAssetName = "PC_RPAsset";

        /// <summary>Whether the PC tier renders post-processing at all. Off until the look change is signed off.</summary>
        public static readonly bool PcPostProcessingEnabled = false;

        /// <summary>True when the match camera should render post-processing under a pipeline asset with this name.</summary>
        public static bool PostProcessingFor(string rpAssetName) =>
            PcPostProcessingEnabled && rpAssetName == PcRpAssetName;

        /// <inheritdoc cref="PostProcessingFor(string)"/>
        public static bool PostProcessingFor(RenderPipelineAsset rpAsset) =>
            rpAsset != null && PostProcessingFor(rpAsset.name);
    }
}
