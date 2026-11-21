using UnityEngine;
namespace LassylaPort
{
    // Settings for the reproducible camera comparison.
    public sealed class LassylaCaptureCamera : MonoBehaviour
    {
        public bool rawSample;
        public bool measureLinearCoverage;
        public bool timelinePlayback;
        public int samples = 64;
        public float filterRadius = 1.5f;
    }
}
