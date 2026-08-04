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
    /// speeds. This task's job is to decide the VALUES; publishing them is the child machine's
    /// (<see cref="StateTreeExecutor.parameterOverrides"/>), which is why the seeding this file used
    /// to do lives there since M7i: an executor establishes the parameters of every tree it runs the
    /// same way, whether that tree was reached as a sub-tree or started by a runner, and it is also
    /// what writes them into bound fields — something a caller has no access to.
    ///
    /// PARAMETER IDENTITY (M7h): an override binds to a declaration by
    /// <see cref="GraphTaskParameter.id"/>, so renaming a parameter in the sub-tree keeps every
    /// caller's tuning — while the NAME stays the blackboard key the child reads, because the
    /// child reads it by hand-typed string and always did.
    ///
    /// PASS-THROUGH (M7i): a row may take its value from a parameter of the tree THIS STATE lives in
    /// (<see cref="GraphTaskParameterOverride.sourceParameterId"/>) instead of a literal, which is
    /// what lets a value declared at the top of a composition reach the bottom of it. Resolved
    /// against the caller's parameter scope BEFORE the child machine starts and replaces that scope
    /// with its own — the one ordering constraint the feature has.
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
        /// overridden parameter, each bound to a declaration by
        /// <see cref="GraphTaskParameter.id"/>; an unchecked row means "use the tree's declared
        /// default", which keeps a state that overrides one of five parameters from freezing the
        /// other four at whatever they happened to be when it was configured. Rows whose
        /// declaration no longer exists are KEPT rather than silently dropped (the tree may be
        /// mid-edit, and the inspector shows them as stale); this task ignores them with one
        /// warning.</summary>
        public List<GraphTaskParameterOverride> overrides = new List<GraphTaskParameterOverride>();

        /// <summary>Live child machine, or null when the entry aborted. Not serialized, so the
        /// runner's DeepCopy hands every copy a clean one.</summary>
        private StateTreeExecutor m_Executor;

        private bool m_Aborted;
        private bool m_DepthPushed;
        private int m_PreviousDepth;

        /// <summary>One stale-override report per instance. Deliberately NOT reset by
        /// <see cref="OnEnter"/>: the rows are re-read on every activation, and a state re-entered
        /// every second must not turn one wrong row into a console flood.</summary>
        private bool m_UnknownOverrideLogged;

        /// <summary>The same rule for the other way a row can be stale — its pass-through SOURCE is
        /// gone or has changed kind. A separate flag from
        /// <see cref="m_UnknownOverrideLogged"/> because they are separate mistakes with separate
        /// fixes, and one must not silence the other.</summary>
        private bool m_UnresolvedSourceLogged;

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

            // Every guard has passed, so this activation really happens: settle the arguments while
            // the CALLER's parameter scope is still the current one — StartTree replaces it with the
            // child's, and a pass-through row read after that would read the callee's own values.
            m_Executor = new StateTreeExecutor
            {
                data = subTree,
                context = context,
                owner = context.owner,
                logLabel = "RunSubTreeTask '" + subTree.name + "'",
                logContext = context.owner,
                parameterOverrides = ResolvedOverrides(context)
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
        /// This activation's argument values, as the list the child machine is handed. Two things
        /// happen here and nothing else does: PASS-THROUGH rows take their value from the caller's
        /// parameter scope (read now, while that scope is still the current one), and the rows that
        /// are bound to nothing are reported.
        ///
        /// SEEDING IS NOT HERE ANY MORE (M7i). It is <see cref="StateTreeExecutor"/>'s, because a
        /// tree's parameters have to be established for every tree that runs — a root tree included
        /// — and because the executor is also what writes them into bound fields, which a caller
        /// cannot reach. The observable semantics are unchanged: the executor seeds inside
        /// <see cref="StateTreeExecutor.StartTree"/>, before the entry state's tasks are entered, so
        /// the child's very first OnEnter still reads this state's values; and a new executor per
        /// activation is what keeps re-entry re-seeding rather than inheriting.
        ///
        /// THE DECLARATION IS READ FROM THE AUTHORED ASSET (<c>subTree.parameters</c>), not from a
        /// copy: the executor deep-copies the tree for itself, and this reference survives the
        /// runner's own DeepCopy pointing at the same authored asset, so both ends agree on what is
        /// declared however many copies are in between — identities included, which is what makes
        /// resolving a row here and applying it there the same operation.
        /// </summary>
        private List<GraphTaskParameterOverride> ResolvedOverrides(StateTreeContext context)
        {
            List<GraphTaskParameter> declared = subTree.parameters;
            string unresolved;
            List<GraphTaskParameterOverride> rows = StateTreeExecutor.ResolveSourceValues(
                context, overrides, declared, out unresolved);

            WarnUnresolvedSources(unresolved);
            WarnStaleOverrides(declared);
            return rows;
        }

        /// <summary>A pass-through row whose source parameter is gone from the calling tree, or has
        /// been retyped since, is the same kind of wear as a row bound to a deleted declaration: the
        /// row is skipped so the sub-tree's own default stands, everything else still applies, and it
        /// is said once per instance rather than once per activation.</summary>
        private void WarnUnresolvedSources(string unresolved)
        {
            if (unresolved == null || m_UnresolvedSourceLogged)
                return;
            m_UnresolvedSourceLogged = true;
            Debug.LogWarning($"RunSubTreeTask: override for {unresolved} reads a parameter of the " +
                "calling tree that no longer exists or no longer has the same kind. The row is " +
                $"ignored and '{subTree.name}' uses its declared default.", this);
        }

        /// <summary>A tree gets re-authored long after the states that call it were configured, so
        /// an override whose declaration was deleted — or one that was never bound to a declaration
        /// at all — is normal wear rather than a fault: the row is skipped, everything else still
        /// applies, and it is a WARNING said once per instance, the same rule <c>RunGraphTask</c>'s
        /// overrides follow (GraphTaskAsset.cs, ApplyOverrides). Staleness is decided by the SAME
        /// matching rule that applies the live rows, so a row can never be both ignored and
        /// unreported.</summary>
        private void WarnStaleOverrides(List<GraphTaskParameter> declared)
        {
            if (overrides == null || m_UnknownOverrideLogged)
                return;

            string unknown = null;
            for (int i = 0; i < overrides.Count; i++)
            {
                GraphTaskParameterOverride row = overrides[i];
                if (row == null || !row.enabled || row.Declaration(declared) != null)
                    continue;
                unknown = unknown == null ? row.Describe(i) : unknown + ", " + row.Describe(i);
            }

            if (unknown == null)
                return;
            m_UnknownOverrideLogged = true;
            Debug.LogWarning($"RunSubTreeTask: override for {unknown} — sub tree " +
                $"'{subTree.name}' declares no parameter with that id (deleted, or the row was " +
                "never bound). The row is ignored.", this);
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
