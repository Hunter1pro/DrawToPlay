using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Stops this object's tree when its health hits zero — the death teardown a brain-carrying
    /// entity needs: a dead body that keeps hunting is the alternative. StopTree cancels the
    /// running tasks through the normal OnExit(Cancelled) path, so movement and holds tear
    /// down cleanly.
    /// </summary>
    public sealed class DisableRunnerOnDeath : MonoBehaviour
    {
        private HealthComponent m_Health;
        private StateTreeRunner m_Runner;

        private void OnEnable()
        {
            m_Health = GetComponent<HealthComponent>();
            m_Runner = GetComponent<StateTreeRunner>();
            if (m_Health != null)
                m_Health.died += OnDied;
        }

        private void OnDisable()
        {
            if (m_Health != null)
                m_Health.died -= OnDied;
        }

        private void OnDied()
        {
            if (m_Runner != null)
                m_Runner.StopTree();
        }
    }
}
