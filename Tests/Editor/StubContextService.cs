namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>The smallest possible §3.7 service atom — exists so the tests can prove the
    /// connect/lookup path without pretending to be a real service. Own file, like every
    /// concrete MonoBehaviour here, so the type binds to a MonoScript cleanly.</summary>
    internal sealed class StubContextService : StateTreeServiceBehaviour
    {
        public int pings;

        public void Ping() => pings++;
    }
}
