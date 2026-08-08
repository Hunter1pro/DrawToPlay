using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Runs a Blueprint-style task graph as this state's task — the composition
    /// block that lets a state on the STATE canvas execute a program authored on the TASK
    /// canvas. Bakes into one <see cref="RunGraphTask"/>; the 'graph' port takes the
    /// .taskgraph's baked program asset.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Composition", null, "Run Task Graph")]
    public class RunGraphTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(RunGraphTask);
    }
}
