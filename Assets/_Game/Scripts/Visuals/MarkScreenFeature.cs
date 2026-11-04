using CluckWars.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Draws the Death Mark victim's screen mood (<see cref="MarkScreenEffect"/>) with <c>CWMarkMood.shader</c>.
    /// Lives on BOTH <c>PC_Renderer.asset</c> and <c>Mobile_Renderer.asset</c>; needs no post-processing (Mobile has it off).
    /// </summary>
    /// <remarks>
    /// <para><b>Cost.</b> All weights ~0: <see cref="AddRenderPasses"/> returns before enqueuing anything — zero GPU work.
    /// DIM / RED only (<see cref="MarkScreenPass.Overlay"/>): one full-screen triangle, premultiplied blend into the
    /// active colour target, no colour read, no copy, no intermediate-texture request (a raster pass that can merge into
    /// the native render pass on tile GPUs). COLD (<see cref="MarkScreenPass.Graded"/>): true desaturation needs the
    /// scene colour, so the pass copies it to a non-MSAA texture and material-blits back (two full-screen passes, the same
    /// shape as URP's FullScreenPassRendererFeature with "fetch colour"). Mobile already renders to an intermediate
    /// texture (render scale 0.8), so the intermediate request adds nothing there.</para>
    /// <para><b>Event: <see cref="RenderPassEvent.BeforeRenderingPostProcessing"/></b> on Base game cameras only. The whole
    /// scene (opaques, transparents, particles, world-space labels) is moody; the UI overlay camera, Screen Space Overlay
    /// canvases and UI Toolkit panels all draw after it, so the HUD is never dimmed. PC post (bloom, D7 grading) applies on
    /// top, as it does to everything else.</para>
    /// <para><b>Build inclusion.</b> The shader is serialized on this feature inside both renderer assets, which the RP
    /// assets reference, so player builds keep it without an "Always Included" entry (Shader.Find is only an Editor
    /// fallback).</para>
    /// </remarks>
    public sealed class MarkScreenFeature : ScriptableRendererFeature
    {
        /// <summary>Shader name of Assets/_Game/Art/Shaders/CWMarkMood.shader.</summary>
        public const string ShaderName = "Cluck Wars/Mark Mood";

        /// <summary>Shader pass indices.</summary>
        public const int OverlayPassIndex = 0;
        public const int GradedPassIndex = 1;

        [Tooltip("Assets/_Game/Art/Shaders/CWMarkMood.shader. Serialized here so build stripping keeps it.")]
        [SerializeField] private Shader _shader;

        private Material _material;
        private MoodPass _pass;

        private static readonly int DarkId = Shader.PropertyToID("_CW_MarkDark");
        private static readonly int ShapeId = Shader.PropertyToID("_CW_MarkShape");
        private static readonly int RedId = Shader.PropertyToID("_CW_MarkRed");
        private static readonly int GradeId = Shader.PropertyToID("_CW_MarkGrade");

        /// <summary>The mood shader this feature draws with (read by the EditMode tests).</summary>
        public Shader MoodShader => _shader;

        public override void Create()
        {
            if (_shader == null) _shader = Shader.Find(ShaderName);
            if (_material != null) CoreUtils.Destroy(_material);
            _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
            _pass = new MoodPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var mode = MarkScreenEffect.ActivePass;
            if (mode == MarkScreenPass.None || !MarkScreenEffect.RenderingAllowed) return;
            if (!ShouldDraw(renderingData.cameraData.camera, renderingData.cameraData.renderType)) return;
            if (_material == null)
            {
                Log.Error("MarkScreenFeature", "mood shader '" + ShaderName + "' is missing: the Death Mark screen mood is not drawn");
                return;
            }

            MarkScreenEffect.Uniforms(MarkScreenEffect.Current, out var dark, out var shape, out var red, out var grade);
            _material.SetVector(DarkId, dark);
            _material.SetVector(ShapeId, shape);
            _material.SetVector(RedId, red);
            _material.SetVector(GradeId, grade);

            _pass.Material = _material;
            _pass.Mode = mode;
            // Only the colour-reading pass needs an intermediate colour texture (the backbuffer cannot be sampled).
            _pass.requiresIntermediateTexture = mode == MarkScreenPass.Graded;
            renderer.EnqueuePass(_pass);
        }

        /// <summary>Which cameras get the mood: base game cameras only (never overlay / UI, Scene view, previews).</summary>
        public static bool ShouldDraw(Camera camera, CameraRenderType renderType) =>
            camera != null
            && renderType == CameraRenderType.Base
            && camera.cameraType == CameraType.Game;

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private sealed class MoodPass : ScriptableRenderPass
        {
            public Material Material;
            public MarkScreenPass Mode;

            private sealed class OverlayData
            {
                public Material Material;
            }

            public MoodPass()
            {
                profilingSampler = new ProfilingSampler("CW Mark Mood");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var target = resources.activeColorTexture;
                if (!target.IsValid()) return;

                if (Mode == MarkScreenPass.Graded && !resources.isActiveTargetBackBuffer)
                {
                    // Graph-derived descriptor; only diverge where needed: a sampled-only copy needs no MSAA and no clear.
                    var desc = renderGraph.GetTextureDesc(target);
                    desc.name = "_CW_MarkMoodSceneCopy";
                    desc.clearBuffer = false;
                    desc.msaaSamples = MSAASamples.None;
                    var copy = renderGraph.CreateTexture(desc);
                    renderGraph.AddBlitPass(target, copy, Vector2.one, Vector2.zero, passName: "CW Mark Mood Copy");
                    renderGraph.AddBlitPass(
                        new RenderGraphUtils.BlitMaterialParameters(copy, target, Material, GradedPassIndex),
                        passName: "CW Mark Mood Graded");
                    return;
                }

                // Overlay (also the safe fallback if the target were ever the backbuffer: cold then shows as violet
                // darkness without the desaturation). Blended, so the attachment is ReadWrite — AddBlitPass would bind
                // it Write-only and let tile GPUs drop the scene under the blend.
                using (var builder = renderGraph.AddRasterRenderPass<OverlayData>("CW Mark Mood Overlay", out var data, profilingSampler))
                {
                    data.Material = Material;
                    builder.SetRenderAttachment(target, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (OverlayData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.Material, OverlayPassIndex));
                }
            }
        }

        /// <summary>
        /// Error sink, as in <see cref="CWHazeFeature"/>: a renderer feature is an asset and cannot self-inject, so it
        /// borrows the project logger once the ProjectContext exists and reports each problem once.
        /// </summary>
        private static class Log
        {
            private static bool _reported;

            public static void Error(string source, string message)
            {
                if (_reported) return;
                if (!ProjectContext.HasInstance) return;
                var log = ProjectContext.Instance.Container.TryResolve<ILogService>();
                if (log == null) return;
                _reported = true;
                log.Error(source, message);
            }
        }
    }
}
