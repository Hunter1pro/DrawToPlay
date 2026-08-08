using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Travel back to wherever the expedition was entered from — the service verb,
    /// callable from a program. Runs one <see cref="ReturnFromExpeditionTask"/>; the Failure
    /// pin is "not on an expedition".</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Levels", null, "Return From Expedition")]
    public class ReturnFromExpeditionTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(ReturnFromExpeditionTask);
    }
}
