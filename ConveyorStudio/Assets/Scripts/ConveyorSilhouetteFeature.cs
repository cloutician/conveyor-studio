using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class ConveyorSilhouetteFeature : ScriptableRendererFeature
{
    public Material maskMaterial;
    public Material compositeMaterial;
    [Range(1f, 4f)] public float widthPixels = 3f;
    private OutlinePass pass;
    public override void Create() => pass = new OutlinePass();
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (maskMaterial == null || compositeMaterial == null || renderingData.cameraData.cameraType != CameraType.Game
            || renderingData.cameraData.camera.GetComponent<ConveyorOrbitCamera>() == null) return;
        bool any = false;
        foreach (var target in ConveyorSilhouette.Active) if (target != null && target.Highlighted) { any = true; break; }
        if (!any) return;
        pass.maskMaterial = maskMaterial; pass.compositeMaterial = compositeMaterial; pass.width = widthPixels;
        renderer.EnqueuePass(pass);
    }

    private sealed class OutlinePass : ScriptableRenderPass
    {
        public Material maskMaterial, compositeMaterial;
        public float width;
        private static readonly int ColourId = Shader.PropertyToID("_ConveyorOutlineColour");
        private static readonly int ParametersId = Shader.PropertyToID("_ConveyorOutlineParameters");
        private struct Draw { public Renderer renderer; public int submeshes; public Color colour; }
        private sealed class MaskData { public Draw[] draws; public Material material; }
        private sealed class CompositeData { public TextureHandle mask; public Material material; public Vector4 parameters; }
        public OutlinePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var draws = new List<Draw>();
            foreach (var target in ConveyorSilhouette.Active)
            {
                if (target == null || !target.Highlighted || target.Renderers == null) continue;
                foreach (var renderer in target.Renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null) draws.Add(new Draw { renderer = renderer, submeshes = mesh.subMeshCount, colour = target.Colour });
                }
            }
            if (draws.Count == 0) return;
            var descriptor = graph.GetTextureDesc(resources.activeColorTexture);
            descriptor.name = "Conveyor combined silhouette mask";
            descriptor.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = true; descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Point;
            TextureHandle mask = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<MaskData>("Conveyor silhouette / combined parts", out var data))
            {
                data.draws = draws.ToArray(); data.material = maskMaterial;
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                // Sampling the resolved scene depth avoids mismatching the mask's MSAA
                // attachments. Occluded surfaces do not become an x-ray outline.
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (MaskData input, RasterGraphContext context) =>
                {
                    foreach (var draw in input.draws)
                    {
                        if (draw.renderer == null) continue;
                        context.cmd.SetGlobalColor(ColourId, draw.colour);
                        for (int submesh = 0; submesh < draw.submeshes; submesh++)
                            context.cmd.DrawRenderer(draw.renderer, input.material, submesh, 0);
                    }
                });
            }
            using (var builder = graph.AddRasterRenderPass<CompositeData>("Conveyor silhouette / outside border only", out var data))
            {
                data.mask = mask; data.material = compositeMaterial;
                data.parameters = new Vector4(1f / descriptor.width, 1f / descriptor.height, width, 0f);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (CompositeData input, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(ParametersId, input.parameters);
                    Blitter.BlitTexture(context.cmd, input.mask, new Vector4(1, 1, 0, 0), input.material, 0);
                });
            }
        }
    }
}
