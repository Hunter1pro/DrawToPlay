using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Transition to a level; Running until it lands (M16). The 'level' port is the
    /// destination's NAME (resolved against the tree's registry — connect one on the Entry
    /// node), 'levelNameKey' optionally makes the destination dynamic. Bakes into one
    /// <see cref="LoadLevelTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Levels", null, "Load Level")]
    public class LoadLevelTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(LoadLevelTask);
    }
}
