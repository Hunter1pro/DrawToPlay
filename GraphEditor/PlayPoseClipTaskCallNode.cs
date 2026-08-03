using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Plays a PoseAnimator clip by asset name, optionally holding the chain open until the clip ends.
    /// Calls <see cref="PlayPoseClipTask"/>. With <c>waitForEnd</c> on it is latent — the chain parks
    /// here for the length of the clip, which is how a windup lines up with its animation. Its
    /// parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Animation", null, "Play Pose Clip")]
    public class PlayPoseClipTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(PlayPoseClipTask);
    }
}
