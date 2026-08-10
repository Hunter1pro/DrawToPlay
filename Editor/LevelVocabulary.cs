using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// What vocabulary a LEVEL can speak, followed through the wiring that already exists
    /// rather than guessed by asset search:
    ///
    ///   this object registry → the LevelContent that owns it (its <c>objects</c>)
    ///                        → the LevelRegistry catalog whose row names that level
    ///                        → the state TREE that lists that catalog in its registries
    ///                        → the tag registries that tree lists = the GLOBAL vocabulary
    ///
    /// plus the level's OWN (<see cref="LevelContent.tags"/>). So "global" means what the
    /// root tree declares — the same list its key pickers use — reached from a level scene
    /// where the tree itself is not loaded.
    ///
    /// Every step is optional in practice (a level may not be in a catalog yet, a catalog may
    /// not be on a tree). When the chain breaks, <see cref="CollectTags"/> falls back to every
    /// tag registry in the project: a picker that offers too much is a nuisance, one that
    /// silently offers less because an asset was not wired yet is a trap.
    /// </summary>
    internal static class LevelVocabulary
    {
        /// <summary>The level whose manifest this object registry is.</summary>
        public static LevelContent LevelOf(Object objectRegistry)
        {
            if (objectRegistry == null)
                return null;
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LevelContent));
            for (int i = 0; i < guids.Length; i++)
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelContent>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (level != null && level.objects == objectRegistry)
                    return level;
            }
            return null;
        }

        /// <summary>The tree that owns a level: the one whose registries carry the catalog
        /// that lists it. This is the link a level scene cannot walk at run time — the tree
        /// lives in the persistent scene — but the ASSETS still say it.</summary>
        public static StateTreeAsset TreeOf(LevelContent level)
        {
            if (level == null)
                return null;

            string[] treeGuids = AssetDatabase.FindAssets("t:" + nameof(StateTreeAsset));
            for (int i = 0; i < treeGuids.Length; i++)
            {
                var tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(
                    AssetDatabase.GUIDToAssetPath(treeGuids[i]));
                if (tree == null)
                    continue;
                for (int j = 0; j < tree.registries.Count; j++)
                {
                    if (!(tree.registries[j] is LevelRegistry catalog))
                        continue;
                    for (int k = 0; k < catalog.entries.Count; k++)
                    {
                        LevelDef row = catalog.entries[k];
                        if (row != null && row.content == level)
                            return tree;
                    }
                }
            }
            return null;
        }

        /// <summary>Every tag a placement in this registry's level may carry: the owning
        /// tree's declared registries (GLOBAL) plus the level's own (LOCAL) — or, when the
        /// chain is not wired yet, every tag registry in the project.</summary>
        /// <param name="source">Where the list came from, for the field's tooltip — the one
        /// place an author can see whether the wiring is being followed or guessed.</param>
        public static void CollectTags(Object objectRegistry, List<string> into,
            out string source)
        {
            LevelContent level = LevelOf(objectRegistry);
            StateTreeAsset tree = TreeOf(level);

            var parts = new List<string>();
            if (tree != null)
            {
                for (int i = 0; i < tree.registries.Count; i++)
                {
                    if (tree.registries[i] is WorldTagRegistry global)
                    {
                        AddRows(global, into);
                        parts.Add(global.name + " (from " + tree.name + ")");
                    }
                }
            }
            if (level != null && level.tags != null)
            {
                AddRows(level.tags, into);
                parts.Add(level.tags.name + " (this level)");
            }

            if (parts.Count == 0)
            {
                string[] guids = AssetDatabase.FindAssets("t:" + nameof(WorldTagRegistry));
                for (int i = 0; i < guids.Length; i++)
                {
                    var registry = AssetDatabase.LoadAssetAtPath<WorldTagRegistry>(
                        AssetDatabase.GUIDToAssetPath(guids[i]));
                    AddRows(registry, into);
                }
                source = "every tag registry in the project — this level is not listed by a "
                    + "catalog on a tree, so there is no declared vocabulary to follow.";
                return;
            }
            source = "declared by: " + string.Join(", ", parts);
        }

        private static void AddRows(WorldTagRegistry registry, List<string> into)
        {
            if (registry == null)
                return;
            for (int i = 0; i < registry.entries.Count; i++)
            {
                WorldTagDef row = registry.entries[i];
                if (row != null && !string.IsNullOrEmpty(row.name) && !into.Contains(row.name))
                    into.Add(row.name);
            }
        }
    }
}
