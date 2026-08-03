using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// WHY the task is leaving, as a number, for the exit chain to branch on:
    /// <see cref="SuccessValue"/>, <see cref="FailureValue"/>, <see cref="CancelledValue"/> —
    /// the values of <see cref="StateTreeStatus.Success"/>, <see cref="StateTreeStatus.Failure"/> and
    /// <see cref="StateTreeStatus.Cancelled"/>.
    ///
    /// THE ONE THAT MATTERS IS CANCELLED. It means an interrupt transition pre-empted the task
    /// mid-flight rather than the task finishing, and it is the case where teardown usually differs:
    /// a cancelled attack has to put the weapon down, a finished one does not. Compare against
    /// <see cref="CancelledValue"/> with a <see cref="CompareFloatNode"/> and branch.
    ///
    /// Reads 0 outside the exit chain, so a Branch on it elsewhere silently takes the "success"
    /// path — the bake reports it instead of leaving that to be discovered.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.ExitStatus"/>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Lifecycle", null, "Exit Status")]
    public class ExitStatusNode : Node, ITaskGraphNode
    {
        /// <summary>Value this node reads when the task finished successfully.</summary>
        public const float SuccessValue = 0f;

        /// <summary>Value this node reads when the task failed.</summary>
        public const float FailureValue = 1f;

        /// <summary>Value this node reads when the task was interrupted.</summary>
        public const float CancelledValue = 2f;

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.ExitStatus;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddResult<float>(context,
                "0 Success, 1 Failure, 2 Cancelled. Only meaningful in the On Exit chain.");
        }
    }
}
