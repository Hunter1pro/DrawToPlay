using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Compares a numeric blackboard entry against a constant — brief §7.2's "Blackboard
    /// compare". The escape hatch that keeps the library from growing a bespoke condition per
    /// gameplay counter: a task writes "shotsFired" or "aggroLevel" with
    /// <see cref="SetBlackboardTask"/>, and this reads it back as a transition gate.
    ///
    /// A MISSING OR NON-NUMERIC KEY IS FALSE, always — the library's null-safety rule. It is
    /// tempting to treat an absent key as 0 so that "&lt; 3" passes on a fresh entity, but
    /// then a typo in the key name reads as a satisfied condition and the tree silently takes
    /// the wrong branch forever. Absent means "cannot answer", and cannot-answer is false.
    /// Use <see cref="SetBlackboardTask"/> in the entry state to seed counters explicitly.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Blackboard Compare",
        fileName = "BlackboardCompare")]
    [StateTreeCategory("Conditions/Blackboard", "Numeric blackboard key vs constant")]
    public sealed class BlackboardCompareCondition : StateTreeConditionAsset
    {
        public enum Operator
        {
            Equal,
            NotEqual,
            Less,
            LessOrEqual,
            Greater,
            GreaterOrEqual
        }

        public string key = "";

        public Operator op = Operator.GreaterOrEqual;

        public float value;

        /// <summary>Tolerance for <see cref="Operator.Equal"/> / <see cref="Operator.NotEqual"/>.
        /// Float equality on a value that was lerped or accumulated is a coin toss; the
        /// epsilon makes the comparison mean what the author meant.</summary>
        public float epsilon = 0.0001f;

        public override bool Evaluate(StateTreeContext context)
        {
            if (!StateTreeLibraryUtil.TryGetFloat(context, key, out float current))
                return false;

            switch (op)
            {
                case Operator.Equal:
                    return Mathf.Abs(current - value) <= epsilon;
                case Operator.NotEqual:
                    return Mathf.Abs(current - value) > epsilon;
                case Operator.Less:
                    return current < value;
                case Operator.LessOrEqual:
                    return current <= value;
                case Operator.Greater:
                    return current > value;
                default:
                    return current >= value;
            }
        }
    }
}
