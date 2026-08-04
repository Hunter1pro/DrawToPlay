using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Runs another state tree as one task — the composite that lets a tree be AUTHORED by
    /// wiring: save a tree, mark it reusable, and it becomes pickable as a task anywhere else.
    /// A sub-tree is not a new execution model, it is a second <see cref="StateTreeExecutor"/>
    /// on the SAME <see cref="StateTreeContext"/>, so owner, blackboard and the damage/cue
    /// events flow through in both directions with no plumbing.
    ///
    /// RESULT MAPPING: the sub-tree signals completion by ENTERING A STATE, because that is the
    /// only vocabulary an authored tree has. Reaching a state named in
    /// <see cref="successStates"/> ends this task with Success, one in
    /// <see cref="failureStates"/> with Failure, anything else keeps it Running. Matching is
    /// case-insensitive so "Success" and "success" both work. A state that is in neither list
    /// and has no tasks and no transitions (a dead end) keeps this task Running forever — that
    /// is the author's bug, not a runtime one, and it is visible as a stuck highlight in the
    /// editor rather than a silent Success.
    ///
    /// CANCELLED PROPAGATION (the reason this is not just "start another runner"): OnExit stops
    /// the child executor for EVERY status, and the executor exits its own running tasks with
    /// <see cref="StateTreeStatus.Cancelled"/>. An interrupt in the parent therefore cancels all
    /// the way down the composition chain, so a sub-tree's nav goals / timers / spawned VFX tear
    /// down exactly like an inline task's. The deep copy the child made is destroyed at the same
    /// point — nothing survives the state exit.
    ///
    /// RECURSION: two guards, both needed. A static walk of the authored tree graph on entry
    /// catches "this tree runs itself" (directly or through an A→B→A chain) BEFORE anything is
    /// copied, and a depth counter on <c>context.domainContext[<see cref="depthKey"/>]</c>
    /// catches whatever the walk gives up on (it stops after <see cref="maxDepth"/> levels).
    /// Either way the task fails with one error log instead of recursing into a stack overflow.
    /// The walk is O(reachable trees) per state entry, capped by depth, on authoring-scale
    /// graphs — a diamond-shaped composition is walked more than once by design (a path stack,
    /// not a visited set, is what detects cycles).
    ///
    /// PARAMETERS (M7g): a sub-tree's declared parameters
    /// (<see cref="StateTreeAsset.parameters"/>) are the arguments of this call, and
    /// <see cref="overrides"/> is what this particular state passes — parity with
    /// <see cref="RunGraphTask"/>, so one authored "patrol" tree serves three states at three
    /// speeds. There is no argument list to pass them through, because a tree only ever receives
    /// a value one way: <see cref="OnEnter"/> writes each declared name into the SHARED blackboard
    /// before the child machine starts, so the child's very first OnEnter already reads them.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Run Sub Tree", fileName = "RunSubTree")]
    [StateTreeCategory("Tasks/Composite", "Run another state tree as this task")]
    public sealed class RunSubTreeTask : StateTreeTaskAsset
    {
        /// <summary>domainContext key holding the current nesting depth. Underscore-prefixed so
        /// it can never collide with authored domain keys.</summary>
        public const string depthKey = "__subtreeDepth";

        /// <summary>Nesting limit. 32 is far past any sane composition and still cheap to hit
        /// safely — the guard exists for authoring mistakes, not for budgeting.</summary>
        public const int maxDepth = 32;

        /// <summary>Tree to run. Any StateTreeAsset works; the editor picker only OFFERS the
        /// ones marked treeKind "task", which is a curation rule, not a runtime one.</summary>
        public StateTreeAsset subTree;

        /// <summary>Sub-tree state ids that end this task with Success.</summary>
        public List<string> successStates = new List<string> { "success", "exit" };

        /// <summary>Sub-tree state ids that end this task with Failure.</summary>
        public List<string> failureStates = new List<string> { "fail", "failure" };

        /// <summary>This state's values for the sub-tree's declared parameters, one row per
        /// overridden parameter; an unchecked row means "use the tree's declared default", which
        /// keeps a state that overrides one of five parameters from freezing the other four at
        /// whatever they happened to be when it was configured. Rows naming a parameter the tree
        /// no longer declares are KEPT rather than silently dropped (the tree may be mid-edit, and
        /// the inspector shows them as stale); this task ignores them with one warning.</summary>
        public List<GraphTaskParameterOverride> overrides = new List<GraphTaskParameterOverride>();

        /// <summary>Live child machine, or null when the entry aborted. Not serialized, so the
        /// runner's DeepCopy hands every copy a clean one.</summary>
        private StateTreeExecutor m_Executor;

        private bool m_Aborted;
        private bool m_DepthPushed;
        private int m_PreviousDepth;

        /// <summary>One stale-override report per instance. Deliberately NOT reset by
        /// <see cref="OnEnter"/>: seeding runs on every activation, and a state re-entered every
        /// second must not turn one wrong row into a console flood.</summary>
        private bool m_UnknownOverrideLogged;

        public override void OnEnter(StateTreeContext context)
        {
            m_Executor = null;
            m_Aborted = false;
            m_DepthPushed = false;
            m_PreviousDepth = 0;

            if (context == null)
            {
                m_Aborted = true;
                return;
            }
            if (subTree == null)
            {
                Debug.LogError("RunSubTreeTask: no sub tree assigned", this);
                m_Aborted = true;
                return;
            }

            int depth = ReadDepth(context);
            if (depth >= maxDepth)
            {
                Debug.LogError($"RunSubTreeTask: sub tree nesting deeper than {maxDepth} at " +
                    $"'{subTree.name}', aborting", this);
                m_Aborted = true;
                return;
            }
            if (HasSubTreeCycle(subTree))
            {
                Debug.LogError($"RunSubTreeTask: '{subTree.name}' runs itself (directly or " +
                    "through another tree), aborting", this);
                m_Aborted = true;
                return;
            }

            m_PreviousDepth = depth;
            context.domainContext[depthKey] = depth + 1;
            m_DepthPushed = true;

            // Every guard has passed, so this activation really happens: publish the arguments
            // BEFORE the child machine starts, or its entry state's OnEnter would read the
            // previous activation's values (or nothing at all).
            SeedParameters(context);

            m_Executor = new StateTreeExecutor
            {
                data = subTree,
                context = context,
                owner = context.owner,
                logLabel = "RunSubTreeTask '" + subTree.name + "'",
                logContext = context.owner
            };
            m_Executor.StartTree();
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            // A refused entry (no tree, cycle, too deep) or a sub-tree that could not start
            // — the executor already logged why — fails, so the parent state can transition
            // away instead of hanging on a task that will never do anything.
            if (m_Aborted || m_Executor == null || !m_Executor.isRunning)
                return StateTreeStatus.Failure;

            // The entry state itself may already be terminal (a one-state "always succeed"
            // tree), so classify before ticking as well as after. A single TickTree performs
            // at most one transition, so polling the active id after it misses no state.
            StateTreeStatus status = Classify(m_Executor.activeNodeId);
            if (status != StateTreeStatus.Running)
                return status;

            m_Executor.TickTree(deltaTime);
            return Classify(m_Executor.activeNodeId);
        }

        /// <summary>Single teardown path for every status: the parent runner calls OnExit on
        /// completion, on interrupt and on StopTree, so stopping here (rather than the moment a
        /// terminal state is seen) keeps the child's cancel-and-destroy in one place.</summary>
        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (m_Executor != null)
            {
                m_Executor.StopTree();
                m_Executor = null;
            }
            if (m_DepthPushed && context != null)
            {
                if (m_PreviousDepth > 0)
                    context.domainContext[depthKey] = m_PreviousDepth;
                else
                    context.domainContext.Remove(depthKey);
                m_DepthPushed = false;
            }
            m_Aborted = false;
        }

        // ---------------------------------------------------------------- parameters

        /// <summary>
        /// Writes this activation's argument values into the SHARED blackboard: every parameter
        /// the sub-tree declares, at this state's enabled override or at the tree's declared
        /// default. Called once per activation, after the entry guards and before the child
        /// machine starts.
        ///
        /// RE-SEEDING EVERY ACTIVATION is the point, not an accident. A state re-entered after
        /// its sub-tree spent the last run counting a key down must start from the value the
        /// author configured, not from wherever the previous run left it — the arguments of a
        /// call do not persist across calls.
        ///
        /// THE DECLARATION IS READ FROM THE AUTHORED ASSET (<c>subTree.parameters</c>), not from
        /// a copy: <see cref="StateTreeExecutor.StartTree"/> deep-copies the tree for itself, and
        /// this reference survives the runner's own DeepCopy pointing at the same authored asset,
        /// so both ends agree on what is declared however many copies are in between.
        ///
        /// NO SAVE/RESTORE of whatever the key held before (v1): the declaration IS the tree's
        /// claim on that key, so two sub-trees declaring one name are sharing it on purpose.
        /// Scoped keys are a v2 question, and one this can grow into without changing the
        /// authored data.
        /// </summary>
        private void SeedParameters(StateTreeContext context)
        {
            List<GraphTaskParameter> declared = subTree.parameters;
            if (declared != null)
            {
                for (int i = 0; i < declared.Count; i++)
                {
                    GraphTaskParameter parameter = declared[i];
                    // A nameless row is an inspector row mid-edit, not a key.
                    if (parameter == null || string.IsNullOrEmpty(parameter.name))
                        continue;
                    context.blackboard[parameter.name] = EffectiveValue(parameter);
                }
            }
            WarnStaleOverrides(declared);
        }

        /// <summary>
        /// The value one parameter is seeded with, BOXED AS THE TYPE THE CONSUMERS ALREADY READ.
        /// The blackboard is <c>Dictionary&lt;string, object&gt;</c>, so the boxed type is a real
        /// contract and picking it by taste breaks readers silently:
        /// <list type="bullet">
        /// <item>Float =&gt; <c>float</c>. What the only other library writer boxes
        /// (SetBlackboardTask.cs:65, StateTreeLibraryUtil.cs:184) and what every reader
        /// accepts.</item>
        /// <item>String =&gt; <c>string</c> (SetBlackboardTask.cs:68); null is normalised to
        /// empty, matching how a graph parameter reads (GraphTaskAsset.cs:450-452).</item>
        /// <item>Bool =&gt; <c>float</c> 1/0, NOT a boxed <c>bool</c>. This is the one that has to
        /// be argued: <c>GraphTaskAsset.ReadBlackboardFloat</c> would accept either
        /// (GraphTaskAsset.cs:946-949 reads bool as 1/0), but
        /// <c>StateTreeLibraryUtil.TryGetFloat</c> accepts float/int/double and NOTHING else
        /// (StateTreeLibraryUtil.cs:164-177), and that is the path
        /// <see cref="BlackboardCompareCondition"/> reads through
        /// (BlackboardCompareCondition.cs:45). A boxed bool would therefore make every transition
        /// gated on a declared Bool parameter read false forever, with no diagnostic. A float 1/0
        /// is read correctly by BOTH, it is what a Bool parameter already IS in this model
        /// (GraphTaskAsset.cs:134-135, and GetParamBool/GetParamFloat resolve identically at
        /// GraphTaskAsset.cs:845-849), and <c>HasBlackboardKey</c> only asks whether the key
        /// exists (GraphTaskAsset.cs:852-854), so it is indifferent. No library reader today
        /// boxes or expects a <c>bool</c> on the blackboard — the test stubs' own flags are the
        /// only ones, and they are not parameters.</item>
        /// </list>
        /// </summary>
        private object EffectiveValue(GraphTaskParameter parameter)
        {
            GraphTaskParameterOverride row = EnabledOverride(parameter.name);
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                {
                    string text = row != null ? row.stringValue : parameter.stringValue;
                    return text ?? string.Empty;
                }
                case GraphTaskParameterKind.Bool:
                {
                    float number = row != null ? row.floatValue : parameter.floatValue;
                    return number != 0f ? 1f : 0f;
                }
                default:
                    return row != null ? row.floatValue : parameter.floatValue;
            }
        }

        /// <summary>The enabled override row for a parameter, or null for "use the declared
        /// default". The LAST matching row wins, which is what a name-keyed dictionary of the rows
        /// would do (GraphTaskAsset.cs:364-385) — duplicates are an inspector accident, and the
        /// two must not disagree about which one the author sees applied. Ordinal comparison, for
        /// the same reason: parameter names are keys, not prose.</summary>
        private GraphTaskParameterOverride EnabledOverride(string parameterName)
        {
            if (overrides == null)
                return null;
            GraphTaskParameterOverride found = null;
            for (int i = 0; i < overrides.Count; i++)
            {
                GraphTaskParameterOverride row = overrides[i];
                if (row != null && row.enabled
                    && string.Equals(row.name, parameterName, StringComparison.Ordinal))
                    found = row;
            }
            return found;
        }

        /// <summary>A tree gets re-authored long after the states that call it were configured, so
        /// an override naming a renamed or deleted parameter is normal wear rather than a fault:
        /// the row is skipped, everything else still applies, and it is a WARNING said once per
        /// instance — the same rule <c>RunGraphTask</c>'s overrides follow
        /// (GraphTaskAsset.cs:387-391).</summary>
        private void WarnStaleOverrides(List<GraphTaskParameter> declared)
        {
            if (overrides == null || m_UnknownOverrideLogged)
                return;

            string unknown = null;
            for (int i = 0; i < overrides.Count; i++)
            {
                GraphTaskParameterOverride row = overrides[i];
                if (row == null || !row.enabled || string.IsNullOrEmpty(row.name)
                    || IsDeclared(declared, row.name))
                    continue;
                unknown = unknown == null
                    ? "'" + row.name + "'"
                    : unknown + ", '" + row.name + "'";
            }

            if (unknown == null)
                return;
            m_UnknownOverrideLogged = true;
            Debug.LogWarning($"RunSubTreeTask: override for {unknown} — sub tree " +
                $"'{subTree.name}' declares no such parameter (renamed or removed?). The row is " +
                "ignored.", this);
        }

        private static bool IsDeclared(List<GraphTaskParameter> declared, string parameterName)
        {
            if (declared == null)
                return false;
            for (int i = 0; i < declared.Count; i++)
            {
                GraphTaskParameter parameter = declared[i];
                if (parameter != null
                    && string.Equals(parameter.name, parameterName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private StateTreeStatus Classify(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return StateTreeStatus.Running;
            if (ListContains(successStates, nodeId))
                return StateTreeStatus.Success;
            if (ListContains(failureStates, nodeId))
                return StateTreeStatus.Failure;
            return StateTreeStatus.Running;
        }

        private static bool ListContains(List<string> ids, string nodeId)
        {
            if (ids == null)
                return false;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!string.IsNullOrEmpty(ids[i])
                    && string.Equals(ids[i], nodeId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int ReadDepth(StateTreeContext context)
        {
            object value;
            if (context.domainContext.TryGetValue(depthKey, out value) && value is int)
                return (int)value;
            return 0;
        }

        /// <summary>True when running <paramref name="tree"/> would re-enter a tree that is
        /// already on the composition path. Walks the AUTHORED assets (a running task is a deep
        /// copy, but its subTree reference points at the authored asset, so the graph is the
        /// same either way).</summary>
        private static bool HasSubTreeCycle(StateTreeAsset tree)
        {
            return VisitTree(tree, new List<StateTreeAsset>());
        }

        private static bool VisitTree(StateTreeAsset tree, List<StateTreeAsset> path)
        {
            if (tree == null)
                return false;
            for (int i = 0; i < path.Count; i++)
            {
                if (ReferenceEquals(path[i], tree))
                    return true;
            }
            // Give up rather than walk forever: the runtime depth counter is the backstop for
            // an acyclic chain this long, and it reports the depth honestly.
            if (path.Count >= maxDepth)
                return false;

            path.Add(tree);
            bool cycle = VisitNode(tree.root, path, 0);
            path.RemoveAt(path.Count - 1);
            return cycle;
        }

        private static bool VisitNode(StateTreeNodeAsset node, List<StateTreeAsset> path, int depth)
        {
            // 256 matches the authored-children guard in StateTreeAsset.DeepCopyNode.
            if (node == null || depth > 256)
                return false;
            for (int i = 0; i < node.tasks.Count; i++)
            {
                var nested = node.tasks[i] as RunSubTreeTask;
                if (nested != null && VisitTree(nested.subTree, path))
                    return true;
            }
            for (int i = 0; i < node.children.Count; i++)
            {
                if (VisitNode(node.children[i], path, depth + 1))
                    return true;
            }
            return false;
        }
    }
}
