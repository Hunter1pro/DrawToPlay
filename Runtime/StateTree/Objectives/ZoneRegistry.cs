using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The zones as a catalog — list the objective registry in dependsOn so each
    /// zone's stack picks its rows, and list THIS registry in the objective registry's
    /// dependsOn so the service (and the manifest's zone placements) find the zones.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Zone Registry", fileName = "ZoneRegistry")]
    public sealed class ZoneRegistry : StateTreeRegistry<ZoneDef>
    {
    }
}
