using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal static class SurfaceNoiseColorFieldValidation
{
    [MenuItem("SpiderVerse/Validate Particle Jump Flood")]
    static void Validate()
    {
        var report = new List<string>();
        try
        {
            foreach (int downsample in new[] { 1, 2 })
            {
                Check(downsample, false, report);
                Check(downsample, true, report);
            }
            report.Add("PASS: full/half resolution, nonzero crop origin, odd dimensions, empty mask, separated regions and occlusion gap.");
            Debug.Log(string.Join("\n", report));
        }
        catch (Exception e) { report.Add("FAIL: " + e); Debug.LogException(e); }
        Directory.CreateDirectory("Temp/ParticleColorValidation");
        File.WriteAllLines("Temp/ParticleColorValidation/jump-flood.txt", report);
    }

    static void Check(int scale, bool empty, List<string> report)
    {
        const int width = 65, height = 47, ox = 3, oy = 5, rw = 61, rh = 41, radius = 32;
        var mask = new Texture2D(width, height, TextureFormat.RFloat, false, true);
        var values = new float[width * height];
        if (!empty)
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if ((x >= 8 && x < 39 && y >= 10 && y < 35 && (x < 20 || x > 23)) ||
                    (x >= 45 && x < 60 && y >= 7 && y < 21)) values[y * width + x] = 1;
        mask.SetPixelData(values, 0); mask.Apply();
        int fw = (rw + scale - 1) / scale, fh = (rh + scale - 1) / scale;
        var descriptor = new RenderTextureDescriptor(fw, fh, GraphicsFormat.R16G16_UInt, 0) { enableRandomWrite = true };
        var a = new RenderTexture(descriptor); var b = new RenderTexture(descriptor);
        var compute = UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("SurfaceNoiseColorField"));
        try
        {
            a.Create(); b.Create();
            int init = compute.FindKernel("Initialize"), flood = compute.FindKernel("JumpFlood");
            compute.SetInts("_FullSize", width, height); compute.SetInts("_FieldSize", fw, fh);
            compute.SetInts("_Origin", ox, oy); compute.SetInt("_Downsample", scale);
            compute.SetTexture(init, "_Mask", mask); compute.SetTexture(init, "_SeedsWrite", a);
            compute.Dispatch(init, (fw + 7) / 8, (fh + 7) / 8, 1);
            RenderTexture read = a, write = b;
            for (int step = Mathf.NextPowerOfTwo(radius / scale); step >= 1; step >>= 1) Dispatch(step);
            Dispatch(4); Dispatch(2); Dispatch(1);
            // DX11 can sample/store RG16 UInt but Unity cannot read this texture
            // format back directly. Copy the actual output into a uint2 buffer.
            var copy = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Scripts/Effects/Editor/SurfaceNoiseColorFieldReadback.compute");
            var seeds = new uint[fw * fh * 2];
            using (var buffer = new ComputeBuffer(fw * fh, 8))
            {
                int kernel = copy.FindKernel("CopySeeds");
                copy.SetInts("_Size", fw, fh); copy.SetTexture(kernel, "_Seeds", read);
                copy.SetBuffer(kernel, "_Result", buffer); copy.Dispatch(kernel, (fw + 7) / 8, (fh + 7) / 8, 1);
                buffer.GetData(seeds);
            }
            var reliable = new List<Vector2>();
            for (int y = oy; y < oy + rh && y < height; y++)
            for (int x = ox; x < ox + rw && x < width; x++)
                if (Reliable(x, y)) reliable.Add(new Vector2(x, y));
            float worst = 0; int checkedPixels = 0;
            for (int y = 0; y < fh; y++)
            for (int x = 0; x < fw; x++)
            {
                int index = (y * fw + x) * 2;
                int sx = (int)seeds[index] - 1, sy = (int)seeds[index + 1] - 1;
                if (empty)
                {
                    if (sx != -1 || sy != -1) throw new Exception("Empty mask produced a valid source");
                    continue;
                }
                var pixel = new Vector2(ox + x * scale, oy + y * scale) + Vector2.one * (scale - 1) * 0.5f;
                float nearest = float.MaxValue;
                foreach (var candidate in reliable) nearest = Mathf.Min(nearest, Vector2.Distance(pixel, candidate));
                if (nearest > radius - 2) continue;
                if (!Reliable(sx, sy)) throw new Exception($"Invalid/background seed at {x},{y}: {sx},{sy}");
                float error = Vector2.Distance(pixel, new Vector2(sx, sy)) - nearest;
                worst = Mathf.Max(worst, error); checkedPixels++;
                if (error > scale * 1.5f) throw new Exception($"Nearest source error {error:F3} exceeds tolerance at {x},{y}, source {sx},{sy}");
            }
            report.Add($"scale={scale}, empty={empty}: PASS; {checkedPixels} pixels checked; maximum excess distance={worst:F3}px");

            bool Reliable(int x, int y)
            {
                if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1) return false;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                    if (values[(y + j) * width + x + i] == 0) return false;
                return true;
            }
            void Dispatch(int step)
            {
                compute.SetInt("_Jump", step); compute.SetTexture(flood, "_SeedsRead", read);
                compute.SetTexture(flood, "_SeedsWrite", write); compute.Dispatch(flood, (fw + 7) / 8, (fh + 7) / 8, 1);
                var swap = read; read = write; write = swap;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mask); UnityEngine.Object.DestroyImmediate(compute);
            a.Release(); b.Release(); UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
        }
    }

    [MenuItem("SpiderVerse/Compare Particle Color (Scene View)")]
    static void Compare()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) throw new InvalidOperationException("Open a Scene View first.");
        var effects = UnityEngine.Object.FindObjectsByType<SurfaceNoiseParticleEffect>(FindObjectsSortMode.None);
        var original = Array.ConvertAll(effects, e => e.useJumpFloodColor);
        var originalHalf = Array.ConvertAll(effects, e => e.halfResolutionColorField);
        var go = new GameObject("Particle color comparison camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.CopyFrom(view.camera); camera.cameraType = CameraType.Game; camera.enabled = false;
        camera.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);
        var additional = camera.GetUniversalAdditionalCameraData();
        additional.renderPostProcessing = view.sceneViewState.showImageEffects;
        var sceneData = view.camera.GetComponent<UniversalAdditionalCameraData>();
        if (sceneData != null) additional.volumeLayerMask = sceneData.volumeLayerMask;
        int width = view.camera.pixelWidth, height = view.camera.pixelHeight;
        // Scene View's navigation pivot may be far behind a close-up reached with
        // fly navigation. Orbit around visible character geometry instead.
        float focusDistance = float.PositiveInfinity;
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        foreach (var effect in effects)
            if (effect.ColorSourceRenderers != null)
                foreach (var renderer in effect.ColorSourceRenderers)
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                        renderer.bounds.IntersectRay(ray, out float distance) && distance > camera.nearClipPlane)
                        focusDistance = Mathf.Min(focusDistance, distance);
        if (float.IsInfinity(focusDistance)) focusDistance = Mathf.Max(camera.nearClipPlane * 2, view.size);
        Vector3 focus = ray.GetPoint(focusDistance);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        try
        {
            target.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Directory.CreateDirectory("Temp/ParticleColorValidation");
            string[] names = { "anchor", "jump-flood", "jump-flood-full", "jump-flood-orbit", "jump-flood-ortho" };
            for (int stage = 0; stage < names.Length; stage++)
            {
                foreach (var effect in effects)
                {
                    effect.useJumpFloodColor = stage > 0;
                    effect.halfResolutionColorField = stage != 2;
                }
                if (stage == 3)
                {
                    camera.transform.RotateAround(focus, Vector3.up, 20);
                    camera.ResetWorldToCameraMatrix(); camera.ResetProjectionMatrix();
                }
                if (stage == 4)
                {
                    camera.orthographic = true;
                    camera.orthographicSize = focusDistance *
                        Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    camera.ResetProjectionMatrix();
                }
                RenderPipeline.SubmitRenderRequest(camera, request);
                var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                    File.WriteAllBytes("Temp/ParticleColorValidation/" + names[stage] + ".png", texture.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
            }
            Debug.Log("Particle color comparison saved in Temp/ParticleColorValidation.");
        }
        finally
        {
            for (int i = 0; i < effects.Length; i++) if (effects[i] != null)
            {
                effects[i].useJumpFloodColor = original[i]; effects[i].halfResolutionColorField = originalHalf[i];
            }
            RenderTexture.active = previous; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go);
            view.Repaint();
        }
    }
}
