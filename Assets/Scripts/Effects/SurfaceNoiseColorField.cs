using System;
using System.Collections.Generic;
using SpiderVerse.LineArt;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// One immutable color snapshot per camera, one cropped field per effect/character.
// RenderGraph owns all temporary textures; no persistent camera-sized allocations.
internal sealed class SurfaceNoiseColorField : IDisposable
{
    ComputeShader compute;
    readonly Material[] masks = new Material[3];
    int initialize, jump;
    static readonly int Enabled = Shader.PropertyToID("_SurfaceColorFieldEnabled");
    readonly List<Material> sharedMaterials = new List<Material>();

    sealed class CopyData { public TextureHandle source, destination; }
    sealed class FieldData
    {
        public SurfaceNoiseColorField owner;
        public SurfaceNoiseParticleEffect effect;
        public Camera camera;
        public ObjectLineArtFeature.CameraSample cameraSample;
        public TextureHandle source, color, depth, sceneDepth, mask, a, b;
        public RectInt rect;
        public int width, height, fieldWidth, fieldHeight, downsample, radius;
        public bool enabled;
    }

    bool EnsureResources()
    {
        if (!SystemInfo.supportsComputeShaders ||
            !SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_UInt, FormatUsage.LoadStore)) return false;
        if (compute == null)
        {
            var resource = Resources.Load<ComputeShader>("SurfaceNoiseColorField");
            if (resource == null) return false;
            compute = UnityEngine.Object.Instantiate(resource);
            compute.hideFlags = HideFlags.HideAndDontSave;
            initialize = compute.FindKernel("Initialize");
            jump = compute.FindKernel("JumpFlood");
        }
        if (masks[0] == null)
        {
            var shader = Resources.Load<Shader>("SurfaceNoiseColorMask");
            if (shader == null || !shader.isSupported) return false;
            for (int i = 0; i < masks.Length; i++)
            {
                masks[i] = CoreUtils.CreateEngineMaterial(shader);
                masks[i].SetFloat("_Cull", i);
            }
        }
        return true;
    }

    internal bool Record(RenderGraph graph, UniversalCameraData cameraData, UniversalResourceData resources,
        SurfaceNoiseParticleEffect[] effects, ObjectLineArtFeature.CameraSample sample)
    {
        int width = cameraData.cameraTargetDescriptor.width;
        int height = cameraData.cameraTargetDescriptor.height;
        // The existing anchor path remains available on unsupported/XR targets.
        if (cameraData.cameraTargetDescriptor.dimension != TextureDimension.Tex2D ||
            width >= 65535 || height >= 65535 || !EnsureResources()) return false;
        bool anyField = Array.Exists(effects, e => e != null && e.NeedsColorField);
        if (!anyField) return false;

        var sourceDesc = graph.GetTextureDesc(resources.activeColorTexture);
        sourceDesc.name = "Surface Noise Full Resolution Color";
        sourceDesc.msaaSamples = MSAASamples.None;
        sourceDesc.bindTextureMS = false;
        sourceDesc.depthBufferBits = DepthBits.None;
        sourceDesc.clearBuffer = false;
        var source = graph.CreateTexture(sourceDesc);
        using (var builder = graph.AddUnsafePass<CopyData>("Surface Noise · full resolution color snapshot", out var data))
        {
            data.source = resources.activeColorTexture;
            data.destination = source;
            builder.UseTexture(data.source, AccessFlags.Read);
            builder.UseTexture(data.destination, AccessFlags.Write);
            builder.SetRenderFunc((CopyData d, UnsafeGraphContext context) =>
                Blitter.BlitCameraTexture(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                    (RTHandle)d.source, (RTHandle)d.destination));
        }

        foreach (var effect in effects)
        {
            if (effect == null || !effect.isActiveAndEnabled || effect.SubmittedParticleCount == 0 ||
                (cameraData.camera.cullingMask & (1 << effect.gameObject.layer)) == 0) continue;
            int radius = Mathf.Clamp(effect.colorExtensionPixels, 8, 256);
            RectInt rect = GetRect(effect, cameraData, width, height, radius + 4);
            if (rect.width == 0 || rect.height == 0) continue;
            bool enabled = effect.NeedsColorField;
            int downsample = effect.halfResolutionColorField ? 2 : 1;
            int fieldWidth = (rect.width + downsample - 1) / downsample;
            int fieldHeight = (rect.height + downsample - 1) / downsample;
            TextureHandle mask = default, a = default, b = default;
            if (enabled)
            {
                mask = graph.CreateTexture(new TextureDesc(width, height)
                {
                    name = "Surface Noise Visible Character Depth", colorFormat = GraphicsFormat.R32_SFloat,
                    filterMode = FilterMode.Point, clearBuffer = false
                });
                var seedDesc = new TextureDesc(fieldWidth, fieldHeight)
                {
                    name = "Surface Noise JFA A", colorFormat = GraphicsFormat.R16G16_UInt,
                    enableRandomWrite = true, filterMode = FilterMode.Point, clearBuffer = false
                };
                a = graph.CreateTexture(seedDesc);
                seedDesc.name = "Surface Noise JFA B";
                b = graph.CreateTexture(seedDesc);
            }
            using (var builder = graph.AddUnsafePass<FieldData>("Surface Noise · character Jump Flood and particles", out var data))
            {
                data.owner = this; data.effect = effect; data.camera = cameraData.camera; data.cameraSample = sample;
                data.source = source; data.color = resources.activeColorTexture; data.depth = resources.activeDepthTexture;
                data.sceneDepth = resources.cameraDepthTexture; data.mask = mask; data.a = a; data.b = b;
                data.width = width; data.height = height; data.rect = rect; data.fieldWidth = fieldWidth;
                data.fieldHeight = fieldHeight; data.downsample = downsample; data.radius = radius; data.enabled = enabled;
                builder.UseTexture(source, AccessFlags.Read);
                // Effects with JFA disabled still sample the original opaque texture.
                builder.UseTexture(resources.cameraOpaqueTexture, AccessFlags.Read);
                builder.UseTexture(data.color, AccessFlags.ReadWrite);
                // Some existing particle materials write depth.
                builder.UseTexture(data.depth, AccessFlags.ReadWrite);
                builder.UseTexture(data.sceneDepth, AccessFlags.Read);
                if (enabled)
                {
                    builder.UseTexture(mask, AccessFlags.ReadWrite);
                    builder.UseTexture(a, AccessFlags.ReadWrite);
                    builder.UseTexture(b, AccessFlags.ReadWrite);
                }
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((FieldData d, UnsafeGraphContext context) =>
                    d.owner.Execute(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), d));
            }
        }
        return true;
    }

    static RectInt GetRect(SurfaceNoiseParticleEffect effect, UniversalCameraData data, int width, int height, int margin)
    {
        var renderers = effect.ColorSourceRenderers;
        if (renderers == null) return new RectInt();
        // This pass renders into intermediate textures. GetGPUProjectionMatrix()
        // consults legacy cameraColorTargetHandle and is invalid during RG recording.
        var vp = GL.GetGPUProjectionMatrix(data.GetProjectionMatrix(), true) * data.GetViewMatrix();
        Vector2 min = new Vector2(width, height), max = Vector2.zero;
        bool found = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                (data.camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
            var bounds = renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                // A box crossing the near plane needs a conservative screen-wide region.
                if (data.camera.WorldToViewportPoint(p).z <= data.camera.nearClipPlane)
                    return new RectInt(0, 0, width, height);
                Vector4 clip = vp * new Vector4(p.x, p.y, p.z, 1);
                Vector2 uv = new Vector2(clip.x, clip.y) / clip.w * 0.5f + Vector2.one * 0.5f;
                if (SystemInfo.graphicsUVStartsAtTop) uv.y = 1 - uv.y;
                Vector2 pixel = Vector2.Scale(uv, new Vector2(width, height));
                min = Vector2.Min(min, pixel); max = Vector2.Max(max, pixel); found = true;
            }
        }
        if (!found) return new RectInt();
        int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x) - margin, 0, width);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(min.y) - margin, 0, height);
        // Anchor the half-resolution grid to the screen, not to a moving bounds
        // origin, so a one-pixel camera motion does not flip every seed's parity.
        x0 &= ~1; y0 &= ~1;
        int x1 = Mathf.Clamp(Mathf.CeilToInt(max.x) + margin, 0, width);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(max.y) + margin, 0, height);
        return new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
    }

    void Execute(CommandBuffer cmd, FieldData d)
    {
        cmd.SetGlobalFloat(Enabled, 0);
        if (d.enabled)
        {
            cmd.BeginSample("Surface Noise · visible character mask");
            cmd.SetRenderTarget(d.mask);
            cmd.SetViewport(new Rect(0, 0, d.width, d.height));
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetGlobalTexture("_CameraDepthTexture", d.sceneDepth);
            foreach (var renderer in d.effect.ColorSourceRenderers)
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                    !renderer.gameObject.activeInHierarchy || (d.camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                renderer.GetSharedMaterials(sharedMaterials);
                for (int i = 0; i < sharedMaterials.Count; i++)
                {
                    Material sourceMaterial = sharedMaterials[i];
                    if (sourceMaterial == null || sourceMaterial.renderQueue > 2500) continue;
                    int cull = sourceMaterial.HasProperty("_Cull") ? Mathf.Clamp((int)sourceMaterial.GetFloat("_Cull"), 0, 2) : 2;
                    cmd.DrawRenderer(renderer, masks[cull], i, 0);
                }
            }
            cmd.EndSample("Surface Noise · visible character mask");
            cmd.BeginSample("Surface Noise · Jump Flood");
            cmd.SetComputeIntParams(compute, "_FullSize", d.width, d.height);
            cmd.SetComputeIntParams(compute, "_FieldSize", d.fieldWidth, d.fieldHeight);
            cmd.SetComputeIntParams(compute, "_Origin", d.rect.x, d.rect.y);
            cmd.SetComputeIntParam(compute, "_Downsample", d.downsample);
            cmd.SetComputeTextureParam(compute, initialize, "_Mask", d.mask);
            cmd.SetComputeTextureParam(compute, initialize, "_SeedsWrite", d.a);
            int gx = (d.fieldWidth + 7) / 8, gy = (d.fieldHeight + 7) / 8;
            cmd.DispatchCompute(compute, initialize, gx, gy, 1);
            TextureHandle read = d.a, write = d.b;
            int firstJump = Mathf.NextPowerOfTwo((d.radius + d.downsample - 1) / d.downsample);
            for (int step = firstJump; step >= 1; step >>= 1)
                Dispatch(step);
            // Local repair reduces errors where separated/concave silhouettes meet.
            Dispatch(4); Dispatch(2); Dispatch(1);
            cmd.EndSample("Surface Noise · Jump Flood");
            cmd.SetGlobalTexture("_SurfaceSourceColor", d.source);
            cmd.SetGlobalTexture("_SurfaceColorMask", d.mask);
            cmd.SetGlobalTexture("_SurfaceColorSeeds", read);
            cmd.SetGlobalVector("_SurfaceColorFieldRect", new Vector4(d.rect.x, d.rect.y, d.fieldWidth, d.fieldHeight));
            cmd.SetGlobalVector("_SurfaceColorFieldParams", new Vector4(d.downsample, d.radius, d.width, d.height));
            cmd.SetGlobalFloat(Enabled, 1);

            void Dispatch(int step)
            {
                cmd.SetComputeIntParam(compute, "_Jump", step);
                cmd.SetComputeTextureParam(compute, jump, "_SeedsRead", read);
                cmd.SetComputeTextureParam(compute, jump, "_SeedsWrite", write);
                cmd.DispatchCompute(compute, jump, gx, gy, 1);
                var swap = read; read = write; write = swap;
            }
        }
        cmd.SetRenderTarget(d.color, d.depth);
        cmd.SetViewport(new Rect(0, 0, d.width, d.height));
        d.effect.DrawFromRenderPass(cmd, d.camera, d.cameraSample);
        cmd.SetGlobalFloat(Enabled, 0);
    }

    public void Dispose()
    {
        CoreUtils.Destroy(compute); compute = null;
        for (int i = 0; i < masks.Length; i++) { CoreUtils.Destroy(masks[i]); masks[i] = null; }
    }
}
