using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE MAP'S MODEL (phase 2 of the reverse wire map): asset-level edges derived from
    /// <see cref="AssetWireScan"/> — row-level wires ROLL UP to the registry that owns the
    /// row, so the graph stays readable at asset granularity while every edge remembers a
    /// sample of the wires it stands for. Window-free and AssetDatabase-free on the query
    /// side, so tests feed instances directly.
    /// </summary>
    internal static class AssetWireGraph
    {
        /// <summary>One edge of the map: the other asset, how many wires it stands for,
        /// and one of them spelled out for the tooltip.</summary>
        internal struct GraphEdge
        {
            public UnityEngine.Object other;
            public int wires;
            public string sample;
        }

        /// <summary>Who references <paramref name="asset"/> — direct asset references plus,
        /// for a registry, every use of its rows, grouped per referencing asset.</summary>
        internal static List<GraphEdge> IncomingOf(AssetWireScan.Index index,
            UnityEngine.Object asset)
        {
            var grouped = new Dictionary<UnityEngine.Object, GraphEdge>();
            Accumulate(grouped, AssetWireScan.UsersOfAsset(index, asset), asset);

            // A subsystem's CALLERS are incoming edges (§4g): whoever writes one of its
            // declared request keys is pointing at it, exactly like a row reference does.
            if (asset is ServiceDef def)
            {
                for (int i = 0; i < def.requests.Count; i++)
                {
                    ServiceRequest request = def.requests[i];
                    if (request == null || string.IsNullOrEmpty(request.key))
                        continue;
                    if (index.requestCallers.TryGetValue(request.key, out var callers))
                        Accumulate(grouped, callers, asset);
                }
            }

            if (asset is StateTreeRegistryAsset registry)
            {
                for (int i = 0; i < registry.Count; i++)
                {
                    StateTreeRegistryEntry row = registry.EntryAt(i);
                    if (row != null)
                        Accumulate(grouped, AssetWireScan.UsersOfRow(index, row), asset);
                }
            }
            return Flatten(grouped);
        }

        /// <summary>What <paramref name="asset"/> references — a fresh forward scan of just
        /// this asset, row targets resolved to their owning registries through the shared
        /// index's <see cref="AssetWireScan.Index.rowOwners"/>.</summary>
        internal static List<GraphEdge> OutgoingOf(AssetWireScan.Index index,
            UnityEngine.Object asset)
        {
            var forward = new AssetWireScan.Index();
            // Seed the forward scan with WHO ANSWERS WHAT (§4g): a request call is only
            // recognisable against the project's owner map, and a fresh local index knows
            // none of it — so the scan would see every key as an ordinary string.
            foreach (KeyValuePair<string, UnityEngine.Object> owner in index.requestOwners)
                forward.requestOwners[owner.Key] = owner.Value;

            switch (asset)
            {
                case StateTreeAsset tree:
                    AssetWireScan.ScanTree(tree, forward);
                    break;
                case StateTreeRegistryAsset registry:
                    AssetWireScan.ScanRegistry(registry, forward);
                    break;
                case GameObject prefab:
                    AssetWireScan.ScanPrefab(prefab, forward);
                    break;
                case ServiceDef def:
                    AssetWireScan.ScanServiceDef(def, forward);
                    break;
                default:
                    return new List<GraphEdge>();
            }

            var grouped = new Dictionary<UnityEngine.Object, GraphEdge>();
            foreach (KeyValuePair<UnityEngine.Object, List<AssetWireScan.WireUse>> pair
                in forward.assetUses)
            {
                if (!ReferenceEquals(pair.Key, asset))
                    AccumulateTarget(grouped, pair.Key, pair.Value);
            }
            foreach (KeyValuePair<string, List<AssetWireScan.WireUse>> pair
                in forward.rowUsesById)
            {
                if (index.rowOwners.TryGetValue(pair.Key, out UnityEngine.Object owner)
                    && !ReferenceEquals(owner, asset))
                    AccumulateTarget(grouped, owner, pair.Value);
            }
            foreach (KeyValuePair<string, List<AssetWireScan.WireUse>> pair
                in forward.rowUsesByName)
            {
                if (index.rowOwners.TryGetValue("name:" + pair.Key,
                        out UnityEngine.Object owner)
                    && !ReferenceEquals(owner, asset))
                    AccumulateTarget(grouped, owner, pair.Value);
            }
            // …and the subsystems this asset CALLS: each written request key is an edge to
            // the def that answers it.
            foreach (KeyValuePair<string, List<AssetWireScan.WireUse>> pair
                in forward.requestCallers)
            {
                if (index.requestOwners.TryGetValue(pair.Key, out UnityEngine.Object owner)
                    && !ReferenceEquals(owner, asset))
                    AccumulateTarget(grouped, owner, pair.Value);
            }
            return Flatten(grouped);
        }

        private static void Accumulate(Dictionary<UnityEngine.Object, GraphEdge> grouped,
            List<AssetWireScan.WireUse> uses, UnityEngine.Object self)
        {
            for (int i = 0; i < uses.Count; i++)
            {
                if (uses[i].context == null || ReferenceEquals(uses[i].context, self))
                    continue;
                Bump(grouped, uses[i].context, uses[i].description);
            }
        }

        private static void AccumulateTarget(
            Dictionary<UnityEngine.Object, GraphEdge> grouped, UnityEngine.Object target,
            List<AssetWireScan.WireUse> uses)
        {
            for (int i = 0; i < uses.Count; i++)
                Bump(grouped, target, uses[i].description);
        }

        private static void Bump(Dictionary<UnityEngine.Object, GraphEdge> grouped,
            UnityEngine.Object key, string description)
        {
            if (grouped.TryGetValue(key, out GraphEdge edge))
            {
                edge.wires += 1;
                grouped[key] = edge;
            }
            else
            {
                grouped[key] = new GraphEdge { other = key, wires = 1, sample = description };
            }
        }

        private static List<GraphEdge> Flatten(
            Dictionary<UnityEngine.Object, GraphEdge> grouped)
        {
            var edges = new List<GraphEdge>(grouped.Values);
            edges.Sort((a, b) => string.CompareOrdinal(a.other.name, b.other.name));
            return edges;
        }
    }
}
