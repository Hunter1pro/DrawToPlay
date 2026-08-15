using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ATTRIBUTE, AS A REGISTRY ROW — the answer to "is an attribute C# or data": the
    /// VOCABULARY is data, the rules are code. A row is what an effect PICKS (the effect
    /// registry lists the attribute registry in dependsOn), what a parameter or key can name,
    /// and what a component seeds from — so wiring "stamina" into a new effect is a pick, not
    /// a recompile, and a misspelled attribute is not representable. The behaviour of a value
    /// (clamping, modifiers, health's guard window and death) lives in
    /// <see cref="AttributeComponent"/> and its domain facades, where behaviour belongs.
    /// </summary>
    [Serializable]
    public sealed class AttributeDef : StateTreeRegistryEntry
    {
        [Tooltip("Shown in UI; the row's name stays the runtime key.")]
        public string displayName = "";

        [Tooltip("The value an actor starts with when nothing overrides it — a seed or a "
            + "domain component (health's maxHP) usually does.")]
        public float baseValue = 100f;

        [Tooltip("What this attribute MEANS — shown wherever the row is offered.")]
        public string description = "";
    }
}
