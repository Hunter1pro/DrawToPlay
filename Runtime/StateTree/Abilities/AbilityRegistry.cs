using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The ability catalog — a registry like any other: list it in a tree's Data
    /// section and ActivateAbilityTask picks rows with ⛃; the dashboard edits it; a
    /// <see cref="ServiceDef"/> names it as the ability service's nouns.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Ability Registry",
        fileName = "AbilityRegistry")]
    public sealed class AbilityRegistry : StateTreeRegistry<AbilityDef>
    {
    }
}
