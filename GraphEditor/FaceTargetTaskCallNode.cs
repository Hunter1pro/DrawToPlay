using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Flips the owner toward the blackboard target and succeeds immediately.
    /// Calls <see cref="FaceTargetTask"/>; it never returns Running, so the chain carries straight on
    /// out of the Success pin in the same tick. Its parameter ports mirror that type's fields
    /// 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Movement", null, "Face Target")]
    public class FaceTargetTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(FaceTargetTask);
    }
}
