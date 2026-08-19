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

        [Tooltip("WHICH zone this volume is — picked from the zone registry. The manifest "
            + "sets it from the placement's entry (the placer pattern); a hand-placed "
            + "volume picks it here. The row's ID becomes this citizen's tag.")]
        public StateTreeEntryRef<ZoneDef> zone = new StateTreeEntryRef<ZoneDef>();

        // WHAT IT IS CALLED IS AUTHORED, NOT DERIVED (M31). This used to invent two tags in
        // OnEnable — "zone" and the picked row's id — which made a volume findable by a word
        // no vocabulary held and no map could see. The def wears "zone" and the PLACEMENT wears
        // which zone it is, so the objective pointing at it and the thing carrying it are two
        // picks from one list.
    }
}
