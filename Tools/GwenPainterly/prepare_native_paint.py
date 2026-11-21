"""Cycles bake sources and export the existing unique atlas mesh for native painting.
No procedural brush generation or painting is performed here.
"""
import bpy, json
from pathlib import Path
from mathutils import Matrix

ROOT = Path('F:/Projections/SpiderVerse')
DEST = ROOT/'Tools/GwenPainterly/NativePaint'
DEST.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
data = json.loads((ROOT/'Tools/GwenPainterly/work/atlas_meshes.json').read_text())['surfaces']
verts=[]; normals=[]; faces=[]; uvs=[]; uv0=[]; mids=[]; materials=[]
for surface in data:
    offset=len(verts); mo=len(materials)
    for v in surface['positions']:
        verts.append((v['x']*100+(1.2 if surface['name']=='wait head' else 0),v['y']*100,v['z']*100))
    normals += [tuple(v[k] for k in 'xyz') for v in surface['normals']]
    uvs += [(v['x'],v['y']) for v in surface['uv']]
    uv0 += [(v['x'],v['y']) for v in surface['uv0']]
    faces += [tuple(offset+j for j in surface['triangles'][i:i+3]) for i in range(0,len(surface['triangles']),3)]
    mids += [mo+i for i in surface['materialIds']]
    materials += surface['materials']
mesh=bpy.data.meshes.new('Gwen Atlas Mesh'); mesh.from_pydata(verts,[],faces); mesh.update()
obj=bpy.data.objects.new('Gwen',mesh); bpy.context.collection.objects.link(obj)
bpy.context.view_layer.objects.active=obj; obj.select_set(True)
for poly in mesh.polygons: poly.use_smooth=True
mesh.normals_split_custom_set_from_vertices(normals)
for name,values in [('OriginalUV',uv0),('PainterlyUV',uvs)]:
    layer=mesh.uv_layers.new(name=name)
    for loop in mesh.loops: layer.data[loop.index].uv=values[loop.vertex_index]
mesh.uv_layers.active_index=1
mesh.uv_layers[1].active_render=True
source_nodes=[]; bake_nodes=[]; emission_nodes=[]
for name in materials:
    mat=bpy.data.materials.new(name); mat.use_nodes=True
    nodes=mat.node_tree.nodes; nodes.clear(); links=mat.node_tree.links
    tex=nodes.new('ShaderNodeTexImage')
    uv=nodes.new('ShaderNodeUVMap'); uv.uv_map='OriginalUV'; links.new(uv.outputs['UV'],tex.inputs['Vector'])
    emission=nodes.new('ShaderNodeEmission'); output=nodes.new('ShaderNodeOutputMaterial')
    links.new(emission.outputs[0],output.inputs['Surface'])
    target=nodes.new('ShaderNodeTexImage'); nodes.active=target
    source_nodes.append(tex); bake_nodes.append(target); emission_nodes.append(emission);mesh.materials.append(mat)
for poly,mi in zip(mesh.polygons,mids): poly.material_index=mi
scene=bpy.context.scene; scene.render.engine='CYCLES';scene.cycles.samples=1
scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.bake.margin=20;scene.render.bake.use_clear=True;scene.render.bake.normal_space='OBJECT'
scene.view_settings.view_transform='Standard'
for mode,filename,srgb in [('NORMAL','NormalOS_Baked.png',False),('EMIT','BaseColor_Baked.png',True),('ALPHA','Opacity_Baked.png',False)]:
    img=bpy.data.images.new(filename,width=4096,height=4096,alpha=True,float_buffer=True)
    img.colorspace_settings.name='sRGB' if srgb else 'Non-Color'
    for target in bake_nodes: target.image=img
    if mode=='NORMAL':
        bpy.ops.object.bake(type='NORMAL')
    else:
        obj.hide_render=True; obj.select_set(False)
        scene.render.bake.use_clear=False
        # Bake one original material at a time to keep 4K precision without
        # retaining all source textures and Cycles buffers simultaneously.
        for mi,name in enumerate(materials):
            mat=mesh.materials[mi]; tex=source_nodes[mi]; emission=emission_nodes[mi]
            tex.image=bpy.data.images.load(str(ROOT/'Assets/Texture'/(name+'.png')),check_existing=True)
            mat.node_tree.links.new(tex.outputs['Alpha' if mode=='ALPHA' else 'Color'],emission.inputs['Color'])
            part=bpy.data.meshes.new(name); part.from_pydata(verts,[],[f for f,m in zip(faces,mids) if m==mi]); part.update()
            for uvname,values in [('OriginalUV',uv0),('PainterlyUV',uvs)]:
                layer=part.uv_layers.new(name=uvname)
                for loop in part.loops:layer.data[loop.index].uv=values[loop.vertex_index]
            part.uv_layers.active_index=1;part.uv_layers[1].active_render=True
            part.materials.append(mat)
            piece=bpy.data.objects.new(name,part);bpy.context.collection.objects.link(piece)
            piece.select_set(True);bpy.context.view_layer.objects.active=piece
            bpy.ops.object.bake(type='EMIT')
            bpy.data.objects.remove(piece,do_unlink=True);bpy.data.meshes.remove(part)
            source_img=tex.image;tex.image=None;bpy.data.images.remove(source_img)
            print('BAKED MATERIAL',mode,name,flush=True)
        obj.hide_render=False;obj.select_set(True);bpy.context.view_layer.objects.active=obj
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_depth='16';scene.render.image_settings.color_mode='RGBA'
    img.save_render(str(DEST/filename),scene=scene)
    print('BAKED',filename,flush=True)
    for target in bake_nodes: target.image=None
    bpy.data.images.remove(img)
bpy.ops.wm.save_as_mainfile(filepath=str(DEST/'Gwen_BakeSources.blend'))
# Painting mesh uses a single texture set, with the already baked UV1 as UV0.
mesh.uv_layers.remove(mesh.uv_layers[0]); mesh.materials.clear()
mat=bpy.data.materials.new('GwenAtlas');mesh.materials.append(mat)
for poly in mesh.polygons:poly.material_index=0
bpy.ops.wm.obj_export(filepath=str(DEST/'Gwen_Paint.obj'),export_selected_objects=True,forward_axis='NEGATIVE_Z',up_axis='Y')
print('NATIVE_PAINT_INPUTS_READY',flush=True)
