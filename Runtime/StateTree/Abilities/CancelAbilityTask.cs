using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// CANCEL the owner's active ability (M23) — the reaction states' half of the model. An
    /// ability runs on its own executor, deliberately decoupled from the mind-tree that
    /// started it (fire-and-forget is a feature) — which means a flinch or a death that
    /// interrupts the MIND does not, by itself, interrupt the SWING: the live run showed a
    /// staggering raider whose strike kept travelling. A reaction state that means "and stop
    /// what you were doing" says so with this task, the way the HT hosts force-cancel on
    /// death.
    /// </summary>
    [StateTreeCategory("Tasks/Abilities", "Cancel whatever ability the owner is running")]
    public sealed class CancelAbilityTask : StateTreeTaskAsset
    {
        [InjectOwner] private AbilityHost m_Host;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Host != null)
                m_Host.Cancel();
            return StateTreeStatus.Success;
        }
    }
}
