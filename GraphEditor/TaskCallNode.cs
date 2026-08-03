using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Base for every "call a library task" instruction — the node that makes a task graph a
    /// COMPOSITE rather than a language. Chase, Attack, Play Pose Clip and the rest are already
    /// written, tested and tuned in <c>Runtime/StateTree/Library</c>; a graph task calls them and
    /// spends its own nodes on the logic BETWEEN them.
    ///
    /// IT IS LATENT, and that is the whole point. The task is entered the first time the chain
    /// reaches this node; while it answers Running the graph task returns Running and the next tick
    /// RESUMES HERE — the wrapped task keeps its instance state across ticks exactly as it would
    /// inside a state. When it finishes, the chain leaves by
    /// <see cref="TaskGraphPorts.SuccessExecPortName"/> or
    /// <see cref="TaskGraphPorts.FailureExecPortName"/>, so "chase; if that fails, idle" is two wires.
    /// If the graph task is itself cancelled while this one is running, the wrapped task gets
    /// <see cref="StateTreeStatus.Cancelled"/> — teardown propagates all the way down.
    ///
    /// Latent means TICK CHAIN ONLY: the enter and exit chains complete inside one call, and a call
    /// node on either is reported by the bake and stepped past at runtime.
    ///
    /// PARAMETERS ARE 1:1 WITH THE TASK'S FIELDS, BY CONSTRUCTION — the subclasses hand-list nothing.
    /// <see cref="LibraryParameterPorts.DefineParameterPorts"/> reflects over the runtime type's
    /// public fields and names each port exactly after the field, and
    /// <see cref="TaskGraphBaker"/> reads them back through the same helper, so a new library task
    /// costs a six-line wrapper and no baker change. It is the same mechanism the M7 state-tree
    /// blocks use (<see cref="StateTaskBlockNode"/>), which is why a task tuned in one graph flavour
    /// looks identical in the other.
    ///
    /// EACH CALL NODE BAKES ITS OWN CONFIGURED COPY of the task as a sub-asset of the imported file,
    /// so two Chase nodes with different speeds are two assets and neither shares instance state with
    /// the other.
    ///
    /// Abstract on purpose: an abstract node type is skipped by the palette, so only the concrete
    /// wrappers are offered.
    /// </summary>
    [Serializable]
    public abstract class TaskCallNode : Node, ITaskGraphNode
    {
        /// <summary>
        /// The <see cref="StateTreeTaskAsset"/> subclass this node calls. The bake creates one
        /// configured instance of it per node and copies every parameter port into the field of the
        /// same name.
        /// </summary>
        public abstract Type taskType { get; }

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.DoTask;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.SuccessExecPortName, "Success",
                "Runs once the task finishes successfully.");
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.FailureExecPortName, "Failure",
                "Runs once the task fails. Leave it empty to fall through to the end of the chain.");

            LibraryParameterPorts.DefineParameterPorts(context, taskType);
        }
    }
}
