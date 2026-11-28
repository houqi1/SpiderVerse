using System;
using UnityEditor;
using UnityEngine;

// Custom/LambertRamp 的材质面板：用 Unity 的 Gradient 编辑器编辑，实时烘焙成 256x1 的 _RampMap（作为材质子资源保存）。
// Gradient 以 JSON 存在材质的 override tag 里（Material Variant 子材质会沿父链读取）。
public class LambertRampGUI : ShaderGUI
{
    const int Res = 256;

    // 一个 Gradient 对应：贴图属性名 / 存 JSON 的 tag / 烘焙贴图的名字 / 面板上的标签。
    class Ramp
    {
        public string Prop, Tag, TexName, Label;
        public Gradient Gradient; public Material Owner;
    }
    static readonly Ramp Main = new Ramp { Prop = "_RampMap", Tag = "LambertRampGradient", TexName = "RampGradient", Label = "Gradient" };
    static readonly Ramp Stripe = new Ramp { Prop = "_StripeRampMap", Tag = "LambertRampStripeGradient", TexName = "StripeRampGradient", Label = "Stripe Step 4: Final Gradient" };
    static readonly Ramp StripeLambert = new Ramp { Prop = "_StripeLambertRampMap", Tag = "LambertRampStripeLambertGradient", TexName = "StripeLambertRampGradient", Label = "Stripe Step 1: Lambert Gradient" };
    static readonly Ramp[] Ramps = { Main, Stripe, StripeLambert };

    [Serializable] class GKey { public float t, r, g, b, a; }
    [Serializable] class GData { public int mode; public GKey[] colors; public GKey[] alphas; }

    public override void OnGUI(MaterialEditor ed, MaterialProperty[] props)
    {
        var mat = (Material)ed.target;
        foreach (var p in props)
        {
            if ((p.flags & MaterialProperty.PropFlags.HideInInspector) != 0) continue;
            var ramp = System.Array.Find(Ramps, r => r.Prop == p.name);
            if (ramp != null) { DrawGradient(ed, mat, ramp); continue; }
            ed.ShaderProperty(p, p.displayName);
        }
    }

    void DrawGradient(MaterialEditor ed, Material mat, Ramp r)
    {
        if (ed.targets.Length > 1)
        {
            EditorGUILayout.HelpBox("多选时不能编辑 Gradient，请单独选择材质。", MessageType.Info);
            return;
        }
        if (r.Gradient == null || r.Owner != mat) { r.Gradient = LoadGradient(mat, r); r.Owner = mat; }
        // 还没烘焙过（比如新增的 Stripe ramp 还是默认白贴图）：先把默认渐变烘焙进去。
        if (string.IsNullOrEmpty(GetTag(mat, r.Tag)) || !mat.GetTexture(r.Prop))
        {
            SaveGradient(mat, r.Gradient, r);
            Bake(mat, r.Gradient, r);
        }

        EditorGUI.BeginChangeCheck();
        var g = EditorGUILayout.GradientField(new GUIContent(r.Label, "横轴 = 归一化 Lambert"), r.Gradient);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(mat, "Edit Ramp Gradient");
            r.Gradient = g;
            SaveGradient(mat, g, r);
            Bake(mat, g, r);
        }
    }

    // 材质还没有 gradient 时写入一个默认值（暗 -> 亮）。
    public static void EnsureBaked(Material mat)
    {
        if (!string.IsNullOrEmpty(GetTag(mat, Main.Tag)) && mat.GetTexture(Main.Prop)) return;
        var g = LoadGradient(mat, Main);
        SaveGradient(mat, g, Main);
        Bake(mat, g, Main);
    }

    // 按材质自身保存的 Gradient JSON 重新烘焙贴图（用于 Gradient 标签被还原/改动后同步贴图）。
    [MenuItem("Tools/LambertRamp/Rebake Gradients From Tags (selected materials)")]
    static void RebakeSelected()
    {
        int n = 0;
        foreach (var m in Selection.GetFiltered<Material>(SelectionMode.Assets))
        {
            if (m.shader == null || m.shader.name != "Custom/LambertRamp") continue;
            foreach (var r in Ramps)
            {
                if (string.IsNullOrEmpty(m.GetTag(r.Tag, false, ""))) continue; // 只处理自己持有 Gradient 的材质
                Undo.RecordObject(m, "Rebake Ramp Gradient");
                Bake(m, LoadGradient(m, r), r);
                n++;
            }
            AssetDatabase.SaveAssetIfDirty(m);
        }
        Debug.Log($"[LambertRamp] 已按标签重新烘焙 {n} 个 Gradient。");
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

    static Gradient LoadGradient(Material mat, Ramp r)
    {
        string json = GetTag(mat, r.Tag);
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

    static void SaveGradient(Material mat, Gradient g, Ramp r)
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
        mat.SetOverrideTag(r.Tag, JsonUtility.ToJson(d));
        EditorUtility.SetDirty(mat);
    }

    static void Bake(Material mat, Gradient g, Ramp r)
    {
        var tex = GetOrCreateTexture(mat, r);
        var px = new Color[Res];
        for (int i = 0; i < Res; i++) px[i] = g.Evaluate(i / (Res - 1f));
        tex.SetPixels(px);
        tex.Apply(false);
        EditorUtility.SetDirty(tex);
        mat.SetTexture(r.Prop, tex);
        EditorUtility.SetDirty(mat);
    }

    static Texture2D GetOrCreateTexture(Material mat, Ramp r)
    {
        string path = AssetDatabase.GetAssetPath(mat);
        var cur = mat.GetTexture(r.Prop) as Texture2D;
        // 只复用属于这个材质本身的烘焙贴图（子材质不能改父材质的贴图）
        if (cur && cur.name == r.TexName && (string.IsNullOrEmpty(path) || AssetDatabase.GetAssetPath(cur) == path))
            return cur;

        var tex = new Texture2D(Res, 1, TextureFormat.RGBA32, false, false)
        {
            name = r.TexName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 0,
        };
        if (!string.IsNullOrEmpty(path)) AssetDatabase.AddObjectToAsset(tex, mat);
        else tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }
}
