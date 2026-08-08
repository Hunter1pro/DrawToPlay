using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Travel back to wherever the expedition was entered from — the service spends
    /// its memory of it. Bakes into one <see cref="ReturnFromExpeditionTask"/>; fails when
    /// there is nothing to return to.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Tasks/Levels", null, "Return From Expedition")]
    public class ReturnFromExpeditionTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(ReturnFromExpeditionTask);
    }
}
