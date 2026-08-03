using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Compares the owner's or the target's health fraction against a threshold — the seam for enrage and flee behaviour.
    /// Evaluates <see cref="HealthThresholdCondition"/> as a bool. Its parameter ports mirror that
    /// type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Combat", null, "Health Threshold")]
    public class HealthThresholdConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(HealthThresholdCondition);
    }
}
