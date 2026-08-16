using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WATER, AS A PLACE (M26) — a citizen with an extent, tagged like every other thing
    /// in the world, that can answer one question: is this point in me?
    ///
    /// Deliberately not a trigger collider with events. A trigger would make water know
    /// who entered it and when, which is the wrong way round: the actors ask, each on
    /// their own tick, exactly as MoveTo objectives ask a zone whether they have arrived.
    /// That keeps the volume inert — a level can have five, an actor can care about none
    /// of them — and it means an AI that plans a route can ask about a point it is not
    /// standing on yet, which an event never allows.
    ///
    /// The Y axis is deliberately generous: a boat sits on a surface, a walker stands on
    /// the bed, and neither should fall out of the volume because of a step height.
    /// </summary>
    [AddComponentMenu("Draw To Play/World/Water Volume")]
    public sealed class WaterVolumeBehaviour : WorldObjectBehaviour
    {
        [Tooltip("The volume's extent in world units, centred on this object.")]
        public Vector3 size = new Vector3(20f, 6f, 20f);

        [Tooltip("The tag actors look for. A level may hold several volumes; they all "
            + "answer to this one name.")]
        public string waterTag = "water";

        protected override void OnEnable()
        {
            // Describe first, register after — the world's add event must see a finished
            // citizen (the M18 rule).
            EnsureTag(waterTag);
            base.OnEnable();
        }

        /// <summary>
        /// Whether a world point is inside this volume — in METRES, whatever the object's
        /// scale.
        ///
        /// Not InverseTransformPoint: that divides by lossy scale, so a volume drawn by a
        /// scaled quad (a Unity plane is ten units wide, so it is always scaled) measured
        /// its own size in the mesh's units and came out a third of the authored figure.
        /// The size on this component is world metres and stays world metres; only the
        /// rotation is taken from the transform.
        /// </summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation)
                * (worldPoint - transform.position);
            Vector3 half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x
                && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>
        /// Where a boat sits: THE WATER'S OWN HEIGHT.
        ///
        /// Not the top of the box. The box is deliberately tall so a walker on the bed and
        /// a hull on the surface are both "in the water" for detection — reading its top as
        /// the surface floated the boat two metres above the visible plane, sailing through
        /// the air over its own reflection.
        /// </summary>
        public float SurfaceY => transform.position.y + surfaceOffset;

        [Tooltip("Lift applied to whatever floats here — how deep the hull sits.")]
        public float surfaceOffset = 0.05f;

        /// <summary>The water volume containing a point, or null — asked by whoever is
        /// deciding whether they are afloat. By TAG through the world, so a level's water
        /// is found the same way its enemies are.</summary>
        public static WaterVolumeBehaviour At(GameObject asker, Vector3 worldPoint,
            string tag = "water")
        {
            WorldService world = StateTreeContextHost.FindService<WorldService>(asker);
            if (world == null)
                return null;
            var found = new System.Collections.Generic.List<WorldObjectBehaviour>();
            world.CollectByTag(tag, found);
            for (int i = 0; i < found.Count; i++)
            {
                var volume = found[i] != null ? found[i].As<WaterVolumeBehaviour>() : null;
                if (volume != null && volume.Contains(worldPoint))
                    return volume;
            }
            return null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.6f, 0.9f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, size);
        }
    }
}
