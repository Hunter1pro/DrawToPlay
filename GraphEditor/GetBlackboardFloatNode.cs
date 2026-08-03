using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Reads a number out of the shared blackboard. A MISSING KEY READS 0 rather than failing, which
    /// is what makes "count up from nothing" work with no initialisation:
    /// <c>Set(k, Get(k) + 1)</c> is correct on the first pass too.
    ///
    /// The value is pulled fresh every time something reads this node, so two reads in one chain see
    /// a write that happened between them.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.GetBlackboardFloat"/>: <c>stringValue</c> the key.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Blackboard", null, "Get Float")]
    public class GetBlackboardFloatNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the blackboard key. Baked as a constant.</summary>
        public const string KeyPortName = "key";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.GetBlackboardFloat;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<string>(context, KeyPortName, "Key",
                "Blackboard key to read. Baked as a constant — wiring it has no effect.");
            TaskGraphPorts.AddResult<float>(context, "The stored number, or 0 when the key is unset.");
        }
    }
}
