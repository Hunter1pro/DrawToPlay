using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Walks the owner toward the blackboard target until it is inside stopRange; Failure the moment there is no target left to chase, which is how a chase state stands down without a negated condition.
    /// Bakes into one <see cref="ChaseTargetTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Movement", null, "Chase Target")]
    public class ChaseTargetTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(ChaseTargetTask);
    }
}
