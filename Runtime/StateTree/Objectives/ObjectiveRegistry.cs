using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The quest line's catalog — objectives as rows, chained by wires, their
    /// subjects picked from the registries this one lists in dependsOn (dialogs, items).</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Objective Registry",
        fileName = "ObjectiveRegistry")]
    public sealed class ObjectiveRegistry : StateTreeRegistry<ObjectiveDef>
    {
    }
}
