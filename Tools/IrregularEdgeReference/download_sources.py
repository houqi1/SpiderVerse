"""Pin upstream shader sources to the installed Blender reference renderer."""
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parent
COMMIT = "e13b728c6880"
BASE = f"https://raw.githubusercontent.com/blender/blender/{COMMIT}/"
FILES = [
    "source/blender/gpu/shaders/common/gpu_shader_common_hash.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_noise.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_fractal_noise.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_tex_noise.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_fresnel.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_map_range.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_val_to_rgb.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_mix_shader.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_transparent.glsl",
    "source/blender/gpu/shaders/material/gpu_shader_material_texture_coordinates.glsl",
    "source/blender/nodes/shader/nodes/node_shader_tex_noise.cc",
    "source/blender/nodes/shader/nodes/node_shader_valToRgb.cc",
    "COPYING",
]

def main():
    folder = ROOT / "blender-source"
    folder.mkdir(exist_ok=True)
    prior = folder / "manifest.json"
    paths = [entry["path"] for entry in json.loads(prior.read_text())["files"]] if prior.exists() else FILES
    manifest = {"commit": COMMIT, "files": [], "unavailable": []}
    for name in dict.fromkeys(paths):
        try:
            req = urllib.request.Request(BASE + name, headers={"User-Agent": "EdgeStudy-SourceAudit"})
            with urllib.request.urlopen(req, timeout=30) as response:
                content = response.read()
            (folder / Path(name).name).write_bytes(content)
            manifest["files"].append({"path": name, "url": BASE + name, "sha256": hashlib.sha256(content).hexdigest()})
            print(Path(name).name, len(content), flush=True)
        except Exception as exc:
            manifest["unavailable"].append({"path": name, "error": str(exc)})
            print("UNAVAILABLE", name, str(exc), flush=True)
    (folder / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")

if __name__ == "__main__":
    main()
