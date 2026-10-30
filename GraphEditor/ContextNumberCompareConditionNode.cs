using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>A number on a context scope compared against a constant — how a routed step reads an
    /// answer lifted to the root. Bakes into one
    /// <see cref="ContextNumberCompareCondition"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Context", null, "Context Number Compare")]
    public class ContextNumberCompareConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(ContextNumberCompareCondition);
    }
}
