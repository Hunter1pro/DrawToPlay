using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A PART THAT CAN WEAR A COLOUR (M30.3) — the one seam the def-owned body needs into a
    /// game's own look.
    ///
    /// The def can say "this object wears the colour of the row it is an instance of", and that
    /// sentence has to survive the fact that HOW a thing is coloured is entirely the game's
    /// business: a property block here, a material swap there, a shader parameter somewhere else.
    /// So the def names the part and the part is asked; nothing in this assembly knows what a
    /// renderer is.
    /// </summary>
    public interface IWorldTintable
    {
        /// <summary>Wear this colour. Called while the object is still inactive, so anything the
        /// implementation defers to OnEnable has to be applied here too.</summary>
        void SetTint(Color tint);
    }
}
