using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>What a declared key holds — including the kinds that are POLICY, not
    /// storage: an <see cref="Event"/> is a presence key (the no-event-bus doctrine as a
    /// type: raised by writing it, consumed by clearing it), a <see cref="Tag"/> is world
    /// vocabulary (matched by the registry, never stored on a blackboard), and a
    /// <see cref="Screen"/> is a UI address (matched by the UI service's address book).</summary>
    public enum StateTreeKeyKind
    {
        Float,
        String,
        Bool,
        Object,
        Event,
        Tag,
        Screen
    }

    /// <summary>
    /// One declared key in a tree's header — the M12 contract entry. The ID is the identity
    /// (minted once, never edited); the NAME is what runtime dictionaries and graphs actually
    /// use and is freely renameable, because every wired use references the id and is
    /// rewritten to the current name when the tree starts. Keys are declared ONLY here, on
    /// trees: a scope's contract is its mounted tree's header, and another tree's vocabulary
    /// is imported through <see cref="StateTreeAsset.uses"/> — sharing follows structure,
    /// never a registry.
    /// </summary>
    [Serializable]
    public sealed class StateTreeKeyDeclaration
    {
        /// <summary>Stable identity, minted by the editor when the row is added.</summary>
        public string id = "";

        /// <summary>The runtime key text — display name and dictionary key in one, safe to
        /// rename because uses are id-wired.</summary>
        public string name = "";

        public StateTreeKeyKind kind = StateTreeKeyKind.Float;

        /// <summary>What this key MEANS — shown wherever the key is offered.</summary>
        public string description = "";

        /// <summary>String kind, optional (§4d): the value NAMES A ROW of this registry —
        /// "a string" becomes "an item of M21Items", so tools can offer rows and validators
        /// can refuse names that exist nowhere. Null = a free string, as before.</summary>
        public StateTreeRegistryAsset namesRowOf;

        /// <summary>Object kind, optional (§4d): the payload's type name — the CONTRACT a
        /// routed result landing here must honour (checked at the landing site, loudly).
        /// A state can then hand a whole result class forward under one key: the contract
        /// grows by growing the class, never by adding keys or rewiring readers.</summary>
        public string payloadTypeName = "";
    }

    /// <summary>
    /// THE key-semantic field (M14) — the wire lives IN the field, the way
    /// <see cref="StateTreeEntryRef{TEntry}"/> carries its own reference: no parallel row
    /// on the node, no field-by-index addressing, nothing for editor and executor to keep
    /// in agreement. <see cref="text"/> is the authored runtime string and the fallback;
    /// <see cref="keyId"/> wires it to a declared key (empty = free-typed, still legal).
    /// The executor resolves the id to the declaration's CURRENT name at StartTree, so
    /// renames stay free; atom bodies read the field as a plain string through the
    /// implicit conversion and never know any of this happened. There is deliberately no
    /// implicit conversion FROM string: replacing the wrapper would silently discard the
    /// wire, so writing <see cref="text"/> is an explicit act.
    /// </summary>
    [Serializable]
    public sealed class StateTreeKeyField
    {
        public string text = "";

        public string keyId = "";

        [NonSerialized]
        private string m_Resolved;

        public StateTreeKeyField()
        {
        }

        public StateTreeKeyField(string text)
        {
            this.text = text ?? "";
        }

        /// <summary>What runs: the declaration's current name once the executor resolved
        /// the wire, the authored text otherwise.</summary>
        public string Value => m_Resolved ?? text;

        public bool isWired => !string.IsNullOrEmpty(keyId);

        public static implicit operator string(StateTreeKeyField field)
        {
            return field != null ? field.Value : "";
        }

        public override string ToString()
        {
            return Value;
        }

        /// <summary>Executor-only: the resolved name for THIS deep copy. Null clears back
        /// to the authored text.</summary>
        public void BindResolved(string name)
        {
            m_Resolved = name;
        }
    }

    /// <summary>Marks a string field as KEY-SEMANTIC — a blackboard/context key or a world
    /// tag — so the inspector offers the declaration picker there and validation knows what
    /// the text means. Unmarked string fields are just text. <see cref="any"/> is for the
    /// GENERIC atoms (set/get/has-key): their field works on a key of every kind — clearing
    /// an event, presence-testing a string — so the picker offers all declarations, with
    /// <see cref="kind"/> demoted to the "most likely" hint that sorts them.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StateTreeKeyAttribute : Attribute
    {
        public StateTreeKeyAttribute(StateTreeKeyKind kind = StateTreeKeyKind.Float,
            bool any = false)
        {
            this.kind = kind;
            this.any = any;
        }

        public StateTreeKeyKind kind { get; }

        public bool any { get; }
    }

    /// <summary>
    /// The one search both the editor's pickers and the executor's resolve share, so what is
    /// OFFERED and what RESOLVES can never disagree: own declarations first, then the
    /// <c>uses</c> imports (cycle-guarded), then — given an owner — up the mount chain, each
    /// host's tree with ITS imports. Nearest declaration wins, which is the same
    /// nearest-scope rule the context atoms already follow.
    /// </summary>
    public static class StateTreeKeyResolver
    {
        public static StateTreeKeyDeclaration Find(StateTreeAsset tree, GameObject owner,
            string keyId)
        {
            if (string.IsNullOrEmpty(keyId))
                return null;

            var visited = new HashSet<StateTreeAsset>();
            StateTreeKeyDeclaration found = FindInTree(tree, keyId, visited);
            if (found != null)
                return found;

            StateTreeContextHost host = owner != null
                ? StateTreeContextHost.ResolveNearest(owner)
                : null;
            int guard = 0;
            while (host != null && ++guard < 32)
            {
                found = FindInTree(host.tree, keyId, visited);
                if (found != null)
                    return found;
                host = host.ParentHost;
            }
            return null;
        }

        /// <summary>Every declaration visible FROM a tree at edit time (own + imports) — what
        /// the picker lists. The mount chain is a runtime fact an asset cannot see; importing
        /// an ancestor's tree is how its keys appear in the picker too.</summary>
        public static void CollectVisible(StateTreeAsset tree,
            List<StateTreeKeyDeclaration> into)
        {
            var visited = new HashSet<StateTreeAsset>();
            Collect(tree, into, visited);
        }

        private static StateTreeKeyDeclaration FindInTree(StateTreeAsset tree, string keyId,
            HashSet<StateTreeAsset> visited)
        {
            if (tree == null || !visited.Add(tree))
                return null;

            var keys = tree.keys;
            for (int i = 0; keys != null && i < keys.Count; i++)
            {
                if (keys[i] != null
                    && string.Equals(keys[i].id, keyId, StringComparison.Ordinal))
                    return keys[i];
            }

            var uses = tree.uses;
            for (int i = 0; uses != null && i < uses.Count; i++)
            {
                StateTreeKeyDeclaration found = FindInTree(uses[i], keyId, visited);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static void Collect(StateTreeAsset tree, List<StateTreeKeyDeclaration> into,
            HashSet<StateTreeAsset> visited)
        {
            if (tree == null || !visited.Add(tree))
                return;
            var keys = tree.keys;
            for (int i = 0; keys != null && i < keys.Count; i++)
            {
                if (keys[i] != null)
                    into.Add(keys[i]);
            }
            var uses = tree.uses;
            for (int i = 0; uses != null && i < uses.Count; i++)
                Collect(uses[i], into, visited);
        }
    }
}
