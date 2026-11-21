"""Check encoded vector lengths, deviation bounds, alpha and Unity import receipt."""
from pathlib import Path
import cv2,numpy as np,json,re
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Texture/GwenPainterly'; WORK=Path(__file__).resolve().parent/'work'
n=np.flipud(cv2.imread(str(OUT/'Gwen_Painterly_NormalOS_4K.png'),cv2.IMREAD_UNCHANGED))[...,::-1]
c=np.flipud(cv2.imread(str(OUT/'Gwen_Painterly_BaseColor_4K.png'),cv2.IMREAD_UNCHANGED))
assert n.shape==(4096,4096,3) and n.dtype==np.uint16
assert c.shape==(4096,4096,4) and c.dtype==np.uint8
length_error=angle=alpha_error=0.
for si in range(6):
    z=np.load(WORK/f'color_tile_{si}.npz'); p=z['pixel']; x=p%4096; y=p//4096
    nn=n[y,x].astype(np.float32)/65535*2-1
    length_error=max(length_error,float(np.abs(np.linalg.norm(nn,axis=-1)-1).max()))
    angle=max(angle,float(np.degrees(np.arccos(np.clip(np.sum(nn*z['normal'],axis=1),-1,1))).max()))
    alpha_error=max(alpha_error,float(np.abs(c[y,x,3].astype(np.float32)/255-z['color'][:,3]).max()))
assert length_error<.0001 and alpha_error<1/255 and angle<28.5
report=json.loads((OUT/'color_validation.json').read_text())
report.update(encoded_normal_max_length_error=length_error,max_normal_change_degrees=angle,max_alpha_resample_error=alpha_error)
receipt=Path('F:/Temp/GwenPainterlyBake/color-check-retry.log').read_text()
assert 'GWEN_COLOR_VALIDATED: 9 materials' in receipt
report['unity_check']='9 material bindings and 4096px sRGB color / linear model-space normal imports validated; shader imported without errors'
(OUT/'color_validation.json').write_text(json.dumps(report,indent=2))
readme=OUT/'README.txt';text=readme.read_text(encoding='utf-8')
text=re.sub(r'当前法线改变量最大约 [\d.]+ 度',f'当前法线改变量最大约 {angle:.1f} 度',text)
readme.write_text(text,encoding='utf-8')
print(json.dumps(dict(normal_length_error=length_error,max_angle=angle,alpha_error=alpha_error)))
