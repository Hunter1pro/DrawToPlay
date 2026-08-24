using System;
using Unity.GraphToolkit.Editor;
using UnityEngine.Scripting.APIUpdating;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Compares the owner's or the target's health fraction against a threshold — the seam for enrage and flee behaviour.
    /// Evaluates <see cref="HealthThresholdCondition"/> as a bool. Its parameter ports mirror that
    /// type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Combat", null, "Health Threshold")]
    // Serialized graphs reference node types by ASSEMBLY name;     // core GraphEditor assembly, so without this shim every pre-split asset that used it
    // loses the node as a missing type on its next save.
    [MovedFrom(true, null, "PowerOfFire.DrawToPlay.Examples.GraphEditor", null)]
    public class HealthThresholdConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(HealthThresholdCondition);
    }
}
