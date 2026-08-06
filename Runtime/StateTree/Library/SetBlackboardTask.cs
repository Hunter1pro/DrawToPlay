using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Writes one blackboard entry and Succeeds — brief §7.2's "SetBlackboard". Small, and the
    /// reason several other components can stay small: it is how a preset SEEDS the locked
    /// tuning keys ("moveSpeed", "attackRange", "detectRange") in an entry state instead of
    /// every component growing a duplicate of the same serialized float, and how a counter
    /// that <see cref="BlackboardCompareCondition"/> reads gets initialised.
    ///
    /// M6 conventions prefer serialized fields on the components themselves, so this is the
    /// EXCEPTION path, not the default one: reach for it when a value must be shared by
    /// several tasks (or changed by one and read by another), not to avoid setting a field.
    ///
    /// VALUE KINDS: float and string, matching what the blackboard is actually compared
    /// against elsewhere. Object references are excluded on purpose — a serialized
    /// UnityEngine.Object on a sub-asset would create a hard reference from a preset tree into
    /// scene content, and the one cross-task object reference this library needs ("target") is
    /// owned by <see cref="TargetDetectedCondition"/>.
    ///
    /// Clearing is a first-class operation (<see cref="ValueKind.Clear"/>): removing a key so
    /// a condition falls back to its "absent" branch is how a state resets a timer or drops a
    /// target, and there is no float that means "absent".
    ///
    /// Stateless, therefore Cancelled-safe with no teardown: the write already happened or it
    /// did not.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Set Blackboard", fileName = "SetBlackboard")]
    [StateTreeCategory("Tasks/Blackboard", "Write a blackboard value")]
    public sealed class SetBlackboardTask : StateTreeTaskAsset
    {
        public enum ValueKind
        {
            Float,
            String,

            /// <summary>Remove the key entirely.</summary>
            Clear
        }

        [StateTreeKey(StateTreeKeyKind.Float, any: true)]
        public StateTreeKeyField key = new StateTreeKeyField();

        public ValueKind kind = ValueKind.Float;

        public float floatValue;

        public string stringValue = "";

        /// <summary>Only write when the key is absent — "seed a default without stomping
        /// whatever the running tree has already put there".</summary>
        public bool onlyIfMissing;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(key))
                return StateTreeStatus.Failure;

            if (onlyIfMissing && context.blackboard.ContainsKey(key))
                return StateTreeStatus.Success;

            switch (kind)
            {
                case ValueKind.Float:
                    context.blackboard[key] = floatValue;
                    break;
                case ValueKind.String:
                    context.blackboard[key] = stringValue;
                    break;
                default:
                    context.blackboard.Remove(key);
                    break;
            }
            return StateTreeStatus.Success;
        }
    }
}
