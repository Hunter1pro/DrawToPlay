using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// KEEPING THE PROMISE (M30.2) — the runtime half of contracts, and the reason they are not
    /// merely a filter in a picker.
    ///
    /// Three questions, one place: does this DEF claim the contract (authoring truth), does this
    /// BODY keep it right now (runtime truth), and give me the thing it keeps it WITH (the facet).
    /// The third is what makes a contract worth having — a task can say "the damageable part of
    /// whatever I just hit" instead of hard-typing a component and hoping every future actor has
    /// one.
    ///
    /// The body side leans on <see cref="WorldObjectBehaviour.As(Type)"/>, which has exposed
    /// facets since M20 and was waiting for something to ask. The code side honours
    /// <see cref="StateTreeContractAttribute"/>, so an interface can join the vocabulary without
    /// a row — the edge case, met rather than argued with.
    /// </summary>
    public static class StateTreeContracts
    {
        /// <summary>Does this def CLAIM the contract? Authoring truth: what the asset says about
        /// itself, which is what a picker filters on.</summary>
        public static bool Claims(ServiceDef def, ContractDef contract)
        {
            if (def == null || contract == null)
                return false;
            for (int i = 0; i < def.implements.Count; i++)
            {
                StateTreeEntryRef<ContractDef> claim = def.implements[i];
                if (claim == null)
                    continue;
                if (claim.entryId == contract.id
                    || (!string.IsNullOrEmpty(claim.entryName) && claim.entryName == contract.name))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// What this def FAILS to deliver on a promise it makes — empty when it is honest.
        ///
        /// A claim is not proof: a def can say "damageable" and never serve the requests the
        /// contract names. This is the check a validator runs and an inspector shows, and it is
        /// the difference between a contract and a label.
        /// </summary>
        public static void Missing(ServiceDef def, ContractDef contract, List<string> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (def == null || contract == null)
                return;

            for (int i = 0; i < contract.requests.Count; i++)
            {
                string wanted = contract.requests[i];
                if (!string.IsNullOrEmpty(wanted) && def.RequestFor(wanted) == null)
                    into.Add("request '" + wanted + "'");
            }
            // ATTRIBUTES ARE DECLARED NOW (M30.4), so that is the first place to look: a def
            // that says it HAS health delivers the health a contract asks for, and its derived
            // requests are the surface the promise was really about. A def that declares none
            // falls back to its catalog, which is how this read before attributes existed.
            for (int i = 0; i < contract.attributes.Count; i++)
            {
                string wanted = contract.attributes[i];
                if (string.IsNullOrEmpty(wanted) || Declares(def, wanted))
                    continue;
                if (def.registry == null || def.registry.FindByName(wanted) == null)
                    into.Add("attribute '" + wanted + "'");
            }
        }

        /// <summary>Does this def say it HAS that attribute?</summary>
        private static bool Declares(ServiceDef def, string attribute)
        {
            for (int i = 0; i < def.attributes.Count; i++)
            {
                if (def.attributes[i] != null && def.attributes[i].Name == attribute)
                    return true;
            }
            return false;
        }

        /// <summary>Does this live body keep the contract right now?</summary>
        public static bool Keeps(GameObject body, ContractDef contract)
        {
            return Facet(body, contract) != null;
        }

        /// <summary>
        /// The thing this body keeps the contract WITH, or null.
        ///
        /// Two roads, in the order that answers fastest: the contract's own facet type (the
        /// authored answer), then any component whose type is marked with the contract's name
        /// (the code answer). Both go through the citizen's exposed facets when there is a
        /// citizen, so composition — the object that IS the sum of its parts — resolves the same
        /// way a subclass does.
        /// </summary>
        public static object Facet(GameObject body, ContractDef contract)
        {
            if (body == null || contract == null)
                return null;

            Type facetType = contract.FacetType();
            var citizen = body.GetComponent<WorldObjectBehaviour>();
            if (facetType != null)
            {
                object exposed = citizen != null ? citizen.As(facetType) : null;
                if (exposed != null)
                    return exposed;
                Component direct = body.GetComponent(facetType);
                if (direct != null)
                    return direct;
            }

            // THE CODE SEAM: an interface marked [StateTreeContract("openable")] answers for the
            // contract called "openable", whether or not the row names a facet type.
            Component[] components = body.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                    continue;
                if (MarkedAs(components[i].GetType(), contract.name))
                    return components[i];
            }
            return null;
        }

        /// <summary>Is this type marked as keeping the named contract — itself or through an
        /// interface it implements?</summary>
        public static bool MarkedAs(Type type, string contractName)
        {
            if (type == null || string.IsNullOrEmpty(contractName))
                return false;

            if (!s_Marked.TryGetValue(type, out List<string> names))
            {
                names = new List<string>();
                Collect(type, names);
                foreach (Type contractInterface in type.GetInterfaces())
                    Collect(contractInterface, names);
                s_Marked[type] = names;
            }
            return names.Contains(contractName);
        }

        private static void Collect(Type type, List<string> into)
        {
            object[] marks = type.GetCustomAttributes(typeof(StateTreeContractAttribute), false);
            for (int i = 0; i < marks.Length; i++)
            {
                var mark = (StateTreeContractAttribute)marks[i];
                if (!string.IsNullOrEmpty(mark.contractName) && !into.Contains(mark.contractName))
                    into.Add(mark.contractName);
            }
        }

        /// <summary>Per-type, because reflection over a body's components every time a task asks
        /// "is this openable" is the kind of cost that only shows up in a full level.</summary>
        private static readonly Dictionary<Type, List<string>> s_Marked =
            new Dictionary<Type, List<string>>();
    }
}
