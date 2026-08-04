using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Finds a world object by tag (nearest by default) and puts its GameObject on the
    /// blackboard — "target" by default, so the combat call nodes consume it directly. Fails
    /// when nothing carries the tag, which is the branchable "none in range" answer. Calls
    /// <see cref="FindByTagTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/World", null, "Find By Tag")]
    public class FindByTagTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(FindByTagTask);
    }
}
