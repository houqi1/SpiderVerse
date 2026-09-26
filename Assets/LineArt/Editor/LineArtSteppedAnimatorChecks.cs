#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpiderVerse.LineArt.Editor
{
    public static class LineArtSteppedAnimatorChecks
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly MethodInfo Step = typeof(LineArtSteppedAnimator).GetMethod("Step", Flags);
        const string ControllerPath = "Assets/SteppedAnimatorCheck.controller";
        const string ClipPath = "Assets/SteppedAnimatorCheck.anim";

        public static void BatchRun()
        {
            if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/Library/LineArtValidation/"))
                throw new InvalidOperationException("Run only in the isolated Library/LineArtValidation batch project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var clip = new AnimationClip();
            clip.SetCurve("Bone", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 10, 10));
            AssetDatabase.CreateAsset(clip, ClipPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddMotion(clip);
            AssetDatabase.SaveAssets();
            // Avoid losing this callback to a domain reload when entering Play mode.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception("Stepped Animator check: " + message);
        }

        static void Near(float actual, float expected, string message)
            => Require(Mathf.Abs(actual - expected) < .003f, message + $" (actual {actual}, expected {expected})");

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            ObjectLineArtFeature feature = null;
            try
            {
                var root = new GameObject("Stepped Animator Check");
                var bone = new GameObject("Bone").transform;
                bone.SetParent(root.transform, false);
                var animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.Update(0);
                var component = root.AddComponent<LineArtSteppedAnimator>();
                var camera = new GameObject("Reference Camera").AddComponent<Camera>();
                void Tick(int generation, float delta) => Step.Invoke(component, new object[] { camera, generation, delta });

                Tick(1, 0);
                Require(!animator.enabled, "automatic evaluation is disabled");
                Tick(1, .04f);
                Tick(1, .04f);
                Near(bone.localPosition.x, 0, "pose is held between samples");
                Tick(2, .02f);
                Near(bone.localPosition.x, .1f, "elapsed time is accumulated across held frames");
                Tick(3, .35f);
                Near(bone.localPosition.x, .45f, "slow frames preserve animation speed");
                animator.speed = 2;
                Tick(4, .1f);
                Near(bone.localPosition.x, .65f, "Animator.speed is retained");
                Tick(5, 0);
                Near(bone.localPosition.x, .65f, "zero animation delta holds pose");
                component.enabled = false;
                Require(animator.enabled && !animator.keepAnimatorStateOnDisable &&
                    animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms,
                    "disabling component restores Animator settings");
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Update(.1f);
                Near(bone.localPosition.x, .85f, "normal evaluation resumes without restarting state");
                component.enabled = true;
                Tick(6, 0);
                Tick(7, .1f);
                Near(bone.localPosition.x, 1.05f, "re-enabling resumes stepping");
                component.enabled = false;
                animator.enabled = false;
                Tick(8, .1f);
                Require(!animator.enabled, "an already disabled Animator is left disabled");

                feature = ScriptableObject.CreateInstance<ObjectLineArtFeature>();
                feature.SyncLineArtCamera(camera);
                int generationBefore = feature.LineArtCameraGeneration(camera);
                var holds = (System.Collections.IDictionary)typeof(ObjectLineArtFeature).GetField("cameraHolds", Flags).GetValue(feature);
                object hold = holds[camera.GetInstanceID()];
                hold.GetType().GetField("nextUpdate").SetValue(hold, -1d);
                feature.SyncLineArtCamera(camera);
                Require(feature.LineArtCameraGeneration(camera) == generationBefore,
                    "animation and rendering share one generation even after the interval expires within a frame");

                Debug.Log("LINE_ART_STEPPED_ANIMATOR_CHECKS_PASSED");
                System.IO.Directory.CreateDirectory("Validation");
                System.IO.File.WriteAllText("Validation/stepped-animator-checks.txt",
                    "PASS: pose hold, accumulated time, slow frames, Animator.speed, pause, settings restoration, state continuity, re-enable, disabled Animator, shared per-frame generation.\n");
                feature.Dispose();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (feature) feature.Dispose();
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
