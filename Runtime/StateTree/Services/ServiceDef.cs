using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A DOMAIN SERVICE, DECLARED (M23, brief §10.3) — the generalization of what an ability
    /// system and an objective system are: not a monolith of code, but a scope + a registry of
    /// nouns + rules its data obeys + trees that do the work. The two HeavenlyTreasures
    /// generations each proved one half of why this asset exists: the Godot side showed that
    /// rules-as-data (tag channels, kind nesting, params with defaults) is what makes design
    /// fast; the Unity side documented what the alternative costs — one 416-line entry file of
    /// 132 hand-written container registrations per objective flavor.
    ///
    /// The def DECLARES; it never runs. A service behaviour (e.g. <c>AbilityService</c>)
    /// mounts on the scope's context host carrying one of these, and reads its registry and
    /// rules — so writing a NEW domain service is authoring a def plus small tasks, not
    /// re-implementing activation gates and validation from scratch.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Services/Service Definition",
        fileName = "ServiceDef")]
    public sealed class ServiceDef : ScriptableObject
    {
        [Tooltip("What this service is called in diagnostics and pickers.")]
        public string serviceName = "";

        [Tooltip("Which context scope the service mounts on — where its state lives and dies. "
            + "A level service resets with the level; a root service survives travel.")]
        public StateTreeContextKind scope = StateTreeContextKind.Level;

        [Tooltip("The service's NOUNS — the registry whose rows are what this service manages "
            + "(abilities, objectives, equipment slots). Its dependsOn names what rows may "
            + "reference.")]
        public StateTreeRegistryAsset registry;

        [Tooltip("The tree kind this service's rows may name — 'ability' for the ability "
            + "service, so ONE ability is ONE tree and a row pointing at some other domain's "
            + "tree is a validation finding, not a runtime surprise. Empty = unchecked.")]
        public string treeKind = "";

        /// <summary>
        /// What may nest under what, per KIND — declared rows a service's validators and
        /// pickers read. The ability service no longer nests data (its structure went typed:
        /// effect rows referencing cue rows, unrepresentable errors instead of refused ones),
        /// but the mechanism stays for services whose rows do compose (objectives).
        /// </summary>
        public List<ServiceNestingRule> nestingRules = new List<ServiceNestingRule>();

        /// <summary>Whether <paramref name="childKind"/> may sit under
        /// <paramref name="parentKind"/>. No rule for the parent = leaf = nothing may.</summary>
        public bool Allows(string parentKind, string childKind)
        {
            ServiceNestingRule rule = RuleFor(parentKind);
            return rule != null && rule.childKinds != null
                && rule.childKinds.Contains(childKind);
        }

        /// <summary>The kinds legal under <paramref name="parentKind"/> — what a picker offers.
        /// Empty for a leaf kind.</summary>
        public IReadOnlyList<string> AllowedUnder(string parentKind)
        {
            ServiceNestingRule rule = RuleFor(parentKind);
            return rule != null && rule.childKinds != null
                ? (IReadOnlyList<string>)rule.childKinds
                : Array.Empty<string>();
        }

        private ServiceNestingRule RuleFor(string parentKind)
        {
            for (int i = 0; i < nestingRules.Count; i++)
            {
                ServiceNestingRule rule = nestingRules[i];
                if (rule != null && string.Equals(rule.parentKind, parentKind,
                    StringComparison.Ordinal))
                    return rule;
            }
            return null;
        }
    }

    /// <summary>One nesting rule: what kinds may be the children of this kind. Beside
    /// <see cref="ServiceDef"/> like <see cref="StateTreeTransition"/> lives beside the model —
    /// a plain serializable row, not a sub-asset.</summary>
    [Serializable]
    public sealed class ServiceNestingRule
    {
        [Tooltip("The parent kind this rule is about. The registry row itself is its "
            + "service's root kind (e.g. 'ability').")]
        public string parentKind = "";

        [Tooltip("Kinds allowed directly beneath it. A kind listed nowhere is a leaf.")]
        public List<string> childKinds = new List<string>();
    }
}
