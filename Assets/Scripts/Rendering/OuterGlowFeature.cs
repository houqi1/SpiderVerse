using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Colored outer glow with bloom-style brightness prefilter:
/// character (current materials) → threshold → vertical blur → UV offset → mask body → add.
/// Mask uses full character alpha; blur/offset use brightness-filtered color only.
/// </summary>
public class OuterGlowFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        [Tooltip("Objects on these layers are re-rendered with their current materials for the glow color.")]
        public LayerMask layerMask = 1 << 6; // Character

        [Header("Bloom Prefilter")]
        [Tooltip("Luminance (grayscale) threshold. Below this contributes little/no glow.")]
        [Range(0f, 2f)] public float bloomThreshold = 0.6f;

        [Tooltip("Soft knee as a fraction of threshold (0 = hard cut, 1 = soft).")]
        [Range(0f, 1f)] public float bloomKnee = 0.5f;

        [Header("Glow")]
        [Range(0f, 8f)] public float intensity = 1.2f;

        [Tooltip("Vertical blur radius as a fraction of screen height.")]
        [Range(0f, 0.2f)] public float blurAmount = 0.035f;

        [Tooltip("Constant UV shift when sampling the blurred color (always applied).")]
        public Vector2 baseUVOffset = Vector2.zero;

        [Tooltip("Extra UV shift modulated by Offset Strength Map (screen UV).")]
        public Vector2 uvOffset = Vector2.zero;

        [Tooltip("Screen-space map R: 0.5 = no extra offset, <0.5 negative, >0.5 positive (relative to UV Offset). Gray if empty.")]
        public Texture2D offsetStrengthMap;

        [Tooltip("Tiling (XY) and offset (ZW) for the signed-offset sample of Strength Map.")]
        public Vector4 offsetStrengthMapST = new Vector4(1f, 1f, 0f, 0f);

        [Tooltip("Independent tiling (XY) and offset (ZW) for the second Strength Map sample (smoothstep weight).")]
        public Vector4 offsetStrengthMapST2 = new Vector4(1f, 1f, 0f, 0f);

        [Tooltip("smoothstep edge0 for the second Strength Map sample.")]
        [Range(0f, 1f)] public float offsetStrengthSmoothstepEdge0 = 0f;

        [Tooltip("smoothstep edge1 for the second Strength Map sample.")]
        [Range(0f, 1f)] public float offsetStrengthSmoothstepEdge1 = 1f;

        [Range(0, 2)] public int downsample = 1;

        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        public Shader shader;
    }

    public Settings settings = new Settings();

    Material m_Material;
    OuterGlowPass m_Pass;

    public override void Create()
    {
        if (settings.shader == null)
            settings.shader = Shader.Find("Hidden/Custom/OuterGlow");

        if (settings.shader == null)
            return;

        if (m_Material == null || m_Material.shader != settings.shader)
        {
            if (m_Material != null)
            {
                if (Application.isPlaying)
                    Destroy(m_Material);
                else
                    DestroyImmediate(m_Material);
            }

            m_Material = CoreUtils.CreateEngineMaterial(settings.shader);
        }

        m_Pass = new OuterGlowPass(m_Material, settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Pass == null || m_Material == null)
            return;

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            return;

        m_Pass.renderPassEvent = settings.renderPassEvent;
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        m_Pass = null;
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }

    sealed class OuterGlowPass : ScriptableRenderPass
    {
        const int kPrefilterPass = 0;
        const int kBlurPass = 1;
        const int kCompositePass = 2;

        static readonly int s_VerticalBlurId = Shader.PropertyToID("_VerticalBlur");
        static readonly int s_IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int s_BloomThresholdId = Shader.PropertyToID("_BloomThreshold");
        static readonly int s_BloomKneeId = Shader.PropertyToID("_BloomKnee");
        static readonly int s_BaseUVOffsetId = Shader.PropertyToID("_BaseUVOffset");
        static readonly int s_UVOffsetId = Shader.PropertyToID("_UVOffset");
        static readonly int s_CharacterTexId = Shader.PropertyToID("_CharacterTex");
        static readonly int s_OffsetStrengthMapId = Shader.PropertyToID("_OffsetStrengthMap");
        static readonly int s_OffsetStrengthMapSTId = Shader.PropertyToID("_OffsetStrengthMap_ST");
        static readonly int s_OffsetStrengthMapST2Id = Shader.PropertyToID("_OffsetStrengthMapST2");
        static readonly int s_OffsetStrengthSmoothstepId = Shader.PropertyToID("_OffsetStrengthSmoothstep");

        static readonly List<ShaderTagId> s_ShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        readonly Material m_Material;
        readonly Settings m_Settings;

        public OuterGlowPass(Material material, Settings settings)
        {
            m_Material = material;
            m_Settings = settings;
            profilingSampler = new ProfilingSampler("Outer Glow");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid())
                return;

            int downsample = Mathf.Clamp(m_Settings.downsample, 0, 2);
            TextureDesc colorDesc = cameraColor.GetDescriptor(renderGraph);
            colorDesc.depthBufferBits = DepthBits.None;
            colorDesc.msaaSamples = MSAASamples.None;
            colorDesc.bindTextureMS = false;
            colorDesc.clearBuffer = false;
            // Camera HDR formats like R11G11B10 have no alpha; coverage mask needs A.
            colorDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            colorDesc.width = Math.Max(1, colorDesc.width >> downsample);
            colorDesc.height = Math.Max(1, colorDesc.height >> downsample);

            colorDesc.name = "_OuterGlowCharacterColor";
            TextureHandle characterColor = renderGraph.CreateTexture(colorDesc);

            TextureDesc depthDesc = new TextureDesc(colorDesc.width, colorDesc.height)
            {
                name = "_OuterGlowCharacterDepth",
                depthBufferBits = DepthBits.Depth16,
                msaaSamples = MSAASamples.None,
                bindTextureMS = false,
                filterMode = FilterMode.Point,
                clearBuffer = false,
            };
            TextureHandle characterDepth = renderGraph.CreateTexture(depthDesc);

            colorDesc.name = "_OuterGlowBright";
            TextureHandle brightColor = renderGraph.CreateTexture(colorDesc);

            colorDesc.name = "_OuterGlowBlur";
            TextureHandle blurColor = renderGraph.CreateTexture(colorDesc);

            RecordCharacterPass(renderGraph, frameData, characterColor, characterDepth);

            float blur = Mathf.Max(0f, m_Settings.blurAmount);
            m_Material.SetFloat(s_VerticalBlurId, blur);
            m_Material.SetFloat(s_IntensityId, m_Settings.intensity);
            m_Material.SetFloat(s_BloomThresholdId, Mathf.Max(0f, m_Settings.bloomThreshold));
            m_Material.SetFloat(s_BloomKneeId, Mathf.Clamp01(m_Settings.bloomKnee));
            m_Material.SetVector(s_BaseUVOffsetId, m_Settings.baseUVOffset);
            m_Material.SetVector(s_UVOffsetId, m_Settings.uvOffset);
            Texture offsetMap = m_Settings.offsetStrengthMap != null
                ? m_Settings.offsetStrengthMap
                : Texture2D.grayTexture;
            m_Material.SetTexture(s_OffsetStrengthMapId, offsetMap);
            m_Material.SetVector(s_OffsetStrengthMapSTId, m_Settings.offsetStrengthMapST);
            m_Material.SetVector(s_OffsetStrengthMapST2Id, m_Settings.offsetStrengthMapST2);
            m_Material.SetVector(
                s_OffsetStrengthSmoothstepId,
                new Vector2(m_Settings.offsetStrengthSmoothstepEdge0, m_Settings.offsetStrengthSmoothstepEdge1));

            if (!characterColor.IsValid() || !brightColor.IsValid() || !blurColor.IsValid())
                return;

            // Brightness filter first (bloom-style), then blur only the survivors.
            renderGraph.AddBlitPass(
                new RenderGraphUtils.BlitMaterialParameters(characterColor, brightColor, m_Material, kPrefilterPass),
                "OuterGlow Brightness Prefilter");
            renderGraph.AddBlitPass(
                new RenderGraphUtils.BlitMaterialParameters(brightColor, blurColor, m_Material, kBlurPass),
                "OuterGlow Blur Vertical");

            RecordCompositePass(renderGraph, cameraColor, characterColor, blurColor);
        }

        void RecordCharacterPass(
            RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle characterColor,
            TextureHandle characterDepth)
        {
            using (var builder = renderGraph.AddRasterRenderPass<DrawPassData>(
                       "OuterGlow Draw Character", out var passData))
            {
                InitRendererList(frameData, ref passData, renderGraph);

                if (!passData.rendererListHandle.IsValid())
                    return;

                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(characterColor, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(characterDepth, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (DrawPassData data, RasterGraphContext context) =>
                {
                    // Alpha must be 0 outside the character so composite can mask the body.
                    context.cmd.ClearRenderTarget(RTClearFlags.Color | RTClearFlags.Depth, Color.clear, 1f, 0);
                    context.cmd.DrawRendererList(data.rendererListHandle);
                });
            }
        }

        void RecordCompositePass(
            RenderGraph renderGraph,
            TextureHandle cameraColor,
            TextureHandle characterColor,
            TextureHandle blurColor)
        {
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                       "OuterGlow Composite", out var passData))
            {
                passData.material = m_Material;
                passData.characterColor = characterColor;
                passData.blurColor = blurColor;
                passData.intensity = m_Settings.intensity;
                passData.baseUVOffset = m_Settings.baseUVOffset;
                passData.uvOffset = m_Settings.uvOffset;
                passData.offsetStrengthMap = m_Settings.offsetStrengthMap != null
                    ? m_Settings.offsetStrengthMap
                    : Texture2D.grayTexture;
                passData.offsetStrengthMapST = m_Settings.offsetStrengthMapST;
                passData.offsetStrengthMapST2 = m_Settings.offsetStrengthMapST2;
                passData.offsetStrengthSmoothstep = new Vector2(
                    m_Settings.offsetStrengthSmoothstepEdge0,
                    m_Settings.offsetStrengthSmoothstepEdge1);

                builder.UseTexture(characterColor, AccessFlags.Read);
                builder.UseTexture(blurColor, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColor, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    data.material.SetFloat(s_IntensityId, data.intensity);
                    data.material.SetVector(s_BaseUVOffsetId, data.baseUVOffset);
                    data.material.SetVector(s_UVOffsetId, data.uvOffset);
                    data.material.SetTexture(s_OffsetStrengthMapId, data.offsetStrengthMap);
                    data.material.SetVector(s_OffsetStrengthMapSTId, data.offsetStrengthMapST);
                    data.material.SetVector(s_OffsetStrengthMapST2Id, data.offsetStrengthMapST2);
                    data.material.SetVector(s_OffsetStrengthSmoothstepId, data.offsetStrengthSmoothstep);
                    context.cmd.SetGlobalTexture(s_CharacterTexId, data.characterColor);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.blurColor,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        kCompositePass);
                });
            }
        }

        void InitRendererList(ContextContainer frameData, ref DrawPassData passData, RenderGraph renderGraph)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var filterSettings = new FilteringSettings(RenderQueueRange.opaque, m_Settings.layerMask);
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                s_ShaderTagIds, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);

            var param = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }

        class DrawPassData
        {
            public RendererListHandle rendererListHandle;
        }

        class CompositePassData
        {
            public Material material;
            public TextureHandle characterColor;
            public TextureHandle blurColor;
            public float intensity;
            public Vector2 baseUVOffset;
            public Vector2 uvOffset;
            public Texture offsetStrengthMap;
            public Vector4 offsetStrengthMapST;
            public Vector4 offsetStrengthMapST2;
            public Vector2 offsetStrengthSmoothstep;
        }
    }
}
