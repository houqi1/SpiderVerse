using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;

public static class BuildGwenAtlas
{
    [Serializable] public class Surface {
        public string name; public Vector3[] positions, normals;
        public Vector2[] uv, uv0; public int[] triangles, materialIds;
        public string[] materials; public Vector3[] worldPositions;
        public Matrix4x4 localToWorld;
    }
    [Serializable] public class Data { public List<Surface> surfaces = new List<Surface>(); }
    public static void ValidatePaintedColor() {
        const string dest="Assets/GwenPainterly";
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(dest+"/ModelSpacePreview.shader",ImportAssetOptions.ForceUpdate);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(dest+"/ModelSpacePreview.shader");
        if(ShaderUtil.ShaderHasError(shader)) {
            foreach(var msg in ShaderUtil.GetShaderMessages(shader)) Debug.LogError(msg.message);
            throw new Exception("Painterly shader compile failed");
        }
        var colorPath=dest+"/Gwen_Painterly_BaseColor_4K.png";
        var color=AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        var ci=(TextureImporter)AssetImporter.GetAtPath(colorPath);
        var ni=(TextureImporter)AssetImporter.GetAtPath(dest+"/Gwen_Painterly_NormalOS_4K.png");
        if(!color || !ci.sRGBTexture || ni.sRGBTexture || ci.textureType!=TextureImporterType.Default || ni.textureType!=TextureImporterType.Default || color.width!=4096) throw new Exception("Invalid atlas import settings");
        int count=0;
        foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{dest+"/Materials"})) {
            var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if(mat.shader!=shader || mat.GetFloat("_ColorUsesUV1")!=1 || mat.GetTexture("_BaseMap")!=color) throw new Exception("Invalid color atlas binding: "+mat.name);
            count++;
        }
        Debug.Log("GWEN_COLOR_VALIDATED: "+count+" materials; 4K color sRGB ON; model-space normal sRGB OFF; shader OK");
    }
    public static void FinalizeAssets() {
        const string dest="Assets/GwenPainterly";
        var texPath=dest+"/Gwen_Painterly_NormalOS_4K.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(texPath);
        importer.textureType=TextureImporterType.Default;
        importer.sRGBTexture=false; importer.maxTextureSize=4096;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled=true; importer.wrapMode=TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        AssetDatabase.ImportAsset(dest+"/ModelSpacePreview.shader",ImportAssetOptions.ForceUpdate);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(dest+"/ModelSpacePreview.shader");
        if(!normal || !shader) throw new Exception("Missing preview assets");
        Directory.CreateDirectory(dest+"/Materials"); AssetDatabase.Refresh();
        var data=JsonUtility.FromJson<Data>(File.ReadAllText("atlas_meshes.json"));
        var prefabPath=dest+"/Spider-Gwen_PainterlyOS.prefab";
        var go=PrefabUtility.LoadPrefabContents(prefabPath);
        foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            var s=data.surfaces.Find(x=>x.name==r.name);
            if(s==null) { UnityEngine.Object.DestroyImmediate(r.gameObject); continue; }
            var mats=new Material[s.materials.Length];
            for(int i=0;i<mats.Length;i++) {
                var path=dest+"/Materials/"+s.materials[i]+"_Painterly.mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat) {
                    mat=new Material(shader); mat.name=s.materials[i]+"_Painterly";
                    mat.SetTexture("_NormalOS",normal);
                    mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceTextures/"+s.materials[i]+".png"));
                    AssetDatabase.CreateAsset(mat,path);
                }
                mats[i]=mat;
            }
            r.sharedMaterials=mats;
        }
        foreach(var animator in go.GetComponentsInChildren<Animator>(true)) { animator.enabled=false; animator.avatar=null; }
        PrefabUtility.SaveAsPrefabAsset(go,prefabPath);
        PrefabUtility.UnloadPrefabContents(go); AssetDatabase.SaveAssets();
        if(ShaderUtil.ShaderHasError(shader)) {
            foreach(var message in ShaderUtil.GetShaderMessages(shader)) Debug.LogError("GWEN_SHADER: "+message.message+" FILE "+message.file+" LINE "+message.line);
            throw new Exception("Preview shader has compile errors");
        }
        Debug.Log("GWEN_FINALIZE_COMPLETE");
    }
    public static void Run() {
        const string dest="Assets/GwenPainterly";
        Directory.CreateDirectory(dest+"/Meshes");
        AssetDatabase.Refresh();
        var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spider-Gwen.fbx");
        if(!model) throw new Exception("Missing source FBX");
        var go=UnityEngine.Object.Instantiate(model); go.name="Spider-Gwen_PainterlyOS";
        var data=new Data();
        foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            Vector4 rect;
            string name=r.name;
            if(name.Contains("body_white")) rect=new Vector4(0,0,.5f,.5f);
            else if(name.Contains("body_black")) rect=new Vector4(.5f,0,.5f,.5f);
            else if(name=="idle head") rect=new Vector4(0,.5f,.5f,.5f);
            else if(name.Contains("body_pink")) rect=new Vector4(.5f,.5f,.25f,.25f);
            else if(name=="wait head") rect=new Vector4(.75f,.5f,.25f,.25f);
            else if(name.Contains("body_wristband")) rect=new Vector4(.5f,.75f,.25f,.25f);
            else continue;
            var mesh=UnityEngine.Object.Instantiate(r.sharedMesh);
            mesh.name=name+"_PainterlyUV";
            UnwrapParam.SetDefaults(out var settings);
            settings.hardAngle=75; settings.packMargin=.015f;
            if(!Unwrapping.GenerateSecondaryUVSet(mesh,settings)) throw new Exception("Unwrap failed: "+name);
            var uv=mesh.uv2;
            for(int i=0;i<uv.Length;i++) uv[i]=new Vector2(rect.x+uv[i].x*rect.z,rect.y+uv[i].y*rect.w);
            mesh.uv2=uv;
            AssetDatabase.CreateAsset(mesh,dest+"/Meshes/"+mesh.name.Replace(' ','_')+".asset");
            r.sharedMesh=mesh;
            var ids=new List<int>(); var tris=new List<int>();
            for(int s=0;s<mesh.subMeshCount;s++) {
                var st=mesh.GetTriangles(s); tris.AddRange(st);
                for(int t=0;t<st.Length/3;t++) ids.Add(s);
            }
            var materials=new string[r.sharedMaterials.Length];
            for(int i=0;i<materials.Length;i++) materials[i]=r.sharedMaterials[i]?r.sharedMaterials[i].name:"";
            var wp=mesh.vertices; for(int i=0;i<wp.Length;i++) wp[i]=r.transform.TransformPoint(wp[i]);
            data.surfaces.Add(new Surface{name=name,positions=mesh.vertices,normals=mesh.normals,uv=uv,uv0=mesh.uv,triangles=tris.ToArray(),materialIds=ids.ToArray(),materials=materials,worldPositions=wp,localToWorld=r.localToWorldMatrix});
            // The two head variants occupy the same space; preview uses the unmasked one.
            if(name=="wait head") r.gameObject.SetActive(false);
            Debug.Log("ATLAS "+name+" vertices="+mesh.vertexCount+" bones="+mesh.bindposes.Length);
        }
        File.WriteAllText("atlas_meshes.json",JsonUtility.ToJson(data));
        PrefabUtility.SaveAsPrefabAsset(go,dest+"/Spider-Gwen_PainterlyOS.prefab");
        AssetDatabase.SaveAssets();
        Debug.Log("GWEN_ATLAS_COMPLETE");
    }
}
