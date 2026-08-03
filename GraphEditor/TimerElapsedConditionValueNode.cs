using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True once a blackboard timer key is older than seconds; arms itself on first evaluation.
    /// Evaluates <see cref="TimerElapsedCondition"/> as a bool.
    ///
    /// It is the SHARED-CLOCK form of <see cref="WaitNode"/>: the deadline lives on the blackboard
    /// under a key, so another task, another state or the parent tree can read the same timer or
    /// reset it. Use <see cref="WaitNode"/> when the delay belongs to this chain alone.
    ///
    /// Its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Timing", null, "Timer Elapsed")]
    public class TimerElapsedConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(TimerElapsedCondition);
    }
}
