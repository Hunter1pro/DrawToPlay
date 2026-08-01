using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True once the absolute time held in a blackboard cooldown key has passed.
    /// Bakes into one <see cref="CooldownReadyCondition"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Timing", null, "Cooldown Ready")]
    public class CooldownReadyConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(CooldownReadyCondition);
    }
}
