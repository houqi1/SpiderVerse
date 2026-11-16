"""Blender (Cycles) preview: original smooth normals vs the baked Voronoi object-space normal map.
Run:  python Tools/GwenPainterly/run_blender_task.py Tools/GwenPainterly/VoronoiNormal/render_voronoi_preview.py
Writes Preview_{smooth,voronoi}_{front,back}.png next to this file (single white diffuse material, one sun light).
"""
import bpy, json, math, os
from pathlib import Path
from mathutils import Vector

ROOT = Path('F:/Projections/SpiderVerse')
DIR = ROOT / 'Tools/GwenPainterly/VoronoiNormal'
# optional: VN_TEX=<object-space png in this folder>  VN_TAG=<file tag>  VN_CLOSE_ONLY=1 (only the torso/head close-ups of the baked map)
TEX = DIR / os.environ.get('VN_TEX', 'Gwen_Voronoi_NormalOS_4K.png')
TAG = os.environ.get('VN_TAG', '')
CLOSE_ONLY = os.environ.get('VN_CLOSE_ONLY') == '1'

bpy.ops.wm.read_factory_settings(use_empty=True)
surfaces = json.loads((ROOT / 'Tools/GwenPainterly/work/atlas_meshes.json').read_text())['surfaces']
verts, normals, faces, uvs = [], [], [], []
for s in surfaces:
    if s['name'] == 'wait head':           # masked alternative head occupies the same space
        continue
    off = len(verts)
    verts += [(v['x'] * 100, v['y'] * 100, v['z'] * 100) for v in s['positions']]
    normals += [(v['x'], v['y'], v['z']) for v in s['normals']]
    uvs += [(v['x'], v['y']) for v in s['uv']]
    tr = s['triangles']
    faces += [tuple(off + j for j in tr[i:i + 3]) for i in range(0, len(tr), 3)]
mesh = bpy.data.meshes.new('Gwen'); mesh.from_pydata(verts, [], faces); mesh.update()
obj = bpy.data.objects.new('Gwen', mesh); bpy.context.collection.objects.link(obj)
for p in mesh.polygons: p.use_smooth = True
mesh.normals_split_custom_set_from_vertices(normals)
layer = mesh.uv_layers.new(name='UV1')
for loop in mesh.loops: layer.data[loop.index].uv = uvs[loop.vertex_index]

def make_material(name, voronoi):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfDiffuse')
    bsdf.inputs['Color'].default_value = (0.9, 0.9, 0.9, 1)
    nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    if voronoi:
        tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(str(TEX))
        tex.image.colorspace_settings.name = 'Non-Color'; tex.interpolation = 'Linear'
        uv = nt.nodes.new('ShaderNodeUVMap'); uv.uv_map = 'UV1'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nm.space = 'OBJECT'; nm.inputs['Strength'].default_value = 1.0
        nt.links.new(uv.outputs['UV'], tex.inputs['Vector']); nt.links.new(tex.outputs['Color'], nm.inputs['Color'])
        nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    return m

scene = bpy.context.scene
scene.render.engine = 'CYCLES'; scene.cycles.samples = 32; scene.cycles.use_denoising = False
scene.render.threads_mode = 'FIXED'; scene.render.threads = 6
scene.render.resolution_x = 700; scene.render.resolution_y = 1000
scene.view_settings.view_transform = 'Standard'
world = bpy.data.worlds.new('W'); world.use_nodes = True; world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.04, 0.04, 0.05, 1); scene.world = world
sun = bpy.data.lights.new('Sun', 'SUN'); sun.energy = 3.2; sun.angle = math.radians(2)
so = bpy.data.objects.new('Sun', sun); bpy.context.collection.objects.link(so)
cam = bpy.data.cameras.new('Cam'); cam.type = 'ORTHO'; cam.ortho_scale = 2.15
co = bpy.data.objects.new('Cam', cam); bpy.context.collection.objects.link(co); scene.camera = co

for voronoi in ((False, True) if not CLOSE_ONLY else ()):
    mesh.materials.clear(); mesh.materials.append(make_material('M', voronoi))
    for view, (cy, rz, sunrot) in {'front': (-6, 0, (50, 0, -35)), 'back': (6, 180, (50, 0, 145))}.items():
        co.location = (0, cy, 0.9); co.rotation_euler = (math.radians(90), 0, math.radians(rz))
        so.rotation_euler = tuple(math.radians(a) for a in sunrot)
        scene.render.filepath = str(DIR / f"Preview_{'voronoi' if voronoi else 'smooth'}_{view}.png")
        bpy.ops.render.render(write_still=True)
        print('RENDERED', scene.render.filepath, flush=True)

# close-up of the torso / head (front), same light
cam.ortho_scale = 0.62; scene.render.resolution_x = 900; scene.render.resolution_y = 900
co.rotation_euler = (math.radians(90), 0, 0); so.rotation_euler = tuple(math.radians(a) for a in (50, 0, -35))
for voronoi in ((False, True) if not CLOSE_ONLY else (True,)):
    mesh.materials.clear(); mesh.materials.append(make_material('M', voronoi))
    for tag, (cx, cz) in {'torso': (0.0, 1.18), 'head': (0.0, 1.62)}.items():
        co.location = (cx, -6, cz)
        scene.render.filepath = str(DIR / f"Close_{'voronoi' if voronoi else 'smooth'}{TAG}_{tag}.png")
        bpy.ops.render.render(write_still=True)
        print('RENDERED', scene.render.filepath, flush=True)
