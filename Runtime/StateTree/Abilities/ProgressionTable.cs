using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The balance sheet — level → value per attribute, one page for the whole
    /// world scale so "level 5" means the same thing everywhere that reads this table.
    /// An actor is a table reference plus one int (<see cref="AttributeComponent.level"/>);
    /// a different scale (a boss) is a different asset of this same type, not a special
    /// case. Lists the attribute registry in dependsOn.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Progression Table",
        fileName = "ProgressionTable")]
    public sealed class ProgressionTable : StateTreeRegistry<ProgressionRow>
    {
    }
}
