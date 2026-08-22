using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>The writer — M37.3. Until then, it says what it would do.</summary>
    internal static class SubsystemGenerator
    {
        internal static void Generate(SubsystemSketch sketch)
        {
            Debug.Log("[Subsystems] Generate is M37.3 — the sketch '" + sketch.serviceName
                + "' validates; nothing was written.");
        }
    }
}
