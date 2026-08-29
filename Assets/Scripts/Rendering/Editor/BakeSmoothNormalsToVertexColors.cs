#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes angle-weighted smooth normals into vertex colors (RGB) in tangent space.
/// Skinned meshes: Unity skinning only updates Position/Normal/Tangent, not vertex color.
/// Storing smooth normals in tangent space lets the outline shader rebuild them with the
/// skinned TBN at runtime (bone/skinning space).
/// </summary>
public static class BakeSmoothNormalsToVertexColors
{
    const float PositionWeldEpsilon = 1e-5f;

    [MenuItem("SpiderVerse/Bake Smooth Normals To Vertex Colors")]
    public static void BakeSelected()
    {
        GameObject[] selection = Selection.gameObjects;
        if (selection == null || selection.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Bake Smooth Normals",
                "Select one or more GameObjects that have MeshFilter or SkinnedMeshRenderer.",
                "OK");
            return;
        }

        int baked = 0;
        int skipped = 0;

        foreach (GameObject go in selection)
        {
            if (go == null)
                continue;

            var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var filters = go.GetComponentsInChildren<MeshFilter>(true);

            for (int i = 0; i < skinned.Length; i++)
            {
                if (BakeRendererMesh(skinned[i], skinned[i].sharedMesh, m => skinned[i].sharedMesh = m))
                    baked++;
                else
                    skipped++;
            }

            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (BakeRendererMesh(filter, filter.sharedMesh, m => filter.sharedMesh = m))
                    baked++;
                else
                    skipped++;
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "Bake Smooth Normals",
            $"Done.\nBaked: {baked}\nSkipped: {skipped}\n\n" +
            "RGB = tangent-space smooth normals mapped to 0..1.\n" +
            "Alpha is preserved (or set to 1 if missing).",
            "OK");
    }

    static bool BakeRendererMesh(UnityEngine.Object undoTarget, Mesh source, Action<Mesh> assign)
    {
        if (source == null)
            return false;

        string path = AssetDatabase.GetAssetPath(source);
        Mesh working = source;

        // Prefer editing a dedicated baked copy under the same folder when source is an imported mesh.
        if (!string.IsNullOrEmpty(path) && !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            string bakedPath = $"{folder}/{source.name}_SmoothNormalVC.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(bakedPath);
            if (existing == null)
            {
                working = UnityEngine.Object.Instantiate(source);
                working.name = source.name + "_SmoothNormalVC";
                AssetDatabase.CreateAsset(working, bakedPath);
            }
            else
            {
                working = existing;
                EditorUtility.CopySerialized(source, working);
                working.name = source.name + "_SmoothNormalVC";
            }

            assign(working);
        }
        else
        {
            Undo.RecordObject(undoTarget, "Bake Smooth Normals To Vertex Colors");
            if (!string.IsNullOrEmpty(path))
                Undo.RecordObject(working, "Bake Smooth Normals To Vertex Colors");
        }

        if (!BakeIntoMesh(working))
            return false;

        EditorUtility.SetDirty(working);
        if (undoTarget != null)
            EditorUtility.SetDirty(undoTarget);
        return true;
    }

    public static bool BakeIntoMesh(Mesh mesh)
    {
        if (mesh == null)
            return false;

        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        int[] triangles = mesh.triangles;

        if (positions == null || positions.Length == 0 || triangles == null || triangles.Length < 3)
            return false;

        if (normals == null || normals.Length != positions.Length)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        if (tangents == null || tangents.Length != positions.Length)
        {
            mesh.RecalculateTangents();
            tangents = mesh.tangents;
        }

        int vertCount = positions.Length;
        var smoothOS = new Vector3[vertCount];

        // Weld by position, accumulate angle-weighted face normals.
        var weld = new Dictionary<Vector3Int, List<int>>(vertCount);
        for (int i = 0; i < vertCount; i++)
        {
            Vector3Int key = Quantize(positions[i]);
            if (!weld.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(4);
                weld.Add(key, list);
            }

            list.Add(i);
        }

        var accum = new Dictionary<Vector3Int, Vector3>(weld.Count);
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];

            Vector3 p0 = positions[i0];
            Vector3 p1 = positions[i1];
            Vector3 p2 = positions[i2];

            Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
            if (faceNormal.sqrMagnitude < 1e-20f)
                continue;

            // Angle weights at each corner.
            float w0 = AngleWeight(p1 - p0, p2 - p0);
            float w1 = AngleWeight(p0 - p1, p2 - p1);
            float w2 = AngleWeight(p0 - p2, p1 - p2);

            AddWeighted(accum, Quantize(p0), faceNormal.normalized * w0);
            AddWeighted(accum, Quantize(p1), faceNormal.normalized * w1);
            AddWeighted(accum, Quantize(p2), faceNormal.normalized * w2);
        }

        foreach (var kv in weld)
        {
            if (!accum.TryGetValue(kv.Key, out Vector3 sum) || sum.sqrMagnitude < 1e-20f)
            {
                // Fallback to original normals average.
                Vector3 fallback = Vector3.zero;
                for (int i = 0; i < kv.Value.Count; i++)
                    fallback += normals[kv.Value[i]];
                sum = fallback.sqrMagnitude > 1e-20f ? fallback : Vector3.up;
            }

            Vector3 n = sum.normalized;
            for (int i = 0; i < kv.Value.Count; i++)
                smoothOS[kv.Value[i]] = n;
        }

        Color[] existing = mesh.colors;
        bool hasColors = existing != null && existing.Length == vertCount;
        var colors = new Color[vertCount];

        for (int i = 0; i < vertCount; i++)
        {
            Vector3 nOS = normals[i].normalized;
            Vector4 t4 = tangents[i];
            Vector3 tOS = new Vector3(t4.x, t4.y, t4.z);
            if (tOS.sqrMagnitude < 1e-20f)
                tOS = Vector3.Cross(nOS, Vector3.up);
            tOS.Normalize();

            Vector3 bOS = Vector3.Cross(nOS, tOS) * t4.w;
            if (bOS.sqrMagnitude < 1e-20f)
                bOS = Vector3.Cross(nOS, tOS);
            bOS.Normalize();

            // Object → tangent: (dot S·T, S·B, S·N)
            Vector3 s = smoothOS[i];
            Vector3 ts = new Vector3(
                Vector3.Dot(s, tOS),
                Vector3.Dot(s, bOS),
                Vector3.Dot(s, nOS));
            if (ts.sqrMagnitude > 1e-20f)
                ts.Normalize();
            else
                ts = new Vector3(0f, 0f, 1f);

            float a = hasColors ? existing[i].a : 1f;
            colors[i] = new Color(ts.x * 0.5f + 0.5f, ts.y * 0.5f + 0.5f, ts.z * 0.5f + 0.5f, a);
        }

        mesh.colors = colors;
        return true;
    }

    static float AngleWeight(Vector3 a, Vector3 b)
    {
        float al = a.magnitude;
        float bl = b.magnitude;
        if (al < 1e-12f || bl < 1e-12f)
            return 0f;
        float cos = Mathf.Clamp(Vector3.Dot(a / al, b / bl), -1f, 1f);
        return Mathf.Acos(cos);
    }

    static void AddWeighted(Dictionary<Vector3Int, Vector3> accum, Vector3Int key, Vector3 value)
    {
        if (accum.TryGetValue(key, out Vector3 cur))
            accum[key] = cur + value;
        else
            accum[key] = value;
    }

    static Vector3Int Quantize(Vector3 p)
    {
        float s = 1f / PositionWeldEpsilon;
        return new Vector3Int(
            Mathf.RoundToInt(p.x * s),
            Mathf.RoundToInt(p.y * s),
            Mathf.RoundToInt(p.z * s));
    }
}
#endif
