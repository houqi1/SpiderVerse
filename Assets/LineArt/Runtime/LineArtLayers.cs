using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]
#endif

namespace SpiderVerse.LineArt
{
    // Scene selections and appearance are separate from expensive camera geometry.
    // Reuse these records and property blocks between frames; no renderer-wide scene scan here.
    internal sealed class LineArtLayers
    {
        internal sealed class Draw
        {
            internal readonly HashSet<Renderer> targets=new HashSet<Renderer>();
            internal readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
            internal LineArtAppearance appearance;
            internal int batch;
            readonly LineArtAppearance previousAppearance=new LineArtAppearance();
            int width,height;bool initialized;
            internal void UpdateProperties(int w,int h)
            {
                if(initialized&&width==w&&height==h&&appearance.SameAppearance(previousAppearance))return;
                LineArtGpu.SetProperties(properties,appearance,w,h);appearance.CopyTo(previousAppearance);width=w;height=h;initialized=true;
            }
        }
        internal readonly List<Draw> draws=new List<Draw>();
        internal readonly HashSet<Renderer> targets=new HashSet<Renderer>();
        readonly Dictionary<Renderer,bool> eligibility=new Dictionary<Renderer,bool>();
        readonly HashSet<Renderer> previousTargets=new HashSet<Renderer>();
        readonly List<Draw> pool=new List<Draw>();
        readonly List<ObjectLineArtSource> sources=new List<ObjectLineArtSource>();
        internal bool UnionChanged {get;private set;}

        Draw Next(LineArtAppearance appearance)
        {
            int index=draws.Count;
            if(index==pool.Count)pool.Add(new Draw());
            var draw=pool[index];draw.targets.Clear();draw.appearance=appearance;draws.Add(draw);return draw;
        }
        void Add(HashSet<Renderer> target,Renderer[] renderers,Camera camera)
        {
            if(renderers==null)return;
            foreach(var r in renderers){
                if(!r)continue;
                if(!eligibility.TryGetValue(r,out bool visible)){visible=(r is MeshRenderer||r is SkinnedMeshRenderer)&&r.enabled&&!r.forceRenderingOff&&r.gameObject.activeInHierarchy&&(!camera||((1<<r.gameObject.layer)&camera.cullingMask)!=0);eligibility.Add(r,visible);}
                if(visible)target.Add(r);
            }
        }
        internal void Refresh(LineArtSettings defaults,Camera camera)
        {
            draws.Clear();targets.Clear();sources.Clear();eligibility.Clear();
            foreach(var source in ObjectLineArtSource.Active)if(source&&source.isActiveAndEnabled)sources.Add(source);
            sources.Sort((a,b)=>a.GetInstanceID().CompareTo(b.GetInstanceID()));
            // Legacy Sources share one drawing, matching their previous union behavior.
            Draw legacy=null;
            foreach(var source in sources)if(source.layers==null||source.layers.Length==0){if(legacy==null)legacy=Next(defaults);Add(legacy.targets,source.GetRenderers(),camera);}
            if(legacy!=null&&legacy.targets.Count==0)draws.RemoveAt(draws.Count-1);
            foreach(var source in sources)
            {
                if(source.layers==null||source.layers.Length==0)continue;
                Renderer[] inherited=null;
                foreach(var layer in source.layers)
                {
                    if(layer==null||!layer.enabled)continue;
                    var draw=Next(layer.appearance??defaults);
                    Add(draw.targets,layer.useSourceRenderers?(inherited??(inherited=source.GetRenderers())):layer.renderers,camera);
                    if(draw.targets.Count==0)draws.RemoveAt(draws.Count-1);
                }
            }
            foreach(var draw in draws)targets.UnionWith(draw.targets);
            UnionChanged=!previousTargets.SetEquals(targets);
            if(UnionChanged){previousTargets.Clear();previousTargets.UnionWith(targets);}
        }
    }
}
