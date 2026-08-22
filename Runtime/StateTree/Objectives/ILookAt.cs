namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT "SHOW ME" MEANS (M40.4, meta-rule 5). The quest line names a body and stops — it
    /// has no camera and should never grow one. The game provides the answer under this
    /// capability (a camera peek, a minimap ping, a shout) and the quest line calls it.
    /// </summary>
    public interface ILookAt
    {
        void LookAt(WorldObjectBehaviour target);
    }
}
