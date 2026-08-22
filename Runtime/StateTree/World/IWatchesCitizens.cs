namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A SERVICE THAT WANTS TO KNOW WHO IS IN THE WORLD (M40.4, meta-rule 5) — the combat
    /// service watches every citizen's health pool for the crossing that is a death. The world
    /// tells every such service reachable from its scope as a citizen registers and
    /// unregisters: a call at the moment, no subscription to keep, no pair to balance.
    /// </summary>
    public interface IWatchesCitizens
    {
        void CitizenAdded(WorldObjectBehaviour citizen);

        void CitizenRemoved(WorldObjectBehaviour citizen);
    }
}
