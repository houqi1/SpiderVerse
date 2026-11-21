using System.Collections.Generic;
using UnityEngine;

namespace LassylaPort
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class LassylaTemporalSampling : MonoBehaviour
    {
        static readonly HashSet<LassylaTemporalSampling> instances=new();
        static readonly List<Material> inspectedMaterials=new();
        static readonly int toonEdgeEnabled=Shader.PropertyToID("_LassylaEdgeEnabled");
        Renderer[] renderers;Transform[] bones;
        void OnEnable(){instances.Add(this);renderers=GetComponentsInChildren<Renderer>(true);bones=GetComponentsInChildren<Transform>(true);}
        readonly Dictionary<Renderer, Matrix4x4> meshMatrices=new();
        MaterialPropertyBlock coordinates;
        void LateUpdate()
        {
            if(renderers==null)return;
            foreach(var r in renderers)
            {
                if(!r || !r.sharedMaterial || !r.sharedMaterial.HasProperty("_WorldToSourceMesh0"))continue;
                var transformToMesh=r.transform.worldToLocalMatrix;
                if(meshMatrices.TryGetValue(r,out var last) && last==transformToMesh)continue;
                meshMatrices[r]=transformToMesh;
                var local=Matrix4x4.identity;
                for(int j=0;j<3;j++)local.SetRow(j,r.sharedMaterial.GetVector("_BlenderLocal"+j));
                var map=local*transformToMesh;
                coordinates??=new MaterialPropertyBlock();r.GetPropertyBlock(coordinates);
                for(int j=0;j<3;j++)coordinates.SetVector("_WorldToSourceMesh"+j,map.GetRow(j));
                r.SetPropertyBlock(coordinates);
            }
        }
        void OnDisable(){instances.Remove(this);}
        public static bool TryGetStudioBounds(out Bounds bounds)
        {
            bounds=default;bool found=false;
            foreach(var instance in instances){
                if(!instance || instance.renderers==null)continue;
                foreach(var r in instance.renderers){
                    if(!r || !r.enabled || !r.gameObject.activeInHierarchy)continue;
                    if(found)bounds.Encapsulate(r.bounds);else{bounds=r.bounds;found=true;}
                }
            }
            return found;
        }
        public static int SceneFingerprint(){
            unchecked{
                int hash=17;
                foreach(var instance in instances){
                    if(!instance)continue;
                    foreach(var bone in instance.bones)if(bone)hash=hash*31+bone.localToWorldMatrix.GetHashCode();
                    foreach(var r in instance.renderers)if(r){hash=hash*31+r.enabled.GetHashCode();foreach(var m in r.sharedMaterials)if(m)hash=hash*31+m.ComputeCRC().GetHashCode();}
                }
                hash=hash*31+RenderSettings.ambientProbe.GetHashCode();
                foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None)){
                    hash=hash*31+light.transform.localToWorldMatrix.GetHashCode();hash=hash*31+light.color.GetHashCode();
                    hash=hash*31+light.intensity.GetHashCode();hash=hash*31+light.range.GetHashCode();hash=hash*31+light.enabled.GetHashCode();
                }
                foreach(var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None)){
                    hash=hash*31+r.localToWorldMatrix.GetHashCode();hash=hash*31+r.enabled.GetHashCode();
                    r.GetSharedMaterials(inspectedMaterials);
                    bool toonEdge=false;
                    foreach(var m in inspectedMaterials){
                        if(!m || !m.HasProperty(toonEdgeEnabled) || m.GetFloat(toonEdgeEnabled)<.5f)continue;
                        hash=hash*31+m.ComputeCRC().GetHashCode();toonEdge=true;
                    }
                    if(toonEdge && r is SkinnedMeshRenderer skin){
                        foreach(var bone in skin.bones)if(bone)hash=hash*31+bone.localToWorldMatrix.GetHashCode();
                        if(skin.sharedMesh)for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)
                            hash=hash*31+skin.GetBlendShapeWeight(i).GetHashCode();
                    }
                }
                inspectedMaterials.Clear();
                return hash;
            }
        }
    }
}
