using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Raise a key-watched screen and hold the state until it answers — the awaited
    /// screen as a block. Bakes into one <see cref="AwaitScreenTask"/>; its parameter ports
    /// mirror that type's fields 1:1, and the button id comes back as the task's output.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Tasks/UI", null, "Await Screen")]
    public class AwaitScreenTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(AwaitScreenTask);
    }
}
