using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True once a blackboard timer key is older than seconds; arms itself on first evaluation.
    /// Bakes into one <see cref="TimerElapsedCondition"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Timing", null, "Timer Elapsed")]
    public class TimerElapsedConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(TimerElapsedCondition);
    }
}
