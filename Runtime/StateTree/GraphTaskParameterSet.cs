using System;
using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE CALLER'S VALUES for a graph program's parameters — the Blueprint instance model
    /// (<see cref="GraphTaskParameterOverride"/>) made available OUTSIDE a state tree.
    ///
    /// WHY IT EXISTS. A state that runs a program gets a Parameters section in the tree inspector:
    /// the program declares knobs with defaults, the state ticks the ones it cares about. Anything
    /// ELSE that runs a program — a registry row naming an NPC's conversation, a component, a
    /// level's script — had no such thing, so one authored graph could only ever be used one way.
    /// Declare a field of this type beside the program reference, mark it
    /// <see cref="GraphTaskParametersAttribute"/>, and the same rows appear wherever Unity draws
    /// that field.
    ///
    /// IT IS A HOLDER AROUND A LIST, ON PURPOSE. The rows need a drawer, and a
    /// <c>PropertyAttribute</c> on a <c>List&lt;&gt;</c> field is applied per ELEMENT — which would
    /// draw one parameter section per override instead of one for the whole set. A named type takes
    /// a type drawer, which has no such ambiguity.
    ///
    /// AN OVERRIDE IS PER USE. This never lives on the program: writing a value there would change
    /// it for every other caller, which is the one thing the instance model exists to prevent. The
    /// runtime applies it with <see cref="GraphTaskAsset.ApplyOverrides"/> on the per-activation
    /// copy, exactly as <c>RunGraphTask</c> does.
    /// </summary>
    [Serializable]
    public sealed class GraphTaskParameterSet
    {
        /// <summary>The overridden knobs, one row each, bound to a declaration BY ID so the
        /// program's author can rename a parameter without reaching in here. Rows for knobs
        /// nobody touched are not stored — absent means "follow the program's default".</summary>
        public List<GraphTaskParameterOverride> values = new List<GraphTaskParameterOverride>();

        /// <summary>True when nothing is overridden and the program runs exactly as authored.
        /// Cheap enough to call per activation, which is where it is used to skip the apply.</summary>
        public bool isEmpty => values == null || values.Count == 0;
    }
}
