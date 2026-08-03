using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// True or false, on the canvas. Most useful as a temporary: wire it into a
    /// <see cref="BranchNode"/> to pin a chain to one side while you build the other, then replace it
    /// with the real test.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.ConstBool"/>: <c>floatValue</c> non-zero for true, which
    /// is why the program model needs no separate bool field.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "Bool")]
    public class ConstBoolNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the input port holding the constant.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.ConstBool;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<bool>(context, ValuePortName, "Value", "What this node is.");
            TaskGraphPorts.AddResult<bool>(context, "True or false.");
        }
    }
}
