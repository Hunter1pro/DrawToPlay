using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Writes (or clears) a key on a context scope's blackboard — Root/Level/Player, the
    /// M8 spine. How a graph PUBLISHES state for every tree under the same scope ("alarm raised"
    /// on the Level). Calls <see cref="SetContextValueTask"/>; its parameter ports mirror that
    /// type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Context", null, "Set Context Value")]
    public class SetContextValueTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(SetContextValueTask);
    }
}
