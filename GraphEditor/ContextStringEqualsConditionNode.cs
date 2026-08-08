using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while a context scope's string key equals a value — the fan-out check of a
    /// generic travel state ("level:current == meadow" → the meadow state). Bakes into one
    /// <see cref="ContextStringEqualsCondition"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Context", null, "Context String Equals")]
    public class ContextStringEqualsConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(ContextStringEqualsCondition);
    }
}
