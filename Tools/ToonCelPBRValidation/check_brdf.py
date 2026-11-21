"""Numerical checks of the shader equations against the Smith Lambda formulation."""
from pathlib import Path
import json
import numpy as np

rng = np.random.default_rng(124)
count = 200_000
nl = np.exp(rng.uniform(np.log(1.01e-6), 0, count)).astype(np.float32)
nv = np.exp(rng.uniform(np.log(1.01e-6), 0, count)).astype(np.float32)
nh = rng.uniform(0, 1, count).astype(np.float32)
nh[:100] = 1  # Include the sharpest peak.
vh = rng.uniform(0, 1, count).astype(np.float32)
roughness = rng.uniform(0.05, 1, count).astype(np.float32)
roughness[:100] = 0.05
f0 = rng.uniform(0, 1, count).astype(np.float32)

alpha_sq = roughness**4
d = (1 - nh**2) + nh**2 * alpha_sq
D = alpha_sq / (np.pi * d**2)
G = 2 * nl * nv / (
    nv * np.sqrt(alpha_sq + (1 - alpha_sq) * nl**2)
    + nl * np.sqrt(alpha_sq + (1 - alpha_sq) * nv**2)
)
F = f0 + (1 - f0) * (1 - vh)**5
brdf = D * G * F / (4 * nl * nv)
assert np.isfinite(brdf).all() and (brdf >= 0).all()
assert ((G >= 0) & (G <= 1.000001)).all()

# Independent double-precision reference using Lambda, not the shader's
# algebraically rearranged G. These cosines cover grazing and normal incidence.
nl64, nv64, nh64, vh64, r64, f64 = [x.astype(np.float64) for x in (nl, nv, nh, vh, roughness, f0)]
a64 = r64**4
lambda_l = (np.sqrt(1 + a64 * (1 / nl64**2 - 1)) - 1) / 2
lambda_v = (np.sqrt(1 + a64 * (1 / nv64**2 - 1)) - 1) / 2
g_ref = 1 / (1 + lambda_l + lambda_v)
d_ref = a64 / (np.pi * ((1 - nh64**2) + nh64**2 * a64)**2)
f_ref = f64 + (1 - f64) * (1 - vh64)**5
reference = d_ref * g_ref * f_ref / (4 * nl64 * nv64)
relative_error = np.abs(brdf - reference) / np.maximum(reference, 1e-20)
assert relative_error.max() < 0.003, relative_error.max()

shape = np.clip(alpha_sq**2 / d**2, 0, 1)
assert np.isfinite(shape).all() and ((shape >= 0) & (shape <= 1)).all()
assert np.allclose(shape[:100], 1)
for threshold in (0, 0.1, 0.5, 1):
    for smoothness in (0, 0.02, 0.5):
        edge = max(smoothness, 1e-5)
        t = np.clip((shape - (threshold - edge)) / (2 * edge), 0, 1)
        mask = t * t * (3 - 2 * t)
        assert np.isfinite(mask).all() and ((mask >= 0) & (mask <= 1)).all()
        assert np.all(brdf * mask <= brdf * 1.000001)

report = {
    "samples": count,
    "reference": "Double-precision GGX + height-correlated Smith Lambda + Schlick Fresnel",
    "max_relative_error": float(relative_error.max()),
    "finite_nonnegative_brdf": True,
    "bounded_geometry_and_shape_masks": True,
    "zero_smoothness_and_grazing_checks": "PASS",
}
Path(__file__).with_name("numeric-result.json").write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
