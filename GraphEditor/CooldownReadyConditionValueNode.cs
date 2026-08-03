using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True once the absolute time held in a blackboard cooldown key has passed.
    /// Evaluates <see cref="CooldownReadyCondition"/> as a bool — the gate in front of an attack
    /// chain. With <c>armOnReady</c> on it re-arms itself the moment it answers true, so the chain
    /// does not need a separate "start the cooldown" step. Its parameter ports mirror that type's
    /// fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Timing", null, "Cooldown Ready")]
    public class CooldownReadyConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(CooldownReadyCondition);
    }
}
