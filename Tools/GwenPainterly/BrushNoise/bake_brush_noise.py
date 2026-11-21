"""Bake a hand-painted (brush stroke) noise map, tileable.

Same idea as the Blender "Paint System" addon's *Brush Painter* image filter (shown in
https://www.youtube.com/watch?v=TWA-CJ4Qf14), i.e. a Hertzmann style layered stroke painter:

  1. source image  = tileable band-limited noise (or --source <image>, treated as tileable)
  2. colour guide  = Gaussian blurred source (--gaussian-sigma, px)
  3. direction     = gradient of the (more strongly) blurred source; the brush is rotated by the gradient angle,
                     so with an upright brush the stroke runs along the iso-lines (--rotation-offset adds degrees)
  4. N steps (--steps) from big brush (--max-scale) to small brush (--min-scale), opacity --start-opacity -> --end-opacity;
     per step random stamps until `coverage_density` of the image area is covered (overlap factor 0.7),
     each stamp takes the colour of the blurred source at its centre
  5. everything wraps around (torus) -> the result tiles seamlessly, which a world-space stroke threshold map needs

This is an independent re-implementation of that published idea (numpy + cv2), it does not contain addon code.
Brushes: procedural dry-bristle strokes by default, or --brush-folder <dir of RGBA/gray PNG stamps> (alpha = mask).
NOTE the addon's own brush PNGs are GPL-3.0; keep them outside the repo unless you are fine with that.

Outputs (Tools/GwenPainterly/BrushNoise/, or --out):
  <name>.png            16-bit gray, linear (import with sRGB OFF, wrap = Repeat)
  <name>_preview.png    source | painted | 2x2 tiling (seam check)
  <name>.json           parameters + seam statistics

  python bake_brush_noise.py --size 2048 --seed 7
"""
import argparse, glob, json, time
from pathlib import Path
import numpy as np, cv2

ap = argparse.ArgumentParser()
ap.add_argument('--size', type=int, default=2048)
ap.add_argument('--seed', type=int, default=7)
ap.add_argument('--name', default='BrushNoise')
ap.add_argument('--out', default=str(Path(__file__).resolve().parent))
ap.add_argument('--source', default=None, help='gray/colour image used as the source instead of generated noise (treated as tileable)')
ap.add_argument('--noise-scale', type=float, default=0.05, help='feature size of the generated source noise, fraction of image size')
ap.add_argument('--octaves', type=int, default=3)
# Brush Painter parameters (defaults = addon defaults, flow sigma is separate here)
ap.add_argument('--coverage-density', type=float, default=0.7)
ap.add_argument('--steps', type=int, default=5)
ap.add_argument('--min-scale', type=float, default=0.05, help='smallest brush, fraction of image size')
ap.add_argument('--max-scale', type=float, default=0.14, help='biggest brush, fraction of image size')
ap.add_argument('--start-opacity', type=float, default=0.4)
ap.add_argument('--end-opacity', type=float, default=1.0)
ap.add_argument('--gaussian-sigma', type=float, default=3.0, help='blur (px) of the colour the stamps pick up')
ap.add_argument('--flow-sigma', type=float, default=28.0, help='blur (px) before the gradient that drives the stroke direction')
ap.add_argument('--gradient-threshold', type=float, default=0.0, help='skip stamps where normalised gradient magnitude is below this')
ap.add_argument('--rotation-offset', type=float, default=0.0, help='degrees added to the gradient angle')
ap.add_argument('--random-rotation', type=float, default=30.0, help='+-range/2 degrees of random extra rotation per stamp')
ap.add_argument('--rotation-bins', type=int, default=180)
ap.add_argument('--brush-folder', default=None)
ap.add_argument('--procedural-brushes', type=int, default=12)
ap.add_argument('--stroke-tone', type=float, default=0.25, help='how strongly the brush bristle streaks modulate the stamp colour (0 = flat colour like the addon)')
ap.add_argument('--stamp-jitter', type=float, default=0.16, help='random +- tone offset per stamp')
ap.add_argument('--brush-aspect', type=float, default=0.35, help='procedural brush width / length (smaller = longer strokes)')
ap.add_argument('--contrast', type=float, default=1.0, help='stretch of the source around 0.5 before painting')
ap.add_argument('--no-equalize', action='store_true', help='keep the painted values; default rank-equalises to a uniform histogram (so threshold = coverage)')
args = ap.parse_args()

T0 = time.time()
log = lambda *a: print(f'[{time.time() - T0:6.1f}s]', *a, flush=True)
rng = np.random.default_rng(args.seed)
S = args.size
H = W = S


# ------------------------------------------------------------------ periodic helpers
def periodic_gaussian(img, sigma):
    """Gaussian blur with wrap-around (FFT), keeps tileability."""
    if sigma <= 0:
        return img.astype(np.float32, copy=True)
    h, w = img.shape
    fy = np.fft.fftfreq(h).astype(np.float32)[:, None]
    fx = np.fft.rfftfreq(w).astype(np.float32)[None, :]
    g = np.exp(np.float32(-2.0 * np.pi ** 2 * sigma ** 2) * (fx ** 2 + fy ** 2))   # float32 + rfft: low memory (box is near its commit limit)
    spec = np.fft.rfft2(img.astype(np.float32))
    spec *= g
    return np.fft.irfft2(spec, s=(h, w)).astype(np.float32)


def normalise01(a, lo=1.0, hi=99.0):
    l, h = np.percentile(a, [lo, hi])
    return np.clip((a - l) / max(h - l, 1e-8), 0.0, 1.0).astype(np.float32)


def make_source():
    if args.source:
        im = cv2.imread(args.source, cv2.IMREAD_UNCHANGED)
        if im.ndim == 3:
            im = cv2.cvtColor(im[..., :3], cv2.COLOR_BGR2GRAY)
        im = im.astype(np.float32) / (65535.0 if im.dtype == np.uint16 else 255.0)
        return cv2.resize(im, (W, H), interpolation=cv2.INTER_AREA)
    acc = np.zeros((H, W), np.float32)
    amp, sig = 1.0, args.noise_scale * S
    for _ in range(args.octaves):
        n = periodic_gaussian(rng.standard_normal((H, W)).astype(np.float32), sig)
        acc += amp * (n / max(n.std(), 1e-8))
        amp *= 0.5
        sig *= 0.5
    return normalise01(acc)


# ------------------------------------------------------------------ brushes
def procedural_brush(r, size=192):
    """Upright dry-bristle stroke (long axis = image vertical), alpha mask in [0, 1]."""
    L = size                                     # length
    Wd = max(8, int(size * args.brush_aspect * r.uniform(0.8, 1.25)))   # width
    v = np.linspace(-1, 1, L)[:, None]           # along stroke
    u = np.linspace(-1, 1, Wd)[None, :]          # across
    bend = r.uniform(-0.35, 0.35)
    uu = u - bend * v ** 2                       # curved stroke
    uu = uu / np.sqrt(np.clip(1.0 - np.abs(v) ** 3, 0.04, 1.0))   # round both ends (otherwise stamps look like rectangles)
    # ragged side edges
    edge = 1.0 - 0.18 * r.random() - 0.12 * np.abs(np.convolve(r.standard_normal(L), np.ones(9) / 9, 'same'))[:, None] * 3
    body = np.clip((edge - np.abs(uu) ** 3) * 6.0, 0, 1)
    # tapered ends, tail frayed
    head = np.clip((1.0 - np.abs(v)) * r.uniform(4, 8), 0, 1)
    tail_fray = np.clip((1.0 - np.maximum(v, 0) * 1.0), 0, 1)
    # bristle streaks (1D noise across, slowly drifting along)
    k = Wd
    streak = r.standard_normal(k)
    streak = np.convolve(streak, np.ones(3) / 3, 'same')
    streak = (streak - streak.min()) / max(np.ptp(streak), 1e-8)
    drift = periodic_gaussian(r.standard_normal((L, Wd)).astype(np.float32), 6.0)
    drift /= max(drift.std(), 1e-8)
    bristle = np.clip(0.55 + 0.7 * (streak[None, :] - 0.5) + 0.12 * drift, 0, 1)
    dry = np.clip(1.0 - np.maximum(v, 0) * r.uniform(0.2, 0.6) * (1.0 - bristle) * 3.0, 0, 1)   # dry brush skips towards the end
    a = body * head * tail_fray ** 0.5 * bristle * dry
    a = cv2.GaussianBlur(a.astype(np.float32), (0, 0), 0.8)
    tone_l = (streak[None, :] - 0.5) * 2.0 + 0.0 * drift               # bristle streak light/dark across the stroke, ~[-1, 1] (no fine grain)
    tone_l = cv2.GaussianBlur(tone_l.astype(np.float32), (0, 0), 1.5)
    out = np.zeros((L, L), np.float32)           # pad to square (centre), like the addon does for non-square brushes
    tone = np.zeros((L, L), np.float32)
    x0 = (L - Wd) // 2
    out[:, x0:x0 + Wd] = a
    tone[:, x0:x0 + Wd] = tone_l
    return np.clip(out * 1.15, 0, 1), np.clip(tone, -1, 1)


def load_brushes():
    if args.brush_folder:
        fs = []
        for e in ('png', 'jpg', 'jpeg', 'bmp', 'tif', 'tiff'):
            fs += glob.glob(str(Path(args.brush_folder) / f'*.{e}'))
        out = []
        for f in sorted(fs):
            im = cv2.imread(f, cv2.IMREAD_UNCHANGED)
            if im is None:
                continue
            a = im[..., 3] if im.ndim == 3 and im.shape[2] == 4 else (cv2.cvtColor(im[..., :3], cv2.COLOR_BGR2GRAY) if im.ndim == 3 else im)
            a = np.clip(a.astype(np.float32) / (65535.0 if a.dtype == np.uint16 else 255.0), 0, 1)
            h, w = a.shape
            m = max(h, w)
            sq = np.zeros((m, m), np.float32)
            sq[(m - h) // 2:(m - h) // 2 + h, (m - w) // 2:(m - w) // 2 + w] = a
            out.append((sq, np.zeros_like(sq)))
        if out:
            log(f'loaded {len(out)} brushes from {args.brush_folder}')
            return out
        log('brush folder empty, falling back to procedural brushes')
    br = np.random.default_rng(args.seed + 1000)
    return [procedural_brush(br) for _ in range(args.procedural_brushes)]


rot_cache = {}


def rotated(brush_id, brush, angle_deg):
    b = int(round((angle_deg % 360.0) / 360.0 * args.rotation_bins)) % args.rotation_bins
    key = (brush_id, b)
    r = rot_cache.get(key)
    if r is None:
        a = b / args.rotation_bins * 360.0
        h, w = brush[0].shape
        c = (w / 2.0, h / 2.0)
        # cv2 positive angle = counter-clockwise on screen; our angles are image coords (x -> y), hence the minus
        M = cv2.getRotationMatrix2D(c, -a, 1.0)
        cs, sn = abs(M[0, 0]), abs(M[0, 1])
        nw, nh = int(np.ceil(h * sn + w * cs)), int(np.ceil(h * cs + w * sn))
        M[0, 2] += nw / 2.0 - c[0]
        M[1, 2] += nh / 2.0 - c[1]
        r = tuple(cv2.warpAffine(b, M, (nw, nh), flags=cv2.INTER_LINEAR, borderValue=0) for b in brush)
        rot_cache[key] = r
    return r


# ------------------------------------------------------------------ painter
src = make_source()
if args.contrast != 1.0:
    src = np.clip((src - 0.5) * args.contrast + 0.5, 0, 1).astype(np.float32)
color = periodic_gaussian(src, args.gaussian_sigma)
flow = periodic_gaussian(src, args.flow_sigma)
gx = (np.roll(flow, -1, 1) - np.roll(flow, 1, 1)) * 0.5
gy = (np.roll(flow, -1, 0) - np.roll(flow, 1, 0)) * 0.5
mag = np.hypot(gx, gy)
mag = mag / max(float(mag.max()), 1e-8)
theta = np.degrees(np.arctan2(gy, gx))
log('source + flow field done')

brushes = load_brushes()
canvas = src.copy()                               # overlay on input, like the addon
stamps_total = 0
for step in range(args.steps):
    t = 0.0 if args.steps == 1 else step / (args.steps - 1)
    scale = args.min_scale if args.steps == 1 else args.max_scale + (args.min_scale - args.max_scale) * t
    opacity = args.end_opacity if args.steps == 1 else args.start_opacity + (args.end_opacity - args.start_opacity) * t
    bsize = max(2, int(scale * S))
    sized = [tuple(cv2.resize(x, (bsize, bsize), interpolation=cv2.INTER_AREA if bsize < x.shape[0] else cv2.INTER_LINEAR) for x in b) for b in brushes]
    avg_area = float(np.mean([(b[0] > 0).sum() for b in sized]))
    n = int(H * W * args.coverage_density / max(avg_area * 0.7, 1.0))
    n = max(50, min(n, H * W // 8))
    ys = rng.integers(0, H, n)
    xs = rng.integers(0, W, n)
    bi = rng.integers(0, len(sized), n)
    rr = rng.uniform(-0.5, 0.5, n) * args.random_rotation
    jit = rng.uniform(-1, 1, n) * args.stamp_jitter
    used = 0
    for k in range(n):
        y, x = int(ys[k]), int(xs[k])
        if mag[y, x] < args.gradient_threshold:
            continue
        m = rotated((step, int(bi[k])), sized[bi[k]], theta[y, x] + args.rotation_offset + rr[k])
        m, tone = m
        bh, bw = m.shape
        iy = (np.arange(bh) + y - bh // 2) % H
        ix = (np.arange(bw) + x - bw // 2) % W
        idx = np.ix_(iy, ix)
        a = m * opacity
        c = color[y, x] + jit[k] + args.stroke_tone * 0.5 * tone
        canvas[idx] = canvas[idx] * (1.0 - a) + c * a
        used += 1
    stamps_total += used
    log(f'step {step + 1}/{args.steps}: brush {bsize}px opacity {opacity:.2f} stamps {used}/{n}')

painted = np.clip(canvas, 0, 1)
if not args.no_equalize:
    order = np.argsort(painted.ravel(), kind='stable')
    ranks = np.empty(order.size, np.float32)
    ranks[order] = np.arange(order.size, dtype=np.float32) / (order.size - 1)
    painted = ranks.reshape(H, W)
else:
    painted = np.clip(painted, 0, 1)


# ------------------------------------------------------------------ outputs
def seam_stats(a):
    inner_x = float(np.abs(a[:, 1:] - a[:, :-1]).mean())
    inner_y = float(np.abs(a[1:] - a[:-1]).mean())
    return dict(wrap_x=float(np.abs(a[:, 0] - a[:, -1]).mean()), inner_x=inner_x,
                wrap_y=float(np.abs(a[0] - a[-1]).mean()), inner_y=inner_y)


out = Path(args.out)
out.mkdir(parents=True, exist_ok=True)
cv2.imwrite(str(out / f'{args.name}.png'), np.round(painted * 65535.0).astype(np.uint16))
half = 768
tile = np.tile(cv2.resize(painted, (half, half), interpolation=cv2.INTER_AREA), (2, 2))
left = cv2.resize(src, (1536 // 2, 1536 // 2), interpolation=cv2.INTER_AREA)
mid = cv2.resize(painted, (1536 // 2, 1536 // 2), interpolation=cv2.INTER_AREA)
col = np.vstack([left, mid])
prev = np.hstack([col, tile[:1536, :1536]])
cv2.imwrite(str(out / f'{args.name}_preview.png'), np.round(prev * 255.0).astype(np.uint8))
stats = seam_stats(painted)
(out / f'{args.name}.json').write_text(json.dumps(dict(args=vars(args), stamps=stamps_total, seam=stats), indent=2), encoding='utf-8')
log('seam (wrap vs inner neighbour diff):', {k: round(v, 5) for k, v in stats.items()})
log('wrote', out / f'{args.name}.png')
