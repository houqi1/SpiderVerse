#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace SpiderVerse.LineArt.Editor
{
    public static class LineArtDiagnostics
    {
        static Task<string> pending;
        public static void BatchRun(){UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/LineArt/LineArtPreview.unity");Run();}
        [MenuItem("SpiderVerse/Line Art/Diagnose Missing Strokes")]
        public static void Run()
        {
            if(pending!=null)return;
            var text=new StringBuilder();text.AppendLine(DateTime.Now.ToString("O"));
            var sources=UnityEngine.Object.FindObjectsByType<ObjectLineArtSource>(FindObjectsSortMode.None);
            var targets=new HashSet<Renderer>();
            foreach(var source in sources){text.AppendLine($"SOURCE {source.name} enabled={source.isActiveAndEnabled}");if(source.isActiveAndEnabled)foreach(var r in source.renderers.Length>0?source.renderers:source.GetComponentsInChildren<Renderer>())if(r)targets.Add(r);}
            var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/LineArt/LineArt_Renderer.asset");
            var feature=renderer?.rendererFeatures.OfType<ObjectLineArtFeature>().FirstOrDefault();
            var camera=Camera.main;
            if(!feature||!camera){text.AppendLine("Missing feature or main camera.");File.WriteAllText("Library/LineArtDiagnostics.txt",text.ToString());return;}
            string Path(Transform t){string k=t.name+":"+t.GetSiblingIndex();while(t.parent){t=t.parent;k=t.name+":"+t.GetSiblingIndex()+"/"+k;}return k;}
            var groups=new Dictionary<string,HashSet<int>>();
            foreach(var r in targets){var mesh=r is SkinnedMeshRenderer sk?sk.sharedMesh:r.GetComponent<MeshFilter>()?.sharedMesh;
                text.AppendLine($"RENDERER {Path(r.transform)} mesh={mesh?.name} readable={mesh?.isReadable} enabled={r.enabled} active={r.gameObject.activeInHierarchy} forceOff={r.forceRenderingOff} layer={r.gameObject.layer} determinant={r.localToWorldMatrix.determinant} bounds={r.bounds}");
                if(r is SkinnedMeshRenderer skin && mesh && mesh.isReadable && r.gameObject.activeInHierarchy) {
                    var baked=new Mesh();
                    foreach(bool useScale in new[]{false,true}) {
                        skin.BakeMesh(baked,useScale);var vs=baked.vertices;
                        var rb=new Bounds(r.localToWorldMatrix.MultiplyPoint3x4(vs[0]),Vector3.zero);
                        var tb=new Bounds(r.transform.TransformPoint(vs[0]),Vector3.zero);
                        foreach(var v in vs){rb.Encapsulate(r.localToWorldMatrix.MultiplyPoint3x4(v));tb.Encapsulate(r.transform.TransformPoint(v));}
                        text.AppendLine($"BAKE {r.name} useScale={useScale} rendererMatrix={rb} transformMatrix={tb} transformDet={r.transform.localToWorldMatrix.determinant}");
                    }
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                string root=Path(r.transform.root);if(!groups.TryGetValue(root,out var ids))groups[root]=ids=new HashSet<int>();ids.Add(unchecked((int)LineArtGeometry.Hash(r.gameObject.scene.path+"/"+Path(r.transform))));
            }
            var field=typeof(ObjectLineArtFeature).GetField("pass",BindingFlags.NonPublic|BindingFlags.Instance);var pass=field.GetValue(feature);if(pass==null){feature.Create();pass=field.GetValue(feature);}
            var capture=pass.GetType().GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Instance);
            var snapshots=(LineArtGeometry.Snapshot[])capture.Invoke(pass,new object[]{camera,targets,feature.settings});
            text.AppendLine($"Captured {snapshots.Length} meshes, {snapshots.Count(s=>s.draw)} targets. Camera {camera.name}, mask={camera.cullingMask}");
            var view=new LineArtGeometry.View{matrix=camera.projectionMatrix*camera.worldToCameraMatrix,position=camera.transform.position,toCamera=-camera.transform.forward,perspective=!camera.orthographic,width=camera.pixelWidth,height=camera.pixelHeight};
            var settings=feature.settings.Copy();
            pending=Task.Run(()=>{foreach(var group in groups){var subset=snapshots.Select(s=>new LineArtGeometry.Snapshot{id=s.id,draw=s.draw&&group.Value.Contains(s.id),topology=s.topology,vertices=s.vertices,cull=s.cull,opaque=s.opaque,orientation=s.orientation}).ToArray();
                foreach(bool occlusion in new[]{false,true}){var options=settings.Copy();options.occlusion=occlusion;options.intersections=false;var result=LineArtGeometry.Build(subset,view,options,CancellationToken.None);text.AppendLine($"RESULT {group.Key} occlusion={occlusion}: candidates={result.candidates}, spans={result.spans}, chains={result.chains}, vertices={result.positions.Length}");}
            }return text.ToString();});
            EditorApplication.update+=Finish;
        }
        static void Finish(){if(pending==null||!pending.IsCompleted)return;EditorApplication.update-=Finish;File.WriteAllText("Library/LineArtDiagnostics.txt",pending.IsFaulted?pending.Exception.ToString():pending.Result);bool failed=pending.IsFaulted;pending=null;Debug.Log("Line Art diagnostics saved to Library/LineArtDiagnostics.txt");if(Application.isBatchMode)EditorApplication.Exit(failed?1:0);}
    }
}
#endif
