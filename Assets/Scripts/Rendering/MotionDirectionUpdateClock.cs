using System;

// Time-based publication; collecting samples and rendering continue between ticks.
internal sealed class MotionDirectionUpdateClock
{
    bool initialized;
    bool wasSynchronized;
    int lastFrame = int.MinValue;
    int lastGeneration;
    double nextUpdate;

    public bool Tick(double now, int frame, bool synchronized, int generation, double hz, bool reset)
    {
        if (reset) initialized = false;
        if (initialized && lastFrame == frame) return false;
        lastFrame = frame;
        double interval = 1.0 / Math.Max(1.0, hz);

        if (!initialized || synchronized != wasSynchronized)
        {
            initialized = true;
            wasSynchronized = synchronized;
            lastGeneration = generation;
            nextUpdate = now + interval;
            return true;
        }

        if (synchronized)
        {
            if (generation == lastGeneration) return false;
            lastGeneration = generation;
            return true;
        }

        if (now + 1e-9 < nextUpdate) return false;
        // Skip missed deadlines after a hitch; never replay multiple GPU captures.
        nextUpdate += (Math.Floor(Math.Max(0.0, now - nextUpdate) / interval) + 1.0) * interval;
        return true;
    }
}
