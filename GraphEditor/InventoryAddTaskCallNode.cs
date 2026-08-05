using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Adds items to the Player-scope inventory (M10). Calls
    /// <see cref="InventoryAddTask"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Items", null, "Inventory Add")]
    public class InventoryAddTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(InventoryAddTask);
    }
}
