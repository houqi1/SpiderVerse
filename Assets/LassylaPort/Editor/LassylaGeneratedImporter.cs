using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LassylaPort.Editor
{
    // Work on the import artifact, leaving source FBXs and their existing UVs untouched.
    sealed class LassylaGeneratedImporter : AssetPostprocessor
    {
        public override uint GetVersion()=>3;
        void OnPostprocessModel(GameObject model)
        {
            if(!assetPath.StartsWith("Assets/",System.StringComparison.Ordinal))return;
            var meshes=new HashSet<Mesh>();
            foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(r.sharedMesh)meshes.Add(r.sharedMesh);
            foreach(var f in model.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh)meshes.Add(f.sharedMesh);
            foreach(var mesh in meshes)LassylaGeneratedCoordinates.Bake(mesh,AuthoredGwenCoordinates(mesh));
        }

        Dictionary<Vector3,Vector3> AuthoredGwenCoordinates(Mesh mesh)
        {
            if(assetPath!="Assets/Model/Spider-Gwen.fbx")return null;
            var path=LassylaSetup.Dir+"/Meshes/"+mesh.name.Replace(' ','_')+".asset";
            if(!System.IO.File.Exists(path))return null;
            context.DependsOnArtifact(path);
            return ReadAuthored(path);
        }
        internal static Dictionary<Vector3,Vector3> ReadAuthored(string path)
        {
            var reference=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!reference)return null;
            var generated=new List<Vector3>();reference.GetUVs(2,generated);
            var points=reference.vertices;
            if(generated.Count!=points.Length)return null;
            var lookup=new Dictionary<Vector3,Vector3>(points.Length);
            for(int i=0;i<points.Length;i++)lookup[points[i]]=generated[i];
            return lookup;
        }
    }
}
