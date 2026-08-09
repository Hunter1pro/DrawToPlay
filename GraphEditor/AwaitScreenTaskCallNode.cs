using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Raise a key-watched screen and wait for its answer — <c>await ui.Process()</c>
    /// as a program node: LATENT, resumes when a button writes the answer key, then takes the
    /// Success pin. Runs one <see cref="AwaitScreenTask"/>; its parameter ports mirror that
    /// type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/UI", null, "Await Screen")]
    public class AwaitScreenTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(AwaitScreenTask);
    }
}
