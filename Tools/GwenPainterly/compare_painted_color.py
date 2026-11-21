from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2]; WORK=Path(__file__).resolve().parent/'work'; OUT=ROOT/'Assets/Texture/GwenPainterly'
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',25); small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
for images,filename in [(['detail_normals_only.png','detail_after.png'],'ColorComparison.png'),(['full_normals_only.png','after.png'],'ColorFullComparison.png')]:
    board=Image.new('RGB',(1600,990),(26,29,36)); draw=ImageDraw.Draw(board)
    draw.text((30,20),'Spider-Gwen · 底色与法线共用笔触',font=font,fill=(240,240,245))
    draw.text((30,60),'同一模型与灯光；跨部件及 UV 接缝使用共享的表面笔触。',font=small,fill=(180,190,205))
    for col,(file,label) in enumerate(zip(images,['修改前 · 仅法线笔触化','修改后 · 底色 + 法线 + 接缝'])):
        im=Image.open(WORK/file).convert('RGBA'); im.thumbnail((770,847)); x=15+col*800
        board.paste(im,(x,130),im);draw.text((x+15,95),label,font=font,fill=(235,235,240))
    board.save(OUT/filename)

if (WORK/'detail_color_previous_size.png').exists():
    board=Image.new('RGB',(1600,990),(26,29,36)); draw=ImageDraw.Draw(board)
    draw.text((30,20),'Spider-Gwen · 更大的共享笔触',font=font,fill=(240,240,245))
    draw.text((30,60),'底色、法线及接缝笔触同步放大；长度和宽度均为上一版的 1.5 倍。',font=small,fill=(180,190,205))
    for col,(file,label) in enumerate([('detail_color_previous_size.png','上一版'),('detail_after.png','新版 · 放大笔触')]):
        im=Image.open(WORK/file).convert('RGBA'); im.thumbnail((770,847)); x=15+col*800
        board.paste(im,(x,130),im); draw.text((x+15,95),label,font=font,fill=(235,235,240))
    board.save(OUT/'ColorBrushSizeComparison.png')
