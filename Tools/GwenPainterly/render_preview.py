import bpy,json,math,sys
from pathlib import Path
from mathutils import Matrix,Vector
ROOT=Path(r'F:/Projections/SpiderVerse'); WORK=ROOT/'Tools/GwenPainterly/work'
bpy.ops.wm.read_factory_settings(use_empty=True)
surfaces=json.loads((WORK/'atlas_meshes.json').read_text())['surfaces']
paint=bpy.data.images.load(str(ROOT/'Assets/Texture/GwenPainterly/Gwen_Painterly_NormalOS_4K.png'))
paint.colorspace_settings.name='Non-Color'
base=paint if '--after-only' in sys.argv else bpy.data.images.load(str(WORK/'Gwen_NormalOS_Base.png'))
base.colorspace_settings.name='Non-Color'
normal_nodes=[]
C=Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)))
for s in surfaces:
    if s['name']=='wait head': continue
    p=[tuple(v[k] for k in 'xyz') for v in s['positions']]
    ns=[tuple(v[k] for k in 'xyz') for v in s['normals']]
    tri=[s['triangles'][i:i+3] for i in range(0,len(s['triangles']),3)]
    mesh=bpy.data.meshes.new(s['name']); mesh.from_pydata(p,[],tri); mesh.update()
    obj=bpy.data.objects.new(s['name'],mesh); bpy.context.collection.objects.link(obj)
    obj.matrix_world=C@Matrix([[s['localToWorld'][f'e{r}{c}'] for c in range(4)] for r in range(4)])
    for poly in mesh.polygons: poly.use_smooth=True
    mesh.normals_split_custom_set_from_vertices(ns)
    for name,values in [('UV0',s['uv0']),('NormalUV',s['uv'])]:
        layer=mesh.uv_layers.new(name=name)
        for loop in mesh.loops:
            v=values[loop.vertex_index]; layer.data[loop.index].uv=(v['x'],v['y'])
    for name in s['materials']:
        mat=bpy.data.materials.new(name); mat.use_nodes=True; nodes=mat.node_tree.nodes; links=mat.node_tree.links
        bsdf=nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value=.72
        colorfile=(ROOT/'Assets/Texture/GwenPainterly/Gwen_Painterly_BaseColor_4K.png') if '--paint-color' in sys.argv else ROOT/'Assets/Texture'/(name+'.png')
        if colorfile.exists():
            tex=nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(str(colorfile),check_existing=True)
            uv=nodes.new('ShaderNodeUVMap'); uv.uv_map='NormalUV' if '--paint-color' in sys.argv else 'UV0'; links.new(uv.outputs['UV'],tex.inputs['Vector']); links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
            if 'hair' in name: links.new(tex.outputs['Alpha'],bsdf.inputs['Alpha'])
        tex=nodes.new('ShaderNodeTexImage'); tex.image=paint; normal_nodes.append(tex)
        uv=nodes.new('ShaderNodeUVMap'); uv.uv_map='NormalUV'; links.new(uv.outputs['UV'],tex.inputs['Vector'])
        normal=nodes.new('ShaderNodeNormalMap'); normal.space='OBJECT'
        links.new(tex.outputs['Color'],normal.inputs['Color']); links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
        mesh.materials.append(mat)
    for poly,mi in zip(mesh.polygons,s['materialIds']): poly.material_index=mi

scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=16
scene.cycles.use_denoising=True; scene.render.threads_mode='FIXED'; scene.render.threads=6
scene.render.resolution_x=1000; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Studio'); scene.world.color=(.1,.1,.1); scene.view_settings.view_transform='AgX'
def point(obj,target): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(2.1,-4.5,2.0)); cam=bpy.context.object; point(cam,(0,0,.85)); cam.data.type='ORTHO';cam.data.ortho_scale=1.95;scene.camera=cam
for name,loc,power,size,col in [('Key',(-2,-3,4),380,1.8,(1,.88,.76)),('Fill',(2,-1,2),90,2,(.65,.8,1)),('Rim',(0,2,3),250,1.5,(.7,.8,1))]:
    bpy.ops.object.light_add(type='AREA',location=loc); light=bpy.context.object; light.name=name; light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.data.color=col;point(light,(0,0,1))
scene.render.film_transparent=True
for mode,img in [('before',base),('after',paint)]:
    if '--after-only' in sys.argv and mode=='before': continue
    for node in normal_nodes: node.image=img
    scene.render.filepath=str(WORK/(mode+'.png')); bpy.ops.render.render(write_still=True)
cam.location=(1.6,-4,1.8); point(cam,(0,0,1.13));cam.data.ortho_scale=.85
for mode,img in [('detail_before',base),('detail_after',paint)]:
    if '--after-only' in sys.argv and mode=='detail_before': continue
    for node in normal_nodes: node.image=img
    scene.render.filepath=str(WORK/(mode+'.png')); bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(WORK/'Gwen_Painterly_Preview.blend'))
