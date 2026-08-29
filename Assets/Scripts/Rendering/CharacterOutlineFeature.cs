using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Post-process character outline.
/// Screen-expand mode: body mask → Expand(outer) - Expand(inner).
/// Normal-extrusion mode: draw extruded outer + inner masks → outer - inner (no screen expand).
/// </summary>
public class CharacterOutlineFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        [Tooltip("Objects on these layers are drawn into the mask.")]
        public LayerMask layerMask = 1 << 6; // Character

        [Header("Mask")]
        [Tooltip("On: ring = normal-extruded outer mask - inner mask (no screen-space expand).")]
        public bool useNormalExtrusionMask = false;

        [Tooltip("Object-space extrusion for the outer mask when Use Normal Extrusion Mask is on. Negative = inward.")]
        public float normalExtrusionOuter = 0.02f;

        [Tooltip("Object-space extrusion for the inner mask (0 = tight body). Negative = inward. Ring = outer - inner.")]
        public float normalExtrusionInner = 0f;

        [Tooltip("Extrude using smooth normals baked into vertex color (tangent space). Required for correct skinned outlines.")]
        public bool useSmoothNormalsFromVertexColor = false;

        [Header("Outline")]
        public Color outlineColor = Color.white;

        [Tooltip("Screen-expand mode only: outer width in pixels at the reference distance.")]
        [Range(0f, 32f)] public float outerWidth = 4f;

        [Tooltip("Screen-expand mode only: inner width in pixels. Ring = outer - inner.")]
        [Range(0f, 32f)] public float innerWidth = 0f;

        [Tooltip("Screen-space ring offset in pixels (XY).")]
        public Vector2 outlineOffset = Vector2.zero;

        [Range(0f, 8f)] public float intensity = 1f;

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
    }

    public Settings settings = new Settings();

    Material m_OutlineMaterial;
    Material m_MaskMaterialOuter;
    Material m_MaskMaterialInner;
    CharacterOutlinePass m_Pass;

    public override void Create()
    {
        if (settings.outlineShader == null)
            settings.outlineShader = Shader.Find("Hidden/Custom/CharacterOutline");
        if (settings.maskShader == null)
            settings.maskShader = Shader.Find("Hidden/Custom/CharacterOutlineMask");

        if (settings.outlineShader == null || settings.maskShader == null)
            return;

        if (m_OutlineMaterial == null || m_OutlineMaterial.shader != settings.outlineShader)
        {
            CoreUtils.Destroy(m_OutlineMaterial);
            m_OutlineMaterial = CoreUtils.CreateEngineMaterial(settings.outlineShader);
        }

        if (m_MaskMaterialOuter == null || m_MaskMaterialOuter.shader != settings.maskShader)
        {
            CoreUtils.Destroy(m_MaskMaterialOuter);
            m_MaskMaterialOuter = CoreUtils.CreateEngineMaterial(settings.maskShader);
        }

        if (m_MaskMaterialInner == null || m_MaskMaterialInner.shader != settings.maskShader)
        {
            CoreUtils.Destroy(m_MaskMaterialInner);
            m_MaskMaterialInner = CoreUtils.CreateEngineMaterial(settings.maskShader);
        }

        m_Pass = new CharacterOutlinePass(m_OutlineMaterial, m_MaskMaterialOuter, m_MaskMaterialInner, settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Pass == null || m_OutlineMaterial == null || m_MaskMaterialOuter == null || m_MaskMaterialInner == null)
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
        CoreUtils.Destroy(m_MaskMaterialOuter);
        CoreUtils.Destroy(m_MaskMaterialInner);
        m_OutlineMaterial = null;
        m_MaskMaterialOuter = null;
        m_MaskMaterialInner = null;
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

        static readonly List<ShaderTagId> s_ShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        readonly Material m_OutlineMaterial;
        readonly Material m_MaskMaterialOuter;
        readonly Material m_MaskMaterialInner;
        readonly Settings m_Settings;

        public CharacterOutlinePass(
            Material outlineMaterial,
            Material maskMaterialOuter,
            Material maskMaterialInner,
            Settings settings)
        {
            m_OutlineMaterial = outlineMaterial;
            m_MaskMaterialOuter = maskMaterialOuter;
            m_MaskMaterialInner = maskMaterialInner;
            m_Settings = settings;
            profilingSampler = new ProfilingSampler("Character Outline");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_OutlineMaterial == null || m_MaskMaterialOuter == null || m_MaskMaterialInner == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid())
                return;

            Camera camera = cameraData.camera;
            TryGetCharacterScreenUV(camera, out Vector2 characterScreenUV, out float anchorDepth);

            TextureDesc colorDesc = cameraColor.GetDescriptor(renderGraph);
            int width = Math.Max(1, colorDesc.width);
            int height = Math.Max(1, colorDesc.height);

            var maskDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R8_UNorm,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                bindTextureMS = false,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            TextureHandle sceneDepth = resourceData.activeDepthTexture;

            float distanceScale = 1f;
            if (m_Settings.compensateDistance)
            {
                float reference = Mathf.Max(m_Settings.referenceDistance, 0.01f);
                float depth = Mathf.Max(anchorDepth, 0.01f);
                distanceScale = reference / depth;
            }

            Vector2 offsetPixels = m_Settings.outlineOffset * distanceScale;

            TextureHandle bodyOrSingleMask;
            TextureHandle outerMask = TextureHandle.nullHandle;
            TextureHandle innerMask = TextureHandle.nullHandle;
            bool extruded = m_Settings.useNormalExtrusionMask;

            if (extruded)
            {
                float outerExt = m_Settings.normalExtrusionOuter;
                float innerExt = m_Settings.normalExtrusionInner;

                maskDesc.name = "_CharacterOutlineMaskOuter";
                outerMask = renderGraph.CreateTexture(maskDesc);
                maskDesc.name = "_CharacterOutlineMaskInner";
                innerMask = renderGraph.CreateTexture(maskDesc);

                RecordMaskPass(
                    renderGraph, frameData, outerMask, sceneDepth,
                    m_MaskMaterialOuter, outerExt, "CharacterOutline Draw Outer Extruded Mask");
                RecordMaskPass(
                    renderGraph, frameData, innerMask, sceneDepth,
                    m_MaskMaterialInner, innerExt, "CharacterOutline Draw Inner Mask");
                bodyOrSingleMask = outerMask;
            }
            else
            {
                maskDesc.name = "_CharacterOutlineMask";
                bodyOrSingleMask = renderGraph.CreateTexture(maskDesc);
                RecordMaskPass(
                    renderGraph, frameData, bodyOrSingleMask, sceneDepth,
                    m_MaskMaterialInner, 0f, "CharacterOutline Draw Body Mask");
            }

            float outerPixels = Mathf.Clamp(Mathf.Max(0f, m_Settings.outerWidth) * distanceScale, 0f, 64f);
            float innerPixels = Mathf.Clamp(Mathf.Max(0f, m_Settings.innerWidth) * distanceScale, 0f, 64f);
            if (innerPixels > outerPixels)
            {
                float swap = innerPixels;
                innerPixels = outerPixels;
                outerPixels = swap;
            }

            m_OutlineMaterial.SetColor(s_OutlineColorId, m_Settings.outlineColor);
            m_OutlineMaterial.SetFloat(s_OutlineWidthOuterId, outerPixels);
            m_OutlineMaterial.SetFloat(s_OutlineWidthInnerId, innerPixels);
            m_OutlineMaterial.SetVector(s_OutlineOffsetId, offsetPixels);
            m_OutlineMaterial.SetFloat(s_OutlineIntensityId, m_Settings.intensity);
            m_OutlineMaterial.SetVector(s_CharacterScreenUVId, characterScreenUV);
            m_OutlineMaterial.SetFloat(s_UseExtrudedMaskId, extruded ? 1f : 0f);
            m_OutlineMaterial.SetVector(
                s_CharacterMaskTexelSizeId,
                new Vector4(1f / width, 1f / height, width, height));

            TextureDesc tempDesc = cameraColor.GetDescriptor(renderGraph);
            tempDesc.name = "_CharacterOutlineTemp";
            tempDesc.depthBufferBits = DepthBits.None;
            tempDesc.msaaSamples = MSAASamples.None;
            tempDesc.bindTextureMS = false;
            tempDesc.clearBuffer = false;
            TextureHandle tempColor = renderGraph.CreateTexture(tempDesc);

            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;
            RecordCompositePass(
                renderGraph,
                cameraColor,
                tempColor,
                bodyOrSingleMask,
                outerMask,
                innerMask,
                cameraDepthTexture,
                characterScreenUV,
                outerPixels,
                innerPixels,
                offsetPixels,
                extruded);

            renderGraph.AddBlitPass(tempColor, cameraColor, Vector2.one, Vector2.zero, passName: "CharacterOutline Copy Back");
        }

        void RecordMaskPass(
            RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle characterMask,
            TextureHandle sceneDepth,
            Material maskMaterial,
            float normalExtrusion,
            string passName)
        {
            // Must set before CreateRendererList: overrideMaterial CB is captured with the list.
            float useSmoothVC = m_Settings.useSmoothNormalsFromVertexColor ? 1f : 0f;
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
                    // Global + material: RendererList override sometimes ignores late Material.SetFloat.
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
            float outerPixels,
            float innerPixels,
            Vector2 offsetPixels,
            bool extruded)
        {
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                       "CharacterOutline Composite", out var passData))
            {
                passData.material = m_OutlineMaterial;
                passData.bodyMask = bodyMask;
                passData.outerMask = outerMask;
                passData.innerMask = innerMask;
                passData.sourceColor = sourceColor;
                passData.outlineColor = m_Settings.outlineColor;
                passData.outerWidth = outerPixels;
                passData.innerWidth = innerPixels;
                passData.outlineOffset = offsetPixels;
                passData.intensity = m_Settings.intensity;
                passData.characterScreenUV = characterScreenUV;
                passData.texelSize = m_OutlineMaterial.GetVector(s_CharacterMaskTexelSizeId);
                passData.useExtrudedMask = extruded ? 1f : 0f;

                builder.UseTexture(bodyMask, AccessFlags.Read);
                if (extruded)
                {
                    if (outerMask.IsValid())
                        builder.UseTexture(outerMask, AccessFlags.Read);
                    if (innerMask.IsValid())
                        builder.UseTexture(innerMask, AccessFlags.Read);
                }

                builder.UseTexture(sourceColor, AccessFlags.Read);
                if (cameraDepthTexture.IsValid())
                    builder.UseTexture(cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    data.material.SetColor(s_OutlineColorId, data.outlineColor);
                    data.material.SetFloat(s_OutlineWidthOuterId, data.outerWidth);
                    data.material.SetFloat(s_OutlineWidthInnerId, data.innerWidth);
                    data.material.SetVector(s_OutlineOffsetId, data.outlineOffset);
                    data.material.SetFloat(s_OutlineIntensityId, data.intensity);
                    data.material.SetVector(s_CharacterScreenUVId, data.characterScreenUV);
                    data.material.SetVector(s_CharacterMaskTexelSizeId, data.texelSize);
                    data.material.SetFloat(s_UseExtrudedMaskId, data.useExtrudedMask);
                    context.cmd.SetGlobalTexture(s_CharacterMaskTexId, data.bodyMask);
                    if (data.useExtrudedMask > 0.5f)
                    {
                        context.cmd.SetGlobalTexture(s_CharacterMaskOuterTexId, data.outerMask);
                        context.cmd.SetGlobalTexture(s_CharacterMaskInnerTexId, data.innerMask);
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
        }
    }
}
