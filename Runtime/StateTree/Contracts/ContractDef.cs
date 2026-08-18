using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A CONTRACT (M30.2) — what a thing promises to be, as a row.
    ///
    /// C#'s idea, kept honest: "damageable", "openable", "carryable" is a NAME plus the surface
    /// that name implies — the requests an implementer must serve and the attributes it must
    /// have. A def that claims it can be checked against it; a field can ask for it instead of
    /// asking for a concrete def, and the picker then offers exactly the things that keep the
    /// promise.
    ///
    /// IT IS RUNTIME-REAL, not an editor filter. <see cref="facetTypeName"/> names the C# type
    /// the promise resolves to on a live body — the type <see cref="WorldObjectBehaviour.As{T}"/>
    /// has always been able to hand back — so "give me the damageable facet of that object" is a
    /// thing a task can actually do. A contract with no facet type is still checkable as data
    /// (requests and attributes), it just cannot be dereferenced; that is a real state, not a
    /// broken one, and the difference is worth being able to see.
    ///
    /// AND CODE MAY EXTEND IT. An interface marked <see cref="StateTreeContractAttribute"/>
    /// satisfies the contract of the same name without anybody authoring a row for it — the edge
    /// case where a promise is easier to state in C# than in data, which this toolset should
    /// meet rather than argue with.
    /// </summary>
    [Serializable]
    public sealed class ContractDef : StateTreeRegistryEntry
    {
        [Tooltip("What to call it in a picker — 'Damageable', 'Openable'.")]
        public string displayName = "";

        [TextArea]
        [Tooltip("What keeping this promise MEANS, for whoever is about to claim it.")]
        public string description = "";

        [Tooltip("The requests an implementer must serve — by key, the way callers ask.")]
        public List<string> requests = new List<string>();

        [Tooltip("The attributes an implementer must have — by name.")]
        public List<string> attributes = new List<string>();

        [Tooltip("The C# type this promise resolves to on a live body (assembly-qualified or "
            + "plain). Empty = a data-only contract: checkable, not dereferenceable.")]
        public string facetTypeName = "";

        /// <summary>The resolved facet type, or null — cached, because a contract is asked this
        /// question once per body and the answer cannot change without a domain reload.</summary>
        public Type FacetType()
        {
            if (m_Resolved != null || m_Looked)
                return m_Resolved;
            m_Looked = true;
            if (string.IsNullOrEmpty(facetTypeName))
                return null;
            m_Resolved = Type.GetType(facetTypeName);
            if (m_Resolved != null)
                return m_Resolved;
            // Plain names are worth supporting: an author types "IDamageable", not
            // "Ns.IDamageable, Asm, Version=...". One scan, cached like the rest.
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }
                for (int i = 0; i < types.Length; i++)
                {
                    if (types[i].Name == facetTypeName || types[i].FullName == facetTypeName)
                        return m_Resolved = types[i];
                }
            }
            return null;
        }

        [NonSerialized] private Type m_Resolved;
        [NonSerialized] private bool m_Looked;

        public override string Describe()
        {
            string label = string.IsNullOrEmpty(displayName) ? name : displayName;
            int promises = requests.Count + attributes.Count;
            return label + " — " + (promises == 0 ? "a name only" : promises + " promised")
                + (string.IsNullOrEmpty(facetTypeName) ? " (data only)" : " → " + facetTypeName);
        }
    }
}
