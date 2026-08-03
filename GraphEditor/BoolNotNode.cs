using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Inverts a bool. The library conditions are all written in the positive
    /// ("target in range", "line of sight"), so this is how a chain asks the opposite without a
    /// second condition type — and it is why the M6 library never needed negated variants.
    ///
    /// An unwired input is false, so a Not with nothing in it is TRUE. Worth knowing before wiring
    /// one into a Branch and walking away.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.BoolNot"/>: <c>data[0]</c>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "Not")]
    public class BoolNotNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the bool data pin being inverted (<c>data[0]</c>).</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.BoolNot;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<bool>(context, ValuePortName, "Value",
                "The bool to invert. Unwired is false, so an empty Not is true.");
            TaskGraphPorts.AddResult<bool>(context, "The opposite of the input.");
        }
    }
}
