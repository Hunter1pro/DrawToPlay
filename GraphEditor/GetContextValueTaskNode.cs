using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Copies a context-scope value (Root/Level/Player) into this tree's own blackboard.
    /// Bakes into one <see cref="GetContextValueTask"/>; its parameter ports mirror that type's
    /// fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Context", null, "Get Context Value")]
    public class GetContextValueTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(GetContextValueTask);
    }
}
