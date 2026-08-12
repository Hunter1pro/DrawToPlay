using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Base for every "ask a library condition" node: the M6 conditions — target in range, line of
    /// sight, cooldown ready, health under a threshold — as a bool a
    /// <see cref="BranchNode"/> can use. Evaluated fresh every time something pulls it, never
    /// cached, because a condition that answered last tick is not an answer.
    ///
    /// THE RESULT PORT IS PLAIN <c>bool</c>, and that is the one real difference from M7's
    /// <see cref="StateTreeConditionNode"/>, whose output is typed with the concrete condition class
    /// so that it fits a transition's condition slot and nothing else. Here a condition is one term
    /// among many: it has to combine with a comparison, a blackboard test and a Not through
    /// <see cref="BoolAndNode"/> and friends, and only a shared type does that.
    ///
    /// Parameters mirror the condition's public fields 1:1 by construction, through the same
    /// <see cref="LibraryParameterPorts"/> both graph flavours use, and each node bakes its own
    /// configured condition sub-asset.
    ///
    /// Abstract on purpose: an abstract node type is skipped by the palette, so only the concrete
    /// wrappers are offered.
    /// </summary>
    [Serializable]
    public abstract class ConditionValueNode : Node, ITaskGraphNode
    {
        /// <summary>
        /// The <see cref="StateTreeConditionAsset"/> subclass this node evaluates. The bake creates
        /// one configured instance of it per node and copies every parameter port into the field of
        /// the same name.
        /// </summary>
        public abstract Type conditionType { get; }

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.EvaluateCondition;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddResult<bool>(context, "What the condition answers, asked afresh on "
                + "every read.");

            LibraryParameterPorts.DefineParameterPorts(context, conditionType, this);
        }
    }
}
