using System.IO;
using UnityEditor;
using UnityEngine;

// Right-click a texture -> "Bake CDF LUT". Writes <name>_CdfLut.png (512x1, linear, clamp) next to it.
// Samples the texture via GPU Blit so it matches exactly what the shader sees (incl. sRGB decode),
// and no Read/Write flag is needed on the source.
public static class CdfLutBaker
{
    const int Bins = 1024;   // histogram resolution
    const int LutSize = 512; // must match shader

    [MenuItem("Assets/Bake CDF LUT", true)]
    static bool Validate() => Selection.activeObject is Texture2D;

    [MenuItem("Assets/Bake CDF LUT")]
    static void Bake()
    {
        var src = (Texture2D)Selection.activeObject;
        string srcPath = AssetDatabase.GetAssetPath(src);

        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readback = new Texture2D(src.width, src.height, TextureFormat.RGBAFloat, false, true);
        readback.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        readback.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        var hist = new double[Bins];
        var px = readback.GetPixels();
        foreach (var c in px)
            hist[Mathf.Clamp(Mathf.FloorToInt(c.r * Bins), 0, Bins - 1)]++;
        Object.DestroyImmediate(readback);

        // Mid-rank CDF per bin: fraction strictly below + half of the bin itself (handles flat/tied regions).
        var cdf = new float[Bins];
        double acc = 0, n = px.Length;
        for (int i = 0; i < Bins; i++)
        {
            cdf[i] = (float)((acc + hist[i] * 0.5) / n);
            acc += hist[i];
        }

        var lut = new Texture2D(LutSize, 1, TextureFormat.RGBA32, false, true);
        for (int x = 0; x < LutSize; x++)
        {
            float v = Mathf.Pow(x / (LutSize - 1f), 2f); // LUT indexed by sqrt(v)
            float f = v * (Bins - 1);
            int i0 = Mathf.FloorToInt(f), i1 = Mathf.Min(i0 + 1, Bins - 1);
            float y = Mathf.Lerp(cdf[i0], cdf[i1], f - i0);
            lut.SetPixel(x, 0, new Color(y, y, y, 1));
        }
        lut.Apply();

        string outPath = Path.Combine(Path.GetDirectoryName(srcPath), Path.GetFileNameWithoutExtension(srcPath) + "_CdfLut.png");
        File.WriteAllBytes(outPath, lut.EncodeToPNG());
        Object.DestroyImmediate(lut);
        AssetDatabase.ImportAsset(outPath);

        var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
        imp.sRGBTexture = false;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        Debug.Log($"CDF LUT baked: {outPath}");
    }
}
