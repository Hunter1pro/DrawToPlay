using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Compares a numeric blackboard key against a value with a chosen operator.
    /// Evaluates <see cref="BlackboardCompareCondition"/> as a bool. A
    /// <see cref="GetBlackboardFloatNode"/> into a <see cref="CompareFloatNode"/> says the same thing
    /// in two nodes with a wireable right-hand side; this one is a single node and is what a tree
    /// converted from the state-tree flavour already uses. Its parameter ports mirror that type's
    /// fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Blackboard", null, "Blackboard Compare")]
    public class BlackboardCompareConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(BlackboardCompareCondition);
    }
}
