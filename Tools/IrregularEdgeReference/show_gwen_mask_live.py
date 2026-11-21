"""Run inside the currently open Blender project; preserve live material edits."""
import bpy
from mathutils import Vector
mask=bpy.data.objects['wait head']
unmasked=bpy.data.objects['idle head']
material=unmasked.data.materials[0]
mask.hide_set(False)
mask.hide_render=False
for i in range(len(mask.data.materials)):
    mask.data.materials[i]=material
if not mask.data.materials:
    mask.data.materials.append(material)
for polygon in mask.data.polygons:
    polygon.material_index=0
unmasked.hide_set(True)
unmasked.hide_render=True
for obj in bpy.context.selected_objects:
    obj.select_set(False)
mask.select_set(True)
bpy.context.view_layer.objects.active=mask
nodes=material.node_tree.nodes
labels={'Fresnel':'边缘视角 · IOR','Noise Texture':'缺口尺寸 · Scale','ColorRamp.003':'边缘范围 · 黑白位置','ColorRamp.004':'缺口比例 · 黑白位置'}
for node in nodes:
    node.select=node.name in labels
    if node.name in labels:
        node.label=labels[node.name]
nodes.active=nodes['Noise Texture']
# Reframe the visible head and upper body after changing the head mesh.
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        space=area.spaces.active
        space.shading.type='RENDERED'
        space.region_3d.view_location=Vector((0,0,1.43))
        space.region_3d.view_distance=1.65
        space.region_3d.view_rotation=Vector((-.35,4.5,-.55)).to_track_quat('-Z','Y')
        space.region_3d.view_perspective='PERSP'
print('MASK_READY:',mask.name,'MATERIAL:',material.name,'edge controls selected; live values preserved')
