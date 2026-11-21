using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class GwenPainterlyMaterialHierarchy
{
    const string Root = "Assets/Texture/GwenPainterly";
    public const string ParentPath = Root + "/Gwen_Painterly_ToonBase.mat";
    const string Work = "Tools/GwenPainterly/MaterialHierarchy";
    const string Request = Work + "/apply.request";
    static GwenPainterlyMaterialHierarchy() { EditorApplication.delayCall += RunRequested; }
    static void RunRequested()
    {
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += RunRequested; return; }
        File.Move(Request, Work + "/apply.running");
        try { Apply(); File.Move(Work + "/apply.running", Work + "/apply.completed"); }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    sealed class Value
    {
        public string Name; public ShaderPropertyType Type; public float Float; public int Int;
        public Vector4 Vector; public Texture Texture; public Vector2 Scale, Offset;
        public Value(Material m, int i)
        {
            Name=m.shader.GetPropertyName(i); Type=m.shader.GetPropertyType(i);
            switch (Type)
            {
                case ShaderPropertyType.Texture: Texture=m.GetTexture(Name); Scale=m.GetTextureScale(Name); Offset=m.GetTextureOffset(Name); break;
                case ShaderPropertyType.Color: Vector=m.GetColor(Name); break;
                case ShaderPropertyType.Vector: Vector=m.GetVector(Name); break;
                case ShaderPropertyType.Int: Int=m.GetInteger(Name); break;
                default: Float=m.GetFloat(Name); break;
            }
        }
        public bool Matches(Material m)
        {
            switch (Type)
            {
                case ShaderPropertyType.Texture: return m.GetTexture(Name)==Texture && m.GetTextureScale(Name)==Scale && m.GetTextureOffset(Name)==Offset;
                case ShaderPropertyType.Color: return ((Vector4)m.GetColor(Name)-Vector).sqrMagnitude<1e-12f;
                case ShaderPropertyType.Vector: return (m.GetVector(Name)-Vector).sqrMagnitude<1e-12f;
                case ShaderPropertyType.Int: return m.GetInteger(Name)==Int;
                default: return Mathf.Abs(m.GetFloat(Name)-Float)<1e-6f;
            }
        }
        public void Set(Material m)
        {
            switch(Type)
            {
                case ShaderPropertyType.Texture: m.SetTexture(Name,Texture); m.SetTextureScale(Name,Scale); m.SetTextureOffset(Name,Offset); break;
                case ShaderPropertyType.Color: m.SetColor(Name,(Color)Vector); break;
                case ShaderPropertyType.Vector: m.SetVector(Name,Vector); break;
                case ShaderPropertyType.Int: m.SetInteger(Name,Int); break;
                default: m.SetFloat(Name,Float); break;
            }
        }
    }

    [MenuItem("Tools/Rendering/Restore Gwen Painterly Material Hierarchy")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Material hierarchy requires Edit mode.");
        var children=AssetDatabase.FindAssets("t:Material",new[]{Root+"/Materials"})
            .Select(id=>AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(id))).OrderBy(m=>m.name).ToArray();
        if(children.Length!=9) throw new Exception("Expected nine Painterly child materials.");
        var shader=children[0].shader;
        if(children.Any(m=>m.shader!=shader))throw new Exception("All parts must use the same Toon shader.");
        Directory.CreateDirectory(Work);
        string backup=Work+"/Before_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");Directory.CreateDirectory(backup);
        foreach(var m in children)
        {
            string path=AssetDatabase.GetAssetPath(m);
            File.Copy(path,backup+"/"+Path.GetFileName(path));
            File.Copy(path+".meta",backup+"/"+Path.GetFileName(path)+".meta");
        }
        var before=children.ToDictionary(m=>m,m=>Enumerable.Range(0,shader.GetPropertyCount()).Select(i=>new Value(m,i)).ToArray());
        var queues=children.ToDictionary(m=>m,m=>m.renderQueue);
        var keywords=children.ToDictionary(m=>m,m=>m.shaderKeywords.OrderBy(k=>k).ToArray());
        var parent=AssetDatabase.LoadAssetAtPath<Material>(ParentPath);
        if(!parent)
        {
            parent=new Material(children.Single(m=>m.name=="hero_spidergwen01_S03_01_Painterly"));
            parent.name="Gwen_Painterly_ToonBase";
            if(parent.parent)parent.parent=null;
            parent.SetTexture("_SpMap",null);
            AssetDatabase.CreateAsset(parent,ParentPath);
        }
        else
        {
            File.Copy(ParentPath,backup+"/"+Path.GetFileName(ParentPath));
            File.Copy(ParentPath+".meta",backup+"/"+Path.GetFileName(ParentPath)+".meta");
        }
        Undo.RecordObjects(children.Cast<UnityEngine.Object>().Append(parent).ToArray(),"Restore Gwen material inheritance");
        var report=new StringBuilder();report.AppendLine("Parent: "+ParentPath);report.AppendLine("Backup: "+backup);
        foreach(var m in children)
        {
            m.parent=parent;
            m.RevertAllPropertyOverrides();
            var differences=new List<string>();
            foreach(var value in before[m])if(!value.Matches(parent)){value.Set(m);differences.Add(value.Name);}
            if(m.renderQueue!=queues[m])m.renderQueue=queues[m];
            if(!m.shaderKeywords.OrderBy(k=>k).SequenceEqual(keywords[m]))m.shaderKeywords=keywords[m];
            foreach(var value in before[m])if(!value.Matches(m))throw new Exception("Appearance changed: "+m.name+" "+value.Name);
            if(m.parent!=parent)throw new Exception("Parent assignment failed: "+m.name);
            EditorUtility.SetDirty(m);
            report.AppendLine("PASS "+m.name+"; overrides: "+string.Join(", ",differences));
        }
        // Verify real propagation, then restore before saving any assets.
        float threshold=parent.GetFloat("_ShadeThreshold");
        try
        {
            parent.SetFloat("_ShadeThreshold",threshold+.03125f);
            foreach(var m in children)if(Mathf.Abs(m.GetFloat("_ShadeThreshold")-(threshold+.03125f))>1e-6f)
                throw new Exception("Shared shading parameter did not propagate: "+m.name);
        }
        finally { parent.SetFloat("_ShadeThreshold",threshold); }
        EditorUtility.SetDirty(parent);
        AssetDatabase.SaveAssetIfDirty(parent);
        foreach(var m in children)AssetDatabase.SaveAssetIfDirty(m);
        foreach(var m in children)foreach(var value in before[m])if(!value.Matches(m))throw new Exception("Post-save appearance mismatch.");
        report.AppendLine("PASS: nine variants, original effective shader properties preserved; parent shade-threshold edit propagates to all nine and was restored.");
        File.WriteAllText(Work+"/hierarchy.report.txt",report.ToString());
        Selection.activeObject=parent;EditorGUIUtility.PingObject(parent);SceneView.RepaintAll();
        Debug.Log("GWEN_MATERIAL_HIERARCHY_OK\n"+report);
    }
}
