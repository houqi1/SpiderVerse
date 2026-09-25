# Fixed-count silhouette distribution

Each layer exposes `Prefer Silhouette Distribution`, `Front Facing Weight`
(default 0.2), and `Silhouette Distribution Width` (default 0.5).
The original UV noise, threshold, softness and region mask determine the particle
count. Distribution settings relocate that fixed number of instances.
Weight 1 or disabling the feature restores original anchors.

The CPU prepares original anchors plus up to 15 valid alternatives per particle.
Alternatives use area-weighted triangle sampling and the same noise/mask acceptance
rules. By default, all source meshes in the same layer share a GPU pool. If per-renderer
count overrides are used, each renderer/layer gets its own pool so distribution
cannot transfer that renderer's particle budget onto a different renderer.

The update kernel computes both shading normals and geometric triangle normals
from the current deformed vertices. Distribution uses geometric normals; shading,
normal offsets and existing camera fades retain their previous normals.
Every rendering camera builds its own layer weight CDF using its current actual
pose, independently of the stepped line-art camera. A front weight of zero gives
zero weight to triangles whose absolute normal/view dot product is at least Width.
The transition toward tangent triangles is smooth. This is an orientation-based
distribution, not a screen-space outer-contour mask.

GPU weights are quantized to positive integers with a count-dependent scale that
prevents prefix-sum overflow. Exact integer prefix sums ensure zero-weight spans
stay flat; floating-point scans can create false intervals through rounding.
Stratified inverse-CDF sampling relocates exactly the original number of instances
across the layer. It may reuse eligible anchors when support is small, so very
narrow allowed regions can produce overlapping particles. It never uses a
front-facing local fallback while any positive-weight candidate exists elsewhere
in the layer. Only if the ENTIRE eligible layer has zero weight are original
anchors retained to preserve count; no sampler can satisfy both zero front support
and a nonzero fixed count in that case.

Enabled layers store/update 16 samples per instance (80 bytes per GPU sample,
including geometric normals), plus two 4-byte prefix buffers per sample. Per-camera
weight evaluation and a logarithmic number of scan passes replace the old repeated
16-way search in every billboard vertex. Draw counts remain unchanged; no runtime
GPU readback is needed. Hidden source batches receive zero weight. The weight-one
and disabled paths skip distribution compute passes.

Depth testing, transparency, camera-facing fades and frustum visibility still
control which submitted particles are visible. Billboards can overlap front-facing
screen pixels even when their surface anchors are on eligible side-facing triangles.

`SpiderVerse/Validate Fixed Count Distribution` checks actual GPU selection:
zero front-face selections for widths 0.01/0.5/1, perspective/orthographic views,
two viewing directions, fixed counts, disabled/uniform modes and the previous
local-pool failure with the only eligible sample in a different pool/source range.
The test deliberately makes shading normals disagree with geometric normals.
Report: `Temp/ParticleColorValidation/distribution.txt`.

`SpiderVerse/Audit Current Particle Distribution` dispatches the production draw
for current scene effects into a temporary target, then reads their actual pool
and CDF buffers to check the current Scene View's selected face orientations.
It reports active layer settings, original/selected front-facing counts and global
weight support. It does not save or change scene parameters.
Report: `Temp/ParticleColorValidation/distribution-scene.txt`.

## Per-renderer counts

The component Inspector shows `Per Renderer Counts` immediately below Target
Renderers. Each entry has `Override Count`, `Candidate Count`, and the actual
submitted count summed over enabled layers. The independent count is the sample
budget **before noise and mask selection, for each layer**; the existing Threshold
and Softness controls still remove samples. Zero disables particles on that renderer.
With Override Count off, that renderer keeps its original allocation from the
global Candidate Count. Overriding one renderer does not redistribute the remaining
global budget to others.

Settings are keyed by renderer reference, not array position. Independent random
streams preserve another renderer's samples when a count changes, and preserve
overridden samples when Target Renderers is reordered. Clearing all overrides restores
the original global sampling sequence. Duplicate target references are counted once.
Runtime scripts can call `SetRendererCandidateCount(renderer, count)` and
`ClearRendererCandidateCount(renderer)`; the next effect update applies the change.

Count overrides isolate distribution pools per renderer/layer. Consequently, a
renderer with no eligible side-facing candidate cannot borrow candidates from
another renderer. The previously documented empty-support fallback then applies
within that renderer. This count feature does not implement the proposed continuous
triangle resampling replacement; discrete candidate reuse remains a known limitation.

`SpiderVerse/Validate Per Renderer Counts` checks legacy allocation, independent
counts, zero counts, live updates, stability of other renderers' samples, reference
binding through list reordering, pool isolation, noise filtering, and restoration.
Report: `Temp/ParticleColorValidation/renderer-counts.txt`.
