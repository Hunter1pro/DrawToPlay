using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE WIRE INDEX, ROLLED UP TO ASSETS (M30.5) — what the map draws, kept apart from the
    /// drawing so it can be asked questions in a test instead of by eye.
    ///
    /// Two roll-ups do the work, and both answer the question a map is actually asked. A ROW's
    /// use becomes an edge to the CATALOG that holds the row, because "which catalog does this
    /// tree need" is a question and "which of its 40 rows" is a different, later one. A REQUEST's
    /// caller becomes an edge to the DEF that answers it, for the same reason. Sub-assets fold
    /// into the file that owns them: a tree's task is not a thing anybody moves.
    ///
    /// Nothing is inferred. Every edge here was authored by somebody, which is what makes the
    /// absence of one worth looking at.
    /// </summary>
    internal sealed class DependencyGraph
    {
        internal enum NodeKind { Registry, Def, Tree, Graph, Prefab, Other }

        internal enum EdgeKind { Reference, Row, Request }

        internal sealed class Node
        {
            public Object asset;
            public string label;
            public string type;
            public NodeKind kind;
            public Rect rect;
            public int outgoing;
            public int incoming;
        }

        internal struct Edge
        {
            public int from;
            public int to;
            public EdgeKind kind;
            public int count;
            public string first;
        }

        internal readonly List<Node> nodes = new List<Node>();
        internal readonly List<Edge> edges = new List<Edge>();
        internal readonly Dictionary<Object, int> lookup = new Dictionary<Object, int>();

        private readonly Dictionary<long, int> m_EdgeLookup = new Dictionary<long, int>();

        internal static DependencyGraph Build(AssetWireScan.Index index)
        {
            var graph = new DependencyGraph();
            if (index == null)
                return graph;

            foreach (var pair in index.assetUses)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                    graph.Connect(pair.Value[i].context, pair.Key, EdgeKind.Reference,
                        pair.Value[i].description);
            }

            foreach (var pair in index.rowUsesById)
            {
                if (!index.rowOwners.TryGetValue(pair.Key, out Object owner))
                    continue;
                for (int i = 0; i < pair.Value.Count; i++)
                    graph.Connect(pair.Value[i].context, owner, EdgeKind.Row,
                        pair.Value[i].description);
            }
            foreach (var pair in index.rowUsesByName)
            {
                if (!index.rowOwners.TryGetValue("name:" + pair.Key, out Object owner))
                    continue;
                for (int i = 0; i < pair.Value.Count; i++)
                    graph.Connect(pair.Value[i].context, owner, EdgeKind.Row,
                        pair.Value[i].description);
            }

            foreach (var pair in index.requestCallers)
            {
                if (!index.requestOwners.TryGetValue(pair.Key, out Object owner))
                    continue;
                for (int i = 0; i < pair.Value.Count; i++)
                    graph.Connect(pair.Value[i].context, owner, EdgeKind.Request,
                        pair.Value[i].description + " → " + pair.Key);
            }

            graph.Layout();
            return graph;
        }

        /// <summary>Columns by kind, rows by discovery — deterministic on purpose: a map that
        /// rearranges itself between openings is one you have to re-read every time.</summary>
        internal void Layout()
        {
            var perColumn = new int[6];
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                int column = (int)node.kind;
                node.rect = new Rect(30f + column * 250f, 30f + perColumn[column] * 44f,
                    210f, 34f);
                perColumn[column]++;
            }
        }

        /// <summary>Is there an edge between these two, either way — "does this touch that",
        /// which is what a focused view is filtered by.</summary>
        internal bool Touching(int a, int b)
        {
            if (a < 0 || b < 0)
                return false;
            if (a == b)
                return true;
            for (int i = 0; i < edges.Count; i++)
            {
                Edge edge = edges[i];
                if ((edge.from == a && edge.to == b) || (edge.to == a && edge.from == b))
                    return true;
            }
            return false;
        }

        internal int IndexOf(Object asset)
        {
            return asset != null && lookup.TryGetValue(asset, out int found) ? found : -1;
        }

        private void Connect(Object from, Object to, EdgeKind kind, string description)
        {
            if (from == null || to == null)
                return;
            int a = NodeFor(from);
            int b = NodeFor(to);
            // A FILE THAT REFERS TO ITSELF is what a sub-asset roll-up produces (a tree's task
            // naming its own tree), and drawing it would be a loop that means nothing.
            if (a == b)
                return;

            long id = ((long)a << 34) | ((long)b << 4) | (long)kind;
            if (m_EdgeLookup.TryGetValue(id, out int existing))
            {
                Edge edge = edges[existing];
                edge.count++;
                edges[existing] = edge;
                return;
            }
            m_EdgeLookup[id] = edges.Count;
            edges.Add(new Edge { from = a, to = b, kind = kind, count = 1, first = description });
            nodes[a].outgoing++;
            nodes[b].incoming++;
        }

        private int NodeFor(Object asset)
        {
            if (lookup.TryGetValue(asset, out int found))
                return found;

            Object owner = OwnerOf(asset);
            if (owner != asset && owner != null)
            {
                int rolled = NodeFor(owner);
                lookup[asset] = rolled;
                return rolled;
            }

            var node = new Node
            {
                asset = asset,
                label = asset.name,
                type = asset.GetType().Name,
                kind = KindOf(asset)
            };
            lookup[asset] = nodes.Count;
            nodes.Add(node);
            return nodes.Count - 1;
        }

        /// <summary>The file an object belongs to — a task inside a tree is the tree, on a map
        /// of things people move.</summary>
        private static Object OwnerOf(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
                return asset;
            Object main = AssetDatabase.LoadMainAssetAtPath(path);
            return main != null ? main : asset;
        }

        /// <summary>
        /// THE ONE EDIT THE MAP OFFERS — declare a catalog, which is the edge every empty picker
        /// in this toolset is missing. A registry declares in Depends On, a def in Declares; both
        /// are the same sentence about what may be named from here.
        ///
        /// Refuses self-declaration and repeats, and answers whether anything changed — so the
        /// caller knows whether to dirty an asset, and a test knows what happened.
        /// </summary>
        internal static bool Declare(Object owner, StateTreeRegistryAsset catalog)
        {
            if (owner == null || catalog == null || ReferenceEquals(owner, catalog))
                return false;

            if (owner is StateTreeRegistryAsset registry)
            {
                if (registry.dependsOn.Contains(catalog))
                    return false;
                registry.dependsOn.Add(catalog);
                return true;
            }
            if (owner is ServiceDef def)
            {
                if (def.declares.Contains(catalog))
                    return false;
                def.declares.Add(catalog);
                return true;
            }
            return false;
        }

        internal static NodeKind KindOf(Object asset)
        {
            switch (asset)
            {
                case StateTreeRegistryAsset _: return NodeKind.Registry;
                case ServiceDef _: return NodeKind.Def;
                case StateTreeAsset _: return NodeKind.Tree;
                case GraphTaskAsset _: return NodeKind.Graph;
                case GameObject _: return NodeKind.Prefab;
                default: return NodeKind.Other;
            }
        }
    }
}
