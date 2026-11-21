"""Replay shared vector/color strokes, then paint physically continuous seam stamps.

Base color is resampled from the original UV0 assets into the existing unique UV1
atlas. Geometry-space seam stamps write the same shape into both RGB color and
model-space normals, irrespective of UV island or renderer boundaries.
"""
from pathlib import Path
from collections import defaultdict
import json,gc
import numpy as np
import cv2
from PIL import Image
from scipy.ndimage import distance_transform_edt
from scipy.spatial import cKDTree
from bake_painterly import raster,unit
from make_atlas import arr,BRUSH_SCALE

ROOT=Path(__file__).resolve().parents[2]; WORK=Path(__file__).resolve().parent/'work'
OUT=ROOT/'Assets/Texture/GwenPainterly'; SIZE=4096
SEAM_BRUSH_SCALE=BRUSH_SCALE/1.8
RECTS=[(2048,0,2048),(2048,2048,1024),(0,0,2048),(2048,3072,1024),(0,2048,2048),(3072,2048,1024)]
SURFACES=json.loads((WORK/'atlas_meshes.json').read_text())['surfaces']

def material_strength(name):
    if 'face' in name: return .20
    if 'hair' in name: return .48
    if name.endswith('_05'): return .40
    if name.endswith('_01'): return .55
    if name.endswith('_03'): return .70
    return .94

def raster_attributes(s,uv,size):
    ix=np.array(s['triangles']).reshape(-1,3); vpos=arr(s['positions'],'xyz')*100
    uv0=arr(s['uv0'],'xy'); mids=np.array(s['materialIds'])
    pos=np.zeros((size,size,3),np.float32); color=np.zeros((size,size,4),np.float32)
    mat=np.full((size,size),-1,np.int8)
    images=[]
    for name in s['materials']:
        im=np.array(Image.open(ROOT/'Assets/Texture'/(name+'.png')).convert('RGBA'),dtype=np.float32)/255
        im=np.flipud(im).copy()
        sigma=1.5 if name.endswith('_04') else .65 if name.endswith('_06') else .35
        smooth=cv2.GaussianBlur(im,(0,0),sigma); smooth[:,:,3]=im[:,:,3]
        images.append(smooth)
    for ti,tri in enumerate(ix):
        q=uv[tri]*size-.5
        lo=np.maximum(np.ceil(q.min(0)).astype(int),0); hi=np.minimum(np.floor(q.max(0)).astype(int),size-1)
        if np.any(hi<lo): continue
        x0,y0=lo; x1,y1=hi; yy,xx=np.mgrid[y0:y1+1,x0:x1+1]; a,b,c=q
        den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den)<1e-10: continue
        w0=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
        w1=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
        w=np.stack([w0,w1,1-w0-w1],-1).astype(np.float32); inside=np.all(w>=-1e-6,-1)
        tp=w@vpos[tri]; tuv=w@uv0[tri]; im=images[mids[ti]]; h,wid=im.shape[:2]
        sampled=cv2.remap(im,((tuv[:,:,0]%1)*wid-.5).astype(np.float32),((tuv[:,:,1]%1)*h-.5).astype(np.float32),cv2.INTER_LINEAR,borderMode=cv2.BORDER_WRAP)
        pos[y0:y1+1,x0:x1+1][inside]=tp[inside]
        color[y0:y1+1,x0:x1+1][inside]=sampled[inside]
        mat[y0:y1+1,x0:x1+1][inside]=mids[ti]
    return pos,color,mat

def paint_tiles():
    rng=np.random.default_rng(140026); crng=np.random.default_rng(90916)
    for si,(s,(ox,oy,size)) in enumerate(zip(SURFACES,RECTS)):
        ix=np.array(s['triangles']).reshape(-1,3); uv=(arr(s['uv'],'xy')*SIZE-[ox,oy])/size
        p=arr(s['positions'],'xyz'); vn=arr(s['normals'],'xyz')
        n,m,c=raster(dict(uv=uv[ix],n=vn[ix]),size)
        pos,color,mat=raster_attributes(s,uv,size)
        flow=np.zeros((size,size,2),np.float32)
        for tri in ix:
            tuv=uv[tri]; pp=p[tri]; coef=np.linalg.lstsq((pp[1:]-pp[0]).T,[0.,0.,1.],rcond=None)[0]
            d=(tuv[1:]-tuv[0]).T@coef; d=d/max(np.linalg.norm(d),1e-8)
            cv2.fillConvexPoly(flow,np.round(tuv*size-.5).astype(np.int32),(float(d[0]),float(d[1])))
        pn=n.copy(); pc=color.copy(); ys,xs=np.where(m)
        strengths=np.array([material_strength(name) for name in s['materials']])
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
                nr=pn[y0:y1,x0:x1]; nr[:]=nr*(1-alpha[...,None])+sample*alpha[...,None]
                # Exactly the same brush mask and sample location as the normal stroke.
                sample_color=color[y,x,:3].copy(); jitter=crng.normal(0,.025)
                sample_color=np.clip(sample_color*(1+jitter)+crng.normal(0,.006,3),0,1)
                same=mat[y0:y1,x0:x1]==mat[y,x]
                ac=alpha*same*strengths[max(0,mat[y,x])]
                region=pc[y0:y1,x0:x1,:3]
                region[:]=region*(1-ac[...,None])+sample_color*ac[...,None]
        np.savez(WORK/f'color_tile_{si}.npz',pos=pos[m],normal=n[m],normal_paint=unit(pn[m]),color=color[m],color_paint=pc[m],pixel=((ys+oy)*SIZE+xs+ox).astype(np.int32),mat=mat[m])
        print('SHARED STROKES',s['name'],len(xs),flush=True)
        del n,m,c,pos,color,mat,flow,pn,pc,ys,xs,sub,nr,region
        gc.collect()

def seam_segments():
    edges=defaultdict(list)
    for si,s in enumerate(SURFACES):
        if si==5: continue # Alternative masked head must not paint onto the visible face.
        p=arr(s['positions'],'xyz')*100; uv=arr(s['uv'],'xy'); ix=np.array(s['triangles']).reshape(-1,3)
        quant=np.round(p/1e-5).astype(int)
        for ti,tri in enumerate(ix):
            for ai,bi in [(0,1),(1,2),(2,0)]:
                a,b=tri[ai],tri[bi]; qa,qb=tuple(quant[a]),tuple(quant[b])
                if qa>qb: a,b=b,a; qa,qb=qb,qa
                edges[(qa,qb)].append((si,int(s['materialIds'][ti]),p[a],p[b],uv[a],uv[b]))
    result=[]
    for refs in edges.values():
        if len(refs)<2: continue
        a=refs[0]
        for b in refs[1:]:
            material=a[0]!=b[0] or a[1]!=b[1]
            uv_seam=np.max(np.abs(np.r_[a[4]-b[4],a[5]-b[5]]))>1e-5
            if material or uv_seam:
                # Face/hair contact is an anatomical border, not a cloth color seam.
                if (a[0]==4 and a[1] in [0,1]) or (b[0]==4 and b[1] in [0,1]):
                    if material: continue
                result.append((a[2],a[3],material,a[0],b[0])); break
    return result

def join_seams():
    # Compact occupied texels only: avoid constructing a full 4K position atlas.
    chunks=[]
    for si in range(6):
        z=np.load(WORK/f'color_tile_{si}.npz'); d={k:z[k] for k in z.files}; d['sid']=np.full(len(d['pos']),si,np.int8); chunks.append(d)
    fields={k:np.concatenate([d[k] for d in chunks]) for k in chunks[0]}; del chunks;gc.collect()
    p,n,pn,col,pc,pixel,sid=[fields[k] for k in ['pos','normal','normal_paint','color','color_paint','pixel','sid']]
    visible=np.flatnonzero(sid!=5)
    tree=cKDTree(p[visible],compact_nodes=True)
    rng=np.random.default_rng(8155); seam_hits=np.zeros(len(p),bool)
    boundary_weight=np.zeros(len(p),np.float32); original_pn=pn.copy()
    segments=seam_segments(); counts={'material_strokes':0,'uv_strokes':0}; pairs=defaultdict(int)
    for a,b,is_material,sa,sb in segments:
        length=np.linalg.norm(b-a)
        if length<1e-6: continue
        steps=max(1,int(np.ceil(length/(.009*SEAM_BRUSH_SCALE))))
        for t in (np.arange(steps)+.5)/steps:
            center=a*(1-t)+b*t
            near=visible[tree.query_ball_point(center,.026*SEAM_BRUSH_SCALE)]
            if len(near)<4: continue
            _,idx=tree.query(center); nearest=visible[idx]; normal=n[nearest]
            # Stamps straddle the geometric seam and are projected into both UV charts.
            tangent=unit(b-a); tangent=unit(tangent-normal*np.dot(normal,tangent))
            across=unit(np.cross(normal,tangent))
            if np.linalg.norm(across)<.5: continue
            tangent=unit(tangent+across*rng.uniform(-.8,.8)); across=unit(np.cross(normal,tangent))
            ll=rng.uniform(.022,.040); ww=rng.uniform(.009,.017) if is_material else rng.uniform(.012,.020)
            ll*=SEAM_BRUSH_SCALE; ww*=SEAM_BRUSH_SCALE
            side=rng.choice([-1,1]); sample_pos=center+across*side*ww*.25
            _,source_idx=tree.query(sample_pos); source=visible[source_idx]
            if np.dot(n[source],normal)<.75: source=nearest
            delta=p[near]-center
            u=delta@tangent/(ll*.5); v=delta@across/(ww*.5); depth=np.abs(delta@normal)
            phase=rng.uniform(0,6.28)
            edge=.8+.12*np.sin(u*11+phase)+.06*np.sin(u*29+phase)
            alpha=np.clip(np.minimum((.92-np.abs(u))*10,(edge-np.abs(v))*10),0,1).astype(np.float32)
            alpha*=(depth<.005)&(n[near]@normal>.85)&(sid[near]!=5)
            # Bound the source-to-target deviation, not just each one's relation
            # to the seam center: two individually valid vectors can differ by 70°.
            alpha*=(n[near]@n[source]>.88)
            # Keep pixels with discarded alpha (hair cards) and sharp fold backs out.
            alpha*=col[near,3]>.8
            if is_material:
                # Cloth edges may cross hood panels, but not skin or hair.
                alpha*=~((sid[near]==4)&(fields['mat'][near]<2))
            if not np.any(alpha): continue
            pn[near]=unit(pn[near]*(1-alpha[:,None])+n[source]*alpha[:,None])
            strength=.96 if is_material else .88
            color_alpha=alpha*strength
            paint_color=pc[source,:3].copy()
            pc[near,:3]=pc[near,:3]*(1-color_alpha[:,None])+paint_color*color_alpha[:,None]
            seam_hits[near[alpha>.1]]=True
            if is_material: boundary_weight[near]=np.maximum(boundary_weight[near],alpha)
            counts['material_strokes' if is_material else 'uv_strokes']+=1
            if is_material: pairs[f'{SURFACES[sa]["name"]} / {SURFACES[sb]["name"]}']+=1
    del tree,visible; gc.collect()
    # Preserve semantic detail after overlapping seam stamps. Cloth boundaries stay
    # fully painted, while eyes/lips and web patterns retain their original shapes.
    for si,s in enumerate(SURFACES):
        for mi,name in enumerate(s['materials']):
            keep=.85 if 'face' in name else .30 if 'hair' in name else .65 if name.endswith('_05') else .25 if name.endswith('_01') else 0.
            if not keep: continue
            selected=np.flatnonzero((sid==si)&(fields['mat']==mi))
            blend=keep*(1-boundary_weight[selected])
            pc[selected,:3]=pc[selected,:3]*(1-blend[:,None])+col[selected,:3]*blend[:,None]
            if 'face' in name:
                pn[selected]=unit(original_pn[selected]*.85+pn[selected]*.15)
    del original_pn;gc.collect()
    # Final border dilation is isolated per tile and follows the new seam edits.
    normal_atlas=np.memmap(WORK/'joint_normal_u16.bin',mode='w+',dtype=np.uint16,shape=(SIZE,SIZE,3));normal_atlas[:]=[32768,32768,65535]
    color_atlas=np.memmap(WORK/'joint_color_u8.bin',mode='w+',dtype=np.uint8,shape=(SIZE,SIZE,4));color_atlas[:]=0
    for si,(ox,oy,size) in enumerate(RECTS):
        select=sid==si; pix=pixel[select]; x=pix%SIZE-ox; y=pix//SIZE-oy
        m=np.zeros((size,size),bool); m[y,x]=True
        nt=np.zeros((size,size,3),np.float32);nt[:]=[0,0,1];nt[y,x]=pn[select]
        ct=np.zeros((size,size,4),np.float32);ct[y,x]=pc[select]
        dist,near=distance_transform_edt(~m,return_indices=True); pad=(~m)&(dist<=20)
        for tile in (nt,ct): tile[pad]=tile[near[0][pad],near[1][pad]]
        normal_atlas[oy:oy+size,ox:ox+size]=np.round(np.clip(nt*.5+.5,0,1)*65535).astype(np.uint16)
        color_atlas[oy:oy+size,ox:ox+size]=np.round(np.clip(ct,0,1)*255).astype(np.uint8)
        del nt,ct,m,dist,near,pad,tile;gc.collect()
    cv2.imwrite(str(OUT/'Gwen_Painterly_NormalOS_4K.png'),np.ascontiguousarray(np.flipud(normal_atlas)[...,::-1]))
    Image.fromarray(np.flipud(color_atlas)).save(OUT/'Gwen_Painterly_BaseColor_4K.png')
    Image.fromarray(np.flipud(color_atlas[::4,::4])).save(OUT/'BaseColorAtlasPreview.png')
    Image.fromarray(np.flipud((normal_atlas[::4,::4]/257).astype(np.uint8))).save(OUT/'AtlasPreview.png')
    report=dict(resolution=SIZE,brush_scale=BRUSH_SCALE,seam_brush_scale=SEAM_BRUSH_SCALE,shared_color_normal_masks=True,seam_segments=len(segments),seam_texels=int(seam_hits.sum()),**counts,material_seams=dict(pairs),normal_length_max_error=float(np.max(np.abs(np.linalg.norm(pn,axis=-1)-1))),alpha_preserved=True)
    (OUT/'color_validation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2),flush=True)

if __name__=='__main__':
    import sys
    if '--seams-only' not in sys.argv: paint_tiles()
    join_seams()
