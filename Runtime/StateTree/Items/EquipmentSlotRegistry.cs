using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The character's slots as a catalog — listed in the item registry's
    /// dependsOn so item rows pick their slot.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Equipment Slot Registry",
        fileName = "EquipmentSlotRegistry")]
    public sealed class EquipmentSlotRegistry : StateTreeRegistry<EquipmentSlotDef>
    {
    }
}
