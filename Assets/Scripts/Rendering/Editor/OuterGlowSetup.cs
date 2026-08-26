#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class OuterGlowSetup
{
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string ShaderPath = "Assets/Shader/OuterGlow.shader";

    [MenuItem("SpiderVerse/Setup Outer Glow Feature")]
    public static void Setup()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
        if (renderer == null)
        {
            Debug.LogError($"Outer Glow setup: missing renderer at {RendererPath}");
            return;
        }

        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
            shader = Shader.Find("Hidden/Custom/OuterGlow");

        if (shader == null)
        {
            Debug.LogError("Outer Glow setup: OuterGlow shader not found.");
            return;
        }

        foreach (var feature in renderer.rendererFeatures)
        {
            if (feature is OuterGlowFeature existing)
            {
                existing.settings.shader = shader;
                EditorUtility.SetDirty(existing);
                EditorUtility.SetDirty(renderer);
                AssetDatabase.SaveAssets();
                Debug.Log("Outer Glow Feature already present; refreshed shader reference.");
                Selection.activeObject = renderer;
                return;
            }
        }

        var glow = ScriptableObject.CreateInstance<OuterGlowFeature>();
        glow.name = "OuterGlowFeature";
        glow.settings.shader = shader;
        int characterLayer = LayerMask.NameToLayer("Character");
        glow.settings.layerMask = characterLayer >= 0 ? 1 << characterLayer : 1 << 6;
        glow.settings.intensity = 1.2f;
        glow.settings.blurAmount = 0.035f;
        glow.settings.downsample = 1;

        AssetDatabase.AddObjectToAsset(glow, renderer);
        // ScriptableRendererData.rendererFeatures is not publicly mutable on all versions;
        // use SerializedObject so this works across URP 17.
        var so = new SerializedObject(renderer);
        var features = so.FindProperty("m_RendererFeatures");
        features.arraySize += 1;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = glow;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(renderer);
        EditorUtility.SetDirty(glow);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(RendererPath);

        Selection.activeObject = renderer;
        Debug.Log("Added OuterGlowFeature to PC_Renderer. Put Gwen on Character layer, add OuterGlowAnchor to her root, then tune glow settings.");
    }
}
#endif
