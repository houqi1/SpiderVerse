"""Read a source .blend without running its embedded scripts; export node truth.

blender --background --factory-startup --disable-autoexec --python inspect_material.py
        -- source.blend output.json
"""
import bpy
import hashlib
import json
from pathlib import Path
import sys

args = sys.argv[sys.argv.index("--") + 1:]
source, target = map(Path, args[:2])
bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)

def value(v):
    if isinstance(v, (str, bool, int, float)) or v is None:
        return v
    try:
        return [value(x) for x in v]
    except TypeError:
        return str(v)

def socket(s, index):
    info = {"index": index, "name": s.name, "identifier": s.identifier, "type": s.bl_idname,
            "enabled": s.enabled, "linked": s.is_linked}
    if hasattr(s, "default_value"):
        info["default"] = value(s.default_value)
    return info

def properties(obj, names):
    return {key: value(getattr(obj, key)) for key in names if hasattr(obj, key)}

def node(n):
    result = {"name": n.name, "type": n.bl_idname, "label": n.label, "mute": n.mute,
              "location": value(n.location), "inputs": [socket(s, i) for i, s in enumerate(n.inputs)],
              "outputs": [socket(s, i) for i, s in enumerate(n.outputs)]}
    result.update(properties(n, ["operation", "use_clamp", "blend_type", "data_type", "factor_mode",
        "clamp_factor", "clamp_result", "noise_dimensions", "noise_type", "normalize",
        "voronoi_dimensions", "feature", "distance", "interpolation_type", "vector_type",
        "convert_from", "convert_to", "space", "distribution"]))
    if getattr(n, "node_tree", None):
        result["node_tree"] = n.node_tree.name
    if hasattr(n, "color_ramp"):
        r = n.color_ramp
        result["ramp"] = {**properties(r, ["color_mode", "hue_interpolation", "interpolation"]),
                          "elements": [{"position": e.position, "color": value(e.color)} for e in r.elements]}
    if hasattr(n, "texture_mapping"):
        result["texture_mapping"] = properties(n.texture_mapping, ["translation", "rotation", "scale", "vector_type", "mapping_x", "mapping_y", "mapping_z", "use_min", "use_max", "min", "max"])
    return result

def tree(t):
    return {"name": t.name, "nodes": [node(n) for n in t.nodes], "links": [
        {"from_node": l.from_node.name, "from_socket": l.from_socket.identifier,
         "to_node": l.to_node.name, "to_socket": l.to_socket.identifier} for l in t.links]}

report = {
    "source": str(source), "sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "saved_version": value(bpy.data.version), "renderer_version": bpy.app.version_string,
    "renderer_commit": bpy.app.build_hash.decode(),
    "materials": [{"name": m.name, **properties(m, ["surface_render_method", "use_transparent_shadow", "use_transparency_overlap", "use_backface_culling", "use_backface_culling_shadow", "blend_method", "alpha_threshold"]),
                   "tree": tree(m.node_tree) if m.node_tree else None} for m in bpy.data.materials],
    "groups": [tree(t) for t in bpy.data.node_groups if t.bl_idname == "ShaderNodeTree"],
    "scenes": [{"name": s.name, "engine": s.render.engine, "view_settings": properties(s.view_settings, ["view_transform", "look", "exposure", "gamma"]),
                "render": properties(s.render, ["film_transparent", "resolution_x", "resolution_y", "resolution_percentage"])} for s in bpy.data.scenes],
    "objects": [{"name": o.name, "type": o.type, "location": value(o.location), "rotation": value(o.rotation_euler), "scale": value(o.scale),
                 "materials": [slot.material.name if slot.material else None for slot in o.material_slots]} for o in bpy.data.objects],
}
target.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
print("NODE_AUDIT", target, "materials", len(report["materials"]), "groups", len(report["groups"]), "saved", report["saved_version"])
