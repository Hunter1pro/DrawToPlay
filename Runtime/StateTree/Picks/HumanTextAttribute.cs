using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// This string is words a person reads — a line, a label, a verb on a HUD — and the one kind of
    /// string a task, condition or template parameter may hold as free text. Everything else a string
    /// field holds names something (a placement, a tag, a row, a key, a scope) and is picked.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HumanTextAttribute : Attribute
    {
    }

    /// <summary>
    /// This string is a literal datum the runtime writes or compares verbatim — the value a key is
    /// set to — not a reference to anything. Allowed as free text; a key whose declaration names a
    /// row catalog is the way to make such a value pickable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class LiteralValueAttribute : Attribute
    {
    }

    /// <summary>A string that names a row of a catalog of <see cref="entryType"/> — stored by the
    /// row's name (what the runtime compares), picked from the catalogs the asset can reach.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RowNameAttribute : UnityEngine.PropertyAttribute
    {
        public RowNameAttribute(Type entryType)
        {
            this.entryType = entryType;
        }

        public Type entryType { get; }
    }

    /// <summary>Picking nothing is a choice here, and this is what it means — a step may leave the
    /// reference empty, where any other picked reference left empty is a step not finished.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NoneMeansAttribute : Attribute
    {
        public NoneMeansAttribute(string meaning)
        {
            this.meaning = meaning;
        }

        public string meaning { get; }
    }

    /// <summary>A string that names a blackboard key — picked from the declared keys (a tree's
    /// keys, a registry's, a <see cref="KeyCatalogAttribute"/> class's constants), never typed.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class KeyNameAttribute : UnityEngine.PropertyAttribute
    {
    }

    /// <summary>A string that names a context scope's id — "" for the nearest of its kind, or a
    /// <see cref="StateTreeContextHost.contextId"/> / level row's name.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ScopeIdAttribute : UnityEngine.PropertyAttribute
    {
    }

    /// <summary>A string that names a state of the sub-tree its task runs.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SubTreeStateAttribute : UnityEngine.PropertyAttribute
    {
    }

    /// <summary>
    /// Every public constant string of this class is a declared key name — how a game whose keys
    /// live in code (one class of constants) offers them to every key picker.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class KeyCatalogAttribute : Attribute
    {
    }
}
