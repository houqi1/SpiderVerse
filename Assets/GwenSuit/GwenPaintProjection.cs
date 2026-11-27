using UnityEngine;

namespace SpiderVerse
{
    // All costume panels share the character pivot. The camera supplies only the two projection axes.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GwenPaintProjection : MonoBehaviour
    {
        [SerializeField, Min(0.0001f)] private float referenceHeight = 1.8f;
        private static readonly int Anchor = Shader.PropertyToID("_GwenProjectionAnchor");
        private Renderer[] panels;
        private MaterialPropertyBlock block;
        private Vector4 previous;

        private void OnEnable()
        {
            panels = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            block = new MaterialPropertyBlock();
            previous = Vector4.positiveInfinity;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (panels == null) return;
            var p = transform.position;
            float height = transform.TransformVector(Vector3.up * referenceHeight).magnitude;
            var anchor = new Vector4(p.x, p.y, p.z, 1f / Mathf.Max(height, 0.0001f));
            if (anchor == previous) return;
            previous = anchor;
            foreach (var panel in panels)
            {
                if (!panel) continue;
                panel.GetPropertyBlock(block);
                block.SetVector(Anchor, anchor);
                panel.SetPropertyBlock(block);
            }
        }
    }
}
