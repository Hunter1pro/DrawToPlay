using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>Where a "+ New" was chosen: the tree and the state the field belongs to, when the picker knows
    /// them (a step's parameter does; a plain field may not).</summary>
    public sealed class StateTreeRowContext
    {
        public StateTreeRowContext(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            this.tree = tree;
            this.node = node;
        }

        public StateTreeAsset tree { get; }

        public StateTreeNodeAsset node { get; }
    }

    /// <summary>One "+ New" a game's maker answers: the registry, where it was chosen and where the picker was.
    /// <see cref="Make"/> runs the making — the row recorded for undo by whoever builds it — as one undo step
    /// with the pick, and picks it.</summary>
    public sealed class StateTreeRowRequest
    {
        private readonly Func<Func<StateTreeRegistryEntry>, StateTreeRegistryEntry> m_Make;

        public StateTreeRowRequest(StateTreeRegistryAsset registry, StateTreeRowContext context, Rect at,
            Func<Func<StateTreeRegistryEntry>, StateTreeRegistryEntry> make)
        {
            this.registry = registry;
            this.context = context ?? new StateTreeRowContext(null, null);
            this.at = at;
            m_Make = make;
        }

        public StateTreeRegistryAsset registry { get; }

        public StateTreeRowContext context { get; }

        public Rect at { get; }

        /// <summary>The row <paramref name="build"/> returns, picked; null when it made none.</summary>
        public StateTreeRegistryEntry Make(Func<StateTreeRegistryEntry> build) => m_Make != null ? m_Make(build) : null;
    }

    /// <summary>
    /// A game's own "+ New" for a registry's rows, where a name alone does not make a usable row (a task is its
    /// words). It asks what it needs — a small window of its own — and calls <see cref="StateTreeRowRequest.Make"/>.
    /// Every picker that offers "+ New" in that registry offers this instead.
    /// </summary>
    public interface IStateTreeRowMaker
    {
        bool Makes(StateTreeRegistryAsset registry);

        /// <summary>The menu line, "+ New task…".</summary>
        string Label(StateTreeRegistryAsset registry);

        void Ask(StateTreeRowRequest request);
    }

    /// <summary>The makers a game registered, from an <c>[InitializeOnLoad]</c> class of its own (a domain reload
    /// clears them, and the game's class registers again).</summary>
    public static class StateTreeRowMakers
    {
        private static readonly List<IStateTreeRowMaker> s_Makers = new List<IStateTreeRowMaker>();

        public static void Register(IStateTreeRowMaker maker)
        {
            if (maker != null && !s_Makers.Contains(maker))
                s_Makers.Add(maker);
        }

        public static void Unregister(IStateTreeRowMaker maker) => s_Makers.Remove(maker);

        /// <summary>The first maker that makes this registry's rows, or null.</summary>
        public static IStateTreeRowMaker For(StateTreeRegistryAsset registry)
        {
            if (registry == null)
                return null;
            foreach (IStateTreeRowMaker maker in s_Makers)
            {
                if (maker.Makes(registry))
                    return maker;
            }
            return null;
        }
    }
}
