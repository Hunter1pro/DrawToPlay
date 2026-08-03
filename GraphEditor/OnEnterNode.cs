using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Start of the chain that runs ONCE, the moment the task is entered — the graph face of
    /// <c>StateTreeTaskAsset.OnEnter</c>. Use it to seed the blackboard, fire an entry cue or reset a
    /// counter.
    ///
    /// The chain runs to completion inside the enter call, so it cannot wait: a
    /// <see cref="WaitNode"/> or a task call on it is reported by the bake and stepped straight past
    /// at runtime. Everything that takes time belongs on <see cref="OnTickNode"/>.
    ///
    /// This node is not an instruction — it bakes to <c>GraphTaskAsset.enterEntry</c>, the INDEX of
    /// whatever its pin reaches.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Lifecycle", null, "On Enter")]
    public class OnEnterNode : Node
    {
        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.ExecOutPortName, string.Empty,
                "Runs once when the task is entered. Nothing here may wait.");
        }
    }
}
