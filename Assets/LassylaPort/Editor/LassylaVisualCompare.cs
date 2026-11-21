using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

namespace LassylaPort.Editor
{
    public static class LassylaVisualCompare
    {
        static bool diagnostics;
        static bool rendered;
        static bool close;
        static bool skinnedSignals;
        static bool viewport,occlusion;
        public static void CaptureViewport(){viewport=true;close=true;try{CaptureRendered();}finally{viewport=false;close=false;}}
        public static void CaptureOcclusion(){occlusion=true;try{CaptureRendered();}finally{occlusion=false;}}
        public static void CaptureSkinnedSignals(){skinnedSignals=true;try{CaptureRendered();}finally{skinnedSignals=false;}}
        public static void CaptureClose(){close=true;try{CaptureRendered();}finally{close=false;}}
        public static void CaptureRendered(){rendered=true;try{Capture();}finally{rendered=false;}}
        public static void CaptureSignals(){diagnostics=true;try{Capture();}finally{diagnostics=false;}}
        static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
        static Matrix4x4 M(float[] v){var m=Matrix4x4.zero;for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=v[r*4+c];return m;}
        static float Halton(int i,int b){float f=1,r=0;while(i>0){f/=b;r+=f*(i%b);i/=b;}return r;}
        public static void Capture(){
            string folder=LassylaSetup.Work+"/visual";Directory.CreateDirectory(folder);
            var root=GameObject.Find("Spider-Gwen Lassyla");if(!root)throw new Exception("Missing Lassyla model");
            var parameters=LassylaSetup.ReadParameters();var originals=root.GetComponentsInChildren<SkinnedMeshRenderer>();
            var copies=new List<GameObject>();var materials=new List<Material>();
            var liveValues=new List<string>();
            Matrix4x4 blenderToUnity=Matrix4x4.identity;
            var cameraObject=new GameObject("Lassyla matched-view camera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var capture=cameraObject.AddComponent<LassylaCaptureCamera>();capture.measureLinearCoverage=true;capture.rawSample=!rendered||skinnedSignals;capture.samples=viewport?16:64;
            camera.fieldOfView=36;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<29;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var rt=new RenderTexture(512,640,24,RenderTextureFormat.ARGBFloat){antiAliasing=1};rt.Create();
            try{
                foreach(var src in originals){
                    var obj=rendered?UnityEngine.Object.Instantiate(src.gameObject):new GameObject(src.name+" comparison rest mesh");
                    obj.hideFlags=HideFlags.HideAndDontSave;obj.layer=29;copies.Add(obj);
                    obj.transform.SetPositionAndRotation(src.transform.position,src.transform.rotation);obj.transform.localScale=src.transform.lossyScale;
                    Renderer r;
                    if(rendered)r=obj.GetComponent<SkinnedMeshRenderer>();else{obj.AddComponent<MeshFilter>().sharedMesh=src.sharedMesh;r=obj.AddComponent<MeshRenderer>();}
                    var mat=new Material(src.sharedMaterial);mat.SetFloat("_DebugView",skinnedSignals?7:6);materials.Add(mat);
                    var keys=new[]{"_IOR","_NoiseScale","_NoiseDetail","_NoiseRoughness","_NoiseLacunarity","_NoiseDistortion","_EdgeStart","_EdgeEnd","_NoiseStart","_NoiseEnd","_NoiseDark"};
                    liveValues.Add(src.name+" "+string.Join(" ",keys.Select(k=>k+"="+mat.GetFloat(k).ToString("R"))));
                    var vals=new[]{parameters.ior,parameters.noiseScale,parameters.noiseDetail,parameters.noiseRoughness,parameters.noiseLacunarity,parameters.noiseDistortion,parameters.edgeRamp[0].position,parameters.edgeRamp[1].position,parameters.noiseRamp[0].position,parameters.noiseRamp[1].position,parameters.noiseRamp[0].color[0]};
                    if(!rendered)for(int k=0;k<keys.Length;k++)mat.SetFloat(keys[k],vals[k]);
                    r.sharedMaterials=Enumerable.Repeat(mat,src.sharedMesh.subMeshCount).ToArray();
                    var map=Matrix4x4.identity;for(int j=0;j<3;j++)map.SetRow(j,mat.GetVector("_BlenderLocal"+j));
                    blenderToUnity=obj.transform.localToWorldMatrix*map.inverse*M(parameters.meshes.First(x=>x.name==src.name).matrix).inverse;
                }
                File.WriteAllLines(folder+"/live-material-values.txt",liveValues);
                if(occlusion){
                    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.hideFlags=HideFlags.HideAndDontSave;cube.layer=29;copies.Add(cube);
                    cube.transform.position=blenderToUnity.MultiplyPoint3x4(new Vector3(0,-.4f,1.5f));cube.transform.localScale=new Vector3(.4f,.4f,.2f);
                    var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.SetColor("_BaseColor",Color.red);materials.Add(mat);cube.GetComponent<Renderer>().sharedMaterial=mat;
                }
                foreach(int angle in new[]{0,45,90,135,180,270}){
                    float a=angle*Mathf.Deg2Rad;var target=new Vector3(0,0,close?1.55f:.94f);float distance=close?1.1f:3.3f;var p=target+new Vector3(Mathf.Sin(a)*distance,-Mathf.Cos(a)*distance,close?.06f:.18f);
                    camera.transform.position=blenderToUnity.MultiplyPoint3x4(p);
                    camera.transform.rotation=Quaternion.LookRotation(blenderToUnity.MultiplyVector(target-p),blenderToUnity.MultiplyVector(Vector3.forward));
                    var sum=new Color[512*640];
                    if(rendered){
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        var oldTarget=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(512,640,TextureFormat.RGBAFloat,false,true);
                        tex.ReadPixels(new Rect(0,0,512,640),0,0);tex.Apply();RenderTexture.active=oldTarget;
                        string prefix=occlusion?"unity-occlusion-":(viewport?"unity-viewport-close-":(skinnedSignals?"unity-skinned-signals-":(close?"unity-renderer-close-":"unity-renderer-")));
                        Save(folder+"/"+prefix+angle,tex.GetPixels());UnityEngine.Object.DestroyImmediate(tex);if(occlusion)break;continue;
                    }
                    if(diagnostics){
                        foreach(int mode in new[]{7,4}){
                            foreach(var mat in materials)mat.SetFloat("_DebugView",mode);
                            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                            var oldTarget=RenderTexture.active;RenderTexture.active=rt;var diag=new Texture2D(512,640,TextureFormat.RGBAFloat,false,true);
                            diag.ReadPixels(new Rect(0,0,512,640),0,0);diag.Apply();RenderTexture.active=oldTarget;
                            Save(folder+(mode==7?"/unity-signals-":"/unity-generated-")+angle,diag.GetPixels());UnityEngine.Object.DestroyImmediate(diag);
                        }
                        File.WriteAllText(folder+"/material-values.txt",string.Join("\n",materials.Select(m=>m.name+" noiseScale="+m.GetFloat("_NoiseScale")+" IOR="+m.GetFloat("_IOR")+" noiseEnd="+m.GetFloat("_NoiseEnd"))));
                        continue;
                    }
                    for(int sample=0;sample<64;sample++){
                        foreach(var mat in materials)mat.SetFloat("_HashOffset",Halton(sample+1,7));
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(512,640,TextureFormat.RGBAFloat,false,true);
                        tex.ReadPixels(new Rect(0,0,512,640),0,0);tex.Apply();RenderTexture.active=old;var values=tex.GetPixels();
                        if(sample==0)Save(folder+"/unity-single-"+angle,values);
                        for(int i=0;i<sum.Length;i++)sum[i]+=values[i]/64f;
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                    Save(folder+"/unity-64-"+angle,sum);
                }
            }finally{foreach(var go in copies)UnityEngine.Object.DestroyImmediate(go);foreach(var mat in materials)UnityEngine.Object.DestroyImmediate(mat);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(cameraObject);}
            Debug.Log("LASSYLA_SIX_VIEWS_CAPTURED");
        }
        static void Save(string path,Color[] values){
            using(var b=new BinaryWriter(File.Open(path+".f32",FileMode.Create)))foreach(var c in values){b.Write(c.r);b.Write(c.g);b.Write(c.b);b.Write(c.a);}
            var png=new Texture2D(512,640,TextureFormat.RGBA32,false,false);png.SetPixels(values.Select(c=>c.gamma).ToArray());png.Apply();File.WriteAllBytes(path+".png",png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);
        }
    }
}
