#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace SpiderVerse.LineArt.Editor
{
    // Runs in an isolated batch editor; deliberately never opens/replaces the user's scene.
    public static class LineArtRefreshChecks
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static object Field(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
        static void Require(bool ok,string label){if(!ok)throw new Exception("Line Art refresh check failed: "+label);}
        public static void BatchRun()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Run this regression check in an isolated batch editor.");
            ObjectLineArtFeature feature=null;
            try {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.AddComponent<ObjectLineArtSource>();
                var camera=new GameObject("Check Camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-4);
                camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.aspect=1;
                feature=ScriptableObject.CreateInstance<ObjectLineArtFeature>();
                feature.strokeShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/LineArt/Shaders/ObjectLineArt.shader");
                feature.settings.intersections=false;feature.Create();
                object pass=Field(feature,"pass");var update=pass.GetType().GetMethod("Update",Flags);
                object Update()=>update.Invoke(pass,new object[]{camera,512,512});
                void Finish(object s){var task=(Task<LineArtGeometry.Result>)Field(s,"pending");if(task!=null)task.GetAwaiter().GetResult();}
                object state=Update();Finish(state);state=Update();
                Require((bool)Field(state,"ready"),"baseline produces visible strokes");
                var original=(Mesh)Field(state,"mesh");int indices=(int)original.GetIndexCount(0);
                for(int i=0;i<10;i++)feature.Create();
                Require(ReferenceEquals(pass,Field(feature,"pass")),"Inspector validation reuses render pass");
                feature.settings.thickness=7;feature.settings.offset=new Vector2(9,-4);feature.settings.textureRepeats=5;
                feature.Create();state=Update();
                Require(ReferenceEquals(original,Field(state,"mesh"))&&(bool)Field(state,"ready"),"uniform changes retain mesh");
                Require(Field(state,"pending")==null,"uniform changes do not schedule geometry");
                feature.settings.lengthTrim=.2f;feature.Create();state=Update();
                Require((bool)Field(state,"ready")&&ReferenceEquals(original,Field(state,"mesh")),"old drawing retained during rebuild");
                Require(original.GetIndexCount(0)==indices,"front buffer remains intact");
                feature.settings.lengthTrim=.4f;state=Update();
                Require((bool)Field(state,"ready")&&ReferenceEquals(original,Field(state,"mesh")),"rapid edits retain old drawing");
                Finish(state);state=Update();
                Require((bool)Field(state,"ready")&&!ReferenceEquals(original,Field(state,"mesh")),"completed result swaps buffers");
                feature.settings.contour=false;feature.settings.crease=false;feature.settings.materialBorders=false;feature.settings.boundaries=false;
                state=Update();Require((bool)Field(state,"ready"),"empty rebuild also retains old result until completion");
                Finish(state);state=Update();Require(!(bool)Field(state,"ready"),"legitimately empty result clears drawing");
                System.IO.Directory.CreateDirectory("Validation");System.IO.File.WriteAllText("Validation/refresh-checks.txt","PASS: repeated Create, uniform-only updates, pending rebuild, rapid edits, atomic buffer swap, empty results.\n");
                Debug.Log("LINE_ART_REFRESH_CHECKS_PASSED");feature.Dispose();UnityEngine.Object.DestroyImmediate(feature);EditorApplication.Exit(0);
            }catch(Exception e){Debug.LogException(e);if(feature){feature.Dispose();UnityEngine.Object.DestroyImmediate(feature);}EditorApplication.Exit(1);}
        }
    }
}
#endif
