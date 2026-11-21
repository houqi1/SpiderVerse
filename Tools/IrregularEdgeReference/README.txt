Lassyla irregular edge study
===========================

Open the current local app:
  http://127.0.0.1:8769/IrregularEdgeLab.html

Start it again from PowerShell:
  python F:\Projections\SpiderVerse\Tools\IrregularEdgeReference\serve_native.py
Use --blender PATH if Blender is installed elsewhere. The service binds only to
127.0.0.1. Ctrl+C stops a foreground service. No packages are required.

The standalone file is Documentation/IrregularEdgeLab.html. Its WebGL mode runs
offline. Blender native mode additionally requires the local Python service.

WHAT IS EXACT, AND WHAT IS NOT
The WebGL version ports the edge branch using Blender source, original model
geometry, render subdivision level, Generated coordinates, SNORM10 normals,
Jenkins hash, Perlin/fBM, dielectric Fresnel, linear ramps and EEVEE alpha hash.
It does not use a generic lookalike noise or a CSS/SVG silhouette.
The original branch is:
  Fresnel(IOR 1.4500000476837158) -> ColorRamp.003 --+
  Noise Texture(3D fBM) -> ColorRamp.004 ----------+-> Multiply -> Mix Shader
  Mix Shader: base shader first, white Transparent BSDF second.
  Surface: DITHERED, double-sided, transparency overlap enabled.

WebGL is NOT pixel-identical to EEVEE. The saved validation report explicitly
records the one-sample visibility differences. Browser shader arithmetic and
the simplified accumulation are not the full native Film/TAA implementation.
The WebGL display base is neutral lighting, not the author's full paint shader.

Blender native mode directly loads the supplied material blend in installed
Blender 5.2.3 LTS (e13b728c6880). The right image retains the entire original
material graph and lights. HTML controls camera, selected object, original
material and editable parameters. It uses native EEVEE, DITHERED, Film/TAA,
the original 1.5 filter size and AgX. No shader reimplementation is involved
in that mode. It is a local renderer behind HTML, not a browser-only renderer.
The left comparison bypasses only the last transparent Mix Shader.
Drag/change parameters, then click the native render button to update.
Original .blend files are opened with scripts disabled and never saved over.

EVIDENCE AND REBUILD
material-audit.json / scene-audit.json: original node sockets, values and links.
blender-source/manifest.json: pinned upstream paths, URLs and SHA-256 hashes.
build_reference.py: original meshes + 2048 native front/back node samples.
render_geometry_reference.py: original mesh pixel probes, 2818 probes total.
render_alpha_reference.py: native one-sample visibility images.
webgl-validation.json: browser's actual numeric and pixel comparison report.
native-renders/<key>/metadata.json: native version, source hash and parameters.
assemble_core.py: extracts the source GLSL; build_html.py packages the HTML.
lab.js / lab-body.html / lab-style.css: readable frontend source.

To rebuild the HTML from the checked-in reference data:
  python Tools/IrregularEdgeReference/assemble_core.py
  python Tools/IrregularEdgeReference/build_html.py
To regenerate Blender reference data, run each *_reference.py with:
  blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python SCRIPT

Blender-derived source retains GPL-2.0-or-later licensing; see
blender-source/COPYING and blender-source/GPL-license.txt. Author assets are
the user's supplied downloads; this study does not grant additional rights
to redistribute them.

Video: https://www.youtube.com/watch?v=mNlL77U0IL8
Material blend SHA-256:
ecda84d13cf48e98e571d434cba4dc49d30f01aeaabc93e118fa7fed1ec4d2c0
