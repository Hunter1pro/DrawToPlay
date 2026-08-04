using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while a context scope's blackboard holds (or, inverted, lacks) a key — the
    /// cross-tree coordination check of the M8 spine: "alarm raised on the Level" as a plain
    /// bool. Evaluates <see cref="HasContextKeyCondition"/>; its parameter ports mirror that
    /// type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Context", null, "Has Context Key")]
    public class HasContextKeyConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(HasContextKeyCondition);
    }
}
