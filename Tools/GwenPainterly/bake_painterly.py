"""Bake true mesh-space normals and resample them into overlapping brush facets.

This generates numeric vector data from mesh geometry; no AI color image is used.
"""
from pathlib import Path
import json, re, argparse
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

ROOT=Path(__file__).resolve().parents[2]
WORK=Path(__file__).resolve().parent/'work'
OUT=ROOT/'Assets/Texture/GwenPainterly'

def unit(v):
    return v/np.maximum(np.linalg.norm(v,axis=-1,keepdims=True),1e-9)

def unity_mesh(path):
    text=path.read_text()
    count=int(re.search(r'm_VertexCount: (\d+)',text)[1])
    block=text.split('m_Channels:',1)[1].split('m_DataSize:',1)[0]
    channels=[tuple(map(int,m)) for m in re.findall(r'stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)',block)]
    raw=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',text)[1])
    strides={}
    for stream,offset,fmt,dim in channels:
        if dim: strides[stream]=max(strides.get(stream,0),offset+4*dim)
    starts={}; start=0
    for stream,stride in sorted(strides.items()):
        starts[stream]=start; start=(start+stride*count+15)//16*16
    def attr(i):
        stream,offset,fmt,dim=channels[i]
        assert fmt==0
        return np.ndarray((count,dim),dtype='<f4',buffer=raw,offset=starts[stream]+offset,strides=(strides[stream],4)).copy()
    idxfmt=int(re.search(r'm_IndexFormat: (\d+)',text)[1])
    indices=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',text)[1]),dtype='<u2' if idxfmt==0 else '<u4')
    subblock=text.split('m_SubMeshes:',1)[1].split('m_Shapes:',1)[0]
    subs=[tuple(map(int,m)) for m in re.findall(r'firstByte: (\d+)\s+indexCount: (\d+)\s+topology: \d+\s+baseVertex: (\d+)',subblock)]
    return attr(0),attr(1),attr(4),indices,subs

def source(entry):
    path=ROOT/'Assets/Model'/(entry['object']+'_SmoothNormalVC.asset')
    p,n,uv,ix,subs=unity_mesh(path)
    entries=json.loads((WORK/'surfaces.json').read_text())
    group=[e for e in entries if e['object']==entry['object']]
    mi=next(i for i,e in enumerate(group) if e['key']==entry['key'])
    start,count,base=subs[mi]; ix=ix[start//ix.itemsize:start//ix.itemsize+count].reshape(-1,3)+base
    assert len(ix)==entry['triangles'], (path,len(ix),entry['triangles'])
    return dict(pos=p[ix],n=n[ix],uv=uv[ix])

def raster(data,size):
    normal=np.zeros((size,size,3),np.float32)
    valid=np.zeros((size,size),bool)
    conflict=np.zeros_like(valid)
    for uv,n in zip(data['uv'],data['n']):
        # Repeat wrapping, including small triangles straddling the 0/1 boundary.
        for oy in range(-int(np.floor(uv[:,1].max())),1-int(np.floor(uv[:,1].min()))):
          for ox in range(-int(np.floor(uv[:,0].max())),1-int(np.floor(uv[:,0].min()))):
            q=(uv+[ox,oy])*size-.5
            lo=np.maximum(np.ceil(q.min(0)).astype(int),0); hi=np.minimum(np.floor(q.max(0)).astype(int),size-1)
            if np.any(hi<lo): continue
            x0,y0=lo; x1,y1=hi
            y,x=np.mgrid[y0:y1+1,x0:x1+1]; a,b,c=q
            den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(den)<1e-10: continue
            w0=((b[1]-c[1])*(x-c[0])+(c[0]-b[0])*(y-c[1]))/den
            w1=((c[1]-a[1])*(x-c[0])+(a[0]-c[0])*(y-c[1]))/den
            w2=1-w0-w1; inside=(w0>=-1e-6)&(w1>=-1e-6)&(w2>=-1e-6)
            nn=unit(w0[...,None]*n[0]+w1[...,None]*n[1]+w2[...,None]*n[2])
            region=normal[y0:y1+1,x0:x1+1]; mask=valid[y0:y1+1,x0:x1+1]
            conflict[y0:y1+1,x0:x1+1]|=inside&mask&(np.sum(region*nn,axis=-1)<.985)
            region[inside]=nn[inside]; mask[inside]=True
    return normal,valid,conflict

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--diagnose',action='store_true'); args=parser.parse_args()
    entries=json.loads((WORK/'surfaces.json').read_text())
    for entry in entries:
        data=source(entry); n,m,c=raster(data,512)
        print(entry['object'],entry['material'],'occupied',round(m.mean()*100,2),'conflicting',round(c.sum()/max(m.sum(),1)*100,3),flush=True)
