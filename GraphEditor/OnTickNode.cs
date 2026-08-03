using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Start of the chain that runs EVERY TICK — the graph face of
    /// <c>StateTreeTaskAsset.OnTick</c>, and the one chain a task normally has.
    ///
    /// TWO RULES MAKE THE WHOLE EXECUTION MODEL, and both are the Blueprint ones:
    /// <list type="bullet">
    /// <item>A chain that runs off the end without hitting a Return leaves the task RUNNING. The task
    /// stays alive until it says otherwise, which is why "do nothing this tick" needs no node.</item>
    /// <item>A latent instruction (<see cref="WaitNode"/>, a task call that returns Running) parks the
    /// chain ON ITSELF. The next tick RESUMES there — it does not restart from this node — so a chain
    /// reads top to bottom as a sequence even though it is driven one tick at a time.</item>
    /// </list>
    ///
    /// With no On Tick node at all the task succeeds immediately (<c>tickEntry</c> stays -1), which is
    /// the correct behaviour for a task that only has an enter chain.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Lifecycle", null, "On Tick")]
    public class OnTickNode : Node
    {
        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.ExecOutPortName, string.Empty,
                "Runs every tick, or resumes at the latent instruction the chain is parked on.");
        }
    }
}
