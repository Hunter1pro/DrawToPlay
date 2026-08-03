using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Writes text into the shared blackboard. The string half of
    /// <see cref="SetBlackboardFloatNode"/> — same rules: the key is baked as a constant, the value
    /// is a data pin.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.SetBlackboardString"/>: <c>stringValue</c> the key,
    /// <c>data[0]</c> the value source (or <c>stringValue2</c> when the pin is unwired),
    /// <c>exec[0]</c> next. The value gets its own constant field because <c>stringValue</c> is
    /// already spoken for by the key.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Blackboard", null, "Set String")]
    public class SetBlackboardStringNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the blackboard key. Baked as a constant.</summary>
        public const string KeyPortName = "key";

        /// <summary>Name of the string data pin holding the value to write.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.SetBlackboardString;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context);

            TaskGraphPorts.AddData<string>(context, KeyPortName, "Key",
                "Blackboard key to write. Baked as a constant — wiring it has no effect.");
            TaskGraphPorts.AddData<string>(context, ValuePortName, "Value",
                "The text to store. Wire it, or type a constant.");
        }
    }
}
