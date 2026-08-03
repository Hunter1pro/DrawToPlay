using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Holds the chain for a number of seconds. THE LATENT PRIMITIVE, and the reason a task graph
    /// reads like a script instead of like a tick handler: the chain parks here, the task returns
    /// Running, and the next tick RESUMES AT THIS NODE rather than restarting from
    /// <see cref="OnTickNode"/>. When the time is up the chain continues out of
    /// <see cref="TaskGraphPorts.ExecOutPortName"/> in the same tick.
    ///
    /// The timer is reset each time the chain reaches this node afresh, so a Wait inside a loop waits
    /// again on every pass. Legal in the tick chain ONLY: the enter and exit chains complete inside
    /// one call, and the runtime steps past a latent there with an error.
    ///
    /// This is NOT <see cref="WaitTask"/> from the M6 library (which is a whole task, with jitter, and
    /// has its own call node). Use this one to pace a chain; use the library task when you want a
    /// state whose entire job is waiting.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.Wait"/>: <c>data[0]</c> the duration (or
    /// <c>floatValue</c> when the pin is unwired), <c>exec[0]</c> next.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Flow", null, "Wait")]
    public class WaitNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the float data pin holding the duration in seconds.</summary>
        public const string SecondsPortName = "seconds";

        /// <summary>Duration a freshly dropped Wait holds for.</summary>
        public const float DefaultSeconds = 1f;

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.Wait;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context);

            context.AddInputPort<float>(SecondsPortName)
                .WithDisplayName("Seconds")
                .WithTooltip("How long to hold the chain. Wire it for a computed delay, or type a "
                    + "constant. Zero or less passes straight through.")
                .WithDefaultValue(DefaultSeconds)
                .Build();
        }
    }
}
