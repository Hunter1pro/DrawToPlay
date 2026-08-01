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

    /// <summary>Authored as a sub-asset with serialized params; subclasses override the
    /// virtuals. Port of state_tree_task.gd — the runner deep-copies the whole tree on
    /// Start (data.duplicate(true) mirror), so instance fields on a task are safe
    /// per-runner state.</summary>
    public abstract class StateTreeTaskAsset : ScriptableObject
    {
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
    }

}
