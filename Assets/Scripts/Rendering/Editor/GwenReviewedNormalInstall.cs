using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// File requests keep expensive offline baking separate from the live Unity editor.
[InitializeOnLoad]
public static class GwenReviewedNormalInstall
{
    public static string Work {
        get {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-gwenWork");return i>=0?args[i+1]:"Tools/VideoNormalPreview/UnitySkinning";}
    }
    public const string Destination = "Assets/Texture/GwenReviewedNormals";
    static bool busy;
    static GwenReviewedNormalInstall() { EditorApplication.update += Poll; }
    static async void Poll()
    {
        if(busy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var request = Work + "/command.request";
        if(!File.Exists(request)) return;
        busy = true;
        var command = File.ReadAllText(request).Trim(); File.Delete(request);
        try {
            if(command == "inspect") Inspect();
            else if(command == "apply") Apply();
            else if(command == "validate") await GwenReviewedNormalValidation.RunAsync();
            else if(command == "refresh") AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            else if(command == "quit" && Application.isBatchMode) EditorApplication.Exit(0);
            else throw new Exception("Unknown reviewed-normal command: " + command);
            File.WriteAllText(Work+"/"+command+".completed", DateTime.UtcNow.ToString("O"));
        } catch(Exception e) {File.WriteAllText(Work+"/"+command+".error", e.ToString());Debug.LogException(e);}
        finally {busy=false;}
    }
    static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent)+"/"+t.name : t.name;
    public static void BatchStart() {
        Directory.CreateDirectory(Work);
        EditorSceneManager.OpenScene("Assets/LineArt/LineArtPreview.unity");
        Inspect();File.WriteAllText(Work+"/ready.txt",Application.unityVersion);
    }
    [Serializable] public class Surface { public string name, hierarchy, meshPath; public int vertexCount; public Vector3[] positions,normals; public Vector4[] tangents; public Vector2[] uv0,uv1; public int[] triangles; public string[] materials,shaders; public bool active; }
    [Serializable] public class Inventory { public string selected,scene; public bool playing; public List<Surface> surfaces = new List<Surface>(); public List<string> animators=new List<string>(); }
    [Serializable] class BakedSurface {public string mesh,mesh_sha256;}
    [Serializable] class BakeManifest {public BakedSurface[] surfaces;}
    static void Inspect()
    {
        var data=new Inventory {selected=Selection.activeObject?Selection.activeObject.name:"",scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,playing=EditorApplication.isPlaying};
        foreach(var r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>().Where(r=>r.gameObject.scene.IsValid() && r.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(r))) {
            if(!r.name.Contains("spidergwen") && !r.name.Contains("head")) continue;
            var m=r.sharedMesh;
            data.surfaces.Add(new Surface{name=r.name,hierarchy=Hierarchy(r.transform),meshPath=AssetDatabase.GetAssetPath(m),vertexCount=m.vertexCount,positions=m.vertices,normals=m.normals,tangents=m.tangents,uv0=m.uv,uv1=m.uv2,triangles=m.triangles,materials=r.sharedMaterials.Select(x=>AssetDatabase.GetAssetPath(x)).ToArray(),shaders=r.sharedMaterials.Select(x=>x?x.shader.name:"").ToArray(),active=r.gameObject.activeInHierarchy});
        }
        foreach(var a in Resources.FindObjectsOfTypeAll<Animator>().Where(a=>a.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(a))) {
            data.animators.Add(Hierarchy(a.transform)+" | enabled="+a.enabled+" | controller="+AssetDatabase.GetAssetPath(a.runtimeAnimatorController)+" | clips="+(a.runtimeAnimatorController?string.Join(",",a.runtimeAnimatorController.animationClips.Select(c=>c.name+":"+c.length)):""));
        }
        File.WriteAllText(Work+"/inventory.json",JsonUtility.ToJson(data,true));
    }
    [MenuItem("Tools/Rendering/Install Reviewed Gwen Skinned Normals")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install in Edit mode.");
        Directory.CreateDirectory(Work+"/BeforeInstall");
        var bake=JsonUtility.FromJson<BakeManifest>(File.ReadAllText(Work+"/bake_report.json"));
        using(var sha=System.Security.Cryptography.SHA256.Create())
            foreach(var surface in bake.surfaces) {
                string meshPath="Assets/Texture/GwenPainterly/Meshes/"+surface.mesh.Replace(' ','_')+"_PainterlyUV.asset";
                string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(meshPath))).Replace("-","").ToLowerInvariant();
                if(hash!=surface.mesh_sha256)throw new Exception("Mesh changed after normal bake; rebake before installing: "+meshPath);
            }
        string texturePath=Destination+"/Gwen_Reviewed_NormalTS_4K.png";
        AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
        if(importer==null) throw new Exception("Missing reviewed tangent atlas.");
        importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.maxTextureSize=4096;importer.mipmapEnabled=true;importer.alphaSource=TextureImporterAlphaSource.None;importer.wrapMode=TextureWrapMode.Clamp;importer.anisoLevel=4;importer.isReadable=false;importer.SaveAndReimport();
        var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        string[] parents={"Assets/Texture/GwenPainterly/Gwen_Painterly_ToonBase.mat","Assets/Materials/SpiderGwen2/SpiderGwen2_WrapLambert_Parent.mat"};
        var report=new List<string>();
        foreach(var path in parents) {
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat) throw new Exception("Missing parent material: "+path);
            string backup=Work+"/BeforeInstall/"+Path.GetFileName(path);if(!File.Exists(backup))File.Copy(path,backup);
            Undo.RecordObject(mat,"Install reviewed skinned normals");
            if(mat.HasProperty("_WrapNormalTangentSpace")) {
                mat.SetTexture("_WrapNormalMap",tex);mat.SetFloat("_WrapNormalTangentSpace",1);mat.SetFloat("_WrapNormalEnabled",1);mat.SetFloat("_CelModelNormalEnabled",1);mat.SetFloat("_WrapNormalStrength",1);mat.SetFloat("_WrapNormalUV",1);mat.SetTextureScale("_WrapNormalMap",Vector2.one);mat.SetTextureOffset("_WrapNormalMap",Vector2.zero);
            } else if(mat.HasProperty("_NormalTangentSpace")) {
                mat.SetTexture("_ObjectNormalMap",tex);mat.SetFloat("_NormalTangentSpace",1);mat.SetFloat("_NormalStrength",1);mat.SetFloat("_FlipY",0);mat.SetFloat("_FlipZ",0);mat.DisableKeyword("_FLIP_Y");mat.DisableKeyword("_FLIP_Z");mat.SetTextureScale("_ObjectNormalMap",Vector2.one);mat.SetTextureOffset("_ObjectNormalMap",Vector2.zero);
            } else throw new Exception("Shader does not support skinned normals: "+mat.shader.name);
            EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);report.Add("Parent updated: "+path);
            // Existing variants inherit these normal properties. Keep their shader,
            // keywords and other overrides untouched; validation checks effective bindings.
            bool oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try {if(!mat.SetPass(0)||ShaderUtil.ShaderHasError(mat.shader))throw new Exception("Shader failed: "+string.Join("\n",ShaderUtil.GetShaderMessages(mat.shader).Select(x=>x.message)));}
            finally {ShaderUtil.allowAsyncCompilation=oldAsync;}
        }
        Inspect();File.WriteAllText(Work+"/install_report.txt",string.Join("\n",report)+"\nNo mesh, skeleton, controller, UV or skin-weight modifications.");SceneView.RepaintAll();
    }
}
