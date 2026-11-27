using System;
using UnityEditor;
using UnityEngine;

// Custom/LambertRamp 的材质面板：用 Unity 的 Gradient 编辑器编辑，实时烘焙成 256x1 的 _RampMap（作为材质子资源保存）。
// Gradient 以 JSON 存在材质的 override tag 里（Material Variant 子材质会沿父链读取）。
public class LambertRampGUI : ShaderGUI
{
    const string TagGradient = "LambertRampGradient";
    const string TexName = "RampGradient";
    const int Res = 256;

    [Serializable] class GKey { public float t, r, g, b, a; }
    [Serializable] class GData { public int mode; public GKey[] colors; public GKey[] alphas; }

    Gradient gradient;
    Material gradientOwner;

    public override void OnGUI(MaterialEditor ed, MaterialProperty[] props)
    {
        var mat = (Material)ed.target;
        foreach (var p in props)
        {
            if ((p.flags & MaterialProperty.PropFlags.HideInInspector) != 0) continue;
            if (p.name == "_RampMap") { DrawGradient(ed, mat); continue; }
            ed.ShaderProperty(p, p.displayName);
        }
    }

    void DrawGradient(MaterialEditor ed, Material mat)
    {
        if (ed.targets.Length > 1)
        {
            EditorGUILayout.HelpBox("多选时不能编辑 Gradient，请单独选择材质。", MessageType.Info);
            return;
        }
        if (gradient == null || gradientOwner != mat) { gradient = LoadGradient(mat); gradientOwner = mat; }

        EditorGUI.BeginChangeCheck();
        var g = EditorGUILayout.GradientField(new GUIContent("Gradient", "横轴 = 归一化 Lambert"), gradient);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(mat, "Edit Ramp Gradient");
            gradient = g;
            SaveGradient(mat, g);
            Bake(mat, g);
        }
    }

    // 材质还没有 gradient 时写入一个默认值（暗 -> 亮）。
    public static void EnsureBaked(Material mat)
    {
        if (!string.IsNullOrEmpty(GetTag(mat, TagGradient)) && mat.GetTexture("_RampMap")) return;
        var g = LoadGradient(mat);
        SaveGradient(mat, g);
        Bake(mat, g);
    }

    static string GetTag(Material m, string key)
    {
        for (var x = m; x; x = x.parent)
        {
            var s = x.GetTag(key, false, "");
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return "";
    }

    static Gradient LoadGradient(Material mat)
    {
        string json = GetTag(mat, TagGradient);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var d = JsonUtility.FromJson<GData>(json);
                var g = new Gradient { mode = (GradientMode)d.mode };
                var ck = new GradientColorKey[d.colors.Length];
                for (int i = 0; i < ck.Length; i++)
                    ck[i] = new GradientColorKey(new Color(d.colors[i].r, d.colors[i].g, d.colors[i].b, 1), d.colors[i].t);
                var ak = new GradientAlphaKey[d.alphas.Length];
                for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(d.alphas[i].a, d.alphas[i].t);
                g.SetKeys(ck, ak);
                return g;
            }
            catch (Exception) { /* 落到默认值 */ }
        }
        var def = new Gradient();
        def.SetKeys(
            new[] { new GradientColorKey(new Color(0.3f, 0.3f, 0.4f), 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        return def;
    }

    static void SaveGradient(Material mat, Gradient g)
    {
        var d = new GData { mode = (int)g.mode, colors = new GKey[g.colorKeys.Length], alphas = new GKey[g.alphaKeys.Length] };
        for (int i = 0; i < d.colors.Length; i++)
        {
            var k = g.colorKeys[i];
            d.colors[i] = new GKey { t = k.time, r = k.color.r, g = k.color.g, b = k.color.b };
        }
        for (int i = 0; i < d.alphas.Length; i++)
        {
            var k = g.alphaKeys[i];
            d.alphas[i] = new GKey { t = k.time, a = k.alpha };
        }
        mat.SetOverrideTag(TagGradient, JsonUtility.ToJson(d));
        EditorUtility.SetDirty(mat);
    }

    static void Bake(Material mat, Gradient g)
    {
        var tex = GetOrCreateTexture(mat);
        var px = new Color[Res];
        for (int i = 0; i < Res; i++) px[i] = g.Evaluate(i / (Res - 1f));
        tex.SetPixels(px);
        tex.Apply(false);
        EditorUtility.SetDirty(tex);
        mat.SetTexture("_RampMap", tex);
        EditorUtility.SetDirty(mat);
    }

    static Texture2D GetOrCreateTexture(Material mat)
    {
        string path = AssetDatabase.GetAssetPath(mat);
        var cur = mat.GetTexture("_RampMap") as Texture2D;
        // 只复用属于这个材质本身的烘焙贴图（子材质不能改父材质的贴图）
        if (cur && cur.name == TexName && (string.IsNullOrEmpty(path) || AssetDatabase.GetAssetPath(cur) == path))
            return cur;

        var tex = new Texture2D(Res, 1, TextureFormat.RGBA32, false, false)
        {
            name = TexName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 0,
        };
        if (!string.IsNullOrEmpty(path)) AssetDatabase.AddObjectToAsset(tex, mat);
        else tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }
}
