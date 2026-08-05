using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>True while the Player-scope inventory holds at least N of an item — "can
    /// afford" as a bool (M10). Evaluates <see cref="InventoryCountCondition"/>; its
    /// parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Items", null, "Inventory Count")]
    public class InventoryCountConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(InventoryCountCondition);
    }
}
