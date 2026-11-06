using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A game's own section on a selected row of the Level Manifest — a plain choice in place of the raw arguments
    /// it stands for. The package draws it under the row and leaves the arguments it owns out of the row's
    /// Parameters list; what the choice means is the game's. Registered from the game's <c>[InitializeOnLoad]</c>
    /// class (meta-rule 5: the package defines the seam, the game implements it).
    /// </summary>
    public interface ILevelRowSection
    {
        /// <summary>False: nothing drawn, nothing hidden on this row.</summary>
        bool Offers(LevelObjectRegistry manifest, LevelObjectDef row);

        /// <summary>The parameter ids this section draws itself; the row's Parameters list leaves them out.</summary>
        IReadOnlyCollection<string> Owns(LevelObjectDef row);

        /// <summary>The section. <paramref name="changed"/> redraws the selected row after an edit.</summary>
        VisualElement Build(LevelObjectRegistry manifest, LevelObjectDef row, Action changed);

        /// <summary>The manifest's standing problems this section sees, shown with the Level Manifest's warnings.
        /// Asked on every edit: answer from a cache.</summary>
        IEnumerable<string> Warnings(LevelObjectRegistry manifest) => Array.Empty<string>();
    }

    /// <summary>The sections a game registered; a domain reload clears them, and the game's class registers again.</summary>
    public static class LevelRowSections
    {
        private static readonly List<ILevelRowSection> s_Sections = new List<ILevelRowSection>();

        public static void Register(ILevelRowSection section)
        {
            if (section != null && !s_Sections.Contains(section))
                s_Sections.Add(section);
        }

        public static void Unregister(ILevelRowSection section) => s_Sections.Remove(section);

        /// <summary>The sections offered on <paramref name="row"/>, in the order they were registered.</summary>
        public static List<ILevelRowSection> Offered(LevelObjectRegistry manifest, LevelObjectDef row)
        {
            var offered = new List<ILevelRowSection>();
            if (manifest == null || row == null)
                return offered;
            foreach (ILevelRowSection section in s_Sections)
            {
                if (section.Offers(manifest, row))
                    offered.Add(section);
            }
            return offered;
        }

        /// <summary>Every parameter id a section draws for <paramref name="row"/>.</summary>
        public static HashSet<string> Owned(LevelObjectRegistry manifest, LevelObjectDef row)
        {
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (ILevelRowSection section in Offered(manifest, row))
            {
                IReadOnlyCollection<string> ids = section.Owns(row);
                if (ids != null)
                    owned.UnionWith(ids);
            }
            return owned;
        }

        /// <summary>The ids a section draws for the row a parameter set sits on — none when the set is not a
        /// manifest row's.</summary>
        internal static HashSet<string> Owned(SerializedProperty set)
        {
            if (s_Sections.Count == 0 || set == null || !(set.serializedObject.targetObject is LevelObjectRegistry manifest))
                return new HashSet<string>();
            Match index = Regex.Match(set.propertyPath, @"^entries\.Array\.data\[(\d+)\]\.parameters$");
            if (!index.Success)
                return new HashSet<string>();
            int i = int.Parse(index.Groups[1].Value);
            return i < manifest.entries.Count ? Owned(manifest, manifest.entries[i]) : new HashSet<string>();
        }

        public static List<string> Warnings(LevelObjectRegistry manifest)
        {
            var warnings = new List<string>();
            if (manifest == null)
                return warnings;
            foreach (ILevelRowSection section in s_Sections)
            {
                IEnumerable<string> lines = section.Warnings(manifest);
                if (lines != null)
                    warnings.AddRange(lines);
            }
            return warnings;
        }
    }
}
