#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpiderVerse.LineArt.Editor
{
    public static class LineArtLayerChecks
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object value,string name)=>value.GetType().GetField(name,Flags).GetValue(value);
        static void Require(bool condition,string label){if(!condition)throw new Exception("Layer check: "+label);}
        static uint[] Counters(LineArtGpu gpu){var data=new uint[8];((GraphicsBuffer)Field(gpu,"counters")).GetData(data);return data;}
        static LineArtLayer Layer(string name,Renderer[] renderers,Color color)=>new LineArtLayer{name=name,useSourceRenderers=false,renderers=renderers,appearance=new LineArtAppearance{scaleWithDistance=false,color=color,thickness=5,thicknessCurve=false,lengthRandomness=0}};
        static Renderer MeshObject(Vector3[] vertices,int[] indices,Material material,List<Mesh> cleanup)
        {
            var mesh=new Mesh();mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateBounds();cleanup.Add(mesh);
            var go=new GameObject("Layer fixture");go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;return renderer;
        }
        public static void RunFixtures()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Library/LineArtValidation/"))throw new InvalidOperationException("Isolated validation project required");
            var rendererAsset=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>("Assets/LineArt/LineArt_Renderer.asset");
            if(!File.ReadAllText("Assets/LineArt/LineArt_Renderer.asset").Contains("noiseFrequency:"))foreach(var feature in rendererAsset.rendererFeatures)if(feature is ObjectLineArtFeature f)Require(f.settings.noiseFrequency==3,"legacy noise defaults to three cycles");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Layers test camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=3;camera.aspect=1;camera.transform.position=new Vector3(0,0,-5);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetFloat("_Cull",0);var meshes=new List<Mesh>();
            Renderer Quad(float x)=>MeshObject(new[]{new Vector3(x-.65f,-1,0),new Vector3(x+.65f,-1,0),new Vector3(x+.65f,1,0),new Vector3(x-.65f,1,0)},new[]{0,2,1,0,3,2},material,meshes);
            var left=Quad(-1.3f);var right=Quad(1.3f);var source=new GameObject("Layer Source").AddComponent<ObjectLineArtSource>();source.renderers=new[]{left,right};
            var settings=new LineArtSettings{crease=false,materialBorders=false,intersections=false,occlusion=false,thicknessCurve=false,lengthRandomness=0};
            var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/LineArt/Shaders/ObjectLineArt.compute");var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArtGpu.shader");
            var target=new RenderTexture(256,256,0);target.Create();
            using(var gpu=new LineArtGpu(compute,shader))using(var cmd=new CommandBuffer())
            {
                void Render(){cmd.Clear();cmd.SetRenderTarget(target);cmd.ClearRenderTarget(false,true,Color.black);cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));Require(gpu.Prepare(camera,settings),"GPU prepare");gpu.Execute(cmd,camera,256,256,settings);Graphics.ExecuteCommandBuffer(cmd);if(gpu.LayerCount>0)Require(Counters(gpu)[3]==0,"no overflow");}
                Render();Require(Counters(gpu)[0]==8&&gpu.StrokeSetCount==1,"legacy selection preserved");int updates=gpu.GeometryUpdateCount,builds=gpu.StrokeBuildCount;
                source.layers=new[]{Layer("red",source.renderers,Color.red),Layer("blue",source.renderers,Color.blue),Layer("green",source.renderers,Color.green)};
                Render();Require(gpu.LayerCount==3&&gpu.StrokeSetCount==1&&gpu.GeometryUpdateCount==updates&&gpu.StrokeBuildCount==builds,"three appearances share geometry and ribbons");
                int ColoredPixels(Color color){var old=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(256,256,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,256,256),0,0);texture.Apply();RenderTexture.active=old;int count=0;foreach(var pixel in texture.GetPixels())if((pixel-color).maxColorComponent<.05f&&Mathf.Abs(pixel.r-color.r)+Mathf.Abs(pixel.g-color.g)+Mathf.Abs(pixel.b-color.b)<.1f)count++;UnityEngine.Object.DestroyImmediate(texture);return count;}
                Require(ColoredPixels(Color.green)>30,"last layer is on top");Array.Reverse(source.layers);Render();Require(ColoredPixels(Color.red)>30&&gpu.GeometryUpdateCount==updates&&gpu.StrokeBuildCount==builds,"layer reordering changes compositing without geometry work");Array.Reverse(source.layers);
                source.layers[0].appearance.thickness=9;source.layers[0].appearance.offset=new Vector2(2,0);Render();
                Require(gpu.GeometryUpdateCount==updates&&gpu.StrokeBuildCount==builds,"uniform edits do not dispatch geometry");
                source.layers[0].appearance.scaleWithDistance=true;source.layers[0].appearance.sizeUnit=.008f;Render();
                Require(gpu.GeometryUpdateCount==updates&&gpu.StrokeBuildCount==builds,"world sizing remains a uniform-only edit");
                source.layers[0].appearance.scaleWithDistance=false;Render();
                var draws=((LineArtLayers)Field(gpu,"layers")).draws;Require(draws[0].properties.GetColor("_Color")==Color.red&&draws[1].properties.GetColor("_Color")==Color.blue,"independent property blocks");
                source.layers=new[]{Layer("left",new[]{left},Color.red),Layer("right",new[]{right},Color.blue)};Render();
                Require(gpu.StrokeSetCount==2&&gpu.GeometryUpdateCount==updates,"selection splits reuse extraction");
                foreach(var batch in (IEnumerable)Field(gpu,"batches")){var args=new uint[4];((GraphicsBuffer)Field(batch,"arguments")).GetData(args);Require(args[0]==24&&args[1]==1,"each layer draws only four selected edges");}
                var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(256,256,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();RenderTexture.active=previous;
                int red=0,blue=0,wrong=0;var pixels=image.GetPixels32();for(int y=0;y<256;y++)for(int x=0;x<256;x++){var p=pixels[y*256+x];if(p.r>180&&p.b<40){red++;if(x>128)wrong++;}if(p.b>180&&p.r<40){blue++;if(x<128)wrong++;}}
                Directory.CreateDirectory("Validation");File.WriteAllBytes("Validation/layer-selection.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                Require(red>20&&blue>20&&wrong==0,"rendered colors and renderer isolation");
                source.layers[0].appearance.noise=3;source.layers[0].appearance.noiseFrequency=1;Render();
                var first=((IList)Field(gpu,"batches"))[0];var originalBuffer=Field(first,"segments");var low=new uint[4];((GraphicsBuffer)Field(first,"arguments")).GetData(low);
                source.layers[0].appearance.noiseFrequency=12;Render();first=((IList)Field(gpu,"batches"))[0];var high=new uint[4];((GraphicsBuffer)Field(first,"arguments")).GetData(high);
                Require(high[0]>low[0]&&ReferenceEquals(originalBuffer,Field(first,"segments")),"noise frequency increases sampling and reuses allocation");Require(gpu.GeometryUpdateCount==updates,"noise frequency reuses shared chains");
                previous=RenderTexture.active;RenderTexture.active=target;image=new Texture2D(256,256,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();RenderTexture.active=previous;File.WriteAllBytes("Validation/layer-noise-frequency.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                source.layers[0].renderers=Array.Empty<Renderer>();Render();Require(gpu.LayerCount==1&&Counters(gpu)[0]==4,"empty custom selection does not draw all objects");
                updates=gpu.GeometryUpdateCount;source.layers[1].enabled=false;Render();Require(gpu.LayerCount==0&&gpu.GeometryUpdateCount==updates,"disabled layers do no geometry work");
                Debug.Log("LINE_ART_LAYER_GPU_REUSE_AND_PIXELS_PASSED");
            }
            // The reference path uses the same extracted chains for all layers.
            source.layers=new[]{Layer("CPU red",source.renderers,Color.red),Layer("CPU blue",source.renderers,Color.blue)};
            LineArtGeometry.Snapshot[] Capture(Camera c,HashSet<Renderer> targets,LineArtSettings s)
            {
                var snapshots=new List<LineArtGeometry.Snapshot>();foreach(var r in source.renderers){var mesh=r.GetComponent<MeshFilter>().sharedMesh;var positions=mesh.vertices;var topology=LineArtGeometry.Prepare(positions,new[]{mesh.triangles});var welded=new Vector3[topology.representatives.Length];for(int i=0;i<welded.Length;i++)welded[i]=positions[topology.representatives[i]];snapshots.Add(new LineArtGeometry.Snapshot{id=r.GetInstanceID(),rendererId=r.GetInstanceID(),draw=targets.Contains(r),topology=topology,vertices=welded,cull=new[]{0},opaque=new[]{true}});}return snapshots.ToArray();
            }
            using(var cpu=new LineArtCpuLayers(camera,AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArt.shader"),Capture,(snapshots,view)=>123))
            {
                void Update(){cpu.Update(settings,AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArt.shader"),256,256);var task=(Task)Field(cpu,"pending");task?.GetAwaiter().GetResult();cpu.Update(settings,AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArt.shader"),256,256);}
                Update();Require(cpu.drawings.Count==2&&ReferenceEquals(cpu.drawings[0].mesh,cpu.drawings[1].mesh)&&cpu.ExtractionCount==1,"CPU shares extraction and identical meshes");
                source.layers[0].appearance.lengthTrim=.2f;Update();Require(cpu.drawings.Count==2&&!ReferenceEquals(cpu.drawings[0].mesh,cpu.drawings[1].mesh)&&cpu.ExtractionCount==1,"CPU appearance edit reuses extraction");
                source.layers[0].renderers=new[]{left};source.layers[1].renderers=new[]{right};Update();Require(cpu.drawings.Count==2&&cpu.ExtractionCount==1,"CPU selection split reuses extraction");
                foreach(var v in cpu.drawings[0].mesh.vertices)Require(v.x<0,"CPU left membership");foreach(var v in cpu.drawings[1].mesh.vertices)Require(v.x>0,"CPU right membership");
                Debug.Log("LINE_ART_LAYER_CPU_REUSE_PASSED");
            }
            target.Release();UnityEngine.Object.DestroyImmediate(target);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);
            IntersectionMembership(compute,shader);
        }
        static void IntersectionMembership(ComputeShader compute,Shader shader)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var meshes=new List<Mesh>();var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetFloat("_Cull",0);
            var a=MeshObject(new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(0,1,0)},new[]{0,2,1},material,meshes);
            var b=MeshObject(new[]{new Vector3(0,-.5f,-1),new Vector3(0,-.5f,1),new Vector3(0,.5f,0)},new[]{0,1,2},material,meshes);
            var source=new GameObject("Intersection layers").AddComponent<ObjectLineArtSource>();source.layers=new[]{Layer("a",new[]{a},Color.red),Layer("b",new[]{b},Color.blue)};
            var camera=new GameObject("Intersection camera").AddComponent<Camera>();camera.orthographic=true;camera.transform.position=new Vector3(0,0,-5);camera.orthographicSize=3;
            var settings=new LineArtSettings{contour=false,crease=false,materialBorders=false,boundaries=false,intersections=true,occlusion=false};
            using(var gpu=new LineArtGpu(compute,shader))using(var cmd=new CommandBuffer())
            {
                gpu.Prepare(camera,settings);gpu.Execute(cmd,camera,128,128,settings);Graphics.ExecuteCommandBuffer(cmd);Require(Counters(gpu)[4]==1,"shared intersection computed once");
                foreach(var batch in (IEnumerable)Field(gpu,"batches")){var args=new uint[4];((GraphicsBuffer)Field(batch,"arguments")).GetData(args);Require(args[0]==6,"intersection included for either participating renderer");}
            }
            foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);Debug.Log("LINE_ART_LAYER_INTERSECTION_MEMBERSHIP_PASSED");
        }
    }
}
#endif
