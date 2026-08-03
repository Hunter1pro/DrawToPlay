using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// A number, on the canvas. Every data pin can hold a typed-in value already, so this node earns
    /// its place when ONE number feeds SEVERAL pins: name it once, wire it three times, retune it in
    /// one spot. (A typed output has Multi capacity — PortModel.get_Capacity,
    /// UnityEditor.GraphToolkitModule IL 74310-74354 — so one constant really does feed many pins.)
    ///
    /// Graph Toolkit's own constant nodes and variable nodes work too and bake to the same thing; a
    /// graph VARIABLE is flattened to its default value, which the bake warns about.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.ConstFloat"/>: <c>floatValue</c>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "Float")]
    public class ConstFloatNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the input port holding the constant.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.ConstFloat;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<float>(context, ValuePortName, "Value", "The number this node is.");
            TaskGraphPorts.AddResult<float>(context, "The number.");
        }
    }
}
