using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The catalogs a row-name field picks from: the ones the asset (or the tree it lives in) reaches,
    /// else every catalog of that row type in the project — cached per type, dropped on a project change,
    /// so nothing walks the project per repaint.
    /// </summary>
    public static class StateTreeRowOffers
    {
        private static readonly Dictionary<Type, List<StateTreeRegistryAsset>> s_Project =
            new Dictionary<Type, List<StateTreeRegistryAsset>>();

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        public static void Invalidate()
        {
            s_Project.Clear();
        }

        /// <summary>The catalogs of <paramref name="entryType"/> for an owner; <paramref name="declared"/>
        /// says whether they are the owner's own or the project's.</summary>
        public static List<StateTreeRegistryAsset> For(UnityEngine.Object owner, Type entryType, out bool declared)
        {
            var found = new List<StateTreeRegistryAsset>();
            declared = true;
            if (entryType == null)
                return found;
            Reachable(owner, entryType, found);
            if (found.Count == 0 && owner != null)
            {
                string path = AssetDatabase.GetAssetPath(owner);
                UnityEngine.Object main = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
                if (main != null && main != owner)
                    Reachable(main, entryType, found);
            }
            if (found.Count > 0)
                return found;
            declared = false;
            found.AddRange(Project(entryType));
            return found;
        }

        public static StateTreeRegistryEntry Find(List<StateTreeRegistryAsset> catalogs, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            foreach (StateTreeRegistryAsset catalog in catalogs)
            {
                StateTreeRegistryEntry entry = catalog != null ? catalog.FindByName(name) : null;
                if (entry != null)
                    return entry;
            }
            return null;
        }

        /// <summary>The catalog a row lives in, or null.</summary>
        public static StateTreeRegistryAsset CatalogOf(List<StateTreeRegistryAsset> catalogs, StateTreeRegistryEntry row)
        {
            foreach (StateTreeRegistryAsset catalog in catalogs)
            {
                for (int i = 0; catalog != null && i < catalog.Count; i++)
                {
                    if (catalog.EntryAt(i) == row)
                        return catalog;
                }
            }
            return null;
        }

        private static void Reachable(UnityEngine.Object owner, Type entryType, List<StateTreeRegistryAsset> into)
        {
            var reachable = new List<StateTreeRegistryAsset>();
            StateTreeOffers.ReachableRegistries(owner, reachable);
            foreach (StateTreeRegistryAsset registry in reachable)
            {
                if (registry != null && entryType.IsAssignableFrom(registry.entryType) && !into.Contains(registry))
                    into.Add(registry);
            }
        }

        private static List<StateTreeRegistryAsset> Project(Type entryType)
        {
            if (s_Project.TryGetValue(entryType, out List<StateTreeRegistryAsset> cached))
                return cached;
            var found = new List<StateTreeRegistryAsset>();
            foreach (string guid in AssetDatabase.FindAssets("t:StateTreeRegistryAsset"))
            {
                var registry = AssetDatabase.LoadAssetAtPath<StateTreeRegistryAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (registry != null && entryType.IsAssignableFrom(registry.entryType))
                    found.Add(registry);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            s_Project[entryType] = found;
            return found;
        }
    }
}
