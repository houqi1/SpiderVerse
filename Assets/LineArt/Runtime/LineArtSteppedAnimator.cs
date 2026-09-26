using System.Collections.Generic;
using UnityEngine;

namespace SpiderVerse.LineArt
{
    /// <summary>Holds Animator poses between the reference camera's Line Art updates.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("Animation/Line Art Stepped Animator")]
    public sealed class LineArtSteppedAnimator : MonoBehaviour
    {
        // Renderers observe completed pose changes; reading this must never
        // advance an independent camera clock or evaluate the Animator again.
        public struct MotionSample
        {
            public int sourceId;
            public int generation;
            public int frame;
        }

        static readonly List<LineArtSteppedAnimator> active = new List<LineArtSteppedAnimator>();
#if UNITY_EDITOR
        static int lastSceneRepaintFrame = -1;
#endif
        [Tooltip("Camera using the Object Line Art Renderer Feature. Empty uses Main Camera. Its Update Rate controls animation sampling.")]
        public Camera referenceCamera;

        Animator animator;
        Camera sampledCamera;
        bool ownsAnimator;
        bool previousKeepState;
        bool previousWriteDefaults;
        AnimatorCullingMode previousCulling;
        int lastGeneration;
        float pendingTime;
        bool warned;
        int motionGeneration;
        int motionFrame = -1;

        void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        public static bool TryGetMotionSample(Camera camera, out MotionSample sample)
        {
            sample = default;
            if (!Application.isPlaying || !camera) return false;

            bool sceneView = camera.cameraType == CameraType.SceneView;
            Camera preferredCamera = sceneView ? Camera.main : camera;
            LineArtSteppedAnimator selected = null;
            foreach (var candidate in active)
            {
                if (!candidate || !candidate.isActiveAndEnabled || !candidate.ownsAnimator ||
                    !candidate.sampledCamera || candidate.motionFrame < 0) continue;
                if (candidate.sampledCamera == preferredCamera)
                {
                    selected = candidate;
                    break;
                }
                // A Scene camera cannot drive an Animator itself. Prefer the
                // main camera's actor, or the first active actor when absent.
                if (sceneView && selected == null) selected = candidate;
            }
            if (!selected) return false;
            sample = new MotionSample
            {
                sourceId = selected.GetInstanceID(),
                generation = selected.motionGeneration,
                frame = selected.motionFrame
            };
            return true;
        }

        void RecordMotionSample()
        {
            motionGeneration++;
            motionFrame = Time.frameCount;
#if UNITY_EDITOR
            // Request preview rendering when the pose actually changes, rather
            // than waiting for mouse movement to repaint the Scene view.
            if (lastSceneRepaintFrame != motionFrame)
            {
                lastSceneRepaintFrame = motionFrame;
                UnityEditor.SceneView.RepaintAll();
            }
#endif
        }

        void LateUpdate()
        {
            if (!animator) animator = GetComponent<Animator>();
            Camera camera = referenceCamera ? referenceCamera : Camera.main;
            if (!camera || !ObjectLineArtFeature.TryGetCameraSample(camera, out var sample))
            {
                ReleaseAnimator();
                if (!warned)
                {
                    Debug.LogWarning("Line Art Stepped Animator needs a camera with an active Object Line Art Renderer Feature. Assign Reference Camera; normal Animator playback is retained until it is available.", this);
                    warned = true;
                }
                return;
            }

            warned = false;
            float delta = animator.updateMode == AnimatorUpdateMode.UnscaledTime
                ? Time.unscaledDeltaTime : Time.deltaTime;
            Step(camera, sample.generation, delta);
        }

        void Step(Camera camera, int generation, float deltaTime)
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!animator || !animator.runtimeAnimatorController)
            {
                ReleaseAnimator();
                return;
            }

            if (!ownsAnimator)
            {
                // Do not start an Animator that the scene or another script disabled.
                if (!animator.enabled) return;
                previousKeepState = animator.keepAnimatorStateOnDisable;
                previousWriteDefaults = animator.writeDefaultValuesOnDisable;
                previousCulling = animator.cullingMode;
                animator.keepAnimatorStateOnDisable = true;
                animator.writeDefaultValuesOnDisable = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = false;
                ownsAnimator = true;
                sampledCamera = camera;
                lastGeneration = generation;
                pendingTime = 0;
                // This frame already received its normal Animator update.
                animator.Update(0);
                RecordMotionSample();
                return;
            }

            pendingTime += Mathf.Max(0, deltaTime);
            if (sampledCamera == camera && lastGeneration == generation) return;

            sampledCamera = camera;
            lastGeneration = generation;
            float elapsed = pendingTime;
            pendingTime = 0;
            // Accumulate elapsed animation time so lower sampling rates do not slow
            // playback. Animator.speed and controller transitions still apply.
            if (elapsed > 0)
            {
                animator.Update(elapsed);
                RecordMotionSample();
            }
        }

        void OnDisable()
        {
            active.Remove(this);
            ReleaseAnimator();
        }

        void ReleaseAnimator()
        {
            if (ownsAnimator && animator)
            {
                // Restore while keep-state is still enabled, preserving the current state.
                animator.enabled = true;
                animator.keepAnimatorStateOnDisable = previousKeepState;
                animator.writeDefaultValuesOnDisable = previousWriteDefaults;
                animator.cullingMode = previousCulling;
            }
            ownsAnimator = false;
            sampledCamera = null;
            motionFrame = -1;
            pendingTime = 0;
        }
    }
}
