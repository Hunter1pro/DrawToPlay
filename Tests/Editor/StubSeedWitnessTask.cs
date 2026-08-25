namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Task stub that says what the board held AT THE MOMENT IT RAN — one entry per
    /// <c>OnEnter</c> and one per <c>OnTick</c>, each carrying the value it found under
    /// <see cref="watchKey"/>, or <c>(nothing)</c>.
    ///
    /// The point is the ordering, so what it records is not "did the key arrive" (a later tick
    /// would say yes either way) but "was it there when this state began". A run whose first
    /// entry reads <c>(nothing)</c> is a state that ticked before the parameters its scope
    /// declares were on the board — the defect this stub exists to fail on.
    ///
    /// Configuration is serialized, because the runner deep-copies the tree and only serialized
    /// state survives; the log rides the context, for the same reason
    /// (<see cref="StateTreeTestLog"/>).
    /// </summary>
    internal sealed class StubSeedWitnessTask : StateTreeTaskAsset
    {
        /// <summary>The seeded key this task claims to need.</summary>
        public string watchKey = "mode";

        public override void OnEnter(StateTreeContext context)
        {
            StateTreeTestLog.Record(context, "enter:" + Saw(context));
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            StateTreeTestLog.Record(context, "tick:" + Saw(context));
            return StateTreeStatus.Running;
        }

        private string Saw(StateTreeContext context)
        {
            if (context == null || context.blackboard == null)
                return "(nothing)";
            return context.blackboard.TryGetValue(watchKey, out object held) && held != null
                ? held.ToString()
                : "(nothing)";
        }
    }
}
