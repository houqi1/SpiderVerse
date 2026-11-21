using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GwenPainterlyToonSetup
{
    const string Root = "Assets/Texture/GwenPainterly";
    const string Report = "Temp/GwenPainterlyToon.report.txt";
    static SkinnedMeshRenderer[] SceneRenderers() => Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>()
        .Where(r => r.gameObject.scene.IsValid() && r.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(r)).ToArray();
    [MenuItem("Tools/Rendering/Apply Gwen Painterly Toon")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Apply in Edit mode.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shader/Toon.shader");
        var color = ImportAtlas("Gwen_Painterly_BaseColor_4K.png", true);
        var normal = ImportAtlas("Gwen_Painterly_NormalOS_4K.png", false);
        var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Model/Spider-Gwen.fbx");
        var prefabPath = Root + "/Spider-Gwen_PainterlyOS.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        var report = new StringBuilder();
        try
        {
            var sources = sourceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var targets = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var sceneRenderers = SceneRenderers().Where(r => r.gameObject.scene.path == "Assets/LineArt/LineArtPreview.unity").ToArray();
            foreach (var target in targets)
            {
                var source = sources.Single(r => r.name == target.name);
                var mats = target.sharedMaterials;
                if (mats.Length != source.sharedMaterials.Length) throw new Exception("Material slot mismatch: " + target.name);
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    // Existing variants already inherit the atlas setup and shared
                    // tuning. Reapplying the character must not flatten them.
                    if (mat.parent && AssetDatabase.GetAssetPath(mat.parent) == GwenPainterlyMaterialHierarchy.ParentPath)
                        continue;
                    // Preserve existing Toon tuning, while keeping the original UV0 materials usable.
                    CopyResolvedToonProperties(source.sharedMaterials[i], mat, shader);
                    mat.SetTexture("_BaseMap", color);
                    mat.SetTextureScale("_BaseMap", Vector2.one);
                    mat.SetTextureOffset("_BaseMap", Vector2.zero);
                    mat.SetFloat("_BaseMapUV", 1);
                    mat.SetTexture("_WrapNormalMap", normal);
                    mat.SetTextureScale("_WrapNormalMap", Vector2.one);
                    mat.SetTextureOffset("_WrapNormalMap", Vector2.zero);
                    mat.SetFloat("_WrapNormalUV", 1);
                    mat.SetFloat("_WrapNormalEnabled", 1);
                    mat.SetFloat("_WrapNormalStrength", 1);
                    EditorUtility.SetDirty(mat);
                }
                foreach (var r in sceneRenderers.Where(r => r.name == target.name))
                {
                    var mesh = PreserveOutlineColors(target.sharedMesh, r.sharedMesh);
                    Undo.RecordObject(r, "Apply Gwen painterly Toon");
                    r.sharedMesh = mesh;
                    // Preserve any extra outline/shell material slots appended to the renderer.
                    var sceneMats = r.sharedMaterials;
                    for (int i = 0; i < Math.Min(mats.Length, sceneMats.Length); i++) sceneMats[i] = mats[i];
                    r.sharedMaterials = sceneMats;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                    EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
                    report.AppendLine("Connected scene renderer: " + r.name);
                }
                target.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        AssetDatabase.SaveAssets();
        // Save only the requested character scene; retain every existing in-memory edit.
        var scene = SceneManager.GetSceneByPath("Assets/LineArt/LineArtPreview.unity");
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.SaveScene(scene);
        Validate(shader, color, normal, report);
        File.WriteAllText(Report, report.ToString());
        Debug.Log("GWEN_TOON_CONNECTED\n" + report);
        SceneView.RepaintAll();
    }
    static Texture2D ImportAtlas(string name, bool srgb)
    {
        string path = Root + "/" + name;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static void CopyResolvedToonProperties(Material source, Material target, Shader shader)
    {
        // Read inherited values explicitly: copying a Material Variant's serialized
        // override state into a standalone material can discard its texture bindings.
        var flat = new Material(shader) { name = target.name, shaderKeywords = source.shaderKeywords,
            renderQueue = source.renderQueue, enableInstancing = source.enableInstancing,
            doubleSidedGI = source.doubleSidedGI, globalIlluminationFlags = source.globalIlluminationFlags };
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string p = shader.GetPropertyName(i);
            if (!source.HasProperty(p)) continue;
            switch (shader.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color: flat.SetColor(p, source.GetColor(p)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector: flat.SetVector(p, source.GetVector(p)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    flat.SetTexture(p, source.GetTexture(p));
                    flat.SetTextureScale(p, source.GetTextureScale(p));
                    flat.SetTextureOffset(p, source.GetTextureOffset(p)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Int: flat.SetInteger(p, source.GetInteger(p)); break;
                default: flat.SetFloat(p, source.GetFloat(p)); break;
            }
        }
        EditorUtility.CopySerialized(flat, target);
        UnityEngine.Object.DestroyImmediate(flat);
    }
    static Mesh PreserveOutlineColors(Mesh atlas, Mesh current)
    {
        if (!current || current == atlas || current.colors32.Length != current.vertexCount) return atlas;
        // Secondary UV unwrapping splits vertices. Copy outline normals stored in colors
        // by original position, not vertex index, so existing silhouette rendering survives.
        var lookup = new System.Collections.Generic.Dictionary<Vector3, Color32>();
        var positions = current.vertices;
        var colors = current.colors32;
        for (int i = 0; i < positions.Length; i++) lookup[positions[i]] = colors[i];
        var result = new Color32[atlas.vertexCount];
        positions = atlas.vertices;
        for (int i = 0; i < positions.Length; i++)
            if (!lookup.TryGetValue(positions[i], out result[i])) throw new Exception("Outline vertex mismatch: " + current.name);
        atlas.colors32 = result;
        EditorUtility.SetDirty(atlas);
        return atlas;
    }
    static void Validate(Shader shader, Texture2D color, Texture2D normal, StringBuilder report)
    {
        bool async = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root + "/Materials" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat.shader != shader || mat.GetTexture("_BaseMap") != color || mat.GetTexture("_WrapNormalMap") != normal
                    || mat.GetFloat("_BaseMapUV") != 1 || mat.GetFloat("_WrapNormalUV") != 1 || mat.GetFloat("_WrapNormalEnabled") != 1)
                    throw new Exception("Incorrect atlas binding: " + mat.name);
                if (!mat.SetPass(0)) throw new Exception("Toon forward pass unavailable: " + mat.name);
                count++;
            }
            if (ShaderUtil.ShaderHasError(shader))
                throw new Exception(string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            report.AppendLine("PASS: " + count + " Toon materials; forward shader compiled; both atlases UV1; color sRGB; normal linear RGB.");
        }
        finally { ShaderUtil.allowAsyncCompilation = async; }
    }
}
