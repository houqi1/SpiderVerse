using UnityEngine;
using SpiderVerse.LineArt;

/// <summary>Continuously rotates this object around a configurable axis.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
[AddComponentMenu("SpiderVerse/Rotate Around Axis")]
public sealed class RotateAroundAxis : MonoBehaviour
{
    [Tooltip("Rotation axis. For Local space this is relative to the object; for World space it is relative to the scene.")]
    public Vector3 axis = Vector3.up;

    [Tooltip("Rotation speed in degrees per second. Use a negative value to rotate in the opposite direction.")]
    public float degreesPerSecond = 90f;

    [Tooltip("Whether the rotation axis is local to this object or fixed in world space.")]
    public Space axisSpace = Space.Self;

    [Tooltip("When enabled, rotation updates on the same camera sample as Line Art. Assign the camera used by Line Art, or leave empty to use Main Camera.")]
    public bool followLineArtUpdateRate = true;

    [Tooltip("Camera whose Line Art sample drives rotation. Empty uses Main Camera.")]
    public Camera referenceCamera;

    [Range(1, 60)]
    [Tooltip("Custom update rate in Hz. Used when Follow Line Art Update Rate is disabled, or no active Line Art feature is found.")]
    public int updateRate = 12;

    float pendingTime;
    int lastLineArtGeneration = -1;
    bool wasFollowingLineArt;

    void OnEnable()
    {
        pendingTime = 0f;
        lastLineArtGeneration = -1;
        wasFollowingLineArt = false;
    }

    void LateUpdate()
    {
        pendingTime += Time.deltaTime;

        if (followLineArtUpdateRate)
        {
            Camera camera = referenceCamera ? referenceCamera : Camera.main;
            if (camera && ObjectLineArtFeature.TryGetCameraSample(camera, out var sample))
            {
                if (!wasFollowingLineArt)
                {
                    // Start from the current sample; don't apply time accumulated before synchronization.
                    pendingTime = 0f;
                    wasFollowingLineArt = true;
                    lastLineArtGeneration = sample.generation;
                    return;
                }

                if (sample.generation != lastLineArtGeneration)
                {
                    ApplyRotation(pendingTime);
                    pendingTime = 0f;
                    lastLineArtGeneration = sample.generation;
                }
                return;
            }
        }

        wasFollowingLineArt = false;
        lastLineArtGeneration = -1;

        float interval = 1f / Mathf.Max(1, updateRate);
        if (pendingTime < interval)
            return;

        ApplyRotation(pendingTime);
        pendingTime %= interval;
    }

    void ApplyRotation(float elapsed)
    {
        if (elapsed <= 0f || axis.sqrMagnitude < 0.000001f)
            return;

        transform.Rotate(axis.normalized, degreesPerSecond * elapsed, axisSpace);
    }
}
