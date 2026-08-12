using System.Collections.Generic;
using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The pins every <see cref="TaskGraph"/> node is built from: the untyped EXEC wires that say
    /// what happens next, and the typed RESULT pin that every data node produces its value on. One
    /// place owns their names and their look, because the baker reads them back by those exact names
    /// and the node classes cannot share a base class (they derive from <see cref="Node"/>,
    /// <see cref="TaskCallNode"/> and <see cref="ConditionValueNode"/> alike).
    ///
    /// EXEC PINS ARE UNTYPED ON PURPOSE, and it is the same reason M7's state flow ports are:
    /// <c>PortModel.get_Capacity</c> returns Single only for a port that is both typed AND an input
    /// (UnityEditor.GraphToolkitModule IL 74310-74354). An instruction has to accept many
    /// predecessors — every re-join after a Branch is exactly that — so its exec IN pin must be
    /// untyped, and a port only connects to a port of a compatible type, so the exec OUT pins have to
    /// be untyped too. The cost is that an exec OUT pin also accepts many wires while the program
    /// model has one slot per pin; <see cref="TaskGraphBaker"/> reports the extra wires as errors.
    ///
    /// DATA PINS ARE TYPED for the mirror-image reason: Single capacity is precisely "this value has
    /// one source", and the type check is what stops a string reaching a float pin.
    /// </summary>
    public static class TaskGraphPorts
    {
        /// <summary>Name of the "run me" pin. Every instruction node has exactly one.</summary>
        public const string ExecInPortName = "exec";

        /// <summary>Name of the single "run this next" pin on a linear instruction.</summary>
        public const string ExecOutPortName = "then";

        /// <summary>Name of <see cref="BranchNode"/>'s taken-when-true pin (<c>exec[0]</c>).</summary>
        public const string TrueExecPortName = "onTrue";

        /// <summary>Name of <see cref="BranchNode"/>'s taken-when-false pin (<c>exec[1]</c>).</summary>
        public const string FalseExecPortName = "onFalse";

        /// <summary>Name of <see cref="TaskCallNode"/>'s pin taken when the task succeeds
        /// (<c>exec[0]</c>).</summary>
        public const string SuccessExecPortName = "success";

        /// <summary>Name of <see cref="TaskCallNode"/>'s pin taken when the task fails
        /// (<c>exec[1]</c>).</summary>
        public const string FailureExecPortName = "failure";

        /// <summary>Name of the output pin every data node produces its value on. Deliberately NOT
        /// "value": <see cref="BlackboardCompareCondition"/> has a field called <c>value</c>, and
        /// <see cref="ConditionValueNode"/> generates a parameter port per field.</summary>
        public const string ResultPortName = "result";

        /// <summary>Adds the "run me" pin. Accepts many wires, so several instructions can jump
        /// here.</summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        public static void AddExecIn(Node.IPortDefinitionContext context)
        {
            context.AddInputPort(ExecInPortName)
                .WithDisplayName(string.Empty)
                .WithTooltip("Runs when the instruction wired into this pin reaches it.")
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }

        /// <summary>Adds the single "run this next" pin of a linear instruction.</summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        public static void AddExecOut(Node.IPortDefinitionContext context)
        {
            AddExecOut(context, ExecOutPortName, string.Empty,
                "Runs after this instruction. Leave it empty to end the chain — the task then stays "
                + "Running until something returns.");
        }

        /// <summary>Adds a named "run this next" pin, for the instructions that have more than
        /// one.</summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        /// <param name="portName">Port name; the baker reads the pin back by it.</param>
        /// <param name="displayName">Label on the canvas.</param>
        /// <param name="tooltip">What taking this pin means.</param>
        public static void AddExecOut(Node.IPortDefinitionContext context, string portName,
            string displayName, string tooltip)
        {
            if (context == null || string.IsNullOrEmpty(portName))
                return;

            context.AddOutputPort(portName)
                .WithDisplayName(displayName ?? string.Empty)
                .WithTooltip(tooltip ?? string.Empty)
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }

        /// <summary>Adds the typed <see cref="ResultPortName"/> output every data node carries.</summary>
        /// <typeparam name="T">The value type this node produces (float, string or bool).</typeparam>
        /// <param name="context">The port definition context of the node being defined.</param>
        /// <param name="tooltip">What the value means.</param>
        public static void AddResult<T>(Node.IPortDefinitionContext context, string tooltip)
        {
            context.AddOutputPort<T>(ResultPortName)
                .WithDisplayName(string.Empty)
                .WithTooltip(tooltip ?? string.Empty)
                .Build();
        }

        /// <summary>Adds a typed value INPUT — a data pin the baker resolves to a source node index,
        /// or to a literal when nothing is wired into it.</summary>
        /// <typeparam name="T">The value type this pin accepts.</typeparam>
        /// <param name="context">The port definition context of the node being defined.</param>
        /// <param name="portName">Port name; the baker reads the pin back by it.</param>
        /// <param name="displayName">Label on the canvas.</param>
        /// <param name="tooltip">What the value is used for.</param>
        public static void AddData<T>(Node.IPortDefinitionContext context, string portName,
            string displayName, string tooltip)
        {
            context.AddInputPort<T>(portName)
                .WithDisplayName(displayName ?? string.Empty)
                .WithTooltip(tooltip ?? string.Empty)
                .Build();
        }

        /// <summary>
        /// A string value pin that is a DROPDOWN of <paramref name="choices"/> rather than a text
        /// box — for the pins whose legal values are a known list (a registry row, a field of one).
        /// </summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        /// <param name="portName">Port name; the baker reads it by this.</param>
        /// <param name="displayName">Label on the node.</param>
        /// <param name="tooltip">Hover text.</param>
        /// <param name="choices">The values to offer. Empty or null leaves a plain text field —
        /// which is also what happens on a Graph Toolkit that will not take the attribute, so a
        /// pin authored either way keeps working (see <see cref="PortChoices"/>).</param>
        public static void AddChoiceData(Node.IPortDefinitionContext context, string portName,
            string displayName, string tooltip, IReadOnlyList<string> choices)
        {
            var builder = context.AddInputPort<string>(portName)
                .WithDisplayName(displayName ?? string.Empty)
                .WithTooltip(tooltip ?? string.Empty);

            PortChoices.TryOffer(builder, choices);
            builder.Build();
        }

        /// <summary>Adds a NAMED typed value OUTPUT — a return pin a downstream data pin can
        /// read (a task call's [TaskOutput], flowing inside the program).</summary>
        public static void AddDataOut<T>(Node.IPortDefinitionContext context, string portName,
            string displayName, string tooltip)
        {
            context.AddOutputPort<T>(portName)
                .WithDisplayName(displayName ?? string.Empty)
                .WithTooltip(tooltip ?? string.Empty)
                .Build();
        }

        /// <summary>Whether an instruction is a DATA node: pulled on demand by whoever reads its
        /// result, never reached by an exec wire. The complement — <see cref="IsExecKind"/> — is what
        /// an exec pin is allowed to point at.</summary>
        /// <param name="kind">The instruction to classify.</param>
        /// <returns>True for the pull-only instructions.</returns>
        public static bool IsDataKind(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.ConstFloat:
                case GraphTaskNodeKind.ConstString:
                case GraphTaskNodeKind.ConstBool:
                case GraphTaskNodeKind.GetTaskOutputFloat:
                case GraphTaskNodeKind.GetTaskOutputString:
                case GraphTaskNodeKind.GetTaskOutputBool:
                case GraphTaskNodeKind.GetBlackboardFloat:
                case GraphTaskNodeKind.GetBlackboardString:
                case GraphTaskNodeKind.HasBlackboardKey:
                case GraphTaskNodeKind.EvaluateCondition:
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.BoolAnd:
                case GraphTaskNodeKind.BoolOr:
                case GraphTaskNodeKind.BoolNot:
                case GraphTaskNodeKind.ExitStatus:
                case GraphTaskNodeKind.RegistryEntry:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether an instruction is reached by an exec wire.</summary>
        /// <param name="kind">The instruction to classify.</param>
        /// <returns>True for the instructions an exec pin may point at.</returns>
        public static bool IsExecKind(GraphTaskNodeKind kind) => !IsDataKind(kind);

        /// <summary>Whether an instruction can return Running and therefore resume at itself on the
        /// next tick. Latents are legal only in the tick chain: the enter and exit chains run to
        /// completion inside one call, so the runtime logs an error and steps straight past one.
        /// </summary>
        /// <param name="kind">The instruction to classify.</param>
        /// <returns>True for <see cref="GraphTaskNodeKind.Wait"/> and
        /// <see cref="GraphTaskNodeKind.DoTask"/>.</returns>
        public static bool IsLatentKind(GraphTaskNodeKind kind)
            => kind == GraphTaskNodeKind.Wait || kind == GraphTaskNodeKind.DoTask;

        /// <summary>Whether an instruction terminates the tick with a status.</summary>
        /// <param name="kind">The instruction to classify.</param>
        /// <returns>True for the three Return instructions.</returns>
        public static bool IsReturnKind(GraphTaskNodeKind kind)
            => kind == GraphTaskNodeKind.ReturnSuccess || kind == GraphTaskNodeKind.ReturnFailure
                || kind == GraphTaskNodeKind.ReturnRunning;

        /// <summary>The default value of a data pin's type, i.e. what the interpreter reads when the
        /// pin is unwired and the instruction has no literal slot for it (bool false, float 0,
        /// string ""). Used by the bake to decide whether an authored literal needs a synthesized
        /// constant node to survive.</summary>
        /// <param name="valueType">The pin's data type.</param>
        /// <returns>The value the interpreter would use for an unwired pin.</returns>
        public static object DefaultOf(Type valueType)
        {
            if (valueType == typeof(bool))
                return false;
            if (valueType == typeof(float))
                return 0f;
            if (valueType == typeof(string))
                return string.Empty;
            return null;
        }
    }
}
