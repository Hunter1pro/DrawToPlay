namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT A SCENE CAME TO (§4d) — the announcement's payload: which one, whether it played,
    /// whether the player skipped it, and why it refused when it did.
    ///
    /// Published on refusals too, for the reason the craft result is: "the keeper is not in
    /// this level" is the single most useful thing a cutscene can say, and a contract that
    /// only reported successes would make the failure travel by some second channel.
    /// </summary>
    public sealed class CutsceneResult
    {
        /// <summary>Where a scene's outcome lands on the root blackboard.</summary>
        public const string Key = "cutscene.last";

        public CutsceneDef cutscene;

        public string cutsceneName = "";

        /// <summary>It ran to the end of its script.</summary>
        public bool played;

        /// <summary>It was cut short by a press — the world is where finishing would leave it.</summary>
        public bool skipped;

        /// <summary>Why it did not start, in words a log or a HUD can show. Empty on success.</summary>
        public string refusal = "";

        /// <summary>The whole outcome as one sentence, for a skin that binds one property.</summary>
        public string line = "";
    }
}
