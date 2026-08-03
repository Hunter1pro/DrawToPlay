using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Fires the weapon def's projectile prefab from a muzzle offset toward the target — the archer's shot.
    /// Calls <see cref="SpawnProjectileTask"/>: Failure when there is no target, no weapon or the
    /// cooldown is still running, so the Failure pin is where the "can't shoot yet" branch goes. Its
    /// parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Combat", null, "Spawn Projectile")]
    public class SpawnProjectileTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(SpawnProjectileTask);
    }
}
