"""Native one-sample visibility reference, preserving the author's transparent closure."""
import bpy, json, math, base64
from pathlib import Path
R=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(R/'Lassyla_Material.blend'),load_ui=False,use_scripts=False)
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=256;scene.render.resolution_y=256;scene.render.resolution_percentage=100
scene.render.film_transparent=True;scene.render.filter_size=.01
scene.view_settings.view_transform='Raw';scene.view_settings.look='None'
scene.eevee.taa_render_samples=1
scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_depth='32';scene.render.image_settings.color_mode='RGBA'
cam=bpy.data.cameras.new('AlphaReferenceCamera');camera=bpy.data.objects.new('AlphaReferenceCamera',cam);scene.collection.objects.link(camera);scene.camera=camera
cam.type='PERSP';cam.sensor_fit='HORIZONTAL';cam.sensor_width=36;cam.lens=18/math.tan(math.radians(16))
for obj in scene.objects:obj.hide_render=True
camera.hide_render=False
geometry=json.loads((R/'geometry-reference.json').read_text());result={}
for name,ref in geometry.items():
    obj=bpy.data.objects[name];obj.hide_render=False;obj.location=(0,0,0);obj.rotation_euler=(0,0,0);obj.scale=(1,1,1)
    camera.location=(0,0,4.3*ref['radius']);camera.rotation_euler=(0,0,0)
    mat=obj.material_slots[0].material.copy();obj.material_slots[0].material=mat
    nodes=mat.node_tree.nodes;links=mat.node_tree.links
    emission=nodes.new('ShaderNodeEmission');emission.inputs['Color'].default_value=(1,1,1,1)
    links.new(emission.outputs[0],nodes['Mix Shader.001'].inputs[1])
    scene.render.filepath=str(R/(name+'-native-alpha.exr'));bpy.ops.render.render(write_still=True)
    im=bpy.data.images.load(scene.render.filepath,check_existing=False);pixels=list(im.pixels)
    alpha=bytes(round(min(1,max(0,pixels[i]))*255) for i in range(3,len(pixels),4))
    result[name]={'width':256,'height':256,'samples':1,'alpha':base64.b64encode(alpha).decode()}
    obj.hide_render=True
(R/'alpha-reference.json').write_text(json.dumps(result,separators=(',',':')))
print('ALPHA_REFERENCE',list(result))
