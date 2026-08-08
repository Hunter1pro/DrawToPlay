using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One row of a data registry — the M13 data model, on the HeavenlyTreasures rule: an
    /// entry is a PLAIN serializable C# class in a list, never a ScriptableObject per item.
    /// The ID is the identity (minted when the row is added, what every typed reference
    /// stores); the NAME is the runtime string — inventory keys, clicked-item routing, deep
    /// logs — and is freely renameable because references are id-wired, the same split
    /// declared keys made. <see cref="group"/> is a path ("weapons/melee") that exists only
    /// to organize: dashboards section by it and entry pickers submenu by it.
    /// </summary>
    [Serializable]
    public class StateTreeRegistryEntry
    {
        public string id = "";

        public string name = "";

        public string group = "";
    }

    /// <summary>
    /// The non-generic face of a data registry — what tree headers list, what the executor
    /// and the editor talk to without knowing the entry type. A registry is the §3.7 DATA
    /// row grown a spine: what can exist, as rows with identity, connected to trees by
    /// listing the asset in <see cref="StateTreeAsset.registries"/> — never by a string a
    /// task happens to hold.
    /// </summary>
    public abstract class StateTreeRegistryAsset : ScriptableObject
    {
        /// <summary>The entry class this registry's rows are — how typed references and
        /// pickers decide which registry answers for them.</summary>
        public abstract Type entryType { get; }

        public abstract int Count { get; }

        public abstract StateTreeRegistryEntry EntryAt(int index);

        /// <summary>Id lookup; null for null/empty/unknown. Linear on purpose — a registry
        /// big enough to need an index is big enough to deserve that decision being made
        /// then, on real numbers.</summary>
        public StateTreeRegistryEntry FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < Count; i++)
            {
                StateTreeRegistryEntry entry = EntryAt(i);
                if (entry != null && string.Equals(entry.id, id, StringComparison.Ordinal))
                    return entry;
            }
            return null;
        }

        /// <summary>Ordinal NAME lookup — the runtime-string path, for the fields that stay
        /// dynamic (a blackboard-routed click) and resolve at tick time.</summary>
        public StateTreeRegistryEntry FindByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            for (int i = 0; i < Count; i++)
            {
                StateTreeRegistryEntry entry = EntryAt(i);
                if (entry != null && string.Equals(entry.name, name, StringComparison.Ordinal))
                    return entry;
            }
            return null;
        }
    }

    /// <summary>
    /// A registry KIND is this subclassed with its entry class — and nothing else: the
    /// dashboard inspector, the tree-header Data list and the entry pickers all work by
    /// reflection over <see cref="StateTreeRegistryAsset.entryType"/>, so declaring a new
    /// kind of thing (quests, recipes, weapon archetypes) is one entry class plus one
    /// one-line asset class.
    /// </summary>
    public abstract class StateTreeRegistry<TEntry> : StateTreeRegistryAsset
        where TEntry : StateTreeRegistryEntry
    {
        public List<TEntry> entries = new List<TEntry>();

        public override Type entryType => typeof(TEntry);

        public override int Count => entries.Count;

        public override StateTreeRegistryEntry EntryAt(int index)
        {
            return index >= 0 && index < entries.Count ? entries[index] : null;
        }

        public bool TryGet(string name, out TEntry entry)
        {
            entry = FindByName(name) as TEntry;
            return entry != null;
        }
    }

    /// <summary>The executor's view of a typed reference field — what the StartTree
    /// injection pass talks to, so it never needs the generic argument.</summary>
    public interface IStateTreeEntryRef
    {
        string EntryId { get; }

        /// <summary>The serialized name — the display cache when an id is wired, and the
        /// WHOLE reference when it is not: a name-only reference is the free-typed flavor
        /// (what a graph port authors), resolved by name at StartTree like a free-typed
        /// key runs on its text.</summary>
        string EntryName { get; }

        Type EntryType { get; }

        void Bind(StateTreeRegistryEntry entry);
    }

    /// <summary>
    /// THE typed entry reference — the M13 field type a task declares instead of an id
    /// string or a bare Object slot, so the shallow-string problem cannot be rebuilt on the
    /// object side. Serialized as the entry's id (the wire) plus a name cache (what a YAML
    /// reader sees; never trusted at runtime). The executor resolves it against the tree's
    /// <see cref="StateTreeAsset.registries"/> at StartTree and injects the live entry —
    /// <see cref="entry"/> is null until then, and stays null for an id that resolves
    /// nowhere (one error is logged; the task's own guard decides what failing means).
    /// </summary>
    [Serializable]
    public sealed class StateTreeEntryRef<TEntry> : IStateTreeEntryRef
        where TEntry : StateTreeRegistryEntry
    {
        public string entryId = "";

        public string entryName = "";

        [NonSerialized]
        private TEntry m_Entry;

        /// <summary>The live entry, injected at StartTree. Null when unset or unresolved.</summary>
        public TEntry entry => m_Entry;

        public bool isSet => !string.IsNullOrEmpty(entryId);

        string IStateTreeEntryRef.EntryId => entryId;

        string IStateTreeEntryRef.EntryName => entryName;

        Type IStateTreeEntryRef.EntryType => typeof(TEntry);

        void IStateTreeEntryRef.Bind(StateTreeRegistryEntry entry)
        {
            m_Entry = entry as TEntry;
        }
    }

    /// <summary>The executor's view of a whole-registry field — resolution is purely by
    /// entry type against the tree's list, so there is nothing to serialize.</summary>
    public interface IStateTreeRegistryRef
    {
        Type EntryType { get; }

        void Bind(StateTreeRegistryAsset registry);
    }

    /// <summary>
    /// A typed reference to a WHOLE registry — for the atoms that speak about the catalog
    /// itself (list the inventory, look a routed name up) rather than one row. Resolved at
    /// StartTree to the FIRST registry in the tree's list whose entries are
    /// <typeparamref name="TEntry"/>, so the task carries no asset slot to mis-wire; a tree
    /// that lists no matching registry gets one error and a null.
    /// </summary>
    [Serializable]
    public sealed class StateTreeRegistryRef<TEntry> : IStateTreeRegistryRef
        where TEntry : StateTreeRegistryEntry
    {
        [NonSerialized]
        private StateTreeRegistry<TEntry> m_Registry;

        /// <summary>The live registry, injected at StartTree. Null until then, or when the
        /// tree lists none of this entry type.</summary>
        public StateTreeRegistry<TEntry> registry => m_Registry;

        public bool TryGet(string name, out TEntry entry)
        {
            entry = null;
            return m_Registry != null && m_Registry.TryGet(name, out entry);
        }

        Type IStateTreeRegistryRef.EntryType => typeof(TEntry);

        void IStateTreeRegistryRef.Bind(StateTreeRegistryAsset registry)
        {
            m_Registry = registry as StateTreeRegistry<TEntry>;
        }
    }
}
