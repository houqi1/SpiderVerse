import platform
if hasattr(platform,'_wmi'):platform._wmi=None
import psutil,json
from pathlib import Path
for p in psutil.process_iter(['pid','name','cmdline']):
    cmd=p.info['cmdline'] or []
    if any('paint_cross_part_seams.py' in a for a in cmd):print(json.dumps({'pid':p.pid,'task':'paint_cross_part_seams'}))
out=Path('Tools/GwenPainterly/UVBrushPass/Tip35_ContinuousBW15')
for f in out.glob('*'):
    if f.suffix in ['.png','.json']:print(f.name,f.stat().st_size)
