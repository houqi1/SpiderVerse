using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LassylaPort.Editor
{
    [InitializeOnLoad]
    public static class LassylaSetup
    {
        public const string Work="Tools/LassylaUnity", Dir="Assets/LassylaPort";
        public const string ParentPath=Dir+"/Materials/Lassyla_OriginalNodes.mat";
        const string ScenePath="Assets/LineArt/LineArtPreview.unity", InstanceName="Spider-Gwen Lassyla";
        static bool busy;
        static LassylaSetup(){EditorApplication.update+=Poll;}
        static void Poll(){
            if(busy||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            string request=Work+"/command.request";if(!File.Exists(request))return;
            string command;
            try{command=File.ReadAllText(request).Trim();File.Delete(request);}
            catch(IOException){return;} // A producer may still be writing; retry next editor update.
            busy=true;
            try {
                if(command=="studio-match")LassylaStudioInstall.Install();
                else if(command=="body-check")LassylaBodyCheck.Capture();
                else if(command=="history-check")LassylaBodyCheck.VerifyLiveHistory();
                else if(command.StartsWith("scene-frame-"))LassylaBodyCheck.CaptureSceneTarget(command.Substring("scene-frame-".Length));
                else if(command=="front-focus")LassylaColourCheck.FocusTorso();
                else if(command=="oblique-focus")LassylaColourCheck.FocusOblique();
                else if(command=="hood-focus")LassylaColourCheck.FocusHood();
                else if(command=="forest-preview")LassylaForestPreview.Toggle();
                else if(command=="return-city")LassylaForestPreview.ReturnToCity();
                else if(command=="sync-shared-ior")LassylaBodyCheck.SyncSharedIOR();
                else if(command=="colour-check")LassylaColourCheck.Capture();
                else if(command=="detail-check")LassylaColourCheck.Capture(true);
                else if(command=="saved-view-check")LassylaColourCheck.Capture(savedView:true);
                else if(command=="live-check")LassylaColourCheck.Capture(liveOrbit:true);
                else if(command=="reference-focus")LassylaColourCheck.FocusReference();
                else if(command=="apply")Apply();
                else if(command=="validate")LassylaValidation.Run();
                else if(command=="refresh")AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                else if(command=="preview")Preview();
                else if(command=="focus")Focus();
                else if(command=="material")SelectMaterial();
                else if(command=="views")LassylaVisualCompare.Capture();
                else if(command=="signals")LassylaVisualCompare.CaptureSignals();
                else if(command=="edge-fix")LassylaStudioInstall.Install();
                else if(command=="rendered-views")LassylaVisualCompare.CaptureRendered();
                else if(command=="rest-pose")LassylaRestPose.Apply();
                else if(command=="close-views")LassylaVisualCompare.CaptureClose();
                else if(command=="skinned-signals")LassylaVisualCompare.CaptureSkinnedSignals();
                else if(command=="viewport-views")LassylaVisualCompare.CaptureViewport();
                else if(command=="occlusion-view")LassylaVisualCompare.CaptureOcclusion();
                else if(command=="quit"&&Application.isBatchMode)EditorApplication.Exit(0);
                else throw new Exception("Unknown Lassyla command: "+command);
                File.WriteAllText(Work+"/"+command+".completed",DateTime.UtcNow.ToString("O"));
            }catch(Exception e){File.WriteAllText(Work+"/"+command+".error",e.ToString());Debug.LogException(e);}
            finally{busy=false;}
        }
        public static void Start(){
            Directory.CreateDirectory(Work);
            EditorSceneManager.OpenScene(ScenePath);
            File.WriteAllText(Work+"/ready.txt",Application.unityVersion+" / "+SystemInfo.graphicsDeviceName);
        }
        [Serializable] public class Surface {public string name;public bool visible;public float[] location,size,matrix,localMin,localMax,probePositions,probeUVs;public int vertexCount;}
        [Serializable] public class Ramp {public float position;public float[] color;}
        [Serializable] public class Group {public string name;public float[] values;}
        [Serializable] public class Parameters {public Surface[] meshes;public float ior,noiseScale,noiseDetail,noiseRoughness,noiseLacunarity,noiseDistortion,normalMix,colorMix;public Ramp[] edgeRamp,noiseRamp,palette;public Group[] groups;}
        public static Parameters ReadParameters()=>JsonUtility.FromJson<Parameters>(File.ReadAllText(Work+"/parameters.json"));
        public static void InstallEdgeRendering(){
            var p=ReadParameters();var parent=AssetDatabase.LoadAssetAtPath<Material>(ParentPath);
            string backup=Work+"/material-before-visual-fix.json";
            if(!File.Exists(backup))File.WriteAllText(backup,EditorJsonUtility.ToJson(parent,true));
            Undo.RecordObject(parent,"Restore Blender reference edge values");
            string[] keys={"_IOR","_NoiseScale","_NoiseDetail","_NoiseRoughness","_NoiseLacunarity","_NoiseDistortion","_EdgeStart","_EdgeEnd","_NoiseStart","_NoiseEnd","_NoiseDark","_HashScale","_HashOffset","_DebugView"};
            float[] values={p.ior,p.noiseScale,p.noiseDetail,p.noiseRoughness,p.noiseLacunarity,p.noiseDistortion,p.edgeRamp[0].position,p.edgeRamp[1].position,p.noiseRamp[0].position,p.noiseRamp[1].position,p.noiseRamp[0].color[0],.1f,1f/7f,0};
            for(int i=0;i<keys.Length;i++)parent.SetFloat(keys[i],values[i]);EditorUtility.SetDirty(parent);
            var root=GameObject.Find(InstanceName);
            foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
                var mat=r.sharedMaterial;var basis=Matrix4x4.identity;for(int j=0;j<3;j++)basis.SetRow(j,mat.GetVector("_BlenderNormalToUnity"+j));
                var inverse=basis.inverse;for(int j=0;j<3;j++)mat.SetVector("_BlenderWorld"+j,inverse.GetRow(j));EditorUtility.SetDirty(mat);
            }
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/LineArt/LineArt_Renderer.asset");
            var feature=renderer.rendererFeatures.OfType<LassylaEdgeFeature>().FirstOrDefault();
            if(!feature){feature=ScriptableObject.CreateInstance<LassylaEdgeFeature>();feature.name="Lassyla EEVEE Transparency";AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);}
            feature.samples=16;feature.filterRadius=1.5f;feature.blenderExposure=0;
            feature.compositeShader=Shader.Find("Hidden/Lassyla/Accumulate");feature.SetActive(true);feature.Create();renderer.SetDirty();
            var serializedRenderer=new SerializedObject(renderer);var featureMap=serializedRenderer.FindProperty("m_RendererFeatureMap");
            featureMap.arraySize=renderer.rendererFeatures.Count;
            for(int i=0;i<renderer.rendererFeatures.Count;i++){
                if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i],out string guid,out long id))featureMap.GetArrayElementAtIndex(i).longValue=id;
            }
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);EditorUtility.SetDirty(renderer);AssetDatabase.SaveAssets();SceneView.RepaintAll();
        }
        static Vector3 V(float[] x)=>new Vector3(x[0],x[1],x[2]);
        static Matrix4x4 Matrix(float[] x){var m=new Matrix4x4();for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=x[r*4+c];return m;}
        static string Safe(string s)=>s.Replace(' ','_');
        static void RestoreImportedPose(GameObject root){
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Model/Spider-Gwen.fbx");
            foreach(var t in original.GetComponentsInChildren<Transform>(true)){
                string path=AnimationUtility.CalculateTransformPath(t,original.transform);
                var target=path.Length==0?root.transform:root.transform.Find(path);
                if(!target)throw new Exception("Missing imported bone: "+path);
                if(path.Length!=0)target.localPosition=t.localPosition;
                target.localRotation=t.localRotation;target.localScale=t.localScale;
            }
        }
        static void SaveAsset(UnityEngine.Object value,string path){
            var existing=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if(existing){EditorUtility.CopySerialized(value,existing);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);}
            else AssetDatabase.CreateAsset(value,path);
        }
        // Verify all axis/sign/unit candidates against the source positions; never infer Generated
        // from the Unity skinned bounds (those are often expanded by animation).
        static Matrix4x4 CoordinateMap(Mesh mesh,Surface s,out float error){
            var vertices=mesh.vertices;var uv=mesh.uv;var sourceMin=V(s.localMin);var sourceMax=V(s.localMax);
            Vector3 umin=vertices[0],umax=vertices[0];foreach(var v in vertices){umin=Vector3.Min(umin,v);umax=Vector3.Max(umax,v);}
            int[][] perms={new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}};
            error=float.PositiveInfinity;Matrix4x4 best=Matrix4x4.identity;
            foreach(var perm in perms)for(int signs=0;signs<8;signs++)foreach(float scale in new[]{1f,.01f,100f}){
                var m=Matrix4x4.zero;m[3,3]=1;for(int a=0;a<3;a++)m[a,perm[a]]=((signs&(1<<a))!=0?-1:1)*scale;
                var a0=m.MultiplyPoint3x4(umin);var a1=m.MultiplyPoint3x4(umax);
                float boundsError=(Vector3.Min(a0,a1)-sourceMin).magnitude+(Vector3.Max(a0,a1)-sourceMax).magnitude;
                if(boundsError>0.0001f)continue;
                float e=0;
                for(int k=0;k<s.probePositions.Length;k+=3){
                    var p=new Vector3(s.probePositions[k],s.probePositions[k+1],s.probePositions[k+2]);float nearest=float.PositiveInfinity;
                    var sourceUV=new Vector2(s.probeUVs[k/3*2],s.probeUVs[k/3*2+1]);
                    for(int z=0;z<vertices.Length;z++)if((uv[z]-sourceUV).sqrMagnitude<1e-10f)nearest=Mathf.Min(nearest,(m.MultiplyPoint3x4(vertices[z])-p).sqrMagnitude);
                    e=Mathf.Max(e,Mathf.Sqrt(nearest));
                }
                // Several Gwen surfaces mirror both geometry AND UVs. The same FBX's
                // asymmetric head establishes X reflection / 100x units. Prefer that
                // verified mapping over sub-ULP differences between symmetric candidates.
                if(e<0.00001f&&perm[0]==0&&perm[1]==1&&perm[2]==2&&signs==1&&scale==100f){error=e;return m;}
                if(e<error){error=e;best=m;}
            }
            if(error>0.00001f)throw new Exception("Blender/Unity coordinate mapping failed: "+s.name+" error="+error);
            return best;
        }
        [MenuItem("Tools/Lassyla/Create New Spider Gwen")]
        public static void Apply(){
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Run in Edit mode.");
            var parameters=ReadParameters();
            // The project's full-screen motion-vector diagnostic replaces material colors.
            // Preserve its previous asset and disable only that diagnostic for material review.
            const string rendererPath="Assets/LineArt/LineArt_Renderer.asset";
            var rendererData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            var diagnostic=rendererData.rendererFeatures.FirstOrDefault(f=>f&&f.name=="Motion Vector Debug");
            if(diagnostic&&diagnostic.isActive){
                if(!File.Exists(Work+"/LineArt_Renderer.before.asset"))File.Copy(rendererPath,Work+"/LineArt_Renderer.before.asset");
                Undo.RecordObject(diagnostic,"Show Lassyla material instead of motion-vector diagnostic");
                diagnostic.SetActive(false);EditorUtility.SetDirty(diagnostic);EditorUtility.SetDirty(rendererData);
            }
            foreach(var folder in new[]{Dir+"/Materials",Dir+"/Meshes"})Directory.CreateDirectory(folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader=Shader.Find("Lassyla/Blender Material Nodes");if(!shader)throw new Exception("Lassyla shader missing");
            var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity==ShaderCompilerMessageSeverity.Error).ToArray();
            if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message+" line "+e.line)));
            var palette=new Texture2D(257,1,TextureFormat.RGBAHalf,false,true){name="Blender ColorRamp.005",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            byte[] bytes=File.ReadAllBytes(Work+"/palette.f32");var values=new float[1028];Buffer.BlockCopy(bytes,0,values,0,bytes.Length);
            var pixels=new Color[257];for(int i=0;i<257;i++)pixels[i]=new Color(values[i*4],values[i*4+1],values[i*4+2],values[i*4+3]);
            palette.SetPixels(pixels);palette.Apply();SaveAsset(palette,Dir+"/Materials/BlenderPalette.asset");
            var parent=AssetDatabase.LoadAssetAtPath<Material>(ParentPath);
            if(!parent){parent=new Material(shader){name="Lassyla_OriginalNodes"};AssetDatabase.CreateAsset(parent,ParentPath);}
            parent.shader=shader;
            string[] keys={"_IOR","_NoiseScale","_NoiseDetail","_NoiseRoughness","_NoiseLacunarity","_NoiseDistortion","_NormalMix","_ColorMix"};
            float[] vals={parameters.ior,parameters.noiseScale,parameters.noiseDetail,parameters.noiseRoughness,parameters.noiseLacunarity,parameters.noiseDistortion,parameters.normalMix,parameters.colorMix};
            for(int i=0;i<keys.Length;i++)parent.SetFloat(keys[i],vals[i]);
            parent.SetFloat("_EdgeStart",parameters.edgeRamp[0].position);parent.SetFloat("_EdgeEnd",parameters.edgeRamp[1].position);
            parent.SetFloat("_NoiseStart",parameters.noiseRamp[0].position);parent.SetFloat("_NoiseEnd",parameters.noiseRamp[1].position);parent.SetFloat("_NoiseDark",parameters.noiseRamp[0].color[0]);
            parent.SetFloat("_DebugView",0);parent.SetFloat("_HashScale",.1f);parent.SetFloat("_HashOffset",1f/7f);
            foreach(var g in parameters.groups)parent.SetVector(g.name=="Group"?"_ColorGroup":"_NormalGroup",new Vector4(g.values[0],g.values[1],g.values[2],g.values[3]));
            parent.SetTexture("_Palette",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir+"/Materials/BlenderPalette.asset"));EditorUtility.SetDirty(parent);
            var existing=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(x=>x.name==InstanceName);
            GameObject root=existing;
            if(!root){
                root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Model/Spider-Gwen.fbx"));
                PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                root.name=InstanceName;Undo.RegisterCreatedObjectUndo(root,"Create Lassyla Spider Gwen");
            }
            // Keep the imported skeleton, bind poses, bone weights and animation avatar.
            if(!root.GetComponent<LassylaTemporalSampling>())root.AddComponent<LassylaTemporalSampling>();
            foreach(var animator in root.GetComponentsInChildren<Animator>(true)){animator.runtimeAnimatorController=null;animator.enabled=false;}
            RestoreImportedPose(root);
            var report=new List<string>();
            foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
                var s=parameters.meshes.FirstOrDefault(x=>x.name==renderer.name);if(s==null)throw new Exception("Unknown Gwen mesh: "+renderer.name);
                renderer.gameObject.SetActive(s.visible);
                // Always copy the pristine source mesh when rerunning setup.
                var originalRenderer=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Model/Spider-Gwen.fbx").GetComponentsInChildren<SkinnedMeshRenderer>(true).First(x=>x.name==renderer.name);
                var original=originalRenderer.sharedMesh;renderer.localBounds=originalRenderer.localBounds;
                var map=CoordinateMap(original,s,out float error);
                var copy=UnityEngine.Object.Instantiate(original);copy.name=renderer.name+"_LassylaGenerated";
                var generated=new List<Vector3>(copy.vertexCount);var loc=V(s.location);var size=V(s.size);
                foreach(var p in copy.vertices){var b=map.MultiplyPoint3x4(p)-loc;generated.Add(new Vector3(size.x!=0?b.x/(2*size.x)+.5f:.5f,size.y!=0?b.y/(2*size.y)+.5f:.5f,size.z!=0?b.z/(2*size.z)+.5f:.5f));}
                copy.SetUVs(2,generated);
                string meshPath=Dir+"/Meshes/"+Safe(renderer.name)+".asset";SaveAsset(copy,meshPath);renderer.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                string materialPath=Dir+"/Materials/"+Safe(renderer.name)+".mat";
                var child=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(!child){child=new Material(shader){name=renderer.name};AssetDatabase.CreateAsset(child,materialPath);}
                child.parent=parent;child.RevertAllPropertyOverrides();
                for(int j=0;j<3;j++)child.SetVector("_BlenderLocal"+j,map.GetRow(j));
                // Convert the source graph's world-space vector into the model's Unity world basis.
                Matrix4x4 unityToBlender=Matrix(s.matrix)*map*renderer.transform.worldToLocalMatrix;
                Matrix4x4 blenderToUnity=unityToBlender.inverse;
                for(int j=0;j<3;j++)child.SetVector("_BlenderNormalToUnity"+j,blenderToUnity.GetRow(j));
                renderer.sharedMaterials=Enumerable.Repeat(child,renderer.sharedMesh.subMeshCount).ToArray();
                renderer.updateWhenOffscreen=true;EditorUtility.SetDirty(child);
                report.Add(renderer.name+" | visible="+s.visible+" | vertices="+renderer.sharedMesh.vertexCount+" | mapping error="+error.ToString("R")+" | "+map);
            }
            if(!existing){
                var other=SceneManager.GetActiveScene().GetRootGameObjects().Where(x=>x!=root&&x.name.IndexOf("Spider",StringComparison.OrdinalIgnoreCase)>=0).ToArray();
                float right=other.Length>0?other.Max(x=>x.transform.position.x):0;
                root.transform.position+=new Vector3(right+1.5f,0,0);
            }
            string prefabPath=Dir+"/Spider-Gwen_Lassyla.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,prefabPath,InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
            if(!File.Exists(Work+"/LineArtPreview.before.unity"))File.Copy(ScenePath,Work+"/LineArtPreview.before.unity");
            EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
            File.WriteAllText(Work+"/installation.txt","Parent: "+ParentPath+"\nPrefab: "+prefabPath+"\n"+string.Join("\n",report));
            LassylaRestPose.Apply();LassylaStudioInstall.Install();
            Focus();Debug.Log("LASSYLA_INSTALL_OK");
        }
        [MenuItem("Tools/Lassyla/Select Shared Material")]
        public static void SelectMaterial(){Selection.activeObject=AssetDatabase.LoadAssetAtPath<Material>(ParentPath);EditorGUIUtility.PingObject(Selection.activeObject);}
        [MenuItem("Tools/Lassyla/Frame New Spider Gwen")]
        public static void Focus(){
            var root=GameObject.Find(InstanceName);if(!root)return;Selection.activeGameObject=root;
            var renderers=root.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return;
            var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            var view=SceneView.lastActiveSceneView;
            if(view){
                view.LookAt(b.center,Quaternion.Euler(7,175,0),b.size.magnitude*.75f,false,true);
                view.Repaint();
            }
        }
        public static void Preview(){LassylaValidation.RenderPreview();}
    }
}
