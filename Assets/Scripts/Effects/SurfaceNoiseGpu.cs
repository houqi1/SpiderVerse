using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// CPU uploads only the baked surface. Particle positions never return to the CPU.
internal sealed class SurfaceNoiseGpu : IDisposable
{
    internal const int DistributionChoices = 16;
    [StructLayout(LayoutKind.Sequential)]
    internal struct Anchor
    {
        public uint i0, i1, i2;
        public float brightness;
        public Vector3 barycentric;
        public float sizeRandom;
        public float offsetRandom;
        public Vector3 padding;
    }

    internal sealed class Batch : IDisposable
    {
        internal GraphicsBuffer vertices, normals, anchors, particles;
        internal readonly Material sourceMaterial;
        internal readonly Material material;
        internal readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        internal int anchorOffset;
        internal LayerPool pool;
        internal int count;
        internal readonly int choiceCount;
        internal int anchorCount => count * choiceCount;
        internal int layerIndex;
        internal Bounds bounds;
        internal bool visible;
        internal Batch(Material template, List<Anchor> data, int vertexCount, int layerIndex, int choiceCount)
        {
            sourceMaterial = template;
            this.layerIndex = layerIndex;
            if (template != null)
            {
                material = new Material(template) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
                material.EnableKeyword("SURFACE_NOISE_GPU");
            }
            this.choiceCount = choiceCount;
            count = data.Count / choiceCount;
            if (count == 0) return;
            vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, 12);
            normals = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, 12);
            anchors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, anchorCount, 48);
            anchors.SetData(data);
        }
        public void Dispose()
        {
            vertices?.Dispose(); normals?.Dispose(); anchors?.Dispose();
            if (material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
            vertices = normals = anchors = particles = null;
            count = 0;
        }
    }

    internal sealed class LayerPool : IDisposable
    {
        internal readonly List<Batch> batches = new List<Batch>();
        internal GraphicsBuffer particles, a, b;
        internal int sampleCount;
        public void Dispose() { particles?.Dispose(); a?.Dispose(); b?.Dispose(); }
    }
    private readonly List<LayerPool> pools = new List<LayerPool>();

    private readonly ComputeShader compute;
    private readonly int kernel, weightsKernel, scanKernel;
    private readonly List<Batch> batches = new List<Batch>();
    private static readonly int CameraPositionId = Shader.PropertyToID("_SurfaceCameraPosition");
    private static readonly int CameraForwardId = Shader.PropertyToID("_SurfaceCameraForward");
    private static readonly int CameraInfluenceId = Shader.PropertyToID("_SurfaceCameraInfluence");
    private static readonly int SilhouetteDistributionId = Shader.PropertyToID("_SurfaceSilhouetteDistribution");

    internal SurfaceNoiseGpu(ComputeShader shader, Material template)
    {
        compute = UnityEngine.Object.Instantiate(shader);
        compute.hideFlags = HideFlags.HideAndDontSave;
        kernel = compute.FindKernel("UpdateAnchors");
        weightsKernel = compute.FindKernel("BuildDistributionWeights");
        scanKernel = compute.FindKernel("ScanDistributionWeights");
    }

    internal Batch AddBatch(Material template, List<Anchor> anchors, int vertexCount, int layerIndex, int choiceCount)
    {
        var batch = new Batch(template, anchors, vertexCount, layerIndex, choiceCount);
        batches.Add(batch);
        return batch;
    }

    internal void ClearBatches()
    {
        foreach (var batch in batches) batch.Dispose();
        batches.Clear();
        foreach (var pool in pools) pool.Dispose();
        pools.Clear();
    }

    internal void FinalizeBatches(bool independentRendererCounts)
    {
        var layers = new Dictionary<int, LayerPool>();
        foreach (var batch in batches)
        {
            if (batch.count == 0) continue;
            LayerPool pool;
            if (independentRendererCounts)
            {
                pool = new LayerPool(); pools.Add(pool);
            }
            else if (!layers.TryGetValue(batch.layerIndex, out pool))
            {
                pool = new LayerPool(); layers.Add(batch.layerIndex, pool); pools.Add(pool);
            }
            batch.pool = pool;
            batch.anchorOffset = pool.sampleCount;
            pool.sampleCount += batch.anchorCount;
            pool.batches.Add(batch);
        }
        foreach (var pool in pools)
        {
            pool.particles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pool.sampleCount, 80);
            pool.a = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pool.sampleCount, 4);
            pool.b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pool.sampleCount, 4);
            foreach (var batch in pool.batches)
            {
                batch.particles = pool.particles;
                batch.properties.SetBuffer("_SurfaceParticles", pool.particles);
                batch.properties.SetBuffer("_SurfaceDistributionCdf", pool.a);
            }
        }
    }

    internal void Update(Batch batch, List<Vector3> vertices, List<Vector3> normals,
        Matrix4x4 localToWorld, Bounds bounds, Vector2 size, Color color, float offset, float jitter)
    {
        if (batch.count == 0) return;
        batch.vertices.SetData(vertices);
        batch.normals.SetData(normals);
        compute.SetInt("_AnchorCount", batch.anchorCount);
        compute.SetInt("_AnchorOffset", batch.anchorOffset);
        compute.SetMatrix("_LocalToWorld", localToWorld);
        compute.SetMatrix("_NormalToWorld", localToWorld.inverse.transpose);
        compute.SetVector("_SizeOffset", new Vector4(Mathf.Min(size.x, size.y), Mathf.Max(size.x, size.y), offset, jitter));
        compute.SetVector("_ParticleColor", (Vector4)color);
        compute.SetBuffer(kernel, "_Vertices", batch.vertices);
        compute.SetBuffer(kernel, "_Normals", batch.normals);
        compute.SetBuffer(kernel, "_Anchors", batch.anchors);
        compute.SetBuffer(kernel, "_SurfaceParticles", batch.particles);
        compute.Dispatch(kernel, (batch.anchorCount + 63) / 64, 1, 1);
        bounds.Expand(2f * (1.5f * Mathf.Max(size.x, size.y) + Mathf.Abs(offset) + Mathf.Abs(jitter)));
        batch.bounds = bounds;
    }

    internal void Draw(CommandBuffer commandBuffer, Camera camera,
        SpiderVerse.LineArt.ObjectLineArtFeature.CameraSample cameraSample,
        SurfaceNoiseParticleEffect.LayerSettings[] layerSettings)
    {
        if (commandBuffer == null || camera == null) return;
        foreach (var pool in pools)
        {
            var first = pool.batches[0];
            var settings = layerSettings != null && first.layerIndex < layerSettings.Length
                ? layerSettings[first.layerIndex] : null;
            bool redistribute = settings != null && settings.preferSilhouetteDistribution &&
                settings.frontFacingWeight < 1f && first.choiceCount > 1;
            GraphicsBuffer cdf = pool.a;
            if (redistribute)
            {
                // Distribution uses the actual render camera, not the stepped line-art camera.
                Vector3 position = camera.transform.position, forward = camera.transform.forward;
                commandBuffer.SetComputeVectorParam(compute, "_DistributionCameraPosition", new Vector4(position.x,position.y,position.z,1));
                commandBuffer.SetComputeVectorParam(compute, "_DistributionCameraForward", new Vector4(forward.x,forward.y,forward.z,camera.orthographic?1:0));
                commandBuffer.SetComputeIntParam(compute, "_DistributionSampleCount", pool.sampleCount);
                commandBuffer.SetComputeBufferParam(compute, weightsKernel, "_SurfaceParticles", pool.particles);
                commandBuffer.SetComputeBufferParam(compute, weightsKernel, "_DistributionWrite", pool.a);
                foreach (var batch in pool.batches)
                {
                    commandBuffer.SetComputeIntParam(compute, "_AnchorCount", batch.anchorCount);
                    commandBuffer.SetComputeIntParam(compute, "_AnchorOffset", batch.anchorOffset);
                    commandBuffer.SetComputeVectorParam(compute, "_DistributionSettings", new Vector4(
                        Mathf.Clamp01(settings.frontFacingWeight), Mathf.Clamp(settings.silhouetteDistributionWidth,.01f,1f),
                        CanDraw(batch) ? 1 : 0, 0));
                    commandBuffer.DispatchCompute(compute, weightsKernel, (batch.anchorCount+63)/64,1,1);
                }
                commandBuffer.SetComputeIntParam(compute, "_DistributionSampleCount", pool.sampleCount);
                GraphicsBuffer write = pool.b;
                for (int step=1; step<pool.sampleCount; step <<= 1)
                {
                    commandBuffer.SetComputeIntParam(compute, "_ScanStep", step);
                    commandBuffer.SetComputeBufferParam(compute, scanKernel, "_DistributionRead", cdf);
                    commandBuffer.SetComputeBufferParam(compute, scanKernel, "_DistributionWrite", write);
                    commandBuffer.DispatchCompute(compute, scanKernel, (pool.sampleCount+63)/64,1,1);
                    var swap=cdf; cdf=write; write=swap;
                }
            }
            int totalCount=0, instanceOffset=0;
            foreach (var batch in pool.batches) if (CanDraw(batch)) totalCount+=batch.count;
            foreach (var batch in pool.batches)
            {
                batch.properties.SetBuffer("_SurfaceDistributionCdf", cdf);
                batch.properties.SetVector("_SurfaceDistributionRange",new Vector4(
                    batch.anchorOffset,pool.sampleCount,instanceOffset,totalCount));
                if (CanDraw(batch)) instanceOffset+=batch.count;
            }
        }
        foreach (var batch in batches)
        {
            if (!CanDraw(batch)) continue;
            // Keep each layer's private GPU material live-linked to Inspector edits.
            if (batch.material.shader != batch.sourceMaterial.shader)
                batch.material.shader = batch.sourceMaterial.shader;
            batch.material.CopyPropertiesFromMaterial(batch.sourceMaterial);
            batch.material.enableInstancing = true;
            batch.material.EnableKeyword("SURFACE_NOISE_GPU");
            SurfaceNoiseParticleEffect.LayerSettings settings = layerSettings != null &&
                batch.layerIndex >= 0 && batch.layerIndex < layerSettings.Length
                    ? layerSettings[batch.layerIndex]
                    : null;
            bool influenceEnabled = settings != null && settings.cameraViewEffectEnabled;
            batch.properties.SetVector(SilhouetteDistributionId, new Vector4(
                settings != null && settings.preferSilhouetteDistribution ? 1f : 0f,
                settings != null ? Mathf.Clamp01(settings.frontFacingWeight) : 1f,
                settings != null ? Mathf.Clamp(settings.silhouetteDistributionWidth, 0.01f, 1f) : 0.5f, batch.choiceCount));
            batch.properties.SetVector(CameraPositionId, new Vector4(
                cameraSample.position.x, cameraSample.position.y, cameraSample.position.z, 1f));
            batch.properties.SetVector(CameraForwardId, new Vector4(
                cameraSample.forward.x, cameraSample.forward.y, cameraSample.forward.z,
                cameraSample.orthographic ? 1f : 0f));
            batch.properties.SetVector(CameraInfluenceId, new Vector4(
                influenceEnabled ? 1f : 0f,
                settings != null ? Mathf.Clamp01(settings.cameraFacingStrength) : 0f,
                settings != null ? Mathf.Max(0.01f, settings.cameraFacingSoftness) : 0.15f,
                settings != null ? Mathf.Clamp01(settings.cameraSilhouetteSizeBoost) : 0f));
            commandBuffer.DrawProcedural(Matrix4x4.identity, batch.material, 0,
                MeshTopology.Triangles, 6, batch.count, batch.properties);
        }
    }

    private static bool CanDraw(Batch batch) => batch.visible && batch.count > 0 &&
        batch.sourceMaterial != null && batch.material != null;

    public void Dispose()
    {
        ClearBatches();
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(compute);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(compute);
        }
    }
}
