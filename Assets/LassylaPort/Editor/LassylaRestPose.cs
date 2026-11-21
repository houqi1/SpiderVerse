using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace LassylaPort.Editor
{
    public static class LassylaRestPose
    {
        static int Depth(Transform t){int n=0;while(t.parent){n++;t=t.parent;}return n;}
        static float Error(Matrix4x4 a,Matrix4x4 b){float e=0;for(int i=0;i<16;i++)e=Mathf.Max(e,Mathf.Abs(a[i]-b[i]));return e;}
        static float SetRest(GameObject root){
            // Snapshot every renderer matrix BEFORE moving any bones. The imported FBX uses
            // centimetre-scale mesh transforms; mixing updated and original matrices compounds it.
            var desired=new Dictionary<Transform,Matrix4x4>();
            foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled)){
                var bind=r.sharedMesh.bindposes;var meshWorld=r.localToWorldMatrix;
                for(int i=0;i<bind.Length;i++)if(r.bones[i]){
                    var world=meshWorld*bind[i].inverse;
                    if(desired.TryGetValue(r.bones[i],out var previous)&&Error(previous,world)>.001f)throw new Exception("Inconsistent FBX bind poses: "+r.bones[i].name);
                    desired[r.bones[i]]=world;
                }
            }
            foreach(var pair in desired.OrderBy(x=>Depth(x.Key))){
                var bone=pair.Key;var local=bone.parent?bone.parent.worldToLocalMatrix*pair.Value:pair.Value;
                var x=(Vector3)local.GetColumn(0);var y=(Vector3)local.GetColumn(1);var z=(Vector3)local.GetColumn(2);
                var scale=new Vector3(x.magnitude,y.magnitude,z.magnitude);if(local.determinant<0)scale.x=-scale.x;
                var rotation=Quaternion.LookRotation(z/scale.z,y/scale.y);
                bone.localPosition=local.GetColumn(3);bone.localRotation=rotation;bone.localScale=scale;
            }
            float error=0;
            foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled)){
                var bind=r.sharedMesh.bindposes;
                for(int i=0;i<bind.Length;i++)if(r.bones[i])error=Mathf.Max(error,Error(r.worldToLocalMatrix*r.bones[i].localToWorldMatrix*bind[i],Matrix4x4.identity));
            }
            return error;
        }
        public static void Apply(){
            var root=GameObject.Find("Spider-Gwen Lassyla");if(!root)throw new Exception("Missing Gwen");
            var clone=UnityEngine.Object.Instantiate(root);clone.name="Temporary bind-pose verification";clone.hideFlags=HideFlags.HideAndDontSave;
            float error;
            try{error=SetRest(clone);if(error>.0001f)throw new Exception("Bind pose skin matrix error: "+error);}
            finally{UnityEngine.Object.DestroyImmediate(clone);}
            Undo.RegisterFullObjectHierarchyUndo(root,"Match Blender rest pose");
            error=SetRest(root);
            if(error>.0001f)throw new Exception("Applied rest pose matrix error: "+error);
            File.WriteAllText(LassylaSetup.Work+"/rest-pose-validation.txt","Maximum skin matrix deviation from identity: "+error.ToString("R"));
            PrefabUtility.ApplyPrefabInstance(root,InteractionMode.AutomatedAction);EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
            SceneView.RepaintAll();
        }
    }
}
