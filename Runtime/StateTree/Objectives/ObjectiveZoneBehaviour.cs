using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A PLACE AN OBJECTIVE MEANS — a world citizen whose tags make it findable
    /// (placements add their row tags; <see cref="ObjectiveDef.targetTag"/> is how a MoveTo
    /// names it) and whose radius says what "arrived" is. Zero, one or many may carry the
    /// same tag: the service targets the NEAREST and arriving at ANY completes. Zones are
    /// never nested — a zone is a disc on the ground, not a hierarchy.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Objective Zone")]
    public sealed class ObjectiveZoneBehaviour : WorldObjectBehaviour
    {
        [Tooltip("What 'arrived' means here, on the ground plane.")]
        public float radius = 1.5f;

        protected override void OnEnable()
        {
            // Tags are fixed at registration — declare the base identity before base
            // registers this citizen. Placement rows add the objective-facing tag.
            if (!tags.Contains("zone"))
                tags.Add("zone");
            base.OnEnable();
        }
    }
}
