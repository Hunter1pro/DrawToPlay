using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// A piece of text, on the canvas. Same job as <see cref="ConstFloatNode"/>: worth a node when
    /// one string feeds several pins — a blackboard value written from two places, a cue name shared
    /// by two branches.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.ConstString"/>: <c>stringValue</c>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "String")]
    public class ConstStringNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the input port holding the constant.</summary>
        public const string ValuePortName = "value";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.ConstString;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<string>(context, ValuePortName, "Value", "The text this node is.");
            TaskGraphPorts.AddResult<string>(context, "The text.");
        }
    }
}
