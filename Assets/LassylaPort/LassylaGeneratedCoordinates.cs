using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LassylaPort
{
    public static class LassylaGeneratedCoordinates
    {
        // Store Generated before skinning. Leave any UV set authored by the artist intact.
        public static bool Bake(Mesh mesh,IReadOnlyDictionary<Vector3,Vector3> authored=null)
        {
            int channel=!mesh.HasVertexAttribute(VertexAttribute.TexCoord7)?7:2;
            if(mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0+channel)))return false;
            var vertices=mesh.vertices;
            if(vertices.Length==0)return false;
            var low=vertices[0];var high=vertices[0];
            foreach(var point in vertices){low=Vector3.Min(low,point);high=Vector3.Max(high,point);}
            var generated=new List<Vector4>(vertices.Length);
            foreach(var p in vertices){
                var uv=At(p,low,high,authored);
                generated.Add(new Vector4(uv.x,uv.y,uv.z,-97.25f));
            }
            mesh.SetUVs(channel,generated);
            return true;
        }
        public static Vector3 At(Vector3 p,Vector3 low,Vector3 high,IReadOnlyDictionary<Vector3,Vector3> authored)
        {
            if(authored!=null && authored.TryGetValue(p,out var result))return result;
            var span=high-low;
            return new Vector3(span.x>0?(high.x-p.x)/span.x:.5f,span.y>0?(p.y-low.y)/span.y:.5f,
                span.z>0?(p.z-low.z)/span.z:.5f);
        }
    }
}
