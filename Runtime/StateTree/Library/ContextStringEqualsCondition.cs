using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True while a context scope's key holds exactly this string — the equality half the
    /// scoped conditions were missing, and the test a TOGGLE branches on: "is the clicked
    /// weapon the one already equipped?" is this condition with <see cref="value"/> ⚑-bound to
    /// the click, ordered before the plain equip transition. Absent key or a non-string value
    /// is simply false (never equal to anything), inverted or not.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Context String Equals",
        fileName = "ContextStringEquals")]
    [StateTreeCategory("Conditions/Context", "A context-scope key equals this string")]
    public sealed class ContextStringEqualsCondition : StateTreeConditionAsset
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        public string scopeId = "";

        /// <summary>Key on the SCOPE's blackboard.</summary>
        [StateTreeKey(StateTreeKeyKind.String)]
        public string key = "";

        /// <summary>The string to compare against — bindable, so a routed value (the clicked
        /// item) can be the comparand.</summary>
        public string value = "";

        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool equal = false;
            if (context != null && !string.IsNullOrEmpty(key))
            {
                StateTreeContextHost host =
                    StateTreeContextHost.Resolve(context.owner, scope, scopeId);
                equal = host != null
                    && host.Context.blackboard.TryGetValue(key, out object held)
                    && held is string text
                    && string.Equals(text, value, StringComparison.Ordinal);
            }
            return invert ? !equal : equal;
        }
    }
}
