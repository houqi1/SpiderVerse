using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LassylaPort.Editor
{
    public static class LassylaBodyCheck
    {
        const string Folder=LassylaSetup.Work+"/visual/color/torso";
        [Serializable] class View {public float[] position,forward,up;public float fov;public int width,height;}
        static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);

        public static void CaptureSceneTarget(string name)
        {
            var view=SceneView.lastActiveSceneView;
            void AfterCamera(ScriptableRenderContext context,Camera camera)
            {
                if(camera!=view.camera)return;
                RenderPipelineManager.endCameraRendering-=AfterCamera;
                var source=camera.targetTexture;
                if(!source)throw new InvalidOperationException("Scene camera has no output texture");
                var copy=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
                var cmd=CommandBufferPool.Get("Read actual Scene window");
                cmd.Blit(source,copy);
                cmd.RequestAsyncReadback(copy,r=>{
                    try{
                        if(!r.hasError)File.WriteAllBytes(Folder+"/scene-"+name+".rgba16",r.GetData<byte>().ToArray());
                    }finally{RenderTexture.ReleaseTemporary(copy);}
                });
                context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);context.Submit();
            }
            RenderPipelineManager.endCameraRendering+=AfterCamera;view.Repaint();
        }

        // Compare 64 ordinary camera frames to the verifier's 64 samples in a single frame.
        // This also catches a history texture being reallocated when its ping-pong role changes.
        public static void VerifyLiveHistory()
        {
            var hero=GameObject.Find("Spider-Gwen Lassyla");
            var material=hero.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            var basis=Matrix4x4.identity;
            for(int i=0;i<3;i++)basis.SetRow(i,material.GetVector("_BlenderNormalToUnity"+i));
            var v=JsonUtility.FromJson<View>(File.ReadAllText(Folder+"/camera-front.json"));
            var go=new GameObject("Lassyla temporal history check"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();
            camera.enabled=false;camera.nearClipPlane=.01f;camera.farClipPlane=1000;camera.fieldOfView=v.fov;
            camera.transform.SetPositionAndRotation(basis.MultiplyPoint3x4(V(v.position)),
                Quaternion.LookRotation(basis.MultiplyVector(V(v.forward)),basis.MultiplyVector(V(v.up))));
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var rt=new RenderTexture(v.width,v.height,24,RenderTextureFormat.ARGBFloat);rt.Create();
            var frame=new Texture2D(v.width,v.height,TextureFormat.RGBAHalf,false,true);
            var previous=RenderTexture.active;
            try{
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
                for(int i=1;i<=65;i++){
                    if(i==65){
                        var reference=go.AddComponent<LassylaCaptureCamera>();
                        reference.samples=64;reference.timelinePlayback=true;
                    }
                    RenderPipeline.SubmitRenderRequest(camera,request);
                    if(i!=1 && i!=64 && i!=65)continue;
                    RenderTexture.active=rt;frame.ReadPixels(new Rect(0,0,v.width,v.height),0,0);frame.Apply();
                    File.WriteAllBytes(Folder+"/history-"+(i==65?"reference":i.ToString())+".rgba16",frame.GetRawTextureData<byte>().ToArray());
                }
            }finally{
                RenderTexture.active=previous;rt.Release();
                foreach(var o in new UnityEngine.Object[]{go,frame,rt})UnityEngine.Object.DestroyImmediate(o);
            }
        }

        public static void SyncSharedIOR()
        {
            var root=GameObject.Find("Spider-Gwen Lassyla");
            var parent=AssetDatabase.LoadAssetAtPath<Material>(LassylaSetup.ParentPath);
            foreach(var mat in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct()){
                if(!mat || mat.parent!=parent || !mat.IsPropertyOverriden("_IOR"))continue;
                Undo.RecordObject(mat,"Use shared Blender Fresnel IOR");
                mat.RevertPropertyOverride("_IOR");EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
            }
            Selection.activeObject=parent;SceneView.RepaintAll();
        }

        public static void Capture()
        {
            var root=GameObject.Find("Spider-Gwen Lassyla");
            var parts=root.GetComponentsInChildren<SkinnedMeshRenderer>();
            var layers=parts.ToDictionary(r=>r.gameObject,r=>r.gameObject.layer);
            var basis=Matrix4x4.identity;
            for(int i=0;i<3;i++)basis.SetRow(i,parts[0].sharedMaterial.GetVector("_BlenderNormalToUnity"+i));
            var scene=SceneView.lastActiveSceneView;
            var state=new System.Text.StringBuilder();
            state.AppendLine($"Scene {scene.cameraMode.name} effects={scene.sceneViewState.imageEffectsEnabled} position={scene.camera.transform.position} FOV={scene.camera.fieldOfView} pixels={scene.camera.pixelWidth}x{scene.camera.pixelHeight} selection={Selection.activeObject?.name}");
            foreach(var part in parts){
                var props=new MaterialPropertyBlock();part.GetPropertyBlock(props);
                state.AppendLine($"{part.name} material={part.sharedMaterial.name} IOR={part.sharedMaterial.GetFloat("_IOR")} override={props.HasFloat("_IOR")} scale={part.sharedMaterial.GetFloat("_HashScale")} debug={part.sharedMaterial.GetFloat("_DebugView")}");
            }
            var go=new GameObject("Front surface comparison"){hideFlags=HideFlags.HideAndDontSave};
            var cam=go.AddComponent<Camera>();cam.nearClipPlane=.01f;cam.farClipPlane=1000;
            var data=cam.GetUniversalAdditionalCameraData();
            var opts=go.AddComponent<LassylaCaptureCamera>();opts.samples=64;opts.timelinePlayback=true;
            var v0=JsonUtility.FromJson<View>(File.ReadAllText(Folder+"/camera-front.json"));
            var rt=new RenderTexture(v0.width,v0.height,24,RenderTextureFormat.ARGBFloat);rt.Create();
            var read=new Texture2D(rt.width,rt.height,TextureFormat.RGBAHalf,false,true);
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.hideFlags=HideFlags.HideAndDontSave;quad.layer=29;
            var ink=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var env=new Texture2D(rt.width,rt.height,TextureFormat.RGBAHalf,false,true){filterMode=FilterMode.Point};
            quad.GetComponent<Renderer>().sharedMaterial=ink;
            var old=RenderTexture.active;
            try{
                foreach(var name in new[]{"front","head","oblique"}){
                    var v=JsonUtility.FromJson<View>(File.ReadAllText(Folder+"/camera-"+name+".json"));
                    cam.transform.SetPositionAndRotation(basis.MultiplyPoint3x4(V(v.position)),Quaternion.LookRotation(basis.MultiplyVector(V(v.forward)),basis.MultiplyVector(V(v.up))));
                    cam.fieldOfView=v.fov;
                    float h=8*Mathf.Tan(v.fov*Mathf.Deg2Rad*.5f);
                    quad.transform.SetPositionAndRotation(cam.transform.position+cam.transform.forward*4,cam.transform.rotation);
                    quad.transform.localScale=new Vector3(h*v.width/v.height,h,1);
                    foreach(var label in new[]{"studio","black","white","pink","city","city-empty"}){
                        bool city=label.StartsWith("city"),empty=label=="city-empty";quad.SetActive(!city);
                        foreach(var pair in layers)pair.Key.layer=city&&!empty?pair.Value:29;
                        cam.cullingMask=empty?~(1<<29):city?~0:1<<29;cam.clearFlags=city?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;
                        if(!city){
                            env.LoadRawTextureData(File.ReadAllBytes(Folder+"/board-"+name+"-"+label+".rgba16"));env.Apply();
                            ink.SetTexture("_BaseMap",env);
                        }
                        foreach(bool post in new[]{false,true}){
                            data.renderPostProcessing=post;
                            RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                            RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);read.Apply();
                            File.WriteAllBytes(Folder+"/unity-"+name+"-"+label+(post?"-post":"")+".rgba16",read.GetRawTextureData<byte>().ToArray());
                        }
                    }
                }
            }finally{
                foreach(var pair in layers)pair.Key.layer=pair.Value;
                RenderTexture.active=old;rt.Release();
                foreach(var o in new UnityEngine.Object[]{go,quad,ink,env,read,rt})UnityEngine.Object.DestroyImmediate(o);
                File.WriteAllText(Folder+"/actual-state.txt",state.ToString());
            }
        }
    }
}
