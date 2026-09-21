using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpiderVerse.LineArt
{
    // All per-frame geometry stays on the GPU. Only small diagnostic counters are read back.
    // A separate instance per camera prevents Scene/Game camera state from aliasing.
    public sealed class LineArtGpu : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Int4 { public int x,y,z,w; public Int4(int a,int b,int c,int d){x=a;y=b;z=c;w=d;} }
        [StructLayout(LayoutKind.Sequential)] struct Face { public Int4 vertices,info; }
        [StructLayout(LayoutKind.Sequential)] struct Edge { public Int4 vertices,faces; }
        sealed class Source
        {
            public Renderer renderer; public Mesh mesh; public int offset,count,materialOffset;
            public GraphicsBuffer staticBuffer; public int stride,positionOffset;
            public Matrix4x4 lastWorld,currentWorld;
            public Transform[] bones=Array.Empty<Transform>();
            public bool cloth,draw;
            public readonly List<Material> materials=new List<Material>();
        }
        sealed class TreeNode { public int a,b,face=-1,depth,index; public Bounds bounds; }
        readonly List<Source> sources=new List<Source>();
        readonly List<Renderer> selected=new List<Renderer>();
        readonly HashSet<Renderer> targets=new HashSet<Renderer>();
        readonly List<GraphicsBuffer> buffers=new List<GraphicsBuffer>();
        readonly List<GraphicsBuffer> frameWrappers=new List<GraphicsBuffer>();
        readonly List<(int start,int count)> levels=new List<(int,int)>();
        readonly Dictionary<Material,Int4> frameMaterials=new Dictionary<Material,Int4>();
        readonly Dictionary<string,int> kernels=new Dictionary<string,int>();
        Renderer[] scene=Array.Empty<Renderer>(); double nextScan;
        readonly ComputeShader compute; readonly Material material;
        GraphicsBuffer vertices,faces,edges,edgeFaces,objects,materials,nodes,nodeTopology,representatives;
        GraphicsBuffer candidates,intersectionCache,spans,heads,endNext,links,segments,counters,dispatchArgs,drawArgs;
        Vector4[] objectData; Int4[] materialData;
        int faceCount,edgeCount,nodeCount,root,capacity,segmentCapacity,hashSize;
        bool disposed,readbackPending; double nextReadback;
        double nextUpdate;int lastGeometryHash;bool deferredUpdate,repaintQueued;
        internal bool RequestDeferredRepaint(double now){if(!deferredUpdate||repaintQueued||now<nextUpdate||Failed)return false;repaintQueued=true;return true;}
        int lastInputHash,lastViewHash;bool hasInput,hasIntersections;
        public string Stats {get;private set;}="GPU initializing";
        public bool Failed {get;private set;}
        public uint[] LastCounters {get;private set;}
        public LineArtGpu(ComputeShader shader,Shader stroke)
        {
            compute=UnityEngine.Object.Instantiate(shader);compute.hideFlags=HideFlags.HideAndDontSave;
            material=CoreUtils.CreateEngineMaterial(stroke);
        }
        static readonly UnityEngine.Profiling.CustomSampler refitSample=UnityEngine.Profiling.CustomSampler.Create("Line Art Refit",true), edgeSample=UnityEngine.Profiling.CustomSampler.Create("Line Art Edges",true), intersectionSample=UnityEngine.Profiling.CustomSampler.Create("Line Art Intersections",true), visibilitySample=UnityEngine.Profiling.CustomSampler.Create("Line Art Visibility",true), strokeSample=UnityEngine.Profiling.CustomSampler.Create("Line Art Strokes",true);
        int K(string name){if(!kernels.TryGetValue(name,out int k)){k=compute.FindKernel(name);kernels.Add(name,k);}return k;}
        GraphicsBuffer Buffer(int count,int stride,GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {var b=new GraphicsBuffer(target,Math.Max(1,count),stride);buffers.Add(b);return b;}
        GraphicsBuffer Data<T>(List<T> data,int stride) where T:struct {var b=Buffer(data.Count,stride);if(data.Count>0)b.SetData(data);return b;}
        static Mesh MeshOf(Renderer r)=>r is SkinnedMeshRenderer s?s.sharedMesh:r.TryGetComponent<MeshFilter>(out var f)?f.sharedMesh:null;
        public bool Prepare(Camera camera,LineArtSettings settings)
        {
            // Selection is consumed only on a geometry update; avoid scanning/sorting the scene on draw-only frames.
            if(faceCount>0&&!Failed&&Time.realtimeSinceStartupAsDouble<nextUpdate&&settings.GeometryHash()==lastGeometryHash)return true;
            targets.Clear();
            foreach(var s in ObjectLineArtSource.Active)if(s&&s.isActiveAndEnabled)foreach(var r in s.GetRenderers())if(r)targets.Add(r);
            if(Time.realtimeSinceStartupAsDouble>=nextScan){scene=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID);nextScan=Time.realtimeSinceStartupAsDouble+1;}
            selected.Clear();
            foreach(var r in scene)if(r&&r.enabled&&!r.forceRenderingOff&&r.gameObject.activeInHierarchy&&
                ((1<<r.gameObject.layer)&camera.cullingMask)!=0&&(r is MeshRenderer||r is SkinnedMeshRenderer)&&
                (targets.Contains(r)||((settings.occlusion||settings.intersections)&&((1<<r.gameObject.layer)&settings.occluderLayers.value)!=0)))selected.Add(r);
            bool addedTarget=false;foreach(var r in targets)if(r&&r.enabled&&!r.forceRenderingOff&&r.gameObject.activeInHierarchy&&((1<<r.gameObject.layer)&camera.cullingMask)!=0&&!selected.Contains(r)){selected.Add(r);addedTarget=true;}
            if(addedTarget)selected.Sort((a,b)=>a.GetInstanceID().CompareTo(b.GetInstanceID()));
            bool changed=selected.Count!=sources.Count;
            if(!changed)for(int i=0;i<selected.Count;i++)if(selected[i]!=sources[i].renderer||MeshOf(selected[i])!=sources[i].mesh||targets.Contains(selected[i])!=sources[i].draw){changed=true;break;}
            if(changed)Rebuild();
            return faceCount>0&&!Failed;
        }
        // Explicit invalidation is required after editing a Mesh's topology in place.
        public void Invalidate(){nextScan=0;Failed=false;ReleaseGeometry();}
        internal void MarkFailed(){Failed=true;}
        void Rebuild()
        {
            ReleaseGeometry();Failed=false;
            var vs=new List<Vector3>();var reps=new List<int>();var fs=new List<Face>();var es=new List<Edge>();var ef=new List<int>();var ms=new List<Int4>();
            foreach(var r in selected)
            {
                var mesh=MeshOf(r);
                if(!mesh||!mesh.isReadable)throw new InvalidOperationException("GPU Line Art requires readable topology: "+r.name);
                if(r.isPartOfStaticBatch)throw new InvalidOperationException("GPU Line Art source must not use static batching: "+r.name);
                var src=new Source{renderer=r,mesh=mesh,draw=targets.Contains(r),offset=vs.Count,count=mesh.vertexCount,materialOffset=ms.Count,lastWorld=r.localToWorldMatrix};
                r.GetSharedMaterials(src.materials);
                for(int i=0;i<mesh.subMeshCount;i++)ms.Add(default);
                int obj=sources.Count,first=fs.Count;
                var positions=mesh.vertices;var sub=new int[mesh.subMeshCount][];
                for(int i=0;i<sub.Length;i++)sub[i]=mesh.GetTopology(i)==MeshTopology.Triangles?mesh.GetIndices(i):Array.Empty<int>();
                var top=LineArtGeometry.Prepare(positions,sub);
                // Use original GPU vertex indices, not a second, independently skinned vertex stream.
                // Split welds with different deformation weights to avoid joining animated seams.
                if(r is SkinnedMeshRenderer && mesh.bindposeCount>0)top=PrepareSkinned(mesh,positions,sub,top);
                // Seed the spatial hierarchy from the actual pose, once. Imported bind-space bounds can be thousands of times larger.
                var initial=positions;Mesh initialSkin=null;
                try {if(r is SkinnedMeshRenderer initialRenderer){initialSkin=new Mesh();initialRenderer.BakeMesh(initialSkin,true);initial=initialSkin.vertices;}foreach(var p in initial)vs.Add(r.localToWorldMatrix.MultiplyPoint3x4(p));}
                finally {if(initialSkin)CoreUtils.Destroy(initialSkin);}
                for(int i=0;i<positions.Length;i++)reps.Add(i);
                foreach(var f in top.faces)fs.Add(new Face{vertices=new Int4(src.offset+top.representatives[f.a],src.offset+top.representatives[f.b],src.offset+top.representatives[f.c],obj),info=new Int4(src.materialOffset+f.material,0,0,0)});
                if(src.draw)foreach(var e in top.edges){int start=ef.Count;foreach(int f in e.faces)ef.Add(first+f);es.Add(new Edge{vertices=new Int4(src.offset+top.representatives[e.a],src.offset+top.representatives[e.b],obj,es.Count),faces=new Int4(start,e.faces.Length,0,0)});}
                if(r is SkinnedMeshRenderer sk){sk.vertexBufferTarget|=GraphicsBuffer.Target.Raw;src.bones=sk.bones;src.cloth=sk.GetComponent<Cloth>()!=null;}
                else {mesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;int stream=mesh.GetVertexAttributeStream(VertexAttribute.Position);src.staticBuffer=mesh.GetVertexBuffer(stream);src.stride=mesh.GetVertexBufferStride(stream);src.positionOffset=mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                    if(mesh.GetVertexAttributeFormat(VertexAttribute.Position)!=VertexAttributeFormat.Float32)throw new InvalidOperationException("GPU Line Art needs Float32 positions: "+mesh.name);}
                sources.Add(src);
            }
            faceCount=fs.Count;edgeCount=es.Count;if(faceCount==0)return;
            vertices=Data(vs,12);faces=Data(fs,32);edges=Data(es,32);edgeFaces=Data(ef,4);representatives=Data(reps,4);
            objectData=new Vector4[sources.Count];objects=Buffer(sources.Count,16);materialData=ms.ToArray();materials=Buffer(ms.Count,16);
            // Build once, refit all world/projected bounds on GPU every update. Leaves use a spatial ordering.
            var tree=new List<TreeNode>();var ids=new int[faceCount];var bounds=new Bounds[faceCount];
            for(int i=0;i<faceCount;i++){ids[i]=i;var f=fs[i].vertices;var b=new Bounds(vs[f.x],Vector3.zero);b.Encapsulate(vs[f.y]);b.Encapsulate(vs[f.z]);bounds[i]=b;}
            int Build(int start,int count,int depth)
            {
                var b=bounds[ids[start]];for(int i=start+1;i<start+count;i++)b.Encapsulate(bounds[ids[i]]);
                int index=tree.Count;var n=new TreeNode{bounds=b,depth=depth,index=index};tree.Add(n);
                if(count==1)n.face=ids[start];
                else {var sz=b.size;int axis=sz.x>sz.y?(sz.x>sz.z?0:2):(sz.y>sz.z?1:2);Array.Sort(ids,start,count,Comparer<int>.Create((x,y)=>bounds[x].center[axis].CompareTo(bounds[y].center[axis])));int half=count/2;n.a=Build(start,half,depth+1);n.b=Build(start+half,count-half,depth+1);}
                return index;
            }
            int oldRoot=Build(0,faceCount,0);tree.Sort((a,b)=>b.depth.CompareTo(a.depth));var remap=new int[tree.Count];for(int i=0;i<tree.Count;i++)remap[tree[i].index]=i;
            var topology=new List<Int4>();int begin=0;
            for(int i=0;i<tree.Count;i++){var n=tree[i];topology.Add(new Int4(n.face<0?remap[n.a]:-1,n.face<0?remap[n.b]:-1,n.face,0));if(i==tree.Count-1||tree[i+1].depth!=n.depth){levels.Add((begin,i+1-begin));begin=i+1;}}
            root=remap[oldRoot];nodeCount=tree.Count;nodeTopology=Data(topology,16);nodes=Buffer(nodeCount,64);
            capacity=Mathf.NextPowerOfTwo(Math.Max(16384,Math.Min(262144,edgeCount*2)));segmentCapacity=capacity*8;hashSize=capacity;
            candidates=Buffer(capacity,80);intersectionCache=Buffer(capacity,80);spans=Buffer(capacity,80);heads=Buffer(hashSize,4);endNext=Buffer(capacity*2,4);links=Buffer(capacity*2,4);
            segments=Buffer(segmentCapacity,80);counters=Buffer(8,4);dispatchArgs=Buffer(12,4,GraphicsBuffer.Target.IndirectArguments);drawArgs=Buffer(4,4,GraphicsBuffer.Target.IndirectArguments);
            counters.SetData(new uint[8]);
            material.SetBuffer("_GpuSegments",segments);
            Stats=$"GPU: {sources.Count} meshes / {faceCount} triangles / {edgeCount} topology edges";
        }
        static LineArtGeometry.Topology PrepareSkinned(Mesh mesh,Vector3[] p,int[][] sub,LineArtGeometry.Topology original)
        {
            // Existing weld identity is retained only when full skin weights and blend-shape deltas agree.
            var weights=mesh.GetAllBoneWeights();var counts=mesh.GetBonesPerVertex();
            var signatures=new string[p.Length];int offset=0;
            for(int i=0;i<p.Length;i++){var b=new System.Text.StringBuilder();int count=counts.Length>i?counts[i]:0;for(int j=0;j<count;j++){var w=weights[offset++];b.Append(w.boneIndex).Append(':').Append(w.weight.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(';');}signatures[i]=b.ToString();}
            weights.Dispose();counts.Dispose();
            var delta=new Vector3[p.Length];
            for(int s=0;s<mesh.blendShapeCount;s++)for(int f=0;f<mesh.GetBlendShapeFrameCount(s);f++){mesh.GetBlendShapeFrameVertices(s,f,delta,null,null);for(int i=0;i<p.Length;i++)if(delta[i]!=Vector3.zero)signatures[i]+=$"/{s}/{f}/{delta[i].x:R}/{delta[i].y:R}/{delta[i].z:R}";}
            var bnd=mesh.bounds;float epsilon=Mathf.Max(bnd.size.magnitude*1e-6f,1e-7f);
            var weld=new Dictionary<(long,long,long,string),int>();var representatives=new List<int>();var map=new int[p.Length];
            for(int i=0;i<p.Length;i++){var v=p[i];var key=((long)Math.Round(v.x/epsilon),(long)Math.Round(v.y/epsilon),(long)Math.Round(v.z/epsilon),signatures[i]);if(!weld.TryGetValue(key,out int id)){id=representatives.Count;representatives.Add(i);weld.Add(key,id);}map[i]=id;}
            var faces=new List<LineArtGeometry.Face>();var adjacency=new Dictionary<(int,int),List<int>>();
            void Add(int a,int b,int f){var key=(Math.Min(a,b),Math.Max(a,b));if(!adjacency.TryGetValue(key,out var l))adjacency.Add(key,l=new List<int>());l.Add(f);}
            for(int m=0;m<sub.Length;m++)for(int i=0;i+2<sub[m].Length;i+=3){int a=map[sub[m][i]],b=map[sub[m][i+1]],c=map[sub[m][i+2]];if(a==b||b==c||a==c)continue;int f=faces.Count;faces.Add(new LineArtGeometry.Face{a=a,b=b,c=c,material=m});Add(a,b,f);Add(b,c,f);Add(c,a,f);}
            var edges=new List<LineArtGeometry.Edge>();foreach(var e in adjacency)edges.Add(new LineArtGeometry.Edge{a=e.Key.Item1,b=e.Key.Item2,faces=e.Value.ToArray()});
            return new LineArtGeometry.Topology{representatives=representatives.ToArray(),faces=faces.ToArray(),edges=edges.ToArray()};
        }
        void Bind(CommandBuffer cmd,int k,string name,GraphicsBuffer buffer)=>cmd.SetComputeBufferParam(compute,k,name,buffer);
        void Dispatch(CommandBuffer cmd,string name,int count)
        {
            int k=K(name);
            // Unity only binds resources actually used by a kernel; each kernel stays within D3D11's 8 UAV limit.
            Bind(cmd,k,"_Vertices",vertices);Bind(cmd,k,"_Faces",faces);Bind(cmd,k,"_Edges",edges);Bind(cmd,k,"_EdgeFaces",edgeFaces);Bind(cmd,k,"_Objects",objects);Bind(cmd,k,"_Materials",materials);
            Bind(cmd,k,"_Nodes",nodes);Bind(cmd,k,"_NodeTopology",nodeTopology);Bind(cmd,k,"_Candidates",candidates);Bind(cmd,k,"_Spans",spans);Bind(cmd,k,"_Heads",heads);Bind(cmd,k,"_EndNext",endNext);Bind(cmd,k,"_Links",links);Bind(cmd,k,"_Segments",segments);Bind(cmd,k,"_Counters",counters);Bind(cmd,k,"_DispatchArgs",dispatchArgs);Bind(cmd,k,"_DrawArgs",drawArgs);
            Bind(cmd,k,"_IntersectionCache",intersectionCache);
            if(count>=0)cmd.DispatchCompute(compute,k,Math.Max(1,(count+63)/64),1,1);else cmd.DispatchCompute(compute,k,dispatchArgs,(uint)(-count-1)*12);
        }
        public void Execute(CommandBuffer cmd,Camera camera,int width,int height,LineArtSettings s)
        {
            if(disposed||faceCount==0||Failed)return;
            repaintQueued=false;deferredUpdate=false;int geometryHash=s.GeometryHash();
            if(Time.realtimeSinceStartupAsDouble<nextUpdate&&geometryHash==lastGeometryHash){deferredUpdate=true;SetMaterial(material,s,width,height);cmd.DrawProceduralIndirect(Matrix4x4.identity,material,0,MeshTopology.Triangles,drawArgs);return;}
            nextUpdate=Time.realtimeSinceStartupAsDouble+1.0/Math.Max(1,s.updateRate);
            // A cheap pose/material fingerprint avoids both vertex readback and idle compute work.
            int inputHash=17;frameMaterials.Clear();
            unchecked {for(int i=0;i<sources.Count;i++){
                var src=sources[i];var r=src.renderer;var world=r.localToWorldMatrix;
                src.currentWorld=world;objectData[i]=new Vector4(targets.Contains(r)?1:0,world.determinant<0?-1:1,0,0);inputHash=inputHash*31+world.GetHashCode();inputHash=inputHash*31+objectData[i].GetHashCode();
                if(r is SkinnedMeshRenderer sk){foreach(var bone in src.bones)if(bone)inputHash=inputHash*31+bone.localToWorldMatrix.GetHashCode();for(int j=0;j<src.mesh.blendShapeCount;j++)inputHash=inputHash*31+sk.GetBlendShapeWeight(j).GetHashCode();if(src.cloth)inputHash=inputHash*31+Time.frameCount;}
                r.GetSharedMaterials(src.materials);for(int j=0;j<src.mesh.subMeshCount;j++){var m=src.materials.Count>0?src.materials[Math.Min(j,src.materials.Count-1)]:null;Int4 data;if(!m)data=new Int4(2,0,0,0);else if(!frameMaterials.TryGetValue(m,out data)){data=new Int4(m.HasProperty("_Cull")?Mathf.RoundToInt(m.GetFloat("_Cull")):2,m.renderQueue<=2500?1:0,0,0);frameMaterials.Add(m,data);}materialData[src.materialOffset+j]=data;inputHash=(inputHash*31+data.x)*31+data.y;}
            }}
            int viewHash=unchecked(((camera.projectionMatrix*camera.worldToCameraMatrix).GetHashCode()*31+width)*31+height);
            if(hasInput&&inputHash==lastInputHash&&viewHash==lastViewHash&&geometryHash==lastGeometryHash){SetMaterial(material,s,width,height);cmd.DrawProceduralIndirect(Matrix4x4.identity,material,0,MeshTopology.Triangles,drawArgs);return;}
            bool rebuildIntersections=!hasIntersections||inputHash!=lastInputHash;
            lastInputHash=inputHash;lastViewHash=viewHash;lastGeometryHash=geometryHash;hasInput=true;
            if(!s.intersections)hasIntersections=false;
            foreach(var wrapper in frameWrappers)wrapper.Dispose();frameWrappers.Clear();
            cmd.BeginSample("Line Art GPU complete pipeline");
            cmd.SetComputeIntParam(compute,"_FaceCount",faceCount);cmd.SetComputeIntParam(compute,"_EdgeCount",edgeCount);cmd.SetComputeIntParam(compute,"_Capacity",capacity);cmd.SetComputeIntParam(compute,"_SegmentCapacity",segmentCapacity);cmd.SetComputeIntParam(compute,"_HashSize",hashSize);cmd.SetComputeIntParam(compute,"_Root",root);
            cmd.SetComputeIntParam(compute,"_RebuildIntersections",rebuildIntersections?1:0);
            cmd.SetComputeMatrixParam(compute,"_ViewProjection",camera.projectionMatrix*camera.worldToCameraMatrix);
            cmd.SetComputeVectorParam(compute,"_Camera",new Vector4(camera.transform.position.x,camera.transform.position.y,camera.transform.position.z,camera.orthographic?0:1));
            cmd.SetComputeVectorParam(compute,"_ToCamera",-camera.transform.forward);
            cmd.SetComputeVectorParam(compute,"_Screen",new Vector4(width,height,0,0));
            cmd.SetComputeVectorParam(compute,"_Options",new Vector4(s.contour?1:0,s.crease?1:0,s.materialBorders?1:0,s.boundaries?1:0));
            cmd.SetComputeVectorParam(compute,"_Connections",new Vector4(s.connectDifferentEdgeTypes?1:0,s.connectJunctions?1:0,Mathf.Cos(s.junctionMaxAngle*Mathf.Deg2Rad),0));
            cmd.SetComputeVectorParam(compute,"_Style",new Vector4(s.lengthTrim,s.lengthRandomness,s.thicknessCurve||s.noise>0?1:0,s.depthEpsilon));
            cmd.SetComputeFloatParam(compute,"_Crease",Mathf.Cos(Mathf.PI-s.creaseAngle*Mathf.Deg2Rad));cmd.SetComputeIntParam(compute,"_Occlusion",s.occlusion?1:0);
            for(int i=0;i<sources.Count;i++)
            {
                var src=sources[i];var r=src.renderer;var world=src.currentWorld;
                if(!(r is SkinnedMeshRenderer)&&world==src.lastWorld)continue;
                src.lastWorld=world;
                GraphicsBuffer input=src.staticBuffer;int stride=src.stride,positionOffset=src.positionOffset;
                if(r is SkinnedMeshRenderer sk){input=sk.GetVertexBuffer();if(input==null){Failed=true;Stats="GPU skin buffer unavailable for "+r.name+"; CPU fallback";Debug.LogWarning(Stats);cmd.EndSample("Line Art GPU complete pipeline");return;}frameWrappers.Add(input);stride=input.stride;positionOffset=0;if(stride<12)stride=12+(src.mesh.HasVertexAttribute(VertexAttribute.Normal)?12:0)+(src.mesh.HasVertexAttribute(VertexAttribute.Tangent)?16:0);
                    // Unity 6's deformed stream is root-bone relative, with scale already applied.
                    // Applying Renderer.localToWorld a second time breaks imported rigs (including 100x FBX transforms).
                    var rootBone=sk.rootBone?sk.rootBone:sk.transform;world=Matrix4x4.TRS(rootBone.position,rootBone.rotation,Vector3.one);
                }
                int k=K("TransformVertices");Bind(cmd,k,"_InputVertices",input);Bind(cmd,k,"_Vertices",vertices);
                cmd.SetComputeIntParam(compute,"_VertexOffset",src.offset);cmd.SetComputeIntParam(compute,"_VertexCount",src.count);cmd.SetComputeIntParam(compute,"_Stride",stride);cmd.SetComputeIntParam(compute,"_PositionOffset",positionOffset);cmd.SetComputeMatrixParam(compute,"_LocalToWorld",world);cmd.DispatchCompute(compute,k,(src.count+63)/64,1,1);
            }
            cmd.SetBufferData(objects,objectData);cmd.SetBufferData(materials,materialData);
            Dispatch(cmd,"Reset",hashSize);
            cmd.BeginSample(refitSample);foreach(var level in levels){cmd.SetComputeIntParam(compute,"_LevelStart",level.start);cmd.SetComputeIntParam(compute,"_LevelCount",level.count);Dispatch(cmd,"Refit",level.count);}
            cmd.EndSample(refitSample);cmd.BeginSample(edgeSample);Dispatch(cmd,"FeatureEdges",edgeCount);cmd.EndSample(edgeSample);cmd.BeginSample(intersectionSample);
            if(s.intersections){if(rebuildIntersections)Dispatch(cmd,"Intersections",faceCount);else Dispatch(cmd,"ReuseIntersections",capacity);hasIntersections=true;}
            cmd.EndSample(intersectionSample);cmd.BeginSample(visibilitySample);Dispatch(cmd,"Arguments",1);Dispatch(cmd,"Visibility",-1);cmd.EndSample(visibilitySample);cmd.BeginSample(strokeSample);
            Dispatch(cmd,"Arguments",1);Dispatch(cmd,"Endpoints",-2);Dispatch(cmd,"Connect",-3);Dispatch(cmd,"Strokes",-2);Dispatch(cmd,"Arguments",1);cmd.EndSample(strokeSample);
            SetMaterial(material,s,width,height);
            cmd.DrawProceduralIndirect(Matrix4x4.identity,material,0,MeshTopology.Triangles,drawArgs);
            cmd.EndSample("Line Art GPU complete pipeline");
            if(!readbackPending&&Time.realtimeSinceStartupAsDouble>=nextReadback&&SystemInfo.supportsAsyncGPUReadback)
            {
                readbackPending=true;nextReadback=Time.realtimeSinceStartupAsDouble+1;
                cmd.RequestAsyncReadback(counters,request=>{readbackPending=false;if(disposed||request.hasError)return;var d=request.GetData<uint>();LastCounters=d.ToArray();Stats=$"GPU: {sources.Count} meshes / {faceCount} triangles / {edgeCount} edges / {d[0]} candidates / {d[1]} visible spans / {d[2]} ribbon segments / {d[4]} intersections";if(d[3]!=0){Failed=true;Stats+=$" — capacity/iteration overflow ({d[3]}), CPU fallback required";Debug.LogWarning(Stats);}});
            }
        }
        internal static void SetMaterial(Material m,LineArtSettings s,int width,int height)
        {
            m.SetColor("_Color",s.color);m.SetVector("_Resolution",new Vector4(width,height,0,0));m.SetFloat("_Width",s.thickness);m.SetFloat("_Taper",s.thicknessCurve?s.endTaper:0);m.SetFloat("_Transition",s.thicknessTransition);m.SetFloat("_Noise",s.noise);m.SetVector("_Offset",s.offset);m.SetVector("_RandomOffset",s.randomOffset);m.SetTexture("_StrokeTex",s.texture?s.texture:Texture2D.whiteTexture);m.SetVector("_TextureST",new Vector4(s.textureTiling.x,s.textureTiling.y,s.textureOffset.x,s.textureOffset.y));float angle=s.textureRotation*Mathf.Deg2Rad;m.SetVector("_TextureRotation",new Vector4(Mathf.Cos(angle),Mathf.Sin(angle),0,0));m.SetFloat("_HasTexture",s.texture?1:0);m.SetFloat("_TextureStrength",s.textureStrength);m.SetFloat("_TextureRepeat",s.textureRepeats);m.SetFloat("_TextureMask",s.darkOnWhiteMask?1:0);
        }
        void ReleaseGeometry(){foreach(var b in buffers)b.Dispose();buffers.Clear();foreach(var s in sources)s.staticBuffer?.Dispose();foreach(var b in frameWrappers)b.Dispose();frameWrappers.Clear();sources.Clear();levels.Clear();faceCount=0;nextUpdate=0;hasInput=false;hasIntersections=false;}
        public void Dispose(){if(disposed)return;disposed=true;ReleaseGeometry();CoreUtils.Destroy(material);CoreUtils.Destroy(compute);}
    }
}
