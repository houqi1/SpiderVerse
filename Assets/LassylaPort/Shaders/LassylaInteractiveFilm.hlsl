// SPDX-License-Identifier: GPL-2.0-or-later
// EEVEE 5.2.3 Film::store_combined, combined_neighbor_boundbox and sample_catmull_rom.
TEXTURE2D_X(_StudioBackground);
TEXTURE2D_X_FLOAT(_SampleDepth);
TEXTURE2D_X_FLOAT(_BackdropDepth);
float4 _FilmSize;
float4x4 _CurrentToPreviousClip;
float _ViewMoved;

float4 ToYCoCg(float4 v) {
    return float4(dot(v.rgb,float3(1,2,1)),2*(v.r-v.b),dot(v.rgb,float3(-1,2,-1)),v.a);
}
float4 FromYCoCg(float4 v) {
    return float4(float3(v.x+v.y-v.z,v.x+v.z,v.x-v.y-v.z)*.25,v.a);
}
int2 FilmPixel(int2 p) {return clamp(p,0,(int2)_FilmSize.xy-1);}
float4 SceneSample(int2 p) {
    p=FilmPixel(p);
    #ifdef TOON_COVERAGE_HISTORY
    return ToYCoCg(LOAD_TEXTURE2D_X(_Source,p).rrrr);
    #else
    float4 surface=LOAD_TEXTURE2D_X(_Source,p);
    return ToYCoCg(float4(surface.rgb+(1-surface.a)*LOAD_TEXTURE2D_X(_StudioBackground,p).rgb,surface.a));
    #endif
}
float4 HistoryRGB(float2 p) {
    float2 f=frac(p),f2=f*f,f3=f2*f;
    float2 w0=f2-.5*(f3+f),w1=1+1.5*f3-2.5*f2;
    float2 w2=-1.5*f3+2*f2+.5*f,w3=.5*(f3-f2);
    float2 w12=w1+w2,center=floor(p)+.5;
    float2 mid=(center+w2/w12)*_FilmSize.zw,lo=(center-1)*_FilmSize.zw,hi=(center+2)*_FilmSize.zw;
    float4 crossWeight=w12.xyyx*float4(w0.yx,w3.xy);
    float wc=w12.x*w12.y;
    float4 h=SAMPLE_TEXTURE2D_X_LOD(_History,sampler_LinearClamp,mid,0)*wc;
    h+=SAMPLE_TEXTURE2D_X_LOD(_History,sampler_LinearClamp,float2(mid.x,lo.y),0)*crossWeight.x;
    h+=SAMPLE_TEXTURE2D_X_LOD(_History,sampler_LinearClamp,float2(lo.x,mid.y),0)*crossWeight.y;
    h+=SAMPLE_TEXTURE2D_X_LOD(_History,sampler_LinearClamp,float2(hi.x,mid.y),0)*crossWeight.z;
    h+=SAMPLE_TEXTURE2D_X_LOD(_History,sampler_LinearClamp,float2(mid.x,hi.y),0)*crossWeight.w;
    return h/(wc+dot(crossWeight,1));
}
float NearestZ(int2 p) {
    p=FilmPixel(p);
    float a=LOAD_TEXTURE2D_X(_SampleDepth,p).r,b=LOAD_TEXTURE2D_X(_BackdropDepth,p).r;
    #if UNITY_REVERSED_Z
        return max(a,b);
    #else
        return min(a,b);
    #endif
}
float2 HistoryMotion(int2 p) {
    if(_ViewMoved<.5)return 0;
    int2 nearest=p;
    float depth=NearestZ(p);
    const int2 offsets[4]={int2(-2,-2),int2(2,-2),int2(-2,2),int2(2,2)};
    [unroll]for(int i=0;i<4;i++){
        int2 q=FilmPixel(p+offsets[i]);float z=NearestZ(q);
        #if UNITY_REVERSED_Z
            bool closer=z>depth;
        #else
            bool closer=z<depth;
        #endif
        if(closer){depth=z;nearest=q;}
    }
    float2 xy=(nearest+.5)*_FilmSize.zw*2-1;
    #if UNITY_UV_STARTS_AT_TOP
        xy.y=-xy.y;
    #endif
    #if !UNITY_REVERSED_Z
        depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
    #endif
    float4 prev=mul(_CurrentToPreviousClip,float4(xy,depth,1));
    if(prev.w<=0)return _FilmSize.xy;
    float2 delta=prev.xy/prev.w-xy;
    #if UNITY_UV_STARTS_AT_TOP
        delta.y=-delta.y;
    #endif
    return delta*.5*_FilmSize.xy;
}
float ClipTime(float origin,float direction,float lo,float hi) {
    if(abs(direction)<1e-6)return 0;
    return ((direction>0?lo:hi)-origin)/direction;
}
float4 PlaybackFilm(V input):SV_Target {
    int2 p=(int2)input.positionCS.xy;
    float4 src=0;float weights=0;
    [unroll]for(int j=0;j<25;j++){
        if(_Taps[j].z==0)continue;
        float4 c=SceneSample(p+(int2)_Taps[j].xy);
        float w=_Taps[j].z/(4+c.x*_ExposureScale);src+=c*w;weights+=w;
    }
    src/=weights;
    float4 result=src;
    if(_HistoryWeight>0){
        float2 motion=HistoryMotion(p),xy=p+motion;
        if(all(xy>=0)&&all(xy<_FilmSize.xy)){
            float4 previous=_ViewMoved>.5?HistoryRGB(xy):LOAD_TEXTURE2D_X(_History,p);
            #ifdef TOON_COVERAGE_HISTORY
            previous=previous.rrrr;
            #endif
            float4 hist=ToYCoCg(previous);
            float4 minPlus=SceneSample(p),maxPlus=minPlus;
            const int2 plus[4]={int2(-1,0),int2(0,-1),int2(1,0),int2(0,1)};
            const int2 corners[4]={int2(-1,-1),int2(1,-1),int2(-1,1),int2(1,1)};
            [unroll]for(int k=0;k<4;k++){float4 c=SceneSample(p+plus[k]);minPlus=min(minPlus,c);maxPlus=max(maxPlus,c);}
            float4 minSquare=minPlus,maxSquare=maxPlus;
            [unroll]for(int k=0;k<4;k++){float4 c=SceneSample(p+corners[k]);minSquare=min(minSquare,c);maxSquare=max(maxSquare,c);}
            float4 lo=(minPlus+minSquare)*.5,hi=(maxPlus+maxSquare)*.5;
            float lumaDistance=min(hist.x-lo.x,hi.x-hist.x);
            float blend=lerp(.05,.20,saturate(length(motion)*.02));
            blend*=saturate((hi.x>lo.x?8*lumaDistance/(hi.x-lo.x):0)+.1);
            float4 dir=src-hist;
            float t=max(ClipTime(hist.x,dir.x,lo.x,hi.x),max(ClipTime(hist.y,dir.y,lo.y,hi.y),ClipTime(hist.z,dir.z,lo.z,hi.z)));
            hist.rgb+=dir.rgb*saturate(t);
            hist.a+=dir.a*saturate(ClipTime(hist.a,dir.a,lo.a,hi.a));
            float wh=(1-blend)/(4+hist.x*_ExposureScale),ws=blend/(4+src.x*_ExposureScale);
            result=(hist*wh+src*ws)/(wh+ws);
        }
    }
    result=max(0,FromYCoCg(result));if(result.a>.995)result.a=1;
    return asfloat((asuint(result)+0x1000u)&0xffffe000u);
}
