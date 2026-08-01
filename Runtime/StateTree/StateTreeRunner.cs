using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Runs a StateTreeAsset against an owner — verbatim port of state_tree_runner.gd.
    /// Deep-copies the tree on StartTree so each runner owns its task instances.
    /// Tick order (the load-bearing semantics, per brief §7.1): interrupts
    /// (checkWhileRunning) → task ticks → on-completion transitions when all tasks are
    /// done. Any pre-emption exits running tasks with Cancelled.
    /// TickTree is public so EditMode tests can drive it without play mode; Update
    /// forwards Time.deltaTime in play mode.
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

        private StateTreeAsset m_ActiveData;
        private readonly Dictionary<string, StateTreeNodeAsset> m_NodeIndex =
            new Dictionary<string, StateTreeNodeAsset>();
        private StateTreeNodeAsset m_CurrentNode;
        private readonly List<StateTreeTaskAsset> m_RunningTasks = new List<StateTreeTaskAsset>();
        private readonly List<StateTreeTaskAsset> m_Finished = new List<StateTreeTaskAsset>();

        public bool isRunning => m_CurrentNode != null;

        public string activeNodeId => m_CurrentNode != null ? m_CurrentNode.nodeId : "";

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
            if (data == null || data.root == null)
            {
                Debug.LogError("StateTreeRunner: data or root is null", this);
                return;
            }
            m_ActiveData = data.DeepCopy();
            m_NodeIndex.Clear();
            BuildIndex(m_ActiveData.root);

            GameObject owner = ownerObject;
            if (owner == null)
                owner = transform.parent != null ? transform.parent.gameObject : gameObject;
            if (context == null)
                context = new StateTreeContext(owner);
            else if (context.owner == null)
                context.owner = owner;

            treeStarted?.Invoke();
            var entry = ResolveEntryNode(m_ActiveData.root);
            EnterNode(entry);
            activeNodeChanged?.Invoke("", entry.nodeId);
        }

        public void StopTree()
        {
            if (m_CurrentNode == null)
                return;
            nodeLeft?.Invoke(m_CurrentNode.nodeId);
            ExitRunningTasks(StateTreeStatus.Cancelled);
            m_CurrentNode = null;
            treeStopped?.Invoke();
            // Release the deep copy (finding B, M6 test agent): Instantiate copies live
            // until domain reload otherwise, leaking a tree per restart.
            StateTreeAsset.DestroyCopy(m_ActiveData);
            m_ActiveData = null;
            m_NodeIndex.Clear();
        }

        private void Update()
        {
            TickTree(Time.deltaTime);
        }

        /// <summary>One runner tick — the _process body, callable headless.</summary>
        public void TickTree(float deltaTime)
        {
            if (m_CurrentNode == null)
                return;

            // 1. Interrupt transitions — evaluated every tick, before task ticks.
            var transitions = m_CurrentNode.transitions;
            for (int i = 0; i < transitions.Count; i++)
            {
                var tr = transitions[i];
                if (tr == null || !tr.checkWhileRunning)
                    continue;
                if (Eval(tr.condition))
                {
                    TransitionTo(tr.targetNodeId);
                    return;
                }
            }

            // 2. Tick active tasks; finished tasks exit with their own status.
            m_Finished.Clear();
            for (int i = 0; i < m_RunningTasks.Count; i++)
            {
                var task = m_RunningTasks[i];
                StateTreeStatus status = task.OnTick(context, deltaTime);
                if (status != StateTreeStatus.Running)
                {
                    task.OnExit(context, status);
                    m_Finished.Add(task);
                }
            }
            for (int i = 0; i < m_Finished.Count; i++)
                m_RunningTasks.Remove(m_Finished[i]);

            // 3. On-completion transitions — only when every task is done.
            if (m_RunningTasks.Count == 0)
            {
                for (int i = 0; i < transitions.Count; i++)
                {
                    var tr = transitions[i];
                    if (tr == null || tr.checkWhileRunning)
                        continue;
                    if (Eval(tr.condition))
                    {
                        TransitionTo(tr.targetNodeId);
                        return;
                    }
                }
            }
        }

        private void EnterNode(StateTreeNodeAsset node)
        {
            m_CurrentNode = node;
            m_RunningTasks.Clear();
            for (int i = 0; i < node.tasks.Count; i++)
            {
                var task = node.tasks[i];
                if (task == null)
                    continue;
                task.OnEnter(context);
                m_RunningTasks.Add(task);
            }
            nodeEntered?.Invoke(node.nodeId);
        }

        private void TransitionTo(string targetId)
        {
            if (string.IsNullOrEmpty(targetId) || !m_NodeIndex.TryGetValue(targetId, out var target))
            {
                Debug.LogError($"StateTreeRunner: unknown transition target '{targetId}'", this);
                return;
            }
            string previousId = m_CurrentNode != null ? m_CurrentNode.nodeId : "";
            target = ResolveEntryNode(target);
            nodeLeft?.Invoke(previousId);
            ExitRunningTasks(StateTreeStatus.Cancelled);
            EnterNode(target);
            activeNodeChanged?.Invoke(previousId, target.nodeId);
        }

        private void ExitRunningTasks(StateTreeStatus status)
        {
            for (int i = 0; i < m_RunningTasks.Count; i++)
                m_RunningTasks[i].OnExit(context, status);
            m_RunningTasks.Clear();
        }

        /// <summary>Organizational nodes resolve to their first leaf: descend while the
        /// node has no tasks, no transitions, and at least one child.</summary>
        private static StateTreeNodeAsset ResolveEntryNode(StateTreeNodeAsset node)
        {
            var current = node;
            int guard = 0;
            while (current != null && current.tasks.Count == 0 && current.transitions.Count == 0
                && current.children.Count > 0 && guard++ < 256)
                current = current.children[0];
            return current;
        }

        private void BuildIndex(StateTreeNodeAsset node)
        {
            if (node == null)
                return;
            if (!string.IsNullOrEmpty(node.nodeId))
                m_NodeIndex[node.nodeId] = node;
            for (int i = 0; i < node.children.Count; i++)
                BuildIndex(node.children[i]);
        }

        private bool Eval(StateTreeConditionAsset condition)
        {
            return condition == null || condition.Evaluate(context);
        }
    }
}
