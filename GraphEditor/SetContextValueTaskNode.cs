using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Writes (or clears) a key on a context scope's blackboard — Root/Level/Player, the
    /// M8 spine. Bakes into one <see cref="SetContextValueTask"/>; its parameter ports mirror
    /// that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Context", null, "Set Context Value")]
    public class SetContextValueTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(SetContextValueTask);
    }
}
