using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace LassylaPort.Editor
{
    public static class LassylaStudioInstall
    {
        [MenuItem("Tools/Lassyla/Match Blender Studio and Edge Nodes")]
        public static void Install()
        {
            var parameters=LassylaSetup.ReadParameters();
            var root=GameObject.Find("Spider-Gwen Lassyla");
            var materials=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            var parent=AssetDatabase.LoadAssetAtPath<Material>(LassylaSetup.ParentPath);
            var backup=LassylaSetup.Work+"/before-studio-match";
            Directory.CreateDirectory(backup);
            foreach(var material in materials.Append(parent)){
                var path=Path.Combine(backup,material.name+".json");
                if(!File.Exists(path))File.WriteAllText(path,EditorJsonUtility.ToJson(material,true));
                Undo.RecordObject(material,"Match Blender reference nodes");
            }
            LassylaSetup.InstallEdgeRendering();
            parent.SetFloat("_ColorMix",parameters.colorMix);
            parent.SetFloat("_NormalMix",parameters.normalMix);
            foreach(var group in parameters.groups) parent.SetVector(group.name=="Group"?"_ColorGroup":"_NormalGroup",new Vector4(group.values[0],group.values[1],group.values[2],group.values[3]));
            foreach(var material in materials)
            {
                foreach(var name in new[]{"_IOR","_EdgeStart","_EdgeEnd","_NoiseStart","_NoiseEnd","_NoiseDark","_NoiseScale","_NoiseDetail","_NoiseDistortion","_NoiseRoughness","_NoiseLacunarity","_HashScale","_HashOffset","_NormalMix","_ColorMix","_NormalGroup","_ColorGroup","_DebugView"})
                    if(material.IsPropertyOverriden(name))material.RevertPropertyOverride(name);
                EditorUtility.SetDirty(material);
            }
            foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
                var material=renderer.sharedMaterial;var local=Matrix4x4.identity;
                for(int j=0;j<3;j++)local.SetRow(j,material.GetVector("_BlenderLocal"+j));
                var worldToSource=local*renderer.transform.worldToLocalMatrix;
                for(int j=0;j<3;j++)material.SetVector("_WorldToSourceMesh"+j,worldToSource.GetRow(j));
                EditorUtility.SetDirty(material);
            }
            const string folder="Assets/LassylaPort/Materials/";
            var probe=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"ForestDiffuse.asset");
            if(!probe){probe=new Texture2D(256,256,TextureFormat.RGBAHalf,false,true);AssetDatabase.CreateAsset(probe,folder+"ForestDiffuse.asset");}
            probe.LoadRawTextureData(File.ReadAllBytes(LassylaSetup.Work+"/forest-diffuse.rgba16"));
            probe.wrapModeU=TextureWrapMode.Repeat;probe.wrapModeV=TextureWrapMode.Clamp;probe.filterMode=FilterMode.Bilinear;probe.Apply();EditorUtility.SetDirty(probe);
            parent.SetTexture("_ForestDiffuse",probe);EditorUtility.SetDirty(parent);
            var rendererData=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/LineArt/LineArt_Renderer.asset");
            var feature=rendererData.rendererFeatures.OfType<LassylaEdgeFeature>().Single();
            feature.display=LoadCube(folder+"StudioFilm.asset","agx-film.rgba16");
            feature.inverseDisplay=LoadCube(folder+"StudioInverse.asset","agx-inverse.rgba16");
            feature.sharedMaterial=parent;
            feature.edgeCoordinateReference=AssetDatabase.LoadAssetAtPath<Material>(folder+"hero_spidergwen01_S03_body_white.mat");
            feature.matchTimelinePlayback=true;
            EditorUtility.SetDirty(feature);rendererData.SetDirty();
            AssetDatabase.SaveAssets();Selection.activeObject=parent;SceneView.RepaintAll();
        }
        static Texture3D LoadCube(string path,string data)
        {
            var cube=AssetDatabase.LoadAssetAtPath<Texture3D>(path);
            if(!cube){cube=new Texture3D(65,65,65,TextureFormat.RGBAHalf,false);AssetDatabase.CreateAsset(cube,path);}
            cube.SetPixelData(File.ReadAllBytes(LassylaSetup.Work+"/"+data),0);
            cube.wrapMode=TextureWrapMode.Clamp;cube.filterMode=FilterMode.Bilinear;cube.Apply();
            EditorUtility.SetDirty(cube);return cube;
        }
    }
}
