using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Emits a named cue with an owner-relative offset for whatever presentation layer is listening.
    /// Bakes into one <see cref="FireCueTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Presentation", null, "Fire Cue")]
    public class FireCueTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(FireCueTask);
    }
}
