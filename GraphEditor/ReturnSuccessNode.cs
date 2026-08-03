using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Ends the tick with <see cref="StateTreeStatus.Success"/> — the task is DONE. In a state tree
    /// that lets the state's completion transitions fire; inside another graph task it takes the
    /// caller's success pin.
    ///
    /// Reaching the end of a chain without a Return leaves the task Running, so this node is how a
    /// task ever finishes. It has no outgoing pin: nothing runs after a return.
    ///
    /// Only meaningful in the tick chain. The enter and exit chains have no status to return, so a
    /// Return there is reported by the bake.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.ReturnSuccess"/>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Flow", null, "Return Success")]
    public class ReturnSuccessNode : Node, ITaskGraphNode
    {
        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.ReturnSuccess;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
        }
    }
}
