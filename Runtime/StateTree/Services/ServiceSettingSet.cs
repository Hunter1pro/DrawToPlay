using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE LAYER OF SETTINGS (M36) — the rows this def (or this install) overrides, held in a
    /// wrapper rather than a bare list for the reason every other declared-options set here is:
    /// Unity hands a field attribute to a list's ELEMENTS, and the panel that offers every
    /// declared knob with its default can only exist on one object.
    /// </summary>
    [System.Serializable]
    public sealed class ServiceSettingSet
    {
        /// <summary>The overridden settings, one row each. Absent means "follow the layer
        /// below" — the def for an install, the class default for a def.</summary>
        public List<ServiceSettingValue> values = new List<ServiceSettingValue>();

        public bool isEmpty => values == null || values.Count == 0;

        /// <summary>The row for a setting, or null when this layer does not override it.</summary>
        public ServiceSettingValue Find(string settingName)
        {
            for (int i = 0; values != null && i < values.Count; i++)
            {
                if (values[i] != null && values[i].name == settingName)
                    return values[i];
            }
            return null;
        }
    }
}
