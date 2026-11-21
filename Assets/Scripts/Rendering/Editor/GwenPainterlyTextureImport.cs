using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Imports the reviewed texture revision without rebuilding materials or scenes.
public static class GwenPainterlyTextureImport
{
    const string Root = "Assets/Texture/GwenPainterly";
    [MenuItem("Tools/Rendering/Import Reviewed Gwen Textures")]
    public static void ImportAndValidate()
    {
        string colorPath = Root + "/Gwen_Painterly_BaseColor_4K.png";
        string normalPath = Root + "/Gwen_Painterly_NormalOS_4K.png";
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(colorPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var sources = Directory.GetFiles(Root + "/SourceUV", "*.png").OrderBy(p => p).ToArray();
        if (sources.Length != 9) throw new Exception("Expected nine reviewed UV paintings.");
        foreach (string file in sources)
        {
            string path = file.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new Exception("Missing texture importer: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        var ci = AssetImporter.GetAtPath(colorPath) as TextureImporter;
        var ni = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (!color || color.width != 4096 || color.height != 4096 || !ci.sRGBTexture
            || ci.textureCompression != TextureImporterCompression.Uncompressed)
            throw new Exception("Incorrect base color import.");
        if (!normal || ni.sRGBTexture || ni.textureType != TextureImporterType.Default)
            throw new Exception("Object-space normal must retain linear RGB import.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shader/Toon.shader");
        var report = new StringBuilder();
        var ids = AssetDatabase.FindAssets("t:Material", new[] { Root + "/Materials" });
        if (ids.Length != 9) throw new Exception("Expected nine existing Toon materials.");
        foreach (var id in ids)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(id));
            if (mat.shader != shader || mat.GetTexture("_BaseMap") != color
                || mat.GetTexture("_WrapNormalMap") != normal || mat.GetFloat("_BaseMapUV") != 1
                || mat.GetFloat("_WrapNormalUV") != 1 || mat.GetFloat("_WrapNormalEnabled") != 1)
                throw new Exception("Existing Toon binding mismatch: " + mat.name);
            report.AppendLine("PASS material: " + mat.name);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Spider-Gwen_PainterlyOS.prefab");
        if (!prefab) throw new Exception("Missing painterly character prefab.");
        foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = renderer.sharedMesh;
            if (!mesh || mesh.uv2.Length != mesh.vertexCount)
                throw new Exception("Missing atlas UV1: " + renderer.name);
        }
        report.AppendLine("PASS: 4096x4096 sRGB base atlas, nine source UV textures, linear object-space normal, existing Toon bindings and atlas UV1.");
        string output = "Tools/GwenPainterly/UVBrushPass/Tip35_LayeredBW5/UnityImport";
        Directory.CreateDirectory(output);
        File.WriteAllText(output + "/UnityImport.report.txt", report.ToString());
        Debug.Log("GWEN_REVIEWED_TEXTURE_IMPORT_OK\n" + report);
    }
}
