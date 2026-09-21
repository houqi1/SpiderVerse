#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SpiderVerse.LineArt.Editor
{
    public static class LineArtSetup
    {
        const string Folder="Assets/LineArt";
        const string Preview=Folder+"/LineArtPreview.unity";
        const string RendererPath=Folder+"/LineArt_Renderer.asset";
        [MenuItem("SpiderVerse/Line Art/Create or Update Preview Scene")]
        public static void CreatePreview()
        {
            var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if(!renderer) {
                AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset",RendererPath);
                renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
                // Only the cloned renderer is changed. Original renderer and existing scene retain their effects.
                foreach(var f in renderer.rendererFeatures)if(f&&(f.GetType().Name=="CharacterOutlineFeature"||f.GetType().Name=="OuterGlowFeature"))f.SetActive(false);
            }
            var feature=renderer.rendererFeatures.OfType<ObjectLineArtFeature>().FirstOrDefault();
            if(!feature) {
                feature=ScriptableObject.CreateInstance<ObjectLineArtFeature>();feature.name="Object Line Art";
                AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);
            }
            feature.strokeShader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/Shaders/ObjectLineArt.shader");feature.SetActive(true);
            feature.geometryCompute=AssetDatabase.LoadAssetAtPath<ComputeShader>(Folder+"/Shaders/ObjectLineArt.compute");
            feature.gpuStrokeShader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/Shaders/ObjectLineArtGpu.shader");
            // Serialized reference retains the shader in builds, and TGA is a regular Unity Texture2D asset.
            EditorUtility.SetDirty(feature);EditorUtility.SetDirty(renderer);
            int index=-1;
            foreach(string path in new[]{"Assets/Settings/PC_RPAsset.asset","Assets/Settings/Mobile_RPAsset.asset"}) {
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);if(!pipeline)continue;
                var so=new SerializedObject(pipeline);var list=so.FindProperty("m_RendererDataList");int slot=-1;
                for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==renderer)slot=i;
                if(slot<0){slot=list.arraySize;list.arraySize++;list.GetArrayElementAtIndex(slot).objectReferenceValue=renderer;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);}
                if(index<0)index=slot;else if(index!=slot)throw new InvalidOperationException("PC and Mobile renderer slots differ; assign the Line Art renderer on the preview camera manually.");
            }
            AssetDatabase.SaveAssets();
            if(!File.Exists(Preview))AssetDatabase.CopyAsset("Assets/Scenes/SampleScene.unity",Preview);
            var scene=SceneManager.GetSceneByPath(Preview);
            bool wasLoaded=scene.IsValid()&&scene.isLoaded;
            if(!wasLoaded)scene=EditorSceneManager.OpenScene(Preview,OpenSceneMode.Additive);
            try {
                var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).Where(r=>r.gameObject.layer==6&&(r is MeshRenderer||r is SkinnedMeshRenderer)).ToArray();
                if(renderers.Length==0)throw new InvalidOperationException("No Character-layer mesh renderers in the copied scene.");
                foreach(var r in renderers) {
                    Mesh mesh=r is SkinnedMeshRenderer sk?sk.sharedMesh:r.GetComponent<MeshFilter>()?.sharedMesh;
                    if(!mesh)continue;
                    var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter;
                    if(importer&&!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
                }
                var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Object Line Art");
                if(!root){root=new GameObject("Object Line Art");SceneManager.MoveGameObjectToScene(root,scene);}
                var source=root.GetComponent<ObjectLineArtSource>()??root.AddComponent<ObjectLineArtSource>();source.renderers=renderers;
                foreach(var camera in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)))camera.GetUniversalAdditionalCameraData().SetRenderer(index);
                EditorSceneManager.SaveScene(scene);
            } finally {if(!wasLoaded)EditorSceneManager.CloseScene(scene,true);}
            AssetDatabase.SaveAssets();Selection.activeObject=feature;
            Debug.Log("Object Line Art preview ready: "+Preview+". Original scene/renderer remain intact. Renderer slot: "+index);
        }
        [MenuItem("SpiderVerse/Line Art/Open Preview Scene")]
        public static void OpenPreview() {if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(Preview);}
        [MenuItem("SpiderVerse/Line Art/Select Stroke Settings")]
        public static void SelectSettings()
        {
            var r=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if(r)Selection.activeObject=r.rendererFeatures.OfType<ObjectLineArtFeature>().FirstOrDefault();
        }
        [MenuItem("SpiderVerse/Line Art/Run Geometry Checks")]
        public static void Tests()
        {
            void Require(bool b,string name){if(!b)throw new Exception("Line Art check failed: "+name);}
            void Near(float a,float b,string name)=>Require(Mathf.Abs(a-b)<1e-4f,name);
            Near(LineArtGeometry.LengthScale(0,false,0,.4f),.6f,"random minimum");
            Near(LineArtGeometry.LengthScale(uint.MaxValue,false,0,.4f),1.4f,"random maximum");
            Near(LineArtGeometry.LengthScale(0,false,.2f,0),.8f,"fixed trim");
            var view=new LineArtGeometry.View{matrix=Matrix4x4.identity,width=1024,height=1024,position=new Vector3(0,0,4),toCamera=Vector3.forward};
            var c=new LineArtGeometry.Chain{seed=uint.MaxValue};c.points.AddRange(new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0)});
            var g=LineArtGeometry.MakeGeometry(new List<LineArtGeometry.Chain>{c},view,new LineArtSettings(),CancellationToken.None);
            Near(g.positions[0].x,-.4f,"extension start tangent");Near(g.positions[g.positions.Length-1].y,1.4f,"extension end tangent");
            Near(g.stroke[0].y,0,"taper start");Near(g.stroke[g.stroke.Length-1].y,1,"taper end");
            Require(g.positions.All(p=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)),"finite geometry");
            Require(g.indices.All(i=>i>=0&&i<g.positions.Length),"indices");
            Require(LineArtGeometry.Hidden(new Vector3(-1,0,.5f),new Vector3(1,0,.5f),new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(0,.5f,0),.00001f,out var interval),"occlusion interval");
            Near(interval.x,.375f,"occlusion cut start");Near(interval.y,.625f,"occlusion cut end");
            Require(!LineArtGeometry.Hidden(new Vector3(-1,0,-.5f),new Vector3(1,0,-.5f),new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(0,.5f,0),.00001f,out _),"front edge visible");
            Require(LineArtGeometry.Clip(new Vector4(-2,0,0,1),new Vector4(0,0,0,1),out float lo,out float hi),"frustum clip");Near(lo,.5f,"frustum endpoint");
            var top=LineArtGeometry.Prepare(new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.zero,Vector3.up,Vector3.left},new[]{new[]{0,1,2,3,4,5}});
            Require(top.representatives.Length==4&&top.edges.Length==5,"weld duplicated mesh corners");
            var snap=new LineArtGeometry.Snapshot{id=1,draw=true,topology=top,vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.left},cull=new[]{0},opaque=new[]{true}};
            var result=LineArtGeometry.Build(new[]{snap},view,new LineArtSettings{intersections=false,lengthRandomness=0},CancellationToken.None);
            Require(result.candidates==4&&result.chains==1,"coplanar shared edge excluded, boundary chained");
            c.closed=true;c.points.Clear();c.points.AddRange(new[]{Vector3.zero,Vector3.right,Vector3.one,Vector3.up});
            var closed=LineArtGeometry.MakeGeometry(new List<LineArtGeometry.Chain>{c},view,new LineArtSettings{lengthRandomness=0},CancellationToken.None);
            Require(closed.positions[0]==closed.positions[closed.positions.Length-1],"closed seam position");Near(closed.stroke[closed.stroke.Length-1].y,1,"closed seam texture UV");
            Debug.Log("LINE_ART_GEOMETRY_CHECKS_PASSED");
        }

        // Batch validation entry point; never runs automatically in the user's editor.
        public static void BuildAndVerify()
        {
            try {
                CreatePreview();Tests();EditorSceneManager.OpenScene(Preview);
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/Shaders/ObjectLineArt.shader");
                foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(message.message);
                camera=Camera.main;if(!camera)throw new Exception("No main camera in preview.");
                renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
                feature=renderer.rendererFeatures.OfType<ObjectLineArtFeature>().First();
                texture=new RenderTexture(960,960,24,RenderTextureFormat.ARGB32);texture.Create();
                camera.aspect=1;started=EditorApplication.timeSinceStartup;frames=0;stage=0;
                EditorApplication.update+=RenderCheck;
            } catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static Camera camera;static RenderTexture texture;static ScriptableRendererData renderer;static ObjectLineArtFeature feature;
        static double started;static int frames,stage;
        static void RenderCheck()
        {
            try {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});frames++;
                if(frames<8||string.IsNullOrEmpty(feature.LastStats)) {
                    if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Timed out waiting for line geometry.");return;
                }
                var errors=ShaderUtil.GetShaderMessages(feature.strokeShader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
                if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
                var old=RenderTexture.active;RenderTexture.active=texture;
                var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();
                Directory.CreateDirectory("Validation");File.WriteAllBytes("Validation/line-art-"+stage+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=old;
                File.AppendAllText("Validation/report.txt",$"Stage {stage}: {feature.LastStats}\n");
                if(stage==0){stage=1;frames=0;feature.settings.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texture/clip.tga");feature.settings.thickness=6;feature.settings.textureRepeats=3;feature.settings.offset=new Vector2(8,6);return;}
                feature.settings.thickness=2;feature.settings.offset=Vector2.zero;feature.settings.textureRepeats=1;
                // Keep the user's existing TGA assigned in the migrated preview.
                EditorUtility.SetDirty(feature);AssetDatabase.SaveAssets();
                Debug.Log("LINE_ART_RENDER_CHECKS_PASSED "+feature.LastStats);EditorApplication.update-=RenderCheck;
                texture.Release();UnityEngine.Object.DestroyImmediate(texture);EditorApplication.Exit(0);
            } catch(Exception e){Debug.LogException(e);EditorApplication.update-=RenderCheck;EditorApplication.Exit(1);}
        }
    }
    [CustomEditor(typeof(ObjectLineArtFeature))]
    public sealed class ObjectLineArtFeatureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();DrawDefaultInspector();
            if(EditorGUI.EndChangeCheck()) {
                EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            EditorGUILayout.HelpBox("Edit mode supported. Scene View uses the URP Asset's DEFAULT Renderer: choose LineArt_Renderer there and enable Show In Scene View. Game View uses its camera's Renderer selection.",MessageType.Info);
            var feature=(ObjectLineArtFeature)target;
            if(!string.IsNullOrEmpty(feature.LastStats))EditorGUILayout.LabelField(feature.LastStats,EditorStyles.wordWrappedLabel);
        }
    }
    [CustomEditor(typeof(ObjectLineArtSource))]
    public sealed class ObjectLineArtSourceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI(){DrawDefaultInspector();EditorGUILayout.HelpBox("Stroke controls are on the Object Line Art renderer feature. Texture2D accepts imported TGA. Mesh Read/Write must be enabled for runtime geometry extraction.",MessageType.Info);if(GUILayout.Button("Select Stroke Settings"))LineArtSetup.SelectSettings();}
    }
}
#endif
