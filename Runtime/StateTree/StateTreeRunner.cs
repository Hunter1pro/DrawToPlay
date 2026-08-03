using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Runs a StateTreeAsset against an owner — the scene-facing half of the ported
    /// state_tree_runner.gd. The machine itself lives in <see cref="StateTreeExecutor"/> (so a
    /// tree can also run nested, as a task, via <c>RunSubTreeTask</c>); this component owns only
    /// the MonoBehaviour lifecycle: autoStart on Start, Time.deltaTime on Update, StopTree on
    /// destroy — plus the serialized authoring fields.
    ///
    /// The public surface (fields, events, StartTree/StopTree/TickTree, isRunning,
    /// activeNodeId) is UNCHANGED by that split, including the two error strings the EditMode
    /// tests match on: the executor prefixes them with <see cref="StateTreeExecutor.logLabel"/>,
    /// which is "StateTreeRunner" here.
    ///
    /// TickTree stays public so EditMode tests can drive it without play mode.
    /// </summary>
    public sealed class StateTreeRunner : MonoBehaviour
    {
        public StateTreeAsset data;
        /// <summary>Owner the context wraps; null = this GameObject's parent (or itself
        /// at the hierarchy root) — the owner_path default of the Godot runner.</summary>
        public GameObject ownerObject;
        public bool autoStart = true;

        public StateTreeContext context;

        public event Action treeStarted;
        public event Action treeStopped;
        public event Action<string> nodeEntered;
        public event Action<string> nodeLeft;
        public event Action<string, string> activeNodeChanged;

        private StateTreeExecutor m_Executor;

        public bool isRunning => m_Executor != null && m_Executor.isRunning;

        public string activeNodeId => m_Executor != null ? m_Executor.activeNodeId : "";

        private void Start()
        {
            if (autoStart && data != null)
                StartTree();
        }

        private void OnDestroy()
        {
            StopTree();
        }

        public void StartTree()
        {
            StateTreeExecutor executor = EnsureExecutor();
            executor.data = data;
            executor.context = context;
            executor.owner = ownerObject != null
                ? ownerObject
                : (transform.parent != null ? transform.parent.gameObject : gameObject);
            executor.StartTree();
            // Adopt whatever context the executor ran with: it creates one when this component
            // was never handed a context, and callers read runner.context afterwards.
            context = executor.context;
        }

        public void StopTree()
        {
            if (m_Executor != null)
                m_Executor.StopTree();
        }

        private void Update()
        {
            TickTree(Time.deltaTime);
        }

        /// <summary>One runner tick — the _process body, callable headless.</summary>
        public void TickTree(float deltaTime)
        {
            if (m_Executor != null)
                m_Executor.TickTree(deltaTime);
        }

        /// <summary>The executor is created lazily and kept for the component's lifetime, so the
        /// event re-raisers are wired exactly once. A runner that never starts never allocates
        /// one, which matters for the FindObjectsByType sweeps the editor tooling does.</summary>
        private StateTreeExecutor EnsureExecutor()
        {
            if (m_Executor != null)
                return m_Executor;

            m_Executor = new StateTreeExecutor
            {
                logLabel = "StateTreeRunner",
                logContext = this
            };
            m_Executor.treeStarted += RaiseTreeStarted;
            m_Executor.treeStopped += RaiseTreeStopped;
            m_Executor.nodeEntered += RaiseNodeEntered;
            m_Executor.nodeLeft += RaiseNodeLeft;
            m_Executor.activeNodeChanged += RaiseActiveNodeChanged;
            return m_Executor;
        }

        // Named handlers rather than lambdas: no closure allocation, and the subscription in
        // EnsureExecutor stays readable.
        private void RaiseTreeStarted() => treeStarted?.Invoke();

        private void RaiseTreeStopped() => treeStopped?.Invoke();

        private void RaiseNodeEntered(string nodeId) => nodeEntered?.Invoke(nodeId);

        private void RaiseNodeLeft(string nodeId) => nodeLeft?.Invoke(nodeId);

        private void RaiseActiveNodeChanged(string from, string to) => activeNodeChanged?.Invoke(from, to);
    }
}
