from pathlib import Path
p=Path('C:/Users/win11/AppData/Local/Unity/Editor/Editor.log')
if p.exists():
    lines=p.read_text(encoding='utf-8',errors='replace').splitlines()[-500:]
    errors=[s for s in lines if 'error CS' in s or 'GWEN_CONTINUOUS_COLOR' in s or ('GwenContinuousColorInstall' in s and 'Exception' in s)]
    print('\n'.join(errors) if errors else 'No recent C# errors or continuous import result.')
