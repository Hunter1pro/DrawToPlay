using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Removes items from the Player-scope inventory; the Failure exec fires when
    /// there are not enough — "cannot afford" as a wire (M10). Calls
    /// <see cref="InventoryRemoveTask"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Items", null, "Inventory Remove")]
    public class InventoryRemoveTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(InventoryRemoveTask);
    }
}
