using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while any world object carries the tag (or, inverted, none does). Bakes
    /// into one <see cref="HasWorldTagCondition"/>; its parameter ports mirror that type's
    /// fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/World", null, "Has World Tag")]
    public class HasWorldTagConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(HasWorldTagCondition);
    }
}
