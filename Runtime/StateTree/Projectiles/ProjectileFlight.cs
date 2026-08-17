using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE SHOT, IN THE AIR (M28) — HT's projectile, ported to a thing a task ticks.
    ///
    /// The shape is worth keeping exactly: it owns its position and velocity and moves by
    /// SPHERECAST from where it was to where it wants to be, SUB-STEPPED so a fast ball cannot
    /// pass through a hull between frames, ignoring the hierarchy it was fired from so it never
    /// hits its own deck, and with two hard lifetimes so a shot that finds nothing is gone
    /// rather than forever. The model is a clone with its physics switched off, because the
    /// sweep is the truth and the mesh is scenery.
    ///
    /// What it does NOT do is decide what a hit means — no damage, no effects, no death. It
    /// reports what it touched and the ability applies the row, which is the same division the
    /// melee verbs already have and the reason an upgrade to damage never edits this file.
    ///
    /// (HT's version was async over UniTask and could load its prefab through Addressables;
    /// neither exists here, so it is ticked and its row holds the prefab.)
    /// </summary>
    public sealed class ProjectileFlight
    {
        private readonly ProjectileDef m_Row;
        private readonly Transform m_IgnoredRoot;
        private readonly GameObject m_Instance;

        private Vector3 m_Position;
        private Vector3 m_Velocity;
        private Vector3 m_Origin;
        private float m_Lived;

        /// <summary>Still going. False the tick it lands or runs out.</summary>
        public bool inFlight { get; private set; }

        /// <summary>What it hit, or null — the answer the ability is waiting for.</summary>
        public GameObject struck { get; private set; }

        /// <summary>Where it ended, hit or not: the point a splash or a hole belongs at.</summary>
        public Vector3 endedAt => m_Position;

        public ProjectileFlight(ProjectileDef row, Vector3 from, Vector3 velocity,
            Transform ignoredRoot)
        {
            m_Row = row;
            m_IgnoredRoot = ignoredRoot;
            m_Position = from;
            m_Origin = from;
            m_Velocity = velocity;
            inFlight = row != null;

            if (row?.prefab == null)
                return;

            m_Instance = Object.Instantiate(row.prefab, from,
                velocity.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(velocity.normalized)
                    : row.prefab.transform.rotation);
            Silence(m_Instance);
        }

        /// <summary>One frame of flight. Returns true while it is still in the air.</summary>
        public bool Tick(float deltaTime)
        {
            if (!inFlight || m_Row == null || deltaTime <= 0f)
                return inFlight;

            m_Lived += deltaTime;

            m_Velocity += new Vector3(0f, m_Row.gravity, 0f) * deltaTime;
            if (m_Row.drag > 0f)
                m_Velocity *= Mathf.Max(0f, 1f - m_Row.drag * deltaTime);

            // SUB-STEPS SIZED BY THE BALL: never move further than its own radius without
            // looking, which is the whole reason a 40 m/s shot does not pass through a 1 m hull.
            float travel = m_Velocity.magnitude * deltaTime;
            float radius = Mathf.Max(0.01f, m_Row.radius);
            int steps = Mathf.Clamp(Mathf.CeilToInt(travel / radius), 1, Mathf.Max(1, m_Row.maxSubSteps));
            float stepTime = deltaTime / steps;

            for (int i = 0; i < steps; i++)
            {
                if (Step(stepTime))
                    return false;
            }

            if ((m_Position - m_Origin).sqrMagnitude >= m_Row.maxDistance * m_Row.maxDistance
                || m_Lived >= m_Row.maxSeconds)
            {
                Land(null);
                return false;
            }

            if (m_Instance != null)
            {
                m_Instance.transform.position = m_Position;
                if (m_Row.faceVelocity && m_Velocity.sqrMagnitude > 0.0001f)
                    m_Instance.transform.forward = m_Velocity.normalized;
            }
            return true;
        }

        /// <summary>Stop it wherever it is — the scene ended, the shooter died, the level went
        /// away. Symmetry the M27 lesson paid for: whatever starts something owns ending it.</summary>
        public void Cancel()
        {
            if (!inFlight)
                return;
            Land(null);
        }

        private bool Step(float stepTime)
        {
            Vector3 start = m_Position;
            Vector3 end = start + m_Velocity * stepTime;
            Vector3 delta = end - start;
            float distance = delta.magnitude;

            if (distance > 0.0001f
                && Physics.SphereCast(start, Mathf.Max(0.01f, m_Row.radius), delta / distance,
                    out RaycastHit hit, distance, m_Row.hitMask, QueryTriggerInteraction.Ignore))
            {
                // ITS OWN SHIP IS NOT A TARGET. Anything under the hierarchy it was fired from
                // is passed through, which is what lets a gun sit inside its own hull.
                if (!Ignored(hit.collider))
                {
                    m_Position = hit.point;
                    Land(hit.collider != null ? hit.collider.gameObject : null);
                    return true;
                }
            }

            m_Position = end;
            return false;
        }

        private bool Ignored(Collider collider)
        {
            if (collider == null || m_IgnoredRoot == null)
                return false;
            return collider.transform == m_IgnoredRoot
                || collider.transform.IsChildOf(m_IgnoredRoot);
        }

        private void Land(GameObject hit)
        {
            struck = hit;
            inFlight = false;
            if (m_Instance == null)
                return;
            m_Instance.transform.position = m_Position;
            if (Application.isPlaying)
                Object.Destroy(m_Instance);
            else
                Object.DestroyImmediate(m_Instance);
        }

        /// <summary>The clone must not do physics of its own: a rigidbody on the model would
        /// fall, and a collider on it would push whatever it passes. HT disables both, and the
        /// reason is the same here.</summary>
        private static void Silence(GameObject instance)
        {
            if (instance == null)
                return;
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
            }
        }
    }
}
