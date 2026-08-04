using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while a context scope's blackboard holds (or, inverted, lacks) a key.
    /// Bakes into one <see cref="HasContextKeyCondition"/>; its parameter ports mirror that
    /// type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Context", null, "Has Context Key")]
    public class HasContextKeyConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(HasContextKeyCondition);
    }
}
