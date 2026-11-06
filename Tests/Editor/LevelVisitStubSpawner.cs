namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A spawner told which visit it is on — what a game's spawner asks its episode.</summary>
    internal sealed class LevelVisitStubSpawner : ManifestSpawner
    {
        public LevelObjectRegistry given;

        protected override LevelObjectRegistry Visit() => given;
    }
}
