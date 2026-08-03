using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Writes (or clears) a blackboard key — how a tree seeds moveSpeed, attackRange and friends.
    /// Calls <see cref="SetBlackboardTask"/>. The primitives
    /// (<see cref="SetBlackboardFloatNode"/>, <see cref="SetBlackboardStringNode"/>) are the ones to
    /// reach for inside a chain because their value is a wireable pin; this task is here for its
    /// extras — <c>onlyIfMissing</c>, and clearing a key. Its parameter ports mirror that type's
    /// fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Blackboard", null, "Set Blackboard")]
    public class SetBlackboardTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(SetBlackboardTask);
    }
}
