using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A number on a context scope's blackboard compared against a constant — the scoped twin of
    /// <see cref="BlackboardCompareCondition"/>, and how a routed step reads an answer an earlier level
    /// lifted to the root. An absent or non-numeric key is false, as there.
    /// </summary>
    [StateTreeCategory("Conditions/Context", "A context-scope number vs constant")]
    public sealed class ContextNumberCompareCondition : StateTreeConditionAsset
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        [ScopeId] public string scopeId = "";

        /// <summary>Key on the SCOPE's blackboard.</summary>
        [StateTreeKey(StateTreeKeyKind.Float)]
        public StateTreeKeyField key = new StateTreeKeyField();

        public BlackboardCompareCondition.Operator op = BlackboardCompareCondition.Operator.Equal;

        public float value;

        public float epsilon = 0.0001f;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null || string.IsNullOrEmpty(key))
                return false;
            StateTreeContextHost host = StateTreeContextHost.Resolve(context.owner, scope, scopeId);
            if (host == null || !host.Context.blackboard.TryGetValue(key, out object held))
                return false;
            float current;
            switch (held)
            {
                case float f: current = f; break;
                case int i: current = i; break;
                case double d: current = (float)d; break;
                default: return false;
            }
            switch (op)
            {
                case BlackboardCompareCondition.Operator.Equal: return Mathf.Abs(current - value) <= epsilon;
                case BlackboardCompareCondition.Operator.NotEqual: return Mathf.Abs(current - value) > epsilon;
                case BlackboardCompareCondition.Operator.Less: return current < value;
                case BlackboardCompareCondition.Operator.LessOrEqual: return current <= value;
                case BlackboardCompareCondition.Operator.Greater: return current > value;
                default: return current >= value;
            }
        }
    }
}
