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
        public event Action<string> nodeEntered;
        public event Action<string> nodeLeft;
        public event Action<string, string> activeNodeChanged;

        private StateTreeAsset m_ActiveData;
        private readonly Dictionary<string, StateTreeNodeAsset> m_NodeIndex =
            new Dictionary<string, StateTreeNodeAsset>();
        private StateTreeNodeAsset m_CurrentNode;
        private readonly List<StateTreeTaskAsset> m_RunningTasks = new List<StateTreeTaskAsset>();
        private readonly List<StateTreeTaskAsset> m_Finished = new List<StateTreeTaskAsset>();

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

            // The parameters are established BEFORE anything of the tree runs — before the entry
            // state's tasks are entered and before the treeStarted listeners can look at the
            // blackboard — because that is what makes them arguments rather than something the tree
            // observes changing under it.
            BuildParameterScope();
            PushParameterScope();
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
        /// </summary>
        private static object BoxedValue(GraphTaskParameter parameter)
        {
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                    return parameter.stringValue ?? string.Empty;
                case GraphTaskParameterKind.Bool:
                    return parameter.floatValue != 0f ? 1f : 0f;
                default:
                    return parameter.floatValue;
            }
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
                any = row != null && row.enabled && !string.IsNullOrEmpty(row.sourceParameterId);
            }
            if (!any)
                return rows;

            var resolved = new List<GraphTaskParameterOverride>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                GraphTaskParameterOverride row = rows[i];
                if (row == null || !row.enabled || string.IsNullOrEmpty(row.sourceParameterId))
                {
                    resolved.Add(row);
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

        // ---------------------------------------------------------------- field bindings (M7i)

        /// <summary>Applies every node's binding rows to the COPY this run owns, depth-first over
        /// the same nodes <see cref="BuildIndex"/> walks (children included, whether or not they
        /// carry an id — an unreachable state is an authoring mistake, not a reason to leave its
        /// fields unbound and its binding errors unreported).</summary>
        private void ApplyBindings(StateTreeNodeAsset node, int depth)
        {
            // 256 matches the authored-children guard in StateTreeAsset.DeepCopyNode.
            if (node == null || depth > 256)
                return;

            List<StateTreeFieldBinding> bindings = node.bindings;
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Count; i++)
                    ApplyBinding(node, bindings[i]);
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

        private string TreeLabel()
        {
            if (m_ActiveData == null)
                return "";
            return !string.IsNullOrEmpty(m_ActiveData.treeName) ? m_ActiveData.treeName
                : m_ActiveData.name;
        }
    }
}
