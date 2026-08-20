using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THIS ONE'S NUMBER (M34) — an attribute value set on a PLACEMENT rather than on the def.
    ///
    /// The device idea's missing half. A def says what a kind of thing HAS (M30.4): a timber
    /// stand has health, a door has `open`. Until now the value came from the prefab's seed, so
    /// a tougher stand meant a second prefab or a second def — the thing every editor with
    /// options on the instance exists to avoid.
    ///
    /// A row here says "this one starts at 5". The name is picked from what the placement's KIND
    /// declares, which is the neighbourhood rule again: you may set what the def says it has.
    /// </summary>
    [Serializable]
    public sealed class PlacementAttribute
    {
        [Tooltip("Which attribute — one the placement's kind declares it has.")]
        public string attribute = "";

        [Tooltip("What this one starts at. It sets the BASE, so a pool starts full at this "
            + "number and a modifier still applies on top.")]
        public float value;
    }
}
