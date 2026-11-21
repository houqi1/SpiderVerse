import os,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[2]
env=os.environ.copy();env['PYTHONPATH']=str(root/'Tools/GwenPainterly/NativePaint/bootstrap')
result=subprocess.run(['F:/Steam/steamapps/common/Blender/blender.exe','--background','--factory-startup','--python-use-system-env','--python',str((root/sys.argv[1]).resolve()),'--',*sys.argv[2:]],env=env)
raise SystemExit(result.returncode)
