using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Emits a named cue on the context — the presentation hook the rest of the game listens to for
    /// sound, VFX and camera work (<c>StateTreeContext.cueFired</c>). The payload carries the owner,
    /// so a listener knows who fired it.
    ///
    /// This is the one-line form. <see cref="FireCueTask"/> from the M6 library is the configurable
    /// one (offset, a payload key/value pair) and has its own call node; reach for it when a cue
    /// needs to say more than its name.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.FireCue"/>: <c>stringValue</c> the cue name,
    /// <c>exec[0]</c> next.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Cues", null, "Fire Cue")]
    public class FireCueNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the cue name. Baked as a constant.</summary>
        public const string CueNamePortName = "cueName";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.FireCue;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context);

            TaskGraphPorts.AddData<string>(context, CueNamePortName, "Cue Name",
                "The cue to emit. Baked as a constant — wiring it has no effect.");
        }
    }
}
