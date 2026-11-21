#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Shows only the glitch fields used by the selected Glitch Mode.</summary>
[CustomEditor(typeof(OuterGlowFeature))]
public class OuterGlowFeatureEditor : Editor
{
    static bool IsVisible(string name, OuterGlowFeature.GlitchMode mode)
    {
        bool slice = mode == OuterGlowFeature.GlitchMode.SliceGlitch;
        bool rgb = mode == OuterGlowFeature.GlitchMode.RGBFlicker;
        bool noise = mode == OuterGlowFeature.GlitchMode.NoiseDisplace;
        switch (name)
        {
            case "glitchDisplacement":
            case "glitchRGBSplit":
            case "glitchSliceHeight":
            case "glitchSliceDensity":
            case "glitchEchoOpacity":
                return slice;
            case "glitchBurstProbability":
                return slice;
            case "glitchUpdatesPerSecond":
                return true;
        }
        if (name.StartsWith("rgb")) return rgb;
        if (name.StartsWith("noise")) return noise;
        return true;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var settings = serializedObject.FindProperty("settings");
        var mode = (OuterGlowFeature.GlitchMode)settings.FindPropertyRelative("glitchMode").enumValueIndex;

        var prop = settings.Copy();
        var end = settings.GetEndProperty();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren) && !SerializedProperty.EqualContents(prop, end))
        {
            enterChildren = false;
            if (IsVisible(prop.name, mode))
                EditorGUILayout.PropertyField(prop, true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}
#endif
