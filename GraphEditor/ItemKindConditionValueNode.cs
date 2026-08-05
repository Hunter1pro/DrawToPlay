using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True when the item under a blackboard key is of one kind — the item-click
    /// Branch of the notebook's inventory flow, as a bool for graphs (M10). Evaluates
    /// <see cref="ItemKindCondition"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Items", null, "Item Kind")]
    public class ItemKindConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(ItemKindCondition);
    }
}
