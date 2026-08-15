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
        /// <summary>The highest level any row speaks for — "this world is balanced up to
        /// here". Levels past it are legal and hold every curve's last value; consumers
        /// that assign levels read this to SAY so instead of flattening silently.</summary>
        public int maxLevel
        {
            get
            {
                var max = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null && entries[i].lastLevel > max)
                        max = entries[i].lastLevel;
                }
                return max;
            }
        }
    }
}
