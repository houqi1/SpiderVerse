using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 场景里的 Spider-Gwen 用的不是 FBX 自带的网格，而是项目里单独保存的网格副本：
//   Assets/Texture/GwenPainterly/Meshes/*_PainterlyUV.asset
//   Assets/Model/*_SmoothNormalVC.asset
// 这里对这些网格逐个重新拟合绑定姿势高度（用网格自己的 bindposes，不依赖顶点顺序），写入 UV3。
public static class BindPoseHeightCopyToLassyla
{
    static readonly string[] MeshDirs = { "Assets/Texture/GwenPainterly/Meshes", "Assets/Model" };
    static readonly string[] Suffixes = { "_PainterlyUV", "_SmoothNormalVC", "_LassylaGenerated" };
    const string SourceModel = "Assets/Model/Spider-Gwen.fbx";
    const int Channel = 3;

    [InitializeOnLoadMethod]
    static void AutoRun() { EditorApplication.delayCall += () => Sync(false); }

    [MenuItem("Tools/Sync Bind-Pose Height To Scene Meshes")]
    static void Manual() { Sync(true); }

    static string StripSuffix(string name)
    {
        foreach (var s in Suffixes) if (name.EndsWith(s)) return name.Substring(0, name.Length - s.Length);
        return name;
    }

    static void Sync(bool verbose)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModel);
        if (!fbx) return;
        var sources = new Dictionary<string, SkinnedMeshRenderer>();
        foreach (var smr in fbx.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            sources[smr.name] = smr;
            sources[smr.name.Replace(' ', '_')] = smr;
        }

        int updated = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Mesh", MeshDirs))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh || AssetDatabase.IsSubAsset(mesh)) continue;
            string key = StripSuffix(mesh.name);
            if (!sources.TryGetValue(key, out var smr) && !sources.TryGetValue(StripSuffix(System.IO.Path.GetFileNameWithoutExtension(path)), out smr)) continue;

            var bind = mesh.bindposes;
            if (bind.Length == 0 || bind.Length != smr.bones.Length)
            {
                Debug.LogWarning($"[BindPoseHeight] 骨骼数与 FBX 不一致，跳过: {path} ({bind.Length} vs {smr.bones.Length})");
                continue;
            }
            if (!BindPoseHeightPostprocessor.FitHeight(smr.bones, bind, out Vector3 a, out float b))
            {
                Debug.LogWarning($"[BindPoseHeight] 拟合失败（匹配骨骼不足）: {path}");
                continue;
            }

            var verts = mesh.vertices;
            var uv = new List<Vector3>(verts.Length);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in verts)
            {
                float h = Vector3.Dot(a, v) + b;
                uv.Add(new Vector3(0f, h, 0f));
                lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
            }

            var cur = new List<Vector3>();
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord3)) mesh.GetUVs(Channel, cur);
            bool same = cur.Count == uv.Count;
            for (int i = 0; same && i < uv.Count; i++) same = (cur[i] - uv[i]).sqrMagnitude < 1e-10f;
            if (same) continue; // 已是最新

            mesh.SetUVs(Channel, uv);
            EditorUtility.SetDirty(mesh);
            updated++;
            Debug.Log($"[BindPoseHeight] 已写入 UV3: {path}  高度 {lo:F3} ~ {hi:F3} m", mesh);
        }
        if (updated > 0) AssetDatabase.SaveAssets();
        else if (verbose) Debug.Log("[BindPoseHeight] 场景网格的 UV3 已是最新。");
    }
}
