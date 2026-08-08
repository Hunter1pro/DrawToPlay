using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Travel to an expedition level, the service remembering the way back. Bakes
    /// into one <see cref="EnterExpeditionTask"/>; its parameter ports mirror that type's
    /// fields 1:1 (the 'expedition' port holds the level's NAME — a free-typed entry
    /// reference).</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Tasks/Levels", null, "Enter Expedition")]
    public class EnterExpeditionTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(EnterExpeditionTask);
    }
}
