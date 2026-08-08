using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Transition to a level; the DoTask resumes across ticks until it lands
    /// (M16). 'level' is the destination's NAME (free-typed entry reference),
    /// 'levelNameKey' makes it dynamic. Calls <see cref="LoadLevelTask"/>; its parameter
    /// ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Levels", null, "Load Level")]
    public class LoadLevelTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(LoadLevelTask);
    }
}
