using UnityEngine;

/// <summary>
/// Marks the transform used by CharacterOutline screen-UV expansion.
/// Independent from OuterGlowAnchor — place wherever the outline should expand from.
/// Works in Edit Mode Game view as well as Play Mode ([ExecuteAlways]).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class CharacterOutlineAnchor : MonoBehaviour
{
    static CharacterOutlineAnchor s_Current;

    [Tooltip("Optional override. If empty, this transform's position is used.")]
    public Transform anchorOverride;

    public static CharacterOutlineAnchor Current => s_Current;

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
