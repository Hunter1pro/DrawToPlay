using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Returns a number FROM this task. Graph parameters are the arguments a state passes in; the Set
    /// Output nodes are the values it gets back, and the transition that fires when the task finishes
    /// decides where each one is routed.
    ///
    /// THIS IS NOT A BLACKBOARD WRITE, and the difference is worth being precise about because the two
    /// nodes look identical. <see cref="SetBlackboardFloatNode"/> writes into shared memory the whole
    /// runner reads at any moment — a side effect, visible immediately, owned by nobody. An output is a
    /// RETURN VALUE: it is buffered on the task instance, captured the moment the task finishes, and
    /// then written wherever the transition's route says — which may be a blackboard key with a
    /// different name, or nowhere at all if no route asks for it. The caller decides what to do with a
    /// return value; that is the whole point of having them.
    ///
    /// THE NAME IS A CONSTANT, THE VALUE IS A PIN — the same split, and the same reason, as the
    /// blackboard nodes. The name is the CONTRACT the transition binds to (name-keyed on purpose:
    /// renaming an output is a breaking change to every route that reads it, exactly like renaming a
    /// method's return), so a computed name would be a contract nothing could be written against. The
    /// value is a full data pin — wire a comparison, a get, a parameter into it, or just type a number.
    ///
    /// SETTING THE SAME OUTPUT FROM TWO BRANCHES IS NORMAL. Only the last write before the task
    /// finishes is what gets captured, so an if/else that sets <c>result</c> on both sides declares one
    /// output, not two.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.SetOutputFloat"/>: <c>stringValue</c> the output name,
    /// <c>data[0]</c> the value source (or <c>floatValue</c> when the pin is unwired), <c>exec[0]</c>
    /// next.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Outputs", null, "Set Float")]
    public class SetOutputFloatNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the output's name. Baked as a constant.</summary>
        public const string OutputNamePortName = "output";

        /// <summary>Name of the float data pin holding the value to return.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.SetOutputFloat;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context);

            TaskGraphPorts.AddData<string>(context, OutputNamePortName, "Output",
                "Name this task returns the value under. Baked as a constant — wiring it has no "
                + "effect. Transitions route outputs by this name, so renaming it breaks them.");
            TaskGraphPorts.AddData<float>(context, ValuePortName, "Value",
                "The number to return. Wire it, or type a constant.");
        }
    }
}
