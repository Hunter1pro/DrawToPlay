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
    ///
    /// PASS-THROUGH (M7i): an override row may take its value from a parameter of the TREE this
    /// state lives in (<see cref="GraphTaskParameterOverride.sourceParameterId"/>) rather than from
    /// a literal, so a graph reused by three states can be driven by each state's tree rather than
    /// re-tuned by hand in each. Resolved per activation against the running tree's parameter scope
    /// and into a COPY of the row list — the serialized rows are authored data, and burning this
    /// frame's value into them would turn a wire back into a constant.
    /// </summary>
    [StateTreeCategory("Tasks/Composite", "Run a logic-graph task by live reference")]
    public sealed class RunGraphTask : StateTreeTaskAsset, IStateTreeOutputSource
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

        /// <summary>One report per instance for rows whose pass-through source is gone or retyped:
        /// a state re-entered every second must not flood the console with the same wear.</summary>
        [System.NonSerialized] private bool m_UnresolvedSourceLogged;

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
            m_Instance.ApplyOverrides(ResolvedOverrides(context));
            m_Instance.OnEnter(context);
        }

        /// <summary>The rows as the graph should see them THIS activation: every pass-through row
        /// resolved against the scope of the tree this state is running in, which is why it is read
        /// here (per activation, on the caller's context) rather than baked anywhere. Rows without a
        /// source are the list itself, unchanged and uncopied — the overwhelmingly common case.
        /// A source that no longer exists, or one whose kind no longer matches the parameter the row
        /// overrides, drops the row so the GRAPH's own default stands, and says so once.</summary>
        private List<GraphTaskParameterOverride> ResolvedOverrides(StateTreeContext context)
        {
            string unresolved;
            List<GraphTaskParameterOverride> rows = StateTreeExecutor.ResolveSourceValues(
                context, overrides, graph.parameters, out unresolved);

            if (unresolved != null && !m_UnresolvedSourceLogged)
            {
                m_UnresolvedSourceLogged = true;
                Debug.LogWarning($"RunGraphTask '{name}': override for {unresolved} reads a " +
                    "parameter of the calling tree that no longer exists or no longer has the same " +
                    $"kind. The row is ignored and '{graph.name}' uses its graph default.", this);
            }
            return rows;
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

        /// <summary>
        /// The graph's return values, forwarded from the per-activation instance (M7j). The wrapper
        /// is what the STATE holds and therefore what a transition's route names, but the values were
        /// produced by the program running inside it — so this is a pass-through and nothing else:
        /// the wrapper declares no outputs of its own and adds no interpretation to the graph's.
        ///
        /// ORDERING, and the reason this can be a pass-through at all: the executor collects at the
        /// moment the task returns a terminal status, INSIDE its tick loop and one statement before
        /// it calls <see cref="OnExit"/> (StateTreeExecutor.TickTree, step 2). At that point
        /// <see cref="m_Instance"/> is still alive; one statement later OnExit has destroyed it and
        /// this would return nothing. Anything that moves the capture after the OnExit call silently
        /// empties every graph task's outputs, which is why the executor's capture site carries the
        /// matching comment.
        ///
        /// A null instance — the graph was never assigned, or the wrapper is being asked outside an
        /// activation — returns false rather than throwing: an absent return value is the same
        /// non-event as a task that simply returned nothing.
        /// </summary>
        public bool TryCollectOutputs(List<TaskOutputValue> into)
        {
            return m_Instance != null && m_Instance.TryCollectOutputs(into);
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
