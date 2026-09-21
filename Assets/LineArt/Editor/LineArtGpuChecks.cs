#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpiderVerse.LineArt.Editor
{
    // Explicit batch entry point. Never opens/saves scenes in an interactive editor.
    public static class LineArtGpuChecks
    {
        public static void BuildBenchmark()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Library/LineArtValidation/"))throw new InvalidOperationException("Isolated project required");
            LineArtSetup.Tests();
            var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/LineArt/LineArt_Renderer.asset");var f=renderer.rendererFeatures.OfType<ObjectLineArtFeature>().First();
            f.geometryCompute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/LineArt/Shaders/ObjectLineArt.compute");f.gpuStrokeShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArtGpu.shader");EditorUtility.SetDirty(f);AssetDatabase.SaveAssets();
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.gpuSkinning=true;PlayerSettings.enableFrameTimingStats=true;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/LineArt/LineArtPreview.unity"},locationPathName="Validation/Player/LineArt.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            EditorApplication.Exit(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded?0:1);
        }
        static Camera camera;static RenderTexture target;static ObjectLineArtFeature feature;
        static int frames,stage;static double started;static bool error;static string gpuReport;
        public static void Run()
        {
            Debug.Log("GPU_CHECK_ENTRY");
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Library/LineArtValidation/"))throw new InvalidOperationException("Run only in the isolated LineArtValidation batch project.");
            Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)error=true;};
            SyntheticChecks();
            LineArtLayerChecks.RunFixtures();
            EditorSceneManager.OpenScene("Assets/LineArt/LineArtPreview.unity");
            Debug.Log("GPU_CHECK_SCENE_LOADED");
            var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/LineArt/LineArt_Renderer.asset");feature=renderer.rendererFeatures.OfType<ObjectLineArtFeature>().First();
            feature.geometryCompute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/LineArt/Shaders/ObjectLineArt.compute");feature.gpuStrokeShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArtGpu.shader");feature.executionMode=ObjectLineArtFeature.ExecutionMode.GpuGeometry;
            camera=Camera.main;target=new RenderTexture(960,960,24,RenderTextureFormat.ARGB32);target.Create();camera.aspect=1;
            foreach(var r in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)){r.vertexBufferTarget|=GraphicsBuffer.Target.Raw;r.updateWhenOffscreen=true;}
            frames=0;stage=0;started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            try {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});frames++;
                if((frames<30||stage==1&&feature.LastStats.StartsWith("GPU"))&&EditorApplication.timeSinceStartup-started<120)return;
                var shaders=ShaderUtil.GetShaderMessages(feature.gpuStrokeShader);foreach(var m in shaders)if(m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(m.message);
                Directory.CreateDirectory("Validation");string report=$"{SystemInfo.graphicsDeviceName}\n{feature.LastStats}\nframes={frames} seconds={EditorApplication.timeSinceStartup-started:F3} errors={error}\n";
                var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(960,960,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,960,960),0,0);image.Apply();File.WriteAllBytes(stage==0?"Validation/gpu-check.png":"Validation/cpu-check.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=old;
                if(stage==0){gpuReport=report+SkinCheck();File.WriteAllText("Validation/gpu-check.txt",gpuReport);stage=1;frames=0;started=EditorApplication.timeSinceStartup;feature.executionMode=ObjectLineArtFeature.ExecutionMode.CpuReference;return;}
                File.WriteAllText("Validation/gpu-check.txt",gpuReport+"\nCPU reference:\n"+report);
                Debug.Log("LINE_ART_GPU_CHECK "+feature.LastStats);EditorApplication.update-=Tick;if(!error&&Array.IndexOf(Environment.GetCommandLineArgs(),"-buildAfterChecks")>=0)BuildBenchmark();else EditorApplication.Exit(error?1:0);
            }catch(Exception e){Debug.LogException(e);EditorApplication.update-=Tick;EditorApplication.Exit(1);}
        }
        static string SkinCheck()
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
            var pass=typeof(ObjectLineArtFeature).GetField("pass",flags).GetValue(feature);
            var states=(System.Collections.IDictionary)pass.GetType().GetField("gpuCameras",flags).GetValue(pass);
            var state=states[camera.GetInstanceID()];if(state==null)return "No GPU state\n";
            var vertices=(GraphicsBuffer)typeof(LineArtGpu).GetField("vertices",flags).GetValue(state);var data=new Vector3[vertices.count];vertices.GetData(data);
            var sources=(System.Collections.IEnumerable)typeof(LineArtGpu).GetField("sources",flags).GetValue(state);var report=new System.Text.StringBuilder();var baked=new Mesh();
            foreach(var source in sources){var type=source.GetType();var renderer=type.GetField("renderer",flags).GetValue(source) as SkinnedMeshRenderer;if(!renderer)continue;int offset=(int)type.GetField("offset",flags).GetValue(source);renderer.BakeMesh(baked,true);var cpu=baked.vertices;float max=0;for(int i=0;i<cpu.Length;i++)max=Mathf.Max(max,Vector3.Distance(renderer.localToWorldMatrix.MultiplyPoint3x4(cpu[i]),data[offset+i]));report.AppendLine($"SKIN {renderer.name}: maxWorldError={max:R} gpu0={data[offset]} cpu0={renderer.localToWorldMatrix.MultiplyPoint3x4(cpu[0])}");
                if(max>1e-3f)throw new Exception($"GPU skin mismatch: {renderer.name}, error={max:R}");
            }
            UnityEngine.Object.DestroyImmediate(baked);return report.ToString();
        }
        static void SyntheticChecks()
        {
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/LineArt/Shaders/ObjectLineArt.compute");var stroke=AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArtGpu.shader");
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            for(int test=0;test<3;test++){
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var c=new GameObject("GPU geometry test camera").AddComponent<Camera>();c.orthographic=true;c.orthographicSize=3;c.transform.position=new Vector3(0,0,-5);c.aspect=1;
                var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.SetFloat("_Cull",0);
                var snapshots=new System.Collections.Generic.List<LineArtGeometry.Snapshot>();var targets=new System.Collections.Generic.List<Renderer>();var meshes=new System.Collections.Generic.List<Mesh>();
                void Add(Vector3[] points,int[] indices,bool draw){var mesh=new Mesh();mesh.vertices=points;mesh.triangles=indices;mesh.RecalculateBounds();meshes.Add(mesh);var go=new GameObject("Geometry");go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;if(draw)targets.Add(r);var top=LineArtGeometry.Prepare(points,new[]{indices});snapshots.Add(new LineArtGeometry.Snapshot{id=snapshots.Count,draw=draw,topology=top,vertices=top.representatives.Select(i=>points[i]).ToArray(),cull=new[]{0},opaque=new[]{true}});}
                if(test<2){Add(new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)},new[]{0,2,1,0,3,2},true);if(test==1)Add(new[]{new Vector3(0,-2,-1),new Vector3(2,-2,-1),new Vector3(2,2,-1),new Vector3(0,2,-1)},new[]{0,2,1,0,3,2},false);}
                else {Add(new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(0,1,0)},new[]{0,2,1},true);Add(new[]{new Vector3(0,-.5f,-1),new Vector3(0,-.5f,1),new Vector3(0,.5f,0)},new[]{0,1,2},true);}
                var source=new GameObject("Source").AddComponent<ObjectLineArtSource>();source.renderers=targets.ToArray();
                var settings=new LineArtSettings{contour=test<2,crease=false,materialBorders=false,boundaries=false,occlusion=test==1,intersections=test==2,thicknessCurve=false,lengthRandomness=0};
                var view=new LineArtGeometry.View{matrix=c.projectionMatrix*c.worldToCameraMatrix,position=c.transform.position,toCamera=-c.transform.forward,perspective=false,width=128,height=128};var expected=LineArtGeometry.Build(snapshots.ToArray(),view,settings,System.Threading.CancellationToken.None);
                using(var gpu=new LineArtGpu(shader,stroke))using(var cmd=new CommandBuffer()){
                    var rt=new RenderTexture(128,128,0);rt.Create();cmd.SetRenderTarget(rt);gpu.Prepare(c,settings);gpu.Execute(cmd,c,128,128,settings);Graphics.ExecuteCommandBuffer(cmd);
                    var buffer=(GraphicsBuffer)typeof(LineArtGpu).GetField("counters",flags).GetValue(gpu);var counters=new uint[8];buffer.GetData(counters);
                    if(counters[3]!=0||counters[0]!=expected.candidates||counters[1]!=expected.spans||counters[4]!=expected.intersections)throw new Exception($"GPU fixture {test}: {string.Join(",",counters)} vs CPU candidates={expected.candidates} spans={expected.spans} intersections={expected.intersections}");
                    if(test==2){settings.depthEpsilon*=2;c.transform.position+=new Vector3(.1f,0,0);cmd.Clear();cmd.SetRenderTarget(rt);gpu.Prepare(c,settings);gpu.Execute(cmd,c,128,128,settings);Graphics.ExecuteCommandBuffer(cmd);buffer.GetData(counters);if(counters[0]!=1||counters[4]!=1||counters[5]!=1||counters[3]!=0)throw new Exception("GPU cached intersection regression");Debug.Log("GPU_INTERSECTION_CACHE_PASSED");}
                    Debug.Log($"GPU_FIXTURE_{test}_PASSED candidates={counters[0]} spans={counters[1]} segments={counters[2]}");rt.Release();UnityEngine.Object.DestroyImmediate(rt);
                }
                foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(mat);
            }
        }
    }
}
#endif
