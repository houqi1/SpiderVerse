"""Extract existing installed Kyle brush samples for use in Blender (no synthetic tips)."""
from pathlib import Path
import struct,json
from PIL import Image,ImageDraw
src=Path('F:/Sp/Adobe Substance 3D Painter/resources/starter_assets/presets/brushes/Kyle Brush Presets.abr')
out=Path(__file__).parent/'NativePaint/BrushTips';out.mkdir(exist_ok=True)
d=src.read_bytes();end=16+struct.unpack_from('>I',d,12)[0];p=16;items=[]
while p<end:
    size=struct.unpack_from('>I',d,p)[0];start=p+4;nextp=start+((size+3)//4*4)
    name=d[start+1:start+37].decode('ascii'); q=start+301
    top,left,bottom,right,depth,compression=struct.unpack_from('>4iHB',d,q);q+=19
    w,h=right-left,bottom-top
    if depth!=8 or not(0<w<8192 and 0<h<8192):raise ValueError((w,h,depth))
    if compression==0:raw=d[q:q+w*h]
    else:
        rows=struct.unpack_from('>'+str(h)+'H',d,q);q+=h*2;raw=bytearray()
        for n in rows:
            row=d[q:q+n];q+=n;i=0;decoded=bytearray()
            while i<n:
                k=row[i];i+=1
                if k<128:decoded+=row[i:i+k+1];i+=k+1
                elif k>128:decoded+=row[i:i+1]*(257-k);i+=1
            assert len(decoded)==w
            raw+=decoded
    im=Image.frombytes('L',(w,h),bytes(raw));path=out/f'tip_{len(items):02d}.png';im.save(path)
    items.append(dict(index=len(items),uuid=name,width=w,height=h,file=path.name))
    p=nextp
(out/'source.json').write_text(json.dumps(dict(source=str(src),tips=items),indent=2))
sheet=Image.new('RGB',(1000,((len(items)+7)//8)*125),(220,220,220));draw=ImageDraw.Draw(sheet)
for item in items:
    im=Image.open(out/item['file']);im.thumbnail((115,100));i=item['index'];x=(i%8)*125;y=(i//8)*125
    sheet.paste(im,(x,y));draw.text((x,y+102),f"{i}: {item['width']}x{item['height']}",fill='black')
sheet.save(out/'ContactSheet.jpg');print('Extracted',len(items),'original brush tips')
