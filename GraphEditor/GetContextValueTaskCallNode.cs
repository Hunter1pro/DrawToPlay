using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Copies a context-scope value (Root/Level/Player) into this tree's own blackboard,
    /// where Get Blackboard nodes and field bindings read it — the v1 read path of the M8 spine
    /// (a direct context value pin is the planned v2). Calls <see cref="GetContextValueTask"/>;
    /// its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Context", null, "Get Context Value")]
    public class GetContextValueTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(GetContextValueTask);
    }
}
