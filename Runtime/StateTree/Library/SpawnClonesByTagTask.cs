using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Clone a tagged scene object — the spawner that needs no prefab machinery: the TEMPLATE
    /// is a live citizen (the first zombie IS the mold for every later one), the clone lands
    /// scattered beside it, gets a FRESH identity minted (the copied stable id is
    /// re-registered before anything can collide), and its own components take over — a
    /// cloned runner starts its own tree, a cloned health enrolls itself. This is how a wave
    /// escalates: "each cleared wave spawns one more" is this task with count 1 on the
    /// cleared state.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Spawn Clones By Tag",
        fileName = "SpawnClonesByTag")]
    [StateTreeCategory("Tasks/World", "Clone a tagged scene object; fresh id, scattered nearby")]
    public sealed class SpawnClonesByTagTask : StateTreeTaskAsset
    {
        /// <summary>Tag naming the TEMPLATE. Several carriers: the first found is the mold.</summary>
        public string templateTag = "";

        public int count = 1;

        /// <summary>World-unit scatter radius around the template.</summary>
        public float scatterRadius = 1f;

        private bool m_WarnedNoTemplate;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(templateTag) || count <= 0)
                return StateTreeStatus.Failure;

            WorldService world = StateTreeContextHost.FindService<WorldService>(context.owner);
            var bucket = new System.Collections.Generic.List<WorldObjectBehaviour>();
            if (world != null)
                world.CollectByTag(templateTag, bucket);
            WorldObjectBehaviour template = bucket.Count > 0 ? bucket[0] : null;
            if (template == null)
            {
                if (!m_WarnedNoTemplate)
                {
                    m_WarnedNoTemplate = true;
                    Debug.LogWarning("SpawnClonesByTagTask: no template carrying '" + templateTag
                        + "' reachable — nothing to clone.", context.owner);
                }
                return StateTreeStatus.Failure;
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 scatter = Random.insideUnitCircle * scatterRadius;
                GameObject clone = Object.Instantiate(template.gameObject,
                    template.transform.position + new Vector3(scatter.x, scatter.y, 0f),
                    template.transform.rotation);
                clone.name = template.gameObject.name + " (spawned)";

                // The clone registered during Instantiate with the TEMPLATE'S id — a fresh
                // identity has to be minted through the documented re-register gesture before
                // the collision becomes anyone's problem.
                WorldObjectBehaviour citizen = clone.GetComponent<WorldObjectBehaviour>();
                if (citizen != null)
                {
                    citizen.UnregisterFromWorld();
                    citizen.stableId = "";
                    citizen.EnsureStableId();
                    citizen.RegisterToWorld();
                }
            }
            return StateTreeStatus.Success;
        }
    }
}
