using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpiderVerse.LineArt
{
    // CPU fallback shares snapshots, intersections, occlusion and connected chains too.
    // Only final ribbon meshes vary with a renderer selection or geometry appearance.
    internal sealed class LineArtCpuLayers : IDisposable
    {
        sealed class Batch : IDisposable
        {
            internal readonly HashSet<Renderer> targets;
            internal readonly HashSet<int> ids=new HashSet<int>();
            internal readonly LineArtAppearance appearance;
            internal Mesh mesh,staging;
            internal bool dirty=true,ready;
            internal Batch(LineArtLayers.Draw draw)
            {
                targets=new HashSet<Renderer>(draw.targets);foreach(var r in targets)ids.Add(r.GetInstanceID());appearance=draw.appearance.CopyAppearance();
                mesh=NewMesh();staging=NewMesh();
            }
            static Mesh NewMesh(){var mesh=new Mesh{name="Line Art shared layer ribbons",indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};mesh.MarkDynamic();return mesh;}
            internal bool Matches(LineArtLayers.Draw draw)=>appearance.SameGeometry(draw.appearance)&&targets.SetEquals(draw.targets);
            internal void Publish(LineArtGeometry.Result result)
            {
                staging.Clear();staging.SetVertices(result.positions);staging.SetUVs(0,result.previous);staging.SetUVs(1,result.next);staging.SetUVs(2,result.stroke);staging.SetIndices(result.indices,MeshTopology.Triangles,0,false);staging.RecalculateBounds();
                var old=mesh;mesh=staging;staging=old;ready=result.indices.Length>0;dirty=false;
            }
            public void Dispose(){CoreUtils.Destroy(mesh);CoreUtils.Destroy(staging);}
        }
        internal struct Drawing {internal Mesh mesh;internal Material material;internal MaterialPropertyBlock properties;}
        sealed class Request {internal Batch batch;internal HashSet<int> ids;internal LineArtSettings settings;internal LineArtGeometry.Result result;}
        sealed class Work {internal LineArtGeometry.Extraction extraction;internal LineArtGeometry.View view;internal List<Request> requests;}
        readonly Camera camera;
        readonly Material material;
        readonly Func<Camera,HashSet<Renderer>,LineArtSettings,LineArtGeometry.Snapshot[]> capture;
        readonly Func<LineArtGeometry.Snapshot[],LineArtGeometry.View,int> snapshotHash;
        readonly LineArtLayers layers=new LineArtLayers();
        readonly List<Batch> batches=new List<Batch>(),nextBatches=new List<Batch>();
        internal readonly List<Drawing> drawings=new List<Drawing>();
        readonly LineArtGeometry.IntersectionCache intersections=new LineArtGeometry.IntersectionCache();
        CancellationTokenSource cancel=new CancellationTokenSource();
        Task<Work> pending;
        LineArtGeometry.Extraction extraction;
        LineArtGeometry.View extractedView;
        int extractionHash,lastSnapshotHash,lastCameraGeneration=-1;bool hasSnapshot,repaintQueued,deferred;
        double nextUpdate;
        internal string Stats {get;private set;}="CPU layers initializing";
        internal int ExtractionCount {get;private set;}

        internal LineArtCpuLayers(Camera camera,Shader shader,Func<Camera,HashSet<Renderer>,LineArtSettings,LineArtGeometry.Snapshot[]> capture,Func<LineArtGeometry.Snapshot[],LineArtGeometry.View,int> hash)
        {this.camera=camera;material=CoreUtils.CreateEngineMaterial(shader);this.capture=capture;snapshotHash=hash;}
        internal bool RequestRepaint(double now)
        {
            if(!camera||repaintQueued)return false;
            if(pending!=null&&pending.IsCompleted||pending==null&&deferred&&now>=nextUpdate){repaintQueued=true;return true;}return false;
        }
        internal void Suspend()
        {
            deferred=false;repaintQueued=false;
            if(pending==null)return;
            cancel.Cancel();cancel.Dispose();cancel=new CancellationTokenSource();pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);pending=null;hasSnapshot=false;
        }
        void ConfigureBatches()
        {
            nextBatches.Clear();
            foreach(var draw in layers.draws)
            {
                Batch found=null;
                foreach(var b in nextBatches)if(b.Matches(draw)){found=b;break;}
                if(found==null)foreach(var b in batches)if(b.Matches(draw)){found=b;break;}
                if(found==null)found=new Batch(draw);
                if(!nextBatches.Contains(found))nextBatches.Add(found);draw.batch=nextBatches.IndexOf(found);
            }
            foreach(var old in batches)if(!nextBatches.Contains(old))old.Dispose();batches.Clear();batches.AddRange(nextBatches);
        }
        internal void Update(LineArtSettings settings,Shader shader,int width,int height,int cameraGeneration=-1,double nextCameraUpdate=-1)
        {
            repaintQueued=false;drawings.Clear();layers.Refresh(settings,camera);ConfigureBatches();
            if(material.shader!=shader)material.shader=shader;
            int hash=settings.ExtractionHash();
            if(layers.UnionChanged||extractionHash!=hash){Suspend();extraction=null;hasSnapshot=false;nextUpdate=0;lastCameraGeneration=-1;extractionHash=hash;}
            if(layers.draws.Count==0){Suspend();return;}
            if(pending!=null&&pending.IsCompleted)
            {
                if(pending.Status==TaskStatus.RanToCompletion)
                {
                    var work=pending.Result;extraction=work.extraction;extractedView=work.view;
                    foreach(var request in work.requests)if(batches.Contains(request.batch))request.batch.Publish(request.result);
                    Stats=$"CPU: {layers.draws.Count} layers / {batches.Count} stroke sets / {extraction.chains.Count} shared chains / {extraction.spans} visible spans / {extraction.candidates} candidates";
                }else if(pending.IsFaulted)Debug.LogException(pending.Exception);
                pending=null;
            }
            double now=LineArtTime.Now;
            bool external=cameraGeneration>=0;
            bool due=external?cameraGeneration!=lastCameraGeneration||!hasSnapshot:now>=nextUpdate||!hasSnapshot;
            if(external)nextUpdate=nextCameraUpdate;
            deferred=pending==null&&!due;
            if(pending==null)
            {
                bool rebuild=false;LineArtGeometry.Snapshot[] snapshots=null;
                var view=extractedView;
                if(due)
                {
                    snapshots=capture(camera,layers.targets,settings);
                    view=new LineArtGeometry.View{matrix=camera.projectionMatrix*camera.worldToCameraMatrix,position=camera.transform.position,toCamera=-camera.transform.forward,perspective=!camera.orthographic,width=width,height=height};
                    int current=snapshotHash(snapshots,view);rebuild=!hasSnapshot||current!=lastSnapshotHash||extraction==null;lastSnapshotHash=current;hasSnapshot=true;
                    if(external)lastCameraGeneration=cameraGeneration;else nextUpdate=now+1.0/Math.Max(1,settings.updateRate);
                }
                var requests=new List<Request>();
                foreach(var b in batches)if(rebuild||b.dirty)requests.Add(new Request{batch=b,ids=b.ids,settings=settings.WithAppearance(b.appearance)});
                if(requests.Count>0&&(rebuild||extraction!=null))
                {
                    var options=settings.Copy();var token=cancel.Token;var cached=extraction;var capturedView=view;int modelHash=rebuild?snapshotHash(snapshots,default):0;
                    if(rebuild)ExtractionCount++;
                    pending=Task.Run(()=>
                    {
                        var shared=rebuild?LineArtGeometry.Extract(snapshots,capturedView,options,token,intersections,modelHash):cached;
                        foreach(var request in requests)
                        {
                            var selected=new List<LineArtGeometry.Chain>();foreach(var chain in shared.chains)if(request.ids.Contains(chain.ownerA)||request.ids.Contains(chain.ownerB))selected.Add(chain);
                            request.result=LineArtGeometry.MakeGeometry(selected,capturedView,request.settings,token);
                        }
                        return new Work{extraction=shared,view=capturedView,requests=requests};
                    },token);
                }
            }
            foreach(var draw in layers.draws)
            {
                var batch=batches[draw.batch];if(!batch.ready)continue;draw.UpdateProperties(width,height);drawings.Add(new Drawing{mesh=batch.mesh,material=material,properties=draw.properties});
            }
        }
        public void Dispose(){Suspend();cancel.Cancel();cancel.Dispose();foreach(var batch in batches)batch.Dispose();batches.Clear();CoreUtils.Destroy(material);}
    }
}
