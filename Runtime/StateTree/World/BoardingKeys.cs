using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The names the boarding flow speaks (M26). One place, because a mode is written by
    /// one task and read by a transition, a condition and a save — four sites that must
    /// agree or the actor is aboard in one of them and ashore in the others.
    /// </summary>
    public static class BoardingKeys
    {
        /// <summary>Set while the actor is afloat. Its presence IS the mode.</summary>
        public const string Aboard = "mode.aboard";

        /// <summary>The ability-gating tag granted while afloat — what stops a land verb
        /// from firing at sea without any task knowing about boats.</summary>
        public const string AboardTag = "Aboard";

        /// <summary>Where the actor was standing when it embarked, so disembarking (or
        /// drowning) has somewhere to put it back.</summary>
        public const string LastGround = "mode.lastGround";
    }
}
