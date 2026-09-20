using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpiderVerse.LineArt
{
    [Serializable]
    public sealed class LineArtSettings
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
        [Header("Stroke appearance (pixels)")]
        public Color color = new Color(0.188f, 0.157f, 0.125f, 1);
        [Range(0.1f,32)] public float thickness = 2;
        public bool thicknessCurve = true;
        [Range(0,1)] public float endTaper = .25f;
        [Range(.25f,4)] public float thicknessTransition = 1;
        [Range(0,.9f)] public float lengthTrim;
        [Range(0,.9f), Tooltip("0.4 = 60–140% of the trimmed whole stroke. Stable per-stroke random draw.")]
        public float lengthRandomness = .4f;
        [Range(0,12)] public float noise;
        [Tooltip("Whole layer screen translation. +X right, +Y down, in render pixels.")]
        public Vector2 offset;
        [Tooltip("Per-stroke random screen translation in pixels. X/Y are maximum absolute offsets; each connected stroke moves as a whole. Stable while stroke identity is unchanged. Zero disables it.")]
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
        [Header("Performance")]
        [Range(1,60)] public int updateRate = 30;
        [Tooltip("Include other opaque scene meshes as CPU occluders. Terrain/particles are not CPU occluders.")]
        public LayerMask occluderLayers = ~0;
        [Min(.000001f)] public float depthEpsilon = .00001f;
        public LineArtSettings Copy() => (LineArtSettings)MemberwiseClone();
        public int GeometryHash()
        {
            unchecked { int h = contour ? 1 : 0;
                h=h*31+(crease?1:0); h=h*31+(materialBorders?1:0); h=h*31+(boundaries?1:0);
                h=h*31+(intersections?1:0); h=h*31+(occlusion?1:0); h=h*31+creaseAngle.GetHashCode();
                h=h*31+(connectDifferentEdgeTypes?1:0); h=h*31+(connectJunctions?1:0);
                h=h*31+junctionMaxAngle.GetHashCode();
                h=h*31+lengthTrim.GetHashCode(); h=h*31+lengthRandomness.GetHashCode();
                h=h*31+(thicknessCurve?1:0); h=h*31+(noise>0?1:0); h=h*31+occluderLayers.value;
                return h*31+depthEpsilon.GetHashCode(); }
        }
    }

    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Rendering/Object Line Art Source")]
    public sealed class ObjectLineArtSource : MonoBehaviour
    {
        [Tooltip("Empty = all MeshRenderer/SkinnedMeshRenderer children. Otherwise only these renderers.")]
        public Renderer[] renderers = Array.Empty<Renderer>();
        internal static readonly HashSet<ObjectLineArtSource> Active = new HashSet<ObjectLineArtSource>();
        void OnEnable() { Active.Add(this); }
        void OnDisable() { Active.Remove(this); }
        internal Renderer[] GetRenderers() => renderers != null && renderers.Length > 0 ? renderers : GetComponentsInChildren<Renderer>();
    }
}
