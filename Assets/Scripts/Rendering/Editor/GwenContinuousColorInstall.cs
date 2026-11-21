using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class GwenContinuousColorInstall
{
    const string Work="Tools/GwenPainterly/UVBrushPass/Tip35_ContinuousBW15";
    const string PathBlack="Assets/Texture/GwenPainterly/Materials/hero_spidergwen01_S03_04_Painterly.mat";
    [Serializable] class TintManifest { public float[] old_base_color_srgb; }
    [MenuItem("Tools/Rendering/Apply Gwen Continuous Color Transition")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Use Edit mode.");
        if(!File.Exists(Work+"/UnityImport/installation.json"))throw new Exception("The revised atlas has not been installed.");
        var tint=JsonUtility.FromJson<TintManifest>(File.ReadAllText(Work+"/tint_bake.json"));
        AssetDatabase.ImportAsset(GwenPainterlyMaterialHierarchy.ParentPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(PathBlack, ImportAssetOptions.ForceUpdate);
        var black=AssetDatabase.LoadAssetAtPath<Material>(PathBlack);
        var parent=AssetDatabase.LoadAssetAtPath<Material>(GwenPainterlyMaterialHierarchy.ParentPath);
        if(!black||black.parent!=parent)throw new Exception("Unexpected black material hierarchy.");
        Color expected=new Color(tint.old_base_color_srgb[0],tint.old_base_color_srgb[1],tint.old_base_color_srgb[2],1);
        Color old=black.GetColor("_BaseColor");
        if(((Vector4)(parent.GetColor("_BaseColor")-Color.white)).sqrMagnitude>1e-10f)throw new Exception("Parent tint changed; do not apply baked tint.");
        // A reimported variant can already resolve to the parent's white tint.
        // Accept only the recorded old tint or the exact intended inherited tint.
        bool recordedTint=((Vector4)(old-expected)).sqrMagnitude<=1e-8f;
        bool inheritedTint=((Vector4)(old-parent.GetColor("_BaseColor"))).sqrMagnitude<=1e-8f;
        if(!recordedTint&&!inheritedTint)throw new Exception("Black tint changed since painting; actual="+old.ToString("F8")+" expected="+expected.ToString("F8"));
        string backup=Work+"/UnityImport/BlackMaterial_BeforeTintBake.mat";
        if(!File.Exists(backup)){File.Copy(PathBlack,backup);File.Copy(PathBlack+".meta",backup+".meta");}
        Undo.RecordObject(black,"Bake black tint into painterly colors");
        black.SetColor("_BaseColor",expected);
        black.RevertPropertyOverride("_BaseColor");
        EditorUtility.SetDirty(black);AssetDatabase.SaveAssetIfDirty(black);
        try
        {
            GwenPainterlyTextureImport.ImportAndValidate();
            if(black.parent!=parent||black.GetColor("_BaseColor")!=parent.GetColor("_BaseColor"))throw new Exception("Tint inheritance failed.");
            File.WriteAllText(Work+"/UnityImport/continuous_import.report.txt",
                "PASS: atlas and nine UV textures imported; black material inherits the common parent tint; previous black tint baked in linear color space; normals and shader retained.");
            SceneView.RepaintAll();Debug.Log("GWEN_CONTINUOUS_COLOR_INSTALLED");
        }
        catch { black.SetColor("_BaseColor",old);EditorUtility.SetDirty(black);AssetDatabase.SaveAssetIfDirty(black);throw; }
    }
}
