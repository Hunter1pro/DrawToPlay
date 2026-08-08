using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Travel to an expedition level, the service remembering the way back — the
    /// service verb, callable from a program. Runs one <see cref="EnterExpeditionTask"/>;
    /// its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Levels", null, "Enter Expedition")]
    public class EnterExpeditionTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(EnterExpeditionTask);
    }
}
