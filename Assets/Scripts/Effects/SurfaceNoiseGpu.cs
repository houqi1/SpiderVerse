using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// CPU uploads only the baked surface. Particle positions never return to the CPU.
internal sealed class SurfaceNoiseGpu : IDisposable
{
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
        internal int count;
        internal Bounds bounds;
        internal bool visible;
        internal Batch(Material template, List<Anchor> data, int vertexCount)
        {
            sourceMaterial = template;
            if (template != null)
            {
                material = new Material(template) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
                material.EnableKeyword("SURFACE_NOISE_GPU");
            }
            count = data.Count;
            if (count == 0) return;
            vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, 12);
            normals = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, 12);
            anchors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 48);
            particles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 32);
            anchors.SetData(data);
            properties.SetBuffer("_SurfaceParticles", particles);
        }
        public void Dispose()
        {
            vertices?.Dispose(); normals?.Dispose(); anchors?.Dispose(); particles?.Dispose();
            if (material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
            vertices = normals = anchors = particles = null;
            count = 0;
        }
    }

    private readonly ComputeShader compute;
    private readonly int kernel;
    private readonly List<Batch> batches = new List<Batch>();

    internal SurfaceNoiseGpu(ComputeShader shader, Material template)
    {
        compute = UnityEngine.Object.Instantiate(shader);
        compute.hideFlags = HideFlags.HideAndDontSave;
        kernel = compute.FindKernel("UpdateAnchors");
    }

    internal Batch AddBatch(Material template, List<Anchor> anchors, int vertexCount)
    {
        var batch = new Batch(template, anchors, vertexCount);
        batches.Add(batch);
        return batch;
    }

    internal void ClearBatches()
    {
        foreach (var batch in batches) batch.Dispose();
        batches.Clear();
    }

    internal void Update(Batch batch, List<Vector3> vertices, List<Vector3> normals,
        Matrix4x4 localToWorld, Bounds bounds, Vector2 size, Color color, float offset, float jitter)
    {
        if (batch.count == 0) return;
        batch.vertices.SetData(vertices);
        batch.normals.SetData(normals);
        compute.SetInt("_AnchorCount", batch.count);
        compute.SetMatrix("_LocalToWorld", localToWorld);
        compute.SetMatrix("_NormalToWorld", localToWorld.inverse.transpose);
        compute.SetVector("_SizeOffset", new Vector4(Mathf.Min(size.x, size.y), Mathf.Max(size.x, size.y), offset, jitter));
        compute.SetVector("_ParticleColor", (Vector4)color);
        compute.SetBuffer(kernel, "_Vertices", batch.vertices);
        compute.SetBuffer(kernel, "_Normals", batch.normals);
        compute.SetBuffer(kernel, "_Anchors", batch.anchors);
        compute.SetBuffer(kernel, "_SurfaceParticles", batch.particles);
        compute.Dispatch(kernel, (batch.count + 63) / 64, 1, 1);
        bounds.Expand(2f * (Mathf.Max(size.x, size.y) + Mathf.Abs(offset) + Mathf.Abs(jitter)));
        batch.bounds = bounds;
    }

    internal void Draw(Camera camera, int layer)
    {
        foreach (var batch in batches)
        {
            if (!batch.visible || batch.count == 0 || batch.sourceMaterial == null || batch.material == null) continue;
            // Keep each layer's private GPU material live-linked to Inspector edits.
            if (batch.material.shader != batch.sourceMaterial.shader)
                batch.material.shader = batch.sourceMaterial.shader;
            batch.material.CopyPropertiesFromMaterial(batch.sourceMaterial);
            batch.material.enableInstancing = true;
            batch.material.EnableKeyword("SURFACE_NOISE_GPU");
            var parameters = new RenderParams(batch.material)
            {
                camera = camera,
                layer = layer,
                worldBounds = batch.bounds,
                matProps = batch.properties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, batch.count);
        }
    }

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
