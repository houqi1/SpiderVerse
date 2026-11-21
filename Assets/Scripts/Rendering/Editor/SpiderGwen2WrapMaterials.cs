using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 为场景中 "Spider-Gwen (2)" 下每个网格(材质槽)创建基于同一父材质的子材质(Material Variant)并赋值。
[InitializeOnLoad]
public static class SpiderGwen2WrapMaterials
{
    const string TargetName = "Spider-Gwen (2)";
    const string Dir = "Assets/Materials/SpiderGwen2";
    const string ParentPath = Dir + "/SpiderGwen2_WrapLambert_Parent.mat";
    const string Work = "Tools/SpiderGwen2Wrap";
    const string Request = Work + "/apply.request";

    static SpiderGwen2WrapMaterials() { EditorApplication.delayCall += RunRequested; }

    static void RunRequested()
    {
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += RunRequested; return; }
        File.Move(Request, Work + "/apply.running");
        try { Apply(); File.Move(Work + "/apply.running", Work + "/apply.completed"); }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    static GameObject FindTarget()
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == TargetName) return t.gameObject;
        }
        return null;
    }

    static Texture FirstTex(Material m, params string[] names)
    {
        foreach (var n in names) if (m.HasProperty(n)) { var t = m.GetTexture(n); if (t) return t; }
        return null;
    }

    static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }

    [MenuItem("Tools/Rendering/Create Spider-Gwen (2) Wrap Lambert Materials")]
    public static void Apply()
    {
        var target = FindTarget();
        if (!target) throw new Exception("找不到场景对象: " + TargetName);
        var shader = Shader.Find("Custom/ObjectSpaceNormalWrapLambert");
        if (!shader) throw new Exception("找不到 shader Custom/ObjectSpaceNormalWrapLambert");

        Directory.CreateDirectory(Dir);
        AssetDatabase.Refresh();

        var parent = AssetDatabase.LoadAssetAtPath<Material>(ParentPath);
        if (!parent)
        {
            parent = new Material(shader) { name = Path.GetFileNameWithoutExtension(ParentPath) };
            AssetDatabase.CreateAsset(parent, ParentPath);
        }

        var renderers = target.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        var used = new HashSet<string>();
        var log = new List<string>();

        foreach (var r in renderers)
        {
            var src = r.sharedMaterials;
            var dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string baseName = Safe(r.name) + (src.Length > 1 ? "_" + i : "");
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

                // 子材质不做任何覆盖：所有参数都在父材质里设置
                child.RevertAllPropertyOverrides();
                var old = src[i];
                EditorUtility.SetDirty(child);
                dst[i] = child;
                log.Add(r.name + "[" + i + "] -> " + path + " (was " + (old ? old.name : "null") + ")");
            }
            Undo.RecordObject(r, "Assign Wrap Lambert child materials");
            r.sharedMaterials = dst;
            EditorUtility.SetDirty(r);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty(); // 不自动保存场景
        File.WriteAllText(Work + "/report.txt", "Parent: " + ParentPath + "\n" + string.Join("\n", log));
        Selection.activeObject = parent;
        Debug.Log("SPIDER_GWEN2_WRAP_OK " + renderers.Length + " renderers\n" + string.Join("\n", log));
    }
}
