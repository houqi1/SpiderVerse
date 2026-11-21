from pathlib import Path
import re,json,hashlib
root=Path('Assets/Texture/GwenPainterly');work=Path('Tools/GwenPainterly/MaterialHierarchy')
report=(work/'hierarchy.report.txt').read_text()
backup=Path(re.search(r'^Backup: (.+)$',report,re.M)[1].strip())
parent=root/'Gwen_Painterly_ToonBase.mat'
guid=re.search(r'^guid: (\w+)',Path(str(parent)+'.meta').read_text(),re.M)[1]
children=list((root/'Materials').glob('*.mat'));assert len(children)==9
for p in children:
    s=p.read_text();assert f'm_Parent: {{fileID: 2100000, guid: {guid}, type: 2}}' in s
    assert '_ShadeThreshold:' not in s and '_WrapNormalMap:' not in s and '_BaseMap:' not in s
    assert Path(str(p)+'.meta').read_bytes()==(backup/(p.name+'.meta')).read_bytes()
protected=json.loads(Path('Tools/GwenPainterly/UVBrushPass/Tip35/protected_files.json').read_text())
for p,h in protected.items():
    if p.endswith('.mat'):continue
    assert hashlib.sha256(Path(p).read_bytes()).hexdigest()==h,p
result={'children':9,'common_parent':str(parent),'child_guids_preserved':True,'shared_parameters_inherited':True,'shader_and_textures_unchanged':True,'unity_effective_values_and_propagation_verified':'PASS: nine variants' in report}
(work/'saved_verification.json').write_text(json.dumps(result,indent=2));print(json.dumps(result))
