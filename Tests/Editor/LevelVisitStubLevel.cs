namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A Level node's data that may be a visit: its level's name and its own manifest.</summary>
    internal sealed class LevelVisitStubLevel : StateTreeTaskAsset, IStateTreeLevelVisit
    {
        public string level = "";

        public LevelObjectRegistry manifest;

        public string levelName => level;

        public LevelObjectRegistry visitManifest
        {
            get => manifest;
            set => manifest = value;
        }
    }
}
