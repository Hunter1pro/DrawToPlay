using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// True when both inputs are true. An unwired input is false, so a half-wired And is false —
    /// which is the safe way round: a test nobody finished does not let a chain through.
    ///
    /// Chain them for three or more terms; the program model keeps every data node at two inputs so
    /// that the flat instruction list stays fixed-width.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.BoolAnd"/>: <c>data[0]</c>, <c>data[1]</c>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "And")]
    public class BoolAndNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the first bool data pin (<c>data[0]</c>).</summary>
        public const string LeftPortName = "left";

        /// <summary>Name of the second bool data pin (<c>data[1]</c>).</summary>
        public const string RightPortName = "right";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.BoolAnd;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<bool>(context, LeftPortName, "A", "First term. Unwired is false.");
            TaskGraphPorts.AddData<bool>(context, RightPortName, "B", "Second term. Unwired is false.");
            TaskGraphPorts.AddResult<bool>(context, "True when both terms are true.");
        }
    }
}
