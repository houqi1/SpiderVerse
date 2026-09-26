#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Installs the motion-direction cache and fullscreen noise pass.</summary>
public static class MotionVectorDebugSetup
{
    const string MaterialPath = "Assets/Shader/MotionVectorDebug.mat";
    const string FeatureName = "Motion Vector Debug";

    [MenuItem("SpiderVerse/Enable Selected Character Motion Vectors")]
    public static void EnableSelectedCharacterMotionVectors()
    {
        var root = Selection.activeGameObject;
        if (root == null || EditorUtility.IsPersistent(root) || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Select a character root in the Hierarchy while in Edit Mode.");
            return;
        }

        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            Undo.RecordObject(renderer, "Enable Character Motion Vectors");
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            renderer.skinnedMotionVectors = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
        }

        Debug.Log($"Enabled per-object and skinned motion vectors on {renderers.Length} SkinnedMeshRenderer(s) under {root.name}. Save the scene to keep these settings.", root);
    }

    [MenuItem("SpiderVerse/Enable Selected Character Motion Vectors", true)]
    static bool CanEnableSelectedCharacterMotionVectors()
    {
        return Selection.activeGameObject != null &&
               !EditorUtility.IsPersistent(Selection.activeGameObject) &&
               !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem("SpiderVerse/Setup Motion Vector Debug")]
    public static void Setup()
    {
        // A selected renderer takes priority, for cameras overriding the pipeline default.
        var renderer = Selection.activeObject as UniversalRendererData;
        if (renderer == null)
            renderer = GetDefaultRenderer();

        if (renderer == null)
        {
            Debug.LogError("Motion Vector Debug: select a Universal Renderer Data asset first.");
            return;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null || material.shader == null)
        {
            Debug.LogError($"Motion Vector Debug: missing material or shader at {MaterialPath}.");
            return;
        }

        MotionVectorNoiseFeature feature = null;
        foreach (var candidate in renderer.rendererFeatures)
        {
            if (candidate is MotionVectorNoiseFeature noise &&
                (noise.name == FeatureName || noise.passMaterial == material))
            {
                feature = noise;
            }
            // Retain the old subasset for Undo, but prevent a second composition.
            if (candidate is FullScreenPassRendererFeature fullScreen && fullScreen.passMaterial == material)
            {
                Undo.RecordObject(fullScreen, "Migrate Motion Vector Debug");
                fullScreen.SetActive(false);
                EditorUtility.SetDirty(fullScreen);
            }
        }

        Undo.RecordObject(renderer, "Setup Motion Vector Debug");
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<MotionVectorNoiseFeature>();
            feature.name = FeatureName;
            AssetDatabase.AddObjectToAsset(feature, renderer);
            Undo.RegisterCreatedObjectUndo(feature, "Create Motion Vector Debug");
            renderer.rendererFeatures.Add(feature);
        }
        else
        {
            Undo.RecordObject(feature, "Configure Motion Vector Debug");
        }

        feature.passMaterial = material;
        feature.SetActive(true);

        // Last among passes at the same injection point.
        renderer.rendererFeatures.Remove(feature);
        renderer.rendererFeatures.Add(feature);
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(renderer));
        Selection.activeObject = renderer;
        EditorGUIUtility.PingObject(renderer);
        Debug.Log($"Motion Vector Debug enabled on {renderer.name}. Enter Play Mode and move the camera or an object. " +
                  "Set Update Mode / Update Rate on the feature; adjust noise on MotionVectorDebug.mat. Disable the feature to restore normal rendering.", renderer);
    }

    static UniversalRendererData GetDefaultRenderer()
    {
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
            return null;

        var serializedPipeline = new SerializedObject(pipeline);
        var renderers = serializedPipeline.FindProperty("m_RendererDataList");
        var defaultIndex = serializedPipeline.FindProperty("m_DefaultRendererIndex");
        if (renderers == null || defaultIndex == null ||
            defaultIndex.intValue < 0 || defaultIndex.intValue >= renderers.arraySize)
            return null;

        return renderers.GetArrayElementAtIndex(defaultIndex.intValue).objectReferenceValue as UniversalRendererData;
    }
}
#endif
