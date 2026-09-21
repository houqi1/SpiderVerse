using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpiderVerse.LineArt
{
    [Serializable]
    public class LineArtAppearance
    {
        [Header("Stroke appearance")]
        [Tooltip("Scale width, noise and offsets with the character projection. Disable for fixed screen pixels.")]
        public bool scaleWithDistance = true;
        [Min(.00001f), Tooltip("World-space size of one appearance unit. Width, noise and offsets share this scale. Default: 0.005 world units.")]
        public float sizeUnit = .005f;
        public Color color = new Color(0.188f, 0.157f, 0.125f, 1);
        [Tooltip("Stroke width in appearance units; scales with camera distance when Scale With Distance is enabled. End taper can reduce width along a stroke.")]
        [Range(0.1f,32)] public float thickness = 2;
        public bool thicknessCurve = true;
        [Range(0,1)] public float endTaper = .25f;
        [Range(.25f,4)] public float thicknessTransition = 1;
        [Range(0,.9f)] public float lengthTrim;
        [Range(0,.9f), Tooltip("0.4 = 60–140% of the trimmed whole stroke. Stable per-stroke random draw.")]
        public float lengthRandomness = .4f;
        [Range(0,12)] public float noise;
        [Range(.1f,32), Tooltip("Noise cycles along one complete stroke. 3 preserves the original frequency.")]
        public float noiseFrequency = 3;
        [Tooltip("Whole layer screen translation. +X right, +Y down, in appearance units; follows Scale With Distance.")]
        public Vector2 offset;
        [Tooltip("Per-stroke random screen translation in appearance units; follows Scale With Distance. X/Y are maximum absolute offsets; each connected stroke moves as a whole. Stable while stroke identity is unchanged. Zero disables it.")]
        public Vector2 randomOffset;
        [Header("Texture")]
        [Tooltip("Drag a Texture2D asset here, including imported .tga. No Read/Write requirement.")]
        public Texture2D texture;
        [Tooltip("Opacity mask only: off = white opaque / black transparent; on = black opaque / white transparent. Texture alpha always multiplies the mask.")]
        public bool darkOnWhiteMask;
        [Range(-180,180), Tooltip("Rotate texture sampling around the center of the stroke UVs, in degrees. Does not rotate the stroke geometry.")]
        public float textureRotation;
        [Tooltip("Texture UV scale after rotation. (1, 1) preserves the original scale. X also multiplies Texture Repeats; negative values mirror the mask.")]
        public Vector2 textureTiling = Vector2.one;
        [Tooltip("Texture UV translation after rotation and tiling. 1 = one texture period. Moves the opacity mask, not the stroke geometry.")]
        public Vector2 textureOffset;
        [Range(0,1)] public float textureStrength = 1;
        [Range(1,30)] public float textureRepeats = 1;
        public bool Subdivide => thicknessCurve || noise > 0;
        public int CurveSamples => noise > 0 ? Mathf.Max(16, Mathf.CeilToInt(Mathf.Clamp(noiseFrequency,.1f,32)*8)) : 16;
        public int StrokeHash(){unchecked{return ((lengthTrim.GetHashCode()*31+lengthRandomness.GetHashCode())*31+(Subdivide?1:0))*31+(Subdivide?CurveSamples:0);}}
        public bool SameGeometry(LineArtAppearance other)=>other!=null&&lengthTrim.Equals(other.lengthTrim)&&lengthRandomness.Equals(other.lengthRandomness)&&Subdivide==other.Subdivide&&(!Subdivide||CurveSamples==other.CurveSamples);
        internal bool SameAppearance(LineArtAppearance other)=>SameGeometry(other)&&scaleWithDistance==other.scaleWithDistance&&sizeUnit.Equals(other.sizeUnit)&&color==other.color&&thickness.Equals(other.thickness)&&thicknessCurve==other.thicknessCurve&&endTaper.Equals(other.endTaper)&&thicknessTransition.Equals(other.thicknessTransition)&&noise.Equals(other.noise)&&noiseFrequency.Equals(other.noiseFrequency)&&offset==other.offset&&randomOffset==other.randomOffset&&texture==other.texture&&darkOnWhiteMask==other.darkOnWhiteMask&&textureRotation.Equals(other.textureRotation)&&textureTiling==other.textureTiling&&textureOffset==other.textureOffset&&textureStrength.Equals(other.textureStrength)&&textureRepeats.Equals(other.textureRepeats);
        public LineArtAppearance CopyAppearance(){var result=new LineArtAppearance();CopyTo(result);return result;}
        public void CopyTo(LineArtAppearance target)
        {
            target.scaleWithDistance=scaleWithDistance;target.sizeUnit=sizeUnit;target.color=color;target.thickness=thickness;target.thicknessCurve=thicknessCurve;target.endTaper=endTaper;target.thicknessTransition=thicknessTransition;
            target.lengthTrim=lengthTrim;target.lengthRandomness=lengthRandomness;target.noise=noise;target.noiseFrequency=noiseFrequency;target.offset=offset;target.randomOffset=randomOffset;
            target.texture=texture;target.darkOnWhiteMask=darkOnWhiteMask;target.textureRotation=textureRotation;target.textureTiling=textureTiling;target.textureOffset=textureOffset;target.textureStrength=textureStrength;target.textureRepeats=textureRepeats;
        }
    }

    [Serializable]
    public sealed class LineArtSettings : LineArtAppearance
    {
        [Header("Feature edges")]
        public bool contour = true, crease = true, materialBorders = true, boundaries = false;
        public bool intersections = true, occlusion = true;
        [Range(0,180), Tooltip("Blender surface angle; 120 selects normal changes above 60 degrees.")]
        public float creaseAngle = 120;
        [Header("Stroke connections")]
        [Tooltip("Allow contour, crease and material edges to join at a shared mesh vertex. Does not join separate objects or occlusion cuts.")]
        public bool connectDifferentEdgeTypes;
        [Tooltip("Continue through branch vertices using mutually best aligned edges in screen space.")]
        public bool connectJunctions;
        [Range(0,90), Tooltip("Maximum screen-space turn at a branch. 0 = straight only. Ordinary two-edge vertices keep their original connection.")]
        public float junctionMaxAngle = 20;
        [Header("Performance")]
        [Range(1,60)] public int updateRate = 30;
        [Tooltip("Include other opaque scene meshes as CPU occluders. Terrain/particles are not CPU occluders.")]
        public LayerMask occluderLayers = ~0;
        [Min(.000001f)] public float depthEpsilon = .00001f;
        public int GeometryHash()=>unchecked(ExtractionHash()*31+StrokeHash());
        public LineArtSettings WithAppearance(LineArtAppearance appearance){var result=Copy();appearance.CopyTo(result);return result;}
        public LineArtSettings Copy() => (LineArtSettings)MemberwiseClone();
        public int ExtractionHash()
        {
            unchecked { int h = contour ? 1 : 0;
                h=h*31+(crease?1:0); h=h*31+(materialBorders?1:0); h=h*31+(boundaries?1:0);
                h=h*31+(intersections?1:0); h=h*31+(occlusion?1:0); h=h*31+creaseAngle.GetHashCode();
                h=h*31+(connectDifferentEdgeTypes?1:0); h=h*31+(connectJunctions?1:0);
                h=h*31+junctionMaxAngle.GetHashCode();
                h=h*31+occluderLayers.value;
                return h*31+depthEpsilon.GetHashCode(); }
        }
    }

    [Serializable]
    public sealed class LineArtLayer
    {
        public string name = "Layer";
        public bool enabled = true;
        [Tooltip("Use the Source renderer selection. Disable to assign this layer's own renderer list; an empty custom list draws nothing.")]
        public bool useSourceRenderers = true;
        public Renderer[] renderers = Array.Empty<Renderer>();
        public LineArtAppearance appearance = new LineArtAppearance();
    }

    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Rendering/Object Line Art Source")]
    public sealed class ObjectLineArtSource : MonoBehaviour
    {
        [Tooltip("Empty = all MeshRenderer/SkinnedMeshRenderer children. Otherwise only these renderers.")]
        public Renderer[] renderers = Array.Empty<Renderer>();
        [Tooltip("Empty preserves the original single layer using the Renderer Feature appearance. Layers draw in list order, later layers on top.")]
        public LineArtLayer[] layers = Array.Empty<LineArtLayer>();
        internal static bool HasLayers { get {foreach(var source in Active)if(source&&source.isActiveAndEnabled&&source.layers!=null&&source.layers.Length>0)return true;return false;} }
        internal static readonly HashSet<ObjectLineArtSource> Active = new HashSet<ObjectLineArtSource>();
        void OnEnable() { Active.Add(this); }
        void OnDisable() { Active.Remove(this); }
        internal Renderer[] GetRenderers() => renderers != null && renderers.Length > 0 ? renderers : GetComponentsInChildren<Renderer>();
    }
}
