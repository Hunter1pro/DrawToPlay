using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while the blackboard target sits inside range (and outside minRange) — the reach test that turns a chase into a swing.
    /// Bakes into one <see cref="TargetInRangeCondition"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Perception", null, "Target In Range")]
    public class TargetInRangeConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(TargetInRangeCondition);
    }
}
