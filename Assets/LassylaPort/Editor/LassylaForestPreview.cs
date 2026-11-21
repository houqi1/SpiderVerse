using UnityEditor;
using UnityEngine;

namespace LassylaPort.Editor
{
    [InitializeOnLoad]
    public static class LassylaForestPreview
    {
        const string MenuPath = "Tools/Lassyla/Blender Material Preview";
        static SceneView view;
        static bool previousEffects, previousGizmos, ownsIsolation;
        static Vector3 cityPivot;
        static Quaternion cityRotation;
        static float citySize, cityFieldOfView;
        static bool cityOrthographic;
        public static bool Active => view;

        static LassylaForestPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Close;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingEditMode) Close();
            };
            EditorApplication.quitting += Close;
        }

        [MenuItem(MenuPath)]
        public static void Toggle()
        {
            if (Active) { Close(); return; }
            var target = SceneView.lastActiveSceneView;
            var hero = GameObject.Find("Spider-Gwen Lassyla");
            var panorama = AssetDatabase.LoadAssetAtPath<Texture2D>(LassylaSetup.Dir + "/Editor/ForestMaterialPreview.png");
            if (!target || !hero || !panorama) return;

            var material = hero.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            var basis = Matrix4x4.identity;
            for (int i = 0; i < 3; ++i) basis.SetRow(i, material.GetVector("_BlenderNormalToUnity" + i));
            view = target;
            cityPivot = view.pivot;
            cityRotation = view.rotation;
            citySize = view.size;
            cityOrthographic = view.orthographic;
            cityFieldOfView = view.cameraSettings.fieldOfView;
            previousEffects = view.sceneViewState.showImageEffects;
            previousGizmos = view.drawGizmos;
            var visibility = SceneVisibilityManager.instance;
            ownsIsolation = !visibility.IsCurrentStageIsolated();
            if (ownsIsolation) visibility.Isolate(hero, true);
            view.sceneViewState.showImageEffects = false;
            view.drawGizmos = false;
            LassylaEdgeFeature.forestPreviewCamera = view.camera;
            LassylaEdgeFeature.forestPreviewPanorama = panorama;
            LassylaEdgeFeature.forestWorldToBlender = basis.inverse;
            EditorApplication.update += CheckView;
            view.Focus();
            view.Repaint();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static void CheckView()
        {
            if (!view || !SceneVisibilityManager.instance.IsCurrentStageIsolated()) Close();
        }

        [MenuItem("Tools/Lassyla/Return to City View")]
        public static void ReturnToCity()
        {
            var target = view ? view : SceneView.lastActiveSceneView;
            Close();
            // The Scene window can also be in Unity's own isolation mode.
            var visibility = SceneVisibilityManager.instance;
            if (visibility.IsCurrentStageIsolated()) visibility.ExitIsolation();
            if (target) target.Focus();
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        static bool CheckMenu()
        {
            Menu.SetChecked(MenuPath, Active);
            return Active || (!EditorApplication.isPlaying && SceneView.lastActiveSceneView && GameObject.Find("Spider-Gwen Lassyla"));
        }

        static void Close()
        {
            EditorApplication.update -= CheckView;
            LassylaEdgeFeature.forestPreviewCamera = null;
            LassylaEdgeFeature.forestPreviewPanorama = null;
            if (ownsIsolation && SceneVisibilityManager.instance.IsCurrentStageIsolated())
                SceneVisibilityManager.instance.ExitIsolation();
            ownsIsolation = false;
            if (!view) return;
            view.sceneViewState.showImageEffects = previousEffects;
            view.drawGizmos = previousGizmos;
            view.cameraSettings.fieldOfView = cityFieldOfView;
            view.LookAt(cityPivot, cityRotation, citySize, cityOrthographic, true);
            view.Repaint();
            view = null;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
