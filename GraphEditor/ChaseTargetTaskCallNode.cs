using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Walks the owner toward the blackboard target until it is inside stopRange.
    /// Calls <see cref="ChaseTargetTask"/> latently: it stays Running while the owner is closing, and
    /// takes the Failure pin the moment there is no target left to chase — which is how a chase ends
    /// without a negated condition. Its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Movement", null, "Chase Target")]
    public class ChaseTargetTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(ChaseTargetTask);
    }
}
