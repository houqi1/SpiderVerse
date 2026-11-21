"""Create native Blender node / mesh evidence, without editing the supplied .blend."""
import bpy
import bmesh
import json
import math
import random
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Lassyla_Material.blend'),load_ui=False,use_scripts=False)
source=bpy.data.materials['Lassyla Material']
audit=json.loads((ROOT/'material-audit.json').read_text(encoding='utf-8'))

def inp(node,name): return node.inputs[name].default_value
configs={}
for material in bpy.data.materials:
    if not material.name.startswith('Lassyla Material'): continue
    nodes=material.node_tree.nodes
    configs[material.name]={
      'ior':inp(nodes['Fresnel'],'IOR'),
      'noise':{name:inp(nodes['Noise Texture'],name) for name in ['Scale','Detail','Roughness','Lacunarity','Distortion']},
      'edgeRamp':[{'position':e.position,'color':list(e.color)}for e in nodes['ColorRamp.003'].color_ramp.elements],
      'noiseRamp':[{'position':e.position,'color':list(e.color)}for e in nodes['ColorRamp.004'].color_ramp.elements],
      'coordinate':'Generated','renderMethod':material.surface_render_method,
      'backfaceCulling':material.use_backface_culling,
    }

# Export evaluated original meshes, including split normals and Generated coordinates.
meshes={}
for o in bpy.data.objects:
    for modifier in o.modifiers:
        if modifier.type=='SUBSURF':modifier.levels=modifier.render_levels
bpy.context.view_layer.update()
depsgraph=bpy.context.evaluated_depsgraph_get()
for name in ['Sphere','Cube','Suzanne']:
    original=bpy.data.objects[name]
    obj=original.evaluated_get(depsgraph);mesh=obj.to_mesh();mesh.calc_loop_triangles()
    # The original mesh texture space provides the automatic Generated coordinate transform.
    loc=original.data.texspace_location.copy();size=original.data.texspace_size.copy()
    positions=[];normals=[];generated=[]
    for tri in mesh.loop_triangles:
        for li in tri.loops:
            loop=mesh.loops[li];co=mesh.vertices[loop.vertex_index].co
            normal=mesh.corner_normals[li].vector
            positions.extend(co);normals.extend(normal)
            generated.extend([(co[i]-loc[i])/(2*size[i])+.5 if size[i] else .5 for i in range(3)])
    meshes[name]={'positions':positions,'normals':normals,'generated':generated,
                  'textureSpaceLocation':list(loc),'textureSpaceSize':list(size),
                  'material':original.material_slots[0].material.name}
    obj.to_mesh_clear()

W,H=64,32;COUNT=W*H;rng=random.Random(7429)
samples=[]
for i in range(COUNT):
    if i<COUNT//2: p=[rng.random() for _ in range(3)]
    else: p=[rng.uniform(-3,3) for _ in range(3)]
    c=0.001+0.999*((i+0.5)/COUNT)
    samples.append([*p,c])

# Native Geometry Nodes evaluate Noise Texture and its Color Ramp in float32.
native_mesh=bpy.data.meshes.new('ReferenceSamples');native_mesh.from_pydata([s[:3]for s in samples],[],[])
native_obj=bpy.data.objects.new('ReferenceSamples',native_mesh);bpy.context.collection.objects.link(native_obj)
group=bpy.data.node_groups.new('ExactNativeNoiseReference','GeometryNodeTree')
group.interface.new_socket(name='Geometry',in_out='INPUT',socket_type='NodeSocketGeometry')
group.interface.new_socket(name='Geometry',in_out='OUTPUT',socket_type='NodeSocketGeometry')
nodes=group.nodes;links=group.links
gi=nodes.new('NodeGroupInput');go=nodes.new('NodeGroupOutput');pos=nodes.new('GeometryNodeInputPosition')
noise=nodes.new('ShaderNodeTexNoise');noise.noise_dimensions='3D';noise.noise_type='FBM';noise.normalize=True
for k,v in configs['Lassyla Material']['noise'].items(): noise.inputs[k].default_value=v
links.new(pos.outputs['Position'],noise.inputs['Vector'])
ramp=nodes.new('ShaderNodeValToRGB');ramp.color_ramp.interpolation='LINEAR'
for e,ref in zip(ramp.color_ramp.elements,configs['Lassyla Material']['noiseRamp']):e.position=ref['position'];e.color=ref['color']
links.new(noise.outputs['Fac'],ramp.inputs['Fac'])
geometry=gi.outputs['Geometry']
for name,output in [('native_noise',noise.outputs['Fac']),('native_noise_ramp',ramp.outputs['Color'])]:
    store=nodes.new('GeometryNodeStoreNamedAttribute');store.data_type='FLOAT';store.domain='POINT';store.inputs['Name'].default_value=name
    links.new(geometry,store.inputs['Geometry']);links.new(output,store.inputs['Value']);geometry=store.outputs['Geometry']
links.new(geometry,go.inputs['Geometry']);mod=native_obj.modifiers.new('ReferenceNodes','NODES');mod.node_group=group
bpy.context.view_layer.update();evaluated=native_obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
native_noise=[v.value for v in evaluated.attributes['native_noise'].data]
native_ramp=[v.value for v in evaluated.attributes['native_noise_ramp'].data]

# Native EEVEE evaluates the original Noise, Fresnel and final mask nodes.
for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=W;scene.render.resolution_y=H;scene.render.resolution_percentage=100
scene.render.film_transparent=True;scene.render.filter_size=0.01
scene.view_settings.view_transform='Raw';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
if hasattr(scene,'eevee') and hasattr(scene.eevee,'taa_render_samples'):scene.eevee.taa_render_samples=1
scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_depth='32';scene.render.image_settings.color_mode='RGBA'
plane_mesh=bpy.data.meshes.new('ReferencePlane');plane_mesh.from_pydata([(-1,-.5,0),(1,-.5,0),(1,.5,0),(-1,.5,0)],[],[(0,1,2,3)])
plane=bpy.data.objects.new('ReferencePlane',plane_mesh);scene.collection.objects.link(plane)
uv=plane_mesh.uv_layers.new(name='UVMap')
for loop,co in zip(uv.data,[(0,0),(1,0),(1,1),(0,1)]):loop.uv=co
camera_data=bpy.data.cameras.new('ReferenceCamera');camera=bpy.data.objects.new('ReferenceCamera',camera_data);scene.collection.objects.link(camera)
camera.location=(0,0,3);camera_data.type='ORTHO';camera_data.ortho_scale=2;scene.camera=camera
mat=source.copy();mat.name='NativeEdgeReference';plane.data.materials.append(mat);mat.surface_render_method='DITHERED'
nodes=mat.node_tree.nodes;links=mat.node_tree.links
def image_node(name,pixels):
    image=bpy.data.images.new(name,width=W,height=H,alpha=True,float_buffer=True)
    image.colorspace_settings.name='Non-Color';image.use_half_precision=False;image.pixels.foreach_set(pixels);image.update()
    n=nodes.new('ShaderNodeTexImage');n.image=image;n.interpolation='Closest';return n
positions=image_node('ReferencePositions',[v for s in samples for v in [*s[:3],1]])
normals=image_node('ReferenceNormals',[v for s in samples for v in [math.sqrt(1-s[3]*s[3]),0,s[3],1]])
links.new(positions.outputs['Color'],nodes['Noise Texture'].inputs['Vector'])
links.new(normals.outputs['Color'],nodes['Fresnel'].inputs['Normal'])
combine=nodes.new('ShaderNodeCombineXYZ')
links.new(nodes['Noise Texture'].outputs['Fac'],combine.inputs['X'])
links.new(nodes['Fresnel'].outputs['Fac'],combine.inputs['Y'])
links.new(nodes['Mix.006'].outputs['Result'],combine.inputs['Z'])
emission=nodes.new('ShaderNodeEmission');links.new(combine.outputs[0],emission.inputs['Color']);links.new(emission.outputs[0],nodes['Material Output'].inputs['Surface'])
scene.render.filepath=str(ROOT/'native-node-reference.exr');bpy.ops.render.render(write_still=True)
image=bpy.data.images.load(scene.render.filepath,check_existing=False);pixels=list(image.pixels)
reference=[pixels[i*4:i*4+4]for i in range(COUNT)]
bm=bmesh.new();bm.from_mesh(plane_mesh);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(plane_mesh);bm.free();plane_mesh.update()
scene.render.filepath=str(ROOT/'native-backface-reference.exr');bpy.ops.render.render(write_still=True)
back_image=bpy.data.images.load(scene.render.filepath,check_existing=False);back_pixels=list(back_image.pixels)
back_reference=[back_pixels[i*4:i*4+4]for i in range(COUNT)]
result={'sourceSHA256':audit['sha256'],'savedVersion':audit['saved_version'],'renderer':bpy.app.version_string,
        'commit':bpy.app.build_hash.decode(),'configs':configs,'width':W,'height':H,'samples':samples,
        'nativeNoise':native_noise,'nativeNoiseRamp':native_ramp,'nativeEEVEE':reference,'nativeBackface':back_reference}
(ROOT/'edge-reference.json').write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
(ROOT/'source-meshes.json').write_text(json.dumps(meshes,separators=(',',':')),encoding='utf-8')
print('REFERENCE_DONE',COUNT,'samples',[(n,len(m['positions'])//9)for n,m in meshes.items()])
