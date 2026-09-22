#if UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpiderVerse.LineArt.Editor
{
    public static class LineArtDepthChecks
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Segment { public Vector4 a,b,previous,next,style; }
        static void Require(bool value,string label){if(!value)throw new Exception(label);}
        public static void BatchRun()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Library/LineArtValidation/"))throw new InvalidOperationException("Isolated validation project required");
            try {Run();LineArtLayerChecks.RunFixtures();Debug.Log("LINE_ART_DEPTH_CHECKS_PASSED");EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static void Run()
        {
            var appearance=new LineArtAppearance{depthOffset=.25f};
            var copy=appearance.CopyAppearance();Require(copy.depthOffset==.25f,"depth offset copies");
            copy.depthOffset=-.25f;Require(!appearance.SameAppearance(copy)&&appearance.SameGeometry(copy),"offset invalidates appearance only");
            var first=new LineArtLayers.Draw{appearance=appearance};var second=new LineArtLayers.Draw{appearance=copy};
            first.UpdateProperties(128,128);second.UpdateProperties(128,128);
            Require(first.properties.GetFloat("_DepthOffset")==.25f&&second.properties.GetFloat("_DepthOffset")==-.25f,"independent layer offsets");
            appearance.depthOffset=.5f;first.UpdateProperties(128,128);
            Require(first.properties.GetFloat("_DepthOffset")==.5f,"live offset refresh");
            var go=new GameObject("Depth fixture");var camera=go.AddComponent<Camera>();camera.aspect=1;camera.nearClipPlane=.3f;camera.farClipPlane=100;camera.orthographicSize=3;
            var target=new RenderTexture(128,128,24);target.Create();var pixels=new Texture2D(128,128,TextureFormat.RGBA32,false);
            var quad=new Mesh();quad.vertices=new[]{new Vector3(-10,-10,3),new Vector3(10,-10,3),new Vector3(10,10,3),new Vector3(-10,10,3)};quad.triangles=new[]{0,2,1,0,3,2};
            var occluder=new Material(Shader.Find("Universal Render Pipeline/Unlit"));occluder.SetColor("_BaseColor",Color.black);occluder.SetFloat("_Cull",0);
            Vector3 a=new Vector3(-1,0,4),b=new Vector3(1,0,4);
            var mesh=new Mesh();mesh.vertices=new[]{a,a,b,b};mesh.SetUVs(0,new[]{a,a,a,a});mesh.SetUVs(1,new[]{b,b,b,b});mesh.SetUVs(2,new[]{new Vector4(-1,0,1,0),new Vector4(1,0,1,0),new Vector4(-1,1,1,0),new Vector4(1,1,1,0)});mesh.triangles=new[]{0,1,2,1,3,2};
            using(var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,80))using(var cmd=new CommandBuffer())
            {
                buffer.SetData(new[]{new Segment{a=new Vector4(a.x,a.y,a.z,0),b=new Vector4(b.x,b.y,b.z,1),previous=a,next=b,style=new Vector4(1,0,0,0)}});
                foreach(bool ortho in new[]{false,true})foreach(string shaderName in new[]{"ObjectLineArt","ObjectLineArtGpu"})
                {
                    camera.orthographic=ortho;var material=new Material(Shader.Find("Hidden/SpiderVerse/"+shaderName));
                    foreach(float offset in new[]{0f,2f,-2f})
                    {
                        LineArtGpu.SetMaterial(material,new LineArtAppearance{scaleWithDistance=false,color=Color.white,thickness=8,thicknessCurve=false,depthOffset=offset},128,128);
                        cmd.Clear();cmd.SetRenderTarget(target);cmd.ClearRenderTarget(true,true,Color.black);
                        cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                        cmd.SetGlobalVector("_ProjectionParams",new Vector4(1,.3f,100,.01f));
                        cmd.DrawMesh(quad,Matrix4x4.identity,occluder,0,0);
                        if(shaderName.EndsWith("Gpu")){material.SetBuffer("_GpuSegments",buffer);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,6);}
                        else cmd.DrawMesh(mesh,Matrix4x4.identity,material,0,0);
                        Graphics.ExecuteCommandBuffer(cmd);var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();RenderTexture.active=old;
                        int count=0;foreach(var pixel in pixels.GetPixels())if(pixel.r>.5f)count++;
                        Require(offset==2?count>40:count==0,shaderName+" ortho="+ortho+" offset="+offset+" pixels="+count);
                    }
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
            UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(quad);UnityEngine.Object.DestroyImmediate(occluder);UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
#endif
