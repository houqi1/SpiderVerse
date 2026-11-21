import bpy, json
from pathlib import Path
root = Path(r'F:/Projections/SpiderVerse')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root/'Assets/Model/Spider-Gwen.fbx'))
info=[]
for obj in bpy.data.objects:
    if obj.type != 'MESH': continue
    uv=obj.data.uv_layers.active
    coords=[tuple(v.uv) for v in uv.data] if uv else []
    info.append(dict(name=obj.name, verts=len(obj.data.vertices), faces=len(obj.data.polygons), materials=[s.name for s in obj.material_slots], uv_layers=[u.name for u in obj.data.uv_layers], uv_min=[min(x[i] for x in coords) for i in range(2)] if coords else None, uv_max=[max(x[i] for x in coords) for i in range(2)] if coords else None, bounds=[list(obj.matrix_world @ __import__('mathutils').Vector(c)) for c in obj.bound_box]))
out=root/'Tools/GwenPainterly/work'
out.mkdir(parents=True,exist_ok=True)
(out/'model_info.json').write_text(json.dumps(info,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'source.blend'))
print(json.dumps(info,indent=2))
