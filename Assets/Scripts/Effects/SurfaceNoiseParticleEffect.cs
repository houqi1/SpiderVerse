using System;
using System.Collections.Generic;
using SpiderVerse.LineArt;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(SurfaceNoiseParticleEffect))]
public sealed class SurfaceNoiseParticleEffectInspector : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var effect = (SurfaceNoiseParticleEffect)target;
        UnityEditor.EditorGUILayout.HelpBox(
            "有效模型：" + effect.SurfaceCount + "    GPU 颗粒（全部启用层）：" + effect.SubmittedParticleCount,
            effect.SubmittedParticleCount > 0 ? UnityEditor.MessageType.Info : UnityEditor.MessageType.Warning);
        if (GUILayout.Button("重建编辑器预览"))
        {
            effect.RebuildEffect();
            UnityEditor.SceneView.RepaintAll();
        }
    }

    public override bool RequiresConstantRepaint() => true;
}
#endif

/// <summary>
/// Selects stable UV-noise anchors on the CPU when the distribution changes.
/// Bakes skinning on the CPU, then updates anchors and draws billboards on the GPU.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class SurfaceNoiseParticleEffect : MonoBehaviour
{
    internal static readonly List<SurfaceNoiseParticleEffect> ActiveEffects = new List<SurfaceNoiseParticleEffect>();

    [Serializable]
    public sealed class LayerSettings
    {
        public bool enabled = true;
        public Texture2D distributionNoise;
        public Texture2D regionMask;
        public Vector2 noiseTiling = Vector2.one;
        public Vector2 noiseOffset;
        [Range(0f, 1f)] public float threshold = 0.56f;
        [Range(0f, 0.5f)] public float softness = 0.16f;
        public Material particleMaterial;
        public Color particleColor = new Color(0.82f, 0.93f, 1f, 1f);
        public Vector2 particleSize = new Vector2(0.012f, 0.028f);
        [Range(-0.1f, 0.1f)] public float surfaceOffset = 0.006f;
        [Range(-0.1f, 0.1f)] public float surfaceOffsetJitter = 0.004f;

        [Header("Camera view influence")]
        [Tooltip("Let the camera view fade back-facing particles and enlarge particles near the silhouette.")]
        public bool cameraViewEffectEnabled;
        [Range(0f, 1f), Tooltip("How strongly back-facing particles fade out.")]
        public float cameraFacingStrength = 1f;
        [Range(0.01f, 0.5f), Tooltip("Softness around the surface tangent where facing visibility changes.")]
        public float cameraFacingSoftness = 0.15f;
        [Range(0f, 1f), Tooltip("Maximum particle size increase at grazing angles.")]
        public float cameraSilhouetteSizeBoost = 0.35f;
    }

    [Header("Surface and distribution")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField, Min(1), Tooltip("Number of stable surface samples. Increase this for denser grains inside selected noise regions.")]
    private int candidateCount = 9000;
    [SerializeField, Tooltip("Fixed seed for repeatable sample positions and per-particle variation.")]
    private int seed = 173;
    [SerializeField, HideInInspector] private Texture2D distributionNoise;
    [SerializeField, HideInInspector] private Texture2D regionMask;
    [SerializeField, HideInInspector] private Vector2 noiseTiling = Vector2.one;
    [SerializeField, HideInInspector] private Vector2 noiseOffset;
    [SerializeField, HideInInspector] private float threshold = 0.56f;
    [SerializeField, HideInInspector] private float softness = 0.16f;
    [SerializeField, HideInInspector] private Material particleMaterial;
    [SerializeField, HideInInspector] private Color particleColor = new Color(0.82f, 0.93f, 1f, 1f);
    [SerializeField, HideInInspector] private Vector2 particleSize = new Vector2(0.012f, 0.028f);
    [SerializeField, HideInInspector] private float surfaceOffset = 0.006f;
    [SerializeField, HideInInspector] private float surfaceOffsetJitter = 0.004f;
    [SerializeField] private LayerSettings[] layers = Array.Empty<LayerSettings>();
    [SerializeField, HideInInspector] private int layerSettingsVersion;

    [Header("Screen-space particle color (Jump Flood)")]
    [Tooltip("Extend this character's visible colors across each particle pixel. Disable to compare with anchor sampling.")]
    public bool useJumpFloodColor = true;
    [Range(8, 256), Tooltip("Maximum color extension in camera render pixels, including the safe inner edge.")]
    public int colorExtensionPixels = 128;
    [Tooltip("Half-size coordinate field; the source color and visibility mask remain full resolution.")]
    public bool halfResolutionColorField = true;

    internal Renderer[] ColorSourceRenderers => targetRenderers;
    internal bool NeedsColorField => useJumpFloodColor && isActiveAndEnabled && SubmittedParticleCount > 0 &&
        Array.Exists(layers, layer => layer != null && layer.enabled && layer.particleMaterial != null &&
            layer.particleMaterial.HasProperty("_UseSampledSurfaceColor") &&
            layer.particleMaterial.GetFloat("_UseSampledSurfaceColor") > 0.5f);

    [Header("GPU anchor update")]
    [SerializeField] private ComputeShader anchorCompute;
    [SerializeField, Tooltip("Legacy reference retained for migration. This system is stopped; GPU billboards render the effect.")]
    private new ParticleSystem particleSystem;
    private SurfaceNoiseGpu gpu;

    private readonly List<SurfaceSource> sources = new List<SurfaceSource>();
    private Candidate[] candidates = Array.Empty<Candidate>();
    private Texture2D generatedNoise;
    private readonly List<LayerTextureData> layerTextureData = new List<LayerTextureData>();
    private int builtSeed;
    private int builtCandidateCount;
    private bool initialized;
    private bool rebuildRequested;
    public int SubmittedParticleCount { get; private set; }
    public int SurfaceCount => sources.Count;
#if UNITY_EDITOR
    private double nextEditorUpdate;
#endif
    private bool warnedUnreadableTexture;

    private sealed class LayerTextureData
    {
        public Texture2D EffectiveNoise;
        public Color32[] NoisePixels;
        public Color32[] MaskPixels;
        public int NoiseWidth, NoiseHeight, MaskWidth, MaskHeight;
        public int NoiseId, MaskId;
        public Vector2 Tiling, Offset;
        public float Threshold, Softness;
    }

    private sealed class SurfaceSource
    {
        public Renderer Renderer;
        public SkinnedMeshRenderer SkinnedRenderer;
        public Mesh Mesh;
        public Mesh BakedMesh;
        public Vector3[] RestVertices;
        public Vector2[] UVs;
        public int[] Triangles;
        public double[] TriangleAreaCdf;
        public double TotalArea;
        public readonly List<Vector3> CurrentVertices = new List<Vector3>();
        public readonly List<Vector3> CurrentNormals = new List<Vector3>();
        public bool GeneratedRestNormals;
        public SurfaceNoiseGpu.Batch[] Batches;
    }

    private struct Candidate
    {
        public int sourceIndex;
        public int i0;
        public int i1;
        public int i2;
        public Vector3 barycentric;
        public Vector2 uv;
        public float keepRandom;
        public float sizeRandom;
        public float rotation;
        public float offsetRandom;
        public float brightness;
    }

    private void OnEnable()
    {
        if (!ActiveEffects.Contains(this))
            ActiveEffects.Add(this);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
        if (!Application.IsPlaying(gameObject))
        {
            rebuildRequested = true;
            return;
        }
#endif
        if (!initialized)
            RebuildEffect();
    }

    internal void DrawFromRenderPass(CommandBuffer commandBuffer, Camera camera,
        ObjectLineArtFeature.CameraSample cameraSample)
    {
        if (!isActiveAndEnabled || !initialized || gpu == null || commandBuffer == null || camera == null ||
            (camera.cullingMask & (1 << gameObject.layer)) == 0)
            return;

        gpu.Draw(commandBuffer, camera, cameraSample, layers);
    }

    private void OnValidate()
    {
        MigrateLayers();
        // Defer mesh baking and object creation until the main-thread update.
        rebuildRequested = true;
    }

    private void MigrateLayers()
    {
        if (layerSettingsVersion >= 1) return;
        if (layers == null || layers.Length == 0)
        {
            layers = new[]
            {
                new LayerSettings
                {
                    distributionNoise = distributionNoise,
                    regionMask = regionMask,
                    noiseTiling = noiseTiling,
                    noiseOffset = noiseOffset,
                    threshold = threshold,
                    softness = softness,
                    particleMaterial = particleMaterial,
                    particleColor = particleColor,
                    particleSize = particleSize,
                    surfaceOffset = surfaceOffset,
                    surfaceOffsetJitter = surfaceOffsetJitter
                }
            };
        }
        layerSettingsVersion = 1;
#if UNITY_EDITOR
        if (gameObject.scene.IsValid())
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (this == null || !isActiveAndEnabled || Application.isPlaying ||
            UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating ||
            UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
            UnityEditor.EditorUtility.IsPersistent(this))
            return;
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now < nextEditorUpdate)
            return;
        nextEditorUpdate = now + 1.0 / 20.0;
        UpdateEffect();
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    private void LateUpdate()
    {
        if (Application.IsPlaying(gameObject))
            UpdateEffect();
    }

    private void UpdateEffect()
    {
        if (rebuildRequested || !initialized)
            RebuildEffect();
        if (!initialized || gpu == null)
            return;

        EnsureNoiseTexture();
        bool rebuiltCandidates = seed != builtSeed || candidateCount != builtCandidateCount;
        if (rebuiltCandidates)
        {
            BuildCandidates();
            PrepareTextureData();
            RecalculateSelection();
        }

        if (!rebuiltCandidates && DistributionSettingsChanged())
        {
            PrepareTextureData();
            RecalculateSelection();
        }

        UpdateParticles();
    }

    [ContextMenu("Rebuild Particle Distribution")]
    public void RebuildEffect()
    {
        MigrateLayers();
        rebuildRequested = false;
        EnsureRenderers();
        EnsureNoiseTexture();
        EnsureGpu();
        BuildSurfaceSources();
        BuildCandidates();
        PrepareTextureData();
        RecalculateSelection();
        initialized = true;

        if (gpu != null && isActiveAndEnabled)
            UpdateParticles();
    }

    private void EnsureRenderers()
    {
        if (targetRenderers != null && targetRenderers.Length > 0)
            return;

        targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void EnsureNoiseTexture()
    {
        if (generatedNoise == null)
            generatedNoise = CreateDefaultNoiseTexture();
        if (layers == null) layers = Array.Empty<LayerSettings>();
        while (layerTextureData.Count < layers.Length)
            layerTextureData.Add(new LayerTextureData());
        while (layerTextureData.Count > layers.Length)
            layerTextureData.RemoveAt(layerTextureData.Count - 1);
    }

    private void EnsureGpu()
    {
        gpu?.Dispose();
        gpu = null;
        if (particleSystem != null)
            particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (anchorCompute == null)
            anchorCompute = Resources.Load<ComputeShader>("SurfaceNoiseAnchors");
#if UNITY_EDITOR
        Material defaultMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Shader/CharacterNoiseBillboard.mat");
#else
        Material defaultMaterial = null;
#endif
        foreach (LayerSettings layer in layers)
            if (layer != null && layer.particleMaterial == null)
                layer.particleMaterial = defaultMaterial;
        if (!SystemInfo.supportsComputeShaders || anchorCompute == null ||
            layers.Length == 0 || Array.Exists(layers, layer => layer != null && layer.enabled && layer.particleMaterial == null))
        {
            Debug.LogError("Surface Noise: GPU rendering needs compute support, a ComputeShader, and a material for each enabled layer.", this);
            return;
        }
        Material seedMaterial = Array.Find(layers, layer => layer != null && layer.enabled)?.particleMaterial ?? defaultMaterial;
        if (seedMaterial != null)
            gpu = new SurfaceNoiseGpu(anchorCompute, seedMaterial);
    }

    private void BuildSurfaceSources()
    {
        ReleaseBakedMeshes();
        sources.Clear();

        if (targetRenderers == null)
            return;

        for (int r = 0; r < targetRenderers.Length; r++)
        {
            Renderer target = targetRenderers[r];
            if (target == null || target is ParticleSystemRenderer)
                continue;

            Mesh mesh = null;
            var skinned = target as SkinnedMeshRenderer;
            if (skinned != null)
            {
                mesh = skinned.sharedMesh;
            }
            else
            {
                var filter = target.GetComponent<MeshFilter>();
                if (filter != null)
                    mesh = filter.sharedMesh;
            }

            if (mesh == null)
                continue;

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            if (vertices.Length == 0 || uvs.Length != vertices.Length || triangles.Length < 3)
            {
                Debug.LogWarning("Surface Noise Particle Effect: skipped mesh without usable UVs: " +
                    target.name, target);
                continue;
            }

            var source = new SurfaceSource
            {
                Renderer = target,
                SkinnedRenderer = skinned,
                Mesh = mesh,
                RestVertices = vertices,
                UVs = uvs,
                Triangles = triangles,
                TriangleAreaCdf = new double[triangles.Length / 3]
            };

            if (skinned != null)
            {
                source.BakedMesh = new Mesh
                {
                    name = target.name + " Surface Noise Bake",
                    hideFlags = HideFlags.HideAndDontSave
                };
                // Compensate the renderer scale before applying localToWorld below.
                // Imported FBX hierarchies can contain 100x/10000x scale factors.
                skinned.BakeMesh(source.BakedMesh, true);
                source.BakedMesh.GetVertices(source.CurrentVertices);
                source.BakedMesh.GetNormals(source.CurrentNormals);
            }
            else
            {
                mesh.GetVertices(source.CurrentVertices);
                mesh.GetNormals(source.CurrentNormals);
            }

            if (source.CurrentNormals.Count != vertices.Length)
            {
                source.CurrentNormals.Clear();
                source.CurrentNormals.AddRange(BuildVertexNormals(vertices, triangles));
                source.GeneratedRestNormals = true;
            }

            Matrix4x4 matrix = target.transform.localToWorldMatrix;
            double accumulatedArea = 0.0;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                // Measure the initial posed surface: raw bind vertices transformed by
                // the renderer alone do not include the bones' bind-pose compensation.
                Vector3 a = matrix.MultiplyPoint3x4(source.CurrentVertices[triangles[t]]);
                Vector3 b = matrix.MultiplyPoint3x4(source.CurrentVertices[triangles[t + 1]]);
                Vector3 c = matrix.MultiplyPoint3x4(source.CurrentVertices[triangles[t + 2]]);
                accumulatedArea += Vector3.Cross(b - a, c - a).magnitude * 0.5;
                source.TriangleAreaCdf[t / 3] = accumulatedArea;
            }

            source.TotalArea = accumulatedArea;
            if (source.TotalArea <= 1e-10)
            {
                if (source.BakedMesh != null)
                    DestroyOwnedObject(source.BakedMesh);
                continue;
            }

            sources.Add(source);
        }
    }

    private static Vector3[] BuildVertexNormals(Vector3[] vertices, int[] triangles)
    {
        var normals = new Vector3[vertices.Length];
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            Vector3 face = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
            normals[i0] += face;
            normals[i1] += face;
            normals[i2] += face;
        }

        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].sqrMagnitude > 1e-10f ? normals[i].normalized : Vector3.up;
        return normals;
    }

    private void BuildCandidates()
    {
        builtSeed = seed;
        builtCandidateCount = Mathf.Max(1, candidateCount);
        candidateCount = builtCandidateCount;
        candidates = new Candidate[builtCandidateCount];
        double totalArea = 0.0;
        for (int i = 0; i < sources.Count; i++)
            totalArea += sources[i].TotalArea;

        if (totalArea <= 0.0)
        {
            Debug.LogWarning("Surface Noise Particle Effect: no valid mesh surfaces were found.", this);
            return;
        }

        var random = new System.Random(seed);
        var sourceCdf = new double[sources.Count];
        double cumulativeSourceArea = 0.0;
        for (int i = 0; i < sources.Count; i++)
        {
            cumulativeSourceArea += sources[i].TotalArea;
            sourceCdf[i] = cumulativeSourceArea;
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            double sourceSample = random.NextDouble() * totalArea;
            int sourceIndex = FindCdfIndex(sourceCdf, sourceSample);
            SurfaceSource source = sources[sourceIndex];
            int triangleIndex = FindCdfIndex(source.TriangleAreaCdf,
                random.NextDouble() * source.TotalArea);
            int triangleOffset = triangleIndex * 3;

            double root = Math.Sqrt(random.NextDouble());
            float r1 = (float)random.NextDouble();
            float b0 = 1f - (float)root;
            float b1 = (float)root * (1f - r1);
            float b2 = (float)root * r1;

            int i0 = source.Triangles[triangleOffset];
            int i1 = source.Triangles[triangleOffset + 1];
            int i2 = source.Triangles[triangleOffset + 2];
            Vector2 uv = source.UVs[i0] * b0 + source.UVs[i1] * b1 + source.UVs[i2] * b2;

            candidates[i] = new Candidate
            {
                sourceIndex = sourceIndex,
                i0 = i0,
                i1 = i1,
                i2 = i2,
                barycentric = new Vector3(b0, b1, b2),
                uv = uv,
                keepRandom = (float)random.NextDouble(),
                sizeRandom = (float)random.NextDouble(),
                rotation = (float)random.NextDouble() * 360f,
                offsetRandom = (float)random.NextDouble(),
                brightness = Mathf.Lerp(0.78f, 1f, (float)random.NextDouble())
            };
        }
    }

    private static int FindCdfIndex(double[] cdf, double value)
    {
        int low = 0;
        int high = cdf.Length - 1;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (value < cdf[mid])
                high = mid;
            else
                low = mid + 1;
        }
        return low;
    }

    private void PrepareTextureData()
    {
        EnsureNoiseTexture();
        for (int i = 0; i < layers.Length; i++)
        {
            LayerSettings layer = layers[i];
            LayerTextureData data = layerTextureData[i];
            if (layer == null) continue;
            data.EffectiveNoise = layer.distributionNoise != null && layer.distributionNoise.isReadable
                ? layer.distributionNoise : generatedNoise;
            data.NoisePixels = data.EffectiveNoise.GetPixels32();
            data.NoiseWidth = data.EffectiveNoise.width;
            data.NoiseHeight = data.EffectiveNoise.height;
            data.MaskPixels = null;
            data.MaskWidth = data.MaskHeight = 0;
            if (layer.regionMask != null && layer.regionMask.isReadable)
            {
                data.MaskPixels = layer.regionMask.GetPixels32();
                data.MaskWidth = layer.regionMask.width;
                data.MaskHeight = layer.regionMask.height;
            }
            else if (layer.regionMask != null && !warnedUnreadableTexture)
            {
                Debug.LogWarning("Surface Noise Particle Effect: a layer's region mask must have Read/Write enabled; it will be ignored.", this);
                warnedUnreadableTexture = true;
            }
            data.NoiseId = layer.distributionNoise != null ? layer.distributionNoise.GetInstanceID() : 0;
            data.MaskId = layer.regionMask != null ? layer.regionMask.GetInstanceID() : 0;
            data.Tiling = layer.noiseTiling;
            data.Offset = layer.noiseOffset;
            data.Threshold = layer.threshold;
            data.Softness = layer.softness;
        }
    }

    private bool DistributionSettingsChanged()
    {
        if (layerTextureData.Count != layers.Length) return true;
        for (int i = 0; i < layers.Length; i++)
        {
            LayerSettings layer = layers[i];
            LayerTextureData data = layerTextureData[i];
            if (layer == null) return true;
            int noiseId = layer.distributionNoise != null ? layer.distributionNoise.GetInstanceID() : 0;
            int maskId = layer.regionMask != null ? layer.regionMask.GetInstanceID() : 0;
            if (noiseId != data.NoiseId || maskId != data.MaskId || layer.noiseTiling != data.Tiling ||
                layer.noiseOffset != data.Offset || !Mathf.Approximately(layer.threshold, data.Threshold) ||
                !Mathf.Approximately(layer.softness, data.Softness)) return true;
        }
        return false;
    }

    private void RecalculateSelection()
    {
        if (gpu == null) return;
        gpu.ClearBatches();
        for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            LayerSettings layer = layers[layerIndex];
            LayerTextureData data = layerTextureData[layerIndex];
            var anchors = new List<SurfaceNoiseGpu.Anchor>[sources.Count];
            for (int i = 0; i < anchors.Length; i++) anchors[i] = new List<SurfaceNoiseGpu.Anchor>();
            if (layer != null && layer.enabled)
            {
                float lower = Mathf.Clamp01(layer.threshold - layer.softness);
                float upper = Mathf.Clamp01(layer.threshold + layer.softness);
                bool hardThreshold = layer.softness <= 1e-5f;
                foreach (Candidate c in candidates)
                {
                    float noise = SampleTexture(data.NoisePixels, data.NoiseWidth, data.NoiseHeight,
                        Vector2.Scale(c.uv, layer.noiseTiling) + layer.noiseOffset);
                    float probability = hardThreshold
                        ? (noise >= layer.threshold ? 1f : 0f)
                        : SmoothStep(lower, upper, noise);
                    if (data.MaskPixels != null)
                        probability *= SampleTexture(data.MaskPixels, data.MaskWidth, data.MaskHeight, c.uv);
                    if (c.keepRandom >= probability || c.sourceIndex < 0 || c.sourceIndex >= sources.Count)
                        continue;
                    anchors[c.sourceIndex].Add(new SurfaceNoiseGpu.Anchor
                    {
                        i0 = (uint)c.i0, i1 = (uint)c.i1, i2 = (uint)c.i2,
                        barycentric = c.barycentric, brightness = c.brightness,
                        sizeRandom = c.sizeRandom, offsetRandom = c.offsetRandom
                    });
                }
            }
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                SurfaceSource source = sources[sourceIndex];
                if (source.Batches == null || source.Batches.Length != layers.Length)
                    source.Batches = new SurfaceNoiseGpu.Batch[layers.Length];
                Material material = layer != null ? layer.particleMaterial : null;
                source.Batches[layerIndex] = gpu.AddBatch(material, anchors[sourceIndex], source.CurrentVertices.Count, layerIndex);
            }
        }
        PrepareTextureData();
    }

    private static float SmoothStep(float a, float b, float value)
    {
        if (Mathf.Abs(b - a) <= 1e-6f)
            return value >= b ? 1f : 0f;
        float t = Mathf.Clamp01((value - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    private float SampleTexture(Color32[] pixels, int width, int height, Vector2 uv)
    {
        if (pixels == null || width <= 0 || height <= 0)
            return 0.5f;

        float x = Mathf.Repeat(uv.x, 1f) * width - 0.5f;
        float y = Mathf.Repeat(uv.y, 1f) * height - 0.5f;
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float tx = x - x0;
        float ty = y - y0;
        int x1 = WrapIndex(x0 + 1, width);
        int y1 = WrapIndex(y0 + 1, height);
        x0 = WrapIndex(x0, width);
        y0 = WrapIndex(y0, height);

        float a = Mathf.Lerp(pixels[y0 * width + x0].r, pixels[y0 * width + x1].r, tx);
        float b = Mathf.Lerp(pixels[y1 * width + x0].r, pixels[y1 * width + x1].r, tx);
        return Mathf.Lerp(a, b, ty) / 255f;
    }

    private static int WrapIndex(int value, int length)
    {
        value %= length;
        return value < 0 ? value + length : value;
    }

    private void UpdateParticles()
    {
        SubmittedParticleCount = 0;
        if (gpu == null) return;
        foreach (SurfaceSource source in sources)
        {
            if (source.Batches == null) continue;
            bool visible = source.Renderer != null && source.Renderer.enabled &&
                source.Renderer.gameObject.activeInHierarchy;
            if (!visible) continue;
            if (source.SkinnedRenderer != null && source.BakedMesh != null)
            {
                source.SkinnedRenderer.BakeMesh(source.BakedMesh, true);
                source.BakedMesh.GetVertices(source.CurrentVertices);
                source.BakedMesh.GetNormals(source.CurrentNormals);
                if (source.CurrentNormals.Count != source.CurrentVertices.Count)
                {
                    source.BakedMesh.RecalculateNormals();
                    source.BakedMesh.GetNormals(source.CurrentNormals);
                }
            }
            for (int layerIndex = 0; layerIndex < source.Batches.Length; layerIndex++)
            {
                SurfaceNoiseGpu.Batch batch = source.Batches[layerIndex];
                LayerSettings layer = layers[layerIndex];
                if (batch == null) continue;
                batch.visible = visible && layer != null && layer.enabled;
                if (!batch.visible || batch.count == 0) continue;
                gpu.Update(batch, source.CurrentVertices, source.CurrentNormals,
                    source.Renderer.transform.localToWorldMatrix, source.Renderer.bounds,
                    layer.particleSize, layer.particleColor, layer.surfaceOffset, layer.surfaceOffsetJitter);
                SubmittedParticleCount += batch.count;
            }
        }
    }

    private static Texture2D CreateDefaultNoiseTexture()
    {
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            name = "Generated Tileable Surface Noise",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                float value = PeriodicValueNoise(u, v, 3, 31) * 0.58f +
                              PeriodicValueNoise(u, v, 7, 97) * 0.24f +
                              PeriodicValueNoise(u, v, 15, 151) * 0.12f +
                              PeriodicValueNoise(u, v, 31, 233) * 0.06f;
                byte channel = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
                pixels[y * size + x] = new Color32(channel, channel, channel, 255);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static float PeriodicValueNoise(float u, float v, int cells, int salt)
    {
        float x = u * cells;
        float y = v * cells;
        int ix = Mathf.FloorToInt(x);
        int iy = Mathf.FloorToInt(y);
        float fx = SmoothStep01(x - ix);
        float fy = SmoothStep01(y - iy);
        float a = HashGrid(ix, iy, cells, salt);
        float b = HashGrid(ix + 1, iy, cells, salt);
        float c = HashGrid(ix, iy + 1, cells, salt);
        float d = HashGrid(ix + 1, iy + 1, cells, salt);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float SmoothStep01(float value)
    {
        return value * value * (3f - 2f * value);
    }

    private static float HashGrid(int x, int y, int cells, int salt)
    {
        x %= cells;
        y %= cells;
        if (x < 0) x += cells;
        if (y < 0) y += cells;
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00ffffffu) / 16777215f;
        }
    }

    private void ReleaseBakedMeshes()
    {
        for (int i = 0; i < sources.Count; i++)
        {
            Mesh mesh = sources[i].BakedMesh;
            if (mesh == null)
                continue;

            if (Application.IsPlaying(gameObject))
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }
    }

    private void OnDisable()
    {
        ActiveEffects.Remove(this);
        gpu?.Dispose();
        gpu = null;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        SubmittedParticleCount = 0;
        initialized = false;
        if (particleSystem != null)
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ReleaseBakedMeshes();
        sources.Clear();
        candidates = Array.Empty<Candidate>();

        if (generatedNoise != null)
        {
            DestroyOwnedObject(generatedNoise);
            generatedNoise = null;
            layerTextureData.Clear();
        }
    }

    private void OnDestroy()
    {
        ActiveEffects.Remove(this);
        gpu?.Dispose();
        gpu = null;
        ReleaseBakedMeshes();
        if (generatedNoise != null)
            DestroyOwnedObject(generatedNoise);
    }

    private void DestroyOwnedObject(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.IsPlaying(gameObject))
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
