using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpiderVerse.LineArt
{
    // Opt-in standalone benchmark. No effect without the explicit command-line flag.
    public sealed class LineArtBenchmark : MonoBehaviour
    {
        RenderTexture target;ObjectLineArtFeature feature;Camera view;Vector3 position;Quaternion rotation;
        readonly List<double> times=new List<double>();readonly List<double> gpuTimes=new List<double>();
        readonly FrameTiming[] timing=new FrameTiming[1];double start,last;string mode,output;bool moving,done,profile;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"-lineArtBenchmark")>=0)new GameObject("Line Art benchmark").AddComponent<LineArtBenchmark>();}
        void Start()
        {
            var args=Environment.GetCommandLineArgs();string Arg(string name,string fallback){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
            profile=Array.IndexOf(args,"-lineArtProfile")>=0;
            mode=Arg("-lineArtMode","gpu");output=Arg("-lineArtOutput",Path.Combine(Application.persistentDataPath,"line-art-benchmark"));moving=Arg("-lineArtMotion","moving")=="moving";
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.runInBackground=true;
            foreach(var f in Resources.FindObjectsOfTypeAll<ObjectLineArtFeature>())if(f.name=="Object Line Art"){feature=f;break;}
            if(!feature){Debug.LogError("Benchmark feature missing");Application.Quit(2);return;}
            feature.executionMode=mode=="gpu"?ObjectLineArtFeature.ExecutionMode.GpuGeometry:ObjectLineArtFeature.ExecutionMode.CpuReference;feature.SetActive(mode!="off");feature.showInSceneView=false;
            view=Camera.main;target=new RenderTexture(960,960,24,RenderTextureFormat.ARGB32);target.Create();view.enabled=false;view.aspect=1;position=view.transform.position;rotation=view.transform.rotation;start=last=Time.realtimeSinceStartupAsDouble;
        }
        readonly string[] markerNames={"Line Art Refit","Line Art Edges","Line Art Intersections","Line Art Visibility","Line Art Strokes"};
        readonly UnityEngine.Profiling.Recorder[] recorders=new UnityEngine.Profiling.Recorder[5];readonly double[] sums=new double[5];readonly int[] samples=new int[5];
        void Update()
        {
            if(done||!view)return;double now=Time.realtimeSinceStartupAsDouble,elapsed=now-start,dt=now-last;last=now;
            if(moving){view.transform.position=position+view.transform.right*(Mathf.Sin((float)elapsed*.8f)*.08f);view.transform.rotation=rotation*Quaternion.Euler(0,Mathf.Sin((float)elapsed*.65f)*3,0);}
            RenderPipeline.SubmitRenderRequest(view,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            FrameTimingManager.CaptureFrameTimings();
            if(elapsed>8){times.Add(dt*1000);if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].gpuFrameTime>0)gpuTimes.Add(timing[0].gpuFrameTime);}
            if(profile)for(int i=0;i<recorders.Length;i++){if(recorders[i]==null||!recorders[i].isValid){recorders[i]=UnityEngine.Profiling.Recorder.Get(markerNames[i]);recorders[i].enabled=true;}if(elapsed>8&&recorders[i].gpuSampleBlockCount>0){sums[i]+=recorders[i].gpuElapsedNanoseconds/1e6;samples[i]++;}}
            if(elapsed<20)return;done=true;times.Sort();gpuTimes.Sort();double sum=0;foreach(double t in times)sum+=t;double avg=sum/Math.Max(1,times.Count);
            double Percent(List<double> values,double q)=>values.Count==0?0:values[Math.Min(values.Count-1,(int)(values.Count*q))];
            string F(double x)=>x.ToString("F3",CultureInfo.InvariantCulture);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output+".txt",$"device={SystemInfo.graphicsDeviceName}\nrenderMode=explicit-offscreen\nmode={mode}\nmotion={moving}\nresolution={Screen.width}x{Screen.height}\nframes={times.Count}\nmeanMs={F(avg)}\nfps={F(1000/avg)}\np50Ms={F(Percent(times,.5))}\np95Ms={F(Percent(times,.95))}\ngpuP50Ms={F(Percent(gpuTimes,.5))}\nstats={feature.LastStats}\n");
            for(int i=0;i<recorders.Length;i++)File.AppendAllText(output+".txt",$"{markerNames[i]}Ms={F(sums[i]/Math.Max(1,samples[i]))} samples={samples[i]}\n");
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(960,960,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,960,960),0,0);image.Apply();File.WriteAllBytes(output+".png",image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;Invoke(nameof(Finish),1);
        }
        void Finish()=>Application.Quit();
    }
}
