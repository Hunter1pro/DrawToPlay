using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>What a step parameter's section is told: the step, the template it runs, the parameter, and the
    /// row it names with that row's catalog (null when it names none).</summary>
    public sealed class StateTreeStepParameterContext
    {
        public StateTreeStepParameterContext(StateTreeAsset tree, StateTreeNodeAsset node, StateTreeAsset template,
            StateTreeStepParameter parameter, StateTreeRegistryEntry row, StateTreeRegistryAsset registry)
        {
            this.tree = tree;
            this.node = node;
            this.template = template;
            this.parameter = parameter;
            this.row = row;
            this.registry = registry;
        }

        /// <summary>The tree the step lives in.</summary>
        public StateTreeAsset tree { get; }

        public StateTreeNodeAsset node { get; }

        public StateTreeAsset template { get; }

        public StateTreeStepParameter parameter { get; }

        public StateTreeRegistryEntry row { get; }

        public StateTreeRegistryAsset registry { get; }
    }

    /// <summary>
    /// A game's own lines under a step's parameter — the picked row's words, edited right there. Drawn under
    /// the parameter's picker in Mission mode and under its override row in Dev mode. <c>changed</c> redraws the
    /// inspector.
    /// </summary>
    public interface IStateTreeStepParameterSection
    {
        bool Offers(StateTreeStepParameterContext at);

        VisualElement Build(StateTreeStepParameterContext at, Action changed);
    }

    /// <summary>The sections a game registered, from an <c>[InitializeOnLoad]</c> class of its own (a domain reload
    /// clears them, and the game's class registers again).</summary>
    public static class StateTreeStepParameterSections
    {
        private static readonly List<IStateTreeStepParameterSection> s_Sections = new List<IStateTreeStepParameterSection>();

        public static void Register(IStateTreeStepParameterSection section)
        {
            if (section != null && !s_Sections.Contains(section))
                s_Sections.Add(section);
        }

        public static void Unregister(IStateTreeStepParameterSection section) => s_Sections.Remove(section);

        /// <summary>The sections offered at <paramref name="at"/>, built, in the order they were registered.</summary>
        public static List<VisualElement> Build(StateTreeStepParameterContext at, Action changed)
        {
            var built = new List<VisualElement>();
            if (at == null)
                return built;
            foreach (IStateTreeStepParameterSection section in s_Sections)
            {
                if (!section.Offers(at))
                    continue;
                VisualElement element = section.Build(at, changed);
                if (element != null)
                    built.Add(element);
            }
            return built;
        }
    }
}
