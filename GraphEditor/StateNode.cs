using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// One state of the tree — the graph face of <see cref="StateTreeNodeAsset"/>.
    ///
    /// It is a <see cref="ContextNode"/> because a state OWNS its tasks: the runner enters a state,
    /// starts every task in it, ticks them in parallel and only leaves when they are all finished
    /// (StateTreeRunner.cs:151-164). A context node holds an ordered list of block nodes that
    /// cannot exist anywhere else, which is that relationship exactly — and the palette inside the
    /// state offers only <c>[UseWithContext(typeof(StateNode))]</c> task blocks, so a state can
    /// never contain something that is not a task.
    ///
    /// Transitions are NOT blocks: they leave the state and reach another one, so they are their
    /// own nodes wired between states (<see cref="TransitionNode"/>).
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("State Tree", null, "State")]
    public class StateNode : ContextNode
    {
        /// <summary>Maps to <c>StateTreeNodeAsset.nodeId</c>; transitions target a state by it.</summary>
        public const string NodeIdPortName = "nodeId";

        /// <summary>Maps to <c>StateTreeNodeAsset.displayName</c>.</summary>
        public const string DisplayNamePortName = "displayName";

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            StateTreeFlowPorts.AddFlowInPort(context);
            StateTreeFlowPorts.AddFlowOutPort(context);

            context.AddInputPort<string>(NodeIdPortName)
                .WithDisplayName("Node Id")
                .WithTooltip("StateTreeNodeAsset.nodeId. Unique within the tree: transitions and the runner's index key off it.")
                .Build();

            context.AddInputPort<string>(DisplayNamePortName)
                .WithDisplayName("Display Name")
                .WithTooltip("StateTreeNodeAsset.displayName. Free text for humans; the runner ignores it.")
                .Build();
        }

        /// <summary>The authored state id, or an empty string when unset.</summary>
        /// <returns>Value of the "nodeId" port.</returns>
        public string GetNodeId()
        {
            return LibraryParameterPorts.TryReadValue(GetInputPortByName(NodeIdPortName), typeof(string), out var value) && value is string text
                ? text
                : string.Empty;
        }

        /// <summary>The authored display name, or an empty string when unset.</summary>
        /// <returns>Value of the "displayName" port.</returns>
        public string GetDisplayName()
        {
            return LibraryParameterPorts.TryReadValue(GetInputPortByName(DisplayNamePortName), typeof(string), out var value) && value is string text
                ? text
                : string.Empty;
        }
    }
}
