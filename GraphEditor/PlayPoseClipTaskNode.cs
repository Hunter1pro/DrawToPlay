using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Plays a PoseAnimator clip by asset name, optionally holding the state open until the clip ends.
    /// Bakes into one <see cref="PlayPoseClipTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Presentation", null, "Play Pose Clip")]
    public class PlayPoseClipTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(PlayPoseClipTask);
    }
}
