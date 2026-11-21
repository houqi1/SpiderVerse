"""Run by the local HTML server inside Blender. Never saves the source blend."""
import bpy, json, sys, math, hashlib
from pathlib import Path
from mathutils import Matrix
R=Path(__file__).resolve().parent
job_path=Path(sys.argv[sys.argv.index('--')+1]);job=json.loads(job_path.read_text())
bpy.ops.wm.open_mainfile(filepath=str(R/'Lassyla_Material.blend'),load_ui=False,use_scripts=False)
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=job['width'];scene.render.resolution_y=job['height'];scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.eevee.taa_render_samples=64
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='8'
obj=bpy.data.objects[job['shape']]
for other in scene.objects:
    if other.type=='MESH':other.hide_render=other!=obj
obj.location=(0,0,0);obj.rotation_mode='QUATERNION'
obj.rotation_quaternion=(Matrix.Rotation(job['yaw'],4,'Y')@Matrix.Rotation(job['pitch'],4,'X')).to_quaternion()
obj.scale=(1,1,1)
mat=bpy.data.materials[job['material']].copy();obj.material_slots[0].material=mat
nodes=mat.node_tree.nodes;links=mat.node_tree.links;p=job['parameters']
nodes['Fresnel'].inputs['IOR'].default_value=p['ior']
nodes['Noise Texture'].inputs['Scale'].default_value=p['scale']
nodes['ColorRamp.003'].color_ramp.elements[0].position=p['edgeStart']
nodes['ColorRamp.003'].color_ramp.elements[1].position=p['edgeEnd']
nodes['ColorRamp.004'].color_ramp.elements[0].position=p['noiseStart']
nodes['ColorRamp.004'].color_ramp.elements[1].position=p['noiseEnd']
cam=bpy.data.cameras.new('HTMLPreviewCamera');camera=bpy.data.objects.new('HTMLPreviewCamera',cam);scene.collection.objects.link(camera);scene.camera=camera
cam.type='PERSP';cam.sensor_fit='VERTICAL';cam.sensor_height=36;cam.lens=18/math.tan(math.radians(16))
ref=json.loads((R/'geometry-reference.json').read_text())[job['shape']]
aspect=job['width']/job['height']
camera.location=(0,0,max(4.3,4.3/aspect)*ref['radius']/job['zoom']);camera.rotation_euler=(0,0,0)
output=nodes['Material Output'].inputs['Surface']
if job['mode']!=0:
    emission=nodes.new('ShaderNodeEmission')
    signal={1:nodes['ColorRamp.003'].outputs['Color'],2:nodes['Noise Texture'].outputs['Fac'],3:nodes['Mix.006'].outputs['Result']}[job['mode']]
    links.new(signal,emission.inputs['Color']);links.new(emission.outputs[0],output)
scene.render.filepath=str(job_path.parent/'right.png');bpy.ops.render.render(write_still=True)
if job['compare']:
    # Only bypass the last transparent Mix Shader for the left-hand comparison.
    links.new(nodes['Mix Shader.001'].inputs[1].links[0].from_socket,output)
    scene.render.filepath=str(job_path.parent/'left.png');bpy.ops.render.render(write_still=True)
metadata={'engine':'BLENDER_EEVEE','blenderVersion':bpy.app.version_string,'commit':bpy.app.build_hash.decode(),'sourceSHA256':hashlib.sha256((R/'Lassyla_Material.blend').read_bytes()).hexdigest(),'sourceMaterial':job['material'],'samples':64,'filmFilterSize':scene.render.filter_size,'viewTransform':scene.view_settings.view_transform,'look':scene.view_settings.look,'materialRenderMethod':mat.surface_render_method,'backfaceCulling':mat.use_backface_culling,'fullOriginalMaterial':True,'nativeFilmAndDisplay':True,'request':job}
(job_path.parent/'metadata.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')

