using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>The generic subsystem caller as a GRAPH NODE (ui-wiring brief §4g): write
    /// a declared request onto the root board from wherever this graph runs — what lets a
    /// dialog end with "and the bag opens on the gift".
    /// Evaluates <see cref="RequestTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Services", null, "Request Subsystem")]
    public class RequestTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(RequestTask);
    }
}
