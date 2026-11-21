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
Every rendering camera builds its own layer weight CDF using the same held camera
position, forward direction and projection mode as line art. Camera data advances
on Line Art's Update Rate, in sync with particle facing fades and silhouette size
boosts. Surface deformation and rendering still update normally between camera
samples; screen projection and color/depth sampling use the current render view.
A front weight of zero gives
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

## Triangle area sizing

Each layer also offers `Size By Triangle Area` (off by default). When enabled,
the square root of the current world-space triangle area replaces the random size
factor and maps into the sorted `Particle Size` endpoints. The reference minimum
and maximum are captured from nondegenerate triangles per renderer on rebuild;
deformation can change sizes between those bounds without renormalizing the whole
mesh each frame. Equal-area meshes use the midpoint. This is a relative mapping,
not an absolute area-to-size multiplier. Silhouette redistribution preserves the
selected triangle's size in this mode. With the switch off, original random sizes
are retained. The camera silhouette size boost, if enabled, still applies afterwards.

## Surface-aligned billboard rotation

Each layer exposes `Align Billboard To Surface` (default off), `Billboard Surface
Direction` (UV U or V, default V), `Billboard Rotation Offset` (-180 to 180 degrees),
and `Billboard Rotation Follow Speed` (default 20). The billboard's local vertical
axis follows the selected surface UV direction. The GPU reconstructs that direction
from the current deformed triangle edges and UVs, then projects it into each camera's
view plane. Degenerate UV triangles fall back to their longest of the two anchor edges.
UV seams and differently oriented islands can produce different stroke directions;
this does not infer anatomical flow independently of the UV layout.

The quad stays camera-facing and its texture coordinates remain unchanged, so both
the dot and noise atlas rotate together. Per-camera GPU angle histories retain the
last orientation at a vanishing projection and smoothly approach valid directions.
The initial fallback is upright. Rotation offsets are applied after smoothing.
Perspective projection accounts for off-center anchors; orthographic views use
the projected surface direction directly. Projection follows the current render
view, like the billboard axes; the existing line-art-stepped facing/distribution
camera data is unchanged. Redistribution uses the selected anchor's direction.

The 80-byte particle layout is unchanged. Each candidate adds a 16-byte direction,
each batch stores UVs (8 bytes/vertex), and active rotation stores 8 bytes per drawn
particle per camera. Enabled rotation adds one compute dispatch per visible batch.
Camera histories are released on batch rebuild/disposal and when cameras are destroyed.

Run `SpiderVerse/Validate Surface Billboard Rotation` for GPU checks of deformed
UV directions, orthographic/perspective projection, smoothing, and pole history.
The report is written to `Temp/BillboardRotationValidation/gpu.txt`.

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
