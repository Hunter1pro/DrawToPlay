using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Returns text FROM this task. The string half of <see cref="SetOutputFloatNode"/> — same rules:
    /// the name is the contract a transition binds to and is baked as a constant, the value is a data
    /// pin, and the last write before the task finishes is the one that gets captured.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.SetOutputString"/>: <c>stringValue</c> the output name,
    /// <c>data[0]</c> the value source (or <c>stringValue2</c> when the pin is unwired), <c>exec[0]</c>
    /// next. The value gets its own constant field because <c>stringValue</c> is already spoken for by
    /// the name — the same slot split <see cref="SetBlackboardStringNode"/> makes for its key.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Outputs", null, "Set String")]
    public class SetOutputStringNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the output's name. Baked as a constant.</summary>
        public const string OutputNamePortName = "output";

        /// <summary>Name of the string data pin holding the value to return.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.SetOutputString;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context);

            TaskGraphPorts.AddData<string>(context, OutputNamePortName, "Output",
                "Name this task returns the value under. Baked as a constant — wiring it has no "
                + "effect. Transitions route outputs by this name, so renaming it breaks them.");
            TaskGraphPorts.AddData<string>(context, ValuePortName, "Value",
                "The text to return. Wire it, or type a constant.");
        }
    }
}
