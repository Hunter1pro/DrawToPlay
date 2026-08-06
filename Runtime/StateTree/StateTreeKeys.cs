using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>What a declared key holds — including the two kinds that are POLICY, not
    /// storage: an <see cref="Event"/> is a presence key (the no-event-bus doctrine as a
    /// type: raised by writing it, consumed by clearing it), a <see cref="Tag"/> is world
    /// vocabulary (matched by the registry, never stored on a blackboard).</summary>
    public enum StateTreeKeyKind
    {
        Float,
        String,
        Bool,
        Object,
        Event,
        Tag
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
    }

    /// <summary>
    /// One wire from a declared key to a task/condition string field — the same addressing
    /// scheme as <see cref="StateTreeFieldBinding"/>, because it is the same problem: point
    /// at a field on this node's task list or a transition's condition. The field itself
    /// keeps holding the plain string (ports, graphs and the VM never change); this row is
    /// what lets the executor overwrite that string with the declaration's CURRENT name at
    /// StartTree, which is what makes renames free.
    /// </summary>
    [Serializable]
    public sealed class StateTreeKeyLink
    {
        public StateTreeFieldBinding.TargetKind targetKind = StateTreeFieldBinding.TargetKind.Task;

        public int targetIndex;

        public string fieldName = "";

        public string keyId = "";
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
