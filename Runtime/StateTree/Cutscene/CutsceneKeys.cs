namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The names a cutscene answers to. One place, because a request is written by whoever
    /// asks (a trigger, an objective, a dialog node) and read by the def that declares it.
    /// </summary>
    public static class CutsceneKeys
    {
        /// <summary>Play one. Value = the cutscene row's name.</summary>
        [ServiceRequestKey]
        public const string Play = "cutscene.play";

        /// <summary>Skip whatever is playing. Value is ignored.</summary>
        [ServiceRequestKey]
        public const string Skip = "cutscene.skip";

        /// <summary>Set on the ROOT board for as long as a scene is playing, holding the name
        /// of the scene. Its presence IS the mode — the player's tree reads it, exactly as the
        /// boarding key works for water.</summary>
        public const string Playing = "cutscene.playing";

        /// <summary>The standing tag an actor holds while a scene has the controls; land verbs
        /// refuse on it through AbilityDef.blockedByTags.</summary>
        public const string WatchingTag = "Watching";
    }
}
