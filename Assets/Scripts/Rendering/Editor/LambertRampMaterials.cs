using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 给场景中指定的 Spider-Gwen 对象挂 Custom/LambertRamp 材质。
// 结构：一个父材质（统一调 gradient / lambert 参数）+ 每个材质槽一个子材质（只覆盖原来的 BaseMap / BaseColor）。
// 通过 Tools/LambertRamp 下的请求文件驱动：
//   list.request            -> 把场景里所有名字含 "Spider-Gwen" 的对象写入 list.txt
//   assign.request (内容="目标路径|来源路径") -> 给目标赋材质，贴图/UV/颜色取自来源对象的原材质，结果写入 report.txt
[InitializeOnLoad]
public static class LambertRampMaterials
{
    const string Dir = "Assets/Materials/LambertRamp";
    const string ParentPath = Dir + "/SpiderGwen_LambertRamp_Parent.mat";
    const string ParentBaseMap = "Assets/Texture/GwenPainterly/Gwen_Painterly_BaseColor_4K.png";
    const string Work = "Tools/LambertRamp";
    static double next;

    static LambertRampMaterials() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            if (File.Exists(Work + "/list.request")) { File.Delete(Work + "/list.request"); List(); }
            if (File.Exists(Work + "/resetgradient.request")) { File.Delete(Work + "/resetgradient.request"); ResetChildGradients(); }
            if (File.Exists(Work + "/assign.request"))
            {
                // 内容: "目标路径|原材质来源路径"，来源省略时读目标自身当前材质
                var parts = File.ReadAllText(Work + "/assign.request").Trim().Split('|');
                File.Delete(Work + "/assign.request");
                Apply(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : parts[0].Trim());
            }
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    // 子材质的 gradient 回到继承父材质：清掉 gradient tag 和 _RampMap / 旧色标等覆盖，只保留 _BaseMap 和 _BaseMapUV。
    static void ResetChildGradients()
    {
        var lines = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Dir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m || !m.parent) continue;
            var map = m.GetTexture("_BaseMap"); var st = (m.mainTextureScale, m.mainTextureOffset);
            float uv = m.GetFloat("_BaseMapUV");
            m.RevertAllPropertyOverrides();
            m.SetOverrideTag("LambertRampGradient", "");
            m.SetOverrideTag("LambertRampSource", "");
            m.SetTexture("_BaseMap", map);
            m.SetTextureScale("_BaseMap", st.Item1); m.SetTextureOffset("_BaseMap", st.Item2);
            if (uv != m.parent.GetFloat("_BaseMapUV")) m.SetFloat("_BaseMapUV", uv);
            EditorUtility.SetDirty(m);
            lines.Add(path);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllLines(Work + "/report.txt", lines);
    }

    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

    static IEnumerable<Transform> AllTransforms()
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    yield return t;
        }
    }

    static void List()
    {
        var lines = new List<string>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
            lines.Add("scene: " + SceneManager.GetSceneAt(s).path + (SceneManager.GetSceneAt(s).isDirty ? " (dirty)" : ""));
        foreach (var t in AllTransforms())
        {
            if (t.name.IndexOf("Spider-Gwen", StringComparison.OrdinalIgnoreCase) < 0 &&
                t.name.IndexOf("SpiderGwen", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var rs = t.GetComponentsInChildren<Renderer>(true);
            var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name + "(" + m.shader.name + ")").Distinct();
            lines.Add(PathOf(t) + " | active=" + t.gameObject.activeInHierarchy + " | id=" + t.gameObject.GetInstanceID()
                      + " | renderers=" + rs.Length + " | " + string.Join(", ", mats));
        }
        File.WriteAllText(Work + "/list.txt", string.Join("\n", lines));
    }

    static Texture FirstTex(Material m, params string[] names)
    {
        foreach (var n in names) if (m.HasProperty(n)) { var t = m.GetTexture(n); if (t) return t; }
        return null;
    }

    static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }

    static void Apply(string targetPath, string sourcePath)
    {
        var matches = AllTransforms().Where(t => PathOf(t) == targetPath).ToArray();
        if (matches.Length != 1) throw new Exception("对象路径匹配到 " + matches.Length + " 个: " + targetPath);
        var target = matches[0].gameObject;

        var shader = Shader.Find("Custom/LambertRamp");
        if (!shader) throw new Exception("找不到 shader Custom/LambertRamp");

        Directory.CreateDirectory(Dir);
        AssetDatabase.Refresh();

        var parent = AssetDatabase.LoadAssetAtPath<Material>(ParentPath);
        if (!parent)
        {
            parent = new Material(shader) { name = Path.GetFileNameWithoutExtension(ParentPath) };
            AssetDatabase.CreateAsset(parent, ParentPath);
        }

        // 父材质：Gwen 共用的 Painterly 底色图集，走 UV1
        var parentTex = AssetDatabase.LoadAssetAtPath<Texture>(ParentBaseMap);
        if (!parentTex) throw new Exception("找不到底色贴图 " + ParentBaseMap);
        parent.SetTexture("_BaseMap", parentTex);
        parent.SetFloat("_BaseMapUV", 1);
        LambertRampGUI.EnsureBaked(parent); // 父材质没有 gradient 时写入默认值
        EditorUtility.SetDirty(parent);

        var sourceMatches = AllTransforms().Where(t => PathOf(t) == sourcePath).ToArray();
        if (sourceMatches.Length != 1) throw new Exception("来源路径匹配到 " + sourceMatches.Length + " 个: " + sourcePath);
        var source = sourceMatches[0];

        var renderers = target.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        var used = new HashSet<string>();
        var log = new List<string>();

        foreach (var r in renderers)
        {
            // 按相对路径在来源对象里找对应 renderer，取它的原始材质
            string rel = PathOf(r.transform).Substring(targetPath.Length);
            var srcT = source.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => PathOf(t).Substring(sourcePath.Length) == rel);
            var srcR = srcT ? srcT.GetComponent<Renderer>() : null;
            if (!srcR) throw new Exception("来源里找不到对应 renderer: " + rel);
            var src = srcR.sharedMaterials;
            if (src.Length != r.sharedMaterials.Length) throw new Exception("材质槽数量不一致: " + rel);
            var dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string baseName = "LambertRamp_" + Safe(r.name) + (src.Length > 1 ? "_" + i : "");
                string name = baseName; int n = 1;
                while (!used.Add(name)) name = baseName + "_" + (++n);
                string path = Dir + "/" + name + ".mat";

                var child = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!child)
                {
                    child = new Material(shader) { name = name };
                    AssetDatabase.CreateAsset(child, path);
                }
                child.parent = parent;
                child.RevertAllPropertyOverrides();

                // 只把原材质的底色贴图 / 颜色作为子材质覆盖，其余参数全部继承父材质
                var old = src[i];
                string info = "";
                if (old)
                {
                    string texProp = new[] { "_BaseMap", "_MainTex", "_BaseColorMap" }
                        .FirstOrDefault(p => old.HasProperty(p) && old.GetTexture(p));
                    var tex = texProp != null ? old.GetTexture(texProp) : null;
                    if (tex && tex != parentTex)
                    {
                        child.SetTexture("_BaseMap", tex);
                        child.SetTextureScale("_BaseMap", old.GetTextureScale(texProp));
                        child.SetTextureOffset("_BaseMap", old.GetTextureOffset(texProp));
                    }
                    // 原材质没有 UV 选项（如 URP Lit）就是 UV0
                    float uv = old.HasProperty("_BaseMapUV") ? old.GetFloat("_BaseMapUV") : 0;
                    if (uv != 1) child.SetFloat("_BaseMapUV", uv);
                    Color c = old.HasProperty("_BaseColor") ? old.GetColor("_BaseColor")
                            : old.HasProperty("_Color") ? old.GetColor("_Color") : Color.white;
                    if (c != Color.white) child.SetColor("_BaseColor", c);
                    info = " tex=" + (tex ? tex.name : "none") + " uv" + uv + " color=" + c;
                }
                EditorUtility.SetDirty(child);
                dst[i] = child;
                log.Add(r.name + "[" + i + "] -> " + path + " (from " + (old ? old.name + " / " + old.shader.name : "null") + info + ")");
            }
            Undo.RecordObject(r, "Assign LambertRamp materials");
            r.sharedMaterials = dst;
            EditorUtility.SetDirty(r);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(target.scene); // 不自动保存场景
        File.WriteAllText(Work + "/report.txt", "Target: " + targetPath + "\nSource: " + sourcePath + "\nParent: " + ParentPath + "\n" + string.Join("\n", log));
        Selection.activeObject = parent;
        Debug.Log("LAMBERT_RAMP_OK " + renderers.Length + " renderers\n" + string.Join("\n", log));
    }
}
