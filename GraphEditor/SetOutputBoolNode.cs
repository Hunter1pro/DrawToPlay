using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Returns a yes/no answer FROM this task — "did it hit?", "was the path blocked?" — the flag half
    /// of <see cref="SetOutputFloatNode"/>. Same rules: the name is the contract a transition binds to
    /// and is baked as a constant, the value is a data pin, and the last write before the task finishes
    /// is the one that gets captured.
    ///
    /// A BOOL OUTPUT RIDES IN THE FLOAT SLOT (1 or 0), which is the same thing
    /// <c>GraphTaskParameter</c> does for a bool parameter and for the same reason: the captured value
    /// record has one number and one string, so a third field would exist to hold a bit. The
    /// <c>kind</c> on the record is what says to read it back as a flag, and there is no
    /// <see cref="SetBlackboardFloatNode"/>-style bool sibling on the blackboard side to be
    /// inconsistent with — the blackboard has no bool node at all.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.SetOutputBool"/>: <c>stringValue</c> the output name,
    /// <c>data[0]</c> the value source (or <c>floatValue</c>, non-zero for true, when the pin is
    /// unwired), <c>exec[0]</c> next.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Outputs", null, "Set Bool")]
    public class SetOutputBoolNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the output's name. Baked as a constant.</summary>
        public const string OutputNamePortName = "output";

        /// <summary>Name of the bool data pin holding the value to return.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.SetOutputBool;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context);

            TaskGraphPorts.AddData<string>(context, OutputNamePortName, "Output",
                "Name this task returns the value under. Baked as a constant — wiring it has no "
                + "effect. Transitions route outputs by this name, so renaming it breaks them.");
            TaskGraphPorts.AddData<bool>(context, ValuePortName, "Value",
                "The flag to return. Wire it, or tick the box.");
        }
    }
}
