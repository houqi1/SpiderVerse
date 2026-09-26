using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace SpiderVerse.LineArt
{
    // Scene-view camera drags repaint without a player-loop tick, so Time.realtimeSinceStartup
    // stays latched for the whole gesture. The line-art step and the shell global share this clock.
    static class LineArtTime
    {
        static readonly double ToSeconds = 1.0 / System.Diagnostics.Stopwatch.Frequency;
        static readonly long Origin = System.Diagnostics.Stopwatch.GetTimestamp();
        public static double Now => (System.Diagnostics.Stopwatch.GetTimestamp() - Origin) * ToSeconds;
    }

    // Only snapshots cross the worker boundary. Unity scene, skinning and Mesh upload stay on the main thread.
    public sealed class ObjectLineArtFeature : ScriptableRendererFeature
    {
        public struct CameraSample
        {
            public Vector3 position;
            public Vector3 forward;
            public bool orthographic;
            public int generation;
        }

        public Shader strokeShader;
        public enum ExecutionMode { CpuReference, GpuGeometry }
        [Tooltip("GPU geometry replaces CPU skin snapshots, intersections, visibility, chaining and mesh uploads. CPU remains available for comparison and unsupported devices.")]
        public ExecutionMode executionMode;
        public ComputeShader geometryCompute;
        public Shader gpuStrokeShader;
        public LineArtSettings settings = new LineArtSettings();
        public bool showInSceneView = true;
        [SerializeField, HideInInspector] string lastStats;
        public string LastStats => lastStats;
        LineArtPass pass;
        SurfaceNoisePass surfaceNoisePass;
        static readonly int LineArtCameraPositionId = Shader.PropertyToID("_LineArtCameraPosition");
        readonly Dictionary<int, CameraHold> cameraHolds = new Dictionary<int, CameraHold>();
        sealed class CameraHold
        {
            public Vector3 position;
            public Vector3 forward;
            public bool orthographic;
            public double nextUpdate;
            public int generation;
            public int lastPlayFrame = -1;
        }
        // Steps the camera on line art's updateRate and publishes it before the frame draws.
        public void SyncLineArtCamera(Camera camera)
        {
            if (!camera) return;
            int id = camera.GetInstanceID();
            if (!cameraHolds.TryGetValue(id, out var hold))
            {
                hold = new CameraHold();
                cameraHolds.Add(id, hold);
            }
            double now = LineArtTime.Now;
            // Animator stepping and render passes can request this sample far apart in
            // one slow frame. They must still observe the same generation in Play mode.
            bool canAdvance = !Application.isPlaying || hold.lastPlayFrame != Time.frameCount;
            if (canAdvance && (hold.generation == 0 || now >= hold.nextUpdate))
            {
                hold.position = camera.transform.position;
                hold.forward = camera.transform.forward;
                hold.orthographic = camera.orthographic;
                hold.nextUpdate = now + 1.0 / Math.Max(1, settings.updateRate);
                hold.generation++;
            }
            if (Application.isPlaying) hold.lastPlayFrame = Time.frameCount;
            Vector3 p = hold.position;
            Shader.SetGlobalVector(LineArtCameraPositionId, new Vector4(p.x, p.y, p.z, 1f));
        }
        public int LineArtCameraGeneration(Camera camera) => camera && cameraHolds.TryGetValue(camera.GetInstanceID(), out var hold) ? hold.generation : 0;
        public double LineArtCameraNextUpdate(Camera camera) => camera && cameraHolds.TryGetValue(camera.GetInstanceID(), out var hold) ? hold.nextUpdate : 0;

        public bool TryGetLineArtCameraSample(Camera camera, out CameraSample sample)
        {
            sample = default;
            if (!camera || !cameraHolds.TryGetValue(camera.GetInstanceID(), out var hold))
                return false;

            sample = new CameraSample
            {
                position = hold.position,
                forward = hold.forward,
                orthographic = hold.orthographic,
                generation = hold.generation
            };
            return true;
        }

        // Resolve the feature for this camera and step the shared hold so line art and
        // surface particles consume the identical per-camera sample and update rate.
        public static bool TryGetCameraSample(Camera camera, out CameraSample sample)
        {
            sample = default;
            if (!camera || UniversalRenderPipeline.asset == null)
                return false;

            UniversalRenderPipelineAsset pipeline = UniversalRenderPipeline.asset;
            UniversalAdditionalCameraData additionalData = camera.GetComponent<UniversalAdditionalCameraData>();
            ScriptableRenderer selectedRenderer = additionalData != null
                ? additionalData.scriptableRenderer
                : pipeline.scriptableRenderer;
            if (selectedRenderer == null)
                return false;

            var rendererDataList = pipeline.rendererDataList;
            for (int rendererIndex = 0; rendererIndex < rendererDataList.Length; rendererIndex++)
            {
                ScriptableRendererData rendererData = rendererDataList[rendererIndex];
                if (rendererData == null || pipeline.GetRenderer(rendererIndex) != selectedRenderer)
                    continue;

                List<ScriptableRendererFeature> features = rendererData.rendererFeatures;
                for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
                {
                    if (!(features[featureIndex] is ObjectLineArtFeature feature) || !feature.isActive)
                        continue;

                    feature.SyncLineArtCamera(camera);
                    return feature.TryGetLineArtCameraSample(camera, out sample);
                }
            }

            return false;
        }

        public void InvalidateGpuGeometry()=>pass?.InvalidateGpuGeometry();
        // URP calls Create from OnValidate for every Inspector edit. Keep camera meshes
        // and in-flight work alive; actual renderer disposal still releases them.
        public override void Create() {
            if(pass==null)pass=new LineArtPass(this);
            if(surfaceNoisePass==null)surfaceNoisePass=new SurfaceNoisePass(this);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            var c=renderingData.cameraData;
            if(c.cameraType!=CameraType.Game && !(showInSceneView&&c.cameraType==CameraType.SceneView))return;
            if(c.renderType==CameraRenderType.Overlay)return;
            SyncLineArtCamera(c.camera);
            if(surfaceNoisePass!=null && SurfaceNoiseParticleEffect.ActiveEffects.Count>0)
                renderer.EnqueuePass(surfaceNoisePass);
            if(ObjectLineArtSource.Active.Count==0)return;
            if(pass!=null)renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) {pass?.Dispose();pass=null;surfaceNoisePass?.Dispose();surfaceNoisePass=null;}

        sealed class SurfaceNoisePass : ScriptableRenderPass
        {
            sealed class PassData
            {
                public Camera camera;
                public CameraSample cameraSample;
                public SurfaceNoiseParticleEffect[] effects;
                public TextureHandle opaqueColor;
                public TextureHandle color;
                public TextureHandle depth;
                public TextureHandle cameraDepth;
            }

            readonly ObjectLineArtFeature owner;
            readonly SurfaceNoiseColorField colorField = new SurfaceNoiseColorField();

            public void Dispose() => colorField.Dispose();

            public SurfaceNoisePass(ObjectLineArtFeature owner)
            {
                this.owner=owner;
                renderPassEvent=RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                var cameraData=frameData.Get<UniversalCameraData>();
                var resources=frameData.Get<UniversalResourceData>();
                if(!resources.cameraOpaqueTexture.IsValid() || !resources.activeColorTexture.IsValid() ||
                    !resources.cameraDepthTexture.IsValid() ||
                    SurfaceNoiseParticleEffect.ActiveEffects.Count==0)return;

                Camera camera=cameraData.camera;
                CameraSample sample;
                if(!owner.TryGetLineArtCameraSample(camera,out sample))
                    sample=new CameraSample{position=camera.transform.position,forward=camera.transform.forward,orthographic=camera.orthographic};

                if(colorField.Record(graph,cameraData,resources,SurfaceNoiseParticleEffect.ActiveEffects.ToArray(),sample))return;

                using(var builder=graph.AddUnsafePass<PassData>("Surface Noise · color matched particles",out var data))
                {
                    data.camera=camera;
                    data.cameraSample=sample;
                    data.effects=SurfaceNoiseParticleEffect.ActiveEffects.ToArray();
                    data.opaqueColor=resources.cameraOpaqueTexture;
                    data.color=resources.activeColorTexture;
                    data.depth=resources.activeDepthTexture;
                    data.cameraDepth=resources.cameraDepthTexture;
                    builder.UseTexture(data.opaqueColor,AccessFlags.Read);
                    builder.UseTexture(data.color,AccessFlags.ReadWrite);
                    builder.UseTexture(data.depth,AccessFlags.ReadWrite);
                    builder.UseTexture(data.cameraDepth,AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData d,UnsafeGraphContext context)=>
                    {
                        context.cmd.SetRenderTarget(d.color,d.depth);
                        var commandBuffer=CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        commandBuffer.SetGlobalFloat("_SurfaceColorFieldEnabled",0);
                        for(int i=0;i<d.effects.Length;i++)
                        {
                            var effect=d.effects[i];
                            if(effect!=null && effect.isActiveAndEnabled)
                                effect.DrawFromRenderPass(commandBuffer,d.camera,d.cameraSample);
                        }
                    });
                }
            }
        }

        sealed class LineArtPass : ScriptableRenderPass
        {
            readonly ObjectLineArtFeature owner;
            readonly Dictionary<int,CameraState> cameras=new Dictionary<int,CameraState>();
            readonly Dictionary<Mesh,LineArtGeometry.Topology> topology=new Dictionary<Mesh,LineArtGeometry.Topology>();
            readonly Dictionary<SkinnedMeshRenderer,Mesh> baked=new Dictionary<SkinnedMeshRenderer,Mesh>();
            readonly HashSet<int> unreadable=new HashSet<int>();
            readonly Dictionary<int,LineArtGpu> gpuCameras=new Dictionary<int,LineArtGpu>();
            readonly Dictionary<int,LineArtCpuLayers> cpuLayers=new Dictionary<int,LineArtCpuLayers>();
            bool gpuWarning;
            Renderer[] sceneRenderers=Array.Empty<Renderer>();double nextScan;
            sealed class CameraState
            {
                public Camera camera;public Mesh mesh,stagingMesh;public Material material;public Task<LineArtGeometry.Result> pending;
                public CancellationTokenSource cancel=new CancellationTokenSource();public double nextUpdate;public int geometryHash;
                public readonly LineArtGeometry.IntersectionCache intersections = new LineArtGeometry.IntersectionCache();
                public string sourceSignature;public bool ready, hasSnapshot, deferredCapture, repaintQueued;public int snapshotHash, cameraGeneration;
            }
            sealed class PassData {public Mesh mesh;public Material material;public MaterialPropertyBlock properties;}
            sealed class GpuPassData {public LineArtGpu gpu;public Camera camera;public int width,height;public LineArtSettings settings;public TextureHandle color,depth;public ObjectLineArtFeature owner;}
            LineArtGpu PrepareGpu(Camera camera)
            {
                if(owner.executionMode!=ExecutionMode.GpuGeometry)return null;
                if(!SystemInfo.supportsComputeShaders||!owner.geometryCompute||!owner.gpuStrokeShader){if(!gpuWarning){Debug.LogWarning("Line Art GPU resources/compute support unavailable; using CPU reference.");gpuWarning=true;}return null;}
                int id=camera.GetInstanceID();
                if(!gpuCameras.TryGetValue(id,out var gpu)){gpu=new LineArtGpu(owner.geometryCompute,owner.gpuStrokeShader);gpuCameras.Add(id,gpu);}
                if(gpu.Failed)return null;
                if(cpuLayers.TryGetValue(id,out var layered))layered.Suspend();
                if(cameras.TryGetValue(id,out var old)){old.deferredCapture=false;old.repaintQueued=false;if(old.pending!=null){old.cancel.Cancel();old.cancel.Dispose();old.cancel=new CancellationTokenSource();old.pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);old.pending=null;old.hasSnapshot=false;}}
                try {gpu.UseCameraStep(owner.LineArtCameraGeneration(camera),owner.LineArtCameraNextUpdate(camera));return gpu.Prepare(camera,owner.settings)?gpu:null;}
                catch(Exception e){gpu.MarkFailed();if(!gpuWarning){Debug.LogWarning("Line Art GPU setup failed; using CPU reference: "+e);gpuWarning=true;}return null;}
            }
            public LineArtPass(ObjectLineArtFeature owner) {
                this.owner=owner;renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update += EditorUpdate;
#endif
            }
#if UNITY_EDITOR
            // Edit mode has no continuous Game/Scene render loop. Request a frame only
            // for finished work or a throttled snapshot, never repaint continuously while idle.
            void EditorUpdate()
            {
                if(Application.isBatchMode || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || !owner || !owner.isActive || ObjectLineArtSource.Active.Count==0)return;
                double now=LineArtTime.Now;bool repaint=false;
                foreach(var state in cameras.Values) {
                    if(!state.camera || state.repaintQueued)continue;
                    if(state.pending!=null && state.pending.IsCompleted || state.deferredCapture && now>=state.nextUpdate) {
                        state.repaintQueued=true;repaint=true;
                    }
                }
                foreach(var gpu in gpuCameras.Values)if(gpu.RequestDeferredRepaint(now))repaint=true;
                foreach(var layered in cpuLayers.Values)if(layered.RequestRepaint(now))repaint=true;
                if(!repaint)return;
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
#endif
            static string Path(Transform t) {string key=t.name+":"+t.GetSiblingIndex();while(t.parent){t=t.parent;key=t.name+":"+t.GetSiblingIndex()+"/"+key;}return key;}
            static Mesh SourceMesh(Renderer r) => r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.TryGetComponent<MeshFilter>(out var f)?f.sharedMesh:null;
            LineArtGeometry.Snapshot[] Capture(Camera camera,HashSet<Renderer> targets,LineArtSettings settings)
            {
                double now=Time.realtimeSinceStartupAsDouble;
                if(now>=nextScan){sceneRenderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);nextScan=now+1;}
                var renderers=new HashSet<Renderer>(targets);
                if(settings.occlusion||settings.intersections)foreach(var r in sceneRenderers)
                    if(r && ((1<<r.gameObject.layer)&settings.occluderLayers.value)!=0)renderers.Add(r);
                var ordered=renderers.Where(r=>r && r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && ((1<<r.gameObject.layer)&camera.cullingMask)!=0 && (r is MeshRenderer || r is SkinnedMeshRenderer)).OrderBy(r=>r.GetInstanceID());
                var result=new List<LineArtGeometry.Snapshot>();
                foreach(var r in ordered) {
                    var mesh=SourceMesh(r);if(!mesh)continue;
                    if(!mesh.isReadable) {
                        if(unreadable.Add(mesh.GetInstanceID()))Debug.LogWarning($"Object Line Art: enable Read/Write on mesh '{mesh.name}' to include it in geometric line extraction/occlusion.",r);
                        continue;
                    }
                    if(!topology.TryGetValue(mesh,out var t)) {
                        var submeshes=new int[mesh.subMeshCount][];
                        for(int i=0;i<submeshes.Length;i++)submeshes[i]=mesh.GetTopology(i)==MeshTopology.Triangles?mesh.GetIndices(i):Array.Empty<int>();
                        t=LineArtGeometry.Prepare(mesh.vertices,submeshes);topology.Add(mesh,t);
                    }
                    Vector3[] source;
                    if(r is SkinnedMeshRenderer sk) {
                        if(!baked.TryGetValue(sk,out var bm)){bm=new Mesh{name="Line Art skin snapshot",hideFlags=HideFlags.HideAndDontSave};baked.Add(sk,bm);}
                        // Unity 6: compensate the skinned Transform scale before applying localToWorld.
                        // Without this, FBX rigs with import/bone scale produce displaced, oversized strokes.
                        sk.BakeMesh(bm,true);source=bm.vertices;
                    } else source=mesh.vertices;
                    var world=r.localToWorldMatrix;var vertices=new Vector3[t.representatives.Length];
                    for(int i=0;i<vertices.Length;i++)vertices[i]=world.MultiplyPoint3x4(source[t.representatives[i]]);
                    var materials=r.sharedMaterials;var cull=new int[mesh.subMeshCount];var opaque=new bool[mesh.subMeshCount];
                    for(int i=0;i<cull.Length;i++) {
                        var m=materials.Length>0?materials[Math.Min(i,materials.Length-1)]:null;
                        cull[i]=m&&m.HasProperty("_Cull")?Mathf.RoundToInt(m.GetFloat("_Cull")):2;
                        opaque[i]=m&&m.renderQueue<=2500;
                    }
                    result.Add(new LineArtGeometry.Snapshot{rendererId=r.GetInstanceID(),id=unchecked((int)LineArtGeometry.Hash(r.gameObject.scene.path+"/"+Path(r.transform))),draw=targets.Contains(r),topology=t,vertices=vertices,cull=cull,opaque=opaque,orientation=world.determinant<0?-1:1});
                }
                return result.ToArray();
            }
            static int SnapshotHash(LineArtGeometry.Snapshot[] snapshots, LineArtGeometry.View view)
            {
                unchecked {
                    int h=view.matrix.GetHashCode()*31+view.width;h=h*31+view.height;
                    foreach(var s in snapshots) {
                        h=h*31+s.id;h=h*31+(s.draw?1:0);h=h*31+s.orientation.GetHashCode();
                        foreach(var p in s.vertices)h=h*31+p.GetHashCode();
                        foreach(int c in s.cull)h=h*31+c;
                        foreach(bool o in s.opaque)h=h*31+(o?1:0);
                    } return h;
                }
            }
            CameraState Update(Camera camera,int width,int height)
            {
                if(!owner.strokeShader)owner.strokeShader=Shader.Find("Hidden/SpiderVerse/ObjectLineArt");
                if(!owner.strokeShader)return null;
                int id=camera.GetInstanceID();
                if(!cameras.TryGetValue(id,out var state)) {
                    state=new CameraState{camera=camera,mesh=new Mesh{name="Object Line Art strokes",indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave},material=CoreUtils.CreateEngineMaterial(owner.strokeShader)};
                    state.mesh.MarkDynamic();
                    state.stagingMesh=new Mesh{name="Object Line Art pending strokes",indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};
                    state.stagingMesh.MarkDynamic();cameras.Add(id,state);
                }
                state.repaintQueued=false;
                var targets=new HashSet<Renderer>();
                foreach(var source in ObjectLineArtSource.Active)if(source&&source.isActiveAndEnabled)foreach(var r in source.GetRenderers())if(r)targets.Add(r);
                string signature=string.Join(",",targets.Where(r=>r&&r.enabled&&r.gameObject.activeInHierarchy).Select(r=>r.GetInstanceID()).OrderBy(i=>i));
                var s=owner.settings;int hash=s.GeometryHash();
                if(state.sourceSignature!=signature || state.geometryHash!=hash) {
                    state.cancel.Cancel();state.cancel.Dispose();state.cancel=new CancellationTokenSource();
                    // Observe faults on discarded work, without blocking the render thread.
                    if(state.pending!=null)state.pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                    state.pending=null;
                    // Parameter edits keep the last complete drawing until its replacement is ready.
                    // A changed object list must not leave removed objects outlined.
                    if(state.sourceSignature!=signature)state.ready=false;
                    state.hasSnapshot=false;state.nextUpdate=0;state.sourceSignature=signature;state.geometryHash=hash;
                }
                if(state.pending!=null&&state.pending.IsCompleted) {
                    if(state.pending.Status==TaskStatus.RanToCompletion) {
                        var data=state.pending.Result;var mesh=state.stagingMesh;mesh.Clear();
                        mesh.SetVertices(data.positions);mesh.SetUVs(0,data.previous);mesh.SetUVs(1,data.next);mesh.SetUVs(2,data.stroke);
                        mesh.SetIndices(data.indices,MeshTopology.Triangles,0,false);mesh.RecalculateBounds();
                        // Upload completely before publishing the new drawing, including valid empty results.
                        state.stagingMesh=state.mesh;state.mesh=mesh;state.ready=data.indices.Length>0;
                        owner.lastStats=$"{data.chains} strokes / {data.spans} visible spans / {data.candidates} candidates · worker {data.milliseconds:F1} ms";
                    } else if(state.pending.IsFaulted)Debug.LogException(state.pending.Exception);
                    state.pending=null;
                }
                int cameraGeneration=owner.LineArtCameraGeneration(camera);
                state.nextUpdate=owner.LineArtCameraNextUpdate(camera);
                bool cameraDue=cameraGeneration!=state.cameraGeneration||!state.hasSnapshot;
                state.deferredCapture=state.pending==null&&!cameraDue;
                if(state.pending==null&&cameraDue) {
                    var snapshot=Capture(camera,targets,s);
                    var view=new LineArtGeometry.View{matrix=camera.projectionMatrix*camera.worldToCameraMatrix,position=camera.transform.position,toCamera=-camera.transform.forward,perspective=!camera.orthographic,width=width,height=height};
                    int snapshotHash = SnapshotHash(snapshot, view);
                    if(!state.hasSnapshot || snapshotHash != state.snapshotHash) {
                        var copy=s.Copy();var token=state.cancel.Token;int modelHash=SnapshotHash(snapshot,default);
                        state.pending=Task.Run(()=>LineArtGeometry.Build(snapshot,view,copy,token,state.intersections,modelHash),token);
                        state.snapshotHash=snapshotHash;state.hasSnapshot=true;
                    }
                    state.cameraGeneration=cameraGeneration;
                }
                var m=state.material;if(m.shader!=owner.strokeShader)m.shader=owner.strokeShader;
                m.SetFloat("_DepthOffset",s.depthOffset);m.SetColor("_Color",s.color);m.SetVector("_Resolution",new Vector4(width,height,0,0));
                m.SetFloat("_Width",s.thickness);m.SetFloat("_WorldSizeUnit",s.scaleWithDistance?Mathf.Max(.00001f,s.sizeUnit):0);m.SetFloat("_Taper",s.thicknessCurve?s.endTaper:0);m.SetFloat("_Transition",s.thicknessTransition);
                m.SetFloat("_Noise",s.noise);m.SetFloat("_NoiseFrequency",Mathf.Clamp(s.noiseFrequency,.1f,32));m.SetVector("_Offset",s.offset);m.SetVector("_RandomOffset",s.randomOffset);m.SetTexture("_StrokeTex",s.texture?s.texture:Texture2D.whiteTexture);
                m.SetVector("_TextureST",new Vector4(s.textureTiling.x,s.textureTiling.y,s.textureOffset.x,s.textureOffset.y));
                float textureAngle=s.textureRotation*Mathf.Deg2Rad;
                m.SetVector("_TextureRotation",new Vector4(Mathf.Cos(textureAngle),Mathf.Sin(textureAngle),0,0));
                m.SetFloat("_HasTexture",s.texture?1:0);m.SetFloat("_TextureStrength",s.textureStrength);m.SetFloat("_TextureRepeat",s.textureRepeats);m.SetFloat("_TextureMask",s.darkOnWhiteMask?1:0);
                return state;
            }
            LineArtCpuLayers UpdateLayers(Camera camera,int width,int height)
            {
                if(!owner.strokeShader)owner.strokeShader=Shader.Find("Hidden/SpiderVerse/ObjectLineArt");
                if(!owner.strokeShader)return null;
                int id=camera.GetInstanceID();if(!cpuLayers.TryGetValue(id,out var state)){state=new LineArtCpuLayers(camera,owner.strokeShader,Capture,SnapshotHash);cpuLayers.Add(id,state);}
                if(cameras.TryGetValue(id,out var legacy)){legacy.deferredCapture=false;if(legacy.pending!=null){legacy.cancel.Cancel();legacy.cancel.Dispose();legacy.cancel=new CancellationTokenSource();legacy.pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);legacy.pending=null;legacy.hasSnapshot=false;}}
                state.Update(owner.settings,owner.strokeShader,width,height,owner.LineArtCameraGeneration(camera),owner.LineArtCameraNextUpdate(camera));owner.lastStats=state.Stats;return state;
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                var camera=frameData.Get<UniversalCameraData>();var resources=frameData.Get<UniversalResourceData>();
                var gpu=PrepareGpu(camera.camera);
                if(gpu!=null){
                    using(var builder=graph.AddUnsafePass<GpuPassData>("Object Line Art · GPU geometry",out var data)){
                        data.gpu=gpu;data.camera=camera.camera;data.width=camera.cameraTargetDescriptor.width;data.height=camera.cameraTargetDescriptor.height;data.settings=owner.settings.Copy();data.color=resources.activeColorTexture;data.depth=resources.activeDepthTexture;data.owner=owner;
                        builder.UseTexture(data.color,AccessFlags.ReadWrite);builder.UseTexture(data.depth,AccessFlags.Read);builder.AllowPassCulling(false);
                        builder.SetRenderFunc((GpuPassData d,UnsafeGraphContext context)=>{context.cmd.SetRenderTarget(d.color,d.depth);var cmd=CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);d.gpu.UseCameraStep(d.owner.LineArtCameraGeneration(d.camera),d.owner.LineArtCameraNextUpdate(d.camera));d.gpu.Execute(cmd,d.camera,d.width,d.height,d.settings);d.owner.lastStats=d.gpu.Stats;});
                    }return;
                }
                if(ObjectLineArtSource.HasLayers){
                    var layered=UpdateLayers(camera.camera,camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height);if(layered==null)return;
                    foreach(var drawing in layered.drawings)using(var builder=graph.AddRasterRenderPass<PassData>("Object Line Art · layer",out var data)){
                        data.mesh=drawing.mesh;data.material=drawing.material;data.properties=drawing.properties;builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.Read);
                        builder.SetRenderFunc((PassData d,RasterGraphContext context)=>context.cmd.DrawMesh(d.mesh,Matrix4x4.identity,d.material,0,0,d.properties));
                    }return;
                }
                if(cpuLayers.TryGetValue(camera.camera.GetInstanceID(),out var oldLayers))oldLayers.Suspend();
                var state=Update(camera.camera,camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height);
                if(state==null||!state.ready)return;
                using(var builder=graph.AddRasterRenderPass<PassData>("Object Line Art · geometric strokes",out var data)) {
                    data.mesh=state.mesh;data.material=state.material;
                    builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.Read);
                    builder.SetRenderFunc((PassData d,RasterGraphContext context)=>context.cmd.DrawMesh(d.mesh,Matrix4x4.identity,d.material,0,0));
                }
            }
            [Obsolete("Compatibility path; Unity 6 uses RecordRenderGraph.")]
            public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData renderingData)
            {
                var renderer=renderingData.cameraData.renderer;
                ConfigureTarget(renderer.cameraColorTargetHandle,renderer.cameraDepthTargetHandle);
            }
            // Compatibility mode is supported as well; default Unity 6 path is RenderGraph.
            [Obsolete("Compatibility path; Unity 6 uses RecordRenderGraph.")]
            public override void Execute(ScriptableRenderContext context,ref RenderingData renderingData)
            {
                var camera=renderingData.cameraData;
                var gpu=PrepareGpu(camera.camera);if(gpu!=null){var gc=CommandBufferPool.Get("Object Line Art GPU");gpu.UseCameraStep(owner.LineArtCameraGeneration(camera.camera),owner.LineArtCameraNextUpdate(camera.camera));gpu.Execute(gc,camera.camera,camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height,owner.settings);context.ExecuteCommandBuffer(gc);CommandBufferPool.Release(gc);owner.lastStats=gpu.Stats;return;}
                if(ObjectLineArtSource.HasLayers){var layered=UpdateLayers(camera.camera,camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height);if(layered==null)return;var layeredCmd=CommandBufferPool.Get("Object Line Art layers");foreach(var d in layered.drawings)layeredCmd.DrawMesh(d.mesh,Matrix4x4.identity,d.material,0,0,d.properties);context.ExecuteCommandBuffer(layeredCmd);CommandBufferPool.Release(layeredCmd);return;}
                if(cpuLayers.TryGetValue(camera.camera.GetInstanceID(),out var oldLayers))oldLayers.Suspend();
                var state=Update(camera.camera,camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height);
                if(state==null||!state.ready)return;
                var cmd=CommandBufferPool.Get("Object Line Art");cmd.DrawMesh(state.mesh,Matrix4x4.identity,state.material,0,0);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
            public void InvalidateGpuGeometry(){foreach(var gpu in gpuCameras.Values)gpu.Invalidate();gpuWarning=false;}
            public void Dispose()
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
                foreach(var s in cameras.Values){s.cancel.Cancel();s.cancel.Dispose();if(s.pending!=null)s.pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);CoreUtils.Destroy(s.mesh);CoreUtils.Destroy(s.stagingMesh);CoreUtils.Destroy(s.material);}
                foreach(var mesh in baked.Values)CoreUtils.Destroy(mesh);baked.Clear();cameras.Clear();topology.Clear();
                foreach(var layered in cpuLayers.Values)layered.Dispose();cpuLayers.Clear();
                foreach(var gpu in gpuCameras.Values)gpu.Dispose();gpuCameras.Clear();
            }
        }
    }
}
