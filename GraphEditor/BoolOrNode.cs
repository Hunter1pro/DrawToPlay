using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// True when either input is true. An unwired input is false, so a half-wired Or passes through
    /// whatever IS wired — handy while building: wire one term, get the behaviour, add the second.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.BoolOr"/>: <c>data[0]</c>, <c>data[1]</c>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "Or")]
    public class BoolOrNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the first bool data pin (<c>data[0]</c>).</summary>
        public const string LeftPortName = "left";

        /// <summary>Name of the second bool data pin (<c>data[1]</c>).</summary>
        public const string RightPortName = "right";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.BoolOr;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<bool>(context, LeftPortName, "A", "First term. Unwired is false.");
            TaskGraphPorts.AddData<bool>(context, RightPortName, "B", "Second term. Unwired is false.");
            TaskGraphPorts.AddResult<bool>(context, "True when either term is true.");
        }
    }
}
