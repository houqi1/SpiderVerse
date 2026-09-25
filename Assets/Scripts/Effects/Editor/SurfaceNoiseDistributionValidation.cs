using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class SurfaceNoiseDistributionValidation
{
    struct Particle { public Vector4 positionSize, color, normalWS, surfacePosition, distributionNormal; }

    [MenuItem("SpiderVerse/Validate Fixed Count Distribution")]
    static void Validate()
    {
        var report = new List<string>();
        try
        {
            CheckGpu(report);
            CheckEffectCount(report);
            report.Add("PASS: GPU weighted selection and component count invariants.");
            Debug.Log(string.Join("\n", report));
        }
        catch (Exception e) { report.Add("FAIL: " + e); Debug.LogException(e); }
        Directory.CreateDirectory("Temp/ParticleColorValidation");
        File.WriteAllLines("Temp/ParticleColorValidation/distribution.txt", report);
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    static void CheckGpu(List<string> report)
    {
        const int count=2048, choices=16;
        var rng=new System.Random(1234);
        var samples=new Particle[count*choices];
        for(int i=0;i<samples.Length;i++)
        {
            float z=(float)rng.NextDouble()*2-1, phi=(float)rng.NextDouble()*Mathf.PI*2;
            float r=Mathf.Sqrt(1-z*z);
            Vector3 n=new Vector3(r*Mathf.Cos(phi),r*Mathf.Sin(phi),z);
            samples[i]=new Particle { positionSize=new Vector4(n.x,n.y,n.z,1),
                color=new Vector4(i,0,0,1), normalWS=new Vector4(0,0,1,.5f),
                surfacePosition=new Vector4(n.x,n.y,n.z,1), distributionNormal=new Vector4(n.x,n.y,n.z,1) };
        }
        var shader=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/Scripts/Effects/Editor/SurfaceNoiseDistributionValidation.compute"));
        var distribution=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("SurfaceNoiseAnchors"));
        try
        {
            int kernel=shader.FindKernel("Validate"), weights=distribution.FindKernel("BuildDistributionWeights"),
                scan=distribution.FindKernel("ScanDistributionWeights");
            using(var input=new ComputeBuffer(samples.Length,80))
            using(var a=new ComputeBuffer(samples.Length,4))
            using(var b=new ComputeBuffer(samples.Length,4))
            using(var output=new ComputeBuffer(count,4))
            {
                input.SetData(samples);
                shader.SetBuffer(kernel,"_SurfaceParticles",input);
                shader.SetBuffer(kernel,"_Selected",output);
                shader.SetInt("_Count",count);
                shader.SetVector("_SurfaceDistributionRange",new Vector4(0,samples.Length,0,count));
                distribution.SetBuffer(weights,"_SurfaceParticles",input);
                distribution.SetInt("_AnchorCount",samples.Length);
                distribution.SetInt("_AnchorOffset",0);
                distribution.SetInt("_DistributionSampleCount",samples.Length);
                var indices=new uint[count];
                void Run(float front,float width,Vector3 camera,bool ortho,bool enabled=true)
                {
                    Vector3 forward=-camera.normalized;
                    distribution.SetVector("_DistributionCameraPosition",new Vector4(camera.x,camera.y,camera.z,1));
                    distribution.SetVector("_DistributionCameraForward",new Vector4(forward.x,forward.y,forward.z,ortho?1:0));
                    distribution.SetVector("_DistributionSettings",new Vector4(front,width,1,0));
                    distribution.SetBuffer(weights,"_DistributionWrite",a);
                    distribution.Dispatch(weights,(samples.Length+63)/64,1,1);
                    ComputeBuffer read=a,write=b;
                    for(int step=1;step<samples.Length;step<<=1)
                    {
                        distribution.SetInt("_ScanStep",step);
                        distribution.SetBuffer(scan,"_DistributionRead",read);
                        distribution.SetBuffer(scan,"_DistributionWrite",write);
                        distribution.Dispatch(scan,(samples.Length+63)/64,1,1);
                        var swap=read; read=write; write=swap;
                    }
                    shader.SetBuffer(kernel,"_SurfaceDistributionCdf",read);
                    shader.SetVector("_SurfaceSilhouetteDistribution",new Vector4(enabled?1:0,front,width,choices));
                    shader.Dispatch(kernel,(count+63)/64,1,1); output.GetData(indices);
                }
                foreach(bool ortho in new[]{true,false})
                foreach(Vector3 camera in new[]{new Vector3(0,0,5),new Vector3(5,0,0)})
                foreach(float width in new[]{.01f,.5f,1f})
                {
                    Run(0,width,camera,ortho);
                    float maximum=0;
                    foreach(uint index in indices)
                    {
                        Require(index<samples.Length,"Invalid global candidate index");
                        Particle p=samples[index];
                        Vector3 view=ortho?camera.normalized:(camera-(Vector3)p.surfacePosition).normalized;
                        float facing=Mathf.Abs(Vector3.Dot((Vector3)p.distributionNormal,view));
                        maximum=Mathf.Max(maximum,facing);
                        Require(facing<width,"Zero front weight selected a front-facing triangle");
                    }
                    var previous=(uint[])indices.Clone(); Run(0,width,camera,ortho);
                    for(int i=0;i<count;i++) Require(previous[i]==indices[i],"Unstable selection");
                    report.Add($"GPU ortho={ortho}, camera={camera}, width={width}: {count} instances, ZERO front-face selections, max facing={maximum:F5}");
                }
                // Reproduce the reported failure: all local pools are front-facing,
                // except one valid side candidate in the last pool/source range.
                // Its shading normal is deliberately front-facing: geometry must win.
                for(int i=0;i<samples.Length;i++) samples[i].distributionNormal=new Vector4(0,0,1,1);
                int side=samples.Length-1;
                samples[side].distributionNormal=new Vector4(1,0,0,1);
                input.SetData(samples);
                Run(0,.01f,new Vector3(0,0,5),true);
                foreach(uint index in indices) Require(index==side,"A local fallback leaked onto a front face");
                report.Add("PASS: a side candidate in another pool/source receives all instances; no local fallback, shading normals ignored.");
                // The side triangle is removed from the eligible set: only now may
                // the entire layer fall back to originals to preserve its count.
                samples[side].distributionNormal.w=0; input.SetData(samples);
                Run(0,.01f,new Vector3(0,0,5),true);
                for(int i=0;i<count;i++) Require(indices[i]==i*choices,"Empty-support fallback lost original");
                Run(0,.5f,new Vector3(0,0,5),true,false);
                for(int i=0;i<count;i++) Require(indices[i]==i*choices,"Disabled mode changed original");
                Run(1,.5f,new Vector3(0,0,5),true);
                for(int i=0;i<count;i++) Require(indices[i]==i*choices,"Uniform mode changed original");
                report.Add("PASS: whole-layer zero support, disabled mode and weight-one preserve originals.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(shader); UnityEngine.Object.DestroyImmediate(distribution); }
    }

    [MenuItem("SpiderVerse/Audit Current Particle Distribution")]
    static void AuditCurrentScene()
    {
        var report=new List<string>();
        Camera camera=SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
        if(camera==null) return;
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        RenderTexture target=RenderTexture.GetTemporary(32,32,24);
        try
        {
            foreach(var effect in UnityEngine.Object.FindObjectsByType<SurfaceNoiseParticleEffect>(FindObjectsSortMode.None))
            {
                if(!effect.isActiveAndEnabled) continue;
                var effectType=typeof(SurfaceNoiseParticleEffect);
                object gpu=effectType.GetField("gpu",fields).GetValue(effect);
                if(gpu==null) continue;
                var layers=(SurfaceNoiseParticleEffect.LayerSettings[])effectType.GetField("layers",fields).GetValue(effect);
                var draw=gpu.GetType().GetMethod("Draw",fields);
                Type sampleType=draw.GetParameters()[2].ParameterType;
                object sample=Activator.CreateInstance(sampleType);
                sampleType.GetField("position").SetValue(sample,camera.transform.position);
                sampleType.GetField("forward").SetValue(sample,camera.transform.forward);
                sampleType.GetField("orthographic").SetValue(sample,camera.orthographic);
                using(var cmd=new CommandBuffer())
                {
                    cmd.SetRenderTarget(target);
                    cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                    cmd.SetGlobalFloat("_SurfaceColorFieldEnabled",0);
                    draw.Invoke(gpu,new object[]{cmd,camera,sample,layers});
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                var pools=(System.Collections.IEnumerable)gpu.GetType().GetField("pools",fields).GetValue(gpu);
                foreach(object pool in pools)
                {
                    Type type=pool.GetType();
                    var candidates=(GraphicsBuffer)type.GetField("particles",fields).GetValue(pool);
                    var data=new Particle[candidates.count]; candidates.GetData(data);
                    var batches=(System.Collections.IEnumerable)type.GetField("batches",fields).GetValue(pool);
                    foreach(object batch in batches)
                    {
                        Type bt=batch.GetType();
                        int layerIndex=(int)bt.GetField("layerIndex",fields).GetValue(batch);
                        int count=(int)bt.GetField("count",fields).GetValue(batch);
                        int choices=(int)bt.GetField("choiceCount",fields).GetValue(batch);
                        bool visible=(bool)bt.GetField("visible",fields).GetValue(batch);
                        if(!visible || count==0) continue;
                        var layer=layers[layerIndex];
                        var properties=(MaterialPropertyBlock)bt.GetField("properties",fields).GetValue(batch);
                        Vector4 range=properties.GetVector("_SurfaceDistributionRange");
                        if(!layer.preferSilhouetteDistribution || layer.frontFacingWeight>=1 || choices==1)
                        {
                            report.Add($"{effect.name} layer {layerIndex}: distribution disabled/uniform, count={count}");
                            continue;
                        }
                        int steps=0; for(int step=1;step<data.Length;step<<=1)steps++;
                        var cdfBuffer=(GraphicsBuffer)type.GetField(steps%2==0?"a":"b",fields).GetValue(pool);
                        var cdf=new uint[data.Length]; cdfBuffer.GetData(cdf);
                        uint total=cdf[cdf.Length-1];
                        int originalFront=0,selectedFront=0;
                        float width=Mathf.Clamp(layer.silhouetteDistributionWidth,.01f,1f);
                        bool Front(int index)
                        {
                            Vector3 view=camera.orthographic?-camera.transform.forward:
                                (camera.transform.position-(Vector3)data[index].surfacePosition).normalized;
                            return Mathf.Abs(Vector3.Dot((Vector3)data[index].distributionNormal,view))>=width;
                        }
                        for(int i=0;i<count;i++)
                        {
                            int original=(int)range.x+i*choices;
                            if(Front(original))originalFront++;
                            int selected=original;
                            if(total>0)
                            {
                                uint value=Math.Min((uint)(((float)range.z+i+.5f)/range.w*total),total-1);
                                int low=0,high=cdf.Length-1;
                                while(low<high) { int mid=low+(high-low)/2; if(cdf[mid]<=value)low=mid+1;else high=mid; }
                                selected=low;
                            }
                            if(Front(selected))selectedFront++;
                        }
                        report.Add($"{effect.name} layer {layerIndex}: count={count}, frontWeight={layer.frontFacingWeight}, width={width}, globalWeight={total}, originalFront={originalFront}, selectedFront={selectedFront}");
                        if(layer.frontFacingWeight==0 && total>0) Require(selectedFront==0,"Scene audit selected a zero-weight face");
                    }
                }
            }
            report.Add("PASS: actual scene buffers and production draw dispatch audited with the current Scene View camera.");
        }
        catch(Exception e) { report.Add("FAIL: "+e); Debug.LogException(e); }
        finally { RenderTexture.ReleaseTemporary(target); SceneView.RepaintAll(); }
        Directory.CreateDirectory("Temp/ParticleColorValidation");
        File.WriteAllLines("Temp/ParticleColorValidation/distribution-scene.txt",report);
        Debug.Log(string.Join("\n",report));
    }

    [MenuItem("SpiderVerse/Validate Per Renderer Counts")]
    static void ValidateRendererCounts()
    {
        var report=new List<string>();
        var root=new GameObject("Renderer count validation") { hideFlags=HideFlags.HideAndDontSave };
        root.SetActive(false);
        var a=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var b=GameObject.CreatePrimitive(PrimitiveType.Cube);
        a.transform.SetParent(root.transform); b.transform.SetParent(root.transform);
        var ra=a.GetComponent<Renderer>(); var rb=b.GetComponent<Renderer>();
        var effect=root.AddComponent<SurfaceNoiseParticleEffect>();
        var material=new Material(Shader.Find("Custom/UVDots"));
        const BindingFlags fields=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
        var type=typeof(SurfaceNoiseParticleEffect);
        try
        {
            var layer=new SurfaceNoiseParticleEffect.LayerSettings { particleMaterial=material, threshold=0, softness=0 };
            type.GetField("layers",fields).SetValue(effect,new[]{layer});
            type.GetField("layerSettingsVersion",fields).SetValue(effect,1);
            type.GetField("candidateCount",fields).SetValue(effect,256);
            type.GetField("targetRenderers",fields).SetValue(effect,new[]{ra,rb});
            root.SetActive(true); effect.RebuildEffect();
            int defaultA=effect.GetRendererCandidateCount(ra), defaultB=effect.GetRendererCandidateCount(rb);
            Require(defaultA+defaultB==256,"Legacy global budget changed");
            var update=type.GetMethod("UpdateEffect",fields);
            string Signature(Renderer renderer)
            {
                var sources=(System.Collections.IList)type.GetField("sources",fields).GetValue(effect);
                var candidates=(Array)type.GetField("candidates",fields).GetValue(effect);
                var builder=new System.Text.StringBuilder();
                foreach(object c in candidates)
                {
                    var ct=c.GetType(); int index=(int)ct.GetField("sourceIndex").GetValue(c);
                    object source=sources[index];
                    if((Renderer)source.GetType().GetField("Renderer").GetValue(source)!=renderer)continue;
                    builder.Append(ct.GetField("i0").GetValue(c)).Append(':');
                    builder.Append(ct.GetField("i1").GetValue(c)).Append(':');
                    builder.Append(ct.GetField("i2").GetValue(c)).Append(':');
                    builder.Append(((Vector3)ct.GetField("barycentric").GetValue(c)).ToString("R")).Append(';');
                }
                return builder.ToString();
            }
            string legacyB=Signature(rb);
            effect.SetRendererCandidateCount(ra,37); update.Invoke(effect,null);
            Require(effect.GetRendererCandidateCount(ra)==37 && effect.GetRendererCandidateCount(rb)==defaultB,"Override changed other renderer budget");
            Require(Signature(rb)==legacyB,"Override reshuffled untouched renderer");
            effect.SetRendererCandidateCount(rb,83); update.Invoke(effect,null);
            string stableB=Signature(rb);
            foreach(int count in new[]{0,19,127})
            {
                effect.SetRendererCandidateCount(ra,count); update.Invoke(effect,null);
                Require(effect.GetRendererParticleCount(ra)==count,"Renderer A count mismatch");
                Require(effect.GetRendererParticleCount(rb)==83,"Renderer B count changed");
                Require(Signature(rb)==stableB,"Renderer B positions reshuffled");
            }
            type.GetField("targetRenderers",fields).SetValue(effect,new[]{rb,ra}); effect.RebuildEffect();
            Require(effect.GetRendererParticleCount(ra)==127 && effect.GetRendererParticleCount(rb)==83,"Counts followed array index instead of renderer");
            Require(Signature(rb)==stableB,"Reorder reshuffled independent samples");
            layer.preferSilhouetteDistribution=true; layer.frontFacingWeight=0; effect.RebuildEffect();
            object gpu=type.GetField("gpu",fields).GetValue(effect);
            var pools=(System.Collections.IEnumerable)gpu.GetType().GetField("pools",fields).GetValue(gpu);
            foreach(object pool in pools)
            {
                var batches=(System.Collections.IList)pool.GetType().GetField("batches",fields).GetValue(pool);
                Require(batches.Count==1,"Distribution can migrate across independent renderers");
            }
            Require(effect.SubmittedParticleCount==210,"Silhouette setting changed fixed renderer counts");
            layer.distributionNoise=Texture2D.blackTexture; layer.threshold=.5f; effect.RebuildEffect();
            Require(effect.SubmittedParticleCount==0,"Noise no longer filters independent budgets");
            layer.distributionNoise=null; layer.threshold=0; layer.preferSilhouetteDistribution=false;
            effect.ClearRendererCandidateCount(ra); effect.ClearRendererCandidateCount(rb);
            type.GetField("targetRenderers",fields).SetValue(effect,new[]{ra,rb}); effect.RebuildEffect();
            Require(effect.GetRendererCandidateCount(ra)==defaultA && effect.GetRendererCandidateCount(rb)==defaultB,"Clearing overrides failed to restore global distribution");
            Require(Signature(rb)==legacyB,"Legacy renderer positions were not restored");
            report.Add($"PASS: legacy budget 256 ({defaultA}/{defaultB}), independent counts 37/83 then 0/19/127, runtime updates, stable other-renderer positions, reorder, zero budget, isolated distribution pools, noise filtering, and restore.");
        }
        catch(Exception e) { report.Add("FAIL: "+e); Debug.LogException(e); }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(material); }
        Directory.CreateDirectory("Temp/ParticleColorValidation");
        File.WriteAllLines("Temp/ParticleColorValidation/renderer-counts.txt",report);
        Debug.Log(string.Join("\n",report));
    }

    static void CheckEffectCount(List<string> report)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.hideFlags = HideFlags.HideAndDontSave;
        sphere.SetActive(false);
        var effect = sphere.AddComponent<SurfaceNoiseParticleEffect>();
        var material = new Material(Shader.Find("Custom/UVDots"));
        try
        {
            var layer = new SurfaceNoiseParticleEffect.LayerSettings { particleMaterial=material, threshold=.5f };
            var type = typeof(SurfaceNoiseParticleEffect);
            type.GetField("layers",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(effect,new[]{layer});
            type.GetField("layerSettingsVersion",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(effect,1);
            type.GetField("candidateCount",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(effect,256);
            sphere.SetActive(true);
            foreach(float threshold in new[]{0f,.5f,1f})
            {
                layer.threshold=threshold;
                layer.preferSilhouetteDistribution=false;
                effect.RebuildEffect(); int expected=effect.SubmittedParticleCount;
                foreach(float front in new[]{0f,.2f,1f})
                foreach(float width in new[]{.01f,.5f,1f})
                {
                    layer.preferSilhouetteDistribution=true; layer.frontFacingWeight=front;
                    layer.silhouetteDistributionWidth=width; effect.RebuildEffect();
                    Require(effect.SubmittedParticleCount==expected,"Distribution changed submitted count");
                }
                report.Add($"Component threshold={threshold}: count={expected} with distribution off/on and all parameter extremes.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(sphere); UnityEngine.Object.DestroyImmediate(material); }
    }
}
