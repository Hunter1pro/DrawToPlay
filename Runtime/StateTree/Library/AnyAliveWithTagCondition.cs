using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True while any world object carrying the tag is ALIVE — the win/lose test of a combat
    /// circle as one condition: "wave cleared" is this with <see cref="invert"/> on, guarding
    /// the fight state's interrupt. The registry answers WHO carries the tag,
    /// the health pool answers whether they still count; an object with no health at all
    /// counts as alive (a lever is never "dead" — the same rule target validity uses).
    ///
    /// Quiet like every per-tick condition; the tasks that arm the wave carry the wiring
    /// warnings.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Any Alive With Tag",
        fileName = "AnyAliveWithTag")]
    [StateTreeCategory("Conditions/World", "Any world object with this tag is still alive")]
    public sealed class AnyAliveWithTagCondition : StateTreeConditionAsset
    {
        public string tag = "";

        /// <summary>True while NONE is left alive instead — "wave cleared".</summary>
        public bool invert;

        private readonly List<WorldObjectBehaviour> m_Buffer = new List<WorldObjectBehaviour>();

        public override bool Evaluate(StateTreeContext context)
        {
            bool anyAlive = false;
            if (context != null && !string.IsNullOrEmpty(tag))
            {
                WorldService world =
                    StateTreeContextHost.FindService<WorldService>(context.owner);
                if (world != null)
                {
                    m_Buffer.Clear();
                    world.CollectByTag(tag, m_Buffer);
                    for (int i = 0; i < m_Buffer.Count; i++)
                    {
                        if (m_Buffer[i] == null)
                            continue;
                        HealthComponent health = StateTreeLibraryUtil
                            .ResolveComponent<HealthComponent>(m_Buffer[i].gameObject);
                        if (health == null || health.isAlive)
                        {
                            anyAlive = true;
                            break;
                        }
                    }
                }
            }
            return invert ? !anyAlive : anyAlive;
        }
    }
}
