using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHICH DEF THIS BODY IS (M30.4) — the def on top of the object, made findable from the
    /// object.
    ///
    /// M30.3 made the def spawn and control the body; this is the other direction, and the two
    /// together are what "the lowest level object IS a def" actually means at runtime. A task
    /// standing on a door can now ask what a door is — its requests, its attributes, its
    /// promises — without a component per kind of thing and without the caller having been told
    /// in advance.
    ///
    /// Stamped by <see cref="ServiceBodyFactory"/> and by nobody else, because a body that
    /// claims a def that did not build it would answer questions about a thing it is not.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ServiceBodyBinding : MonoBehaviour
    {
        [Tooltip("The def that built this body. Read-only in practice — the factory writes it.")]
        public ServiceDef def;

        /// <summary>The def behind an object, or null when nothing built it from one — a scene
        /// object, or a body from before the def owned it.</summary>
        public static ServiceDef Of(GameObject body)
        {
            if (body == null)
                return null;
            var binding = body.GetComponentInParent<ServiceBodyBinding>();
            return binding != null ? binding.def : null;
        }
    }
}
