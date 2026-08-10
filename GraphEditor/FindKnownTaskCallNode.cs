using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Put THE object carrying a tag on the blackboard — the registry's answer for
    /// something a level has exactly one of, instead of sweeping the level for it.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/World", null, "Find Known (by tag)")]
    public class FindKnownTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(FindKnownTask);
    }
}
