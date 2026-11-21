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
    public enum GlitchMode { SliceGlitch, RGBFlicker, NoiseDisplace }

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
        public bool glowEnabled = true;
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

        [Header("Stabilized Screen UV")]
        [Tooltip("Sample Strength Map relative to OuterGlowAnchor screen position (scheme 2).")]
        public bool useStabilizedScreenUV = true;

        [Tooltip("Scale Strength Map local UV by (anchorDepth / referenceDistance).")]
        public bool stabilizeDistanceCompensation = true;

        [Tooltip("At this camera distance, Strength Map depth scale = 1. Also used as derivative reference.")]
        [Min(0.01f)] public float stabilizedReferenceDistance = 5f;

        [Header("Derivative Scale")]
        [Tooltip("Scale Blur Amount and UV Offset by screen-space derivatives of the anchor plane (world meters/pixel).")]
        public bool useDerivativeScale = true;

        [Header("Mask Edge")]
        [Tooltip("Shrink body coverage in Character RT texels so glow fills up to the silhouette (fixes gap/seam).")]
        [Range(0f, 4f)] public float maskErode = 1f;

        [Tooltip("Feather coverage with smoothstep(0, softness, alpha). Higher = softer hole edge.")]
        [Range(0.01f, 1f)] public float maskSoftness = 0.35f;

        [Range(0, 2)] public int downsample = 1;

        [Header("Character Glitch (Shared Character RT)")]
        [Tooltip("Uses the same character capture and Layer Mask as glow. Keep only the target character on those layers. Scene occlusion is not applied.")]
        public bool glitchEnabled = false;
        public GlitchMode glitchMode = GlitchMode.SliceGlitch;
        [Tooltip("Slice mode: overlay opacity. RGB mode: displacement strength; the split body is fully recomposed, not blended with a normal duplicate.")]
        [Range(0f, 1f)] public float glitchOpacity = 0.7f;
        [Tooltip("Maximum horizontal displacement as a fraction of screen width.")]
        [Range(0f, 0.15f)] public float glitchDisplacement = 0.025f;
        [Range(0f, 0.03f)] public float glitchRGBSplit = 0.003f;
        [Tooltip("Slice height in full-resolution screen pixels.")]
        [Range(1f, 128f)] public float glitchSliceHeight = 14f;
        [Range(0f, 1f)] public float glitchSliceDensity = 0.3f;
        [Tooltip("Fraction of time steps with a glitch burst.")]
        [Range(0f, 1f)] public float glitchBurstProbability = 0.65f;
        [Range(0f, 60f)] public float glitchUpdatesPerSecond = 12f;
        [Tooltip("Opacity of a second displaced copy from this frame (no history buffer).")]
        [Range(0f, 1f)] public float glitchEchoOpacity = 0.3f;
        public int glitchSeed = 1;

        [Header("RGB Flicker")]
        [Tooltip("Sampled once per update (Updates Per Second, same interval as the Noise Displace noise; no screen UV). RG is remapped to (-1,1) and scales the red offset on X/Y; blue samples the same noise at a tick offset. Gray if empty (no offset).")]
        public Texture2D rgbNoiseTexture;
        [Tooltip("Noise sample UV advance per update.")]
        public Vector2 rgbNoiseSpeed = new Vector2(0.17f, 0.11f);
        [Tooltip("Ticks added to the tick used by the blue channel's noise sample.")]
        public float rgbBlueTickOffset = 3f;
        [Tooltip("Gate noise sampled by continuous time (R channel, no screen UV). Flicker is on while its value >= Gate Threshold. White if empty (always on).")]
        public Texture2D rgbGateTexture;
        [Tooltip("Gate noise sample UV advance per second.")]
        public Vector2 rgbGateSpeed = new Vector2(0.2f, 0.13f);
        [Tooltip("step(threshold, gate noise R): higher = flicker is on less often.")]
        [Range(0f, 1f)] public float rgbGateThreshold = 0.5f;
        [Tooltip("Maximum red screen displacement relative to the stationary green channel, in fractions of screen size, reached at noise value 1/-1.")]
        public Vector2 rgbRedOffset = new Vector2(0.012f, 0.002f);
        public Vector2 rgbBlueOffset = new Vector2(-0.012f, -0.002f);
        [Tooltip("RGB Flicker only: draw the displaced colors only outside the character silhouette.")]
        public bool rgbOutsideOnly = false;
        [Tooltip("Tint of the displaced red-channel layer. Defaults reproduce the original channel split.")]
        [ColorUsage(false, true)] public Color rgbRedColor = Color.red;
        [Tooltip("Tint of the displaced blue-channel layer.")]
        [ColorUsage(false, true)] public Color rgbBlueColor = Color.blue;

        [Header("Noise Displace")]
        [Tooltip("RG is remapped to (-1,1) and displaces the character; RGB is added on top of the displaced character. Gray if empty.")]
        public Texture2D noiseTexture;
        [Tooltip("Tiling (XY) and offset (ZW) of the noise in screen UV.")]
        public Vector4 noiseST = new Vector4(1f, 1f, 0f, 0f);
        [Tooltip("Displacement as a fraction of screen size at remapped noise value 1.")]
        [Range(0f, 0.2f)] public float noiseDisplacement = 0.03f;
        [Tooltip("Hue rotation of the displaced character, in turns, where noise B = 1 (0.5 gray noise gives half of this).")]
        [Range(0f, 2f)] public float noiseHueShift = 1f;
        [Tooltip("Noise UV scroll per update (Updates Per Second).")]
        public Vector2 noiseScrollPerTick = new Vector2(0.05f, 0f);
        [Tooltip("Second noise: RG is remapped to (-1,1) and multiplied onto the first remapped noise. Gray if empty (0 after remap).")]
        public Texture2D noiseTexture2;
        [Tooltip("Third texture: max-blended into the displaced character color before the hue shift. Black if empty (no effect).")]
        public Texture2D noiseTexture3;
        [Tooltip("Tiling of the third texture in screen UV.")]
        [Min(0.01f)] public float noiseTiling3 = 1f;
        [Tooltip("Third texture UV scroll per update (Updates Per Second), same rate as the first noise.")]
        public Vector2 noiseScroll3PerTick = new Vector2(0.05f, 0f);
        [Tooltip("Tiling (XY) and offset (ZW) of the second noise; applied to the time-driven sample UV, not screen UV.")]
        public Vector4 noiseST2 = new Vector4(1f, 1f, 0f, 0f);
        [Tooltip("Second noise sample UV = this * time in seconds (before tiling; no screen coordinates).")]
        public Vector2 noiseSpeed2 = new Vector2(0.1f, 0.1f);
        [Tooltip("Blend of the multiply: 0 = first noise unchanged, 1 = first noise multiplied by the second remapped noise.")]
        [Range(0f, 1f)] public float noiseMultiplyStrength = 1f;

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
        if (!settings.glowEnabled && (!settings.glitchEnabled || settings.glitchOpacity <= 0f))
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
        const int kGlitchPass = 3;
        const int kRGBPass = 4;
        const int kNoisePass = 5;
        static readonly int s_NoiseParamsId = Shader.PropertyToID("_NoiseParams");
        static readonly int s_NoiseSTId = Shader.PropertyToID("_NoiseST");
        static readonly int s_NoiseScrollId = Shader.PropertyToID("_NoiseScroll");
        static readonly int s_GlitchNoiseTexId = Shader.PropertyToID("_GlitchNoiseTex");
        static readonly int s_GlitchNoiseTex2Id = Shader.PropertyToID("_GlitchNoiseTex2");
        static readonly int s_NoiseSpeed2Id = Shader.PropertyToID("_NoiseSpeed2");
        static readonly int s_NoiseST2Id = Shader.PropertyToID("_NoiseST2");
        static readonly int s_GlitchNoiseTex3Id = Shader.PropertyToID("_GlitchNoiseTex3");
        static readonly int s_NoiseTiling3Id = Shader.PropertyToID("_NoiseTiling3");
        static readonly int s_NoiseScroll3Id = Shader.PropertyToID("_NoiseScroll3");
        static readonly int s_RGBOffsetsId = Shader.PropertyToID("_RGBOffsets");
        static readonly int s_RGBActiveId = Shader.PropertyToID("_RGBActive");
        static readonly int s_RGBNoiseParamsId = Shader.PropertyToID("_RGBNoiseParams");
        static readonly int s_RGBNoiseTexId = Shader.PropertyToID("_RGBNoiseTex");
        static readonly int s_RGBGateParamsId = Shader.PropertyToID("_RGBGateParams");
        static readonly int s_RGBGateTexId = Shader.PropertyToID("_RGBGateTex");
        static readonly int s_RGBOutsideOnlyId = Shader.PropertyToID("_RGBOutsideOnly");
        static readonly int s_RGBColorRId = Shader.PropertyToID("_RGBColorR");
        static readonly int s_RGBColorBId = Shader.PropertyToID("_RGBColorB");
        static readonly int s_GlitchParamsId = Shader.PropertyToID("_GlitchParams");
        static readonly int s_GlitchTimingId = Shader.PropertyToID("_GlitchTiming");
        static readonly int s_GlitchStyleId = Shader.PropertyToID("_GlitchStyle");

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
        static readonly int s_StabilizedAnchorUVId = Shader.PropertyToID("_StabilizedAnchorUV");
        static readonly int s_StabilizedDepthScaleId = Shader.PropertyToID("_StabilizedDepthScale");
        static readonly int s_StabilizedEnabledId = Shader.PropertyToID("_StabilizedEnabled");
        static readonly int s_MaskErodeId = Shader.PropertyToID("_MaskErode");
        static readonly int s_MaskSoftnessId = Shader.PropertyToID("_MaskSoftness");
        static readonly int s_CharacterTexTexelSizeId = Shader.PropertyToID("_CharacterTex_TexelSize");
        static readonly int s_DerivativeScaleEnabledId = Shader.PropertyToID("_DerivativeScaleEnabled");
        static readonly int s_AnchorViewDepthId = Shader.PropertyToID("_AnchorViewDepth");
        static readonly int s_RefWorldHeightId = Shader.PropertyToID("_RefWorldHeight");

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
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid())
                return;

            Camera camera = cameraData.camera;
            ApplyStabilizedScreenUV(camera);
            ApplyDerivativeScale(camera);

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
            m_Material.SetFloat(s_MaskErodeId, Mathf.Max(0f, m_Settings.maskErode));
            m_Material.SetFloat(s_MaskSoftnessId, Mathf.Clamp(m_Settings.maskSoftness, 0.01f, 1f));
            m_Material.SetVector(
                s_CharacterTexTexelSizeId,
                new Vector4(1f / colorDesc.width, 1f / colorDesc.height, colorDesc.width, colorDesc.height));

            if (!characterColor.IsValid() || !brightColor.IsValid() || !blurColor.IsValid())
                return;

            if (m_Settings.glowEnabled)
            {
                // Brightness filter first (bloom-style), then blur only the survivors.
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(characterColor, brightColor, m_Material, kPrefilterPass),
                    "OuterGlow Brightness Prefilter");
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(brightColor, blurColor, m_Material, kBlurPass),
                    "OuterGlow Blur Vertical");

                RecordCompositePass(renderGraph, cameraColor, characterColor, blurColor);
            }
            if (m_Settings.glitchEnabled && m_Settings.glitchOpacity > 0f)
                RecordGlitchPass(renderGraph, cameraColor, characterColor, cameraData.cameraTargetDescriptor.height);
        }

        void RecordGlitchPass(RenderGraph graph, TextureHandle cameraColor, TextureHandle characterColor, int screenHeight)
        {
            using (var builder = graph.AddRasterRenderPass<GlitchPassData>("Character Glitch (Shared RT)", out var data))
            {
                data.material = m_Material;
                data.characterColor = characterColor;
                data.parameters = new Vector4(Mathf.Clamp01(m_Settings.glitchOpacity),
                    Mathf.Max(0f, m_Settings.glitchDisplacement), Mathf.Max(0f, m_Settings.glitchRGBSplit),
                    Mathf.Max(1f, m_Settings.glitchSliceHeight) / Mathf.Max(1, screenHeight));
                data.timing = new Vector4((float)(Math.Floor(Time.realtimeSinceStartupAsDouble *
                    Math.Max(0f, m_Settings.glitchUpdatesPerSecond)) % 65536.0),
                    Mathf.Clamp01(m_Settings.glitchSliceDensity), Mathf.Clamp01(m_Settings.glitchBurstProbability),
                    m_Settings.glitchSeed % 65536);
                data.style = new Vector4(Mathf.Clamp01(m_Settings.glitchEchoOpacity),
                    m_Material.GetVector(s_StabilizedAnchorUVId).y,
                    m_Material.GetFloat(s_StabilizedEnabledId), 0f);
                data.shaderPass = m_Settings.glitchMode == GlitchMode.RGBFlicker ? kRGBPass :
                    m_Settings.glitchMode == GlitchMode.NoiseDisplace ? kNoisePass : kGlitchPass;
                data.noiseTexture = m_Settings.noiseTexture != null ? m_Settings.noiseTexture : Texture2D.grayTexture;
                data.noiseParams = new Vector4(Mathf.Max(0f, m_Settings.noiseDisplacement),
                    Mathf.Max(0f, m_Settings.noiseHueShift), Mathf.Clamp01(m_Settings.noiseMultiplyStrength),
                    (float)(Time.realtimeSinceStartupAsDouble % 65536.0));
                data.noiseTexture2 = m_Settings.noiseTexture2 != null ? m_Settings.noiseTexture2 : Texture2D.grayTexture;
                data.noiseSpeed2 = m_Settings.noiseSpeed2;
                data.noiseST2 = m_Settings.noiseST2;
                data.noiseTexture3 = m_Settings.noiseTexture3 != null ? m_Settings.noiseTexture3 : Texture2D.blackTexture;
                data.noiseTiling3 = Mathf.Max(0.01f, m_Settings.noiseTiling3);
                data.noiseScroll3 = m_Settings.noiseScroll3PerTick;
                data.noiseScroll = m_Settings.noiseScrollPerTick;
                data.noiseST = m_Settings.noiseST;
                float rgbStrength = Mathf.Clamp01(m_Settings.glitchOpacity);
                data.rgbOffsets = new Vector4(m_Settings.rgbRedOffset.x, m_Settings.rgbRedOffset.y,
                    m_Settings.rgbBlueOffset.x, m_Settings.rgbBlueOffset.y) * rgbStrength;
                data.rgbActive = data.rgbOffsets.sqrMagnitude > 1e-12f ? 1f : 0f;
                data.rgbNoiseParams = new Vector4(m_Settings.rgbNoiseSpeed.x, m_Settings.rgbNoiseSpeed.y,
                    m_Settings.rgbBlueTickOffset, 0f);
                data.rgbGateParams = new Vector4(m_Settings.rgbGateSpeed.x, m_Settings.rgbGateSpeed.y,
                    (float)(Time.realtimeSinceStartupAsDouble % 65536.0), m_Settings.rgbGateThreshold);
                data.rgbGateTexture = m_Settings.rgbGateTexture != null ? m_Settings.rgbGateTexture : Texture2D.whiteTexture;
                data.rgbNoiseTexture = m_Settings.rgbNoiseTexture != null ? m_Settings.rgbNoiseTexture : Texture2D.grayTexture;
                data.rgbOutsideOnly = m_Settings.rgbOutsideOnly ? 1f : 0f;
                data.rgbColorR = m_Settings.rgbRedColor;
                data.rgbColorB = m_Settings.rgbBlueColor;
                builder.UseTexture(characterColor, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColor, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (GlitchPassData pass, RasterGraphContext context) =>
                {
                    pass.material.SetVector(s_GlitchParamsId, pass.parameters);
                    pass.material.SetVector(s_GlitchTimingId, pass.timing);
                    pass.material.SetVector(s_GlitchStyleId, pass.style);
                    pass.material.SetVector(s_RGBOffsetsId, pass.rgbOffsets);
                    pass.material.SetVector(s_RGBNoiseParamsId, pass.rgbNoiseParams);
                    pass.material.SetTexture(s_RGBNoiseTexId, pass.rgbNoiseTexture);
                    pass.material.SetVector(s_RGBGateParamsId, pass.rgbGateParams);
                    pass.material.SetTexture(s_RGBGateTexId, pass.rgbGateTexture);
                    pass.material.SetFloat(s_RGBActiveId, pass.rgbActive);
                    pass.material.SetFloat(s_RGBOutsideOnlyId, pass.rgbOutsideOnly);
                    pass.material.SetColor(s_RGBColorRId, pass.rgbColorR);
                    pass.material.SetColor(s_RGBColorBId, pass.rgbColorB);
                    pass.material.SetVector(s_NoiseParamsId, pass.noiseParams);
                    pass.material.SetVector(s_NoiseSTId, pass.noiseST);
                    pass.material.SetVector(s_NoiseScrollId, pass.noiseScroll);
                    pass.material.SetTexture(s_GlitchNoiseTexId, pass.noiseTexture);
                    pass.material.SetVector(s_NoiseSpeed2Id, pass.noiseSpeed2);
                    pass.material.SetVector(s_NoiseST2Id, pass.noiseST2);
                    pass.material.SetTexture(s_GlitchNoiseTex3Id, pass.noiseTexture3);
                    pass.material.SetFloat(s_NoiseTiling3Id, pass.noiseTiling3);
                    pass.material.SetVector(s_NoiseScroll3Id, pass.noiseScroll3);
                    pass.material.SetTexture(s_GlitchNoiseTex2Id, pass.noiseTexture2);
                    Blitter.BlitTexture(context.cmd, pass.characterColor, new Vector4(1f, 1f, 0f, 0f), pass.material, pass.shaderPass);
                });
            }
        }

        sealed class GlitchPassData
        {
            public Material material;
            public TextureHandle characterColor;
            public Vector4 parameters, timing, style;
            public Vector4 rgbOffsets, rgbNoiseParams, rgbGateParams;
            public Texture2D rgbNoiseTexture, rgbGateTexture;
            public float rgbActive;
            public float rgbOutsideOnly;
            public Color rgbColorR, rgbColorB;
            public Vector4 noiseParams, noiseST, noiseST2;
            public Vector2 noiseScroll, noiseSpeed2;
            public Texture2D noiseTexture, noiseTexture2, noiseTexture3;
            public float noiseTiling3;
            public Vector2 noiseScroll3;
            public int shaderPass;
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
                passData.stabilizedAnchorUV = m_Material.GetVector(s_StabilizedAnchorUVId);
                passData.stabilizedDepthScale = m_Material.GetFloat(s_StabilizedDepthScaleId);
                passData.stabilizedEnabled = m_Material.GetFloat(s_StabilizedEnabledId);
                passData.maskErode = m_Material.GetFloat(s_MaskErodeId);
                passData.maskSoftness = m_Material.GetFloat(s_MaskSoftnessId);
                passData.characterTexelSize = m_Material.GetVector(s_CharacterTexTexelSizeId);
                passData.derivativeScaleEnabled = m_Material.GetFloat(s_DerivativeScaleEnabledId);
                passData.anchorViewDepth = m_Material.GetFloat(s_AnchorViewDepthId);
                passData.refWorldHeight = m_Material.GetFloat(s_RefWorldHeightId);

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
                    data.material.SetVector(s_StabilizedAnchorUVId, data.stabilizedAnchorUV);
                    data.material.SetFloat(s_StabilizedDepthScaleId, data.stabilizedDepthScale);
                    data.material.SetFloat(s_StabilizedEnabledId, data.stabilizedEnabled);
                    data.material.SetFloat(s_MaskErodeId, data.maskErode);
                    data.material.SetFloat(s_MaskSoftnessId, data.maskSoftness);
                    data.material.SetVector(s_CharacterTexTexelSizeId, data.characterTexelSize);
                    data.material.SetFloat(s_DerivativeScaleEnabledId, data.derivativeScaleEnabled);
                    data.material.SetFloat(s_AnchorViewDepthId, data.anchorViewDepth);
                    data.material.SetFloat(s_RefWorldHeightId, data.refWorldHeight);
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

        void ApplyStabilizedScreenUV(Camera camera)
        {
            if (!m_Settings.useStabilizedScreenUV || camera == null)
            {
                DisableStabilizedUV();
                return;
            }

            if (!TryGetAnchorViewport(camera, out Vector3 viewport))
            {
                DisableStabilizedUV();
                return;
            }

            float depth = Mathf.Max(viewport.z, 1e-3f);
            float reference = Mathf.Max(m_Settings.stabilizedReferenceDistance, 0.01f);
            float depthScale = m_Settings.stabilizeDistanceCompensation ? depth / reference : 1f;

            m_Material.SetVector(s_StabilizedAnchorUVId, new Vector4(viewport.x, viewport.y, 0f, 0f));
            m_Material.SetFloat(s_StabilizedDepthScaleId, depthScale);
            m_Material.SetFloat(s_StabilizedEnabledId, 1f);
        }

        void DisableStabilizedUV()
        {
            m_Material.SetVector(s_StabilizedAnchorUVId, new Vector4(0.5f, 0.5f, 0f, 0f));
            m_Material.SetFloat(s_StabilizedDepthScaleId, 1f);
            m_Material.SetFloat(s_StabilizedEnabledId, 0f);
        }

        void ApplyDerivativeScale(Camera camera)
        {
            if (!m_Settings.useDerivativeScale || camera == null)
            {
                DisableDerivativeScale();
                return;
            }

            if (!TryGetAnchorViewport(camera, out Vector3 viewport))
            {
                DisableDerivativeScale();
                return;
            }

            float reference = Mathf.Max(m_Settings.stabilizedReferenceDistance, 0.01f);
            float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float worldHeightAtRef = 2f * reference * Mathf.Tan(halfFovRad);

            m_Material.SetFloat(s_DerivativeScaleEnabledId, 1f);
            m_Material.SetFloat(s_AnchorViewDepthId, Mathf.Max(viewport.z, 1e-3f));
            m_Material.SetFloat(s_RefWorldHeightId, worldHeightAtRef);
        }

        void DisableDerivativeScale()
        {
            m_Material.SetFloat(s_DerivativeScaleEnabledId, 0f);
            m_Material.SetFloat(s_AnchorViewDepthId, 1f);
            m_Material.SetFloat(s_RefWorldHeightId, 1f);
        }

        bool TryGetAnchorViewport(Camera camera, out Vector3 viewport)
        {
            viewport = default;
            if (camera == null || !TryGetStabilizedAnchorPosition(out Vector3 anchorWS))
                return false;

            viewport = camera.WorldToViewportPoint(anchorWS);
            return viewport.z > 0f;
        }

        bool TryGetStabilizedAnchorPosition(out Vector3 position)
        {
            if (OuterGlowAnchor.Current != null)
            {
                position = OuterGlowAnchor.Current.AnchorPosition;
                return true;
            }

            // Fallback: first renderer on the glow layer mask (works in Edit Mode too).
            int mask = m_Settings.layerMask.value;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                if (((1 << r.gameObject.layer) & mask) == 0)
                    continue;

                position = r.bounds.center;
                return true;
            }

            position = default;
            return false;
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
            public Vector4 stabilizedAnchorUV;
            public float stabilizedDepthScale;
            public float stabilizedEnabled;
            public float maskErode;
            public float maskSoftness;
            public Vector4 characterTexelSize;
            public float derivativeScaleEnabled;
            public float anchorViewDepth;
            public float refWorldHeight;
        }
    }
}
