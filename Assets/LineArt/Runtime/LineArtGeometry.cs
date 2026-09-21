// Pure snapshot math. No scene objects, Mesh access, or graphics calls on the worker.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace SpiderVerse.LineArt
{
    public static class LineArtGeometry
    {
        const float Eps = 1e-7f;
        public sealed class Topology
        {
            public int[] representatives;
            public Face[] faces;
            public Edge[] edges;
        }
        public struct Face { public int a,b,c,material; }
        public sealed class Edge { public int a,b; public int[] faces; }
        public sealed class Snapshot
        {
            public int id,rendererId; public bool draw; public float orientation = 1;
            public Topology topology; public Vector3[] vertices;
            public int[] cull; public bool[] opaque;
        }
        public struct View
        {
            public Matrix4x4 matrix; public Vector3 position, toCamera;
            public bool perspective; public int width,height;
        }
        public sealed class IntersectionCache
        {
            internal int key;
            internal List<Candidate> segments;
        }
        public sealed class Result
        {
            public Vector3[] positions,previous,next;
            public Vector4[] stroke;
            public int[] indices;
            public int candidates,spans,chains,intersections;
            public double milliseconds;
        }
        public sealed class Chain
        {
            public readonly List<Vector3> points = new List<Vector3>();
            public bool closed; public uint seed;
            public int ownerA,ownerB;
        }
        struct Triangle
        {
            public Vector3 a,b,c,n; public Bounds bounds;
            public int id,obj,selectionId,ia,ib,ic,cull; public bool opaque,draw;
            public float facing;
        }
        internal sealed class Candidate
        {
            public Vector3 a,b; public string ka,kb,key; public int obj,type,ownerA,ownerB;
            public int[] faces;
        }
        sealed class Span
        {
            public Vector3 a,b; public string ka,kb,key; public int obj,type,ownerA,ownerB;
        }
        struct ProjectedTriangle
        {
            public Vector3 a,b,c; public int source;
            public float area,minX,minY,maxX,maxY,minZ;
        }
        public static uint Hash(string text)
        {
            unchecked { uint h=2166136261; foreach(char c in text) h=(h^c)*16777619;
                h=(h^(h>>16))*0x7feb352d; h=(h^(h>>15))*0x846ca68b; return h^(h>>16); }
        }
        public static float LengthScale(uint seed, bool closed, float trim, float randomness)
        {
            return (closed ? 1 : 1-Mathf.Clamp(trim,0,.95f)) *
                (1 + Mathf.Clamp(randomness,0,.95f)*(float)(2*((seed+.5)/4294967296.0)-1));
        }
        public static Topology Prepare(Vector3[] positions, int[][] submeshes)
        {
            var bounds = new Bounds(positions.Length>0?positions[0]:Vector3.zero,Vector3.zero);
            foreach(var p in positions) bounds.Encapsulate(p);
            float epsilon=Mathf.Max(bounds.size.magnitude*1e-6f,1e-7f);
            var welded=new Dictionary<(long,long,long),int>(); var reps=new List<int>();
            var map=new int[positions.Length];
            for(int i=0;i<positions.Length;i++) {
                var p=positions[i]; var key=((long)Math.Round(p.x/epsilon),(long)Math.Round(p.y/epsilon),(long)Math.Round(p.z/epsilon));
                if(!welded.TryGetValue(key,out int v)) { v=reps.Count; welded.Add(key,v); reps.Add(i); } map[i]=v;
            }
            var faces=new List<Face>(); var adjacency=new Dictionary<(int,int),List<int>>();
            for(int m=0;m<submeshes.Length;m++) {
                var ids=submeshes[m];
                for(int j=0;j+2<ids.Length;j+=3) {
                    int a=map[ids[j]],b=map[ids[j+1]],c=map[ids[j+2]];
                    if(a==b||b==c||c==a) continue;
                    int f=faces.Count; faces.Add(new Face{a=a,b=b,c=c,material=m});
                    Add(a,b,f); Add(b,c,f); Add(c,a,f);
                }
            }
            void Add(int a,int b,int f) { var k=(Math.Min(a,b),Math.Max(a,b));
                if(!adjacency.TryGetValue(k,out var list)) adjacency.Add(k,list=new List<int>()); list.Add(f); }
            var edges=new List<Edge>();
            foreach(var e in adjacency) edges.Add(new Edge{a=e.Key.Item1,b=e.Key.Item2,faces=e.Value.ToArray()});
            return new Topology{representatives=reps.ToArray(),faces=faces.ToArray(),edges=edges.ToArray()};
        }
        static Vector4 Project(Vector3 v, Matrix4x4 m) => m*new Vector4(v.x,v.y,v.z,1);
        static Vector3 Ndc(Vector4 v) => new Vector3(v.x,v.y,v.z)/v.w;
        static float Plane(Vector4 p,int k) { switch(k) {case 0:return p.w+p.x;case 1:return p.w-p.x;case 2:return p.w+p.y;case 3:return p.w-p.y;case 4:return p.w+p.z;default:return p.w-p.z;} }
        static bool Positive(float a,float b,ref float lo,ref float hi)
        {
            if(a<0 && b<0) return false;
            if((a<0)!=(b<0)) { float t=a/(a-b); if(a<0) lo=Mathf.Max(lo,t); else hi=Mathf.Min(hi,t); }
            return hi-lo>Eps;
        }
        public static bool Clip(Vector4 a,Vector4 b,out float lo,out float hi)
        {
            lo=0;hi=1;
            for(int k=0;k<6;k++) if(!Positive(Plane(a,k),Plane(b,k),ref lo,ref hi)) return false;
            return true;
        }
        static List<Vector4> ClipTriangle(Vector4 a,Vector4 b,Vector4 c)
        {
            // Trivial accept/reject avoids six temporary clipping lists for ordinary triangles.
            bool inside=true;
            for(int k=0;k<6;k++) {
                float fa=Plane(a,k),fb=Plane(b,k),fc=Plane(c,k);
                if(fa<0 && fb<0 && fc<0)return new List<Vector4>();
                if(fa<0 || fb<0 || fc<0)inside=false;
            }
            var poly=new List<Vector4>{a,b,c};
            if(inside)return poly;
            for(int k=0;k<6 && poly.Count>0;k++) {
                var output=new List<Vector4>(); var prev=poly[poly.Count-1]; float fp=Plane(prev,k);
                foreach(var p in poly) { float f=Plane(p,k);
                    if((f<0)!=(fp<0)) output.Add(Vector4.LerpUnclamped(prev,p,fp/(fp-f)));
                    if(f>=0) output.Add(p); prev=p;fp=f;
                } poly=output;
            } return poly;
        }
        static float Orient(Vector3 a,Vector3 b,Vector3 p) => (b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
        public static bool Hidden(Vector3 a,Vector3 b,Vector3 p,Vector3 q,Vector3 r,float epsilon,out Vector2 interval)
        {
            interval=default; float area=Orient(p,q,r),lo=0,hi=1;
            if(Mathf.Abs(area)<1e-12f) return false;
            float sign=area>0?1:-1;
            if(!Positive(sign*Orient(p,q,a),sign*Orient(p,q,b),ref lo,ref hi) ||
               !Positive(sign*Orient(q,r,a),sign*Orient(q,r,b),ref lo,ref hi) ||
               !Positive(sign*Orient(r,p,a),sign*Orient(r,p,b),ref lo,ref hi)) return false;
            float Depth(Vector3 x) => (Orient(q,r,x)*p.z+Orient(r,p,x)*q.z+Orient(p,q,x)*r.z)/area;
            if(!Positive(a.z-Depth(a)-epsilon,b.z-Depth(b)-epsilon,ref lo,ref hi)) return false;
            interval=new Vector2(lo,hi);return true;
        }
        static void Union(List<Vector2> list,Vector2 v)
        {
            int a=0;while(a<list.Count && list[a].y<v.x-Eps)a++;
            int b=a;while(b<list.Count && list[b].x<=v.y+Eps) {v.x=Mathf.Min(v.x,list[b].x);v.y=Mathf.Max(v.y,list[b].y);b++;}
            if(b>a)list.RemoveRange(a,b-a);list.Insert(a,v);
        }
        static bool Culled(Triangle t) => (t.cull==2 && t.facing<0)||(t.cull==1 && t.facing>0);

        // AABB BVH limits intersection work to spatially overlapping triangles.
        sealed class Node { public Bounds bounds;public Node left,right;public int start,count; }
        static Node BuildBvh(List<Triangle> triangles,int[] ids,int start,int count,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var bounds=triangles[ids[start]].bounds;
            for(int i=start+1;i<start+count;i++)bounds.Encapsulate(triangles[ids[i]].bounds);
            var node=new Node{bounds=bounds,start=start,count=count}; if(count<=12)return node;
            var size=bounds.size; int axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
            Array.Sort(ids,start,count,Comparer<int>.Create((a,b)=>triangles[a].bounds.center[axis].CompareTo(triangles[b].bounds.center[axis])));
            int half=count/2;node.left=BuildBvh(triangles,ids,start,half,ct);node.right=BuildBvh(triangles,ids,start+half,count-half,ct);return node;
        }
        static List<Vector3> PlaneCut(Triangle t,Triangle plane)
        {
            var p=new[]{t.a,t.b,t.c};var result=new List<Vector3>();
            for(int i=0;i<3;i++) {var a=p[i];var b=p[(i+1)%3];float da=Vector3.Dot(plane.n,a-plane.a),db=Vector3.Dot(plane.n,b-plane.a);
                if(Mathf.Abs(da)<1e-6f)result.Add(a);
                if(da*db<0)result.Add(Vector3.LerpUnclamped(a,b,da/(da-db)));
            }return result;
        }
        static bool Intersect(Triangle a,Triangle b,out Vector3 p,out Vector3 q)
        {
            p=q=default;var axis=Vector3.Cross(a.n,b.n);if(axis.sqrMagnitude<1e-10f)return false;axis.Normalize();
            var ca=PlaneCut(a,b);var cb=PlaneCut(b,a);if(ca.Count<2||cb.Count<2)return false;
            Vector3 origin=ca[0];float amin=0,amax=0,bmin=float.PositiveInfinity,bmax=float.NegativeInfinity;
            foreach(var v in ca){float d=Vector3.Dot(v-origin,axis);amin=Mathf.Min(amin,d);amax=Mathf.Max(amax,d);}
            foreach(var v in cb){float d=Vector3.Dot(v-origin,axis);bmin=Mathf.Min(bmin,d);bmax=Mathf.Max(bmax,d);}
            float lo=Mathf.Max(amin,bmin),hi=Mathf.Min(amax,bmax);if(hi-lo<1e-6f)return false;
            p=origin+axis*lo;q=origin+axis*hi;return true;
        }
        static void FindIntersections(List<Triangle> ts,List<Candidate> candidates,CancellationToken ct)
        {
            if(ts.Count==0)return;
            var ids=new int[ts.Count];for(int i=0;i<ids.Length;i++)ids[i]=i;
            var tree=BuildBvh(ts,ids,0,ids.Length,ct);
            // Quantize intersection endpoints with scale-relative precision to join adjacent triangle pairs.
            float scale=Mathf.Max(tree.bounds.size.magnitude*1e-6f,1e-7f);
            string Key(Vector3 p)=>$"i:{Math.Round(p.x/scale)}:{Math.Round(p.y/scale)}:{Math.Round(p.z/scale)}";
            for(int i=0;i<ts.Count;i++) {ct.ThrowIfCancellationRequested();var a=ts[i]; if(!a.draw)continue;Query(tree);
                void Query(Node node) {
                    if(!node.bounds.Intersects(a.bounds))return;
                    if(node.left!=null){Query(node.left);Query(node.right);return;}
                    for(int j=node.start;j<node.start+node.count;j++) {int id=ids[j];var b=ts[id];
                        if(id<=i && b.draw || id==i || !a.bounds.Intersects(b.bounds))continue;
                        if(a.obj==b.obj && (a.ia==b.ia||a.ia==b.ib||a.ia==b.ic||a.ib==b.ia||a.ib==b.ib||a.ib==b.ic||a.ic==b.ia||a.ic==b.ib||a.ic==b.ic))continue;
                        if(Intersect(a,b,out var p,out var q)) {
                            int owner=Math.Min(a.obj,b.obj);string pair=$"{Math.Min(a.obj,b.obj)}:{Math.Max(a.obj,b.obj)}";
                            candidates.Add(new Candidate{a=p,b=q,ka=pair+Key(p),kb=pair+Key(q),obj=owner,type=16,ownerA=a.selectionId,ownerB=b.selectionId,faces=new[]{i,id},key=$"intersection:{Math.Min(i,id)}:{Math.Max(i,id)}"});
                        }
                    }
                }
            }
        }
        public sealed class Extraction
        {
            public List<Chain> chains;
            public int candidates,spans,intersections;
            public double milliseconds;
        }
        public static Result Build(Snapshot[] snapshots,View view,LineArtSettings settings,CancellationToken ct,IntersectionCache cache=null,int modelHash=0)
        {
            var timer=Stopwatch.StartNew();var extraction=Extract(snapshots,view,settings,ct,cache,modelHash);
            var result=MakeGeometry(extraction.chains,view,settings,ct);result.candidates=extraction.candidates;result.spans=extraction.spans;result.chains=extraction.chains.Count;result.intersections=extraction.intersections;result.milliseconds=timer.Elapsed.TotalMilliseconds;return result;
        }
        public static Extraction Extract(Snapshot[] snapshots,View view,LineArtSettings settings,CancellationToken ct,IntersectionCache cache=null,int modelHash=0)
        {
            var timer=Stopwatch.StartNew();var triangles=new List<Triangle>();var candidates=new List<Candidate>();
            float threshold=Mathf.Cos(Mathf.PI-settings.creaseAngle*Mathf.Deg2Rad);
            foreach(var s in snapshots) {
                ct.ThrowIfCancellationRequested();int first=triangles.Count;
                foreach(var f in s.topology.faces) {
                    var a=s.vertices[f.a];var b=s.vertices[f.b];var c=s.vertices[f.c];
                    var normal=Vector3.Cross(b-a,c-a).normalized*s.orientation;var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);bounds.Expand(1e-6f);
                    triangles.Add(new Triangle{a=a,b=b,c=c,n=normal,bounds=bounds,id=triangles.Count,obj=s.id,selectionId=s.rendererId!=0?s.rendererId:s.id,ia=f.a,ib=f.b,ic=f.c,
                        draw=s.draw,cull=s.cull[f.material],opaque=s.opaque[f.material],facing=Vector3.Dot(normal,view.perspective?view.position-a:view.toCamera)});
                }
                if(!s.draw)continue;
                for(int ei=0;ei<s.topology.edges.Length;ei++) {
                    var edge=s.topology.edges[ei];var face=triangles[first+edge.faces[0]];
                    bool front=false,back=false,crease=false,material=false,allCulled=true;
                    foreach(int fi in edge.faces){var t=triangles[first+fi];front|=t.facing>0;back|=t.facing<=0;
                        crease|=Vector3.Dot(t.n,face.n)<threshold;material|=s.topology.faces[fi].material!=s.topology.faces[edge.faces[0]].material;allCulled&=Culled(t);}
                    int type=edge.faces.Length==1?(settings.contour?1:settings.boundaries?8:0):settings.contour&&front&&back?1:settings.crease&&crease?2:settings.materialBorders&&material?4:0;
                    if(type==0 || settings.occlusion&&allCulled)continue;
                    var faces=new int[edge.faces.Length];for(int i=0;i<faces.Length;i++)faces[i]=first+edge.faces[i];
                    candidates.Add(new Candidate{a=s.vertices[edge.a],b=s.vertices[edge.b],ka=$"v:{edge.a}",kb=$"v:{edge.b}",obj=s.id,type=type,ownerA=s.rendererId!=0?s.rendererId:s.id,ownerB=s.rendererId!=0?s.rendererId:s.id,faces=faces,key=$"edge:{ei}"});
                }
            }
            int before=candidates.Count;
            if(settings.intersections) {
                List<Candidate> intersections=null;
                if(cache!=null)lock(cache){if(cache.key==modelHash)intersections=cache.segments;}
                if(intersections==null){intersections=new List<Candidate>();FindIntersections(triangles,intersections,ct);
                    if(cache!=null)lock(cache){cache.key=modelHash;cache.segments=intersections;}}
                candidates.AddRange(intersections);
            }
            int intersectionCount=candidates.Count-before;
            const int size=48;var grid=new List<int>[size*size];var projected=new List<ProjectedTriangle>();
            int Cell(float x)=>Mathf.Clamp((int)Mathf.Floor((x+1)*size*.5f),0,size-1);
            if(settings.occlusion)foreach(var t in triangles) {
                ct.ThrowIfCancellationRequested();if(!t.opaque||Culled(t))continue;
                var poly=ClipTriangle(Project(t.a,view.matrix),Project(t.b,view.matrix),Project(t.c,view.matrix));
                for(int k=1;k+1<poly.Count;k++) {
                    if(poly[0].w<=0||poly[k].w<=0||poly[k+1].w<=0)continue;
                    var a=Ndc(poly[0]);var b=Ndc(poly[k]);var c=Ndc(poly[k+1]);float area=Orient(a,b,c);if(Mathf.Abs(area)<1e-12f)continue;
                    var p=new ProjectedTriangle{a=a,b=b,c=c,source=t.id,area=area,minX=Mathf.Min(a.x,b.x,c.x),maxX=Mathf.Max(a.x,b.x,c.x),minY=Mathf.Min(a.y,b.y,c.y),maxY=Mathf.Max(a.y,b.y,c.y),minZ=Mathf.Min(a.z,b.z,c.z)};
                    int id=projected.Count;projected.Add(p);
                    for(int y=Cell(p.minY);y<=Cell(p.maxY);y++)for(int x=Cell(p.minX);x<=Cell(p.maxX);x++){int cell=y*size+x;(grid[cell]??(grid[cell]=new List<int>())).Add(id);}
                }
            }
            var seen=new int[projected.Count];var spans=new List<Span>();
            for(int ci=0;ci<candidates.Count;ci++) {
                if((ci&63)==0)ct.ThrowIfCancellationRequested();var s=candidates[ci];
                var ca=Project(s.a,view.matrix);var cb=Project(s.b,view.matrix);
                if(!Clip(ca,cb,out float t0,out float t1))continue;
                var c0=Vector4.LerpUnclamped(ca,cb,t0);var c1=Vector4.LerpUnclamped(ca,cb,t1);if(c0.w<=0||c1.w<=0)continue;
                var a=Ndc(c0);var b=Ndc(c1);
                if(new Vector2((a.x-b.x)*view.width,(a.y-b.y)*view.height).sqrMagnitude<.01f)continue;
                var hidden=new List<Vector2>();float minX=Mathf.Min(a.x,b.x),maxX=Mathf.Max(a.x,b.x),minY=Mathf.Min(a.y,b.y),maxY=Mathf.Max(a.y,b.y),maxZ=Mathf.Max(a.z,b.z);
                if(settings.occlusion) {
                    bool fullyHidden=false;
                    for(int y=Cell(minY);y<=Cell(maxY)&&!fullyHidden;y++)for(int x=Cell(minX);x<=Cell(maxX)&&!fullyHidden;x++){
                        var bucket=grid[y*size+x];if(bucket==null)continue;
                        foreach(int id in bucket) {if(seen[id]==ci+1)continue;seen[id]=ci+1;var p=projected[id];
                            if(p.minZ>=maxZ-settings.depthEpsilon || p.minX>maxX||p.maxX<minX||p.minY>maxY||p.maxY<minY||Array.IndexOf(s.faces,p.source)>=0)continue;
                            if(Hidden(a,b,p.a,p.b,p.c,settings.depthEpsilon,out var interval))Union(hidden,interval);
                            if(hidden.Count==1&&hidden[0].x<=Eps&&hidden[0].y>=1-Eps){fullyHidden=true;break;}
                        }
                    }
                }
                float cursor=0;int span=0;
                foreach(var h in hidden){if(h.x>cursor+Eps)Add(cursor,h.x);cursor=Mathf.Max(cursor,h.y);}
                if(cursor<1-Eps)Add(cursor,1);
                void Add(float lo,float hi) {
                    float WorldT(float x)=>t0+(t1-t0)*(x*c0.w/(c1.w*(1-x)+c0.w*x));
                    float u=WorldT(lo),v=WorldT(hi);if(v-u<Eps)return;
                    spans.Add(new Span{a=Vector3.LerpUnclamped(s.a,s.b,u),b=Vector3.LerpUnclamped(s.a,s.b,v),obj=s.obj,type=s.type,ownerA=s.ownerA,ownerB=s.ownerB,key=s.key+$"/span:{span}",ka=u<Eps?s.ka:$"cut:{ci}:{span}:a",kb=v>1-Eps?s.kb:$"cut:{ci}:{span}:b"});span++;
                }
            }
            return new Extraction{chains=ChainSpans(spans,view,settings),candidates=candidates.Count,spans=spans.Count,intersections=intersectionCount,milliseconds=timer.Elapsed.TotalMilliseconds};
        }
        static List<Chain> ChainSpans(List<Span> spans,View view,LineArtSettings settings)
        {
            var adjacency=new Dictionary<string,List<int>>();var used=new bool[spans.Count];var chains=new List<Chain>();
            string Key(Span s,string k)=>$"{s.obj}/{(settings.connectDifferentEdgeTypes?0:s.type)}/{k}";
            for(int i=0;i<spans.Count;i++) {
                var s=spans[i];s.ka=Key(s,s.ka);s.kb=Key(s,s.kb);
                void AddAdj(string key){if(!adjacency.TryGetValue(key,out var list))adjacency[key]=list=new List<int>();list.Add(i);}
                // Degenerate spans with ka==kb must not be inserted twice, or adjacency.Count==2
                // falsely looks like a 2-regular loop and Walk marks them closed.
                AddAdj(s.ka);if(s.ka!=s.kb)AddAdj(s.kb);
            }
            // Pair once, independently of traversal order. A branch only joins mutually
            // best continuations, so an earlier walk cannot steal a straighter continuation.
            var continuations=new Dictionary<(string,int),int>();
            foreach(var entry in adjacency) {
                var neighbors=entry.Value;
                if(settings.connectJunctions && neighbors.Count>2) {
                    Vector2 Direction(int index) {
                        var edge=spans[index];bool forward=edge.ka==entry.Key;
                        var a=Project(forward?edge.a:edge.b,view.matrix);
                        var b=Project(forward?edge.b:edge.a,view.matrix);
                        if(a.w<=0 || b.w<=0)return Vector2.zero;
                        return new Vector2((b.x/b.w-a.x/a.w)*view.width,(b.y/b.w-a.y/a.w)*view.height).normalized;
                    }
                    var best=new int[neighbors.Count];
                    float limit=Mathf.Cos(Mathf.Clamp(settings.junctionMaxAngle,0,90)*Mathf.Deg2Rad);
                    for(int a=0;a<neighbors.Count;a++) {
                        best[a]=-1;float score=limit-1e-6f;var direction=Direction(neighbors[a]);
                        if(direction.sqrMagnitude==0)continue;
                        for(int b=0;b<neighbors.Count;b++)if(a!=b) {
                            var other=Direction(neighbors[b]);if(other.sqrMagnitude==0)continue;
                            float alignment=-Vector2.Dot(direction,other);
                            if(alignment>score){score=alignment;best[a]=b;}
                        }
                    }
                    for(int a=0;a<neighbors.Count;a++)if(best[a]>=0 && best[best[a]]==a)
                        continuations[(entry.Key,neighbors[a])]=neighbors[best[a]];
                }
            }
            bool Continue(string key,int current,out int candidate) {
                var neighbors=adjacency[key];
                if(neighbors.Count==2){candidate=neighbors[0]==current?neighbors[1]:neighbors[0];return true;}
                return continuations.TryGetValue((key,current),out candidate);
            }
            void Walk(int i,bool reverse) {
                int firstEdge=i;var start=spans[i];string first=reverse?start.kb:start.ka,from=first,anchor=null;
                var c=new Chain{ownerA=start.ownerA,ownerB=start.ownerB};var pointKeys=new List<string>{first};c.points.Add(reverse?start.b:start.a);
                while(!used[i]){used[i]=true;var s=spans[i];bool forward=from==s.ka;string next=forward?s.kb:s.ka;
                    if(anchor==null||string.CompareOrdinal(s.key,anchor)<0)anchor=s.key;
                    c.points.Add(forward?s.b:s.a);pointKeys.Add(next);
                    // Real loops need at least a triangle (3 keys before seam pop → 2 after).
                    if(next==first && Continue(next,i,out int closingEdge) && closingEdge==firstEdge){if(pointKeys.Count>=4)c.closed=true;break;}
                    if(!Continue(next,i,out int candidate)||candidate==i||used[candidate])break;
                    from=next;i=candidate;
                }
                if(c.closed){
                    c.points.RemoveAt(c.points.Count-1);pointKeys.RemoveAt(pointKeys.Count-1);
                    // Guard: collapsed/degenerate loops must not index pointKeys[1].
                    if(pointKeys.Count<2||c.points.Count<2)c.closed=false;
                    else{
                        int seam=0;
                        for(int k=1;k<pointKeys.Count;k++)if(string.CompareOrdinal(pointKeys[k],pointKeys[seam])<0)seam=k;
                        var p=c.points.GetRange(0,seam);c.points.RemoveRange(0,seam);c.points.AddRange(p);
                        var keys=pointKeys.GetRange(0,seam);pointKeys.RemoveRange(0,seam);pointKeys.AddRange(keys);
                        if(pointKeys.Count>=2&&string.CompareOrdinal(pointKeys[1],pointKeys[pointKeys.Count-1])>0)
                            c.points.Reverse(1,c.points.Count-1);
                    }
                }
                c.seed=Hash($"{start.obj}/{start.type}/{anchor}");if(c.points.Count>=2)chains.Add(c);
            }
            for(int i=0;i<spans.Count;i++){if(used[i])continue;var s=spans[i];if(!Continue(s.ka,i,out _))Walk(i,false);else if(!Continue(s.kb,i,out _))Walk(i,true);}
            for(int i=0;i<spans.Count;i++)if(!used[i])Walk(i,false);return chains;
        }
        public static Result MakeGeometry(List<Chain> chains,View view,LineArtSettings settings,CancellationToken ct)
        {
            var positions=new List<Vector3>();var previous=new List<Vector3>();var next=new List<Vector3>();var styles=new List<Vector4>();var indices=new List<int>();
            foreach(var chain in chains) {
                ct.ThrowIfCancellationRequested();var points=new List<Vector3>(chain.points);if(chain.closed)points.Add(points[0]);
                var distances=new float[points.Count];for(int i=1;i<points.Count;i++)distances[i]=distances[i-1]+Vector3.Distance(points[i-1],points[i]);
                float total=distances[distances.Length-1];if(total<Eps)continue;
                float trim=(1-LengthScale(chain.seed,chain.closed,settings.lengthTrim,settings.lengthRandomness))*.5f;
                bool closed=chain.closed&&Mathf.Abs(trim)<Eps;float start=total*trim,end=total*(1-trim);
                var samples=new List<Vector3>();var arcs=new List<float>();
                for(int i=1;i<points.Count;i++) {
                    float lo=i==1?start:Mathf.Max(start,distances[i-1]),hi=i==points.Count-1?end:Mathf.Min(end,distances[i]);
                    float size=distances[i]-distances[i-1];if(hi-lo<Eps||size<Eps)continue;
                    var a=Vector3.LerpUnclamped(points[i-1],points[i],(lo-distances[i-1])/size);var b=Vector3.LerpUnclamped(points[i-1],points[i],(hi-distances[i-1])/size);
                    var pa=Project(a,view.matrix);var pb=Project(b,view.matrix);
                    float pixels=pa.w>1e-5f&&pb.w>1e-5f?new Vector2((pa.x/pa.w-pb.x/pb.w)*view.width*.5f,(pa.y/pa.w-pb.y/pb.w)*view.height*.5f).magnitude:0;
                    int steps=settings.thicknessCurve||settings.noise>0?Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(settings.CurveSamples*(hi-lo)/(end-start)),Mathf.CeilToInt(pixels/8)),1,256):1;
                    if(samples.Count==0){samples.Add(a);arcs.Add(0);}
                    for(int j=1;j<=steps;j++){samples.Add(Vector3.LerpUnclamped(a,b,(float)j/steps));arcs.Add((lo+(hi-lo)*j/steps-start)/(end-start));}
                }
                if(samples.Count<2)continue;
                // Keep the duplicated seam vertex at U=1 so textures never interpolate backward across it.
                int count=samples.Count,baseVertex=positions.Count;
                float phase=(chain.seed%10000)*.001f;
                for(int i=0;i<count;i++) {
                    var prev=samples[i==0?(closed?count-2:0):i-1];var after=samples[i==count-1?(closed?1:i):i+1];
                    for(int side=-1;side<=1;side+=2){positions.Add(samples[i]);previous.Add(prev);next.Add(after);styles.Add(new Vector4(side,arcs[i],closed?1:0,phase));}
                    if(i+1<count){int a=baseVertex+i*2,b=a+2;indices.Add(a);indices.Add(a+1);indices.Add(b);indices.Add(a+1);indices.Add(b+1);indices.Add(b);}
                }
            }
            return new Result{positions=positions.ToArray(),previous=previous.ToArray(),next=next.ToArray(),stroke=styles.ToArray(),indices=indices.ToArray()};
        }
    }
}
