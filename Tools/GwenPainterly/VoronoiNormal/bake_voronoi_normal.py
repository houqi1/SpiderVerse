"""Bake a Voronoi-flattened normal atlas for Spider-Gwen (UV1 atlas, PainterlyUV meshes).

Reads the REAL Unity vertex data of Assets/Texture/GwenPainterly/Meshes/*_PainterlyUV.asset
(position / normal / tangent / uv0 / uv1 / skin weights / indices) -- i.e. exactly what the GPU receives.

Two outputs (both 4096^2, linear 16-bit RGBA PNG, UV1 atlas, 20px margin):
  Gwen_Voronoi_NormalTS_4K.png  TANGENT space: rgb = (S.T, S.B, S.N)*0.5+0.5 relative to the ORIGINAL vertex normal / tangent.
                                Use with Toon.shader  _WrapNormalTangentSpace = 1 : skinning rotates N,T,B with the bones, so the
                                flattened normal follows the animation and strength 1 -> 0 fades to the original vertex normal.
  Gwen_Voronoi_NormalOS_4K.png  OBJECT (mesh) space: only valid while the skinned pose == bind pose.

Voronoi: cells on the surface, seeds = farthest point sampling + Lloyd on uniformly sampled surface points,
distance in (position, nw*spacing*vertex-normal) so thin double-sided parts are not merged.
Cell normal S = normalised sum of the ORIGINAL (interpolated) vertex normals of all samples in the cell.

Memory: everything per-texel is processed in chunks (peak well under 1 GB) -- the machine this runs on is often close to its commit limit.
Python on this machine needs  PYTHONPATH=Tools/GwenPainterly/NativePaint/bootstrap  (stalled WMI workaround), without -I.

Also writes skin_check.json: simulates the prefab's bone pose (GPU skinning of N,T) and measures how far each map is
from the skinned vertex normal -- this is what you see in Unity when you fade "Model Space Normal Strength" 1 -> 0.

  python bake_voronoi_normal.py --cells 10000 --samples 2000000 --no-install
"""
import argparse, json, hashlib, re, time, gc
from pathlib import Path
import numpy as np, cv2
from scipy.spatial import cKDTree
from scipy import ndimage

ap = argparse.ArgumentParser()
ap.add_argument('--cells', type=int, default=500)
ap.add_argument('--relax', type=int, default=3, help='Lloyd iterations (round, regular cells). 0 keeps the raw random look')
ap.add_argument('--seeding', choices=['fps', 'random', 'varied'], default='fps', help='fps = even/regular, random = irregular, varied = irregular + spatially varying cell size')
ap.add_argument('--density-var', type=float, default=1.6, help='strength of the density field for --seeding varied')
ap.add_argument('--nw', type=float, default=1.0, help='normal weight in the Voronoi metric (in cell spacings); 0 = pure 3D')
ap.add_argument('--seed', type=int, default=7)
ap.add_argument('--size', type=int, default=4096)
ap.add_argument('--margin', type=int, default=20)
ap.add_argument('--ss', type=int, default=2)
ap.add_argument('--samples', type=int, default=900000)
ap.add_argument('--chunk', type=int, default=400000, help='texels per chunk')
ap.add_argument('--workers', type=int, default=4)
ap.add_argument('--name', default='Gwen_Voronoi')
ap.add_argument('--no-install', action='store_true')
ap.add_argument('--stats-only', action='store_true')
args = ap.parse_args()

ROOT = Path('F:/Projections/SpiderVerse')
ASSETS = ROOT / 'Assets'
MESHDIR = ASSETS / 'Texture/GwenPainterly/Meshes'
PREFAB = ASSETS / 'Texture/GwenPainterly/Spider-Gwen_PainterlyOS.prefab'
OUT = ROOT / 'Tools/GwenPainterly/VoronoiNormal'
OUT.mkdir(exist_ok=True)
T0 = time.time()
log = lambda *a: print(f'[{time.time()-T0:6.1f}s]', *a, flush=True)
rng = np.random.default_rng(args.seed)
W_ = args.workers

# ============================================================ Unity mesh asset parser
FMT = {0: ('<f4', 4), 1: ('<f2', 2), 2: ('u1', 1), 3: ('i1', 1), 4: ('<u2', 2), 5: ('<i2', 2), 6: ('u1', 1), 7: ('i1', 1), 8: ('<u2', 2), 9: ('<i2', 2), 10: ('<u4', 4), 11: ('<i4', 4)}

def load_mesh(path):
    t = path.read_text(encoding='utf-8')
    vd = t[t.index('m_VertexData:'):]
    nv = int(re.search(r'm_VertexCount: (\d+)', vd).group(1))
    ch = [tuple(int(x) for x in c) for c in re.findall(r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', vd)[:14]]
    raw = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]*)', vd).group(1))
    stride = {}
    for s, o, f, d in ch:                                  # streams are laid out one after another, each padded to 16 bytes
        if d: stride[s] = max(stride.get(s, 0), o + d * FMT[f][1])
    base, pos = {}, 0
    for s in sorted(stride):
        base[s] = pos; pos += (stride[s] * nv + 15) // 16 * 16
    def attr(i):
        s, o, f, d = ch[i]
        if d == 0: return None
        dt, sz = FMT[f]
        a = np.empty((nv, d), dtype=np.float64)
        for k in range(d):
            a[:, k] = np.ndarray(nv, dtype=dt, buffer=raw, offset=base[s] + o + k * sz, strides=(stride[s],))
        if f == 2: a /= 255.0
        return a
    idx_hex = re.search(r'm_IndexBuffer: ([0-9a-f]*)', t).group(1)
    fmt32 = int(re.search(r'm_IndexFormat: (\d+)', t).group(1)) == 1
    tri = np.frombuffer(bytes.fromhex(idx_hex), dtype='<u4' if fmt32 else '<u2').astype(np.int64).reshape(-1, 3)
    bp = re.findall(r'e(\d)(\d): ([-\d.e]+)', t[t.index('m_BindPose:'):t.index('m_BoneNameHashes')])
    bindposes = np.array([float(v[2]) for v in bp]).reshape(-1, 4, 4)
    m = dict(name=re.search(r'm_Name: (.*)', t).group(1).strip(), pos=attr(0), nrm=attr(1), tan=attr(2), uv0=attr(4), uv1=attr(5),
             bw=attr(12), bi=attr(13), tri=tri, bindposes=bindposes)
    m['nrm'] /= np.maximum(np.linalg.norm(m['nrm'], axis=1, keepdims=True), 1e-20)
    return m

meshes = []
for p in sorted(MESHDIR.glob('*.asset')):
    m = load_mesh(p); m['guid'] = re.search(r'guid: (\w+)', (p.with_name(p.name + '.meta')).read_text()).group(1); meshes.append(m)
    log(f"loaded {m['name']}: verts {len(m['pos'])} tris {len(m['tri'])} skin dims {None if m['bw'] is None else m['bw'].shape[1]} uv1 {m['uv1'] is not None}")

P = np.concatenate([m['pos'] for m in meshes]); N = np.concatenate([m['nrm'] for m in meshes])
Tg = np.concatenate([m['tan'] for m in meshes]); UV1 = np.concatenate([m['uv1'] for m in meshes])
off = np.cumsum([0] + [len(m['pos']) for m in meshes])
TRI = np.concatenate([m['tri'] + off[i] for i, m in enumerate(meshes)])
MID = np.concatenate([[i] * len(m['tri']) for i, m in enumerate(meshes)])
names = [m['name'] for m in meshes]
alt = np.array([names[i].startswith('wait_head') for i in MID])      # masked alternative head, same place as idle head
e1 = P[TRI[:, 1]] - P[TRI[:, 0]]; e2 = P[TRI[:, 2]] - P[TRI[:, 0]]
area = 0.5 * np.linalg.norm(np.cross(e1, e2), axis=1)
w_area = np.where(alt, 0.0, area)
log('tris', len(TRI), 'world area %.3f m^2' % (area.sum() * 1e4))

# ============================================================ 1) uniform surface samples (position + ORIGINAL vertex normal), chunked
M = args.samples
pdf = w_area / w_area.sum()
sp = np.empty((M, 3)); sn = np.empty((M, 3))
for c0 in range(0, M, 250000):
    m_ = min(250000, M - c0)
    tri = rng.choice(len(TRI), size=m_, p=pdf)
    r1, r2 = rng.random(m_), rng.random(m_)
    fl = r1 + r2 > 1; r1[fl] = 1 - r1[fl]; r2[fl] = 1 - r2[fl]
    b = np.stack([1 - r1 - r2, r1, r2], 1)
    sp[c0:c0 + m_] = (P[TRI[tri]] * b[..., None]).sum(1)
    n_ = (N[TRI[tri]] * b[..., None]).sum(1); sn[c0:c0 + m_] = n_ / np.maximum(np.linalg.norm(n_, axis=1, keepdims=True), 1e-12)
spacing = np.sqrt(w_area.sum() / args.cells)
log('cells %d, spacing (local) %.5f (~%.2f cm in world), samples per cell %.0f' % (args.cells, spacing, spacing * 1e4, M / args.cells))
embed = lambda pos, nrm: np.hstack([pos, nrm * (args.nw * spacing)]) if args.nw > 0 else pos
E = embed(sp, sn)

# ============================================================ 2) FPS + Lloyd
if args.seeding == 'fps':
    # regular: farthest point sampling (even cell size) -- usually followed by Lloyd (--relax) for round, regular cells
    sub = rng.choice(M, size=min(M, max(200000, 20 * args.cells)), replace=False); Es = E[sub]
    si = [int(rng.integers(len(Es)))]; mind = np.full(len(Es), np.inf)
    for k_ in range(args.cells - 1):
        mind = np.minimum(mind, ((Es - Es[si[-1]]) ** 2).sum(1)); si.append(int(mind.argmax()))
        if args.cells > 2000 and (k_ + 1) % 2000 == 0: log(f'  FPS {k_+1}/{args.cells}')
    seeds = Es[si].copy(); del Es, mind
else:
    # irregular: plain random points on the surface (clumpy, uneven cells). 'varied' additionally modulates the point density with a
    # smooth low-frequency 3D field, so some regions get small cells and others large ones (size ratio ~ exp(density_var)).
    if args.seeding == 'varied':
        K = 7; dirs = rng.normal(size=(K, 3)); dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
        lam = rng.uniform(0.10, 0.45, size=K) * 0.01                    # wavelengths 10..45 cm world (positions are local = world/100)
        ph = rng.uniform(0, 2 * np.pi, size=K); amp = rng.uniform(0.6, 1.0, size=K)
        field = np.zeros(M)
        for k_ in range(K): field += amp[k_] * np.sin(2 * np.pi * (sp @ dirs[k_]) / lam[k_] + ph[k_])
        field /= amp.sum()                                              # in [-1, 1]
        wgt = np.exp(args.density_var * field)
    else:
        wgt = np.ones(M)
    si = rng.choice(M, size=args.cells, replace=False, p=wgt / wgt.sum())
    seeds = E[si].copy(); del wgt
    log(f'seeding={args.seeding}: {args.cells} random seeds' + (f', density field exp(+-{args.density_var}) => cell size varies ~x{np.exp(args.density_var / 2) ** 2:.1f}' if args.seeding == 'varied' else ''))
for it in range(args.relax):
    lab = cKDTree(seeds).query(E, workers=W_)[1]; cnt = np.bincount(lab, minlength=len(seeds)).astype(np.float64); new = seeds.copy()
    for k in range(E.shape[1]):
        sm = np.bincount(lab, weights=E[:, k], minlength=len(seeds)); new[cnt > 0, k] = sm[cnt > 0] / cnt[cnt > 0]
    log(f'Lloyd {it+1}: mean seed shift {np.linalg.norm(new[:, :3] - seeds[:, :3], axis=1).mean() / spacing:.3f} spacing, empty cells {(cnt == 0).sum()}'); seeds = new
tree = cKDTree(seeds); lab = tree.query(E, workers=W_)[1]; ncell = len(seeds)
cnt = np.bincount(lab, minlength=ncell)
cn = np.stack([np.bincount(lab, weights=sn[:, k], minlength=ncell) for k in range(3)], 1)
coh = np.linalg.norm(cn, axis=1) / np.maximum(cnt, 1)
cn /= np.maximum(np.linalg.norm(cn, axis=1, keepdims=True), 1e-12); cn[cnt == 0] = (0, 0, 1)
dev = np.degrees(np.arccos(np.clip((cn[lab] * sn).sum(1), -1, 1)))
log(f'cells {ncell} (empty {(cnt==0).sum()}); coherence mean {coh[cnt>0].mean():.3f} min {coh[cnt>0].min():.3f}; flatten deviation vs original vertex normal: mean {dev.mean():.1f} p95 {np.percentile(dev,95):.1f} max {dev.max():.1f} deg')
del E, sp, sn, lab, dev; gc.collect()

# ============================================================ 3) skinning simulation (prefab pose) -> what Unity shows
def skin_check():
    txt = PREFAB.read_text(encoding='utf-8'); docs = {}
    for mm in re.finditer(r'--- !u!(\d+) &(\d+)\n(.*?)(?=\n--- !u!|\Z)', txt, re.S): docs[int(mm.group(2))] = (int(mm.group(1)), mm.group(3))
    vec = lambda bl, k: [float(g) for g in re.search(k + r': \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)(?:, w: ([-\d.e]+))?\}', bl).groups() if g is not None]
    fid = lambda bl, k: int(re.search(k + r': \{fileID: (-?\d+)', bl).group(1))
    def trs(bl):
        t_, q, s = np.array(vec(bl, 'm_LocalPosition')), vec(bl, 'm_LocalRotation'), np.array(vec(bl, 'm_LocalScale')); x, y, z, w = q
        R = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)], [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)], [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
        Mx = np.eye(4); Mx[:3, :3] = R * s; Mx[:3, 3] = t_; return Mx
    cache = {}
    def l2w(i):
        if i not in cache:
            bl = docs[i][1]; f = fid(bl, 'm_Father'); cache[i] = (l2w(f) if f else np.eye(4)) @ trs(bl)
        return cache[i]
    res = {}
    for did, (cls, bl) in docs.items():
        if cls != 137: continue
        guid = re.search(r'm_Mesh: \{fileID: \d+, guid: (\w+)', bl).group(1)
        mi = next((i for i, m in enumerate(meshes) if m['guid'] == guid), None)
        if mi is None: continue
        bones = [int(x) for x in re.findall(r'- \{fileID: (-?\d+)\}', bl.split('m_Bones:')[1].split('m_BlendShapeWeights')[0])]
        go = fid(bl, 'm_GameObject'); rt = next(t for t, (c, b2) in docs.items() if c == 4 and fid(b2, 'm_GameObject') == go)
        RWi = np.linalg.inv(l2w(rt))
        A = np.array([RWi @ l2w(bn) @ meshes[mi]['bindposes'][k] for k, bn in enumerate(bones)])
        Rm = A[:, :3, :3]; res[mi] = Rm / np.cbrt(np.abs(np.linalg.det(Rm)))[:, None, None]
    return res

skin = skin_check(); sk_report = {}
for mi, m in enumerate(meshes):
    if mi not in skin or m['bw'] is None: continue
    Rm = skin[mi]; w = m['bw'] / np.maximum(m['bw'].sum(1, keepdims=True), 1e-9); idx = m['bi'].astype(np.int64)
    def apply(v):
        out = np.zeros_like(v)
        for k in range(w.shape[1]): out += w[:, k:k + 1] * np.einsum('vij,vj->vi', Rm[idx[:, k]], v)
        return out
    ns = apply(m['nrm']); m['n_skin'] = ns / np.maximum(np.linalg.norm(ns, axis=1, keepdims=True), 1e-12)
    ts = apply(m['tan'][:, :3]); m['t_skin'] = ts / np.maximum(np.linalg.norm(ts, axis=1, keepdims=True), 1e-12)
if all('n_skin' in m for m in meshes):
    ns_all = np.concatenate([m['n_skin'] for m in meshes]); ts_all = np.concatenate([m['t_skin'] for m in meshes]); wv = Tg[:, 3]
    S = cn[tree.query(embed(P, N), workers=W_)[1]]
    ang = lambda a, b: np.degrees(np.arccos(np.clip((a * b).sum(1), -1, 1)))
    Tn = Tg[:, :3] - N * (N * Tg[:, :3]).sum(1, keepdims=True); Tn /= np.maximum(np.linalg.norm(Tn, axis=1, keepdims=True), 1e-12); Bn = wv[:, None] * np.cross(N, Tn)
    tsv = np.stack([(S * Tn).sum(1), (S * Bn).sum(1), (S * N).sum(1)], 1)
    Tk = ts_all - ns_all * (ns_all * ts_all).sum(1, keepdims=True); Tk /= np.maximum(np.linalg.norm(Tk, axis=1, keepdims=True), 1e-12); Bk = wv[:, None] * np.cross(ns_all, Tk)
    S_skin = tsv[:, 0:1] * Tk + tsv[:, 1:2] * Bk + tsv[:, 2:3] * ns_all; S_skin /= np.maximum(np.linalg.norm(S_skin, axis=1, keepdims=True), 1e-12)
    st = lambda a: dict(mean=float(a.mean()), p95=float(np.percentile(a, 95)), over90_pct=float((a > 90).mean() * 100), over45_pct=float((a > 45).mean() * 100))
    sk_report = dict(vertices=int(len(P)), bind_pose_reference_flatten_deviation_deg=st(ang(S, N)), skinning_rotation_of_vertex_normal_deg=st(ang(N, ns_all)),
                     object_space_map_vs_skinned_vertex_normal=st(ang(S, ns_all)), tangent_space_map_vs_skinned_vertex_normal=st(ang(S_skin, ns_all)))
    for k, v in sk_report.items():
        if isinstance(v, dict): log(f'SKIN CHECK {k}: mean {v["mean"]:.1f} p95 {v["p95"]:.1f} deg, >90deg {v["over90_pct"]:.2f}%')
    (OUT / 'skin_check.json').write_text(json.dumps(sk_report, indent=2))
if args.stats_only: raise SystemExit(0)

# ============================================================ 4) rasterise UV1 atlas -> triangle id per texel
S_ = args.size
tid = np.full((S_, S_), -1, dtype=np.int32); uvt = UV1[TRI]
for t in range(len(TRI)):
    a, b_, c = uvt[t] * S_
    x0 = max(int(np.floor(min(a[0], b_[0], c[0]) - 1)), 0); x1 = min(int(np.ceil(max(a[0], b_[0], c[0]) + 1)), S_ - 1)
    y0 = max(int(np.floor(min(a[1], b_[1], c[1]) - 1)), 0); y1 = min(int(np.ceil(max(a[1], b_[1], c[1]) + 1)), S_ - 1)
    if x1 < x0 or y1 < y0: continue
    d = (b_[1] - c[1]) * (a[0] - c[0]) + (c[0] - b_[0]) * (a[1] - c[1])
    if abs(d) < 1e-12: continue
    xs_, ys_ = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    l1 = ((b_[1] - c[1]) * (xs_ - c[0]) + (c[0] - b_[0]) * (ys_ - c[1])) / d; l2 = ((c[1] - a[1]) * (xs_ - c[0]) + (a[0] - c[0]) * (ys_ - c[1])) / d; l3 = 1 - l1 - l2
    m = (l1 >= -1e-6) & (l2 >= -1e-6) & (l3 >= -1e-6); sub_t = tid[y0:y1 + 1, x0:x1 + 1]; sub_t[m & (sub_t < 0)] = t
covered = tid >= 0
ys, xs = np.nonzero(covered); ys = ys.astype(np.int32); xs = xs.astype(np.int32); tt = tid[ys, xs]; del tid
n_tex = len(ys)
log(f'rasterised {n_tex:,} texels ({covered.mean()*100:.1f}%)')

# ============================================================ 5) per texel Voronoi (supersampled), chunked; object + tangent space encoded straight into uint16
def enc16(v): return np.clip(np.round((v * 0.5 + 0.5) * 65535.0), 0, 65535).astype(np.uint16)
enc_os = np.full((S_, S_, 4), 65535, dtype=np.uint16); enc_os[..., :3] = 32768            # outside islands: neutral
enc_ts = np.full((S_, S_, 4), 65535, dtype=np.uint16); enc_ts[..., 0:2] = 32768; enc_ts[..., 2] = 65535   # flat tangent-space normal
cid = np.full((S_, S_), -1, dtype=np.int32)
ss = args.ss
for c0 in range(0, n_tex, args.chunk):
    sl = slice(c0, c0 + args.chunk); yc, xc, tc = ys[sl], xs[sl], tt[sl]
    A = uvt[tc, 0] * S_; B = uvt[tc, 1] * S_; C = uvt[tc, 2] * S_
    den = (B[:, 1] - C[:, 1]) * (A[:, 0] - C[:, 0]) + (C[:, 0] - B[:, 0]) * (A[:, 1] - C[:, 1]); den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    wsign = np.sign(Tg[TRI[tc, 0], 3]); wsign[wsign == 0] = 1
    Pt, Nt, Tt = P[TRI[tc]], N[TRI[tc]], Tg[TRI[tc], :3]                       # (n,3,3)
    def bary(px, py):
        l1 = ((B[:, 1] - C[:, 1]) * (px - C[:, 0]) + (C[:, 0] - B[:, 0]) * (py - C[:, 1])) / den
        l2 = ((C[:, 1] - A[:, 1]) * (px - C[:, 0]) + (A[:, 0] - C[:, 0]) * (py - C[:, 1])) / den
        return np.stack([l1, l2, 1 - l1 - l2], 1)
    acc_os = np.zeros((len(yc), 3)); acc_ts = np.zeros((len(yc), 3))
    for iy in range(ss):
        for ix in range(ss):
            w = bary(xc + (ix + 0.5) / ss, yc + (iy + 0.5) / ss)
            pos = (Pt * w[..., None]).sum(1)
            nr = (Nt * w[..., None]).sum(1); nr /= np.maximum(np.linalg.norm(nr, axis=1, keepdims=True), 1e-12)
            Sx = cn[tree.query(embed(pos, nr), workers=W_)[1]]
            tg = (Tt * w[..., None]).sum(1); tg = tg - nr * (nr * tg).sum(1, keepdims=True); tg /= np.maximum(np.linalg.norm(tg, axis=1, keepdims=True), 1e-12)
            bt = wsign[:, None] * np.cross(nr, tg)
            acc_os += Sx
            acc_ts += np.stack([(Sx * tg).sum(1), (Sx * bt).sum(1), (Sx * nr).sum(1)], 1)
    for a_ in (acc_os, acc_ts): a_ /= np.maximum(np.linalg.norm(a_, axis=1, keepdims=True), 1e-12)
    enc_os[yc, xc, :3] = enc16(acc_os); enc_ts[yc, xc, :3] = enc16(acc_ts)
    w0 = bary(xc + .5, yc + .5); pos0 = (Pt * w0[..., None]).sum(1); nr0 = (Nt * w0[..., None]).sum(1); nr0 /= np.maximum(np.linalg.norm(nr0, axis=1, keepdims=True), 1e-12)
    cid[yc, xc] = tree.query(embed(pos0, nr0), workers=W_)[1]
    log(f'  texels {min(c0 + args.chunk, n_tex):,}/{n_tex:,}')
del ys, xs, tt, uvt; gc.collect()

# margin: nearest covered texel
dist, (iy_, ix_) = ndimage.distance_transform_edt(~covered, return_indices=True)
fill = (~covered) & (dist <= args.margin); del dist
fy, fx = np.nonzero(fill); sy, sx = iy_[fy, fx], ix_[fy, fx]; del iy_, ix_
enc_os[fy, fx] = enc_os[sy, sx]; enc_ts[fy, fx] = enc_ts[sy, sx]; cid[fy, fx] = cid[sy, sx]
valid = covered | fill
log(f'margin {args.margin}px: +{len(fy):,} texels'); del fy, fx, sy, sx

def write_png(enc, fname):
    enc[..., [0, 2]] = enc[..., [2, 0]]                                     # RGBA -> BGRA for cv2 (in place)
    p = OUT / fname; cv2.imwrite(str(p), np.ascontiguousarray(enc[::-1])); log('wrote', p, f'{p.stat().st_size/1e6:.1f} MB'); return p      # top row = v=1
os_path = write_png(enc_os, args.name + '_NormalOS_4K.png'); del enc_os
ts_path = write_png(enc_ts, args.name + '_NormalTS_4K.png'); del enc_ts; gc.collect()

# cell-id preview
hue = (np.arange(ncell) * 0.61803398875) % 1.0
hsv = np.stack([hue * 179, 120 + 100 * ((np.arange(ncell) * 0.37) % 1), 150 + 90 * ((np.arange(ncell) * 0.71) % 1)], 1).astype(np.uint8)
colors = cv2.cvtColor(hsv[None], cv2.COLOR_HSV2BGR)[0]; pv = np.zeros((S_, S_, 3), dtype=np.uint8); pv[valid] = colors[cid[valid]]
bd = np.zeros((S_, S_), bool); bd[:-1] |= (cid[:-1] != cid[1:]) & valid[:-1] & valid[1:]; bd[:, :-1] |= (cid[:, :-1] != cid[:, 1:]) & valid[:, :-1] & valid[:, 1:]; pv[bd] = (20, 20, 20)
cv2.imwrite(str(OUT / 'CellIdAtlasPreview.png'), cv2.resize(np.ascontiguousarray(pv[::-1]), (2048, 2048), interpolation=cv2.INTER_AREA))
cv2.imwrite(str(OUT / 'CellIdAtlasCrop.png'), np.ascontiguousarray(pv[::-1][:1024, :1024]))      # 1:1 crop (top-left 1024^2)

# ============================================================ 6) metas (same import settings as the existing NormalOS texture) + install
ref_meta = (ASSETS / 'Texture/GwenPainterly/Gwen_Painterly_NormalOS_4K.png.meta').read_text(encoding='utf-8'); old_guid = ref_meta.split('guid: ')[1].split()[0]
guids = {}
for p in (os_path, ts_path):
    g = hashlib.md5(('GwenVoronoiNormal' + p.name).encode()).hexdigest(); guids[p.name] = g
    (OUT / (p.name + '.meta')).write_text(ref_meta.replace(old_guid, g), encoding='utf-8')
report = dict(textures=guids, size=S_, cells=int(ncell), relax=args.relax, nw=args.nw, seed=args.seed, margin=args.margin, supersample=ss, samples=M,
              spacing_world_cm=float(spacing * 1e4), normal_coherence_mean=float(coh[cnt > 0].mean()), empty_cells=int((cnt == 0).sum()),
              covered_texels=int(n_tex), meshes=names, seconds=round(time.time() - T0, 1), skin_check=sk_report)
(OUT / 'validation.json').write_text(json.dumps(report, indent=2))
np.savez_compressed(OUT / 'cells.npz', seeds=seeds, cell_normals=cn, spacing=spacing)
if not args.no_install:
    dst = ASSETS / 'Texture/GwenPainterly'
    for p in (os_path, ts_path):
        (dst / p.name).write_bytes(p.read_bytes())
        if not (dst / (p.name + '.meta')).exists(): (dst / (p.name + '.meta')).write_text((OUT / (p.name + '.meta')).read_text(encoding='utf-8'), encoding='utf-8')   # never rewrite an existing meta (keeps GUID)
    log('installed to', dst)
log('done')
