#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpiderVerse.LineArt.Editor
{
    public static class LineArtWidthChecks
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Segment { public Vector4 a,b,previous,next,style; }
        public static void BatchRun()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Library/LineArtValidation/"))throw new InvalidOperationException("Isolated validation project required");
            try { Run();Debug.Log("LINE_ART_PIXEL_WIDTH_CHECKS_PASSED");EditorApplication.Exit(0); }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static float RandomSigned(uint seed)
        {
            unchecked {seed=(seed^(seed>>16))*0x7feb352du;seed=(seed^(seed>>15))*0x846ca68bu;seed^=seed>>16;return (seed&0x00ffffffu)*(2f/16777215f)-1;}
        }
        static void Run()
        {
            const int size=512;
            var go=new GameObject("Width test camera");var camera=go.AddComponent<Camera>();camera.aspect=1;camera.fieldOfView=60;
            var target=new RenderTexture(size,size,0);target.Create();
            var pixels=new Texture2D(size,size,TextureFormat.RGBA32,false);
            var mesh=new Mesh();var positions=new List<Vector3>();var previous=new List<Vector3>();var next=new List<Vector3>();var stroke=new List<Vector4>();var indices=new List<int>();
            var segments=new Segment[3];
            for(int i=0;i<3;i++){
                float y=.25f+i*.25f;float near=i==1?20:2,far=i==2?20:near;
                Vector3 a=camera.ViewportToWorldPoint(new Vector3(.15f,y,near)),b=camera.ViewportToWorldPoint(new Vector3(.85f,y,far));
                segments[i]=new Segment{a=new Vector4(a.x,a.y,a.z,0),b=new Vector4(b.x,b.y,b.z,1),previous=a,next=b,style=new Vector4(1,0,0,0)};
                for(int corner=0;corner<4;corner++){positions.Add(corner<2?a:b);previous.Add(a);next.Add(b);stroke.Add(new Vector4((corner&1)==0?-1:1,corner<2?0:1,1,0));}
                foreach(int index in new[]{0,1,2,1,3,2})indices.Add(i*4+index);
            }
            mesh.SetVertices(positions);mesh.SetUVs(0,previous);mesh.SetUVs(1,next);mesh.SetUVs(2,stroke);mesh.SetTriangles(indices,0);
            var report=new List<string>();
            using(var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,80))using(var cmd=new CommandBuffer()){
                buffer.SetData(segments);
                foreach(string path in new[]{"ObjectLineArt","ObjectLineArtGpu"}){
                    var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/"+path+".shader"));
                    try {
                        foreach(float width in new[]{4f,12f}){
                            LineArtGpu.SetMaterial(material,new LineArtAppearance{scaleWithDistance=false,color=Color.white,thickness=width,thicknessCurve=false,noise=0},size,size);
                            cmd.Clear();cmd.SetRenderTarget(target);cmd.ClearRenderTarget(false,true,Color.black);cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                            if(path.EndsWith("Gpu")){material.SetBuffer("_GpuSegments",buffer);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,18);}
                            else cmd.DrawMesh(mesh,Matrix4x4.identity,material);
                            Graphics.ExecuteCommandBuffer(cmd);var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,size,size),0,0);pixels.Apply();RenderTexture.active=old;
                            var colors=pixels.GetPixels();float min=float.MaxValue,max=0;
                            for(int line=0;line<3;line++)foreach(int x in new[]{100,160,256,352,412}){
                                float coverage=0;int center=128+line*128;for(int y=center-20;y<center+20;y++)coverage+=colors[y*size+x].r;
                                min=Mathf.Min(min,coverage);max=Mathf.Max(max,coverage);
                                if(coverage<width-2.1f||coverage>width+.1f)throw new Exception(path+" unexpected pixel width: "+coverage);
                            }
                            if(max-min>.15f)throw new Exception(path+" depth-dependent coverage: "+min+".."+max);
                            report.Add(path+" width="+width+" coverage="+min.ToString("F3")+".."+max.ToString("F3")+" at depth 2 / 20 / 2-to-20");
                            Directory.CreateDirectory("Validation");File.WriteAllBytes("Validation/width-"+path+"-"+width+".png",pixels.EncodeToPNG());
                        }
                    
                        // World-scaled mode: compare actual width and offsets at 2 and 20 units.
                        foreach(bool ortho in new[]{false,true}){
                            camera.orthographic=ortho;camera.orthographicSize=3;
                            // Reuse the perspective fixture positions; measure around projected endpoints.
                            var centers=new Vector2[2];
                            for(int shifted=0;shifted<2;shifted++){
                                LineArtGpu.SetMaterial(material,new LineArtAppearance{scaleWithDistance=true,sizeUnit=.005f,color=Color.white,thickness=24,thicknessCurve=false,offset=shifted==0?Vector2.zero:new Vector2(8,6)},size,size);
                                cmd.Clear();cmd.SetRenderTarget(target);cmd.ClearRenderTarget(false,true,Color.black);cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                                if(path.EndsWith("Gpu")){material.SetBuffer("_GpuSegments",buffer);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,18);}
                                else cmd.DrawMesh(mesh,Matrix4x4.identity,material);
                                Graphics.ExecuteCommandBuffer(cmd);var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,size,size),0,0);pixels.Apply();RenderTexture.active=old;
                                var colors=pixels.GetPixels();
                                for(int line=0;line<2;line++){
                                    float depth=line==0?2:20;
                                    float factor=.005f*Mathf.Abs(camera.projectionMatrix.m11)*size*.5f/(ortho?1:depth);
                                    Vector3 projected=camera.WorldToViewportPoint((positions[line*4]+positions[line*4+2])*.5f);
                                    // Unity RT readback uses top-down screen coordinates for this fixture.
                                    int cy=Mathf.RoundToInt((1-projected.y)*size);float mass=0,weighted=0;
                                    for(int y=Mathf.Max(0,cy-40);y<Mathf.Min(size,cy+40);y++){float v=colors[y*size+256].r;mass+=v;weighted+=y*v;}
                                    if(Mathf.Abs(mass-24*factor)>1.6f)throw new Exception(path+" projected width "+mass+" expected "+24*factor+" ortho="+ortho);
                                    if(shifted==0)centers[line]=new Vector2(0,weighted/mass);
                                    else if(Mathf.Abs(Mathf.Abs(weighted/mass-centers[line].y)-6*factor)>.6f)throw new Exception(path+" projected offset mismatch: "+(weighted/mass-centers[line].y)+" expected "+6*factor+" ortho="+ortho);
                                }
                            }
                            report.Add(path+" world width/offset passed: "+(ortho?"orthographic":"perspective depth 2 and 20"));
                        }
                        camera.orthographic=false;
                        Vector2[] baseline=null;
                        foreach(int mode in new[]{0,1,2,3,4}){
                            var shift=mode==1?new Vector2(13,-9):mode==2?new Vector2(-17,11):Vector2.zero;
                            var random=mode>=3?new Vector2(9,13):Vector2.zero;
                            LineArtGpu.SetMaterial(material,new LineArtAppearance{scaleWithDistance=false,color=Color.white,thickness=8,thicknessCurve=false,offset=shift,randomOffset=random},size,size);
                            cmd.Clear();cmd.SetRenderTarget(target);cmd.ClearRenderTarget(false,true,Color.black);cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                            cmd.SetGlobalVector("_ProjectionParams",new Vector4(1,camera.nearClipPlane,camera.farClipPlane,1/camera.farClipPlane));
                            if(path.EndsWith("Gpu")){material.SetBuffer("_GpuSegments",buffer);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,18);}
                            else cmd.DrawMesh(mesh,Matrix4x4.identity,material);
                            Graphics.ExecuteCommandBuffer(cmd);var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,size,size),0,0);pixels.Apply();RenderTexture.active=old;
                            var colors=pixels.GetPixels();var centers=new Vector2[3];
                            for(int line=0;line<3;line++){
                                float mass=0;int center=128+line*128;
                                for(int y=center-40;y<center+40;y++)for(int x=30;x<size-30;x++){float value=colors[y*size+x].r;mass+=value;centers[line]+=new Vector2(x,y)*value;}
                                if(mass<100)throw new Exception("Offset fixture missing stroke");centers[line]/=mass;
                            }
                            if(mode==0){baseline=centers;continue;}
                            Vector2 expected=mode<3?new Vector2(shift.x,shift.y):new Vector2(RandomSigned(0x68bc21ebu)*random.x,RandomSigned(0x02e5be93u)*random.y);
                            for(int line=0;line<3;line++){
                                Vector2 delta=centers[line]-baseline[line];
                                if((delta-expected).magnitude>.65f)throw new Exception(path+" offset at depth fixture "+line+": "+delta+" expected "+expected);
                                if((delta-(centers[0]-baseline[0])).magnitude>.03f)throw new Exception(path+" depth-dependent offset");
                            }
                            report.Add(path+" offset mode="+mode+" delta="+(centers[0]-baseline[0])+" identical at depth 2 / 20 / 2-to-20");
                        }
                    }finally{UnityEngine.Object.DestroyImmediate(material);}
                }
            }
            File.WriteAllLines("Validation/width-checks.txt",report);foreach(var line in report)Debug.Log(line);
            target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
#endif
