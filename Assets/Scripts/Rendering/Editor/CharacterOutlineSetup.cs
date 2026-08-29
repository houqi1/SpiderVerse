#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class CharacterOutlineSetup
{
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string OutlineShaderPath = "Assets/Shader/CharacterOutline.shader";
    const string MaskShaderPath = "Assets/Shader/CharacterOutlineMask.shader";

    [MenuItem("SpiderVerse/Setup Character Outline Feature")]
    public static void Setup()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
        if (renderer == null)
        {
            Debug.LogError($"Character Outline setup: missing renderer at {RendererPath}");
            return;
        }

        var outlineShader = AssetDatabase.LoadAssetAtPath<Shader>(OutlineShaderPath)
                            ?? Shader.Find("Hidden/Custom/CharacterOutline");
        var maskShader = AssetDatabase.LoadAssetAtPath<Shader>(MaskShaderPath)
                         ?? Shader.Find("Hidden/Custom/CharacterOutlineMask");

        if (outlineShader == null || maskShader == null)
        {
            Debug.LogError("Character Outline setup: outline/mask shader not found.");
            return;
        }

        foreach (var feature in renderer.rendererFeatures)
        {
            if (feature is CharacterOutlineFeature existing)
            {
                existing.settings.outlineShader = outlineShader;
                existing.settings.maskShader = maskShader;
                EditorUtility.SetDirty(existing);
                EditorUtility.SetDirty(renderer);
                AssetDatabase.SaveAssets();
                Debug.Log("Character Outline Feature already present; refreshed shader references.");
                Selection.activeObject = renderer;
                return;
            }
        }

        var outline = ScriptableObject.CreateInstance<CharacterOutlineFeature>();
        outline.name = "CharacterOutlineFeature";
        outline.settings.outlineShader = outlineShader;
        outline.settings.maskShader = maskShader;
        int characterLayer = LayerMask.NameToLayer("Character");
        outline.settings.layerMask = characterLayer >= 0 ? 1 << characterLayer : 1 << 6;
        outline.settings.layers = new System.Collections.Generic.List<CharacterOutlineFeature.OutlineLayer>
        {
            CharacterOutlineFeature.OutlineLayer.Default
        };
        outline.settings.legacyMigrated = true;
        outline.settings.downsample = 0;

        AssetDatabase.AddObjectToAsset(outline, renderer);
        var so = new SerializedObject(renderer);
        var features = so.FindProperty("m_RendererFeatures");
        features.arraySize += 1;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = outline;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(renderer);
        EditorUtility.SetDirty(outline);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(RendererPath);

        Selection.activeObject = renderer;
        Debug.Log(
            "Added CharacterOutlineFeature to PC_Renderer. " +
            "Put the character on the Character layer, add CharacterOutlineAnchor where the outline should expand from, then tune Outline Width / Color.");
    }
}
#endif
