using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Acquires the nearest hostile HealthComponent within detectRange into blackboard["target"], with hysteresis on loseRange — the interrupt that wakes an idle enemy.
    /// Bakes into one <see cref="TargetDetectedCondition"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Perception", null, "Target Detected")]
    public class TargetDetectedConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(TargetDetectedCondition);
    }
}
