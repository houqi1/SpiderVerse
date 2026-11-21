using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CharacterGlitchChecks
{
    [MenuItem("Tools/Rendering/Validate Character Glitch")]
    public static void Run()
    {
        const int size = 128;
        var shader = Shader.Find("Hidden/Custom/OuterGlow");
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException("OuterGlow shader is missing or unsupported.");
        var material = new Material(shader);
        var source = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
        var scene = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
        var readback = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
        var target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var pixels = new Color[size * size];
            for (int y = 32; y < 96; y++)
                for (int x = 48; x < 80; x++)
                    pixels[y * size + x] = Color.white;
            source.SetPixels(pixels);
            source.Apply();
            source.wrapMode = TextureWrapMode.Clamp;
            target.Create();
            material.SetVector("_GlitchParams", new Vector4(1f, 0.18f, 0.03f, 8f / size));
            material.SetVector("_GlitchTiming", new Vector4(7f, 1f, 1f, 1f));
            material.SetVector("_GlitchStyle", new Vector4(0.3f, 0f, 0f, 0f));
            var baseline = new Color(0.07f, 0.11f, 0.17f, 1f);
            var scenePixels = new Color[size * size];
            for (int i = 0; i < scenePixels.Length; i++)
                scenePixels[i] = pixels[i].a > 0f ? pixels[i] : baseline;
            scene.SetPixels(scenePixels);
            scene.Apply();

            Color[] Draw(bool rgb = false)
            {
                using (var cmd = new CommandBuffer())
                {
                    cmd.SetRenderTarget(target);
                    cmd.SetViewport(new Rect(0, 0, size, size));
                    cmd.ClearRenderTarget(false, true, baseline);
                    if (rgb) cmd.Blit(scene, target);
                    cmd.SetGlobalTexture("_BlitTexture", source);
                    cmd.SetGlobalVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
                    cmd.DrawProcedural(Matrix4x4.identity, material, material.FindPass(rgb ? "CharacterRGBFlicker" : "CharacterGlitch"), MeshTopology.Triangles, 3);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }

            bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.002f &&
                Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;
            var result = Draw();
            int outside = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if ((x < 47 || x > 80) && !Same(result[y * size + x], baseline)) outside++;
                    if ((y < 30 || y > 97) && !Same(result[y * size + x], baseline))
                        throw new InvalidOperationException("Glitch changed pixels outside the displaced capture coverage.");
                }
            if (outside == 0) throw new InvalidOperationException("Glitch did not extend beyond the original silhouette.");

            material.SetVector("_GlitchTiming", new Vector4(7f, 0f, 1f, 1f));
            foreach (var pixel in Draw())
                if (!Same(pixel, baseline)) throw new InvalidOperationException("Zero slice density must preserve the destination.");
            material.SetVector("_GlitchTiming", new Vector4(7f, 1f, 0f, 1f));
            foreach (var pixel in Draw())
                if (!Same(pixel, baseline)) throw new InvalidOperationException("Zero burst probability must preserve the destination.");
            material.SetVector("_GlitchTiming", new Vector4(7f, 1f, 1f, 1f));
            material.SetVector("_GlitchParams", new Vector4(0f, 0.18f, 0.03f, 8f / size));
            foreach (var pixel in Draw())
                if (!Same(pixel, baseline)) throw new InvalidOperationException("Zero opacity must preserve the destination.");

            // White noise remaps to +1 on both axes, so offsets equal the configured maxima.
            material.SetTexture("_RGBNoiseTex", Texture2D.whiteTexture);
            material.SetVector("_RGBNoiseParams", Vector4.zero);
            material.SetTexture("_RGBGateTex", Texture2D.whiteTexture);
            material.SetVector("_RGBGateParams", new Vector4(0f, 0f, 0f, 0.5f));
            material.SetFloat("_RGBActive", 1f);
            material.SetVector("_RGBOffsets", new Vector4(16f / size, 0f, -16f / size, 0f));
            result = Draw(true);
            if (!Same(result[64 * size + 50], new Color(0f, 1f, 1f, 1f)))
                throw new InvalidOperationException("RGB must replace the original body's red channel, not overlay a normal duplicate.");
            if (!Same(result[64 * size + 90], new Color(1f, 0f, 0f, 1f)))
                throw new InvalidOperationException("Red channel must extend outside the silhouette using original red color.");
            if (!Same(result[8 * size + 8], baseline))
                throw new InvalidOperationException("RGB changed unrelated background.");
            material.SetFloat("_RGBActive", 0f);
            result = Draw(true);
            for (int i = 0; i < result.Length; i++)
                if (!Same(result[i], scenePixels[i]))
                    throw new InvalidOperationException("An aligned RGB frame must exactly preserve the original scene.");

            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new InvalidOperationException(message.message);
            Debug.Log($"CharacterGlitchChecks PASS: {outside} slice pixels outside silhouette; RGB body replacement, outside color, background preservation, exact aligned frames verified.");
        }
        finally
        {
            RenderTexture.active = previous;
            ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(scene);
            UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
