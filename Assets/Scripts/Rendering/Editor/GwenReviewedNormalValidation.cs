using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public static class GwenReviewedNormalValidation
{
    [Serializable] class Result { public string unity,texture;public List<string> compiledShaders=new List<string>();public List<ClipResult> clips=new List<ClipResult>(); public int materialBindings; }
    [Serializable] class ClipResult {public string name;public float length;public int frames,largeRotationSamples;public float maxPositionChange,maximumNormalRotation,maximumRigidSkinError,p95MixedSkinError,meanAnimatedDeviation,meanStaleDeviation;}
    static Vector3 Basis(Vector3 n,Vector4 t,Vector3 ts) {n.Normalize();Vector3 tangent=((Vector3)t-n*Vector3.Dot(n,t)).normalized;return (tangent*ts.x+Vector3.Cross(n,tangent)*(t.w<0?-1:1)*ts.y+n*ts.z).normalized;}
    [MenuItem("Tools/Rendering/Validate Reviewed Gwen Animation Normals")]
    public static async void Run() { await RunAsync(); }
    public static async Task RunAsync()
    {
        string work=GwenReviewedNormalInstall.Work;Directory.CreateDirectory(work+"/Frames");
        var result=new Result{unity=Application.unityVersion,texture=GwenReviewedNormalInstall.Destination+"/Gwen_Reviewed_NormalTS_4K.png"};
        var importer=(TextureImporter)AssetImporter.GetAtPath(result.texture);bool previousReadable=importer.isReadable;importer.isReadable=true;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(result.texture);
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        GameObject root=null,camObject=null;Material debug=null;RenderTexture rt=null;PlayableGraph graph=default;Scene scene=default;
        try {
            foreach(string name in new[]{"Custom/Toon","Custom/ToonCel","Custom/ObjectSpaceNormalWrapLambert","Hidden/Gwen/SkinnedNormalValidation"}) {
                var sh=Shader.Find(name);if(!sh)throw new Exception("Missing shader "+name);var mat=new Material(sh);bool ok=mat.SetPass(0);UnityEngine.Object.DestroyImmediate(mat);if(!ok||ShaderUtil.ShaderHasError(sh))throw new Exception("Shader error "+name+": "+string.Join("\n",ShaderUtil.GetShaderMessages(sh).Select(x=>x.message)));result.compiledShaders.Add(name);
            }
            var renderers=Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>().Where(r=>r.gameObject.scene.IsValid()&&!EditorUtility.IsPersistent(r)&&AssetDatabase.GetAssetPath(r.sharedMesh).StartsWith("Assets/Texture/GwenPainterly/Meshes/")).ToArray();
            if(renderers.Length==0)throw new Exception("No Gwen atlas skinned meshes in scene.");
            foreach(var r in renderers)foreach(var m in r.sharedMaterials) {
                if(m.HasProperty("_WrapNormalTangentSpace")) {
                    if(m.GetFloat("_WrapNormalTangentSpace")<.5f||m.GetTexture("_WrapNormalMap")!=texture||m.GetFloat("_WrapNormalUV")!=1)throw new Exception("Incorrect scene binding: "+r.name+" / "+m.name);
                } else if(m.HasProperty("_NormalTangentSpace")) {
                    if(m.GetFloat("_NormalTangentSpace")<.5f||m.GetTexture("_ObjectNormalMap")!=texture)throw new Exception("Incorrect wrap scene binding: "+m.name);
                } else continue;
                result.materialBindings++;
            }
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Model/Spider-Gwen.fbx"));root.name="Gwen Validation";SceneManager.MoveGameObjectToScene(root,scene);
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var animator=root.GetComponent<Animator>();if(!animator)animator=root.AddComponent<Animator>();animator.enabled=true;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            debug=new Material(Shader.Find("Hidden/Gwen/SkinnedNormalValidation"));debug.SetTexture("_NormalAtlas",texture);
            var selected=new List<SkinnedMeshRenderer>();
            foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var source=renderers.FirstOrDefault(x=>x.name==r.name);
                if(!source){r.gameObject.SetActive(false);continue;}
                r.sharedMesh=source.sharedMesh;r.sharedMaterials=Enumerable.Repeat(debug,r.sharedMesh.subMeshCount).ToArray();r.updateWhenOffscreen=true;r.gameObject.SetActive(r.name!="wait head");selected.Add(r);
            }
            camObject=new GameObject("Validation Camera");SceneManager.MoveGameObjectToScene(camObject,scene);var camera=camObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.065f,.085f,1);camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=100;
            rt=new RenderTexture(720,840,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();camera.targetTexture=rt;
            var clips=new[]{"dash","skill03-02"}.Select(name=>AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Model/Armature_hero_spidergwen01_S03@"+name+".anim")).Where(c=>c).ToArray();
            if(clips.Length<2)throw new Exception("Expected the project's dash and skill03-02 clips.");
            foreach(var clip in clips) {
                animator.Rebind();graph=PlayableGraph.Create("Reviewed Normal Animation");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);var output=AnimationPlayableOutput.Create(graph,"Skeleton",animator);output.SetSourcePlayable(playable);graph.Play();
                var cr=new ClipResult{name=clip.name,length=clip.length,frames=16};var mixed=new List<float>();double animated=0,stale=0;int compared=0;
                for(int frame=0;frame<cr.frames;frame++) {
                    playable.SetTime(clip.length*frame/(cr.frames-1));graph.Evaluate(0);
                    // Let Unity's player loop upload the newly posed skinned vertex buffers
                    // before capturing a frame; Camera.Render alone reuses the previous upload.
                    EditorApplication.QueuePlayerLoopUpdate();await Task.Delay(80);
                    Bounds frameBounds=default;bool haveBounds=false;
                    foreach(var r in selected) {
                        var mesh=r.sharedMesh;var posed=new Mesh();r.BakeMesh(posed);var n=mesh.normals;var t=mesh.tangents;var uv=mesh.uv2;var pn=posed.normals;var pt=posed.tangents;var pp=posed.vertices;var p=mesh.vertices;var weights=mesh.boneWeights;var binds=mesh.bindposes;
                        if(n.Length!=pn.Length||pt.Length!=n.Length)throw new Exception("Skinning lost normal/tangent data.");
                        Matrix4x4[] bones=r.bones.Select((b,i)=>r.transform.worldToLocalMatrix*b.localToWorldMatrix*binds[i]).ToArray();
                        for(int i=0;i<n.Length;i+=7) {
                            Color c=texture.GetPixelBilinear(uv[i].x,uv[i].y);var ts=new Vector3(c.r*2-1,c.g*2-1,c.b*2-1).normalized;var rest=Basis(n[i],t[i],ts);var actual=Basis(pn[i],pt[i],ts);
                            var bw=weights[i];Vector3 expected=bones[bw.boneIndex0].MultiplyVector(rest)*bw.weight0+bones[bw.boneIndex1].MultiplyVector(rest)*bw.weight1+bones[bw.boneIndex2].MultiplyVector(rest)*bw.weight2+bones[bw.boneIndex3].MultiplyVector(rest)*bw.weight3;
                            float error=Vector3.Angle(actual,expected);if(bw.weight0>.9999f)cr.maximumRigidSkinError=Mathf.Max(cr.maximumRigidSkinError,error);else mixed.Add(error);
                            cr.maxPositionChange=Mathf.Max(cr.maxPositionChange,(pp[i]-p[i]).magnitude);float rotation=Vector3.Angle(n[i],pn[i]);cr.maximumNormalRotation=Mathf.Max(cr.maximumNormalRotation,rotation);
                            if(rotation>35) {cr.largeRotationSamples++;animated+=Vector3.Angle(actual,pn[i]);stale+=Vector3.Angle(rest,pn[i]);compared++;}
                            if(float.IsNaN(actual.x)||Vector3.Dot(actual,pn[i])<0)throw new Exception("Invalid/reversed animated normal.");
                        }
                        if(r.gameObject.activeInHierarchy)foreach(var v in pp) {Vector3 world=r.transform.rotation*v+r.transform.position;if(!haveBounds){frameBounds=new Bounds(world,Vector3.zero);haveBounds=true;}else frameBounds.Encapsulate(world);}
                        UnityEngine.Object.DestroyImmediate(posed);
                    }
                    camera.aspect=720f/840f;
                    camera.transform.position=frameBounds.center+new Vector3(0,0,4);camera.transform.LookAt(frameBounds.center);camera.orthographicSize=Mathf.Max(frameBounds.extents.y,frameBounds.extents.x/camera.aspect)*1.15f;
                    foreach(int mode in new[]{0,1,2}) {if(mode==2 && frame%5!=0)continue;foreach(var r in selected)r.sharedMaterials=mode==2?renderers.First(x=>x.name==r.name).sharedMaterials:Enumerable.Repeat(debug,r.sharedMesh.subMeshCount).ToArray();debug.SetFloat("_Mode",mode);camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var read=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);read.Apply();File.WriteAllBytes(work+"/Frames/"+clip.name.Replace('@','_')+"_"+(mode==0?"Normal":mode==1?"Gray":"Material")+"_"+frame.ToString("D2")+".png",read.EncodeToPNG());UnityEngine.Object.DestroyImmediate(read);RenderTexture.active=old;}
                }
                graph.Destroy();mixed.Sort();cr.p95MixedSkinError=mixed.Count>0?mixed[Mathf.Min(mixed.Count-1,(int)(mixed.Count*.95f))]:0;cr.meanAnimatedDeviation=(float)(animated/Math.Max(compared,1));cr.meanStaleDeviation=(float)(stale/Math.Max(compared,1));
                if(cr.largeRotationSamples<100||cr.maximumRigidSkinError>1||cr.meanAnimatedDeviation>=cr.meanStaleDeviation*.5f)throw new Exception("Animation transport validation failed: "+JsonUtility.ToJson(cr));
                result.clips.Add(cr);File.WriteAllText(work+"/animation_validation.json",JsonUtility.ToJson(result,true));
            }
            Debug.Log("GWEN_REVIEWED_NORMAL_ANIMATION_VALIDATED");
        }
        finally {if(graph.IsValid())graph.Destroy();if(root)UnityEngine.Object.DestroyImmediate(root);if(camObject)UnityEngine.Object.DestroyImmediate(camObject);if(debug)UnityEngine.Object.DestroyImmediate(debug);if(rt){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(scene.IsValid())EditorSceneManager.CloseScene(scene,true);ShaderUtil.allowAsyncCompilation=async;importer.isReadable=previousReadable;importer.SaveAndReimport();}
    }
}
