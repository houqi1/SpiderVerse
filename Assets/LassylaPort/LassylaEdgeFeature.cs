using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LassylaPort
{
    // EEVEE hashed transparency needs accumulation, including in Scene view.
    // URP explicitly disables its own TAA for Scene cameras.
    public sealed class LassylaEdgeFeature : ScriptableRendererFeature
    {
        [Range(1,128)] public int samples = 16;
        [Range(1,16)] public int samplesPerFrame = 4;
        [Range(0,2)] public float filterRadius = 1.5f;
        public float blenderExposure;
        [Tooltip("Match EEVEE's continuously reprojected preview while the reference timeline plays.")]
        public bool matchTimelinePlayback=true;
        public Shader compositeShader;
        public Texture3D display,inverseDisplay;
        public Material sharedMaterial;
        [Tooltip("Blender world-coordinate calibration used by the ToonCel alpha hash.")]
        public Material edgeCoordinateReference;
        #if UNITY_EDITOR
        public static Camera forestPreviewCamera;
        public static Texture2D forestPreviewPanorama;
        public static Matrix4x4 forestWorldToBlender;
        #endif
        Material material;
        EdgePass pass;
        LassylaMeshCoordinates coordinateTable;
        int toonLayers;
        bool bindCoordinates=true;
        void BeginContext(ScriptableRenderContext context,List<Camera> cameras){bindCoordinates=true;}
        ToonCameraRoute beginToon, endToon;
        public override void Create()
        {
            pass?.Dispose(); CoreUtils.Destroy(material);
            coordinateTable=Resources.Load<LassylaMeshCoordinates>("ToonCelMeshCoordinates");
            RenderPipelineManager.beginContextRendering-=BeginContext;
            RenderPipelineManager.beginContextRendering+=BeginContext;
            bindCoordinates=true;
            if(!compositeShader)compositeShader=Shader.Find("Hidden/Lassyla/Accumulate");
            if(compositeShader)material=CoreUtils.CreateEngineMaterial(compositeShader);
            pass=new EdgePass(this){renderPassEvent=RenderPassEvent.AfterRenderingSkybox};
            pass.requiresIntermediateTexture=true;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            beginToon=new ToonCameraRoute(this,true);
            endToon=new ToonCameraRoute(this,false);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            if(!material || data.cameraData.cameraType==CameraType.Preview)return;
            if(bindCoordinates){
                if(!coordinateTable)coordinateTable=Resources.Load<LassylaMeshCoordinates>("ToonCelMeshCoordinates");
                toonLayers=coordinateTable?coordinateTable.BindVisibleMaterials():~0;
                bindCoordinates=false;
            }
            renderer.EnqueuePass(beginToon);
            renderer.EnqueuePass(pass);
            renderer.EnqueuePass(endToon);
        }
        sealed class ToonCameraRoute : ScriptableRenderPass
        {
            readonly LassylaEdgeFeature owner;
            readonly bool enabled;
            public ToonCameraRoute(LassylaEdgeFeature owner,bool enabled)
            {
                this.owner=owner;this.enabled=enabled;
                renderPassEvent=enabled?RenderPassEvent.BeforeRendering:RenderPassEvent.AfterRendering;
            }
            sealed class Data { public bool enabled,moving; public Material reference; }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                using var pass=graph.AddRasterRenderPass<Data>(enabled?"Start ToonCel edge routing":"End ToonCel edge routing",out var data);
                data.enabled=enabled;data.reference=owner.edgeCoordinateReference;
                var camera=frame.Get<UniversalCameraData>().camera;
                var capture=camera.GetComponent<LassylaCaptureCamera>();
                data.moving=enabled && !capture && owner.pass.ViewMoved(camera);
                pass.AllowGlobalStateModification(true);pass.AllowPassCulling(false);
                pass.SetRenderFunc(static(Data d,RasterGraphContext ctx)=>{
                    ctx.cmd.SetGlobalFloat("_ToonLassylaCamera",d.enabled?1:0);
                    ctx.cmd.SetGlobalFloat("_ToonEdgeCameraMoving",d.moving?1:0);
                    if(!d.enabled)return;
                    ctx.cmd.SetGlobalVector("_ToonBlenderWorld0",d.reference?d.reference.GetVector("_BlenderWorld0"):new Vector4(-1,0,0,0));
                    ctx.cmd.SetGlobalVector("_ToonBlenderWorld1",d.reference?d.reference.GetVector("_BlenderWorld1"):new Vector4(0,0,-1,0));
                    ctx.cmd.SetGlobalVector("_ToonBlenderWorld2",d.reference?d.reference.GetVector("_BlenderWorld2"):new Vector4(0,1,0,0));
                });
            }
        }
        protected override void Dispose(bool disposing){
            RenderPipelineManager.beginContextRendering-=BeginContext;
            pass?.Dispose();CoreUtils.Destroy(material);
        }
        sealed class History {public RTHandle texture,next,mask,nextMask;public int fingerprint,count,lastView,backdropMode;public bool hasView,playback,hasToon;public float weight;public Matrix4x4 viewProjection;}
        sealed class EdgePass : ScriptableRenderPass
        {
            readonly LassylaEdgeFeature owner;
            readonly Dictionary<Camera,History> histories=new();
            readonly List<Camera> expiredCameras=new();
            static readonly ShaderTagId studioSurface=new("LassylaSurface"),toonCoverage=new("ToonCelCoverage"),toonColour=new("ToonCelEdgeColour");
            static readonly List<ShaderTagId> diagnosticTags=new(){studioSurface,toonCoverage};
            static readonly int Offset=Shader.PropertyToID("_LassylaTemporalOffset"),UseDepth=Shader.PropertyToID("_LassylaUseSceneDepth"),SceneDepth=Shader.PropertyToID("_LassylaSceneDepth");
            public EdgePass(LassylaEdgeFeature owner){this.owner=owner;}
            static int ViewHash(Camera camera)=>camera.worldToCameraMatrix.GetHashCode()^camera.projectionMatrix.GetHashCode();
            public bool ViewMoved(Camera camera)=>histories.TryGetValue(camera,out var h) && h.hasView && ViewHash(camera)!=h.lastView;
            public void Dispose(){foreach(var h in histories.Values){h.texture?.Release();h.next?.Release();h.mask?.Release();h.nextMask?.Release();}histories.Clear();}
            static float Halton(int i,int b){float f=1,r=0;while(i>0){f/=b;r+=f*(i%b);i/=b;}return r;}
            sealed class DrawData {public RendererListHandle list;public TextureHandle sceneDepth;public RTClearFlags clear;public float offset;public Vector4 jitter;}
            struct CameraGrade {public TextureHandle lut,inverse;public Vector4 parameters,userParameters;public Texture user;public int toneMapper;}
            sealed class BlitData {public TextureHandle source,previous,background,depth,sceneDepth;public Texture3D display,inverse;public Material material;public int pass;public float weight,exposureScale;public Vector4[] taps;public Vector4 size;public Matrix4x4 reprojection;public bool moving;public CameraGrade grade;}
            static void BindGrade(MaterialPropertyBlock properties,CameraGrade grade)
            {
                properties.SetVector("_ProjectLutParams",grade.parameters);
                if(!grade.lut.IsValid())return;
                properties.SetTexture("_ProjectLut",grade.lut);properties.SetInt("_ProjectToneMapper",grade.toneMapper);
                properties.SetVector("_ProjectUserLutParams",grade.userParameters);
                if(grade.user)properties.SetTexture("_ProjectUserLut",grade.user);
                if(grade.inverse.IsValid())properties.SetTexture("_InverseProjectLut",grade.inverse);
            }
            CameraGrade RecordGrade(RenderGraph graph,ContextContainer frame,TextureHandle lut)
            {
                var pp=frame.Get<UniversalPostProcessingData>();int size=pp.lutSize;
                var stack=VolumeManager.instance.stack;
                var user=stack.GetComponent<ColorLookup>();
                var grade=new CameraGrade{lut=lut,parameters=new Vector4(1f/(size*size),1f/size,size-1,Mathf.Pow(2,stack.GetComponent<ColorAdjustments>().postExposure.value)),
                    toneMapper=pp.gradingMode==ColorGradingMode.HighDynamicRange?3:(int)stack.GetComponent<Tonemapping>().mode.value};
                if(user!=null && user.IsActive()){
                    grade.user=user.texture.value;
                    grade.userParameters=new Vector4(1f/grade.user.width,1f/grade.user.height,grade.user.height-1,user.contribution.value);
                }
                var inverse=graph.CreateTexture(new TextureDesc(33*33,33){name="Lassyla inverse camera grade",colorFormat=GraphicsFormat.R16G16B16A16_SFloat,filterMode=FilterMode.Bilinear});
                using(var builder=graph.AddRasterRenderPass<BlitData>("Prepare character for camera colour grading",out var data)){
                    data.grade=grade;data.material=owner.material;builder.UseTexture(lut);builder.SetRenderAttachment(inverse,0,AccessFlags.Write);
                    builder.SetRenderFunc(static(BlitData d,RasterGraphContext ctx)=>{
                        var properties=new MaterialPropertyBlock();BindGrade(properties,d.grade);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity,d.material,6,MeshTopology.Triangles,3,1,properties);
                    });
                }
                grade.inverse=inverse;return grade;
            }
            sealed class StudioShadowData {public RendererListHandle casters;public Matrix4x4 lightMatrix;}
            #if UNITY_EDITOR
            sealed class ForestBackdrop {public Texture2D panorama;public Material material;public Matrix4x4 basis;public Vector4 pixelSize;}
            void DrawForestBackdrop(RenderGraph graph,TextureHandle target,int width,int height)
            {
                using var builder=graph.AddRasterRenderPass<ForestBackdrop>("Blender material preview environment",out var data);
                data.basis=forestWorldToBlender;
                data.panorama=forestPreviewPanorama;data.material=owner.material;data.pixelSize=new Vector4(1f/width,1f/height,0,0);
                builder.SetRenderAttachment(target,0,AccessFlags.Write);
                builder.SetRenderFunc(static(ForestBackdrop d,RasterGraphContext ctx)=>{
                    var p=new MaterialPropertyBlock();p.SetTexture("_ForestEnvironment",d.panorama);
                    p.SetMatrix("_ForestWorldToBlender",d.basis);
                    p.SetVector("_ForestPixelSize",d.pixelSize);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity,d.material,7,MeshTopology.Triangles,3,1,p);
                });
            }
            #endif
            static readonly int StudioShadow=Shader.PropertyToID("_LassylaStudioShadow");
            static TextureHandle RecordStudioShadow(RenderGraph graph,UniversalRenderingData rd,UniversalCameraData cd,UniversalLightData ld)
            {
                if(!LassylaTemporalSampling.TryGetStudioBounds(out var bounds))return TextureHandle.nullHandle;
                var desc=new TextureDesc(1024,1024){name="Forest studio sun depth",colorFormat=GraphicsFormat.R32_SFloat,clearBuffer=true,clearColor=Color.white,filterMode=FilterMode.Point};
                var shadow=graph.CreateTexture(desc);
                var light=new Vector3(-.783121f,.325331f,.529982f).normalized;
                var right=Vector3.Cross(Vector3.up,light).normalized;var up=Vector3.Cross(light,right);
                var extent=bounds.extents;
                var map=Matrix4x4.identity;
                for(int j=0;j<3;j++){
                    var axis=j==0?right:j==1?up:-light;
                    float size=Mathf.Abs(axis.x)*extent.x+Mathf.Abs(axis.y)*extent.y+Mathf.Abs(axis.z)*extent.z+.02f;
                    axis/=2*size;map.SetRow(j,new Vector4(axis.x,axis.y,axis.z,.5f-Vector3.Dot(axis,bounds.center)));
                }
                var drawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("LassylaStudioCaster"),rd,cd,ld,SortingCriteria.None);
                var list=graph.CreateRendererList(new RendererListParams(rd.cullResults,drawing,new FilteringSettings(RenderQueueRange.opaque,cd.camera.cullingMask)));
                using var builder=graph.AddRasterRenderPass<StudioShadowData>("Forest studio self shadows",out var data);
                data.lightMatrix=map;data.casters=list;builder.UseRendererList(list);builder.SetRenderAttachment(shadow,0,AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);builder.SetGlobalTextureAfterPass(shadow,StudioShadow);
                builder.SetRenderFunc(static(StudioShadowData d,RasterGraphContext ctx)=>{
                    ctx.cmd.SetGlobalMatrix("_LassylaStudioProjection",d.lightMatrix);ctx.cmd.DrawRendererList(d.casters);
                });
                return shadow;
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                expiredCameras.Clear();foreach(var entry in histories)if(!entry.Key)expiredCameras.Add(entry.Key);
                foreach(var key in expiredCameras){histories[key].texture?.Release();histories[key].next?.Release();histories[key].mask?.Release();histories[key].nextMask?.Release();histories.Remove(key);}
                var resources=frame.Get<UniversalResourceData>();var cd=frame.Get<UniversalCameraData>();
                var rd=frame.Get<UniversalRenderingData>();var ld=frame.Get<UniversalLightData>();
                var camera=cd.camera;var capture=camera.GetComponent<LassylaCaptureCamera>();
                bool diagnostic=capture && (capture.measureLinearCoverage || capture.rawSample);
                bool hasToon=!diagnostic && (camera.cullingMask & owner.toonLayers)!=0;
                var drawing=diagnostic?RenderingUtils.CreateDrawingSettings(diagnosticTags,rd,cd,ld,cd.defaultOpaqueSortFlags):
                    RenderingUtils.CreateDrawingSettings(studioSurface,rd,cd,ld,cd.defaultOpaqueSortFlags);
                var listParameters=new RendererListParams(rd.cullResults,drawing,new FilteringSettings(RenderQueueRange.opaque,camera.cullingMask));
                var studioShadow=RecordStudioShadow(graph,rd,cd,ld);
                if(capture && capture.rawSample)
                {
                    Draw(graph,graph.CreateRendererList(listParameters),resources.activeColorTexture,resources.activeDepthTexture,TextureHandle.nullHandle,studioShadow,RTClearFlags.None,0,Vector4.zero);
                    return;
                }
                int total=Mathf.Clamp(capture?capture.samples:owner.samples,1,128);
                float radius=Mathf.Clamp(capture?capture.filterRadius:owner.filterRadius,0,2);
                bool colour=owner.display && owner.inverseDisplay && !(capture && capture.measureLinearCoverage) && owner.sharedMaterial && owner.sharedMaterial.GetFloat("_DebugView")==0;
                bool playback=colour && (capture?capture.timelinePlayback:owner.matchTimelinePlayback);
                if(!playback && total==1)radius=0;
                int view=ViewHash(camera);
                if(!histories.TryGetValue(camera,out var history))histories[camera]=history=new History();
                bool moving=!capture && history.hasView && view!=history.lastView;
                history.hasView=true;history.lastView=view;
                int fingerprint=playback?0:view^LassylaTemporalSampling.SceneFingerprint();
                fingerprint=fingerprint*31+moving.GetHashCode();
                fingerprint=fingerprint*31+total;
                fingerprint=fingerprint*31+radius.GetHashCode();
                fingerprint=fingerprint*31+owner.blenderExposure.GetHashCode();
                var desc=cd.cameraTargetDescriptor;desc.depthBufferBits=0;desc.msaaSamples=1;desc.graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat;
                // URP includes the RTHandle name in its allocation descriptor. These handles
                // swap roles after each sample, so both must keep the same allocation key.
                const string historyName="Lassyla coverage history";
                bool allocated=RenderingUtils.ReAllocateHandleIfNeeded(ref history.texture,desc,FilterMode.Point,TextureWrapMode.Clamp,name:historyName);
                allocated|=RenderingUtils.ReAllocateHandleIfNeeded(ref history.next,desc,FilterMode.Point,TextureWrapMode.Clamp,name:historyName);
                TextureHandle maskHistory=default,maskNext=default,maskSample=default,baseColour=default,baseDepth=default;
                RendererListParams maskDraw=default;
                if(hasToon){
                    var maskDesc=desc;maskDesc.graphicsFormat=GraphicsFormat.R16_SFloat;
                    const string maskName="ToonCel edge coverage history";
                    allocated|=RenderingUtils.ReAllocateHandleIfNeeded(ref history.mask,maskDesc,FilterMode.Point,TextureWrapMode.Clamp,name:maskName);
                    allocated|=RenderingUtils.ReAllocateHandleIfNeeded(ref history.nextMask,maskDesc,FilterMode.Point,TextureWrapMode.Clamp,name:maskName);
                    maskHistory=graph.ImportTexture(history.mask);maskNext=graph.ImportTexture(history.nextMask);
                    maskSample=graph.CreateTexture(new TextureDesc(desc.width,desc.height){name="ToonCel binary coverage",colorFormat=GraphicsFormat.R8_UNorm});
                    var filtering=new FilteringSettings(RenderQueueRange.opaque,camera.cullingMask);
                    var maskSettings=RenderingUtils.CreateDrawingSettings(toonCoverage,rd,cd,ld,cd.defaultOpaqueSortFlags);
                    maskDraw=new RendererListParams(rd.cullResults,maskSettings,filtering);
                    var colourSettings=RenderingUtils.CreateDrawingSettings(toonColour,rd,cd,ld,cd.defaultOpaqueSortFlags);
                    var list=graph.CreateRendererList(new RendererListParams(rd.cullResults,colourSettings,filtering));
                    var colourDesc=graph.GetTextureDesc(resources.activeColorTexture);
                    colourDesc.name="ToonCel original camera colour";colourDesc.msaaSamples=MSAASamples.None;
                    colourDesc.bindTextureMS=false;colourDesc.depthBufferBits=DepthBits.None;
                    baseColour=graph.CreateTexture(colourDesc);
                    baseDepth=graph.CreateTexture(new TextureDesc(desc.width,desc.height){name="ToonCel original surface depth",depthBufferBits=DepthBits.Depth32});
                    using var layer=graph.AddRasterRenderPass<DrawData>("Shade unfiltered ToonCel surface",out var d);
                    d.list=list;layer.UseRendererList(list);layer.UseAllGlobalTextures(true);
                    layer.SetRenderAttachment(baseColour,0,AccessFlags.Write);layer.SetRenderAttachmentDepth(baseDepth,AccessFlags.Write);
                    layer.SetRenderFunc(static(DrawData d,RasterGraphContext ctx)=>{
                        ctx.cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);
                        ctx.cmd.DrawRendererList(d.list);
                    });
                }
                int backdropMode=cd.postProcessEnabled?1:0;
                #if UNITY_EDITOR
                if(camera==forestPreviewCamera && forestPreviewPanorama)backdropMode|=2;
                #endif
                if(allocated||history.hasToon!=hasToon||history.backdropMode!=backdropMode||history.playback!=playback||(playback?capture!=null:history.fingerprint!=fingerprint)){history.count=0;history.weight=0;}
                history.backdropMode=backdropMode;history.hasToon=hasToon;
                history.fingerprint=fingerprint;history.playback=playback;
                var accumulation=graph.ImportTexture(history.texture);
                var next=graph.ImportTexture(history.next);
                var sampleDesc=new TextureDesc(desc.width,desc.height){name="Lassyla stochastic surface",colorFormat=GraphicsFormat.R16G16B16A16_SFloat,clearBuffer=false};
                var sample=graph.CreateTexture(sampleDesc);
                var depth=graph.CreateTexture(new TextureDesc(desc.width,desc.height){name="Lassyla stochastic depth",depthBufferBits=DepthBits.Depth32,clearBuffer=false});
                TextureHandle background=TextureHandle.nullHandle,linearBG=TextureHandle.nullHandle;
                var grade=colour && cd.postProcessEnabled && resources.internalColorLut.IsValid()?RecordGrade(graph,frame,resources.internalColorLut):default;
                if(colour){
                    var backgroundDesc=graph.GetTextureDesc(resources.activeColorTexture);
                    backgroundDesc.name="Scene behind Gwen";backgroundDesc.msaaSamples=MSAASamples.None;backgroundDesc.bindTextureMS=false;backgroundDesc.depthBufferBits=DepthBits.None;
                    background=graph.CreateTexture(backgroundDesc);
                    #if UNITY_EDITOR
                    if(camera==forestPreviewCamera && forestPreviewPanorama)
                        DrawForestBackdrop(graph,background,desc.width,desc.height);
                    else
                    #endif
                    using(var builder=graph.AddRasterRenderPass<BlitData>("Read scene behind transparency",out var copy)){
                        copy.source=resources.activeColorTexture;builder.UseTexture(copy.source,AccessFlags.Read);builder.SetRenderAttachment(background,0,AccessFlags.Write);
                        builder.SetRenderFunc(static(BlitData d,RasterGraphContext ctx)=>Blitter.BlitTexture(ctx.cmd,d.source,new Vector4(1,1,0,0),0,false));
                    }
                    if(playback){
                        backgroundDesc.name="Background in Blender scene-linear";backgroundDesc.colorFormat=GraphicsFormat.R16G16B16A16_SFloat;
                        linearBG=graph.CreateTexture(backgroundDesc);
                        Resolve(graph,background,TextureHandle.nullHandle,linearBG,0,null,3,grade:grade);
                    }
                }
                int end=playback?history.count+(capture?total:1):Mathf.Min(total,history.count+(capture?total:owner.samplesPerFrame));
                // This pass requires an intermediate render texture. CameraData's legacy
                // Y-flip query needs an RTHandle which does not exist while recording RenderGraph.
                var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
                var reprojection=history.viewProjection*vp.inverse;
                var size=new Vector4(desc.width,desc.height,1f/desc.width,1f/desc.height);
                for(int s=history.count;s<end;s++)
                {
                    int sampleIndex=playback?s%8192:s;
                    int filterIndex=playback?sampleIndex%8:sampleIndex;
                    var jitter=new Vector2(Mathf.Repeat(Halton(filterIndex+1,2)+.5f,1),Mathf.Repeat(Halton(filterIndex+1,3)+2f/3f,1));
                    if(radius<.70710678f){float angle=jitter.y*2*Mathf.PI;float distance=Mathf.Sqrt(jitter.x)*.5f*radius;jitter=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*distance;}else jitter-=Vector2.one*.5f;
                    var taps=new Vector4[25];int tap=0;float sumWeight=0;
                    if(radius<.01f){taps[0]=new Vector4(0,0,1,0);sumWeight=1;}
                    else for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++){
                        float distance=(new Vector2(x,y)-jitter).sqrMagnitude;if(distance>=radius*radius)continue;
                        float weight=Mathf.Exp(-.5f/(.284f*.284f)*distance/(radius*radius));
                        taps[tap++]=new Vector4(x,-y,weight,0);sumWeight+=weight;
                    }
                    float offset=Halton(sampleIndex+1,7)-1f/7f;
                    var clipJitter=new Vector4(jitter.x*2/desc.width,jitter.y*2/desc.height,moving?1:0,0);
                    Draw(graph,graph.CreateRendererList(listParameters),sample,depth,resources.cameraDepthTexture,studioShadow,RTClearFlags.All,offset,clipJitter);
                    if(playback)Resolve(graph,sample,accumulation,next,s>0?1:0,taps,4,linearBG,depth,resources.cameraDepthTexture,size,reprojection,moving);
                    else Resolve(graph,sample,accumulation,next,history.weight,taps);
                    if(hasToon){
                        // The studio draw has already written its stochastic depth. Preserve it so
                        // the Toon matte sees foreground studio surfaces as well as opaque scenery.
                        Draw(graph,graph.CreateRendererList(maskDraw),maskSample,depth,resources.cameraDepthTexture,default,RTClearFlags.Color,offset,clipJitter);
                        Resolve(graph,maskSample,maskHistory,maskNext,playback?(s>0?1:0):history.weight,taps,playback?9:8,
                            depth:depth,sceneDepth:resources.cameraDepthTexture,size:size,reprojection:reprojection,moving:moving);
                        (maskHistory,maskNext)=(maskNext,maskHistory);(history.mask,history.nextMask)=(history.nextMask,history.mask);
                    }
                    if(!playback)history.weight+=sumWeight;
                    (accumulation,next)=(next,accumulation);(history.texture,history.next)=(history.next,history.texture);
                }
                history.count=playback && end>=16384?8192+end%8192:end;history.viewProjection=vp;
                Resolve(graph,accumulation,background,resources.activeColorTexture,0,null,playback?5:colour?2:1,grade:grade);
                if(hasToon)CompositeToon(graph,baseColour,baseDepth,maskHistory,resources.activeColorTexture,desc.width,desc.height);
                // Publish the centre sample depth for later transparent surfaces and editor
                // overlays, after the sky has been drawn behind the accumulated coverage.
                var depthDrawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("LassylaDepthOnly"),rd,cd,ld,cd.defaultOpaqueSortFlags);
                var depthList=graph.CreateRendererList(new RendererListParams(rd.cullResults,depthDrawing,new FilteringSettings(RenderQueueRange.opaque,camera.cullingMask)));
                using(var builder=graph.AddRasterRenderPass<DrawData>("Lassyla centre-sample depth",out var depthData)){
                    depthData.list=depthList;depthData.jitter=new Vector4(0,0,moving?1:0,0);builder.UseRendererList(depthList);builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);builder.SetRenderFunc(static(DrawData d,RasterGraphContext ctx)=>{
                        ctx.cmd.SetGlobalVector("_LassylaJitter",d.jitter);ctx.cmd.DrawRendererList(d.list);ctx.cmd.SetGlobalVector("_LassylaJitter",Vector4.zero);
                    });
                }
                #if UNITY_EDITOR
                if(camera.cameraType==CameraType.SceneView && (playback||history.count<total))UnityEditor.EditorApplication.delayCall+=UnityEditor.SceneView.RepaintAll;
                #endif
            }
            sealed class ToonCompositeData { public TextureHandle colour,depth,coverage; public Material material; public Vector4 size; }
            void CompositeToon(RenderGraph graph,TextureHandle colour,TextureHandle depth,TextureHandle coverage,TextureHandle target,int width,int height)
            {
                using var builder=graph.AddRasterRenderPass<ToonCompositeData>("Composite native ToonCel colour through edge coverage",out var d);
                d.colour=colour;d.depth=depth;d.coverage=coverage;d.material=owner.material;d.size=new Vector4(width,height,1f/width,1f/height);
                builder.UseTexture(colour);builder.UseTexture(depth);builder.UseTexture(coverage);
                builder.SetRenderAttachment(target,0,AccessFlags.ReadWrite);
                builder.SetRenderFunc(static(ToonCompositeData d,RasterGraphContext ctx)=>{
                    var p=new MaterialPropertyBlock();p.SetTexture("_ToonColour",d.colour);
                    p.SetTexture("_ToonDepth",d.depth);p.SetTexture("_ToonCoverage",d.coverage);p.SetVector("_ToonTargetSize",d.size);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity,d.material,10,MeshTopology.Triangles,3,1,p);
                });
            }
            void Draw(RenderGraph graph,RendererListHandle list,TextureHandle color,TextureHandle depth,TextureHandle sceneDepth,TextureHandle studioShadow,RTClearFlags clear,float offset,Vector4 jitter)
            {
                using var builder=graph.AddRasterRenderPass<DrawData>("Lassyla EEVEE alpha-hash sample",out var data);
                data.list=list;data.clear=clear;data.offset=offset;data.sceneDepth=sceneDepth;data.jitter=jitter;
                builder.UseRendererList(list);
                builder.SetRenderAttachment(color,0,clear!=RTClearFlags.None?AccessFlags.Write:AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depth,clear==RTClearFlags.All?AccessFlags.Write:AccessFlags.ReadWrite);
                if(studioShadow.IsValid())builder.UseTexture(studioShadow,AccessFlags.Read);
                if(sceneDepth.IsValid())builder.UseTexture(sceneDepth,AccessFlags.Read);
                builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DrawData d,RasterGraphContext ctx)=>{
                    if(d.clear!=RTClearFlags.None)ctx.cmd.ClearRenderTarget(d.clear,Color.clear,1,0);
                    ctx.cmd.SetGlobalFloat(Offset,d.offset);ctx.cmd.SetGlobalFloat(UseDepth,d.sceneDepth.IsValid()?1:0);
                    ctx.cmd.SetGlobalVector("_LassylaJitter",d.jitter);
                    if(d.sceneDepth.IsValid())ctx.cmd.SetGlobalTexture(SceneDepth,d.sceneDepth);
                    ctx.cmd.DrawRendererList(d.list);
                    ctx.cmd.SetGlobalFloat(UseDepth,0);ctx.cmd.SetGlobalFloat(Offset,0);
                    ctx.cmd.SetGlobalVector("_LassylaJitter",Vector4.zero);
                });
            }
            void Resolve(RenderGraph graph,TextureHandle source,TextureHandle previous,TextureHandle target,float weight,Vector4[] taps,int pass=0,
                         TextureHandle background=default,TextureHandle depth=default,TextureHandle sceneDepth=default,Vector4 size=default,Matrix4x4 reprojection=default,bool moving=false,CameraGrade grade=default)
            {
                using var builder=graph.AddRasterRenderPass<BlitData>(pass==0?"Lassyla accumulate coverage":"Lassyla composite coverage",out var data);
                data.source=source;data.previous=previous;data.material=owner.material;data.pass=pass;data.weight=weight;data.taps=taps;
                // Blender's pow2f(exposure) is x*x, NOT exp2f(exposure).
                // With the source exposure 0 the native Film luma weight is constant 1/4.
                data.display=owner.display;data.inverse=owner.inverseDisplay;
                data.background=background;data.depth=depth;data.sceneDepth=sceneDepth;data.size=size;data.reprojection=reprojection;data.moving=moving;
                data.grade=grade;
                if(grade.lut.IsValid()){builder.UseTexture(grade.lut);builder.UseTexture(grade.inverse);}
                data.exposureScale=owner.blenderExposure*owner.blenderExposure;
                builder.UseTexture(source,AccessFlags.Read);if(previous.IsValid())builder.UseTexture(previous,AccessFlags.Read);
                if(pass==4)builder.UseTexture(background,AccessFlags.Read);
                if(pass==4 || pass==9){builder.UseTexture(depth,AccessFlags.Read);builder.UseTexture(sceneDepth,AccessFlags.Read);}
                builder.SetRenderAttachment(target,0,pass==0?AccessFlags.Write:AccessFlags.ReadWrite);builder.AllowPassCulling(false);
                builder.SetRenderFunc(static(BlitData d,RasterGraphContext ctx)=>{
                    var properties=new MaterialPropertyBlock();properties.SetTexture("_Source",d.source);properties.SetFloat("_HistoryWeight",d.weight);
                    properties.SetFloat("_ExposureScale",d.exposureScale);
                    if(d.pass==2 || d.pass==3 || d.pass==5){properties.SetTexture("_Film",d.display);properties.SetTexture("_Inverse",d.inverse);BindGrade(properties,d.grade);}
                    if(d.pass==4)properties.SetTexture("_StudioBackground",d.background);
                    if(d.pass==4 || d.pass==9){
                        properties.SetTexture("_SampleDepth",d.depth);properties.SetTexture("_BackdropDepth",d.sceneDepth);
                        properties.SetVector("_FilmSize",d.size);properties.SetMatrix("_CurrentToPreviousClip",d.reprojection);properties.SetFloat("_ViewMoved",d.moving?1:0);
                    }
                    if(d.previous.IsValid())properties.SetTexture("_History",d.previous);
                    if(d.taps!=null)properties.SetVectorArray("_Taps",d.taps);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity,d.material,d.pass,MeshTopology.Triangles,3,1,properties);
                });
            }
        }
    }
}
