using System;
using System.Collections.Generic;
using System.Reflection;
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
    ///
    /// PARAMETERS (M7i, refactor of M7g): the machine that runs a tree is also what ESTABLISHES the
    /// tree's parameters for that run — it seeds the declared names into the blackboard, publishes
    /// the effective values as a scope its states can pass on, and writes them into the bound fields
    /// of the copy it owns. That work used to live in <see cref="RunSubTreeTask"/>, which could only
    /// ever do the first third of it and only for a tree reached as a sub-tree; here it happens for
    /// every tree the same way, once, in the one place that already owns the deep copy and knows
    /// when a run begins and ends. The caller's contribution is <see cref="parameterOverrides"/>.
    ///
    /// OUTPUTS (M7j) are the return flow, and they close the loop the parameters opened. A task that
    /// finishes may hand back named values; this machine captures them at the moment it finishes,
    /// keeps them as the state's exit record, and lets the TRANSITION that ends the state decide
    /// where each one is written. It has to be here for the same reason the parameters are: only the
    /// machine sees the completion, holds the copy the values live on, and knows which transition
    /// fired.
    ///
    /// ENTRY-TIME BINDINGS (M7k) are the last stretch of that loop. A routed output is on the
    /// blackboard by the time the next state is entered, but only a graph or a condition could read
    /// it there; a binding row sourced from a KEY rather than from a parameter carries it the rest of
    /// the way, into the field of a plain C# task, in <see cref="EnterNode"/> and before that task's
    /// OnEnter. Parameters are still bound once at <see cref="StartTree"/>, because an argument does
    /// not change during a call — the two sources differ in WHEN, and that is the whole difference.
    /// </summary>
    public sealed class StateTreeExecutor
    {
        /// <summary>domainContext key holding the <c>id → effective parameter</c> dictionary of the
        /// tree currently running on this context — what a state's override row reads when it passes
        /// a value THROUGH to a sub-tree or a graph. Underscore-prefixed so it can never collide
        /// with authored domain keys, and saved/restored around a run exactly like
        /// <see cref="RunSubTreeTask.depthKey"/>, so a nested tree's scope hides its caller's for
        /// precisely as long as it runs.</summary>
        public const string paramScopeKey = "__paramScope";

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

        /// <summary>
        /// The CALLER's values for this tree's declared parameters — the arguments of the call —
        /// set before <see cref="StartTree"/> and read only there. Null (the root runner's case)
        /// means "the tree's declared defaults", which is what a tree started by nobody in
        /// particular must run at.
        ///
        /// Rows bind to declarations by <see cref="GraphTaskParameter.id"/> through
        /// <see cref="GraphTaskParameterOverride.EnabledFor"/> — the M7h rule, shared with
        /// <see cref="GraphTaskAsset.ApplyOverrides"/> so the two appliers cannot disagree about
        /// which row is live. A row that binds to nothing is simply never consulted here: the WARNING
        /// belongs to whoever owns the serialized rows (<see cref="RunSubTreeTask"/>), because only
        /// it lives long enough to say it once instead of once per activation.
        /// </summary>
        public List<GraphTaskParameterOverride> parameterOverrides;

        public event Action treeStarted;
        public event Action treeStopped;

        /// <summary>The tree ran off its END (M22): the root's last state completed and nothing
        /// declared or implicit handled it. Fired BEFORE the teardown that follows, so a
        /// listener still sees the final blackboard — a finished tree is a returned call, and
        /// this is where its caller reads the result. StopTree alone never fires this: being
        /// stopped is being cancelled, not being done.</summary>
        public event Action treeFinished;

        public event Action<string> nodeEntered;
        public event Action<string> nodeLeft;
        public event Action<string, string> activeNodeChanged;

        private StateTreeAsset m_ActiveData;
        private readonly Dictionary<string, StateTreeNodeAsset> m_NodeIndex =
            new Dictionary<string, StateTreeNodeAsset>();

        /// <summary>Child → parent over the live copy (M22) — what lets a completed last
        /// sibling bubble. Built beside <see cref="m_NodeIndex"/>; by object identity, because
        /// the implicit flow walks the copy's real structure, not ids (a grouping node has
        /// no id worth trusting).</summary>
        private readonly Dictionary<StateTreeNodeAsset, StateTreeNodeAsset> m_ParentIndex =
            new Dictionary<StateTreeNodeAsset, StateTreeNodeAsset>();

        /// <summary>Whether any task of the current activation finished (M22) — the fact
        /// <see cref="StateTreeCompleteWhen.AnyTask"/> gates on. Reset by EnterNode.</summary>
        private bool m_AnyTaskFinished;
        private StateTreeNodeAsset m_CurrentNode;
        private readonly List<StateTreeTaskAsset> m_RunningTasks = new List<StateTreeTaskAsset>();
        private readonly List<StateTreeTaskAsset> m_Finished = new List<StateTreeTaskAsset>();

        /// <summary>Position in the CURRENT NODE's <c>tasks</c> list of each entry in
        /// <see cref="m_RunningTasks"/>, kept in step with it. The two are not the same index:
        /// <see cref="EnterNode"/> skips holes in the authored list, so the third running task can be
        /// the fourth authored one — and a route names the AUTHORED position, because that is what
        /// the inspector shows and what the Ops layer remaps.</summary>
        private readonly List<int> m_RunningTaskIndices = new List<int>();

        /// <summary>What the tasks of the current state RETURNED, by their authored index (M7j) —
        /// the state's exit record, read by the transition that ends it. Only tasks that FINISHED
        /// (returned Success or Failure) appear: a cancelled task returns nothing, the same way an
        /// aborted function does. Cleared by <see cref="EnterNode"/>, so a re-entered state routes
        /// what it produced this time.</summary>
        private readonly Dictionary<int, List<TaskOutputValue>> m_TaskOutputs =
            new Dictionary<int, List<TaskOutputValue>>();

        /// <summary>Scratch the capture collects into before it is known whether there is anything
        /// worth keeping — most tasks return nothing, and allocating a list per finished task to
        /// discover that would be a per-tick allocation on the busiest path in the machine.</summary>
        private readonly List<TaskOutputValue> m_CaptureScratch = new List<TaskOutputValue>();

        /// <summary>Route rows already reported this run, or null until one is. A state pair that
        /// ping-pongs must not turn one wrong row into a console flood, and keying on the ROW (the
        /// copy's own object) rather than on a flag keeps every OTHER broken row still able to say
        /// so once.</summary>
        private HashSet<TransitionOutputRoute> m_WarnedRoutes;

        /// <summary>Blackboard-sourced binding rows already reported this run, or null until one is —
        /// the same once-per-row discipline as <see cref="m_WarnedRoutes"/> and needed more acutely,
        /// because these rows are re-applied on EVERY entry: a state re-entered sixty times a second
        /// would otherwise report one stale field name sixty times a second. Keyed on the row object
        /// (the copy's own, so it dies with the run) rather than on a flag, so one broken row never
        /// silences the next.</summary>
        private HashSet<StateTreeFieldBinding> m_WarnedBindings;

        /// <summary>This run's effective parameters, keyed by identity — the value published as the
        /// scope and the value the bindings write. Null while stopped.</summary>
        private Dictionary<string, GraphTaskParameter> m_ParamScope;

        private bool m_ScopePushed;
        private bool m_HadPreviousScope;
        private object m_PreviousScope;

        /// <summary>Public instance fields per target type, resolved once. A binding row is applied
        /// once per tree start, but a tree started every second by a re-entered state would
        /// otherwise re-walk the same type's fields every time. Static because the answer is a
        /// property of the TYPE, not of a run.</summary>
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> s_FieldsByType =
            new Dictionary<Type, Dictionary<string, FieldInfo>>();

        /// <summary>The <c>[TaskOutput]</c> fields of a task type, resolved once (the M7i cache
        /// pattern, for the same reason and with the same lifetime). An EMPTY array is cached too and
        /// is the answer for almost every type: the capture path runs for every task that finishes,
        /// so "this type returns nothing" has to be a dictionary hit rather than a reflection
        /// walk.</summary>
        private static readonly Dictionary<Type, FieldInfo[]> s_OutputFieldsByType =
            new Dictionary<Type, FieldInfo[]>();

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
            m_ParentIndex.Clear();
            BuildIndex(m_ActiveData.root);

            if (context == null)
                context = new StateTreeContext(owner);
            else if (context.owner == null)
                context.owner = owner;

            // The parameters are established BEFORE anything of the tree runs — before the entry
            // state's tasks are entered and before the treeStarted listeners can look at the
            // blackboard — because that is what makes them arguments rather than something the tree
            // observes changing under it.
            BuildParameterScope();
            PushParameterScope();
            // Declared keys resolve BEFORE bindings: a binding's blackboardKey may itself be
            // a linked field, and it must hold the declaration's CURRENT name by the time the
            // binding machinery reads it. This rewrite is what makes renaming a declared key
            // free — every wired string on the DEEP COPY is refreshed from the id here.
            ResolveKeyFields(m_ActiveData.root, 0);
            ResolveEntryRefs(m_ActiveData.root, 0);
            ApplyBindings(m_ActiveData.root, 0);

            treeStarted?.Invoke();
            var entry = ResolveEntryNode(m_ActiveData.root);
            EnterNode(entry);
            activeNodeChanged?.Invoke("", entry.nodeId);
        }

        public void StopTree()
        {
            if (m_CurrentNode == null)
            {
                // A run that aborted before entering a node can still have pushed a scope; popping
                // it here (rather than only on the normal path) is what keeps a caller's scope from
                // being buried by a tree that never started.
                PopParameterScope();
                return;
            }
            nodeLeft?.Invoke(m_CurrentNode.nodeId);
            ExitRunningTasks(StateTreeStatus.Cancelled);
            m_CurrentNode = null;
            // After the tasks exited, not before: a task's OnExit is still part of this tree's run,
            // and anything it starts must see this tree's parameters rather than its caller's.
            PopParameterScope();
            treeStopped?.Invoke();
            // Release the deep copy (finding B, M6 test agent): Instantiate copies live
            // until domain reload otherwise, leaking a tree per restart.
            StateTreeAsset.DestroyCopy(m_ActiveData);
            m_ActiveData = null;
            m_NodeIndex.Clear();
            m_ParentIndex.Clear();
            // The exit record and the route diagnostics both belong to the copy that has just been
            // destroyed — holding either would be holding values nothing can route and warnings
            // keyed on rows that no longer exist.
            m_TaskOutputs.Clear();
            m_WarnedRoutes = null;
            m_WarnedBindings = null;
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
                    TransitionTo(tr);
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
                    // CAPTURE BEFORE OnExit, and the order is load-bearing rather than tidy: a
                    // wrapper's OnExit tears down the thing the values live on (RunGraphTask
                    // destroys the graph instance it just forwarded from), so a capture moved one
                    // statement down would return nothing for every graph task, silently. It is also
                    // the right SEMANTICS on its own — the return value is fixed at the return, and
                    // OnExit is the teardown that runs afterwards.
                    // Read defensively: the index list is kept in step with the running list, but a
                    // task that reached back into its own executor from OnTick could have emptied
                    // both, and a capture is not worth an IndexOutOfRange over. -1 captures nothing.
                    CaptureOutputs(i < m_RunningTaskIndices.Count ? m_RunningTaskIndices[i] : -1,
                        task, context);
                    task.OnExit(context, status);
                    m_Finished.Add(task);
                }
            }
            if (m_Finished.Count > 0)
                m_AnyTaskFinished = true;
            for (int i = 0; i < m_Finished.Count; i++)
                RemoveRunning(m_Finished[i]);

            // A task may STOP THIS EXECUTOR from inside its own tick — a despawn deactivating
            // the host, a travel tearing the level down around it. Everything below belongs to
            // the run that no longer exists (the local transitions list included: it is the
            // destroyed copy's). The pre-M22 code survived this by accident; the completion
            // gate reads the current node and must not.
            if (m_CurrentNode == null)
                return;

            // 3. Completion — when the state's policy says it is done (M22): declared
            // on-completion transitions first, exactly as before; when none fires, the
            // implicit flow (next sibling / bubble) unless the state Holds.
            if (IsComplete(m_CurrentNode))
            {
                for (int i = 0; i < transitions.Count; i++)
                {
                    var tr = transitions[i];
                    if (tr == null || tr.checkWhileRunning)
                        continue;
                    if (Eval(tr.condition))
                    {
                        TransitionTo(tr);
                        return;
                    }
                }

                if (m_CurrentNode.completionFlow == StateTreeCompletionFlow.NextSibling)
                    ImplicitAdvance();
            }
        }

        /// <summary>The M22 completion gate — <see cref="StateTreeCompleteWhen"/> applied to
        /// what is still running. A state with no tasks is complete under both task policies:
        /// an empty state that never completed would be a trap wearing a default.</summary>
        private bool IsComplete(StateTreeNodeAsset node)
        {
            switch (node.completeWhen)
            {
                case StateTreeCompleteWhen.Never:
                    return false;
                case StateTreeCompleteWhen.AnyTask:
                    return m_AnyTaskFinished || m_RunningTasks.Count == 0;
                default:
                    return !AnyBlockingRunning();
            }
        }

        /// <summary>Whether any still-running task HOLDS completion open — the per-task
        /// blocking flag's only consumer. A linear scan, because a state has a handful of
        /// tasks and this runs at most once per tick.</summary>
        private bool AnyBlockingRunning()
        {
            for (int i = 0; i < m_RunningTasks.Count; i++)
            {
                if (m_RunningTasks[i].blocking)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The IMPLICIT flow of a completed state none of whose declared transitions fired
        /// (M22, brief §10.1): the next sibling; a last sibling hands completion to its
        /// parent, whose own on-completion transitions get a chance before ITS next sibling;
        /// past the root, the tree is finished. A Hold ancestor stops the bubble — the tree
        /// stays where it is, complete and waiting for a declared edge or an interrupt.
        ///
        /// One implicit hop per tick, like every explicit transition: a chain of instantly
        /// complete states advances one state per tick rather than unwinding in a single
        /// frame, which keeps enter/exit observable and makes an accidental cycle survivable.
        /// </summary>
        private void ImplicitAdvance()
        {
            StateTreeNodeAsset node = m_CurrentNode;
            int guard = 0;
            while (node != null && guard++ < 256)
            {
                if (!m_ParentIndex.TryGetValue(node, out StateTreeNodeAsset parent)
                    || parent == null)
                {
                    // Completion reached the top: the run is done, and DONE is a result, not
                    // a stop — see treeFinished.
                    FinishTree();
                    return;
                }

                int slot = parent.children.IndexOf(node);
                if (slot >= 0 && slot + 1 < parent.children.Count)
                {
                    StateTreeNodeAsset next = parent.children[slot + 1];
                    if (next != null)
                    {
                        AdvanceTo(next);
                        return;
                    }
                }

                // Last sibling: the PARENT is now the completed thing. Its declared
                // on-completion transitions fire from here — a parent edge is how a whole
                // sequence says where it goes when it ends.
                for (int i = 0; i < parent.transitions.Count; i++)
                {
                    var tr = parent.transitions[i];
                    if (tr == null || tr.checkWhileRunning)
                        continue;
                    if (Eval(tr.condition))
                    {
                        TransitionTo(tr);
                        return;
                    }
                }

                if (parent.completionFlow == StateTreeCompletionFlow.Hold)
                    return;

                node = parent;
            }
        }

        /// <summary>An implicit move to a sibling — <see cref="TransitionTo"/> minus the parts
        /// only a declared transition has (a target id to resolve, outputs to route). The
        /// finished tasks' returns still exist on the exit record; an implicit hop simply
        /// assigns none of them, the same as a routeless transition.</summary>
        private void AdvanceTo(StateTreeNodeAsset target)
        {
            string previousId = m_CurrentNode != null ? m_CurrentNode.nodeId : "";
            target = ResolveEntryNode(target);
            nodeLeft?.Invoke(previousId);
            ExitRunningTasks(StateTreeStatus.Cancelled);
            EnterNode(target);
            activeNodeChanged?.Invoke(previousId, target.nodeId);
        }

        /// <summary>The run completed by running off its end (M22). Finished is announced
        /// BEFORE the teardown so listeners read the final blackboard; then the ordinary stop
        /// path releases the copy — a finished tree and a stopped tree end in the same clean
        /// state, they just mean different things on the way there.</summary>
        private void FinishTree()
        {
            treeFinished?.Invoke();
            StopTree();
        }

        private void EnterNode(StateTreeNodeAsset node)
        {
            m_CurrentNode = node;
            m_RunningTasks.Clear();
            m_RunningTaskIndices.Clear();
            // AnyTask means "of THIS activation" — a fact carried over from the last state
            // would complete a freshly entered one on its first tick.
            m_AnyTaskFinished = false;
            // The exit record belongs to ONE activation of the state: whatever the last one returned
            // is not what this one is returning, and a route firing on stale values would be worse
            // than one firing on none.
            m_TaskOutputs.Clear();
            // BEFORE the OnEnter loop, and that is the whole contract of a blackboard-sourced
            // binding: a task that acts in OnEnter — which is most of them — must act on the value
            // the transition just routed, not on the one the previous activation left in the field.
            // It is also after RouteOutputs, which TransitionTo ran on the way here, so the keys this
            // reads are the ones the fired transition wrote.
            ApplyEntryBindings(node);
            for (int i = 0; i < node.tasks.Count; i++)
            {
                var task = node.tasks[i];
                if (task == null)
                    continue;
                task.OnEnter(context);
                m_RunningTasks.Add(task);
                m_RunningTaskIndices.Add(i);
            }
            nodeEntered?.Invoke(node.nodeId);
        }

        /// <summary>The fired transition, taken. It is the TRANSITION rather than the target id
        /// because a transition carries more than a destination since M7j — it also says what to do
        /// with the returns of the tasks it is ending.</summary>
        /// <summary>
        /// An EXTERNALLY requested transition (the §4c flows runner): move to a node by id
        /// exactly as a fired interrupt would — outputs skipped (there is no authored
        /// transition to route), running tasks Cancelled, listeners told. False when the
        /// run is down or the id is unknown; the caller decides whether that is loud.
        /// </summary>
        public bool RequestTransition(string targetNodeId)
        {
            if (!isRunning || string.IsNullOrEmpty(targetNodeId)
                || !m_NodeIndex.TryGetValue(targetNodeId, out var target))
                return false;
            string previousId = m_CurrentNode != null ? m_CurrentNode.nodeId : "";
            target = ResolveEntryNode(target);
            nodeLeft?.Invoke(previousId);
            ExitRunningTasks(StateTreeStatus.Cancelled);
            EnterNode(target);
            activeNodeChanged?.Invoke(previousId, target.nodeId);
            return true;
        }

        private void TransitionTo(StateTreeTransition transition)
        {
            string targetId = transition != null ? transition.targetNodeId : "";
            if (string.IsNullOrEmpty(targetId) || !m_NodeIndex.TryGetValue(targetId, out var target))
            {
                Debug.LogError($"{logLabel}: unknown transition target '{targetId}'", logContext);
                return;
            }
            string previousId = m_CurrentNode != null ? m_CurrentNode.nodeId : "";
            target = ResolveEntryNode(target);
            // Routed BEFORE anything observes the state change: a nodeLeft listener, the tasks being
            // cancelled and the next state's first OnEnter all read a blackboard that already carries
            // the returns, which is the whole point of routing them here rather than after.
            RouteOutputs(transition);
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
            m_RunningTaskIndices.Clear();
        }

        /// <summary>Drops one finished task from the running set, keeping
        /// <see cref="m_RunningTaskIndices"/> in step. By identity, like the
        /// <c>List.Remove</c> it replaces: the deep copy gives every authored entry its own object,
        /// so one task asset used twice in a state is still two instances.</summary>
        private void RemoveRunning(StateTreeTaskAsset task)
        {
            int slot = m_RunningTasks.IndexOf(task);
            if (slot < 0)
                return;
            m_RunningTasks.RemoveAt(slot);
            m_RunningTaskIndices.RemoveAt(slot);
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
            {
                if (node.children[i] != null)
                    m_ParentIndex[node.children[i]] = node;
                BuildIndex(node.children[i]);
            }
        }

        private bool Eval(StateTreeConditionAsset condition)
        {
            return condition == null || condition.Evaluate(context);
        }

        // ---------------------------------------------------------------- parameters (M7i)

        /// <summary>
        /// Establishes this run's parameters: for every declaration the tree carries, the effective
        /// value (the caller's enabled override, or the declared default) is BOTH written into the
        /// shared blackboard under the parameter's NAME and kept, keyed by its ID, as the scope the
        /// bindings and the pass-through rows read.
        ///
        /// The two channels are deliberate and neither replaces the other. The NAME is what a task
        /// or condition written to read the blackboard by a hand-typed key finds (the M7g contract,
        /// and the only channel a graph's <c>GetBlackboard*</c> nodes have); the ID is what a
        /// binding or a pass-through row resolves, because those are authored surfaces and must
        /// survive a rename (M7h).
        ///
        /// THE DECLARATION IS READ FROM THE RUNNING COPY (<c>m_ActiveData.parameters</c>), which
        /// carries the same rows and the same identities as the authored asset —
        /// <see cref="StateTreeAsset.DeepCopy"/> clones serialized data — so nothing here can write
        /// to the asset on disk even by accident.
        ///
        /// RE-DERIVED ON EVERY START, and a sub-tree activation is a start: the arguments of a call
        /// do not persist across calls, so a re-entered state hands its tree the configured value
        /// again rather than whatever the last run left on the blackboard.
        ///
        /// NO SAVE/RESTORE of the blackboard keys (unchanged from M7g): the declaration IS the
        /// tree's claim on that key. The SCOPE, being an implementation channel rather than authored
        /// state, is saved and restored — see <see cref="PushParameterScope"/>.
        /// </summary>
        private void BuildParameterScope()
        {
            m_ParamScope = new Dictionary<string, GraphTaskParameter>();
            List<GraphTaskParameter> declared = m_ActiveData.parameters;
            if (declared == null)
                return;

            for (int i = 0; i < declared.Count; i++)
            {
                GraphTaskParameter parameter = declared[i];
                // A nameless row is an inspector row mid-edit, not a parameter: it has no key to
                // seed, and letting it into the scope would let a binding resolve to a half-typed
                // declaration.
                if (parameter == null || string.IsNullOrEmpty(parameter.name))
                    continue;

                GraphTaskParameter effective = EffectiveParameter(parameter);
                context.blackboard[effective.name] = BoxedValue(effective);
                // A declaration with no identity still seeds — that is the name's job — but nothing
                // can bind to it, so it is not in the scope. See GraphTaskParameter.id.
                if (!string.IsNullOrEmpty(effective.id))
                    m_ParamScope[effective.id] = effective;
            }
        }

        /// <summary>One declaration with this run's value applied, as a private COPY: the caller's
        /// override must never reach the authored row, and the scope must stay valid after the copy
        /// the run was built from is destroyed. Only the field the KIND actually shows is taken from
        /// the row, so a row left over from when the parameter was a float cannot smuggle a number
        /// into a string parameter (the rule <see cref="GraphTaskAsset.ApplyOverrides"/>
        /// applies).</summary>
        private GraphTaskParameter EffectiveParameter(GraphTaskParameter declared)
        {
            var effective = new GraphTaskParameter
            {
                name = declared.name,
                kind = declared.kind,
                floatValue = declared.floatValue,
                stringValue = declared.stringValue,
                id = declared.id
            };

            GraphTaskParameterOverride row =
                GraphTaskParameterOverride.EnabledFor(parameterOverrides, declared);
            if (row == null)
                return effective;

            if (declared.kind == GraphTaskParameterKind.String)
                effective.stringValue = row.stringValue;
            else
                effective.floatValue = row.floatValue;
            return effective;
        }

        /// <summary>
        /// The value a parameter is seeded with, BOXED AS THE TYPE THE CONSUMERS ALREADY READ. The
        /// blackboard is <c>Dictionary&lt;string, object&gt;</c>, so the boxed type is a real
        /// contract and picking it by taste breaks readers silently (M7g's finding, moved here
        /// verbatim with its citations):
        /// <list type="bullet">
        /// <item>Float =&gt; <c>float</c>. What the only other library writer boxes
        /// (SetBlackboardTask.cs:65, StateTreeLibraryUtil.cs:184) and what every reader
        /// accepts.</item>
        /// <item>String =&gt; <c>string</c> (SetBlackboardTask.cs:68); null is normalised to empty,
        /// matching how a graph parameter reads (GraphTaskAsset.cs:450-452).</item>
        /// <item>Bool =&gt; <c>float</c> 1/0, NOT a boxed <c>bool</c>. This is the one that has to be
        /// argued: <c>GraphTaskAsset.ReadBlackboardFloat</c> would accept either (it reads a bool as
        /// 1/0), but <c>StateTreeLibraryUtil.TryGetFloat</c> accepts float/int/double and NOTHING
        /// else (StateTreeLibraryUtil.cs:164-177), and that is the path
        /// <see cref="BlackboardCompareCondition"/> reads through
        /// (BlackboardCompareCondition.cs:45). A boxed bool would therefore make every transition
        /// gated on a declared Bool parameter read false forever, with no diagnostic. A float 1/0 is
        /// read correctly by BOTH, and it is what a Bool parameter already IS in this model
        /// (GraphTaskParameter.floatValue != 0).</item>
        /// </list>
        /// A field BINDING is the other channel and has its own rule — a Bool parameter writes a
        /// real <c>bool</c> there, because a field has a declared type and no ambiguity to resolve.
        ///
        /// SHARED WITH OUTPUT ROUTING (M7j), which writes the same dictionary from the other end: a
        /// routed output and a seeded parameter that a task then reads by the same key must not come
        /// back as different types, and the only way to guarantee that is for both to go through
        /// this one switch.
        /// </summary>
        private static object BoxedValue(GraphTaskParameterKind kind, float floatValue,
            string stringValue)
        {
            switch (kind)
            {
                case GraphTaskParameterKind.String:
                    return stringValue ?? string.Empty;
                case GraphTaskParameterKind.Bool:
                    return floatValue != 0f ? 1f : 0f;
                default:
                    return floatValue;
            }
        }

        /// <summary>The boxing rule above, for a declaration.</summary>
        private static object BoxedValue(GraphTaskParameter parameter)
        {
            return BoxedValue(parameter.kind, parameter.floatValue, parameter.stringValue);
        }

        /// <summary>Publishes this run's scope, remembering whatever was there — the caller's scope
        /// when this is a sub-tree — so <see cref="PopParameterScope"/> can hand it back. Same
        /// save/restore discipline as the sub-tree depth counter, and needed for the same reason:
        /// two trees run on ONE context, and the inner one must not leave its parameters standing as
        /// the outer one's after it ends.</summary>
        private void PushParameterScope()
        {
            // Defensive: a second StartTree with no StopTree in between must not lose the caller's
            // scope by saving this executor's own over it.
            if (m_ScopePushed)
                PopParameterScope();

            m_HadPreviousScope = context.domainContext.TryGetValue(paramScopeKey, out m_PreviousScope);
            context.domainContext[paramScopeKey] = m_ParamScope;
            m_ScopePushed = true;
        }

        private void PopParameterScope()
        {
            if (!m_ScopePushed)
                return;
            m_ScopePushed = false;
            if (context != null)
            {
                if (m_HadPreviousScope)
                    context.domainContext[paramScopeKey] = m_PreviousScope;
                else
                    context.domainContext.Remove(paramScopeKey);
            }
            m_PreviousScope = null;
            m_HadPreviousScope = false;
            m_ParamScope = null;
        }

        /// <summary>The effective parameter with <paramref name="parameterId"/> in the scope of the
        /// tree currently running on <paramref name="context"/>, or null when there is no scope or no
        /// such parameter. THE read side of pass-through, and a static because both wrappers
        /// (<see cref="RunSubTreeTask"/>, <see cref="RunGraphTask"/>) ask it about a tree they are
        /// inside rather than one they own.</summary>
        public static GraphTaskParameter ScopeParameter(StateTreeContext context, string parameterId)
        {
            if (context == null || string.IsNullOrEmpty(parameterId))
                return null;
            object scope;
            if (!context.domainContext.TryGetValue(paramScopeKey, out scope))
                return null;
            var parameters = scope as Dictionary<string, GraphTaskParameter>;
            GraphTaskParameter parameter;
            if (parameters == null || !parameters.TryGetValue(parameterId, out parameter))
                return null;
            return parameter;
        }

        /// <summary>
        /// The override rows of one call with every PASS-THROUGH row resolved against the caller's
        /// scope — the list the callee is actually handed. Shared by both wrappers so a value passed
        /// through into a sub-tree and one passed through into a graph cannot come to mean different
        /// things.
        ///
        /// Returns <paramref name="rows"/> ITSELF when no enabled row carries a
        /// <see cref="GraphTaskParameterOverride.sourceParameterId"/>, which is every call that does
        /// not use the feature: no allocation, and the serialized rows are never copied for nothing.
        /// When it does copy, it copies — the serialized rows are authored data and resolving a
        /// value into them would burn a runtime value into the asset.
        ///
        /// A row whose declaration is missing is left alone rather than resolved: it is stale by the
        /// M7h rule and the caller reports it as such, and reporting it twice for two different
        /// reasons would just be noise. A row whose SOURCE is missing, or whose source has a
        /// different kind from the parameter it overrides, is DROPPED — so the callee's declared
        /// default applies, which is the same thing an unchecked row does — and named in
        /// <paramref name="unresolved"/> for the caller's once-per-instance warning.
        /// </summary>
        public static List<GraphTaskParameterOverride> ResolveSourceValues(StateTreeContext context,
            List<GraphTaskParameterOverride> rows, List<GraphTaskParameter> declared,
            out string unresolved)
        {
            unresolved = null;
            if (rows == null)
                return null;

            bool any = false;
            for (int i = 0; i < rows.Count && !any; i++)
            {
                GraphTaskParameterOverride row = rows[i];
                any = row != null && row.enabled
                    && (!string.IsNullOrEmpty(row.sourceParameterId)
                        || !string.IsNullOrEmpty(row.keyId));
            }
            if (!any)
                return rows;

            var resolved = new List<GraphTaskParameterOverride>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                GraphTaskParameterOverride row = rows[i];
                if (row == null || !row.enabled
                    || (string.IsNullOrEmpty(row.sourceParameterId)
                        && string.IsNullOrEmpty(row.keyId)))
                {
                    resolved.Add(row);
                    continue;
                }

                // A KEY-WIRED row resolves to the declaration's CURRENT name — the M14 rename
                // rule for override rows. A wire whose declaration is gone falls back to the
                // row's text, the same grace a KeyField gives.
                if (!string.IsNullOrEmpty(row.keyId))
                {
                    StateTreeKeyDeclaration declaration = StateTreeKeyResolver.Find(null,
                        context != null ? context.owner : null, row.keyId);
                    if (declaration == null
                        || string.Equals(declaration.name, row.stringValue,
                            StringComparison.Ordinal))
                    {
                        resolved.Add(row);
                    }
                    else
                    {
                        resolved.Add(new GraphTaskParameterOverride
                        {
                            name = row.name,
                            enabled = true,
                            id = row.id,
                            keyId = row.keyId,
                            floatValue = row.floatValue,
                            stringValue = declaration.name
                        });
                    }
                    continue;
                }

                GraphTaskParameter target = row.Declaration(declared);
                if (target == null)
                {
                    resolved.Add(row);
                    continue;
                }

                GraphTaskParameter source = ScopeParameter(context, row.sourceParameterId);
                if (source == null || source.kind != target.kind)
                {
                    unresolved = unresolved == null
                        ? row.Describe(i)
                        : unresolved + ", " + row.Describe(i);
                    continue;
                }

                resolved.Add(new GraphTaskParameterOverride
                {
                    name = row.name,
                    enabled = true,
                    id = row.id,
                    sourceParameterId = row.sourceParameterId,
                    floatValue = source.floatValue,
                    stringValue = source.stringValue
                });
            }
            return resolved;
        }

        // ---------------------------------------------------------------- task outputs (M7j)

        /// <summary>
        /// Records what one finished task RETURNED, under its authored index, into the state's exit
        /// record. Called from the tick loop at the moment the task reports Success or Failure and
        /// BEFORE its OnExit — see the comment at the call site for why that order is not
        /// negotiable.
        ///
        /// TWO PRODUCERS, tried in that order and never both. A task that implements
        /// <see cref="IStateTreeOutputSource"/> answers for itself, because its outputs are not
        /// fields at all — a graph's are whatever its Set Output instructions wrote this activation,
        /// and a wrapper's are its inner task's. Everything else is a plain C# task and its outputs
        /// are its <c>[TaskOutput]</c> fields, read now rather than at any other time because "the
        /// value at the moment it finished" is what a return value means.
        ///
        /// CANCELLED TASKS NEVER GET HERE. <see cref="ExitRunningTasks"/> is the only other place a
        /// task is exited and it does not capture: an interrupted task did not return, it was
        /// abandoned, and reading a half-finished value out of it would give a route something that
        /// looks exactly like a real result.
        /// </summary>
        private void CaptureOutputs(int taskIndex, StateTreeTaskAsset task,
            StateTreeContext context)
        {
            if (task == null || taskIndex < 0)
                return;

            m_CaptureScratch.Clear();
            var source = task as IStateTreeOutputSource;
            if (source != null)
                source.TryCollectOutputs(m_CaptureScratch);
            else
                CollectAttributedOutputs(task, m_CaptureScratch);

            // Nothing to record is the overwhelmingly common case, and storing an empty list for it
            // would allocate once per finished task per activation to say "no". EnterNode has already
            // cleared the record, so a task that returns nothing simply has no entry.
            if (m_CaptureScratch.Count == 0)
                return;
            m_TaskOutputs[taskIndex] = new List<TaskOutputValue>(m_CaptureScratch);
            PublishTaskReturns(task, context);
        }

        /// <summary>The task-level RETURN routes (<see cref="StateTreeTaskAsset.returns"/>),
        /// applied at the same capture moment: the unconditional twin of the transition's
        /// per-wire routes, for every output producer alike. An output the task did not set
        /// this activation publishes nothing — absence is a non-event.</summary>
        private void PublishTaskReturns(StateTreeTaskAsset task, StateTreeContext context)
        {
            var routes = task.returns;
            if (routes == null || context == null)
                return;

            for (int i = 0; i < routes.Count; i++)
            {
                TaskReturnRoute route = routes[i];
                string key = route != null ? (string)route.key : null;
                if (route == null || string.IsNullOrEmpty(route.output)
                    || string.IsNullOrEmpty(key))
                    continue;

                int found = IndexOfOutput(m_CaptureScratch, route.output);
                if (found < 0)
                    continue;
                TaskOutputValue value = m_CaptureScratch[found];
                context.blackboard[key] =
                    BoxedValue(value.kind, value.floatValue, value.stringValue);
            }
        }

        /// <summary>Reads every <c>[TaskOutput]</c> field of a task into
        /// <paramref name="into"/>.</summary>
        private static void CollectAttributedOutputs(StateTreeTaskAsset task,
            List<TaskOutputValue> into)
        {
            FieldInfo[] fields = OutputFields(task.GetType());
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                object value = field.GetValue(task);
                Type type = field.FieldType;
                TaskOutputValue captured;

                if (type == typeof(string))
                {
                    captured = new TaskOutputValue
                    {
                        name = field.Name,
                        kind = GraphTaskParameterKind.String,
                        stringValue = value as string ?? string.Empty
                    };
                }
                else if (type == typeof(bool))
                {
                    captured = new TaskOutputValue
                    {
                        name = field.Name,
                        kind = GraphTaskParameterKind.Bool,
                        floatValue = value is bool flag && flag ? 1f : 0f
                    };
                }
                else
                {
                    // float and int both return as Float — the same pairing the field BINDINGS
                    // accept in the other direction, so a value can make the round trip through a
                    // parameter and back out as an output without changing kind on the way.
                    captured = new TaskOutputValue
                    {
                        name = field.Name,
                        kind = GraphTaskParameterKind.Float,
                        floatValue = value is int number ? number : (value is float f ? f : 0f)
                    };
                }
                into.Add(captured);
            }
        }

        /// <summary>The <c>[TaskOutput]</c> fields of a type, cached (the M7i field-cache pattern).
        /// <c>inherit: true</c> on the attribute lookup so a task deriving from one that publishes an
        /// output keeps publishing it, and unsupported field types are filtered out HERE rather than
        /// at capture time — a decorated <c>Vector2</c> is an authoring mistake in C#, not a runtime
        /// event, and it must not cost a type test on every completion.</summary>
        private static FieldInfo[] OutputFields(Type type)
        {
            FieldInfo[] cached;
            if (s_OutputFieldsByType.TryGetValue(type, out cached))
                return cached;

            var found = new List<FieldInfo>();
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsDefined(typeof(TaskOutputAttribute), true))
                    continue;
                Type fieldType = field.FieldType;
                if (fieldType == typeof(float) || fieldType == typeof(int)
                    || fieldType == typeof(bool) || fieldType == typeof(string))
                    found.Add(field);
            }

            cached = found.ToArray();
            s_OutputFieldsByType[type] = cached;
            return cached;
        }

        /// <summary>
        /// Writes the outputs a fired transition asks for into the blackboard — the assignment half
        /// of the call. Runs once, on the transition that is actually taken, so two edges out of one
        /// state can route the same result differently (or not at all) and only the one that fired
        /// has any effect.
        ///
        /// AN INTERRUPT ROUTES ONLY WHAT ALREADY FINISHED, and that falls out of the design rather
        /// than needing a rule: the exit record holds exactly the tasks that returned, so a
        /// checkWhileRunning transition firing over a still-running task finds nothing for it and
        /// says so. That is the honest answer — the task has not returned yet.
        /// </summary>
        private void RouteOutputs(StateTreeTransition transition)
        {
            List<TransitionOutputRoute> routes = transition != null ? transition.outputRoutes : null;
            if (routes == null || routes.Count == 0 || context == null)
                return;

            for (int i = 0; i < routes.Count; i++)
            {
                TransitionOutputRoute route = routes[i];
                // A row with no output named is an inspector row mid-edit rather than a route: it
                // names nothing to read and nothing to write, so there is no mistake to report.
                if (route == null || string.IsNullOrEmpty(route.outputName))
                    continue;

                List<TaskOutputValue> values;
                if (!m_TaskOutputs.TryGetValue(route.taskIndex, out values))
                {
                    RouteWarning(route, "task " + route.taskIndex + " returned nothing — it is "
                        + "still running, it was cancelled by this transition, or there is no task "
                        + "at that position");
                    continue;
                }

                int found = IndexOfOutput(values, route.outputName);
                if (found < 0)
                {
                    RouteWarning(route, "task " + route.taskIndex + " declares no output of that "
                        + "name (it was renamed, or this route was copied from another task)");
                    continue;
                }

                TaskOutputValue value = values[found];
                context.blackboard[route.ResolvedKey()] =
                    BoxedValue(value.kind, value.floatValue, value.stringValue);
            }
        }

        /// <summary>Ordinal match on the contract name — see
        /// <see cref="TaskOutputValue.name"/>.</summary>
        private static int IndexOfOutput(List<TaskOutputValue> values, string outputName)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i].name, outputName, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        /// <summary>A route that could not be honoured: one WARNING per row per run, naming the tree,
        /// the state and the row. A warning rather than an error because the transition still fires
        /// and the tree runs on — the cost is a key that was not written, which the next state will
        /// notice for itself — and once per row because a pair of states that ping-pongs would
        /// otherwise report the same mistake every tick.</summary>
        private void RouteWarning(TransitionOutputRoute route, string reason)
        {
            if (m_WarnedRoutes == null)
                m_WarnedRoutes = new HashSet<TransitionOutputRoute>();
            if (!m_WarnedRoutes.Add(route))
                return;

            string nodeId = m_CurrentNode != null ? m_CurrentNode.nodeId : "";
            Debug.LogWarning($"{logLabel}: tree '{TreeLabel()}' state '{nodeId}' routes " +
                $"{route.Describe()} to blackboard key '{route.ResolvedKey()}', but {reason}. " +
                "The key is not written.", logContext);
        }

        // ---------------------------------------------------------------- field bindings (M7i)

        /// <summary>Applies every node's PARAMETER-sourced binding rows to the COPY this run owns,
        /// depth-first over the same nodes <see cref="BuildIndex"/> walks (children included, whether
        /// or not they carry an id — an unreachable state is an authoring mistake, not a reason to
        /// leave its fields unbound and its binding errors unreported).
        ///
        /// Blackboard-sourced rows are NOT start-time rows and are skipped here: their value does not
        /// exist yet when the tree starts, and re-reading it is the point of them. They belong to
        /// <see cref="ApplyEntryBindings"/>, which runs on every entry of the state that owns
        /// them.</summary>
        /// <summary>
        /// The M12 rewrite: every key-linked string field on the deep copy gets the linked
        /// declaration's CURRENT name — which is the entire trick that makes declared keys
        /// renameable while runtime stays plain strings. A link whose id resolves nowhere
        /// (declaration deleted, import removed, mounted elsewhere) is ONE warning and the
        /// field keeps its serialized text — degraded to an unmanaged key, not broken.
        /// </summary>
        /// <summary>
        /// The M14 form of the key pass: the wire lives in the field
        /// (<see cref="StateTreeKeyField.keyId"/>), so this walks the copy's tasks and
        /// transition conditions, resolves each wired field's id to the declaration's
        /// CURRENT name and binds it on the wrapper — the field's authored text stays as
        /// the fallback the wrapper answers with when the id resolves nowhere (one error;
        /// free-typed keys have no id and are skipped silently).
        /// </summary>
        private void ResolveKeyFields(StateTreeNodeAsset node, int depth)
        {
            if (node == null || depth > 256)
                return;

            for (int i = 0; i < node.tasks.Count; i++)
                ResolveKeyFieldsOn(node.tasks[i], node.nodeId);
            for (int i = 0; i < node.transitions.Count; i++)
            {
                if (node.transitions[i] != null)
                    ResolveKeyFieldsOn(node.transitions[i].condition, node.nodeId);
            }

            for (int i = 0; i < node.children.Count; i++)
                ResolveKeyFields(node.children[i], depth + 1);
        }

        private void ResolveKeyFieldsOn(UnityEngine.Object target, string nodeId)
        {
            if (target == null)
                return;

            FieldInfo[] fields = target.GetType().GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(StateTreeKeyField))
                    continue;
                if (!(fields[i].GetValue(target) is StateTreeKeyField key) || !key.isWired)
                    continue;

                StateTreeKeyDeclaration declaration =
                    StateTreeKeyResolver.Find(m_ActiveData, owner, key.keyId);
                if (declaration != null && !string.IsNullOrEmpty(declaration.name))
                {
                    key.BindResolved(declaration.name);
                    continue;
                }

                // A key wire may also name a declared PARAMETER — the caller's argument
                // surface seeds the blackboard under the parameter's name, so a field wired
                // to the declaration follows its renames exactly like a key wire does.
                GraphTaskParameter parameter = FindParameterById(key.keyId);
                if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                {
                    key.BindResolved(parameter.name);
                    continue;
                }

                Debug.LogError(logLabel + ": key field '" + fields[i].Name + "' on state '"
                    + nodeId + "' resolves no declaration (id '" + key.keyId
                    + "', neither a key nor a parameter) — the field's own text runs.",
                    logContext);
            }
        }

        private GraphTaskParameter FindParameterById(string id)
        {
            var declared = m_ActiveData != null ? m_ActiveData.parameters : null;
            for (int i = 0; declared != null && i < declared.Count; i++)
            {
                if (declared[i] != null
                    && string.Equals(declared[i].id, id, System.StringComparison.Ordinal))
                    return declared[i];
            }
            return null;
        }

        /// <summary>
        /// The M13 data pass: inject live registry entries into every typed reference field
        /// on the copy's tasks and transition conditions, from the tree's
        /// <see cref="StateTreeAsset.registries"/>. Runs beside <see cref="ResolveKeyLinks"/>
        /// and shares its philosophy — ids are the wires, resolution happens once per start,
        /// and a reference that resolves nowhere is ONE error plus a null the task's own
        /// guard answers for. An UNSET reference is skipped silently: an empty slot is
        /// authoring state, not a wiring fault.
        /// </summary>
        private void ResolveEntryRefs(StateTreeNodeAsset node, int depth)
        {
            if (node == null || depth > 256)
                return;

            for (int i = 0; i < node.tasks.Count; i++)
                BindReferenceFields(node.tasks[i], node.nodeId);
            for (int i = 0; i < node.transitions.Count; i++)
            {
                if (node.transitions[i] != null)
                    BindReferenceFields(node.transitions[i].condition, node.nodeId);
            }

            for (int i = 0; i < node.children.Count; i++)
                ResolveEntryRefs(node.children[i], depth + 1);
        }

        private void BindReferenceFields(UnityEngine.Object target, string nodeId)
        {
            if (target == null)
                return;

            // The lean capability wire: [InjectService] fields filled by the framework, no
            // wrapper, no per-atom guard — see StateTreeServiceInjector.
            StateTreeServiceInjector.Inject(target, owner);

            FieldInfo[] fields = target.GetType().GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                if (typeof(IStateTreeEntryRef).IsAssignableFrom(fields[i].FieldType))
                {
                    if (fields[i].GetValue(target) is IStateTreeEntryRef reference)
                        BindEntryRef(reference, fields[i].Name, nodeId);
                }
                else if (typeof(IStateTreeRegistryRef).IsAssignableFrom(fields[i].FieldType))
                {
                    if (fields[i].GetValue(target) is IStateTreeRegistryRef reference)
                        BindRegistryRef(reference, fields[i].Name, nodeId);
                }
                else if (typeof(IStateTreeServiceRef).IsAssignableFrom(fields[i].FieldType))
                {
                    if (fields[i].GetValue(target) is IStateTreeServiceRef reference)
                        BindServiceRef(reference, fields[i].Name, nodeId);
                }
            }
        }

        /// <summary>The M15 capability injection: resolve the field's service type through the
        /// OWNER's view of the spine (nearest host of any kind, else the unique Root — the
        /// FindService rule), so scope follows the mount point. No capability = one error and
        /// the reference stays empty for the task's guard to answer.</summary>
        private void BindServiceRef(IStateTreeServiceRef reference, string fieldName,
            string nodeId)
        {
            StateTreeContextHost host = owner != null
                ? StateTreeContextHost.ResolveNearest(owner)
                : null;
            if (host == null)
                host = StateTreeContextHost.Resolve(owner, StateTreeContextKind.Root);

            object service = host != null ? host.GetService(reference.ServiceType) : null;
            if (service == null)
            {
                Debug.LogError(logLabel + ": state '" + nodeId + "' field '" + fieldName
                    + "' wants a " + reference.ServiceType.Name
                    + " and the spine provides none — the reference stays empty.", logContext);
                return;
            }
            reference.Bind(service);
        }

        private void BindEntryRef(IStateTreeEntryRef reference, string fieldName, string nodeId)
        {
            bool byId = !string.IsNullOrEmpty(reference.EntryId);
            // Name-only is the FREE-TYPED flavor — what a graph port authors, what a hand
            // types — resolved by name the way a free-typed key runs on its text. Empty
            // both ways is authoring state, skipped silently.
            if (!byId && string.IsNullOrEmpty(reference.EntryName))
                return;

            List<StateTreeRegistryAsset> registries = m_ActiveData.registries;
            for (int i = 0; registries != null && i < registries.Count; i++)
            {
                StateTreeRegistryAsset registry = registries[i];
                if (registry == null
                    || !reference.EntryType.IsAssignableFrom(registry.entryType))
                    continue;

                StateTreeRegistryEntry entry = byId
                    ? registry.FindById(reference.EntryId)
                    : registry.FindByName(reference.EntryName);
                if (entry != null)
                {
                    reference.Bind(entry);
                    return;
                }
            }

            Debug.LogError(logLabel + ": entry reference on state '" + nodeId + "' field '"
                + fieldName + "' resolves no entry (" + (byId
                    ? "id '" + reference.EntryId + "'"
                    : "name '" + reference.EntryName + "'")
                + ") in the tree's registries — the reference stays empty.", logContext);
        }

        private void BindRegistryRef(IStateTreeRegistryRef reference, string fieldName,
            string nodeId)
        {
            List<StateTreeRegistryAsset> registries = m_ActiveData.registries;
            for (int i = 0; registries != null && i < registries.Count; i++)
            {
                StateTreeRegistryAsset registry = registries[i];
                if (registry != null
                    && reference.EntryType.IsAssignableFrom(registry.entryType))
                {
                    reference.Bind(registry);
                    return;
                }
            }

            Debug.LogError(logLabel + ": state '" + nodeId + "' field '" + fieldName
                + "' wants a registry of " + reference.EntryType.Name
                + " entries and the tree lists none — the reference stays empty.", logContext);
        }

        private void ApplyBindings(StateTreeNodeAsset node, int depth)
        {
            // 256 matches the authored-children guard in StateTreeAsset.DeepCopyNode.
            if (node == null || depth > 256)
                return;

            List<StateTreeFieldBinding> bindings = node.bindings;
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    StateTreeFieldBinding binding = bindings[i];
                    if (binding == null
                        || binding.sourceKind != StateTreeFieldBinding.SourceKind.Parameter)
                        continue;
                    ApplyBinding(node, binding);
                }
            }
            for (int i = 0; i < node.children.Count; i++)
                ApplyBindings(node.children[i], depth + 1);
        }

        /// <summary>One binding row. Every way it can fail — no field named, a target that is not
        /// there, a parameter that is not declared, a field the parameter's kind cannot write — is
        /// ONE error naming the tree, the state and the field, and the row is skipped. An error
        /// rather than a warning because a bound field silently left at its authored value is
        /// indistinguishable from a binding that worked, which is the whole failure this reports;
        /// skipped rather than thrown because the other states of the tree are still runnable.</summary>
        private void ApplyBinding(StateTreeNodeAsset node, StateTreeFieldBinding binding)
        {
            if (binding == null)
                return;
            if (string.IsNullOrEmpty(binding.fieldName))
            {
                BindingError(node, binding, "the row names no field");
                return;
            }

            UnityEngine.Object target = BindingTarget(node, binding);
            if (target == null)
            {
                BindingError(node, binding, "its target (" + TargetLabel(binding) + ") does not exist");
                return;
            }

            GraphTaskParameter parameter = null;
            if (m_ParamScope == null || string.IsNullOrEmpty(binding.parameterId)
                || !m_ParamScope.TryGetValue(binding.parameterId, out parameter))
            {
                BindingError(node, binding, "this tree declares no parameter with id '"
                    + binding.parameterId + "'");
                return;
            }

            FieldInfo field = Field(target.GetType(), binding.fieldName);
            if (field == null)
            {
                BindingError(node, binding, TargetLabel(binding) + " (" + target.GetType().Name
                    + ") has no public field of that name");
                return;
            }
            if (!TryWrite(field, target, parameter))
            {
                BindingError(node, binding, "parameter '" + parameter.name + "' is a "
                    + parameter.kind + " and the field is a " + field.FieldType.Name
                    + " — the kinds do not match");
            }
        }

        /// <summary>The sub-asset a row points at, on the DEEP COPY (the authored one is never
        /// written to). Null covers an out-of-range index, a hole in the list, and a transition with
        /// no condition — all reported the same way, because to an author they are one mistake:
        /// "there is nothing there".</summary>
        private static UnityEngine.Object BindingTarget(StateTreeNodeAsset node,
            StateTreeFieldBinding binding)
        {
            if (binding.targetIndex < 0)
                return null;
            if (binding.targetKind == StateTreeFieldBinding.TargetKind.TransitionCondition)
            {
                if (binding.targetIndex >= node.transitions.Count)
                    return null;
                StateTreeTransition transition = node.transitions[binding.targetIndex];
                return transition != null ? transition.condition : null;
            }
            return binding.targetIndex < node.tasks.Count ? node.tasks[binding.targetIndex] : null;
        }

        private static string TargetLabel(StateTreeFieldBinding binding)
        {
            return binding.targetKind == StateTreeFieldBinding.TargetKind.TransitionCondition
                ? "the condition of transition " + binding.targetIndex
                : "task " + binding.targetIndex;
        }

        /// <summary>
        /// Writes a parameter into a field, or refuses. The accepted pairs are the ones where the
        /// value carries over with no interpretation: Float into <c>float</c> or <c>int</c> (a
        /// tuning knob is routinely an int count), Bool into <c>bool</c>, String into
        /// <c>string</c>.
        ///
        /// Everything else is refused rather than converted, including the pairs that would
        /// "work" — a Bool into a float field, a Float into a bool one. A silent conversion is how a
        /// binding ends up meaning something the author did not choose, and the link control that
        /// creates these rows only offers compatible parameters in the first place, so a mismatch
        /// here means the field or the parameter changed TYPE afterwards. That is exactly when the
        /// author needs telling.
        /// </summary>
        private static bool TryWrite(FieldInfo field, object target, GraphTaskParameter parameter)
        {
            Type type = field.FieldType;
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                    if (type != typeof(string))
                        return false;
                    field.SetValue(target, parameter.stringValue ?? string.Empty);
                    return true;
                case GraphTaskParameterKind.Bool:
                    if (type != typeof(bool))
                        return false;
                    field.SetValue(target, parameter.floatValue != 0f);
                    return true;
                default:
                    if (type == typeof(float))
                    {
                        field.SetValue(target, parameter.floatValue);
                        return true;
                    }
                    if (type == typeof(int))
                    {
                        field.SetValue(target, (int)parameter.floatValue);
                        return true;
                    }
                    return false;
            }
        }

        /// <summary>Public instance fields of a type by name, cached. <c>GetFields</c> with these
        /// flags also returns the ones a base class declares, which is what an author sees in the
        /// inspector and therefore what may be bound.</summary>
        private static FieldInfo Field(Type type, string fieldName)
        {
            Dictionary<string, FieldInfo> byName;
            if (!s_FieldsByType.TryGetValue(type, out byName))
            {
                byName = new Dictionary<string, FieldInfo>();
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                    byName[fields[i].Name] = fields[i];
                s_FieldsByType[type] = byName;
            }
            FieldInfo field;
            return byName.TryGetValue(fieldName, out field) ? field : null;
        }

        private void BindingError(StateTreeNodeAsset node, StateTreeFieldBinding binding, string reason)
        {
            Debug.LogError($"{logLabel}: tree '{TreeLabel()}' state '{node.nodeId}' binds field " +
                $"'{binding.fieldName}' — {reason}. The binding is skipped.", logContext);
        }

        // ------------------------------------------------------ blackboard-sourced bindings (M7k)

        /// <summary>
        /// Reads the BLACKBOARD-sourced binding rows of the state being entered into the fields they
        /// name. Called from <see cref="EnterNode"/> before the tasks are entered, and therefore
        /// after <see cref="RouteOutputs"/> has written whatever the fired transition routed: that
        /// ordering is what makes "the previous state computed a number and this state's task starts
        /// with it" a single hop rather than something a task has to go and fetch.
        ///
        /// EVERY ENTRY, not once per run. A parameter is an argument and cannot change during a call;
        /// a key is a value the run produces, so a re-entered state must see what is there NOW. It
        /// also means the cost is per entry rather than per tick, which is the right side of that
        /// trade — entries are rare, ticks are not.
        ///
        /// CONDITIONS ARE INCLUDED. A transition's condition is evaluated while the state runs, so a
        /// value routed into the state is exactly as relevant to the edges leaving it as to the tasks
        /// inside it — a threshold carried in and compared against is the ordinary shape of that.
        /// </summary>
        private void ApplyEntryBindings(StateTreeNodeAsset node)
        {
            List<StateTreeFieldBinding> bindings = node != null ? node.bindings : null;
            if (bindings == null || context == null)
                return;

            for (int i = 0; i < bindings.Count; i++)
            {
                StateTreeFieldBinding binding = bindings[i];
                if (binding == null
                    || binding.sourceKind != StateTreeFieldBinding.SourceKind.BlackboardKey)
                    continue;
                ApplyEntryBinding(node, binding);
            }
        }

        /// <summary>
        /// One blackboard-sourced row, on one entry. Three outcomes, and which one a failure gets is
        /// the whole diagnostic design here:
        ///
        /// SILENT — an unfinished row (no key or no field named) and, above all, a key that is not on
        /// the blackboard. The missing key is the case that had to be silent: several transitions
        /// normally lead into one state and only some of them route anything, so entering through an
        /// unrouted edge is the ordinary path, not a fault. The field keeps what it holds, which on
        /// the first entry is its authored value.
        ///
        /// ERROR — the row names a target or a field that is not there. That is the same structural
        /// mistake <see cref="ApplyBinding"/> reports for a parameter row, it cannot come right by
        /// itself, and it is reported whether or not the key happens to be present: waiting for the
        /// key would hide a broken row until the day something routes into it.
        ///
        /// WARNING — the key holds a value the field cannot take. Not an error, because the
        /// blackboard is shared and untyped: another producer writing a string where this row expects
        /// a number is a collision between two authored things, both of which may be individually
        /// right, and the tree keeps running with the field at its previous value.
        ///
        /// Both reports are ONCE PER ROW PER RUN (<see cref="m_WarnedBindings"/>) — this path runs on
        /// every entry, so anything else would turn one mistake into a console flood.
        /// </summary>
        private void ApplyEntryBinding(StateTreeNodeAsset node, StateTreeFieldBinding binding)
        {
            if (string.IsNullOrEmpty(binding.blackboardKey)
                || string.IsNullOrEmpty(binding.fieldName))
                return;

            UnityEngine.Object target = BindingTarget(node, binding);
            if (target == null)
            {
                EntryBindingReport(node, binding, true,
                    "its target (" + TargetLabel(binding) + ") does not exist");
                return;
            }

            FieldInfo field = Field(target.GetType(), binding.fieldName);
            if (field == null)
            {
                EntryBindingReport(node, binding, true, TargetLabel(binding) + " ("
                    + target.GetType().Name + ") has no public field of that name");
                return;
            }

            object value;
            if (!context.blackboard.TryGetValue(binding.blackboardKey, out value) || value == null)
                return;

            if (!TryWriteBoxed(field, target, value))
            {
                EntryBindingReport(node, binding, false, "the key holds a " + value.GetType().Name
                    + " and the field is a " + field.FieldType.Name
                    + " — the value cannot be carried across");
            }
        }

        /// <summary>
        /// Writes a blackboard value into a field, or refuses — the reverse of the boxing rule in
        /// <see cref="BoxedValue(GraphTaskParameterKind, float, string)"/>, and NOT the same table as
        /// the parameter bindings' <see cref="TryWrite"/>. The difference is real rather than an
        /// oversight: a parameter arrives with a declared KIND, so a Bool driving a float field is an
        /// author confusing two types and is refused. A blackboard entry arrives with nothing but a
        /// boxed CLR type, and this model deliberately stores a bool AS a float 1/0 (see the boxing
        /// rule for why — <c>StateTreeLibraryUtil.TryGetFloat</c> reads numbers and nothing else), so
        /// refusing float→bool here would make it impossible to bind the one thing most likely to be
        /// routed: a Bool task output. Numbers and flags are therefore interchangeable in this
        /// direction, and only string is kept apart, because there is no reading of "3" as a number
        /// that is not a guess about what the author meant.
        ///
        /// <c>int</c> and <c>double</c> are accepted as numbers alongside <c>float</c> for the reason
        /// <c>StateTreeLibraryUtil.TryGetFloat</c> accepts them (StateTreeLibraryUtil.cs:164-177): a
        /// hand-written task or an authored preset that stores <c>3</c> rather than <c>3f</c> is
        /// common enough that the codebase's own reader already tolerates it, and a binding that
        /// warned where a <c>Get Blackboard</c> node succeeds would be reporting a difference the
        /// author cannot see.
        /// </summary>
        private static bool TryWriteBoxed(FieldInfo field, object target, object value)
        {
            Type type = field.FieldType;

            if (value is string text)
            {
                if (type != typeof(string))
                    return false;
                field.SetValue(target, text);
                return true;
            }

            if (value is bool flag)
            {
                if (type == typeof(bool))
                {
                    field.SetValue(target, flag);
                    return true;
                }
                if (type == typeof(float))
                {
                    field.SetValue(target, flag ? 1f : 0f);
                    return true;
                }
                if (type == typeof(int))
                {
                    field.SetValue(target, flag ? 1 : 0);
                    return true;
                }
                return false;
            }

            float number;
            switch (value)
            {
                case float f:
                    number = f;
                    break;
                case int i:
                    number = i;
                    break;
                case double d:
                    number = (float)d;
                    break;
                default:
                    return false;
            }

            if (type == typeof(float))
            {
                field.SetValue(target, number);
                return true;
            }
            if (type == typeof(int))
            {
                field.SetValue(target, (int)number);
                return true;
            }
            if (type == typeof(bool))
            {
                field.SetValue(target, number != 0f);
                return true;
            }
            return false;
        }

        /// <summary>One report per row per run, as an error or a warning — see
        /// <see cref="ApplyEntryBinding"/> for which failure gets which.</summary>
        private void EntryBindingReport(StateTreeNodeAsset node, StateTreeFieldBinding binding,
            bool fatal, string reason)
        {
            if (m_WarnedBindings == null)
                m_WarnedBindings = new HashSet<StateTreeFieldBinding>();
            if (!m_WarnedBindings.Add(binding))
                return;

            string message = $"{logLabel}: tree '{TreeLabel()}' state '{node.nodeId}' binds field "
                + $"'{binding.fieldName}' to blackboard key '{binding.blackboardKey}' — {reason}. ";
            if (fatal)
            {
                // Nothing to keep: there is no field to have kept anything.
                Debug.LogError(message + "The binding is skipped.", logContext);
            }
            else
            {
                Debug.LogWarning(message + "The field keeps its current value.", logContext);
            }
        }

        private string TreeLabel()
        {
            if (m_ActiveData == null)
                return "";
            return !string.IsNullOrEmpty(m_ActiveData.treeName) ? m_ActiveData.treeName
                : m_ActiveData.name;
        }
    }
}
