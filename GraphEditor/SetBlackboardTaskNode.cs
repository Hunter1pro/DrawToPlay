using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Writes (or clears) a blackboard key — how a tree seeds moveSpeed, attackRange and friends.
    /// Bakes into one <see cref="SetBlackboardTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Blackboard", null, "Set Blackboard")]
    public class SetBlackboardTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(SetBlackboardTask);
    }
}
