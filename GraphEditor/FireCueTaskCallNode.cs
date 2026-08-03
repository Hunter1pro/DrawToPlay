using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Emits a named cue with an owner-relative offset for whatever presentation layer is listening.
    /// Calls <see cref="FireCueTask"/> — the configurable form of <see cref="FireCueNode"/>, worth the
    /// extra node when the cue needs a position offset or a payload key/value pair. Its parameter
    /// ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Cues", null, "Fire Cue (Task)")]
    public class FireCueTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(FireCueTask);
    }
}
