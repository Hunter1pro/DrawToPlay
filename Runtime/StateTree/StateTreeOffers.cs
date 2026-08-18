using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT MAY THIS THING REFER TO (M30.1) — one answer, used by every picker.
    ///
    /// The rule was always here, in the row pickers: a reference offers the rows of the registries
    /// its owner DECLARES (dependsOn), not everything of that shape in the project. It is the
    /// difference between a menu that means something and a list of every asset somebody ever made,
    /// and it is what makes a dependency a real statement rather than a comment.
    ///
    /// This puts that rule in one place so parameters, keys and rows can share it — and so the
    /// dependency map (M30.5) reads the SAME edges the pickers offer, instead of a second opinion
    /// that drifts.
    ///
    /// Runtime, not editor: the map wants it, tests want it, and it uses nothing but the assets
    /// themselves.
    /// </summary>
    public static class StateTreeOffers
    {
        /// <summary>
        /// Every registry this asset declares, transitively — a registry's own dependsOn closure, a
        /// tree's listed Data registries and theirs, a service def's registry and its closure.
        /// Anything else contributes nothing, which is the point: an asset that declares no
        /// neighbourhood has no offers, and the fix is to declare one.
        /// </summary>
        public static void ReachableRegistries(Object owner, List<StateTreeRegistryAsset> into)
        {
            if (into == null)
                return;
            into.Clear();
            switch (owner)
            {
                case StateTreeRegistryAsset registry:
                    registry.CollectWithDependencies(into);
                    break;
                case StateTreeAsset tree:
                    for (int i = 0; i < tree.registries.Count; i++)
                        Add(tree.registries[i], into);
                    break;
                case ServiceDef service:
                    Add(service.registry, into);
                    if (service.flows != null)
                    {
                        for (int i = 0; i < service.flows.registries.Count; i++)
                            Add(service.flows.registries[i], into);
                    }
                    break;
            }
        }

        /// <summary>The rows a value of this type may name, drawn from the owner's declared
        /// neighbourhood. Empty when the type names no registry, or when the owner does not
        /// declare it — both of which are answers worth showing an author.</summary>
        public static void RowsFor(StateTreeValueType type, Object owner,
            List<StateTreeRegistryEntry> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (type == null || type.kind != StateTreeValueKind.Row || type.rows == null)
                return;

            var reachable = new List<StateTreeRegistryAsset>();
            ReachableRegistries(owner, reachable);
            // THE DECLARED ONE WINS: the type names a registry, and the owner has to be able to
            // see it. A type pointing at a catalog nobody declared is exactly the broken link this
            // rule exists to surface, so it offers nothing rather than quietly working.
            if (!reachable.Contains(type.rows))
                return;

            for (int i = 0; i < type.rows.Count; i++)
            {
                StateTreeRegistryEntry row = type.rows.EntryAt(i);
                if (row != null && !string.IsNullOrEmpty(row.name))
                    into.Add(row);
            }
        }

        /// <summary>
        /// The defs that KEEP a contract, drawn from the owner's declared neighbourhood (M30.2).
        ///
        /// This is what makes a contract-typed field usable: ask for "damageable" and the picker
        /// offers the defs that claim it and are reachable from here — not every def in the
        /// project, and not defs whose catalog nobody declared. The same neighbourhood rule the
        /// rows follow, applied to behaviour.
        /// </summary>
        public static void ImplementersOf(ContractDef contract, Object owner, List<ServiceDef> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (contract == null)
                return;

            var reachable = new List<StateTreeRegistryAsset>();
            ReachableRegistries(owner, reachable);
            for (int i = 0; i < reachable.Count; i++)
            {
                StateTreeRegistryAsset registry = reachable[i];
                if (registry == null)
                    continue;
                for (int j = 0; j < registry.Count; j++)
                {
                    // A def can be a row of a catalog (the M30.3 shape) or the asset that
                    // manages one; both are found the same way — by asking the row what it is.
                    if (registry.EntryAt(j) is IServiceDefCarrier carrier
                        && carrier.ServiceDef != null
                        && StateTreeContracts.Claims(carrier.ServiceDef, contract)
                        && !into.Contains(carrier.ServiceDef))
                        into.Add(carrier.ServiceDef);
                }
            }
        }

        /// <summary>Whether this asset declares that registry — the one-line form of the rule,
        /// for a validator that wants to say "this type points outside the neighbourhood".</summary>
        public static bool Declares(Object owner, StateTreeRegistryAsset registry)
        {
            if (registry == null)
                return false;
            var reachable = new List<StateTreeRegistryAsset>();
            ReachableRegistries(owner, reachable);
            return reachable.Contains(registry);
        }

        private static void Add(StateTreeRegistryAsset registry, List<StateTreeRegistryAsset> into)
        {
            if (registry == null || into.Contains(registry))
                return;
            registry.CollectWithDependencies(into);
        }
    }
}
