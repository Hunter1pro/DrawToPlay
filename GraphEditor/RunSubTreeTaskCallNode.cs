using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Runs a whole state tree as one instruction, on the shared blackboard — a logic graph
    /// calling a state machine, which together with a state calling a logic graph is what makes the
    /// two authoring flavours compose in both directions.
    ///
    /// Calls <see cref="RunSubTreeTask"/> latently: Running while the sub-tree runs, Success when it
    /// enters one of its success states, Failure on a failure state. Cancelling this graph task
    /// cancels the sub-tree and everything inside it.
    ///
    /// The successStates/failureStates lists keep their runtime defaults (success/exit,
    /// fail/failure): a <c>List&lt;string&gt;</c> is not a portable parameter type
    /// (<see cref="LibraryParameterPorts.IsSupportedParameterType"/>), so custom exit-state names are
    /// edited on the baked sub-asset in the State Tree Editor. That matches the scaffold convention,
    /// so the defaults are right for anything the "+ New Sub-Tree Task…" flow produced.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Composite", null, "Run Sub Tree")]
    public class RunSubTreeTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(RunSubTreeTask);
    }
}
