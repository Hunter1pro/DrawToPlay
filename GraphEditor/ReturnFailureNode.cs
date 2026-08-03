using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Ends the tick with <see cref="StateTreeStatus.Failure"/> — the task could not do its job. A
    /// failing task still COUNTS AS FINISHED to the state that owns it (the runner waits for every
    /// task to stop, whatever the status), and inside another graph task it takes the caller's
    /// failure pin, which is how "chase, and if there is nothing to chase, do the other thing" is
    /// written without a negated condition.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.ReturnFailure"/>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Flow", null, "Return Failure")]
    public class ReturnFailureNode : Node, ITaskGraphNode
    {
        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.ReturnFailure;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
        }
    }
}
