"""Connect the color atlas to the existing preview materials; preserve asset GUIDs."""
from pathlib import Path
import re,uuid,json
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Texture/GwenPainterly'
meta=OUT/'Gwen_Painterly_BaseColor_4K.png.meta'
guid=re.search(r'^guid: (\w+)',meta.read_text(),re.M)[1] if meta.exists() else uuid.uuid4().hex
template=(OUT/'Gwen_Painterly_NormalOS_4K.png.meta').read_text()
template=re.sub(r'^guid: \w+',f'guid: {guid}',template,flags=re.M)
template=template.replace('sRGBTexture: 0','sRGBTexture: 1').replace('alphaIsTransparency: 0','alphaIsTransparency: 1')
meta.write_text(template)
for path in (OUT/'Materials').glob('*.mat'):
    text=path.read_text()
    text,n=re.subn(r'(- _BaseMap:\s+m_Texture: )\{[^}]*\}',r'\g<1>{fileID: 2800000, guid: '+guid+', type: 3}',text)
    assert n==1,path
    if '_ColorUsesUV1:' in text: text=re.sub(r'_ColorUsesUV1: [^\n]+','_ColorUsesUV1: 1',text)
    else: text=text.replace('    m_Floats:\n','    m_Floats:\n    - _ColorUsesUV1: 1\n')
    path.write_text(text)
readme=OUT/'README.txt'; text=readme.read_text(encoding='utf-8')
text=text.replace('（放大笔触版）','（底色、法线与接缝共笔触版）')
text=text.replace('   BaseMap 用 UV0；NormalOS 用 UV1/TEXCOORD1。','   新的 BaseMap 与 NormalOS 都用 UV1/TEXCOORD1；原始颜色图仍保留在原路径。')
text=text.replace('3. 贴图导入：Texture Type = Default；sRGB = OFF；Compression = None；Max Size = 4096。','3. 法线图导入：Texture Type = Default；sRGB = OFF；Compression = None；Max Size = 4096。')
addition='''
底色与接缝更新
Gwen_Painterly_BaseColor_4K.png：4096 × 4096 RGBA，sRGB ON。
底色与模型空间法线复用笔触采样点、覆盖形状、方向和尺寸，采用同步放大的笔触尺寸。
黑白战衣、白粉袖子、兜帽等共享几何边界采用表面空间的笔触投射，同时修改两侧颜色和法线。
同一部件的 UV 岛接缝也做了跨岛笔触覆盖。几何真实间隙和轮廓保持原状。
脸部和蛛网细节降低了颜色概括强度，头发透明度保留。
9 个配套预览材质已经使用新底色图，并开启 Use painterly atlas UV1 for base color。
静态姿势限制与原法线版本相同。
'''
if '底色与接缝更新' not in text: text+='\n'+addition
readme.write_text(text,encoding='utf-8')
print('Installed base color on 9 preview materials; GUID',guid)
