using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Reads text out of the shared blackboard. A missing key reads the empty string, the string
    /// mirror of <see cref="GetBlackboardFloatNode"/>'s 0.
    ///
    /// Use <see cref="HasBlackboardKeyNode"/> when "unset" has to be told apart from "set to
    /// nothing".
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.GetBlackboardString"/>: <c>stringValue</c> the key.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Blackboard", null, "Get String")]
    public class GetBlackboardStringNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the blackboard key. Baked as a constant.</summary>
        public const string KeyPortName = "key";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.GetBlackboardString;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<string>(context, KeyPortName, "Key",
                "Blackboard key to read. Baked as a constant — wiring it has no effect.");
            TaskGraphPorts.AddResult<string>(context, "The stored text, or \"\" when the key is unset.");
        }
    }
}
