using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The state-tree machine itself — everything <see cref="StateTreeRunner"/> used to do minus
    /// the MonoBehaviour. Deep-copies the tree on <see cref="StartTree"/> so each executor owns
    /// its task instances, releases that copy on <see cref="StopTree"/>, and ticks in the
    /// verbatim order of state_tree_runner.gd (brief §7.1): interrupts (checkWhileRunning) →
    /// task ticks → on-completion transitions when every task is done. Any pre-emption exits
    /// running tasks with <see cref="StateTreeStatus.Cancelled"/>.
    ///
    /// WHY A PLAIN CLASS: a tree must also be runnable as a TASK inside another tree
    /// (<see cref="RunSubTreeTask"/>) — nested, several per entity, created and destroyed as
    /// states come and go. A MonoBehaviour cannot be nested like that (one component per
    /// GameObject per state would be absurd), so the machine lives here and the component
    /// became a thin wrapper. The semantics are byte-for-byte the ones the M6 EditMode tests
    /// pin down; this file is a MOVE, not a rewrite.
    ///
    /// LOGGING: <see cref="logLabel"/> prefixes the two error messages so a nested executor can
    /// say who it is, while the runner keeps emitting the exact strings its tests expect.
    /// </summary>
    public sealed class StateTreeExecutor
    {
        /// <summary>Authored tree to run. Never mutated — <see cref="StartTree"/> runs a deep
        /// copy of it.</summary>
        public StateTreeAsset data;

        /// <summary>Context handed to every task. Assign one to SHARE state (that is how a
        /// sub-tree sees its parent's blackboard); leave null and <see cref="StartTree"/>
        /// creates one around <see cref="owner"/>.</summary>
        public StateTreeContext context;

        /// <summary>Owner the context wraps when this executor has to create one, and the
        /// fallback owner for a context that was handed over without one.</summary>
        public GameObject owner;

        /// <summary>Prefix of this executor's error messages. Defaults to the runner's name so
        /// the component wrapper needs no special casing.</summary>
        public string logLabel = "StateTreeRunner";

        /// <summary>Object Unity pings when an error of this executor is clicked in the
        /// console (the runner passes itself, a sub-tree passes the owner).</summary>
        public UnityEngine.Object logContext;

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

        /// <summary>The live deep copy, or null while stopped. Diagnostics only — the copy's
        /// nodes are NOT the authored ones, so anything matching against an authored asset must
        /// match by nodeId string (the play-mode highlight rule).</summary>
        public StateTreeAsset activeData => m_ActiveData;

        public void StartTree()
        {
            if (data == null || data.root == null)
            {
                Debug.LogError(logLabel + ": data or root is null", logContext);
                return;
            }
            m_ActiveData = data.DeepCopy();
            m_NodeIndex.Clear();
            BuildIndex(m_ActiveData.root);

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

        /// <summary>One tick — the _process body, callable headless.</summary>
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
                Debug.LogError($"{logLabel}: unknown transition target '{targetId}'", logContext);
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
