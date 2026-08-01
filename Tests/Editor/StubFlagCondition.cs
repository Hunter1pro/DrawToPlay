namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Condition stub driven by a bool on the runner's blackboard. Blackboard-driven rather than
    /// field-driven because the runner deep-copies conditions on StartTree — flipping a field on
    /// the authored asset would never reach the running copy, while the context is shared by
    /// reference. Missing / non-bool value reads as false.
    /// </summary>
    internal sealed class StubFlagCondition : StateTreeConditionAsset
    {
        /// <summary>Blackboard key holding the bool this condition mirrors.</summary>
        public string flagKey = "flag";

        /// <summary>Invert the flag (lets one key drive two opposed transitions).</summary>
        public bool invert;

        /// <summary>Record every evaluation into the context log — used to prove that interrupts
        /// are evaluated BEFORE task ticks within one runner tick.</summary>
        public bool recordEvaluations;

        public override bool Evaluate(StateTreeContext context)
        {
            bool raised = false;
            if (context != null)
            {
                object value;
                if (context.blackboard.TryGetValue(flagKey, out value) && value is bool)
                    raised = (bool)value;
            }
            bool result = raised != invert;
            if (recordEvaluations)
                StateTreeTestLog.Record(context, "cond:" + flagKey + (result ? ":true" : ":false"));
            return result;
        }
    }
}
