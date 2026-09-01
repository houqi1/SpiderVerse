using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Multi-layer character outline (scheme 1):
/// Screen mode: one body mask, each layer ring = Expand(outer) - Expand(inner).
/// Extrusion mode: masks at each unique extrusion, each layer ring = Mask(outerExt) - Mask(innerExt).
/// </summary>
public class CharacterOutlineFeature : ScriptableRendererFeature
{
    public const int MaxRecommendedLayers = 8;

    /// <summary>
    /// Value type so Inspector list elements cannot share references (class lists were overwriting each other).
    /// </summary>
    [Serializable]
    public struct OutlineLayer
    {
        public bool enabled;
        [ColorUsage(true, true)] public Color color;
        [Range(0f, 8f)] public float intensity;
        [Tooltip("Screen-space ring offset in pixels (XY), before distance compensation.")]
        public Vector2 offset;

        [Header("Screen Expand Mode")]
        [Tooltip("Outer expand width in pixels at the reference distance.")]
        [Range(0f, 32f)] public float outerWidth;
        [Tooltip("Inner expand width in pixels. Ring = Expand(outer) - Expand(inner).")]
        [Range(0f, 32f)] public float innerWidth;

        [Header("Normal Extrusion Mode")]
        [Tooltip("Outer object-space extrusion. Ring = Mask(outer) - Mask(inner).")]
        public float outerExtrusion;
        [Tooltip("Inner object-space extrusion (0 = body).")]
        public float innerExtrusion;

        [Header("Control Map")]
        [Tooltip("Tiling (XY) and offset (ZW) for the shared outline control/noise map on this layer.")]
        public Vector4 controlMapST;

        [Tooltip("outline *= step(threshold, noise) for this layer.")]
        [Range(0f, 1f)] public float controlThreshold;

        public static OutlineLayer Default => new OutlineLayer
        {
            enabled = true,
            color = Color.white, // HDR-capable via ColorUsage
            intensity = 1f,
            offset = Vector2.zero,
            outerWidth = 4f,
            innerWidth = 0f,
            outerExtrusion = 0.02f,
            innerExtrusion = 0f,
            controlMapST = new Vector4(1f, 1f, 0f, 0f),
            controlThreshold = 0.5f,
        };
    }

    [Serializable]
    public class Settings
    {
        [Tooltip("Objects on these layers are drawn into the mask.")]
        public LayerMask layerMask = 1 << 6; // Character

        [Header("Mask")]
        [Tooltip("On: scheme 1 uses normal-extruded masks. Off: scheme 1 uses screen-space expand.")]
        public bool useNormalExtrusionMask = false;

        [Tooltip("Extrude using smooth normals baked into vertex color (tangent space). Required for correct skinned outlines.")]
        public bool useSmoothNormalsFromVertexColor = false;

        [Header("Outline Control Map")]
        [Tooltip("Shared noise map. Sampled with mesh UV0 * per-layer tiling/offset (R). Empty = always on.")]
        public Texture2D outlineControlMap;

        [Tooltip("Invert the step result (outline where noise < threshold).")]
        public bool outlineControlInvert = false;

        [Header("Layers (Scheme 1)")]
        [Tooltip("Each enabled layer draws one ring. Arbitrary count; keep small for performance.")]
        public List<OutlineLayer> layers = new List<OutlineLayer>
        {
            OutlineLayer.Default
        };

        [Header("Distance Stability")]
        [Tooltip("Scale screen widths/offset by (referenceDistance / anchorDepth).")]
        public bool compensateDistance = true;

        [Tooltip("At this camera distance to CharacterOutlineAnchor, screen widths/offset are used as-is.")]
        [Min(0.01f)] public float referenceDistance = 5f;

        [Tooltip("Unused while mask ZTests against full-resolution scene depth.")]
        [Range(0, 2)] public int downsample = 0;

        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        public Shader outlineShader;
        public Shader maskShader;

        // Legacy single-layer fields (migrated into layers[0] once).
        [HideInInspector, ColorUsage(true, true)] public Color outlineColor = Color.white;
        [HideInInspector] public float outerWidth = 4f;
        [HideInInspector] public float innerWidth = 0f;
        [HideInInspector] public Vector2 outlineOffset = Vector2.zero;
        [HideInInspector] public float intensity = 1f;
        [HideInInspector] public float normalExtrusionOuter = 0.02f;
        [HideInInspector] public float normalExtrusionInner = 0f;
        [HideInInspector] public bool legacyMigrated;
    }

    public Settings settings = new Settings();

    Material m_OutlineMaterial;
    readonly Dictionary<int, Material> m_MaskMaterialPool = new Dictionary<int, Material>();
    readonly List<Material> m_OutlineMaterialPerLayer = new List<Material>();
    CharacterOutlinePass m_Pass;

    public override void Create()
    {
        if (settings.outlineShader == null)
            settings.outlineShader = Shader.Find("Hidden/Custom/CharacterOutline");
        if (settings.maskShader == null)
            settings.maskShader = Shader.Find("Hidden/Custom/CharacterOutlineMask");

        if (settings.outlineShader == null || settings.maskShader == null)
            return;

        MigrateLegacySettings(settings);

        if (m_OutlineMaterialPerLayer.Count > 0 &&
            m_OutlineMaterialPerLayer[0] != null &&
            m_OutlineMaterialPerLayer[0].shader != settings.outlineShader)
        {
            ClearOutlineMaterialPool();
        }

        if (m_OutlineMaterial == null || m_OutlineMaterial.shader != settings.outlineShader)
        {
            CoreUtils.Destroy(m_OutlineMaterial);
            m_OutlineMaterial = CoreUtils.CreateEngineMaterial(settings.outlineShader);
        }

        // Drop pooled mask materials if shader changed.
        if (m_MaskMaterialPool.Count > 0)
        {
            foreach (var kv in m_MaskMaterialPool)
            {
                if (kv.Value != null && kv.Value.shader != settings.maskShader)
                {
                    ClearMaskMaterialPool();
                    break;
                }
            }
        }

        EnsureOutlineMaterials(Mathf.Max(1, settings.layers != null ? settings.layers.Count : 1));

        m_Pass = new CharacterOutlinePass(
            GetOutlineMaterialForLayer, GetOrCreateMaskMaterial, settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    Material GetOrCreateMaskMaterial(float extrusion)
    {
        int key = Mathf.RoundToInt(extrusion * 10000f);
        if (!m_MaskMaterialPool.TryGetValue(key, out Material mat) || mat == null)
        {
            mat = CoreUtils.CreateEngineMaterial(settings.maskShader);
            m_MaskMaterialPool[key] = mat;
        }

        ApplyMaskMaterialParams(mat, extrusion);
        return mat;
    }

    void ApplyMaskMaterialParams(Material mat, float extrusion)
    {
        mat.SetFloat("_NormalExtrusion", extrusion);
        mat.SetFloat("_UseSmoothNormalVC", settings.useSmoothNormalsFromVertexColor ? 1f : 0f);

    }

    Material GetOutlineMaterialForLayer(int layerIndex)
    {
        EnsureOutlineMaterials(layerIndex + 1);
        return m_OutlineMaterialPerLayer[layerIndex];
    }

    void EnsureOutlineMaterials(int count)
    {
        while (m_OutlineMaterialPerLayer.Count < count)
        {
            Material mat = CoreUtils.CreateEngineMaterial(settings.outlineShader);
            m_OutlineMaterialPerLayer.Add(mat);
        }

        // Keep shared material for fallback/legacy.
        if (m_OutlineMaterial == null || m_OutlineMaterial.shader != settings.outlineShader)
        {
            CoreUtils.Destroy(m_OutlineMaterial);
            m_OutlineMaterial = CoreUtils.CreateEngineMaterial(settings.outlineShader);
        }
    }

    void ClearMaskMaterialPool()
    {
        foreach (var kv in m_MaskMaterialPool)
            CoreUtils.Destroy(kv.Value);
        m_MaskMaterialPool.Clear();
    }

    void ClearOutlineMaterialPool()
    {
        for (int i = 0; i < m_OutlineMaterialPerLayer.Count; i++)
            CoreUtils.Destroy(m_OutlineMaterialPerLayer[i]);
        m_OutlineMaterialPerLayer.Clear();
    }

    static void MigrateLegacySettings(Settings s)
    {
        if (s.layers == null)
            s.layers = new List<OutlineLayer>();

        if (s.layers.Count == 0)
        {
            s.layers.Add(new OutlineLayer
            {
                enabled = true,
                color = s.outlineColor,
                intensity = s.intensity,
                offset = s.outlineOffset,
                outerWidth = s.outerWidth,
                innerWidth = s.innerWidth,
                outerExtrusion = s.normalExtrusionOuter,
                innerExtrusion = s.normalExtrusionInner,
                controlMapST = new Vector4(1f, 1f, 0f, 0f),
                controlThreshold = 0.5f,
            });
            s.legacyMigrated = true;
            return;
        }

        if (!s.legacyMigrated && s.layers.Count == 1)
        {
            OutlineLayer L = s.layers[0];
            bool layerLooksDefault =
                L.color == Color.white &&
                Mathf.Approximately(L.intensity, 1f) &&
                L.offset == Vector2.zero &&
                Mathf.Approximately(L.outerWidth, 4f) &&
                Mathf.Approximately(L.innerWidth, 0f);

            bool legacyLooksCustom =
                s.outlineColor != Color.white ||
                !Mathf.Approximately(s.intensity, 1f) ||
                s.outlineOffset != Vector2.zero ||
                !Mathf.Approximately(s.outerWidth, 4f) ||
                !Mathf.Approximately(s.normalExtrusionOuter, 0.02f);

            if (layerLooksDefault && legacyLooksCustom)
            {
                L.color = s.outlineColor;
                L.intensity = s.intensity;
                L.offset = s.outlineOffset;
                L.outerWidth = s.outerWidth;
                L.innerWidth = s.innerWidth;
                L.outerExtrusion = s.normalExtrusionOuter;
                L.innerExtrusion = s.normalExtrusionInner;
                if (L.controlMapST.x == 0f && L.controlMapST.y == 0f)
                    L.controlMapST = new Vector4(1f, 1f, 0f, 0f);
                s.layers[0] = L;
            }

            s.legacyMigrated = true;
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Pass == null || m_OutlineMaterial == null || settings.maskShader == null)
            return;

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            return;

        m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        m_Pass.renderPassEvent = settings.renderPassEvent;
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        m_Pass = null;
        CoreUtils.Destroy(m_OutlineMaterial);
        m_OutlineMaterial = null;
        ClearMaskMaterialPool();
        ClearOutlineMaterialPool();
    }

    sealed class CharacterOutlinePass : ScriptableRenderPass
    {
        const int kCompositePass = 0;

        static readonly int s_OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int s_OutlineWidthOuterId = Shader.PropertyToID("_OutlineWidthOuter");
        static readonly int s_OutlineWidthInnerId = Shader.PropertyToID("_OutlineWidthInner");
        static readonly int s_OutlineOffsetId = Shader.PropertyToID("_OutlineOffset");
        static readonly int s_OutlineIntensityId = Shader.PropertyToID("_OutlineIntensity");
        static readonly int s_CharacterScreenUVId = Shader.PropertyToID("_CharacterScreenUV");
        static readonly int s_CharacterMaskTexId = Shader.PropertyToID("_CharacterMaskTex");
        static readonly int s_CharacterMaskOuterTexId = Shader.PropertyToID("_CharacterMaskOuterTex");
        static readonly int s_CharacterMaskInnerTexId = Shader.PropertyToID("_CharacterMaskInnerTex");
        static readonly int s_CharacterMaskTexelSizeId = Shader.PropertyToID("_CharacterMaskTex_TexelSize");
        static readonly int s_NormalExtrusionId = Shader.PropertyToID("_NormalExtrusion");
        static readonly int s_UseSmoothNormalVCId = Shader.PropertyToID("_UseSmoothNormalVC");
        static readonly int s_UseExtrudedMaskId = Shader.PropertyToID("_UseExtrudedMask");
        static readonly int s_OutlineControlEnabledId = Shader.PropertyToID("_OutlineControlEnabled");
        static readonly int s_OutlineControlMapId = Shader.PropertyToID("_OutlineControlMap");
        static readonly int s_OutlineControlMapSTId = Shader.PropertyToID("_OutlineControlMap_ST");
        static readonly int s_OutlineControlThresholdId = Shader.PropertyToID("_OutlineControlThreshold");
        static readonly int s_OutlineControlInvertId = Shader.PropertyToID("_OutlineControlInvert");

        static readonly List<ShaderTagId> s_ShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        readonly System.Func<int, Material> m_GetOutlineMaterial;
        readonly System.Func<float, Material> m_GetMaskMaterial;
        readonly Settings m_Settings;

        public CharacterOutlinePass(
            System.Func<int, Material> getOutlineMaterial,
            System.Func<float, Material> getMaskMaterial,
            Settings settings)
        {
            m_GetOutlineMaterial = getOutlineMaterial;
            m_GetMaskMaterial = getMaskMaterial;
            m_Settings = settings;
            profilingSampler = new ProfilingSampler("Character Outline");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_GetOutlineMaterial == null || m_GetMaskMaterial == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid())
                return;

            List<OutlineLayer> layers = m_Settings.layers;
            if (layers == null || layers.Count == 0)
                return;

            var active = new List<OutlineLayer>(layers.Count);
            for (int i = 0; i < layers.Count; i++)
            {
                OutlineLayer layer = layers[i];
                if (layer.enabled)
                    active.Add(layer);
            }

            if (active.Count == 0)
                return;

            Camera camera = cameraData.camera;
            TryGetCharacterScreenUV(camera, out Vector2 characterScreenUV, out float anchorDepth);

            TextureDesc colorDesc = cameraColor.GetDescriptor(renderGraph);
            int width = Math.Max(1, colorDesc.width);
            int height = Math.Max(1, colorDesc.height);
            Vector4 maskTexelSize = new Vector4(1f / width, 1f / height, width, height);

            var maskDesc = new TextureDesc(width, height)
            {
                // R = coverage, G = mesh U, B = mesh V (per-layer ST applied in composite).
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                bindTextureMS = false,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            TextureHandle sceneDepth = resourceData.activeDepthTexture;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;

            float distanceScale = 1f;
            if (m_Settings.compensateDistance)
            {
                float reference = Mathf.Max(m_Settings.referenceDistance, 0.01f);
                float depth = Mathf.Max(anchorDepth, 0.01f);
                distanceScale = reference / depth;
            }

            bool extruded = m_Settings.useNormalExtrusionMask;
            TextureHandle bodyMask = TextureHandle.nullHandle;
            Dictionary<int, TextureHandle> extrusionMasks = null;

            if (extruded)
            {
                extrusionMasks = new Dictionary<int, TextureHandle>();
                for (int i = 0; i < active.Count; i++)
                {
                    OutlineLayer layer = active[i];
                    EnsureExtrusionMask(renderGraph, frameData, maskDesc, sceneDepth, extrusionMasks, layer.outerExtrusion);
                    EnsureExtrusionMask(renderGraph, frameData, maskDesc, sceneDepth, extrusionMasks, layer.innerExtrusion);
                }
            }
            else
            {
                maskDesc.name = "_CharacterOutlineMask";
                bodyMask = renderGraph.CreateTexture(maskDesc);
                RecordMaskPass(renderGraph, frameData, bodyMask, sceneDepth, 0f, "CharacterOutline Draw Body Mask");
            }

            TextureDesc tempDesc = cameraColor.GetDescriptor(renderGraph);
            tempDesc.depthBufferBits = DepthBits.None;
            tempDesc.msaaSamples = MSAASamples.None;
            tempDesc.bindTextureMS = false;
            tempDesc.clearBuffer = false;

            tempDesc.name = "_CharacterOutlineTempA";
            TextureHandle tempA = renderGraph.CreateTexture(tempDesc);
            tempDesc.name = "_CharacterOutlineTempB";
            TextureHandle tempB = renderGraph.CreateTexture(tempDesc);

            // Seed ping-pong with camera color.
            renderGraph.AddBlitPass(cameraColor, tempA, Vector2.one, Vector2.zero, passName: "CharacterOutline Seed");

            TextureHandle read = tempA;
            TextureHandle write = tempB;

            for (int i = 0; i < active.Count; i++)
            {
                OutlineLayer layer = active[i];
                Vector2 offsetPixels = layer.offset * distanceScale;

                TextureHandle outerTex;
                TextureHandle innerTex;
                float outerW = 0f;
                float innerW = 0f;

                if (extruded)
                {
                    outerTex = extrusionMasks[ExtrusionKey(layer.outerExtrusion)];
                    innerTex = extrusionMasks[ExtrusionKey(layer.innerExtrusion)];
                }
                else
                {
                    outerTex = TextureHandle.nullHandle;
                    innerTex = TextureHandle.nullHandle;
                    outerW = Mathf.Clamp(Mathf.Max(0f, layer.outerWidth) * distanceScale, 0f, 64f);
                    innerW = Mathf.Clamp(Mathf.Max(0f, layer.innerWidth) * distanceScale, 0f, 64f);
                    if (innerW > outerW)
                    {
                        float swap = innerW;
                        innerW = outerW;
                        outerW = swap;
                    }
                }

                Vector4 controlST = layer.controlMapST;
                if (controlST.x == 0f && controlST.y == 0f)
                    controlST = new Vector4(1f, 1f, 0f, 0f);

                RecordCompositePass(
                    renderGraph,
                    read,
                    write,
                    bodyMask,
                    outerTex,
                    innerTex,
                    cameraDepthTexture,
                    characterScreenUV,
                    maskTexelSize,
                    m_GetOutlineMaterial(i),
                    layer.color,
                    layer.intensity,
                    outerW,
                    innerW,
                    offsetPixels,
                    extruded,
                    m_Settings.outlineControlMap != null,
                    m_Settings.outlineControlMap != null ? m_Settings.outlineControlMap : Texture2D.whiteTexture,
                    controlST,
                    layer.controlThreshold,
                    m_Settings.outlineControlInvert,
                    $"CharacterOutline Composite Layer {i}");

                TextureHandle swapRT = read;
                read = write;
                write = swapRT;
            }

            renderGraph.AddBlitPass(read, cameraColor, Vector2.one, Vector2.zero, passName: "CharacterOutline Copy Back");
        }

        void EnsureExtrusionMask(
            RenderGraph renderGraph,
            ContextContainer frameData,
            TextureDesc maskDesc,
            TextureHandle sceneDepth,
            Dictionary<int, TextureHandle> map,
            float extrusion)
        {
            int key = ExtrusionKey(extrusion);
            if (map.ContainsKey(key))
                return;

            maskDesc.name = $"_CharacterOutlineMaskExt_{key}";
            TextureHandle rt = renderGraph.CreateTexture(maskDesc);
            RecordMaskPass(
                renderGraph, frameData, rt, sceneDepth, extrusion,
                $"CharacterOutline Draw Extrusion {extrusion:0.####}");
            map[key] = rt;
        }

        static int ExtrusionKey(float extrusion)
        {
            return Mathf.RoundToInt(extrusion * 10000f);
        }

        void RecordMaskPass(
            RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle characterMask,
            TextureHandle sceneDepth,
            float normalExtrusion,
            string passName)
        {
            float useSmoothVC = m_Settings.useSmoothNormalsFromVertexColor ? 1f : 0f;
            Material maskMaterial = m_GetMaskMaterial(normalExtrusion);
            maskMaterial.SetFloat(s_NormalExtrusionId, normalExtrusion);
            maskMaterial.SetFloat(s_UseSmoothNormalVCId, useSmoothVC);

            using (var builder = renderGraph.AddRasterRenderPass<DrawPassData>(passName, out var passData))
            {
                InitRendererList(frameData, ref passData, renderGraph, maskMaterial);

                if (!passData.rendererListHandle.IsValid())
                    return;

                passData.maskMaterial = maskMaterial;
                passData.normalExtrusion = normalExtrusion;
                passData.useSmoothNormalVC = useSmoothVC;

                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(characterMask, 0, AccessFlags.Write);
                if (sceneDepth.IsValid())
                    builder.SetRenderAttachmentDepth(sceneDepth, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (DrawPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(s_NormalExtrusionId, data.normalExtrusion);
                    context.cmd.SetGlobalFloat(s_UseSmoothNormalVCId, data.useSmoothNormalVC);
                    data.maskMaterial.SetFloat(s_NormalExtrusionId, data.normalExtrusion);
                    data.maskMaterial.SetFloat(s_UseSmoothNormalVCId, data.useSmoothNormalVC);
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
                    context.cmd.DrawRendererList(data.rendererListHandle);
                });
            }
        }

        void RecordCompositePass(
            RenderGraph renderGraph,
            TextureHandle sourceColor,
            TextureHandle destColor,
            TextureHandle bodyMask,
            TextureHandle outerMask,
            TextureHandle innerMask,
            TextureHandle cameraDepthTexture,
            Vector2 characterScreenUV,
            Vector4 maskTexelSize,
            Material outlineMaterial,
            Color outlineColor,
            float intensity,
            float outerPixels,
            float innerPixels,
            Vector2 offsetPixels,
            bool extruded,
            bool controlEnabled,
            Texture controlMap,
            Vector4 controlMapST,
            float controlThreshold,
            bool controlInvert,
            string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(passName, out var passData))
            {
                passData.material = outlineMaterial;
                passData.bodyMask = bodyMask;
                passData.outerMask = outerMask;
                passData.innerMask = innerMask;
                passData.sourceColor = sourceColor;
                passData.outlineColor = outlineColor;
                passData.outerWidth = outerPixels;
                passData.innerWidth = innerPixels;
                passData.outlineOffset = offsetPixels;
                passData.intensity = intensity;
                passData.characterScreenUV = characterScreenUV;
                passData.texelSize = maskTexelSize;
                passData.useExtrudedMask = extruded ? 1f : 0f;
                passData.outlineControlEnabled = controlEnabled ? 1f : 0f;
                passData.outlineControlMap = controlMap;
                passData.outlineControlMapST = controlMapST;
                passData.outlineControlThreshold = controlThreshold;
                passData.outlineControlInvert = controlInvert ? 1f : 0f;

                builder.UseTexture(sourceColor, AccessFlags.Read);
                if (!extruded && bodyMask.IsValid())
                    builder.UseTexture(bodyMask, AccessFlags.Read);
                if (extruded)
                {
                    if (outerMask.IsValid())
                        builder.UseTexture(outerMask, AccessFlags.Read);
                    if (innerMask.IsValid())
                        builder.UseTexture(innerMask, AccessFlags.Read);
                }

                if (cameraDepthTexture.IsValid())
                    builder.UseTexture(cameraDepthTexture, AccessFlags.Read);

                builder.SetRenderAttachment(destColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    // Use globals so shared outline material cannot leak layer params across passes.
                    context.cmd.SetGlobalColor(s_OutlineColorId, data.outlineColor);
                    context.cmd.SetGlobalFloat(s_OutlineWidthOuterId, data.outerWidth);
                    context.cmd.SetGlobalFloat(s_OutlineWidthInnerId, data.innerWidth);
                    context.cmd.SetGlobalVector(s_OutlineOffsetId, data.outlineOffset);
                    context.cmd.SetGlobalFloat(s_OutlineIntensityId, data.intensity);
                    context.cmd.SetGlobalVector(s_CharacterScreenUVId, data.characterScreenUV);
                    context.cmd.SetGlobalVector(s_CharacterMaskTexelSizeId, data.texelSize);
                    context.cmd.SetGlobalFloat(s_UseExtrudedMaskId, data.useExtrudedMask);
                    context.cmd.SetGlobalFloat(s_OutlineControlEnabledId, data.outlineControlEnabled);
                    context.cmd.SetGlobalFloat(s_OutlineControlThresholdId, data.outlineControlThreshold);
                    context.cmd.SetGlobalFloat(s_OutlineControlInvertId, data.outlineControlInvert);
                    context.cmd.SetGlobalVector(s_OutlineControlMapSTId, data.outlineControlMapST);
                    Shader.SetGlobalTexture(s_OutlineControlMapId, data.outlineControlMap);

                    data.material.SetColor(s_OutlineColorId, data.outlineColor);
                    data.material.SetFloat(s_OutlineWidthOuterId, data.outerWidth);
                    data.material.SetFloat(s_OutlineWidthInnerId, data.innerWidth);
                    data.material.SetVector(s_OutlineOffsetId, data.outlineOffset);
                    data.material.SetFloat(s_OutlineIntensityId, data.intensity);
                    data.material.SetVector(s_CharacterScreenUVId, data.characterScreenUV);
                    data.material.SetVector(s_CharacterMaskTexelSizeId, data.texelSize);
                    data.material.SetFloat(s_UseExtrudedMaskId, data.useExtrudedMask);
                    data.material.SetFloat(s_OutlineControlEnabledId, data.outlineControlEnabled);
                    data.material.SetFloat(s_OutlineControlThresholdId, data.outlineControlThreshold);
                    data.material.SetFloat(s_OutlineControlInvertId, data.outlineControlInvert);
                    data.material.SetVector(s_OutlineControlMapSTId, data.outlineControlMapST);
                    data.material.SetTexture(s_OutlineControlMapId, data.outlineControlMap);

                    if (data.useExtrudedMask > 0.5f)
                    {
                        context.cmd.SetGlobalTexture(s_CharacterMaskOuterTexId, data.outerMask);
                        context.cmd.SetGlobalTexture(s_CharacterMaskInnerTexId, data.innerMask);
                    }
                    else
                    {
                        context.cmd.SetGlobalTexture(s_CharacterMaskTexId, data.bodyMask);
                    }

                    Blitter.BlitTexture(
                        context.cmd,
                        data.sourceColor,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        kCompositePass);
                });
            }
        }

        bool TryGetCharacterScreenUV(Camera camera, out Vector2 screenUV, out float viewDepth)
        {
            screenUV = new Vector2(0.5f, 0.5f);
            viewDepth = Mathf.Max(m_Settings.referenceDistance, 0.01f);

            if (camera == null || !TryGetAnchorPosition(out Vector3 worldPos))
                return false;

            Vector3 viewport = camera.WorldToViewportPoint(worldPos);
            if (viewport.z <= 0f)
                return false;

            screenUV = new Vector2(viewport.x, viewport.y);
            viewDepth = viewport.z;
            return true;
        }

        bool TryGetAnchorPosition(out Vector3 position)
        {
            if (CharacterOutlineAnchor.Current != null)
            {
                position = CharacterOutlineAnchor.Current.AnchorPosition;
                return true;
            }

            int mask = m_Settings.layerMask.value;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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

        void InitRendererList(
            ContextContainer frameData,
            ref DrawPassData passData,
            RenderGraph renderGraph,
            Material maskMaterial)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var filterSettings = new FilteringSettings(RenderQueueRange.opaque, m_Settings.layerMask);
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                s_ShaderTagIds, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            drawSettings.overrideMaterial = maskMaterial;
            drawSettings.overrideMaterialPassIndex = 0;

            var param = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }

        class DrawPassData
        {
            public RendererListHandle rendererListHandle;
            public Material maskMaterial;
            public float normalExtrusion;
            public float useSmoothNormalVC;
        }

        class CompositePassData
        {
            public Material material;
            public TextureHandle bodyMask;
            public TextureHandle outerMask;
            public TextureHandle innerMask;
            public TextureHandle sourceColor;
            public Color outlineColor;
            public float outerWidth;
            public float innerWidth;
            public Vector2 outlineOffset;
            public float intensity;
            public Vector2 characterScreenUV;
            public Vector4 texelSize;
            public float useExtrudedMask;
            public float outlineControlEnabled;
            public Texture outlineControlMap;
            public Vector4 outlineControlMapST;
            public float outlineControlThreshold;
            public float outlineControlInvert;
        }
    }
}
