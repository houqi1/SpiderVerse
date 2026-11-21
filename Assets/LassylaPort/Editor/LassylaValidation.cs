using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.Rendering;

namespace LassylaPort.Editor
{
    public static class LassylaValidation
    {
        [Serializable] public class Row {public float[] v;}
        [Serializable] class EdgeReference {public Row[] samples,nativeNoise,nativeNoiseRamp,nativeEEVEE,nativeBackface;}
        [Serializable] class GroupReference {public Row[] samples,group0Color,group0Position,group1Color,group1Position,lfColor,hfNoise;}
        [Serializable] class ValidationReport {
            public string unity,device,blenderSource="e13b728c6880";
            public int edgeSamples,groupSamples,visibleRenderers;
            public float noiseMaxError,noiseRampMaxError,frontFresnelMaxError,backFresnelMaxError,frontMaskMaxError,backMaskMaxError;
            public float[] groupMaxErrors;
            public bool edgePassed,groupsPassed,groupOutputsPassed,meshIntegrityPassed,shaderCompiled;
            public string precisionNote="groupsPassed includes a stricter CPU/GPU intermediate-noise comparison; groupOutputsPassed checks the actual group Color and Position outputs. Raw errors are retained.";
            public string scope="Material-node GPU comparison. Matched LookDev images and measured transmission profiles are in visual/color.";
        }
        static Vector4 Vec(Row r)=>new Vector4(r.v[0],r.v.Length>1?r.v[1]:0,r.v.Length>2?r.v[2]:0,r.v.Length>3?r.v[3]:0);
        static Vector4[] Dispatch(ComputeShader shader,string kernel,Row[] samples,int stride){
            int k=shader.FindKernel(kernel);
            using(var input=new ComputeBuffer(samples.Length,16))using(var output=new ComputeBuffer(samples.Length*stride,16)){
                input.SetData(samples.Select(Vec).ToArray());
                shader.SetBuffer(k,"Samples",input);shader.SetBuffer(k,"Results",output);
                shader.Dispatch(k,(samples.Length+63)/64,1,1);
                var result=new Vector4[samples.Length*stride];output.GetData(result);return result;
            }
        }
        static void SaveFloats(string filename,Vector4[] values){using(var w=new BinaryWriter(File.Open(filename,FileMode.Create)))foreach(var v in values){w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(v.w);}}
        static float Error(float actual,float expected){if(float.IsNaN(actual)||float.IsInfinity(actual))throw new Exception("Non-finite GPU output");return Mathf.Abs(actual-expected);}
        [MenuItem("Tools/Lassyla/Validate Against Blender Nodes")]
        public static void Run(){
            string w=LassylaSetup.Work;
            var reference=JsonUtility.FromJson<EdgeReference>(File.ReadAllText(w+"/native-edges.json"));
            var group=JsonUtility.FromJson<GroupReference>(File.ReadAllText(w+"/native-groups.json"));
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(LassylaSetup.Dir+"/Shaders/ValidateNodes.compute");
            var result=new ValidationReport{unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName,edgeSamples=reference.samples.Length*2,groupSamples=group.samples.Length};
            float e=1f/.3772728443145752f,n=1f/(.5772733688354492f-.23636361956596375f);
            shader.SetVector("RampMulBias",new Vector4(e,0,n,-n*.23636361956596375f));
            for(int side=0;side<2;side++){
                shader.SetFloat("FrontFace",side==0?1:0);
                var actual=Dispatch(shader,"EvaluateEdges",reference.samples,1);SaveFloats(w+"/unity-edges-"+side+".f32",actual);
                var gpu=side==0?reference.nativeEEVEE:reference.nativeBackface;
                for(int i=0;i<actual.Length;i++){
                    result.noiseMaxError=Mathf.Max(result.noiseMaxError,Error(actual[i].x,reference.nativeNoise[i].v[0]));
                    result.noiseRampMaxError=Mathf.Max(result.noiseRampMaxError,Error(actual[i].z,reference.nativeNoiseRamp[i].v[0]));
                    if(side==0){result.frontFresnelMaxError=Mathf.Max(result.frontFresnelMaxError,Error(actual[i].y,gpu[i].v[1]));result.frontMaskMaxError=Mathf.Max(result.frontMaskMaxError,Error(actual[i].w,gpu[i].v[2]));}
                    else{result.backFresnelMaxError=Mathf.Max(result.backFresnelMaxError,Error(actual[i].y,gpu[i].v[1]));result.backMaskMaxError=Mathf.Max(result.backMaskMaxError,Error(actual[i].w,gpu[i].v[2]));}
                }
            }
            var groups=Dispatch(shader,"EvaluateGroups",group.samples,6);SaveFloats(w+"/unity-groups.f32",groups);
            Row[][] refs={group.group0Color,group.group0Position,group.group1Color,group.group1Position,group.lfColor,group.hfNoise};
            result.groupMaxErrors=new float[6];
            for(int i=0;i<group.samples.Length;i++)for(int j=0;j<6;j++)for(int k=0;k<refs[j][i].v.Length;k++)result.groupMaxErrors[j]=Mathf.Max(result.groupMaxErrors[j],Error(groups[i*6+j][k],refs[j][i].v[k]));
            // Native EEVEE emission diagnostics are quantized in a half-float render buffer.
            result.edgePassed=result.noiseMaxError<.000002f&&result.noiseRampMaxError<.000003f&&result.frontFresnelMaxError<.00051f&&result.backFresnelMaxError<.00051f&&result.frontMaskMaxError<.00051f&&result.backMaskMaxError<.00051f;
            result.groupsPassed=result.groupMaxErrors.All(x=>x<.0001f);
            result.groupOutputsPassed=result.groupMaxErrors.Take(4).All(x=>x<.0001f);
            var material=AssetDatabase.LoadAssetAtPath<Material>(LassylaSetup.ParentPath);
            if(material){
                material.SetPass(0);
                var errors=ShaderUtil.GetShaderMessages(material.shader).Where(m=>m.severity==ShaderCompilerMessageSeverity.Error).ToArray();
                result.shaderCompiled=errors.Length==0;
                File.WriteAllText(w+"/shader-messages.txt",string.Join("\n",ShaderUtil.GetShaderMessages(material.shader).Select(x=>x.severity+" "+x.message+" line "+x.line)));
            }
            var root=GameObject.Find("Spider-Gwen Lassyla");
            if(root){
                result.visibleRenderers=root.GetComponentsInChildren<Renderer>().Length;
                var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Model/Spider-Gwen.fbx");
                result.meshIntegrityPassed=true;
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
                    var src=original.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(x=>x.name==r.name).sharedMesh;var dst=r.sharedMesh;
                    var generated=new List<Vector3>();dst.GetUVs(2,generated);
                    result.meshIntegrityPassed &= src.vertices.SequenceEqual(dst.vertices)&&src.triangles.SequenceEqual(dst.triangles)&&src.boneWeights.SequenceEqual(dst.boneWeights)&&src.bindposes.SequenceEqual(dst.bindposes)&&generated.Count==dst.vertexCount;
                }
            }
            File.WriteAllText(w+"/validation.json",JsonUtility.ToJson(result,true));
            Debug.Log("LASSYLA_NODE_VALIDATION "+JsonUtility.ToJson(result));
        }
        public static void RenderPreview(){
            var root=GameObject.Find("Spider-Gwen Lassyla");if(!root)throw new Exception("Lassyla instance missing");
            var renderers=root.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            File.WriteAllText(LassylaSetup.Work+"/preview-bounds.txt",string.Join("\n",renderers.Select(r=>r.name+" "+r.bounds)));
            var cameraObject=new GameObject("Lassyla Temporary Verification Camera");var camera=cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<LassylaCaptureCamera>();
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.renderShadows=true;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.15f,.18f,1);camera.fieldOfView=32;
            camera.nearClipPlane=.01f;camera.farClipPlane=100;
            var target=bounds.center;camera.transform.position=target+new Vector3(.2f,.08f,1).normalized*(bounds.size.y*1.9f);camera.transform.LookAt(target);
            var layers=new Dictionary<GameObject,int>();foreach(var t in root.GetComponentsInChildren<Transform>(true)){layers[t.gameObject]=t.gameObject.layer;t.gameObject.layer=30;}
            camera.cullingMask=1<<30;
            var rt=new RenderTexture(768,896,24,RenderTextureFormat.ARGBHalf){antiAliasing=1};rt.Create();
            var material=AssetDatabase.LoadAssetAtPath<Material>(LassylaSetup.ParentPath);float oldDebug=material.GetFloat("_DebugView");
            try{
                foreach(int mode in new[]{0,1,3,4}){
                    material.SetFloat("_DebugView",mode);
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                    var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
                    image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=previous;
                    // The RT contains linear values. Encode display sRGB explicitly for the PNG.
                    var colors=image.GetPixels();for(int j=0;j<colors.Length;j++)colors[j]=colors[j].gamma;
                    var png=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false,false);png.SetPixels(colors);png.Apply();
                    File.WriteAllBytes(LassylaSetup.Work+(mode==0?"/unity-preview.png":"/unity-diagnostic-"+mode+".png"),png.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(png);
                }
            }finally{material.SetFloat("_DebugView",oldDebug);foreach(var entry in layers)entry.Key.layer=entry.Value;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
