using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Runs a graph-authored task program by LIVE REFERENCE — the state holds this thin
    /// wrapper, the wrapper holds the .taskgraph file's baked GraphTaskAsset, so editing
    /// the graph reaches every state that uses it with no copy to re-sync (the same
    /// by-reference model RunSubTreeTask uses for trees).
    ///
    /// A fresh instance of the program is created per activation (OnEnter) and destroyed
    /// on OnExit, so two states — or two runners — sharing one graph never share timers
    /// or latent positions. GraphTaskAsset's own interpreter provides the per-instance
    /// copies of embedded tasks/conditions, the recursion depth guard, and Cancelled
    /// propagation into an active latent; this wrapper only forwards the lifecycle.
    /// </summary>
    [StateTreeCategory("Tasks/Composite", "Run a logic-graph task by live reference")]
    public sealed class RunGraphTask : StateTreeTaskAsset
    {
        /// <summary>The .taskgraph file's main (baked) asset.</summary>
        public GraphTaskAsset graph;

        [System.NonSerialized] private GraphTaskAsset m_Instance;
        [System.NonSerialized] private bool m_WarnedNull;

        public override void OnEnter(StateTreeContext context)
        {
            if (graph == null)
            {
                if (!m_WarnedNull)
                {
                    m_WarnedNull = true;
                    Debug.LogError($"RunGraphTask '{name}': no graph assigned.", this);
                }
                return;
            }
            m_Instance = Instantiate(graph);
            m_Instance.OnEnter(context);
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Instance == null)
                return StateTreeStatus.Failure;
            return m_Instance.OnTick(context, deltaTime);
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (m_Instance == null)
                return;
            m_Instance.OnExit(context, status);
            DestroyInstance();
        }

        private void OnDestroy()
        {
            DestroyInstance();
        }

        private void DestroyInstance()
        {
            if (m_Instance == null)
                return;
            if (Application.isPlaying)
                Destroy(m_Instance);
            else
                DestroyImmediate(m_Instance);
            m_Instance = null;
        }
    }
}
