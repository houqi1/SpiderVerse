using UnityEngine;

/// <summary>
/// Marks the character root used by OuterGlow stabilized screen UVs.
/// Place on Gwen's root (or any transform that should be the strength-map origin).
/// Works in Edit Mode Game view as well as Play Mode ([ExecuteAlways]).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class OuterGlowAnchor : MonoBehaviour
{
    static OuterGlowAnchor s_Current;

    [Tooltip("Optional override. If empty, this transform's position is used.")]
    public Transform anchorOverride;

    public static OuterGlowAnchor Current => s_Current;

    public Vector3 AnchorPosition
    {
        get
        {
            Transform t = anchorOverride != null ? anchorOverride : transform;
            return t != null ? t.position : Vector3.zero;
        }
    }

    void OnEnable()
    {
        s_Current = this;
    }

    void OnDisable()
    {
        if (s_Current == this)
            s_Current = null;
    }
}
