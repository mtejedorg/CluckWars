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
    /// Draws the Death Mark's screen mood (the victim's, or the hunter's edge tint; <see cref="MarkScreenEffect"/>) with <c>CWMarkMood.shader</c>.
    /// The two birds of the hunt are exempt from it (K13, see the remarks). Lives on BOTH <c>PC_Renderer.asset</c> and <c>Mobile_Renderer.asset</c>; needs no post-processing (Mobile has it off).
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
    /// <para><b>Exempt birds (K13).</b> While <see cref="MarkScreenEffect.ExemptCount"/> is above 0, the hunt's birds carry
    /// <see cref="FeedbackTuning.MarkExemptRenderingLayer"/> on their body renderers (<c>ChickenVisuals.SetTintExempt</c>). A stencil
    /// mask pass (pass <see cref="MaskPassIndex"/>, drawn through <c>overrideMaterial</c> over a renderer list filtered by that layer
    /// bit, colour-masked, depth-tested, no depth write) sets stencil bit <see cref="FeedbackTuning.MarkExemptStencilValue"/> where a
    /// bird is the front-most surface; both mood passes then skip those pixels, so the bird keeps its true colour while particles,
    /// rings and transparent effects in front of it (drawn earlier, they sit on top of the bird's own pixels) are tinted like the rest
    /// of the scene. The mood passes bind the camera depth-stencil read-only in that case. With nobody exempt the feature does exactly
    /// what it did before: no mask, no depth-stencil binding, the old blit for the Graded path.</para>
    /// <para><b>Cost while a bird is exempt</b> (per frame, on top of the mood): the mask draws the two birds' meshes once more
    /// (position-only vertex shader, no fragment output), plus the depth-stencil attachment bound on the mood passes. PC (Forward+,
    /// MSAA 4): one extra raster pass; the depth-stencil is already resident, nothing is allocated or copied. Mobile (Forward, MSAA 2,
    /// render scale 0.8): the same extra pass; on a tile GPU the mask and the Overlay pass share the colour and depth attachments, so
    /// they can merge into the render pass already open. The Graded path (cold) is already two full-screen passes and only changes
    /// its second from a blit to an equivalent raster pass that also reads the stencil. No extra full-screen pass and no extra
    /// texture on either tier.</para>
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
        /// <summary>The K13 stencil mask pass (drawn over the exempt birds, not full-screen).</summary>
        public const int MaskPassIndex = 2;

        [Tooltip("Assets/_Game/Art/Shaders/CWMarkMood.shader. Serialized here so build stripping keeps it.")]
        [SerializeField] private Shader _shader;

        private Material _material;
        private MoodPass _pass;

        private static readonly int DarkId = Shader.PropertyToID("_CW_MarkDark");
        private static readonly int ShapeId = Shader.PropertyToID("_CW_MarkShape");
        private static readonly int RedId = Shader.PropertyToID("_CW_MarkRed");
        private static readonly int GradeId = Shader.PropertyToID("_CW_MarkGrade");
        private static readonly int ExemptRefId = Shader.PropertyToID("_CW_MarkExemptRef");

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

            var hunt = MarkScreenEffect.HuntCurrent;
            MarkScreenEffect.Uniforms(MarkScreenEffect.Current, hunt.Edge, hunt.Colour, hunt.Glow, out var dark, out var shape, out var red, out var grade);
            _material.SetVector(DarkId, dark);
            _material.SetVector(ShapeId, shape);
            _material.SetVector(RedId, red);
            _material.SetVector(GradeId, grade);
            _material.SetFloat(ExemptRefId, FeedbackTuning.MarkExemptStencilValue);

            _pass.Material = _material;
            _pass.Mode = mode;
            _pass.Exempt = MarkScreenEffect.ExemptCount > 0;
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
            public bool Exempt;

            private static readonly ShaderTagId[] MaskTags =
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit"),
            };

            private sealed class OverlayData
            {
                public Material Material;
            }

            private sealed class GradedData
            {
                public Material Material;
                public TextureHandle Source;
            }

            private sealed class MaskData
            {
                public RendererListHandle List;
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

                // K13: the exempt birds' stencil mask goes first; without a depth-stencil to write it into, the tint is drawn whole.
                var depth = TextureHandle.nullHandle;
                if (Exempt)
                {
                    if (resources.activeDepthTexture.IsValid())
                    {
                        depth = resources.activeDepthTexture;
                        AddMaskPass(renderGraph, frameData, target, depth);
                    }
                    else Log.Error("MarkScreenFeature", "no active depth-stencil: the hunt's birds are tinted with the rest of the scene");
                }
                bool masked = depth.IsValid();

                if (Mode == MarkScreenPass.Graded && !resources.isActiveTargetBackBuffer)
                {
                    // Graph-derived descriptor; only diverge where needed: a sampled-only copy needs no MSAA and no clear.
                    var desc = renderGraph.GetTextureDesc(target);
                    desc.name = "_CW_MarkMoodSceneCopy";
                    desc.clearBuffer = false;
                    desc.msaaSamples = MSAASamples.None;
                    var copy = renderGraph.CreateTexture(desc);
                    renderGraph.AddBlitPass(target, copy, Vector2.one, Vector2.zero, passName: "CW Mark Mood Copy");
                    if (masked) AddMaskedGradedPass(renderGraph, copy, target, depth);
                    else
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
                    if (masked) builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);   // read-only: the stencil test skips the exempt birds
                    builder.SetRenderFunc(static (OverlayData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.Material, OverlayPassIndex));
                }
            }

            /// <summary>The Graded pass as a raster pass, only used while a bird is exempt: the AddBlitPass it replaces binds no
            /// depth-stencil. The target is ReadWrite because the pixels the stencil skips must keep the scene colour in it.</summary>
            private void AddMaskedGradedPass(RenderGraph renderGraph, TextureHandle copy, TextureHandle target, TextureHandle depth)
            {
                using (var builder = renderGraph.AddRasterRenderPass<GradedData>("CW Mark Mood Graded", out var data, profilingSampler))
                {
                    data.Material = Material;
                    data.Source = copy;
                    builder.UseTexture(copy, AccessFlags.Read);
                    builder.SetRenderAttachment(target, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                    builder.SetRenderFunc(static (GradedData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, GradedPassIndex));
                }
            }

            /// <summary>Sets the exempt stencil bit where an exempt bird is the front-most surface: its renderers (the rendering-layer
            /// filter) drawn with the mask pass as the override material. The colour attachment is bound ReadWrite (nothing is
            /// written to it) so the mask and the mood pass share one attachment set.</summary>
            private void AddMaskPass(RenderGraph renderGraph, ContextContainer frameData, TextureHandle target, TextureHandle depth)
            {
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                var drawing = RenderingUtils.CreateDrawingSettings(MaskTags[0], renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                for (int i = 1; i < MaskTags.Length; i++) drawing.SetShaderPassName(i, MaskTags[i]);
                drawing.overrideMaterial = Material;
                drawing.overrideMaterialPassIndex = MaskPassIndex;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, FeedbackTuning.MarkExemptRenderingLayer);

                using (var builder = renderGraph.AddRasterRenderPass<MaskData>("CW Mark Mood Exempt Mask", out var data, profilingSampler))
                {
                    data.List = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
                    builder.UseRendererList(data.List);
                    builder.SetRenderAttachment(target, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.List));
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
