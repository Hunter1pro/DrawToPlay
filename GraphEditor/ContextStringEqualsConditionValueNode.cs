using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while a context scope's string key equals a value — "which level am I in",
    /// "which screen is up", as a plain bool. Evaluates
    /// <see cref="ContextStringEqualsCondition"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Context", null, "Context String Equals")]
    public class ContextStringEqualsConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(ContextStringEqualsCondition);
    }
}
