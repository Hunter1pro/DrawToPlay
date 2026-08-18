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
                    // WHAT IT DECLARES (M30.4), on top of what it manages: a def that owns a
                    // body has no catalog of its own to speak of and still needs to name the
                    // attributes it has — so it declares them, exactly as a registry does.
                    for (int i = 0; i < service.declares.Count; i++)
                        Add(service.declares[i], into);
                    if (service.flows != null)
                    {
                        for (int i = 0; i < service.flows.registries.Count; i++)
                            Add(service.flows.registries[i], into);
                    }
                    break;
            }
        }

        /// <summary>
        /// The rows a value of this type may NAME, drawn from the owner's declared neighbourhood.
        ///
        /// Two questions with one answer, because a field asking either one is doing the same job:
        /// a Row type names a catalog and offers its rows, a contract type names a PROMISE and
        /// offers whatever keeps it — from any catalog the owner declares. Empty when the type
        /// names nothing, or when the owner does not declare what it named; both are answers worth
        /// showing an author, and <see cref="WhyEmpty"/> says which one happened.
        /// </summary>
        public static void RowsFor(StateTreeValueType type, Object owner,
            List<StateTreeRegistryEntry> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (type == null)
                return;

            if (type.kind == StateTreeValueKind.Object)
            {
                ImplementerRowsOf(ContractNamed(type.contract, owner), owner, into);
                return;
            }

            if (type.kind != StateTreeValueKind.Row || type.rows == null)
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
        /// Why a picker has nothing to offer — in the author's terms, and DIFFERENT per cause.
        ///
        /// "Nothing declares that catalog" and "nothing keeps that promise yet" are two entirely
        /// different afternoons, and a picker that says only "empty" makes the author find out
        /// which one by experiment.
        /// </summary>
        public static string WhyEmpty(StateTreeValueType type, Object owner)
        {
            if (type == null)
                return "no type";
            if (type.kind == StateTreeValueKind.Object)
            {
                if (string.IsNullOrEmpty(type.contract))
                    return "no contract named";
                return ContractNamed(type.contract, owner) == null
                    ? "no contract called '" + type.contract + "' is declared here"
                    : "nothing declared here claims '" + type.contract + "' yet";
            }
            if (type.rows == null)
                return "no catalog named";
            return Declares(owner, type.rows)
                ? "'" + type.rows.name + "' has no rows"
                : "'" + type.rows.name + "' is not declared here";
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
            var rows = new List<StateTreeRegistryEntry>();
            ImplementerRowsOf(contract, owner, rows);
            for (int i = 0; i < rows.Count; i++)
            {
                ServiceDef def = (rows[i] as IServiceDefCarrier)?.ServiceDef;
                if (def != null && !into.Contains(def))
                    into.Add(def);
            }
        }

        /// <summary>
        /// The same answer in the currency a FIELD stores (M30.2b): the rows that carry those defs.
        ///
        /// A def is not a name — the row carrying it is, and every reference in this toolset rides
        /// as a row name. So a field that asks for "something damageable" picks from these and
        /// stores exactly what it always stored, which is why asking by promise costs the runtime
        /// nothing.
        /// </summary>
        public static void ImplementerRowsOf(ContractDef contract, Object owner,
            List<StateTreeRegistryEntry> into)
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
                    StateTreeRegistryEntry row = registry.EntryAt(j);
                    if (row is IServiceDefCarrier carrier
                        && carrier.ServiceDef != null
                        && StateTreeContracts.Claims(carrier.ServiceDef, contract)
                        && !into.Contains(row))
                        into.Add(row);
                }
            }
        }

        /// <summary>
        /// Every contract this asset can NAME — the ones its declared catalogs hold.
        ///
        /// What an "implements" picker offers, and the reason a def cannot claim a promise out of
        /// a catalog it never declared: an unreachable claim is a broken link wearing a name, and
        /// the moment to catch it is while it is being made.
        /// </summary>
        public static void ContractsFor(Object owner, List<ContractDef> into)
        {
            if (into == null)
                return;
            into.Clear();

            var reachable = new List<StateTreeRegistryAsset>();
            ReachableRegistries(owner, reachable);
            for (int i = 0; i < reachable.Count; i++)
            {
                StateTreeRegistryAsset registry = reachable[i];
                if (registry == null)
                    continue;
                for (int j = 0; j < registry.Count; j++)
                {
                    if (registry.EntryAt(j) is ContractDef contract
                        && !string.IsNullOrEmpty(contract.name)
                        && !into.Contains(contract))
                        into.Add(contract);
                }
            }
        }

        /// <summary>The contract of that name in this asset's neighbourhood, or null — how a field
        /// marked with a contract NAME finds the row behind it.</summary>
        public static ContractDef ContractNamed(string name, Object owner)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var contracts = new List<ContractDef>();
            ContractsFor(owner, contracts);
            for (int i = 0; i < contracts.Count; i++)
            {
                if (contracts[i].name == name)
                    return contracts[i];
            }
            return null;
        }

        /// <summary>
        /// Every row of a given KIND this asset can name — the attribute catalogs it declares,
        /// the contracts, the items. The general form of the neighbourhood rule, for a picker
        /// that knows what shape it wants but not which catalog holds it.
        /// </summary>
        public static void RowsOfKind<TRow>(Object owner, List<TRow> into)
            where TRow : StateTreeRegistryEntry
        {
            if (into == null)
                return;
            into.Clear();

            var reachable = new List<StateTreeRegistryAsset>();
            ReachableRegistries(owner, reachable);
            for (int i = 0; i < reachable.Count; i++)
            {
                StateTreeRegistryAsset registry = reachable[i];
                if (registry == null)
                    continue;
                for (int j = 0; j < registry.Count; j++)
                {
                    if (registry.EntryAt(j) is TRow row && !string.IsNullOrEmpty(row.name)
                        && !into.Contains(row))
                        into.Add(row);
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
