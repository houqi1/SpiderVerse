ToonCel PBR specular validation

Material controls (Assets/Shader/ToonCel.shader):
- Enable PBR Specular: local shader keyword, disabled by default.
- Specular Roughness: perceptual roughness; GGX alpha = roughness squared.
- Specular F0: RGB linear reflectance, default 0.04. W is unused.
- Specular Metallic: blends F0 toward the base color for the specular layer only.
- Specular Intensity: scales direct specular radiance.
- Specular Shape Clamp Amount: 0 = unmasked BRDF; 1 = full smoothstep mask.
- Shape Threshold / Smoothness: contour position / edge width on normalized GGX.

The full specular BRDF is D * F * G / (4 * N.L * N.V), with GGX D,
height-correlated Smith G and Schlick Fresnel F. The shape mask multiplies
the BRDF before N.L, light color, distance attenuation and shadow attenuation.
The existing cel diffuse is retained, so the combined artistic shader is not
an energy-conserving metallic/roughness material. No environment specular/IBL
is added. Specular always uses the model-space normal atlas (_WrapNormalMap),
with its UV, strength and optional skinned tangent-basis encoding settings.
This does not require enabling Cel Diffuse Use Model Space Normal Map; that
toggle controls cel diffuse only. Invalid atlas texels use the geometric
normal for specular. The tangent-space _BumpMap does not affect specular.

Reference for GGX and Smith Lambda:
https://pbr-book.org/4ed/Reflection_Models/Roughness_Using_Microfacet_Theory

Numeric check:
  python Tools/ToonCelPBRValidation/check_brdf.py
Outputs numeric-result.json. Compares float32 equations with a double-precision
Lambda-based reference over 200,000 samples, including sharp and grazing cases.

Unity compile check:
Use the isolated Tools/VideoNormalPreview/UnitySkinning/ValidationProject.
Stage ToonCel.shader, ToonCelInput.hlsl and SkinnedNormalBasis.hlsl into its
Assets/ToonCelPBRCheck folder. Rename the staged Shader declaration to
Hidden/ToonCelPBRCheck, and place ValidateToonCelPBR.cs in that folder's Editor
subfolder. Run Unity in batch mode with -executeMethod ValidateToonCelPBR.Run.
The original shader and active editor scene are not changed by this check.
The temporary staging folder can be removed after Unity exits.
Outputs compile-result.txt and unity-compile.log in this directory.
