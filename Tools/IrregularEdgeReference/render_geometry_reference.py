"""Render native EEVEE signals on the untouched author's meshes for pixel probes."""
import bpy,json,math,struct
from pathlib import Path
R=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(R/'Lassyla_Material.blend'),load_ui=False,use_scripts=False)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=256;scene.render.resolution_y=256;scene.render.resolution_percentage=100
scene.render.film_transparent=True;scene.render.filter_size=0.01
scene.view_settings.view_transform='Raw';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
scene.eevee.taa_render_samples=1
scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_depth='32';scene.render.image_settings.color_mode='RGBA'
camera_data=bpy.data.cameras.new('GeometryReferenceCamera');camera=bpy.data.objects.new('GeometryReferenceCamera',camera_data);scene.collection.objects.link(camera)
camera_data.type='PERSP';camera_data.sensor_fit='HORIZONTAL';camera_data.sensor_width=36;camera_data.lens=18/math.tan(math.radians(16));scene.camera=camera
for o in scene.objects:o.hide_render=True
camera.hide_render=False
meshes=json.loads((R/'source-meshes.json').read_text());result={}
for name in ['Sphere','Cube','Suzanne']:
    obj=bpy.data.objects[name];obj.hide_render=False;obj.location=(0,0,0);obj.rotation_euler=(0,0,0);obj.scale=(1,1,1)
    positions=meshes[name]['positions'];radius=max(math.sqrt(sum(x*x for x in positions[i:i+3]))for i in range(0,len(positions),3))
    camera.location=(0,0,4.3*radius);camera.rotation_euler=(0,0,0)
    original=obj.material_slots[0].material;mat=original.copy();mat.name='GeometryReference_'+name;obj.material_slots[0].material=mat
    nodes=mat.node_tree.nodes;links=mat.node_tree.links;combine=nodes.new('ShaderNodeCombineXYZ');emission=nodes.new('ShaderNodeEmission')
    links.new(nodes['Noise Texture'].outputs['Fac'],combine.inputs['X']);links.new(nodes['Fresnel'].outputs['Fac'],combine.inputs['Y']);links.new(nodes['Mix.006'].outputs['Result'],combine.inputs['Z'])
    links.new(combine.outputs[0],emission.inputs['Color']);links.new(emission.outputs[0],nodes['Material Output'].inputs['Surface'])
    scene.render.filepath=str(R/(name+'-native-signals.exr'));bpy.ops.render.render(write_still=True)
    im=bpy.data.images.load(scene.render.filepath,check_existing=False);pixels=list(im.pixels)
    probes=[]
    for y in range(3,253,5):
        for x in range(3,253,5):
            i=(y*256+x)*4
            if pixels[i+3]>.99:probes.append([x,y,*pixels[i:i+3]])
    result[name]={'width':256,'height':256,'radius':radius,'material':original.name,'probes':probes}
    # Keep the complete float image for independent inspection, outside the HTML payload.
    (R/(name+'-native-signals.f32')).write_bytes(struct.pack('<'+'f'*len(pixels),*pixels))
    obj.hide_render=True
(R/'geometry-reference.json').write_text(json.dumps(result,separators=(',',':')))
print('GEOMETRY_REFERENCE',[(n,len(r['probes']))for n,r in result.items()])
