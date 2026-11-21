"""4096px object-space vector atlas, brush facets, padding and numeric QA."""
from pathlib import Path
import json, numpy as np, cv2
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt
from bake_painterly import raster,unit

ROOT=Path(__file__).resolve().parents[2]
WORK=Path(__file__).resolve().parent/'work'
OUT=ROOT/'Assets/Texture/GwenPainterly'
SIZE=4096
BRUSH_SCALE=2.7

def arr(v,keys): return np.array([[p[k] for k in keys] for p in v],np.float32)
def save_normal(path,n):
    rgb=np.round(np.clip(n*.5+.5,0,1)*65535).astype(np.uint16)
    cv2.imwrite(str(path),np.flipud(rgb)[...,::-1])

def main():
    import gc
    OUT.mkdir(parents=True,exist_ok=True)
    surfaces=json.loads((WORK/'atlas_meshes.json').read_text())['surfaces']
    report={'size':SIZE,'brush_scale':BRUSH_SCALE,'space':'Unity object space, RGB=normal*0.5+0.5','uv':'UV1 / Mesh.uv2 / TEXCOORD1','surfaces':[]}
    base_atlas=np.memmap(WORK/'base_u16.bin',mode='w+',dtype=np.uint16,shape=(SIZE,SIZE,3))
    paint_atlas=np.memmap(WORK/'paint_u16.bin',mode='w+',dtype=np.uint16,shape=(SIZE,SIZE,3))
    base_atlas[:]=[32768,32768,65535]; paint_atlas[:]=[32768,32768,65535]
    rects=[(2048,0,2048),(2048,2048,1024),(0,0,2048),(2048,3072,1024),(0,2048,2048),(3072,2048,1024)]
    rng=np.random.default_rng(140026)
    for si,(s,(ox,oy,size)) in enumerate(zip(surfaces,rects)):
        ix=np.array(s['triangles']).reshape(-1,3); uv=(arr(s['uv'],'xy')*SIZE-[ox,oy])/size
        p=arr(s['positions'],'xyz'); vn=arr(s['normals'],'xyz')
        n,m,c=raster(dict(uv=uv[ix],n=vn[ix]),size)
        if c.sum()>10: raise RuntimeError('Conflicting atlas UV')
        flow=np.zeros((size,size,2),np.float32)
        for tri in ix:
            tuv=uv[tri]; pp=p[tri]; edges=(pp[1:]-pp[0]).T
            coef=np.linalg.lstsq(edges,[0.,0.,1.],rcond=None)[0]
            d=(tuv[1:]-tuv[0]).T@coef; d=d/max(np.linalg.norm(d),1e-8)
            cv2.fillConvexPoly(flow,np.round(tuv*size-.5).astype(np.int32),(float(d[0]),float(d[1])))
        painted=n.copy(); ys,xs=np.where(m); strokes=0
        for length,width,coverage in [(65,22,5.),(36,11,2.4)]:
            length*=BRUSH_SCALE; width*=BRUSH_SCALE
            count=int(len(xs)*coverage/(length*width))
            for idx in rng.integers(0,len(xs),count):
                x,y=int(xs[idx]),int(ys[idx]); factor=.65 if si==4 else 1.
                ll=length*rng.uniform(.7,1.35)*factor; ww=width*rng.uniform(.65,1.3)*factor
                angle=np.arctan2(flow[y,x,1],flow[y,x,0])+rng.normal(0,.25)
                ca,sa=np.cos(angle),np.sin(angle); radius=int(ll*.65+ww)
                x0,x1=max(0,x-radius),min(size,x+radius+1); y0,y1=max(0,y-radius),min(size,y+radius+1)
                yy,xx=np.mgrid[y0:y1,x0:x1]; dx=xx-x; dy=yy-y
                u=(dx*ca+dy*sa)/(ll*.5); v=(-dx*sa+dy*ca)/(ww*.5); phase=rng.uniform(0,6.28)
                end=.86+.1*np.sin(v*13+phase)+.045*np.sin(v*37+phase)
                side=.86+.09*np.sin(u*10+phase)+.04*np.sin(u*27)
                alpha=np.clip(np.minimum((end-np.abs(u))*ll*.25,(side-np.abs(v))*ww*.3),0,1).astype(np.float32)
                sub=n[y0:y1,x0:x1]; sample=n[y,x]
                alpha*=m[y0:y1,x0:x1]&(np.sum(sub*sample,axis=-1)>.88)
                region=painted[y0:y1,x0:x1]; region[:]=region*(1-alpha[...,None])+sample*alpha[...,None]
                strokes+=1
        painted=unit(painted)
        angles=np.degrees(np.arccos(np.clip(np.sum(painted[m]*n[m],axis=-1),-1,1)))
        info=dict(name=s['name'],coverage_pixels=int(m.sum()),conflicting_pixels=int(c.sum()),strokes=strokes,mean_angle=float(angles.mean()),p95_angle=float(np.percentile(angles,95)),max_angle=float(angles.max()))
        report['surfaces'].append(info)
        dist,nearest=distance_transform_edt(~m,return_indices=True); pad=(~m)&(dist<=20)
        for field,atlas in [(n,base_atlas),(painted,paint_atlas)]:
            field[~m]=[0,0,1]; field[pad]=field[nearest[0][pad],nearest[1][pad]]
            atlas[oy:oy+size,ox:ox+size]=np.round(np.clip(field*.5+.5,0,1)*65535).astype(np.uint16)
        print(json.dumps(info),flush=True)
        del n,m,c,flow,painted,angles,dist,nearest,pad,field,region,sub
        gc.collect()
    for atlas,path in [(base_atlas,WORK/'Gwen_NormalOS_Base.png'),(paint_atlas,OUT/'Gwen_Painterly_NormalOS_4K.png')]:
        cv2.imwrite(str(path),np.ascontiguousarray(np.flipud(atlas)[...,::-1]))
    Image.fromarray(np.flipud((paint_atlas[::4,::4]/257).astype(np.uint8))).save(WORK/'atlas_preview.png')
    (WORK/'validation.json').write_text(json.dumps(report,indent=2))

if __name__=='__main__': main()
