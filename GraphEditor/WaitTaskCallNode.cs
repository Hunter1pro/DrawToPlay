using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Holds the chain open for a number of seconds with optional jitter — the windup, the recovery, the idle pulse.
    /// Calls <see cref="WaitTask"/>. <see cref="WaitNode"/> is the cheaper way to pace a chain (no
    /// sub-asset, and its duration is a wireable pin); this one exists for the jitter, which is what
    /// stops a room full of the same enemy pulsing in lockstep. Its parameter ports mirror that
    /// type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Timing", null, "Wait (Task)")]
    public class WaitTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(WaitTask);
    }
}
