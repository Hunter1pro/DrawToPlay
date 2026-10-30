using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>A number on a context scope compared against a constant, as a plain bool.
    /// Evaluates
    /// <see cref="ContextNumberCompareCondition"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Context", null, "Context Number Compare")]
    public class ContextNumberCompareConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(ContextNumberCompareCondition);
    }
}
