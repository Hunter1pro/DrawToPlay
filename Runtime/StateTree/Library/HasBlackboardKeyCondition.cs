using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True while THIS tree's blackboard holds a key — the local twin of
    /// <see cref="HasContextKeyCondition"/>, and the branch test of any task that publishes a
    /// fact by writing a key and retracts it by removing one (the TargetDetected idiom;
    /// <see cref="ShowScreenTask"/>'s click result). Graphs have had this as a native node
    /// since M7e; transitions get it here.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Has Blackboard Key",
        fileName = "HasBlackboardKey")]
    [StateTreeCategory("Conditions/Blackboard", "This tree's blackboard holds (or lacks) a key")]
    public sealed class HasBlackboardKeyCondition : StateTreeConditionAsset
    {
        [StateTreeKey(StateTreeKeyKind.Event, any: true)]
        public string key = "";

        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool present = context != null && !string.IsNullOrEmpty(key)
                && context.blackboard.ContainsKey(key);
            return invert ? !present : present;
        }
    }
}
