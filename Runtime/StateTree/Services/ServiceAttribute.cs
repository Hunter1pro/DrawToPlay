using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT A DEF HAS (M30.4) — one attribute it declares, and how much of it it lets anybody
    /// touch.
    ///
    /// This is the half of a def's API that nobody should have to type. A door with `open`, a
    /// tree with `health`, a lamp with `fuel`: the requests that read and change those are the
    /// same three sentences every time, so the def declares the DATA and the request rows are
    /// derived from it (<see cref="ServiceDef.DerivedRequests"/>). Hand-written rows stay for
    /// verbs that are not attribute-shaped — "play this scene" is not a number anybody sets.
    ///
    /// <see cref="writable"/> IS THE PERMISSION, and it exists because the alternative was
    /// called out as the trap this feature could fall into: a generated "set health" row implies
    /// a right that nothing enforces, and an API that implies rights it does not check is
    /// decoration. Read-only derives the ask and nothing else, and the runtime refuses the rest
    /// by the same rule the editor showed.
    /// </summary>
    [Serializable]
    public sealed class ServiceAttribute
    {
        [Tooltip("The attribute row — picked from a catalog this def's registry declares.")]
        public StateTreeEntryRef<AttributeDef> attribute = new StateTreeEntryRef<AttributeDef>();

        [Tooltip("May callers change it? Off derives only the ask — the permission, checked at "
            + "runtime as well as shown here.")]
        public bool writable = true;

        [Tooltip("What it means on THIS kind of thing, when the catalog's own description is "
            + "not the whole story.")]
        public string description = "";

        /// <summary>The name callers use — the attribute row's, which is what every runtime
        /// read already goes by.</summary>
        public string Name => attribute != null ? attribute.entryName : "";
    }
}
