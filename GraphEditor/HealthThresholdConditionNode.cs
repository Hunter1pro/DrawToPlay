using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Compares the owner's or the target's health fraction against a threshold — the seam for enrage and flee states.
    /// Bakes into one <see cref="HealthThresholdCondition"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Combat", null, "Health Threshold")]
    public class HealthThresholdConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(HealthThresholdCondition);
    }
}
