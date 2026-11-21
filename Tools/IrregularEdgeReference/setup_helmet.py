"""Import Khronos DamagedHelmet and assign the author's unchanged material."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Lassyla_Material.blend'),load_ui=False,use_scripts=False)
scene=bpy.context.scene
original_objects=set(bpy.data.objects)
for obj in original_objects:
    if obj.type=='MESH':
        obj.hide_render=True
        obj.hide_set(True)
bpy.ops.import_scene.gltf(filepath=str(ROOT/'DamagedHelmet'/'DamagedHelmet.glb'))
meshes=[o for o in bpy.data.objects if o not in original_objects and o.type=='MESH']
assert meshes,'GLB import did not produce mesh objects'
material=bpy.data.materials['Lassyla Material']
for obj in meshes:
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for face in obj.data.polygons:face.material_index=0
    obj.name='DamagedHelmet_Lassyla'
    obj['source_model']='Khronos glTF Sample Assets / DamagedHelmet'
    obj['material_setup']='Original Lassyla Material, unchanged node tree and values'
bpy.context.view_layer.update()
points=[obj.matrix_world@Vector(v) for obj in meshes for v in obj.bound_box]
lo=Vector(tuple(min(p[i] for p in points) for i in range(3)))
hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
center=(lo+hi)*.5
cam_data=bpy.data.cameras.new('HelmetPreviewCamera')
camera=bpy.data.objects.new('HelmetPreviewCamera',cam_data)
scene.collection.objects.link(camera);scene.camera=camera
camera.location=center+Vector((2.7,-5.6,1.9))
camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
cam_data.lens=55
scene.render.engine='BLENDER_EEVEE'
scene.eevee.taa_render_samples=128
scene.eevee.taa_samples=64
scene.render.resolution_x=1000;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
scene.render.filepath=str(ROOT/'DamagedHelmet_Lassyla.png')
for obj in bpy.context.selected_objects:obj.select_set(False)
for obj in meshes:obj.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active
            space.shading.type='RENDERED'
            space.overlay.show_overlays=False
            space.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
            space.region_3d.view_location=center
            space.region_3d.view_distance=5.2
            space.region_3d.view_perspective='PERSP'
        elif area.type=='NODE_EDITOR':
            area.spaces.active.pin=False
if bpy.context.window and 'Layout' in bpy.data.workspaces:
    bpy.context.window.workspace=bpy.data.workspaces['Layout']
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'DamagedHelmet_Lassyla.blend'))
bpy.ops.render.render(write_still=True)
summary={'blend':str(ROOT/'DamagedHelmet_Lassyla.blend'),'preview':scene.render.filepath,'material':material.name,'objects':[{'name':o.name,'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)}for o in meshes],'bounds':[list(lo),list(hi)],'sourceMaterialUnchanged':True}
(ROOT/'DamagedHelmet'/'setup.json').write_text(json.dumps(summary,indent=2))
print(json.dumps(summary))
