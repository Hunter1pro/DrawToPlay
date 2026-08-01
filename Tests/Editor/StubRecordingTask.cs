namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Task stub that records every lifecycle call into the context log and finishes on a
    /// configurable tick. All configuration lives in serialized fields so it survives the
    /// runner's DeepCopy (Object.Instantiate copies serialized state only).
    /// </summary>
    internal sealed class StubRecordingTask : StateTreeTaskAsset
    {
        /// <summary>Prefix of every log entry this task writes.</summary>
        public string taskId = "task";

        /// <summary>1-based tick index at which OnTick returns <see cref="finishStatus"/>.
        /// Zero or less = never finishes (stays Running forever, the interruptible case).</summary>
        public int finishOnTick;

        /// <summary>Status returned once <see cref="finishOnTick"/> is reached.</summary>
        public StateTreeStatus finishStatus = StateTreeStatus.Success;

        /// <summary>Per-instance state. Two runners sharing one authored asset must never share
        /// this — and the AUTHORED instance must stay at zero, because only the deep copy runs.</summary>
        public int enterCount;
        public int tickCount;
        public int exitCount;

        public override void OnEnter(StateTreeContext context)
        {
            enterCount++;
            tickCount = 0;
            StateTreeTestLog.Record(context, taskId + ":enter");
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            tickCount++;
            StateTreeTestLog.Record(context, taskId + ":tick" + tickCount);
            if (finishOnTick > 0 && tickCount >= finishOnTick)
                return finishStatus;
            return StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            exitCount++;
            StateTreeTestLog.Record(context, taskId + ":exit:" + status);
        }
    }
}
