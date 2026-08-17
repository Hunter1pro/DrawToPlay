using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The catalog of things that fly — one row per ball, bolt or bomb, picked by
    /// name from the ability that launches it.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Projectile Registry",
        fileName = "ProjectileRegistry")]
    public sealed class ProjectileRegistry : StateTreeRegistry<ProjectileDef>
    {
    }
}
