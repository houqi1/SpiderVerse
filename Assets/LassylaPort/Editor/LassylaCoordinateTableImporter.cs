using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace LassylaPort.Editor
{
    [ScriptedImporter(2,"lassylaedges")]
    sealed class LassylaCoordinateTableImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var pixels=new List<Color>();
            var entries=new List<LassylaMeshCoordinates.Entry>();
            foreach(var line in File.ReadAllLines(context.assetPath)){
                if(string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))continue;
                var fields=line.Split('|');
                bool builtin=fields[0].StartsWith("builtin/");
                if(!builtin)context.DependsOnArtifact(fields[0]);
                var mesh=builtin?Resources.GetBuiltinResource<Mesh>(fields[0].Substring(8)):AssetDatabase.LoadAssetAtPath<Mesh>(fields[0]);
                if(!mesh)throw new InvalidDataException("Missing mesh "+fields[0]);
                Dictionary<Vector3,Vector3> authored=null;
                if(fields.Length==2){
                    string path=LassylaSetup.Dir+"/Meshes/"+fields[1]+".asset";
                    context.DependsOnArtifact(path);
                    authored=LassylaGeneratedImporter.ReadAuthored(path);
                }
                entries.Add(new LassylaMeshCoordinates.Entry{mesh=mesh,offset=pixels.Count});
                using var data=MeshUtility.AcquireReadOnlyMeshData(mesh);
                using var points=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp);
                data[0].GetVertices(points);
                var lo=points[0];var hi=lo;
                foreach(var p in points){lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}
                foreach(var p in points){
                    var uv=LassylaGeneratedCoordinates.At(p,lo,hi,authored);
                    pixels.Add(new Color(uv.x,uv.y,uv.z,1));
                }
            }
            const int width=256;
            int height=(pixels.Count+width-1)/width;
            while(pixels.Count<width*height)pixels.Add(Color.clear);
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true){name="Rest coordinates",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            image.SetPixels(pixels.ToArray());image.Apply(false,true);
            var catalog=ScriptableObject.CreateInstance<LassylaMeshCoordinates>();
            catalog.entries=entries.ToArray();catalog.table=image;
            context.AddObjectToAsset("coords",catalog);context.AddObjectToAsset("table",image);context.SetMainObject(catalog);
        }
    }
}
