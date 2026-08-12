using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// THE DATA A GRAPH IS ALLOWED TO NAME — the answer to "which registries answer for this
    /// canvas?", which a graph, unlike a tree, cannot state for itself.
    ///
    /// WHY A GRAPH HAS NO REGISTRY LIST OF ITS OWN. A tree carries
    /// <see cref="StateTreeAsset.registries"/> and the executor binds every typed reference from
    /// it at StartTree. A graph is not addressed that way: it is REACHED, from the registry row
    /// that points at it — an NPC's <c>program</c>, a level's script — and it is that row's
    /// registry that knows what the graph is for. So the edge is declared once, on the registry
    /// (<see cref="StateTreeRegistryAsset.dependsOn"/>), and read here in the opposite direction:
    ///
    ///   .taskgraph file
    ///     ← referenced by a row of        OutpostDialogDef registry   (the ROOTS)
    ///                       which declares dependsOn ▸ ItemDef registry
    ///                                                 DialogKeyDef registry
    ///     ⇒ the scope = roots + their transitive dependencies
    ///
    /// One declaration on the dialog registry therefore gives every dialog graph the same data,
    /// instead of each graph re-listing the same assets and drifting.
    ///
    /// HOW A ROOT IS RECOGNISED. Any public field of any row, of any registry, that is a
    /// <see cref="UnityEngine.Object"/> living at the graph's own asset path. That is
    /// deliberately structural rather than named: the reference the M21 dialog rows hold is
    /// <c>program</c>, but a level row's is <c>script</c> and the next one's will be something
    /// else, and none of them should have to tell this class its field name.
    ///
    /// EMPTY IS A REAL ANSWER, not a failure. A graph no registry row points at yet — a
    /// brand-new file — has no scope, and the validator says exactly that rather than inventing
    /// one from every registry in the project. Silently widening the scope would make the check
    /// pass for names that will not resolve at runtime, which is worse than no check.
    /// </summary>
    public sealed class GraphRegistryScope
    {
        /// <summary>How long a resolved scope is reused before the assets are read again.
        /// <see cref="Graph.OnGraphChanged"/> fires on every keystroke and the resolution walks
        /// every registry row in the project, so an uncached scope would put an
        /// <see cref="AssetDatabase.FindAssets"/> behind the space bar. Two seconds is short
        /// enough that editing a registry and tabbing back to the graph shows the new rows, and
        /// long enough that a burst of edits costs one scan.</summary>
        private const double k_CacheSeconds = 2.0;

        private static readonly Dictionary<string, GraphRegistryScope> s_Cache =
            new Dictionary<string, GraphRegistryScope>(StringComparer.Ordinal);

        private static double s_CacheStamp;

        private readonly List<StateTreeRegistryAsset> m_Registries =
            new List<StateTreeRegistryAsset>();

        /// <summary>The subset of <see cref="m_Registries"/> that NAME the graph — see
        /// <see cref="IsRoot"/> for why the distinction is kept.</summary>
        private readonly List<StateTreeRegistryAsset> m_Roots =
            new List<StateTreeRegistryAsset>();

        /// <summary>The registries this graph may name, roots first then their dependencies,
        /// each appearing once.</summary>
        public IReadOnlyList<StateTreeRegistryAsset> registries => m_Registries;

        /// <summary>True when no registry row anywhere points at this graph — see the class
        /// remarks on why that is reported rather than papered over.</summary>
        public bool isEmpty => m_Registries.Count == 0;

        /// <summary>
        /// Whether this registry is a ROOT of the scope — one that names the graph, rather than
        /// one reached through a Depends On edge.
        ///
        /// The difference matters exactly once: only a root can be the registry whose row STARTS
        /// a run, so only a root's entry class can be what a Row Value node reads when it names no
        /// row of its own. A dependency is data the graph may mention, never a caller.
        /// </summary>
        /// <param name="registry">A registry, normally one from <see cref="registries"/>.</param>
        /// <returns>True when a row of it points at this graph.</returns>
        public bool IsRoot(StateTreeRegistryAsset registry)
        {
            return registry != null && m_Roots.Contains(registry);
        }

        /// <summary>
        /// The scope of a graph, resolved from disk and cached for <see cref="k_CacheSeconds"/>.
        /// </summary>
        /// <param name="graph">The canvas being edited or baked.</param>
        /// <returns>Never null; an empty scope when the graph is unsaved or unreferenced.</returns>
        public static GraphRegistryScope For(Graph graph)
        {
            return For(graph, null);
        }

        /// <summary>
        /// The same, told where the graph lives.
        /// </summary>
        /// <param name="graph">The canvas being edited or baked.</param>
        /// <param name="assetPath">The graph's asset path when the caller already knows it.
        /// THE IMPORTER MUST PASS IT: a graph loaded with
        /// <see cref="GraphDatabase.LoadGraphForImporter{T}"/> is a detached copy and answers
        /// <see cref="GraphDatabase.GetGraphAssetPath"/> with nothing, so a bake that resolved the
        /// scope for itself would find no registries and report every picked row as unreachable —
        /// on exactly the graphs that are correct.</param>
        /// <returns>Never null; an empty scope when the graph is unsaved or unreferenced.</returns>
        public static GraphRegistryScope For(Graph graph, string assetPath)
        {
            string path = !string.IsNullOrEmpty(assetPath)
                ? assetPath
                : graph != null ? GraphDatabase.GetGraphAssetPath(graph) : null;
            if (string.IsNullOrEmpty(path))
                return new GraphRegistryScope();

            if (EditorApplication.timeSinceStartup - s_CacheStamp > k_CacheSeconds)
            {
                s_Cache.Clear();
                s_CacheStamp = EditorApplication.timeSinceStartup;
            }

            if (s_Cache.TryGetValue(path, out GraphRegistryScope cached))
                return cached;

            var scope = new GraphRegistryScope();
            scope.Resolve(graph, path);
            s_Cache[path] = scope;
            return scope;
        }

        /// <summary>Drop the cache — for the tests, and for anything that edits a registry and
        /// needs the next read to see it.</summary>
        public static void InvalidateCache()
        {
            s_Cache.Clear();
            s_CacheStamp = 0.0;
        }

        /// <summary>Whether any registry in scope holds rows of this entry class. False means
        /// the graph cannot be checked against that data at all, which is a different report
        /// from "checked, and the name is wrong".</summary>
        /// <param name="entryType">The entry class a typed reference wants.</param>
        /// <returns>True when at least one in-scope registry answers for it.</returns>
        public bool Answers(Type entryType)
        {
            for (int i = 0; i < m_Registries.Count; i++)
            {
                if (m_Registries[i].entryType == entryType)
                    return true;
            }
            return false;
        }

        /// <summary>Ordinal name lookup across every in-scope registry of the entry class —
        /// the same lookup the executor's StartTree binding does, run at author time.</summary>
        /// <param name="entryType">The entry class a typed reference wants.</param>
        /// <param name="name">The name typed into (or wired into) the port.</param>
        /// <returns>The row, or null when nothing in scope carries that name.</returns>
        public StateTreeRegistryEntry Find(Type entryType, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            for (int i = 0; i < m_Registries.Count; i++)
            {
                StateTreeRegistryAsset registry = m_Registries[i];
                if (registry.entryType != entryType)
                    continue;
                StateTreeRegistryEntry entry = registry.FindByName(name);
                if (entry != null)
                    return entry;
            }
            return null;
        }

        /// <summary>Every in-scope row of one entry class, in registry-then-list order — what
        /// the Blackboard is filled from and what an error message offers as the legal set.</summary>
        /// <param name="entryType">The entry class to collect.</param>
        /// <param name="into">Accumulator; not cleared.</param>
        public void CollectEntries(Type entryType, List<StateTreeRegistryEntry> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < m_Registries.Count; i++)
            {
                StateTreeRegistryAsset registry = m_Registries[i];
                if (registry.entryType != entryType)
                    continue;
                for (int j = 0; j < registry.Count; j++)
                {
                    StateTreeRegistryEntry entry = registry.EntryAt(j);
                    if (entry != null && !string.IsNullOrEmpty(entry.name))
                        into.Add(entry);
                }
            }
        }

        /// <summary>The legal names of one entry class as a readable list, capped so a registry
        /// with three hundred rows does not turn one bad name into a wall of text.</summary>
        /// <param name="entryType">The entry class to describe.</param>
        /// <returns>"medkit, rope, lantern" — or "(none)" when the scope has no rows for it.</returns>
        public string DescribeEntries(Type entryType)
        {
            const int limit = 12;
            var rows = new List<StateTreeRegistryEntry>();
            CollectEntries(entryType, rows);
            if (rows.Count == 0)
                return "(none)";

            var names = new List<string>();
            for (int i = 0; i < rows.Count && i < limit; i++)
                names.Add(rows[i].name);
            string text = string.Join(", ", names);
            return rows.Count > limit ? text + ", … (" + rows.Count + " total)" : text;
        }

        /// <summary>Collect the roots — what the canvas names for itself, plus every registry a
        /// row of which points at this graph — and close over their declared dependencies.</summary>
        /// <param name="graph">The canvas, asked first in case it declares its own data.</param>
        /// <param name="graphPath">Asset path of the graph file.</param>
        private void Resolve(Graph graph, string graphPath)
        {
            if (graph is IGraphDeclaredRegistries declaring)
            {
                var declared = new List<StateTreeRegistryAsset>();
                declaring.CollectDeclaredRegistries(declared);
                for (int i = 0; i < declared.Count; i++)
                {
                    if (declared[i] == null)
                        continue;
                    if (!m_Roots.Contains(declared[i]))
                        m_Roots.Add(declared[i]);
                    declared[i].CollectWithDependencies(m_Registries);
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(StateTreeRegistryAsset));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<StateTreeRegistryAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null || !ReferencesAsset(registry, graphPath))
                    continue;
                if (!m_Roots.Contains(registry))
                    m_Roots.Add(registry);
                registry.CollectWithDependencies(m_Registries);
            }
        }

        /// <summary>Does any row of this registry hold an object reference into
        /// <paramref name="graphPath"/>? See the class remarks on why this is by shape rather
        /// than by field name.</summary>
        /// <param name="registry">The registry to search.</param>
        /// <param name="graphPath">Asset path of the graph file.</param>
        /// <returns>True when at least one row points at the file.</returns>
        private static bool ReferencesAsset(StateTreeRegistryAsset registry, string graphPath)
        {
            FieldInfo[] fields = registry.entryType.GetFields(
                BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < registry.Count; i++)
            {
                StateTreeRegistryEntry entry = registry.EntryAt(i);
                if (entry == null)
                    continue;

                for (int f = 0; f < fields.Length; f++)
                {
                    if (!typeof(UnityEngine.Object).IsAssignableFrom(fields[f].FieldType))
                        continue;
                    var value = fields[f].GetValue(entry) as UnityEngine.Object;
                    if (value == null)
                        continue;
                    if (string.Equals(AssetDatabase.GetAssetPath(value), graphPath,
                        StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }
    }
}
