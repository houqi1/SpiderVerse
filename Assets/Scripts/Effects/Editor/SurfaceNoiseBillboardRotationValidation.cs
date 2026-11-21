using System;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class SurfaceNoiseBillboardRotationValidation
{
    struct Particle { public Vector4 positionSize, color, normalWS, surfacePosition, distributionNormal; }
    struct Anchor { public uint i0, i1, i2; public float brightness; public Vector3 barycentric; public float size, offset; public Vector3 padding; }

    [MenuItem("SpiderVerse/Validate Surface Billboard Rotation")]
    static void Validate()
    {
        const string path = "Temp/BillboardRotationValidation/gpu.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var update = UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("SurfaceNoiseAnchors"));
        var rotate = UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("SurfaceNoiseBillboardRotation"));
        try
        {
            using (var vertices = new ComputeBuffer(3, 12))
            using (var normals = new ComputeBuffer(3, 12))
            using (var uvs = new ComputeBuffer(3, 8))
            using (var anchors = new ComputeBuffer(3, 48))
            using (var particles = new ComputeBuffer(3, 80))
            using (var directions = new ComputeBuffer(3, 16))
            using (var axes = new ComputeBuffer(3, 8))
            using (var cdf = new ComputeBuffer(3, 4))
            {
                vertices.SetData(new[] { Vector3.zero, Vector3.right * 2, Vector3.up * 3 });
                normals.SetData(new[] { Vector3.forward, Vector3.forward, Vector3.forward });
                uvs.SetData(new[] { Vector2.zero, Vector2.right, Vector2.up });
                var anchor = new Anchor { i1 = 1, i2 = 2, brightness = 1, barycentric = Vector3.right };
                anchors.SetData(new[] { anchor, anchor, anchor });
                int kernel = update.FindKernel("UpdateAnchors");
                update.SetInt("_AnchorCount", 3); update.SetInt("_AnchorOffset", 0);
                var transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), new Vector3(2, 3, 1));
                update.SetMatrix("_LocalToWorld", transform); update.SetMatrix("_NormalToWorld", transform.inverse.transpose);
                update.SetVector("_SizeOffset", new Vector4(1, 1, 0, 0));
                update.SetVector("_TriangleSizeSettings", Vector4.zero);
                update.SetBuffer(kernel, "_Vertices", vertices); update.SetBuffer(kernel, "_Normals", normals);
                update.SetBuffer(kernel, "_SurfaceUVs", uvs); update.SetBuffer(kernel, "_Anchors", anchors);
                update.SetBuffer(kernel, "_SurfaceParticles", particles); update.SetBuffer(kernel, "_SurfaceDirections", directions);
                var world = new Vector4[3];
                for (int axis = 0; axis < 2; axis++)
                {
                    update.SetVector("_DirectionSettings", new Vector4(1, axis, 0, 0));
                    update.Dispatch(kernel, 1, 1, 1); directions.GetData(world);
                    Vector3 expected = axis == 0 ? Vector3.up : Vector3.left;
                    if (Vector3.Distance(world[0], expected) > .001f) throw new Exception("UV direction or world transform incorrect");
                }
                directions.SetData(new[] { new Vector4(1, 0, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 1, 0, 0) });
                var p = new Particle { surfacePosition = new Vector4(1, 0, 5, 1) };
                particles.SetData(new[] { p, p, p });
                axes.SetData(new[] { Vector2.up, Vector2.up, Vector2.up }); cdf.SetData(new uint[] { 1, 2, 3 });
                int rotation = rotate.FindKernel("UpdateRotation");
                rotate.SetBuffer(rotation, "_SurfaceParticles", particles); rotate.SetBuffer(rotation, "_SurfaceDirections", directions);
                rotate.SetBuffer(rotation, "_SurfaceBillboardAxes", axes); rotate.SetBuffer(rotation, "_SurfaceDistributionCdf", cdf);
                rotate.SetInt("_RotationCount", 3);
                rotate.SetVector("_SurfaceDistributionRange", new Vector4(0, 3, 0, 3));
                rotate.SetVector("_SurfaceSilhouetteDistribution", new Vector4(0, 1, 1, 1));
                rotate.SetVector("_RotationCameraRight", Vector3.right); rotate.SetVector("_RotationCameraUp", Vector3.up);
                rotate.SetVector("_RotationCameraPosition", Vector4.zero);
                rotate.SetVector("_RotationCameraForward", new Vector4(0, 0, 1, 1));
                rotate.SetVector("_RotationStep", new Vector4(1, 1, 0, 0));
                rotate.Dispatch(rotation, 1, 1, 1);
                var result = new Vector2[3]; axes.GetData(result);
                Check(result[0], Vector2.right); Check(result[1], Vector2.up); Check(result[2], Vector2.up);
                rotate.SetVector("_RotationCameraForward", new Vector4(0, 0, 1, 0));
                rotate.Dispatch(rotation, 1, 1, 1); axes.GetData(result); Check(result[1], Vector2.left);
                directions.SetData(new[] { new Vector4(0, 1, 0, 0), Vector4.zero, Vector4.zero });
                rotate.SetVector("_RotationStep", new Vector4(.5f, 0, 0, 0));
                rotate.Dispatch(rotation, 1, 1, 1); axes.GetData(result);
                Check(result[0], new Vector2(1, 1).normalized); Check(result[1], Vector2.left);
                File.WriteAllText(path, "PASS: deformed world-space UV U/V directions, orthographic alignment, pole history hold, off-axis perspective projection, smooth rotation and invalid-direction hold.\n");
                Debug.Log("Surface billboard rotation: PASS. " + path);
            }
        }
        catch (Exception error) { File.WriteAllText(path, "FAIL: " + error); Debug.LogException(error); }
        finally { UnityEngine.Object.DestroyImmediate(update); UnityEngine.Object.DestroyImmediate(rotate); }
    }

    static void Check(Vector2 actual, Vector2 expected)
    {
        if (!float.IsFinite(actual.x) || !float.IsFinite(actual.y) || Vector2.Distance(actual, expected) > .001f)
            throw new Exception("Expected " + expected + ", got " + actual);
    }
}
