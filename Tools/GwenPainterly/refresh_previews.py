"""Refresh generated images and QA without replacing meshes or asset GUIDs."""
from pathlib import Path
import json,shutil
import cv2,numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2]; WORK=Path(__file__).resolve().parent/'work'
OUT=ROOT/'Assets/Texture/GwenPainterly'
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',25)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
for left,label,title,filename in [
    ('detail_before.png','原始模型法线','Spider-Gwen · 模型空间笔触法线','Comparison.png'),
    ('detail_small_brush.png','上一版 · 小笔触','Spider-Gwen · 笔触尺寸调整','BrushSizeComparison.png')]:
    board=Image.new('RGB',(1600,990),(26,29,36)); draw=ImageDraw.Draw(board)
    draw.text((30,20),title,font=font,fill=(240,240,245))
    draw.text((30,60),'同一模型与灯光；笔触长度、宽度均为第一版的 1.8 倍。4K 模型空间法线 / UV1。',font=small,fill=(180,190,205))
    for col,(image,text) in enumerate([(left,label),('detail_after.png','新版 · 放大笔触')]):
        im=Image.open(WORK/image).convert('RGBA'); im.thumbnail((770,847)); x=15+col*800
        board.paste(im,(x,130),im); draw.text((x+15,95),text,font=font,fill=(235,235,240))
    board.save(OUT/filename)
shutil.copyfile(WORK/'atlas_preview.png',OUT/'AtlasPreview.png')
a=cv2.imread(str(OUT/'Gwen_Painterly_NormalOS_4K.png'),cv2.IMREAD_UNCHANGED)
assert a.shape==(4096,4096,3) and a.dtype==np.uint16
q=a[::4,::4].astype(np.float32)/65535*2-1
err=float(np.abs(np.linalg.norm(q,axis=-1)-1).max()); assert err<.0001
report=json.loads((WORK/'validation.json').read_text())
report['sampled_normal_length_max_error']=err
report['png_format']='4096 x 4096 RGB uint16'
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
readme=OUT/'README.txt'; text=readme.read_text(encoding='utf-8').replace('（第一版）','（放大笔触版）')
note='笔触尺寸更新：长度和宽度均放大至第一版的 1.8 倍，并减少笔触数量以保持覆盖密度。'
if note not in text: text=text.replace('使用方法',note+'\n\n使用方法',1)
readme.write_text(text,encoding='utf-8')
print('Updated previews; normal-length error:',err)
