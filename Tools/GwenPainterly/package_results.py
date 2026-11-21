from pathlib import Path
import shutil,re,json
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2]; WORK=Path(__file__).resolve().parent/'work'
SRC=Path('F:/Temp/GwenPainterlyBake/Assets/GwenPainterly'); OUT=ROOT/'Assets/Texture/GwenPainterly'
assert 'GWEN_FINALIZE_COMPLETE' in Path('F:/Temp/GwenPainterlyBake/finalize3.log').read_text()
shutil.copytree(SRC,OUT,dirs_exist_ok=True)
shutil.copyfile(Path(__file__).parent/'ModelSpacePreview.shader',OUT/'ModelSpacePreview.shader')
shutil.copyfile('F:/Temp/GwenPainterlyBake/atlas_meshes.json',WORK/'atlas_meshes.json')
shutil.copyfile(WORK/'validation.json',OUT/'validation.json')
# Source base-color texture GUIDs remain references to the existing project assets.
prefab=(OUT/'Spider-Gwen_PainterlyOS.prefab').read_text()
source_guid=re.search(r'guid: (\w+)',Path('F:/Temp/GwenPainterlyBake/Assets/Spider-Gwen.fbx.meta').read_text())[1]
assert source_guid not in prefab,'Prefab still references the temporary FBX'
guide='''Spider-Gwen — 模型空间笔触法线（第一版）

主贴图：Gwen_Painterly_NormalOS_4K.png，4096 × 4096，RGB 16-bit PNG。
RGB = Unity 网格局部空间法线 × 0.5 + 0.5；不含底色、不含烘焙灯光。
笔触由程序从原始网格法线附近采样，覆盖为笔触形状的同向法线区域。

使用方法
1. 把 Spider-Gwen_PainterlyOS.prefab 拖入一个 URP 场景，添加方向光。
2. 配套网格保留原 UV0 和骨骼权重，新增互不重叠的 UV1（Mesh.uv2）。
   BaseMap 用 UV0；NormalOS 用 UV1/TEXCOORD1。
3. 贴图导入：Texture Type = Default；sRGB = OFF；Compression = None；Max Size = 4096。
   已附带这些设置。不要设成 Normal map，不要使用 UnpackNormal。
4. Shader 解码：normalOS = normalize(sample.rgb * 2 - 1)，再通过对象法线矩阵转到世界空间。
5. 预览材质的 Brush strength 从 0（原始网格法线）到 1（完整笔触）可调。

为什么有模型副本
原模型身体、兜帽等存在大量镜像重叠 UV，同一个像素对应左右不同的模型空间法线。
这张图为新增 UV1 制作，不能直接拖到原模型 UV0 的 Normal Map 槽里使用。
原 FBX、原场景、原材质及原 Toon Shader 均未被修改。

适用范围
预览材质用于静态/绑定姿势模型；默认禁用副本 Animator。
模型空间法线纹理不会自动随骨骼弯曲，需要额外的法线蒙皮方案才能正确用于骨骼动画。
保留了有脸与面罩两个头部网格，默认只显示有脸版本。
附带的预览 Shader 使用简化的 URP 光照，不是原 Toon Shader 的完整替代。
接入原 Toon Shader 时应使用独立的模型空间法线采样路径及 UV1。

验证
已检查 atlas UV 的方向冲突、法线长度及笔触改变量，并用同一灯光在 Blender 渲染前后对比。
头部存在 3 个像素的 UV 边界舍入冲突（占头部覆盖像素约 0.0003%）；其余为 0。
笔触改变量最大约 28.4 度；岛边缘做了 20 像素向外扩展。
预览图使用原颜色贴图，仅替换模型空间法线。
本次未在原 Unity 场景中替换角色或调整灯光。
'''
(OUT/'README.txt').write_text(guide,encoding='utf-8')
# A comparison figure from the actual rendered model, with identical camera and light.
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',25)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
board=Image.new('RGB',(1600,990),(26,29,36)); draw=ImageDraw.Draw(board)
draw.text((30,20),'Spider-Gwen · 模型空间笔触法线',font=font,fill=(240,240,245))
draw.text((30,60),'同一模型、底色、相机与灯光；仅改变法线。程序生成笔触，4K / UV1。',font=small,fill=(180,190,205))
for col,(filename,title) in enumerate([('detail_before.png','原始模型法线'),('detail_after.png','笔触模型法线')]):
    im=Image.open(WORK/filename).convert('RGBA'); im.thumbnail((770,847))
    x=15+col*800; board.paste(im,(x,130),im)
    draw.text((x+15,95),title,font=font,fill=(235,235,240))
board.save(OUT/'Comparison.png')
Image.open(WORK/'atlas_preview.png').save(OUT/'AtlasPreview.png')
print('Packaged',len(list(OUT.rglob('*'))),'files/directories')
