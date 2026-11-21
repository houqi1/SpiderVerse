using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LassylaPort.Editor
{
    public static class LassylaColourCheck
    {
        [Serializable] sealed class SavedCamera
        {
            public float[] position,forward,up;
            public float fov;
            public int width,height;
        }
        static Vector3 Vector(float[] values)=>new Vector3(values[0],values[1],values[2]);
        [MenuItem("Tools/Lassyla/Match Blender Screenshot View")]
        public static void FocusReference()=>Focus("visual/color/saved-view/camera.json",1.43f);
        [MenuItem("Tools/Lassyla/Front Torso View")]
        public static void FocusTorso()=>Focus("visual/color/torso/camera-front.json",1.32f);
        internal static void FocusOblique()=>Focus("visual/color/torso/camera-oblique.json",1.32f);
        internal static void FocusHood()=>Focus("visual/color/torso/camera-head.json",1.60079f);
        static void Focus(string cameraFile,float pivotHeight)
        {
            var view=SceneView.lastActiveSceneView;
            var root=GameObject.Find("Spider-Gwen Lassyla");
            if(!root||!view)return;
            var material=root.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            var basis=Matrix4x4.identity;
            for(int i=0;i<3;i++)basis.SetRow(i,material.GetVector("_BlenderNormalToUnity"+i));
            var saved=JsonUtility.FromJson<SavedCamera>(File.ReadAllText(Path.Combine(LassylaSetup.Work,cameraFile)));
            var eye=basis.MultiplyPoint3x4(Vector(saved.position));
            var forward=basis.MultiplyVector(Vector(saved.forward)).normalized;
            var up=basis.MultiplyVector(Vector(saved.up)).normalized;
            float distance=Vector3.Distance(eye,basis.MultiplyPoint3x4(new Vector3(0,0,pivotHeight)));
            float halfFov=saved.fov*.5f*Mathf.Deg2Rad;
            float aspect=view.camera.aspect;
            // SceneView.GetVerticalFOV uses the smaller of the two viewport dimensions.
            float neutral=Mathf.Atan(Mathf.Tan(halfFov)*Mathf.Min(aspect,1));
            view.cameraSettings.fieldOfView=neutral*2*Mathf.Rad2Deg;
            view.LookAt(eye+forward*distance,Quaternion.LookRotation(forward,up),distance*Mathf.Sin(neutral),false,true);
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<Material>(LassylaSetup.ParentPath);
            view.Focus();view.Repaint();
        }
        public static void Capture(bool detail = false,bool savedView = false,bool liveOrbit = false)
        {
            var dir = Path.Combine(LassylaSetup.Work, "visual/color");
            if(detail)dir=Path.Combine(dir,"detail");
            int width=detail?1024:512,height=detail?768:640;
            SavedCamera saved=null;
            if(savedView){
                dir=Path.Combine(dir,"saved-view");
                saved=JsonUtility.FromJson<SavedCamera>(File.ReadAllText(Path.Combine(dir,"camera.json")));
                width=saved.width;height=saved.height;
            }
            if(liveOrbit){
                dir=Path.Combine(dir,"live");
                saved=JsonUtility.FromJson<SavedCamera>(File.ReadAllText(Path.Combine(dir,"camera-0.json")));
                width=saved.width;height=saved.height;
            }
            Directory.CreateDirectory(dir);
            var root = GameObject.Find("Spider-Gwen Lassyla");
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
            var savedBlocks=new Dictionary<SkinnedMeshRenderer,MaterialPropertyBlock>();
            foreach(var renderer in renderers){
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);savedBlocks.Add(renderer,block);
            }
            var diagnosticBlock=new MaterialPropertyBlock();
            var keys=new[]{"_IOR","_EdgeEnd","_NoiseStart","_NoiseEnd","_NoiseDark","_NoiseScale","_HashScale","_NormalMix","_ColorMix"};
            File.WriteAllLines(Path.Combine(dir,"unity-state.txt"),renderers.Select(r=>r.name+" block="+r.HasPropertyBlock()+" "+string.Join(" ",keys.Select(k=>k+"="+r.sharedMaterial.GetFloat(k)))+" group="+r.sharedMaterial.GetVector("_NormalGroup")));
            var basis = Matrix4x4.identity;
            for (int i=0; i<3; ++i) basis.SetRow(i, renderers[0].sharedMaterial.GetVector("_BlenderNormalToUnity"+i));
            var layers = renderers.ToDictionary(r=>r.gameObject,r=>r.gameObject.layer);
            var go = new GameObject("Matched Gwen camera") {hideFlags=HideFlags.HideAndDontSave};
            var cam = go.AddComponent<Camera>();
            var additional = cam.GetUniversalAdditionalCameraData();
            additional.renderPostProcessing=false;
            var options = go.AddComponent<LassylaCaptureCamera>();
            options.samples=16;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.nearClipPlane=.01f;cam.farClipPlane=1000;cam.fieldOfView=36;
            var rt = new RenderTexture(width,height,24,RenderTextureFormat.ARGBFloat){antiAliasing=1};
            rt.Create();
            var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
            var backdrop=GameObject.CreatePrimitive(PrimitiveType.Quad);backdrop.hideFlags=HideFlags.HideAndDontSave;backdrop.layer=29;backdrop.SetActive(false);
            var environment=new Texture2D(width,height,TextureFormat.RGBAHalf,false,true){filterMode=FilterMode.Point};
            var backdropMat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));backdropMat.SetTexture("_BaseMap",environment);
            backdrop.GetComponent<MeshRenderer>().sharedMaterial=backdropMat;
            var old=RenderTexture.active;
            try
            {
                foreach (int a in savedView?new[]{0}:detail?new[]{30,135}:new[]{0,45,90,135,180,270})
                {
                    if(liveOrbit)saved=JsonUtility.FromJson<SavedCamera>(File.ReadAllText(Path.Combine(dir,"camera-"+a+".json")));
                    float t=a*Mathf.Deg2Rad;
                    var target=new Vector3(0,0,1.44f);
                    float distance=detail?1.02f:1.44f;
                    var p=target+new Vector3(Mathf.Sin(t)*distance,-Mathf.Cos(t)*distance,.09f);
                    cam.transform.position=basis.MultiplyPoint3x4(p);
                    cam.transform.rotation=Quaternion.LookRotation(basis.MultiplyVector(target-p),basis.MultiplyVector(Vector3.forward));
                    if(saved!=null){
                        cam.fieldOfView=saved.fov;
                        cam.transform.position=basis.MultiplyPoint3x4(Vector(saved.position));
                        cam.transform.rotation=Quaternion.LookRotation(basis.MultiplyVector(Vector(saved.forward)),basis.MultiplyVector(Vector(saved.up)));
                    }
                    foreach(var kv in layers)kv.Key.layer=29;
                    cam.cullingMask=1<<29;
                    foreach(var label in savedView?new[]{"studio","studio-1","studio-4","studio-motion","city"}:detail||liveOrbit?new[]{"studio","city"}:new[]{"dark","light","edge","normal","cells","object","diffuse","black-display","studio","city"})
                    {
                        int mode=label=="edge"?7:label=="normal"?8:label=="cells"?9:label=="object"?10:label=="diffuse"?11:0;
                        options.measureLinearCoverage=label=="light" || label=="dark";
                        options.rawSample=mode!=0;
                        options.samples=label=="studio-1"?1:label=="studio-4"?4:16;
                        options.timelinePlayback=(savedView||liveOrbit) && (label=="studio" || label=="city");
                        if(options.timelinePlayback)options.samples=48;
                        bool studio=label.StartsWith("studio",StringComparison.Ordinal);
                        backdrop.SetActive(studio);
                        if(studio){
                            environment.LoadRawTextureData(File.ReadAllBytes(Path.Combine(dir,"studio-display-"+a+".rgba16")));environment.Apply();
                            backdrop.transform.SetPositionAndRotation(cam.transform.position+cam.transform.forward*4,cam.transform.rotation);
                            float quadHeight=8*Mathf.Tan(cam.fieldOfView*.5f*Mathf.Deg2Rad);backdrop.transform.localScale=new Vector3(quadHeight*width/height,quadHeight,1);
                        }
                        foreach(var renderer in renderers){
                            renderer.GetPropertyBlock(diagnosticBlock);diagnosticBlock.SetFloat("_DebugView",mode);
                            diagnosticBlock.SetFloat("_HashScale",label=="studio-motion"?1:renderer.sharedMaterial.GetFloat("_HashScale"));
                            renderer.SetPropertyBlock(diagnosticBlock);
                        }
                        cam.backgroundColor=label=="light"?Color.white:Color.black;
                        if(label=="city"){
                            foreach(var kv in layers)kv.Key.layer=kv.Value;
                            cam.cullingMask=~0;cam.clearFlags=CameraClearFlags.Skybox;
                        }else cam.clearFlags=CameraClearFlags.SolidColor;
                        RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();
                        File.WriteAllBytes(Path.Combine(dir,"unity-"+label+"-"+a+".f32"),tex.GetRawTextureData<byte>().ToArray());
                        RenderTexture.active=old;
                    }
                }
            }
            finally
            {
                foreach(var kv in layers)kv.Key.layer=kv.Value;
                foreach(var pair in savedBlocks)pair.Key.SetPropertyBlock(pair.Value.isEmpty?null:pair.Value);
                UnityEngine.Object.DestroyImmediate(backdrop);UnityEngine.Object.DestroyImmediate(backdropMat);UnityEngine.Object.DestroyImmediate(environment);
                RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
