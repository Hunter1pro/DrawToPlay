using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The root of a state tree graph: it names the tree and points at the state the runner starts
    /// in. One per graph (<see cref="StateTreeGraph.OnGraphChanged"/> enforces it), mirroring the
    /// canonical sample's StartNode.
    ///
    /// It is a node rather than a property of the graph because the entry has to be VISIBLE: the
    /// baked root is an organizational <see cref="StateTreeNodeAsset"/> whose first child is the
    /// entry state, which is what makes <c>StateTreeRunner.ResolveEntryNode</c> descend into it
    /// (StateTreeRunner.cs:190-198). Drawing that as a wire keeps the runtime rule and the picture
    /// the same thing.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("State Tree", null, "Entry")]
    public class EntryNode : Node
    {
        /// <summary>Output flow port; wire it to the state the runner starts in.</summary>
        public const string EntryPortName = "entry";

        /// <summary>Maps to <c>StateTreeAsset.treeName</c>.</summary>
        public const string TreeNamePortName = "treeName";

        /// <summary>Maps to <c>StateTreeAsset.treeKind</c>.</summary>
        public const string TreeKindPortName = "treeKind";

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort(EntryPortName)
                .WithDisplayName(string.Empty)
                .WithTooltip("The state the runner enters when the tree starts.")
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();

            context.AddInputPort<string>(TreeNamePortName)
                .WithDisplayName("Tree Name")
                .WithTooltip("StateTreeAsset.treeName. Leave empty to use the graph asset's file name.")
                .Build();

            context.AddInputPort<string>(TreeKindPortName)
                .WithDisplayName("Tree Kind")
                .WithTooltip("StateTreeAsset.treeKind. \"enemy_ai\" for an AI tree, \"player_flow\" for the player tree.")
                .WithDefaultValue(StateTreeGraph.DefaultTreeKind)
                .Build();
        }

        /// <summary>The authored tree name, or an empty string when the author left it blank.</summary>
        /// <returns>Value of the "treeName" port.</returns>
        public string GetTreeName()
        {
            return LibraryParameterPorts.TryReadValue(GetInputPortByName(TreeNamePortName), typeof(string), out var value) && value is string text
                ? text
                : string.Empty;
        }

        /// <summary>The authored tree kind, defaulting to <see cref="StateTreeGraph.DefaultTreeKind"/>.</summary>
        /// <returns>Value of the "treeKind" port.</returns>
        public string GetTreeKind()
        {
            if (LibraryParameterPorts.TryReadValue(GetInputPortByName(TreeKindPortName), typeof(string), out var value)
                && value is string text && !string.IsNullOrEmpty(text))
            {
                return text;
            }
            return StateTreeGraph.DefaultTreeKind;
        }
    }
}
