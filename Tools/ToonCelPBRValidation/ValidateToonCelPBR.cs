// Run in the isolated ValidationProject with a staged copy of the shader.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ValidateToonCelPBR
{
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ToonCelPBRCheck/ToonCel.shader");
        if (!shader) throw new Exception("Staged shader missing");
        var material = new Material(shader);
        var variants = new ShaderVariantCollection();
        int checkedVariants = 0;
        string[][] lights = { Array.Empty<string>(), new[]{"_ADDITIONAL_LIGHTS"},
            new[]{"_ADDITIONAL_LIGHTS_VERTEX"}, new[]{"_FORWARD_PLUS", "_ADDITIONAL_LIGHTS"} };
        string[] shadows = { "", "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN" };
        foreach (bool specular in new[]{false,true})
        foreach (var light in lights)
        foreach (var shadow in shadows)
        foreach (bool cel in new[]{false,true})
        {
            var keywords = new List<string>(light);
            if (specular) keywords.Add("_PBR_SPECULAR");
            if (shadow.Length != 0) keywords.Add(shadow);
            if (cel) { keywords.Add("_CEL_THREE_TONE"); keywords.Add("_SHADE_FROM_AMBIENT"); }
            if (light.Length != 0) { keywords.Add("_ADDITIONAL_LIGHT_SHADOWS"); keywords.Add("_SHADOWS_SOFT"); }
            material.shaderKeywords = keywords.ToArray();
            variants.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline, keywords.ToArray()));
            if (!material.SetPass(0)) throw new Exception("SetPass failed: " + string.Join(",", keywords));
            checkedVariants++;
        }
        variants.WarmUp();
        foreach (string pass in new[]{"ShadowCaster", "DepthOnly", "MotionVectors"})
            if (!material.SetPass(material.FindPass(pass))) throw new Exception("Pass failed: " + pass);
        var messages = ShaderUtil.GetShaderMessages(shader);
        var errors = messages.Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
        if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(m => m.message)));
        string report = "Unity " + Application.unityVersion + " / " + SystemInfo.graphicsDeviceType +
            "\nForward variants checked: " + checkedVariants + "\nWarmed variants: " + variants.variantCount +
            "\nShadowCaster, DepthOnly, MotionVectors: PASS\nShader errors: 0\n" +
            string.Join("\n", messages.Select(m => m.severity + ": " + m.message));
        File.WriteAllText("F:/Projections/SpiderVerse/Tools/ToonCelPBRValidation/compile-result.txt", report);
        Debug.Log(report);
        UnityEngine.Object.DestroyImmediate(material);
    }
}
