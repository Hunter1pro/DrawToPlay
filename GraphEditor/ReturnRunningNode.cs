using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Ends the tick with <see cref="StateTreeStatus.Running"/> — nothing to report, ask again next
    /// tick. The next tick starts from <see cref="OnTickNode"/> again, unlike a latent
    /// <see cref="WaitNode"/> which resumes where it parked.
    ///
    /// Running is also what falling off the end of a chain does, so this node exists for the case
    /// where the chain would otherwise continue: an early out in the middle of a sequence. Reaching
    /// for it everywhere is a sign the chain wants a Branch instead.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.ReturnRunning"/>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Flow", null, "Return Running")]
    public class ReturnRunningNode : Node, ITaskGraphNode
    {
        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.ReturnRunning;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
        }
    }
}
