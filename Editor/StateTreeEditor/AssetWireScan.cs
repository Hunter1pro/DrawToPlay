using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE REVERSE WIRE MAP (M23 review: "see who used the ability") — the forward wires
    /// already exist everywhere as picked references; this walks them all once and answers
    /// the opposite question: given a ROW (an ability, a cue, a level), where is it used —
    /// and given an ASSET (a tree, a registry, a program), who references it.
    ///
    /// One reflection pass over the project's registries, trees and prefabs, cached and
    /// invalidated by <see cref="EditorApplication.projectChanged"/> — the UI reads
    /// dictionaries, never rescans per row. Scene objects are not walked (phase 2, with the
    /// global map); the demo's characters are prefabs and its wiring lives in assets, which
    /// is the case this phase serves.
    /// </summary>
    internal static class AssetWireScan
    {
        /// <summary>One place something is used: the asset to ping and the sentence saying
        /// where inside it. When the reference is held BY a registry row, the row rides
        /// along — the chain's next question ("and who uses that row?") needs it.</summary>
        internal struct WireUse
        {
            public UnityEngine.Object context;
            public string description;
            public StateTreeRegistryEntry viaRow;
        }

        /// <summary>One line of an expanded usage chain: the use, at its distance from the
        /// asked-about thing — depth 0 is direct, deeper is "…and who uses that".</summary>
        internal struct ChainLine
        {
            public UnityEngine.Object context;
            public string description;
            public int depth;
        }

        /// <summary>
        /// FOLLOW THE WIRES THROUGH THE ROWS (review: the push tree's user list stopped at
        /// "row 'push'" — but the question was who USES the ability): every use held by a
        /// registry row expands into that row's own users, indented one step, until the
        /// chain leaves the registries or repeats itself.
        /// </summary>
        internal static void CollectChain(Index index, List<WireUse> uses,
            List<ChainLine> into, int depth, HashSet<string> visitedRows)
        {
            if (uses == null || depth > 6)
                return;
            for (int i = 0; i < uses.Count; i++)
            {
                WireUse use = uses[i];
                into.Add(new ChainLine
                {
                    context = use.context,
                    description = use.description,
                    depth = depth
                });
                StateTreeRegistryEntry via = use.viaRow;
                if (via == null)
                    continue;
                var key = !string.IsNullOrEmpty(via.id) ? via.id : "name:" + via.name;
                if (!visitedRows.Add(key))
                    continue;
                CollectChain(index, UsersOfRow(index, via), into, depth + 1, visitedRows);
            }
        }

        internal sealed class Index
        {
            /// <summary>Row uses keyed by the row's ID — the wire the pickers write.</summary>
            public readonly Dictionary<string, List<WireUse>> rowUsesById =
                new Dictionary<string, List<WireUse>>(StringComparer.Ordinal);

            /// <summary>Row uses from NAME-ONLY references (free-typed graph ports and
            /// friends) — matched by name because that is all they carry.</summary>
            public readonly Dictionary<string, List<WireUse>> rowUsesByName =
                new Dictionary<string, List<WireUse>>(StringComparer.Ordinal);

            /// <summary>Asset-to-asset references: tree→registry (Data), registry→registry
            /// (dependsOn), row→tree (an ability's tree), host→tree, and the rest.</summary>
            public readonly Dictionary<UnityEngine.Object, List<WireUse>> assetUses =
                new Dictionary<UnityEngine.Object, List<WireUse>>();

            /// <summary>Which registry ASSET owns each row — keyed by the row's id and by
            /// "name:&lt;name&gt;", so both wire flavors resolve. The map's roll-up: a use
            /// of a row is an edge to the registry that holds it.</summary>
            public readonly Dictionary<string, UnityEngine.Object> rowOwners =
                new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

            internal void AddRowUse(string id, string name, WireUse use)
            {
                if (!string.IsNullOrEmpty(id))
                    Bucket(rowUsesById, id).Add(use);
                else if (!string.IsNullOrEmpty(name))
                    Bucket(rowUsesByName, name).Add(use);
            }

            internal void AddAssetUse(UnityEngine.Object target, WireUse use)
            {
                if (target == null)
                    return;
                if (!assetUses.TryGetValue(target, out List<WireUse> uses))
                {
                    uses = new List<WireUse>();
                    assetUses.Add(target, uses);
                }
                uses.Add(use);
            }

            private static List<WireUse> Bucket(Dictionary<string, List<WireUse>> map,
                string key)
            {
                if (!map.TryGetValue(key, out List<WireUse> uses))
                {
                    uses = new List<WireUse>();
                    map.Add(key, uses);
                }
                return uses;
            }
        }

        private static Index s_Cache;
        private static bool s_Dirty = true;

        static AssetWireScan()
        {
            EditorApplication.projectChanged += () => s_Dirty = true;
        }

        /// <summary>Force the next <see cref="Get"/> to rescan — the map window's Rescan.</summary>
        internal static void Invalidate()
        {
            s_Dirty = true;
        }

        /// <summary>The current map — rebuilt lazily after any project change.</summary>
        internal static Index Get()
        {
            if (s_Dirty || s_Cache == null)
            {
                s_Cache = BuildIndex();
                s_Dirty = false;
            }
            return s_Cache;
        }

        /// <summary>Everywhere this row is referenced — id wires first, then name-only
        /// references carrying its current name.</summary>
        internal static List<WireUse> UsersOfRow(Index index, StateTreeRegistryEntry row)
        {
            var uses = new List<WireUse>();
            if (index == null || row == null)
                return uses;
            if (!string.IsNullOrEmpty(row.id)
                && index.rowUsesById.TryGetValue(row.id, out List<WireUse> byId))
                uses.AddRange(byId);
            if (!string.IsNullOrEmpty(row.name)
                && index.rowUsesByName.TryGetValue(row.name, out List<WireUse> byName))
                uses.AddRange(byName);
            return uses;
        }

        internal static List<WireUse> UsersOfAsset(Index index, UnityEngine.Object asset)
        {
            return index != null && asset != null
                && index.assetUses.TryGetValue(asset, out List<WireUse> uses)
                ? uses
                : new List<WireUse>();
        }

        private static Index BuildIndex()
        {
            var index = new Index();

            foreach (var guid in AssetDatabase.FindAssets("t:StateTreeRegistryAsset"))
            {
                var registry = AssetDatabase.LoadAssetAtPath<StateTreeRegistryAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (registry != null)
                    ScanRegistry(registry, index);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:StateTreeAsset"))
            {
                var tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (tree != null)
                    ScanTree(tree, index);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null)
                    ScanPrefab(prefab, index);
            }
            return index;
        }

        /// <summary>Every row's picked references, plus the registry's own dependsOn edges.
        /// AssetDatabase-free so tests can feed instances directly.</summary>
        internal static void ScanRegistry(StateTreeRegistryAsset registry, Index index)
        {
            if (registry == null)
                return;
            for (int i = 0; i < registry.Count; i++)
            {
                StateTreeRegistryEntry row = registry.EntryAt(i);
                if (row == null)
                    continue;
                if (!string.IsNullOrEmpty(row.id) && !index.rowOwners.ContainsKey(row.id))
                    index.rowOwners.Add(row.id, registry);
                if (!string.IsNullOrEmpty(row.name)
                    && !index.rowOwners.ContainsKey("name:" + row.name))
                    index.rowOwners.Add("name:" + row.name, registry);
                ScanValue(row, registry,
                    registry.name + " · row '" + row.name + "'", index, 0, row);
            }
            IReadOnlyList<StateTreeRegistryAsset> depends = registry.dependsOn;
            for (int i = 0; depends != null && i < depends.Count; i++)
            {
                if (depends[i] != null && depends[i] != registry)
                {
                    index.AddAssetUse(depends[i], new WireUse
                    {
                        context = registry,
                        description = registry.name + " · dependsOn"
                    });
                }
            }
        }

        /// <summary>The tree's Data and imports, and every task's and condition's picked
        /// references, labeled with the state that holds them.</summary>
        internal static void ScanTree(StateTreeAsset tree, Index index)
        {
            if (tree == null)
                return;
            for (int i = 0; i < tree.registries.Count; i++)
            {
                if (tree.registries[i] != null)
                {
                    index.AddAssetUse(tree.registries[i], new WireUse
                    {
                        context = tree,
                        description = tree.name + " · listed as Data"
                    });
                }
            }
            var uses = tree.uses;
            for (int i = 0; uses != null && i < uses.Count; i++)
            {
                if (uses[i] != null && uses[i] != tree)
                {
                    index.AddAssetUse(uses[i], new WireUse
                    {
                        context = tree,
                        description = tree.name + " · imported (uses)"
                    });
                }
            }
            ScanNode(tree, tree.root, index, 0);
        }

        private static void ScanNode(StateTreeAsset tree, StateTreeNodeAsset node,
            Index index, int depth)
        {
            if (node == null || depth > 256)
                return;
            for (int i = 0; i < node.tasks.Count; i++)
            {
                if (node.tasks[i] != null)
                {
                    ScanValue(node.tasks[i], tree, tree.name + " · '" + node.nodeId + "' · "
                        + node.tasks[i].GetType().Name, index, 0);
                }
            }
            for (int i = 0; i < node.transitions.Count; i++)
            {
                StateTreeTransition transition = node.transitions[i];
                if (transition != null && transition.condition != null)
                {
                    ScanValue(transition.condition, tree, tree.name + " · '" + node.nodeId
                        + "' · " + transition.condition.GetType().Name, index, 0);
                }
            }
            for (int i = 0; i < node.children.Count; i++)
                ScanNode(tree, node.children[i], index, depth + 1);
        }

        /// <summary>Every component's picked references, labeled with the prefab and the
        /// component that holds them.</summary>
        internal static void ScanPrefab(GameObject prefab, Index index)
        {
            if (prefab == null)
                return;
            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null)   // a missing script is a null slot
                    continue;
                ScanValue(behaviours[i], prefab,
                    prefab.name + " · " + behaviours[i].GetType().Name, index, 0);
            }
        }

        /// <summary>
        /// The reflection walk: entry refs (id or name-only) become row uses, references to
        /// the toolset's asset types become asset uses, override rows carry their ⛃ entry
        /// wires, and plain toolset classes (parameter sets, seeds, lists of either) recurse
        /// — bounded, and only into this project's own namespace so Unity types stay shut.
        /// </summary>
        private static void ScanValue(object owner, UnityEngine.Object context, string label,
            Index index, int depth, StateTreeRegistryEntry viaRow = null)
        {
            if (owner == null || depth > 4)
                return;

            var fields = owner.GetType().GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                object value;
                try
                {
                    value = fields[i].GetValue(owner);
                }
                catch
                {
                    continue;
                }
                if (value == null)
                    continue;

                switch (value)
                {
                    case IStateTreeEntryRef reference:
                        if (!string.IsNullOrEmpty(reference.EntryId)
                            || !string.IsNullOrEmpty(reference.EntryName))
                        {
                            index.AddRowUse(reference.EntryId, reference.EntryName, new WireUse
                            {
                                context = context,
                                description = label + " · " + fields[i].Name,
                                viaRow = viaRow
                            });
                        }
                        continue;

                    case GraphTaskParameterOverride wire:
                        // The ⛃ entry wire on an argument row — "destination = ridge".
                        if (!string.IsNullOrEmpty(wire.entryId))
                        {
                            index.AddRowUse(wire.entryId, wire.stringValue, new WireUse
                            {
                                context = context,
                                description = label + " · argument '" + wire.name + "'",
                                viaRow = viaRow
                            });
                        }
                        continue;

                    case UnityEngine.Object asset:
                        if (!ReferenceEquals(asset, context) && IsToolsetAsset(asset))
                        {
                            index.AddAssetUse(asset, new WireUse
                            {
                                context = context,
                                description = label + " · " + fields[i].Name,
                                viaRow = viaRow
                            });
                        }
                        continue;

                    case string _:
                        continue;

                    case IList list:
                        for (int j = 0; j < list.Count; j++)
                        {
                            ScanElement(list[j], context, label, fields[i].Name, index,
                                depth, viaRow);
                        }
                        continue;

                    default:
                        if (IsToolsetClass(value))
                            ScanValue(value, context, label, index, depth + 1, viaRow);
                        continue;
                }
            }
        }

        private static void ScanElement(object element, UnityEngine.Object context,
            string label, string fieldName, Index index, int depth,
            StateTreeRegistryEntry viaRow)
        {
            switch (element)
            {
                case null:
                    return;
                case IStateTreeEntryRef reference:
                    if (!string.IsNullOrEmpty(reference.EntryId)
                        || !string.IsNullOrEmpty(reference.EntryName))
                    {
                        index.AddRowUse(reference.EntryId, reference.EntryName, new WireUse
                        {
                            context = context,
                            description = label + " · " + fieldName,
                            viaRow = viaRow
                        });
                    }
                    return;
                case GraphTaskParameterOverride wire:
                    if (!string.IsNullOrEmpty(wire.entryId))
                    {
                        index.AddRowUse(wire.entryId, wire.stringValue, new WireUse
                        {
                            context = context,
                            description = label + " · argument '" + wire.name + "'",
                            viaRow = viaRow
                        });
                    }
                    return;
                case UnityEngine.Object asset:
                    if (!ReferenceEquals(asset, context) && IsToolsetAsset(asset))
                    {
                        index.AddAssetUse(asset, new WireUse
                        {
                            context = context,
                            description = label + " · " + fieldName,
                            viaRow = viaRow
                        });
                    }
                    return;
                default:
                    if (IsToolsetClass(element))
                    {
                        ScanValue(element, context, label + " · " + fieldName, index,
                            depth + 1, viaRow);
                    }
                    return;
            }
        }

        /// <summary>The asset kinds whose reverse edges the map answers for — the toolset's
        /// own. Everything else (materials, clips, prefab refs) stays out of the index.</summary>
        private static bool IsToolsetAsset(UnityEngine.Object asset)
        {
            return asset is StateTreeAsset
                || asset is StateTreeRegistryAsset
                || asset is GraphTaskAsset;
        }

        private static bool IsToolsetClass(object value)
        {
            var ns = value.GetType().Namespace;
            return ns != null && ns.StartsWith("PowerOfFire", StringComparison.Ordinal);
        }
    }
}
