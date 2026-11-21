"""Capture the live, possibly unsaved Blender state without replacing the user's file."""
import bpy, json
from pathlib import Path
root = Path('F:/Projections/SpiderVerse/Tools/LassylaUnity')
root.mkdir(parents=True, exist_ok=True)
def val(v):
    if isinstance(v, (str, bool, int, float)) or v is None: return v
    try: return [val(x) for x in v]
    except TypeError: return str(v)
def props(o, names): return {n: val(getattr(o,n)) for n in names if hasattr(o,n)}
report = {'original_file': bpy.data.filepath, 'was_dirty': bpy.data.is_dirty, 'viewports': [], 'objects': []}
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type != 'VIEW_3D': continue
        s=area.spaces.active; r=s.region_3d
        report['viewports'].append({'screen':screen.name,'active_screen':screen==bpy.context.screen,
            'area':[area.x,area.y,area.width,area.height], 'lens':s.lens,
            'shading':props(s.shading,['type','use_scene_lights','use_scene_world','use_scene_lights_render','use_scene_world_render','studio_light','studiolight_rotate_z','studiolight_intensity','studiolight_background_alpha','studiolight_background_blur','use_studiolight_view_rotation','use_compositor','render_pass']),
            'region':props(r,['view_matrix','window_matrix','perspective_matrix','view_location','view_rotation','view_distance','view_perspective'])})
for o in bpy.data.objects:
    report['objects'].append({'name':o.name,'type':o.type,'hidden':o.hide_get(),'hide_viewport':o.hide_viewport,'hide_render':o.hide_render,'matrix_world':val(o.matrix_world)})
report['studiolights']=[props(l,['name','path','type']) for l in bpy.context.preferences.studio_lights]
report['eevee']=props(bpy.context.scene.eevee,['taa_samples','taa_render_samples','use_gtao','gtao_distance','gtao_factor','use_raytracing'])
report['world']=bpy.context.scene.world.name if bpy.context.scene.world else None
report['display_settings']=props(bpy.context.scene.display_settings,['display_device'])
report['view_settings']=props(bpy.context.scene.view_settings,['view_transform','look','exposure','gamma','use_curve_mapping','view_settings'])
(root/'live-state.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(root/'LiveReference.blend'),copy=True,compress=True)
print('UNITY_SOURCE_CAPTURED',root,'original file and live parameters preserved')
