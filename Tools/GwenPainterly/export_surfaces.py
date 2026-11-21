import bpy, numpy as np, json
from pathlib import Path
root=Path(r'F:/Projections/SpiderVerse'); work=root/'Tools/GwenPainterly/work'
bpy.ops.wm.open_mainfile(filepath=str(work/'source.blend'))
manifest=[]
for obj in bpy.data.objects:
    if obj.type!='MESH' or not (obj.name.startswith('hero_spidergwen') or obj.name in ['idle head','wait head']): continue
    mesh=obj.data
    mesh.calc_loop_triangles(); mesh.calc_tangents(uvmap=mesh.uv_layers.active.name)
    for mi,slot in enumerate(obj.material_slots):
        triangles=[t for t in mesh.loop_triangles if t.material_index==mi]
        key=obj.name.replace(' ','_')+'__'+slot.name
        loops=[list(t.loops) for t in triangles]
        uv=np.array([[mesh.uv_layers.active.data[l].uv[:] for l in tri] for tri in loops],dtype=np.float32)
        pos=np.array([[mesh.vertices[mesh.loops[l].vertex_index].co[:] for l in tri] for tri in loops],dtype=np.float32)
        n=np.array([[mesh.corner_normals[l].vector[:] for l in tri] for tri in loops],dtype=np.float32)
        t=np.array([[mesh.loops[l].tangent[:] for l in tri] for tri in loops],dtype=np.float32)
        b=np.array([[mesh.loops[l].bitangent[:] for l in tri] for tri in loops],dtype=np.float32)
        np.savez_compressed(work/(key+'.npz'),uv=uv,pos=pos,n=n,t=t,b=b)
        manifest.append(dict(key=key,object=obj.name,material=slot.name,triangles=len(triangles),matrix=[list(r) for r in obj.matrix_world]))
(work/'surfaces.json').write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest,indent=2))
