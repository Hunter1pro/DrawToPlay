using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The recipe catalog — and the M13 economics claimed at the item registry, collected:
    /// a new registry KIND is its entry class plus this line, and the dashboard, the tree
    /// header's Data list and every entry picker come from the base by reflection. Not one
    /// line of editor code was written for crafting.
    ///
    /// Its <c>dependsOn</c> names the ITEM registry, which is what makes a recipe's cost and
    /// result pickers offer the item catalog rather than a typed word.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Craft Recipe Registry",
        fileName = "CraftRecipeRegistry")]
    public sealed class CraftRecipeRegistry : StateTreeRegistry<CraftRecipeDef>
    {
    }
}
