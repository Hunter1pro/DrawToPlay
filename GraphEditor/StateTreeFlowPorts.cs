using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The execution ("flow") wires of a state tree graph: Entry → State, State → Transition →
    /// State. One place owns their names and their look so the three node families cannot drift
    /// apart — they cannot share a base class because <see cref="StateNode"/> must derive from
    /// <see cref="ContextNode"/> while the others derive from <see cref="Node"/>.
    ///
    /// FLOW PORTS ARE DELIBERATELY UNTYPED, and that is not laziness:
    /// <c>PortModel</c>'s capacity rule gives an untyped port Multi capacity in both
    /// directions and a TYPED input Single capacity (UnityEditor.GraphToolkitModule,
    /// PortModel.get_Capacity IL 74310-74354). A state must accept many incoming transitions, so
    /// its "in" port has to be untyped; every port it talks to therefore has to be untyped too.
    /// This is exactly the execution-port shape of the canonical Graph Toolkit sample
    /// (VisualNovelDirector, VisualNovelNode.cs:20-31: no data type, empty display name,
    /// <see cref="PortConnectorUI.Arrowhead"/>). The structural rules the type system cannot carry
    /// as a result are checked in <see cref="StateTreeGraph.OnGraphChanged"/>.
    /// </summary>
    public static class StateTreeFlowPorts
    {
        /// <summary>Name of the incoming-flow port on a <see cref="StateNode"/>.</summary>
        public const string InPortName = "in";

        /// <summary>Name of the outgoing-flow port on a <see cref="StateNode"/>.</summary>
        public const string OutPortName = "out";

        /// <summary>Adds the "enter this state" port: every transition and the Entry node land
        /// here, so it must accept many wires.</summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        public static void AddFlowInPort(Node.IPortDefinitionContext context)
        {
            context.AddInputPort(InPortName)
                .WithDisplayName(string.Empty)
                .WithTooltip("Entered from Entry or from a Transition.")
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }

        /// <summary>Adds the "leave this state" port. Every wire out of it must reach a
        /// <see cref="TransitionNode"/>'s from port.</summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        public static void AddFlowOutPort(Node.IPortDefinitionContext context)
        {
            context.AddOutputPort(OutPortName)
                .WithDisplayName(string.Empty)
                .WithTooltip("Wire to the from port of every Transition that leaves this state.")
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }
    }
}
