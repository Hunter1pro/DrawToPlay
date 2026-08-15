using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The effect catalog. The ability registry lists it in dependsOn (an ability's
    /// tree applies its rows); this registry lists the CUE registry in its own dependsOn (its
    /// rows show them) — provenance as a chain of declarations, never a typed name.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Effect Registry",
        fileName = "EffectRegistry")]
    public sealed class EffectRegistry : StateTreeRegistry<EffectDef>
    {
    }
}
