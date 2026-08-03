using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The if. Pulls the bool on <see cref="ConditionPortName"/> and continues down
    /// <see cref="TaskGraphPorts.TrueExecPortName"/> or
    /// <see cref="TaskGraphPorts.FalseExecPortName"/>.
    ///
    /// An empty pin is an end of chain, not an error: "if hurt, flee; otherwise carry on running" is
    /// a Branch with only the true pin wired. An unwired CONDITION is false, so a Branch nobody fed
    /// takes the false pin every time.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.Branch"/>: <c>data[0]</c> the condition, <c>exec[0]</c>
    /// true, <c>exec[1]</c> false.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Flow", null, "Branch")]
    public class BranchNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the bool data pin that decides which way this node goes.</summary>
        public const string ConditionPortName = "condition";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.Branch;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.TrueExecPortName, "True",
                "Runs when the condition is true.");
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.FalseExecPortName, "False",
                "Runs when the condition is false — including when nothing is wired into it.");

            TaskGraphPorts.AddData<bool>(context, ConditionPortName, "Condition",
                "The bool this node branches on. Wire a condition, a comparison or a blackboard test "
                + "into it.");
        }
    }
}
