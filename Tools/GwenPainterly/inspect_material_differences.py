from pathlib import Path
import re,json
p=Path('Assets/Texture/GwenPainterly/Materials')
mats={}
for f in sorted(p.glob('*.mat')):
    s=f.read_text();props={}
    for m in re.finditer(r'^    - (_\w+):(?: ([^\n]+)|\n((?:        [^\n]+\n)+))',s,re.M):props[m[1]]=m[2] or m[3].strip()
    mats[f.stem]=props
for prop in sorted(set().union(*(x.keys() for x in mats.values()))):
    values={n:v.get(prop) for n,v in mats.items()}
    if len(set(values.values()))>1:print(json.dumps({'property':prop,'values':values},ensure_ascii=False))
