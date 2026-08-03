using System.Collections.Generic;
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
    ///
    /// It also carries the per-state PARAMETER OVERRIDES (M7f). The graph's variables are its
    /// parameters and the graph holds their defaults; the overrides live HERE, on the wrapper the
    /// state owns, because that is what makes them per-use — the same live graph reference at two
    /// different speeds — and it is why they are applied to the fresh instance rather than to the
    /// referenced asset.
    /// </summary>
    [StateTreeCategory("Tasks/Composite", "Run a logic-graph task by live reference")]
    public sealed class RunGraphTask : StateTreeTaskAsset
    {
        /// <summary>The .taskgraph file's main (baked) asset.</summary>
        public GraphTaskAsset graph;

        /// <summary>This state's overrides of the graph's parameters, one row per overridden
        /// parameter. Rows for parameters the graph no longer declares are kept rather than
        /// silently dropped (the graph may be mid-edit, and the inspector shows them as stale);
        /// the interpreter ignores them with one warning.</summary>
        public List<GraphTaskParameterOverride> overrides = new List<GraphTaskParameterOverride>();

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
            // Before OnEnter, so the enter chain already reads this state's values — and on the
            // INSTANCE, so the authored graph asset is never written to.
            m_Instance.ApplyOverrides(overrides);
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
