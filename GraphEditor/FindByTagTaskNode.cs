using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Finds a world object by tag onto the blackboard ("target" by default). Bakes
    /// into one <see cref="FindByTagTask"/>; its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/World", null, "Find By Tag")]
    public class FindByTagTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(FindByTagTask);
    }
}
