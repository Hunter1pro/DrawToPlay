using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Acquires the nearest hostile HealthComponent within detectRange into blackboard["target"], with hysteresis on loseRange.
    /// Evaluates <see cref="TargetDetectedCondition"/> as a bool.
    ///
    /// IT WRITES AS WELL AS READS — reading it is what puts the target on the blackboard for every
    /// node after it. Pull it once near the top of a chain rather than in three places: the scan is
    /// interval-limited (<c>rescanInterval</c>) but the bookkeeping is not free, and every read can
    /// change what "target" means for the nodes downstream of it.
    ///
    /// Its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Perception", null, "Target Detected")]
    public class TargetDetectedConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(TargetDetectedCondition);
    }
}
