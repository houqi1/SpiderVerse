"""Update the existing author project with the project's Spider-Gwen FBX."""
import bpy, json, hashlib
from pathlib import Path
from mathutils import Vector

PROJECT=Path('C:/Users/win11/Downloads/Lassyla_Material (1).blend')
FBX=Path('F:/Projections/SpiderVerse/Assets/Model/Spider-Gwen.fbx')
ROOT=Path(__file__).resolve().parent
print('Opening the existing material project',flush=True)
bpy.ops.wm.open_mainfile(filepath=str(PROJECT),load_ui=True,use_scripts=False)
scene=bpy.context.scene
material=bpy.data.materials['Lassyla Material']
old_objects=set(bpy.data.objects)
assert not any(o.get('imported_for_lassyla') for o in old_objects),'Gwen already imported; inspect before rerunning'
print('Importing Spider-Gwen.fbx',flush=True)
bpy.ops.import_scene.fbx(filepath=str(FBX),use_anim=False)
new_objects=[o for o in bpy.data.objects if o not in old_objects]
meshes=[o for o in new_objects if o.type=='MESH']
assert meshes
for o in new_objects:
    o['imported_for_lassyla']=True
    if o.type=='ARMATURE':o.data.pose_position='REST'
for o in meshes:
    o.data.materials.clear();o.data.materials.append(material)
    for polygon in o.data.polygons:polygon.material_index=0
    if o.name=='wait head' or not ('body' in o.name.lower() or o.name=='idle head'):
        o.hide_set(True);o.hide_render=True
for o in old_objects:
    if o.type=='MESH':o.hide_set(True);o.hide_render=True
bpy.context.view_layer.update()
visible=[o for o in meshes if not o.hide_render]
pts=[o.matrix_world@Vector(c) for o in visible for c in o.bound_box]
lo=Vector(tuple(min(p[i] for p in pts) for i in range(3)))
hi=Vector(tuple(max(p[i] for p in pts) for i in range(3)))
center=(lo+hi)*.5
direction=Vector((.8,-4.5,1.1))
rotation=(-direction).to_track_quat('-Z','Y')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active;space.shading.type='RENDERED'
            space.overlay.show_overlays=False
            space.region_3d.view_location=center
            space.region_3d.view_distance=max(3.0,(hi-lo).length*1.15)
            space.region_3d.view_rotation=rotation
            space.region_3d.view_perspective='PERSP'
for o in bpy.context.selected_objects:o.select_set(False)
for o in visible:o.select_set(True)
bpy.context.view_layer.objects.active=next((o for o in visible if o.name=='idle head'),visible[0])
scene.render.engine='BLENDER_EEVEE'
# Save to the SAME project. Blender retains the preceding file as .blend1.
bpy.context.preferences.filepaths.save_version=max(1,bpy.context.preferences.filepaths.save_version)
bpy.ops.wm.save_as_mainfile(filepath=str(PROJECT))
report={'project':str(PROJECT),'fbx':str(FBX),'fbxSHA256':hashlib.sha256(FBX.read_bytes()).hexdigest(),'material':material.name,'allImportedMeshes':[o.name for o in meshes],'visibleMeshes':[o.name for o in visible],'bounds':[list(lo),list(hi)],'nodeCount':len(material.node_tree.nodes),'linkCount':len(material.node_tree.links)}
(ROOT/'gwen-import.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report),flush=True)
