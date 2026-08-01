using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// One way out of a state — the graph face of <see cref="StateTreeTransition"/>. It carries the
    /// three things the runtime struct carries: where it goes ("to"), what has to be true
    /// ("condition") and whether it is an interrupt ("checkWhileRunning").
    ///
    /// WHY A NODE AND NOT A PORT ON THE STATE. A transition is the one place in this model that
    /// owns data of its own, and giving it a node means its condition slot is a real port that only
    /// accepts condition nodes. The alternative — N transition slots generated on the state from a
    /// count option — would make the author bump a counter before every wire, and node options
    /// cannot be written programmatically at all (see the header of
    /// <see cref="StateTreeGraph"/>).
    ///
    /// THE INTERRUPT FLAG IS THE WHOLE PERSONALITY OF A TRANSITION. checkWhileRunning transitions
    /// are evaluated every tick BEFORE the tasks tick, and firing one cancels the running tasks
    /// with <see cref="StateTreeStatus.Cancelled"/>; the others are evaluated only once every task
    /// in the state has finished (StateTreeRunner.cs:100-148). Same wire, opposite behaviour, so
    /// the toggle sits on the face of the node.
    ///
    /// ONE TRANSITION CAN SERVE SEVERAL STATES. The "from" port accepts many wires, and the bake
    /// collects transitions per source state, so wiring three states into one "target lost → idle"
    /// transition writes that same <see cref="StateTreeTransition"/> into all three. The "to" port
    /// is the opposite: <c>targetNodeId</c> is a single id, so more than one target is an error.
    ///
    /// EVALUATION ORDER IS AUTHORED, NOT INCIDENTAL. The runner takes the FIRST matching transition
    /// of each kind, and the order wires happen to have been drawn in is invisible on screen. The
    /// "order" port is therefore an authoring-only sort key (ascending; it has no runtime field):
    /// "in range → attack" must be able to sit in front of the unconditional "→ idle" that catches
    /// a dead target, which is how the M6 presets express "otherwise" without a negated condition.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("State Tree", null, "Transition")]
    public class TransitionNode : Node
    {
        /// <summary>Input flow port; wire it from the source state's "out" port.</summary>
        public const string FromPortName = "from";

        /// <summary>Output flow port; wire it to the target state's "in" port. The target state's
        /// <c>nodeId</c> becomes <c>StateTreeTransition.targetNodeId</c>.</summary>
        public const string ToPortName = "to";

        /// <summary>Maps to <c>StateTreeTransition.condition</c>. Typed
        /// <see cref="StateTreeConditionAsset"/>, so only condition nodes fit; leaving it empty
        /// means "always true" (StateTreeRunner.Eval treats a null condition as satisfied,
        /// StateTreeRunner.cs:210-213).</summary>
        public const string ConditionPortName = "condition";

        /// <summary>Maps to <c>StateTreeTransition.checkWhileRunning</c>.</summary>
        public const string CheckWhileRunningPortName = "checkWhileRunning";

        /// <summary>Authoring-only evaluation order (ascending). No runtime field.</summary>
        public const string OrderPortName = "order";

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort(FromPortName)
                .WithDisplayName(string.Empty)
                .WithTooltip("The state this transition leaves.")
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();

            context.AddOutputPort(ToPortName)
                .WithDisplayName(string.Empty)
                .WithTooltip("The state this transition enters. Exactly one.")
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();

            context.AddInputPort<StateTreeConditionAsset>(ConditionPortName)
                .WithDisplayName("Condition")
                .WithTooltip("StateTreeTransition.condition. Empty means the transition always fires.")
                .Build();

            context.AddInputPort<bool>(CheckWhileRunningPortName)
                .WithDisplayName("Check While Running")
                .WithTooltip("StateTreeTransition.checkWhileRunning. On = interrupt: evaluated every tick before the tasks run, and firing it cancels them.")
                .Build();

            context.AddInputPort<int>(OrderPortName)
                .WithDisplayName("Order")
                .WithTooltip("Authoring only: lower values are evaluated first. Ties keep wire order.")
                .Build();
        }

        /// <summary>Whether this transition is an interrupt.</summary>
        /// <returns>Value of the "checkWhileRunning" port.</returns>
        public bool GetCheckWhileRunning()
        {
            return LibraryParameterPorts.TryReadValue(GetInputPortByName(CheckWhileRunningPortName), typeof(bool), out var value)
                && value is bool flag && flag;
        }

        /// <summary>The authored evaluation order; 0 when unset.</summary>
        /// <returns>Value of the "order" port.</returns>
        public int GetOrder()
        {
            return LibraryParameterPorts.TryReadValue(GetInputPortByName(OrderPortName), typeof(int), out var value) && value is int order
                ? order
                : 0;
        }

        /// <summary>The condition node wired into this transition, if any.</summary>
        /// <returns>The connected <see cref="StateTreeConditionNode"/>, or null.</returns>
        public StateTreeConditionNode GetConditionNode()
        {
            var port = GetInputPortByName(ConditionPortName);
            if (port == null || !port.IsConnected)
                return null;
            return port.FirstConnectedPort?.GetNode() as StateTreeConditionNode;
        }

        /// <summary>The state this transition enters.</summary>
        /// <returns>The single connected <see cref="StateNode"/>, or null when the graph is
        /// unfinished or ambiguous (both are reported by
        /// <see cref="StateTreeGraph.OnGraphChanged"/>).</returns>
        public StateNode GetTargetState()
        {
            var targets = StateTreeGraph.CollectConnectedNodes<StateNode>(GetOutputPortByName(ToPortName));
            return targets.Count == 1 ? targets[0] : null;
        }
    }
}
