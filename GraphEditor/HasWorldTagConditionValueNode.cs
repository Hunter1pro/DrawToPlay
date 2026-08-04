using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while any world object carries the tag (or, inverted, none does) — "a
    /// beacon is lit", "all enemies cleared", as a plain bool. Evaluates
    /// <see cref="HasWorldTagCondition"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/World", null, "Has World Tag")]
    public class HasWorldTagConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(HasWorldTagCondition);
    }
}
