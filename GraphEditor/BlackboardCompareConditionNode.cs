using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Compares a numeric blackboard key against a value with a chosen operator.
    /// Bakes into one <see cref="BlackboardCompareCondition"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Blackboard", null, "Blackboard Compare")]
    public class BlackboardCompareConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(BlackboardCompareCondition);
    }
}
