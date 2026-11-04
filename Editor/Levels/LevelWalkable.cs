using UnityEngine;
using UnityEngine.AI;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>Whether a point stands on the walkable area of the scenes open in the Editor — their NavMesh,
    /// loaded with the scene. Within <see cref="Reach"/> across, and a storey above or below.</summary>
    public static class LevelWalkable
    {
        public enum Standing
        {
            OnIt,
            OffIt,
            NoWalkableArea
        }

        /// <summary>How far across a point may be from the walkable area and still stand on it.</summary>
        public const float Reach = 1f;

        private const float Storey = 2f;

        private const float Search = 500f;

        /// <summary>Where <paramref name="world"/> stands, and the nearest walkable point (itself when there is none).</summary>
        public static Standing Of(Vector3 world, out Vector3 nearest)
        {
            if (NavMesh.SamplePosition(world, out NavMeshHit hit, Storey, NavMesh.AllAreas) && Across(world, hit.position) <= Reach)
            {
                nearest = hit.position;
                return Standing.OnIt;
            }
            if (NavMesh.SamplePosition(world, out hit, Search, NavMesh.AllAreas))
            {
                nearest = hit.position;
                return Standing.OffIt;
            }
            nearest = world;
            return Standing.NoWalkableArea;
        }

        public static Standing Of(Vector3 world) => Of(world, out _);

        /// <summary>The distance on the ground, height left out.</summary>
        public static float Across(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
