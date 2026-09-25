# Particle color from the visible character

`SurfaceNoiseParticleEffect` now defaults to Jump Flood color sampling for materials
with **Sample Character Color** enabled. The existing anchor path is retained for
comparison and unsupported texture/compute targets.

## Controls

On the effect component, under **Screen-space particle color (Jump Flood)**:

- **Use Jump Flood Color**: switch between the new pixel-based field and the previous
  anchor lookup without changing particle distribution, size, noise or opacity.
- **Color Extension Pixels**: maximum source distance in camera render pixels
  (default 128). The last four pixels fade out. Increase if particles extend past
  the field, especially in close-ups.
- **Half Resolution Color Field**: default on. Only source-coordinate selection is
  reduced; the source color and visibility mask stay full resolution. Disable to
  compare thin contours and detailed color boundaries.

## Rendering

At the existing `BeforeRenderingTransparents` particle stage, copy active HDR color
once before any surface particles draw. This avoids both the pipeline's half-size
opaque texture and feedback from earlier particle layers/characters. Later line
art/post-processing are not baked into this source; their existing order is retained.

Each effect draws its opaque target renderers to a full-size depth-valued mask.
Only fragments matching scene depth are retained. Seed candidates must have a valid
3x3 neighborhood with reasonably continuous depth, keeping sources one pixel inside
the visible silhouette. This mask matches the current opaque Toon geometry; future
alpha-cutout or vertex-displaced source shaders need matching mask logic.

The coordinate field is cropped to projected renderer bounds plus the extension
margin. A near-plane intersection conservatively uses the full screen. Coordinates
are stored as full-resolution `pixel + 1` in `R16G16_UInt`; zero means invalid. A
bounded Jump Flood sweep is followed by 4/2/1 local repair passes. Each effect's layers
share this field; different effects cannot borrow one another's seeds.

Particle fragments inside the mask load the current pixel's source color. Exterior
fragments compare four nearby field candidates, then load the nearest source color.
No coordinate or color interpolation is used. Invalid/out-of-range sources fade out,
and a foreground depth check prevents color extending onto nearer occluders.

All textures are RenderGraph-managed, per camera. Source snapshot, mask, JFA and
particle passes expose profiling markers. There is no runtime GPU readback.
At the default half resolution and 128px radius, propagation uses seven large-to-small
steps plus three repair steps; initialization is separate. Two coordinate buffers
cost eight bytes per field pixel together. The full-resolution HDR snapshot and
depth-valued mask are additional costs. GPU duration must be measured on target hardware.

## Verification

- **SpiderVerse > Validate Particle Jump Flood** runs the actual compute shaders and
  compares their output with a brute-force nearest-source reference. Cases cover
  full/half resolution, odd dimensions, nonzero crop origin, separated regions,
  a visibility gap and an empty mask. Results go to
  `Temp/ParticleColorValidation/jump-flood.txt`.
- **SpiderVerse > Compare Particle Color (Scene View)** renders a temporary camera
  from the current view with anchor sampling and half/full-resolution Jump Flood,
  then checks an orbit and orthographic projection. The original effect switches
  and Scene View pose are preserved. PNGs go to `Temp/ParticleColorValidation`.

JFA remains approximate. Seed erosion can omit very thin visible features, and
nearest-source selection can form color boundaries where different contours meet.
The original alpha blending, depth fade settings and billboard texture edges remain
unchanged; this change specifically isolates the effect of color sampling.
