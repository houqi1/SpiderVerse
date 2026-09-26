using System;
using System.Collections.Generic;
using SpiderVerse.LineArt;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>Per-camera sample-and-hold motion vectors shared by noise rotation and debug views.</summary>
public sealed class MotionVectorNoiseFeature : ScriptableRendererFeature
{
    public enum UpdateMode { SyncWithAnimation, FixedRate }

    public Material passMaterial;
    [Tooltip("Sync With Animation saves a whole motion-vector sample at each animation tick and publishes the preceding animation sample. Fixed Rate publishes the current frame's sample.")]
    public UpdateMode updateMode = UpdateMode.FixedRate;
    [Tooltip("Updates per second in Fixed Rate mode, or when no active Line Art feature is available.")]
    [Range(1, 60)] public float updateRate = 12;
    [Tooltip("Optional per-pixel history, which can leave trails at old screen positions. Keep disabled for whole-texture snapshots that replace the previous sample at every update.")]
    public bool holdLastDirection = false;
    [Tooltip("0 holds indefinitely. Otherwise inactive pixels return to the default direction after this many seconds, at the next publication tick.")]
    [Range(0, 30)] public float resetAfterSeconds;
    public bool showInSceneView = true;
    [Tooltip("Reset history when the camera moves farther than this in one rendered frame. 0 disables position cut detection.")]
    [Min(0)] public float cameraCutDistance = 5;
    [Range(1, 180)] public float cameraCutAngle = 45;

    NoisePass pass;
    int revision;

    public override void Create()
    {
        if (pass == null) pass = new NoisePass(this);
        // Inspector validation calls Create repeatedly. Retain live histories;
        // material/size/source changes and explicit resets are handled below.
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraData = renderingData.cameraData;
        // Camera stacks share the base camera's result. Overlays must not composite twice.
        if (cameraData.renderType == CameraRenderType.Overlay || passMaterial == null || passMaterial.passCount < 2)
            return;
        if (cameraData.cameraType != CameraType.Game &&
            !(showInSceneView && cameraData.cameraType == CameraType.SceneView)) return;
        if (pass == null) Create();
        pass.PruneDestroyedCameras();
        renderer.EnqueuePass(pass);
    }

    [ContextMenu("Reset Direction History")]
    public void ResetDirectionHistory() => revision++;

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
        pass = null;
    }

    sealed class CameraHistory
    {
        public Camera camera;
        public RTHandle read, write, published;
        public readonly MotionDirectionUpdateClock clock = new MotionDirectionUpdateClock();
        public bool valid, synchronized, hasSample, wasPlaying;
        public int sampleSourceId;
        public Material material;
        public int revision = -1, lastFrame = -1;
        public double lastTime;
        public Vector3 position;
        public Quaternion rotation;
        public Matrix4x4 projection;
        public float threshold;

        public void Dispose()
        {
            read?.Release(); write?.Release(); published?.Release();
            read = write = published = null;
            valid = false;
        }
    }

    sealed class NoisePass : ScriptableRenderPass
    {
        readonly MotionVectorNoiseFeature owner;
        readonly Dictionary<int, CameraHistory> histories = new Dictionary<int, CameraHistory>();
        readonly List<int> deadCameras = new List<int>();
        int editorFrame;
        static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly int CacheTexture = Shader.PropertyToID("_CachedMotionDirections");
        static readonly int UseCache = Shader.PropertyToID("_UseCachedMotionDirections");
        static readonly int CacheValid = Shader.PropertyToID("_CacheValid");
        static readonly int CacheTime = Shader.PropertyToID("_CacheTime");
        static readonly int HoldDirection = Shader.PropertyToID("_HoldLastDirection");
        static readonly int ResetAfter = Shader.PropertyToID("_ResetDirectionAfter");

        public NoisePass(MotionVectorNoiseFeature owner)
        {
            this.owner = owner;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Motion);
            profilingSampler = new ProfilingSampler("Motion Vector Noise");
        }

        public void PruneDestroyedCameras()
        {
            deadCameras.Clear();
            foreach (var pair in histories)
                if (!pair.Value.camera) deadCameras.Add(pair.Key);
            foreach (int id in deadCameras)
            {
                histories[id].Dispose();
                histories.Remove(id);
            }
        }

        public void Dispose()
        {
            foreach (var history in histories.Values) history.Dispose();
            histories.Clear();
        }

        struct FramePlan
        {
            public CameraHistory history;
            public RTHandle previous, next;
            public bool collect, publish, reset, publishPrevious;
            public float time;
        }

        FramePlan Prepare(Camera camera, RenderTextureDescriptor descriptor)
        {
            int id = camera.GetInstanceID();
            if (!histories.TryGetValue(id, out var history))
            {
                history = new CameraHistory { camera = camera };
                histories.Add(id, history);
            }

            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.enableRandomWrite = false;
            descriptor.useDynamicScale = false;
            // read/write swap after collection. URP includes the handle name in
            // its reallocation check, so keep each physical texture's name when
            // its role changes. Renaming by role reallocates both every frame,
            // which resets the publication clock and defeats the update rate.
            string readName = history.read?.name ?? "Motion Direction Candidate A";
            string writeName = history.write?.name ?? "Motion Direction Candidate B";
            bool resized = RenderingUtils.ReAllocateHandleIfNeeded(ref history.read, descriptor, FilterMode.Point,
                TextureWrapMode.Clamp, name: readName);
            resized |= RenderingUtils.ReAllocateHandleIfNeeded(ref history.write, descriptor, FilterMode.Point,
                TextureWrapMode.Clamp, name: writeName);
            resized |= RenderingUtils.ReAllocateHandleIfNeeded(ref history.published, descriptor, FilterMode.Point,
                TextureWrapMode.Clamp, name: "Motion Direction Published");

            double now = Application.isPlaying ? Time.unscaledTimeAsDouble : LineArtTime.Now;
            int frame = Application.isPlaying ? Time.frameCount : ++editorFrame;
            float threshold = owner.passMaterial.GetFloat("_MotionThreshold");
            bool cut = history.valid && (
                (owner.cameraCutDistance > 0 && Vector3.Distance(camera.transform.position, history.position) > owner.cameraCutDistance) ||
                Quaternion.Angle(camera.transform.rotation, history.rotation) > owner.cameraCutAngle ||
                ProjectionChanged(camera.nonJitteredProjectionMatrix, history.projection));
            bool reset = !history.valid || resized || history.revision != owner.revision || cut ||
                         threshold != history.threshold || now < history.lastTime ||
                         history.wasPlaying != Application.isPlaying || history.material != owner.passMaterial;
            // Scene repainting is intermittent, including while the game plays.
            // Missing a player frame or hiding the view is not a history reset.
            if (camera.cameraType != CameraType.SceneView)
                reset |= now - history.lastTime > 1.0 ||
                         (Application.isPlaying && history.lastFrame != frame && history.lastFrame != frame - 1);

            bool synchronized = false;
            int generation = 0;
            int sampleSourceId = 0;
            bool canCapture = true;
            if (owner.updateMode == UpdateMode.SyncWithAnimation)
            {
                if (LineArtSteppedAnimator.TryGetMotionSample(camera, out var pose))
                {
                    synchronized = true;
                    generation = pose.generation;
                    sampleSourceId = pose.sourceId;
                    // A late Scene repaint may first see a generation after its
                    // motion pulse has passed. Keep the whole existing snapshot
                    // until a pose update is actually rendered; never cache that
                    // intervening held-pose frame as the animation sample.
                    canCapture = pose.frame == Time.frameCount;
                }
                else if (ObjectLineArtFeature.TryGetCameraSample(camera, out var sample))
                {
                    synchronized = true;
                    generation = sample.generation;
                    sampleSourceId = camera.GetInstanceID();
                }
            }

            // A per-frame candidate is not a previous animation sample. Rebuild
            // history when entering/leaving synchronization (including fallback).
            reset |= history.valid && (history.synchronized != synchronized ||
                                      history.sampleSourceId != sampleSourceId);
            bool publish = (reset || canCapture) &&
                history.clock.Tick(now, frame, synchronized, generation, owner.updateRate, reset);

            var plan = new FramePlan
            {
                history = history, previous = history.read, next = history.write,
                // Held-pose render frames commonly contain zero MV. In sync mode
                // they must not overwrite the snapshot captured at the last tick.
                collect = synchronized ? publish : reset || history.lastFrame != frame,
                reset = reset,
                // In sync mode previous is the last animation tick's snapshot.
                // On first use/reset there is no valid preceding sample.
                publishPrevious = synchronized && !reset && history.hasSample,
                time = (float)(now % 64.0),
                publish = publish
            };
            if (plan.collect)
            {
                // Graph pass data captures the handles before swapping; all GPU
                // dependencies are declared explicitly in RecordRenderGraph.
                history.read = plan.next;
                history.write = plan.previous;
            }
            history.valid = true;
            history.synchronized = synchronized;
            history.sampleSourceId = sampleSourceId;
            history.wasPlaying = Application.isPlaying;
            history.material = owner.passMaterial;
            if (reset) history.hasSample = false;
            if (plan.collect && canCapture) history.hasSample = true;
            history.revision = owner.revision;
            history.lastFrame = frame;
            history.lastTime = now;
            history.position = camera.transform.position;
            history.rotation = camera.transform.rotation;
            history.projection = camera.nonJitteredProjectionMatrix;
            history.threshold = threshold;
            return plan;
        }

        static bool ProjectionChanged(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 0.01f) return true;
            return false;
        }

        MaterialPropertyBlock Properties(FramePlan plan, bool composite)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
            properties.SetFloat(UseCache, composite ? 1 : 0);
            if (composite)
                properties.SetTexture(CacheTexture, plan.history.published.rt);
            else
            {
                properties.SetTexture(BlitTexture, plan.previous.rt);
                properties.SetFloat(CacheValid, plan.reset ? 0 : 1);
                properties.SetFloat(CacheTime, plan.time);
                properties.SetFloat(HoldDirection, owner.holdLastDirection ? 1 : 0);
                properties.SetFloat(ResetAfter, Mathf.Clamp(owner.resetAfterSeconds, 0, 30));
            }
            return properties;
        }

        sealed class DrawData
        {
            public Material material;
            public MaterialPropertyBlock properties;
            public int passIndex;
        }

        sealed class CopyData { public TextureHandle source; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (!resources.motionVectorColor.IsValid() || !resources.activeColorTexture.IsValid()) return;
            var plan = Prepare(cameraData.camera, cameraData.cameraTargetDescriptor);
            var published = renderGraph.ImportTexture(plan.history.published);
            // Reuse this graph handle for collection and previous-sample publication.
            var previous = renderGraph.ImportTexture(plan.previous);
            var collected = TextureHandle.nullHandle;

            if (plan.collect)
            {
                var next = renderGraph.ImportTexture(plan.next);
                collected = next;
                using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Collect Motion Directions", out var data))
                {
                    data.material = owner.passMaterial;
                    data.properties = Properties(plan, false);
                    data.passIndex = 1;
                    builder.UseTexture(resources.motionVectorColor, AccessFlags.Read);
                    builder.UseTexture(previous, AccessFlags.Read);
                    builder.SetRenderAttachment(next, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext context) =>
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.passIndex, MeshTopology.Triangles, 3, 1, d.properties));
                }
            }

            if (plan.publish)
            {
                using (var builder = renderGraph.AddRasterRenderPass<CopyData>("Publish Motion Directions", out var data))
                {
                    // Sync publishes the preceding animation sample. This tick's
                    // snapshot stays in the other buffer until the next tick.
                    data.source = plan.publishPrevious || !plan.collect ? previous : collected;
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.SetRenderAttachment(published, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc((CopyData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false));
                }
            }

            using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Motion Vector Noise", out var data))
            {
                data.material = owner.passMaterial;
                data.properties = Properties(plan, true);
                data.passIndex = 0;
                builder.UseTexture(resources.motionVectorColor, AccessFlags.Read);
                builder.UseTexture(published, AccessFlags.Read);
                // Alpha blending reads the existing camera target.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc((DrawData d, RasterGraphContext context) =>
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.passIndex, MeshTopology.Triangles, 3, 1, d.properties));
            }
        }

        [Obsolete("Compatibility Mode support")]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) => ResetTarget();

        [Obsolete("Compatibility Mode support")]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var plan = Prepare(renderingData.cameraData.camera, renderingData.cameraData.cameraTargetDescriptor);
            var cmd = CommandBufferPool.Get("Motion Vector Noise");
            try
            {
                if (plan.collect)
                {
                    CoreUtils.SetRenderTarget(cmd, plan.next);
                    cmd.DrawProcedural(Matrix4x4.identity, owner.passMaterial, 1, MeshTopology.Triangles, 3, 1, Properties(plan, false));
                }
                if (plan.publish)
                {
                    CoreUtils.SetRenderTarget(cmd, plan.history.published);
                    var source = plan.publishPrevious ? plan.previous : plan.history.read;
                    Blitter.BlitTexture(cmd, source, new Vector4(1, 1, 0, 0), 0, false);
                }
                CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.DrawProcedural(Matrix4x4.identity, owner.passMaterial, 0, MeshTopology.Triangles, 3, 1, Properties(plan, true));
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
    }
}
