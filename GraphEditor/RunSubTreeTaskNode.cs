using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Runs another state tree as this task on the shared blackboard — the
    /// composition block, so graph-authored tasks can nest graph-authored tasks.
    /// Bakes into one <see cref="RunSubTreeTask"/>; the subTree reference rides its
    /// parameter port. The successStates/failureStates lists are NOT portable through
    /// the parameter layer (List&lt;string&gt;, see LibraryParameterPorts.IsSupportedParameterType)
    /// and keep their runtime defaults (success/exit, fail/failure) — matching the
    /// scaffold convention; custom exit-state names are edited on the baked asset in the
    /// State Tree Editor.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Composite", null, "Run Sub Tree")]
    public class RunSubTreeTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(RunSubTreeTask);
    }
}
