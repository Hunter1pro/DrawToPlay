using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Writes a number into the shared blackboard — the memory every task, condition and tree in the
    /// runner sees, so this is how a graph task talks to the rest of the behaviour.
    ///
    /// THE KEY IS A CONSTANT, THE VALUE IS A PIN. The key is baked into the instruction (there is no
    /// dynamic-key form in the program model, on purpose: a blackboard whose key names are computed
    /// cannot be read by anyone), while the value is a full data pin — wire a comparison, a get, a
    /// condition into it, or just type a number.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.SetBlackboardFloat"/>: <c>stringValue</c> the key,
    /// <c>data[0]</c> the value source (or <c>floatValue</c> when the pin is unwired),
    /// <c>exec[0]</c> next.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Blackboard", null, "Set Float")]
    public class SetBlackboardFloatNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the blackboard key. Baked as a constant.</summary>
        public const string KeyPortName = "key";

        /// <summary>Name of the float data pin holding the value to write.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.SetBlackboardFloat;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context);

            TaskGraphPorts.AddData<string>(context, KeyPortName, "Key",
                "Blackboard key to write. Baked as a constant — wiring it has no effect.");
            TaskGraphPorts.AddData<float>(context, ValuePortName, "Value",
                "The number to store. Wire it, or type a constant.");
        }
    }
}
