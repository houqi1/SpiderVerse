"""Package the audited data, upstream core and local viewer as one offline HTML."""
import base64,json,re,struct
from pathlib import Path
R=Path(__file__).resolve().parent;DEST=R.parents[1]/'Documentation'/'IrregularEdgeLab.html'
style=(R/'lab-style.css').read_text(encoding='utf-8')
ref=json.loads((R/'edge-reference.json').read_text());meshdata=json.loads((R/'source-meshes.json').read_text());meshes={}
for name,m in meshdata.items():
    floats=[]
    for i in range(0,len(m['positions']),3):
        # GPU_attribute_convert.hh: truncate float32(n * 511), then SNORM decode.
        normals=[int(struct.unpack('<f',struct.pack('<f',n*511))[0])/511 for n in m['normals'][i:i+3]]
        floats.extend(m['positions'][i:i+3]+normals+m['generated'][i:i+3])
    meshes[name]={'data':base64.b64encode(struct.pack('<'+'f'*len(floats),*floats)).decode(),'material':m['material']}
scripts='const REFERENCE='+json.dumps(ref,separators=(',',':'))+';\nconst MESHES='+json.dumps(meshes,separators=(',',':'))+';\n'
scripts+='const GEOMETRY_REFERENCE='+(R/'geometry-reference.json').read_text()+';\n'
scripts+='const ALPHA_REFERENCE='+(R/'alpha-reference.json').read_text()+';\n'
scripts+='const EDGE_CORE='+json.dumps((R/'edge-core.glsl').read_text(),ensure_ascii=False)+';\n'
scripts+=(R/'lab.js').read_text(encoding='utf-8')
html='<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Lassyla · 原工程边缘</title><style>'+style+'</style></head><body>'+(R/'lab-body.html').read_text(encoding='utf-8')+'<script>'+scripts+'</script></body></html>'
DEST.write_text(html,encoding='utf-8');print('Built',DEST,len(html.encode()))
