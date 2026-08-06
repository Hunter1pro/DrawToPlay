using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Tag component the Decorate stamp tool puts on every object it scatters. It exists for
    /// one reason: the Terrain flow's "terrain.decorate" badge is presence-based
    /// (draw-tool-port-brief.md §6.4.3) and every name- or hierarchy-based way of recognising a
    /// stamp is fragile — renaming a decoration, or dragging it out of the blob, must not make
    /// the stage look empty again.
    ///
    /// Runtime, not editor-only, because the marker travels with the scene: it has no Update,
    /// no allocation and no behaviour at all.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StampMarker : MonoBehaviour
    {
        /// <summary>Project path of the stamp asset this object was scattered from, kept for
        /// provenance ("which brush made this?"). Purely informational.</summary>
        public string sourceAssetPath;
    }
}
