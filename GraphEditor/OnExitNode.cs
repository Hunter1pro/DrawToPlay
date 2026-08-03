using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Start of the chain that runs ONCE as the task leaves — the graph face of
    /// <c>StateTreeTaskAsset.OnExit(status)</c>, and the only place
    /// <see cref="ExitStatusNode"/> reads anything.
    ///
    /// THIS CHAIN RUNS FOR EVERY WAY OUT, including <see cref="StateTreeStatus.Cancelled"/> when an
    /// interrupt transition pre-empts the task mid-flight. That is what makes it the teardown hook:
    /// clear the blackboard key you set on enter, stop the loop you started, fire the "stopped" cue.
    /// Branch on <see cref="ExitStatusNode"/> when the teardown differs between finishing and being
    /// cancelled.
    ///
    /// Like the enter chain it runs to completion inside one call and cannot wait.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Lifecycle", null, "On Exit")]
    public class OnExitNode : Node
    {
        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.ExecOutPortName, string.Empty,
                "Runs once as the task leaves, whatever the status. Nothing here may wait.");
        }
    }
}
