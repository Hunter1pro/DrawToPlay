using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>A list of parameter ids, picked from the tree a sibling field holds — a preset's settings.</summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class PresetSettingsAttribute : PropertyAttribute
    {
        public readonly string treeField;

        public PresetSettingsAttribute(string treeField)
        {
            this.treeField = treeField;
        }
    }

    /// <summary>A string (or each string of a list) naming a mission step kind — picked from the kinds the
    /// project's tree schemas declare, never typed.</summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class StepKindAttribute : PropertyAttribute
    {
    }
}
