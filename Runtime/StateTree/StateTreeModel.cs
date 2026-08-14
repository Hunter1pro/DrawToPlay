using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Task result — port of StateTreeTask.Status. Cancelled is only ever passed
    /// to OnExit, when an interrupt (checkWhileRunning transition) or Stop pre-empts the
    /// task: the hook that makes teardown (nav goals, timers, spawned VFX) clean.</summary>
    public enum StateTreeStatus
    {
        Running,
        Success,
        Failure,
        Cancelled
    }

    /// <summary>Runtime context passed to tasks — port of state_tree_context.gd. Lean by
    /// design: domain state lives in the dictionaries, never as typed fields here.</summary>
    public sealed class StateTreeContext
    {
        public GameObject owner;
        public readonly Dictionary<string, object> blackboard = new Dictionary<string, object>();
        public readonly Dictionary<string, object> domainContext = new Dictionary<string, object>();

        public event Action<GameObject, GameObject, float> damageDealt;
        public event Action<string, Dictionary<string, object>> cueFired;
        public event Action<List<GameObject>> battleDetected;

        public StateTreeContext(GameObject owner = null)
        {
            this.owner = owner;
        }

        public void EmitDamageDealt(GameObject source, GameObject target, float amount)
            => damageDealt?.Invoke(source, target, amount);
        public void EmitCueFired(string cueName, Dictionary<string, object> payload)
            => cueFired?.Invoke(cueName, payload);
        public void EmitBattleDetected(List<GameObject> enemies)
            => battleDetected?.Invoke(enemies);
    }

    /// <summary>
    /// WHEN a state's tasks count as complete (M22, brief §10.2) — the gate on its
    /// on-completion transitions and on the implicit flow. Order is serialized — append only.
    /// </summary>
    public enum StateTreeCompleteWhen
    {
        /// <summary>Every BLOCKING task finished — the original rule, and the default every
        /// tree authored before M22 keeps. A state with no tasks is complete immediately.</summary>
        AllTasks = 0,

        /// <summary>At least one task finished this activation; the rest keep running until
        /// the state is actually left. A state with no tasks is complete immediately here
        /// too — "any of nothing" reading as "never" would make an empty state a trap.</summary>
        AnyTask = 1,

        /// <summary>The state never completes: interrupts are the only way out. THE honest
        /// resident state — what an hour-long WaitTask used to fake.</summary>
        Never = 2
    }

    /// <summary>
    /// Where a COMPLETED state goes when none of its declared on-completion transitions fire
    /// (M22, brief §10.1). Order is serialized — append only.
    /// </summary>
    public enum StateTreeCompletionFlow
    {
        /// <summary>The UE default this port adopts: the next sibling; a last sibling bubbles
        /// completion to its parent (whose own on-completion transitions get a chance, then
        /// ITS next sibling); the root completing finishes the tree. Children in order ARE a
        /// sequence — no edges needed. Declared transitions always win over this.</summary>
        NextSibling = 0,

        /// <summary>Complete but stay: the state keeps its declared on-completion transitions
        /// live (a condition may pass later) and goes nowhere by default. For states whose
        /// work is done but whose exit is someone else's decision.</summary>
        Hold = 1
    }

    /// <summary>Authored as a sub-asset with serialized params; subclasses override the
    /// virtuals. Port of state_tree_task.gd — the runner deep-copies the whole tree on
    /// Start (data.duplicate(true) mirror), so instance fields on a task are safe
    /// per-runner state.</summary>
    public abstract class StateTreeTaskAsset : ScriptableObject
    {
        /// <summary>False = this task runs while the state lives but does not hold the
        /// state's completion open (ambient cues, monitors) — the M22 half of
        /// <see cref="StateTreeCompleteWhen.AllTasks"/>. It still ticks until the state is
        /// left, and it exits Cancelled like anything else pre-empted.</summary>
        [Tooltip("Off: the state can complete while this task still runs — for ambient work "
            + "that should not hold a sequence open.")]
        public bool blocking = true;

        /// <summary>The task's RETURN connections: outputs published to blackboard keys the
        /// moment the task finishes, whatever way the state then leaves — the unconditional
        /// half of routing, bound where the task is mounted. Works for every output producer
        /// the executor knows: <c>[TaskOutput]</c> fields and
        /// <see cref="IStateTreeOutputSource"/> implementors alike. The per-exit-wire half
        /// stays on transitions (<see cref="StateTreeTransition.outputRoutes"/>).</summary>
        public List<TaskReturnRoute> returns = new List<TaskReturnRoute>();

        public virtual void OnEnter(StateTreeContext context) { }

        public virtual StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
            => StateTreeStatus.Success;

        public virtual void OnExit(StateTreeContext context, StateTreeStatus status) { }
    }

    /// <summary>Port of state_tree_condition.gd — subclasses override Evaluate.</summary>
    public abstract class StateTreeConditionAsset : ScriptableObject
    {
        public virtual bool Evaluate(StateTreeContext context) => true;
    }

    /// <summary>Port of state_tree_transition.gd. checkWhileRunning=false: evaluated only
    /// when every task in the source node finished (normal sequencing — exiting one state
    /// triggers the next). true: evaluated every tick BEFORE task ticks; fires as an
    /// interrupt and running tasks get OnExit(Cancelled).</summary>
    [Serializable]
    public sealed class StateTreeTransition
    {
        public string targetNodeId = "";
        public StateTreeConditionAsset condition;
        public bool checkWhileRunning;

        /// <summary>
        /// Where this transition puts the OUTPUTS of the tasks that finished in the state it leaves
        /// (M7j). Empty — every transition authored before M7j — means "the returns are discarded",
        /// which is what a call whose result nobody assigns does everywhere else.
        ///
        /// THE ROUTING LIVES ON THE TRANSITION, not on the task, because the transition is the only
        /// authored thing that knows both halves: which tasks have finished (it is the reason the
        /// state is ending) and where the tree is going next (so what the next state will want to
        /// read). Two transitions out of one state may route the same output to different keys, or
        /// one of them to nothing — the "attack succeeded" edge stores the damage, the "target lost"
        /// edge does not — and a task cannot express that about itself.
        ///
        /// Applied by <see cref="StateTreeExecutor"/> after the transition has fired and before the
        /// state is exited, so the values are on the blackboard before the next state's first
        /// OnEnter.
        /// </summary>
        public List<TransitionOutputRoute> outputRoutes = new List<TransitionOutputRoute>();
    }

    /// <summary>
    /// ONE wire from a finished task's named output to a blackboard key, carried by the transition
    /// that fires (M7j) — the assignment half of "the task returns a value".
    ///
    /// THE TASK IS AN INDEX and the output a NAME, and the asymmetry is deliberate. The task is
    /// identified positionally because it is an element of <see cref="StateTreeNodeAsset.tasks"/>
    /// with no identity of its own, exactly as <see cref="StateTreeFieldBinding.targetIndex"/> is —
    /// and the same Ops-layer remapping keeps it pointing at the same task when the list is
    /// reordered. The OUTPUT is named because a name is what the task's author published: a
    /// <c>[TaskOutput]</c> field name or a graph's typed-in Set Output name, neither of which has an
    /// id to bind to, and both of which are contracts a rename is supposed to break loudly rather
    /// than survive quietly (see <see cref="TaskOutputAttribute"/>).
    /// </summary>
    [Serializable]
    public sealed class TransitionOutputRoute
    {
        /// <summary>Index into the SOURCE state's <c>tasks</c> — the task whose return value this
        /// row reads. Out of range, or a task that did not finish, is one warning and the row is
        /// skipped: the transition still fires, because a missing return is not a reason to strand
        /// the tree in the state it was leaving.</summary>
        public int taskIndex;

        /// <summary>The output's contract name, as the task publishes it. Matched
        /// ordinally.</summary>
        public string outputName;

        /// <summary>Blackboard key to write. EMPTY MEANS <see cref="outputName"/>, so the common
        /// case — route "damage" to "damage" — needs nothing typed, and a key is only spelled out
        /// when the author actually wants a different name from the one the task returned.</summary>
        public string blackboardKey;

        /// <summary>The key this row writes: <see cref="blackboardKey"/>, or the output's own name
        /// when none is given. One place, because the inspector shows it as a placeholder and the
        /// executor writes it, and a disagreement between those two would be invisible.</summary>
        public string ResolvedKey()
        {
            return string.IsNullOrEmpty(blackboardKey) ? outputName : blackboardKey;
        }

        /// <summary>How this row is named in a diagnostic — a warning nobody can trace back to a row
        /// is not worth logging.</summary>
        public string Describe()
        {
            string named = string.IsNullOrEmpty(outputName) ? "(unnamed)" : "'" + outputName + "'";
            return "output " + named + " of task " + taskIndex;
        }

        /// <summary>A by-value copy of a route list, for <see cref="StateTreeAsset.DeepCopy"/>.
        /// Lives here rather than there because these are the rows' own semantics: the executor runs
        /// a COPY of the tree, so a route that did not survive the copy would be a route that never
        /// fires, and the copy owns its rows so nothing it does can reach the authored asset.</summary>
        public static List<TransitionOutputRoute> CopyList(List<TransitionOutputRoute> routes)
        {
            if (routes == null)
                return new List<TransitionOutputRoute>();
            var copy = new List<TransitionOutputRoute>(routes.Count);
            for (int i = 0; i < routes.Count; i++)
            {
                TransitionOutputRoute route = routes[i];
                copy.Add(route == null ? null : new TransitionOutputRoute
                {
                    taskIndex = route.taskIndex,
                    outputName = route.outputName,
                    blackboardKey = route.blackboardKey
                });
            }
            return copy;
        }
    }

}
