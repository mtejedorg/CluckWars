using CluckWars.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// PC / console tier surround haze (Phase 8 C F1, Stage 1): one full-screen pass that reconstructs each pixel's
    /// world position from the camera depth texture and blends it toward <see cref="HazeSettings.ShaderColour"/> (<see cref="HazeSettings.Colour"/> in colour-buffer space) by
    /// <see cref="HazeSettings.AmountAt"/> (0 everywhere inside the fence). Lives on <c>PC_Renderer.asset</c> only;
    /// the Mobile tier gets the same look from baked material copies instead (no per-pixel cost there).
    /// </summary>
    /// <remarks>
    /// <para><b>Event: <see cref="RenderPassEvent.AfterRenderingSkybox"/></b> (after opaques + background, before
    /// transparents and before post-processing). Before post so D7 tonemapping / bloom / vignette stay on top.
    /// Before transparents because transparents (particles, range-guide overlays, nameplate quads) do not write depth:
    /// run after them and a particle drawn over a surround pixel would be hazed by the depth BEHIND it. Surround
    /// transparents are not hazed this way, but the surround has none today (trees are alpha-clipped opaques).</para>
    /// <para><b>No colour copy.</b> The pass draws a full-screen triangle with alpha blending, output =
    /// (haze colour, amount), so <c>dst * (1 - a) + haze * a</c> is exactly the lerp. It renders straight into the
    /// active (MSAA) colour target: MSAA 4x needs no resolve, and the only input is the resolved camera depth texture,
    /// requested through <see cref="ScriptableRenderPass.ConfigureInput"/> rather than the RP asset's
    /// "Depth Texture" toggle (A7 keeps that off). On PC the SSAO feature already renders a depth-normals prepass that
    /// fills the depth texture, so the request normally costs nothing extra.</para>
    /// <para><b>Cameras.</b> Base game cameras only, and only while a <see cref="MatchCamera"/> exists (an arena is
    /// loaded): the match camera and the capture tools' free cameras haze; the Bootstrap menu camera, overlay / UI
    /// cameras, previews, reflection and Scene-view cameras do not.</para>
    /// <para><b>Build inclusion.</b> The shader is serialized on this feature inside PC_Renderer.asset, which the PC
    /// RP asset references, so the build keeps it without an "Always Included" entry.</para>
    /// </remarks>
    public sealed class CWHazeFeature : ScriptableRendererFeature
    {
        /// <summary>Shader name of Assets/_Game/Art/Shaders/CWHaze.shader.</summary>
        public const string ShaderName = "Cluck Wars/Haze";

        [Tooltip("Assets/_Game/Art/Shaders/CWHaze.shader. Serialized here so build stripping keeps it.")]
        [SerializeField] private Shader _shader;

        private Material _material;
        private HazePass _pass;

        /// <summary>The haze shader this feature draws with (read by the EditMode tests).</summary>
        public Shader HazeShader => _shader;

        public override void Create()
        {
            if (_shader == null) _shader = Shader.Find(ShaderName);
            if (_material != null) CoreUtils.Destroy(_material);
            _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
            _pass = new HazePass(_material) { renderPassEvent = RenderPassEvent.AfterRenderingSkybox };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldHaze(renderingData.cameraData.camera, renderingData.cameraData.renderType)) return;
            if (_material == null)
            {
                Log.Error("CWHazeFeature", "haze shader '" + ShaderName + "' is missing: the PC surround haze is not drawn");
                return;
            }
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_pass);
        }

        /// <summary>Which cameras get the haze; see the class remarks.</summary>
        public static bool ShouldHaze(Camera camera, CameraRenderType renderType) =>
            camera != null
            && renderType == CameraRenderType.Base
            && camera.cameraType == CameraType.Game
            && MatchCamera.Instance != null;

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private sealed class HazePass : ScriptableRenderPass
        {
            private readonly Material _material;

            private sealed class PassData
            {
                public Material Material;
            }

            public HazePass(Material material)
            {
                _material = material;
                profilingSampler = new ProfilingSampler("CW Haze");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraDepthTexture.IsValid())
                {
                    Log.Error("CWHazeFeature", "no camera depth texture although the pass requested one: haze skipped");
                    return;
                }

                HazeSettings.PushShaderGlobals();
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("CW Haze", out var data, profilingSampler))
                {
                    data.Material = _material;
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.Material, 0));
                }
            }
        }

        /// <summary>
        /// Error sink. A renderer feature is an asset, not a scene object, so it cannot self-inject; it borrows the
        /// project logger once the ProjectContext exists and reports each problem once (a per-frame error would flood).
        /// Before the context exists (Editor before Play) nothing here renders anyway: <see cref="ShouldHaze"/> needs a
        /// live MatchCamera.
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
