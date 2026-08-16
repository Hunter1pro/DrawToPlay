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
        /// <summary>The kind a TREE ROOT answers to in every service's nesting rules — a
        /// well-known constant, so "what may sit at the top level" is a rule like any other:
        /// the ability service declares root → ability → effect → cue, and Add Child on the
        /// root creates an ability, never an effect (the review that put this here).</summary>
        public const string TreeRootKind = "root";

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

        [Tooltip("The subsystem's OWN flow tree (the UI wiring brief §4b), run by the "
            + "service on its scope for as long as it lives. Empty = the service has no "
            + "flows.")]
        public StateTreeAsset flows;

        /// <summary>
        /// THE REQUEST API (§4c) — what this subsystem answers to, declared where the
        /// subsystem is declared. One row per request: the blackboard key that asks, the
        /// flow state that serves, the sentence that explains. The service runner derives
        /// the plumbing FROM these rows — entry when the key appears, consume when the
        /// state exits — so the flow tree authors only what a request MEANS. Declaration
        /// order is priority when several requests are pending.
        /// </summary>
        public List<ServiceRequest> requests = new List<ServiceRequest>();

        /// <summary>The declared request for a key, or null.</summary>
        public ServiceRequest RequestFor(string key)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                ServiceRequest row = requests[i];
                if (row != null && string.Equals(row.key, key, StringComparison.Ordinal))
                    return row;
            }
            return null;
        }

        /// <summary>
        /// What may nest under what, per KIND — declared rows a service's validators and
        /// pickers read. The ability service no longer nests data (its structure went typed:
        /// effect rows referencing cue rows, unrepresentable errors instead of refused ones),
        /// but the mechanism stays for services whose rows do compose (objectives).
        /// </summary>
        public List<ServiceNestingRule> nestingRules = new List<ServiceNestingRule>();

        /// <summary>
        /// What a freshly created child of each KIND is born holding — the task the editor
        /// seeds into a rule-typed state (an 'effect' state arrives with an ApplyEffectTask,
        /// a 'cue' state with a ShowCueTask), so Add Child creates a THING, not an empty box
        /// to fill from memory. Declared here, not hard-coded in the editor: a different
        /// service seeds different atoms.
        /// </summary>
        public List<ServiceKindSeed> kindSeeds = new List<ServiceKindSeed>();

        /// <summary>The task type name a state of this kind is seeded with, or empty.</summary>
        public string SeedTaskFor(string kind)
        {
            for (int i = 0; i < kindSeeds.Count; i++)
            {
                ServiceKindSeed seed = kindSeeds[i];
                if (seed != null && string.Equals(seed.kind, kind, StringComparison.Ordinal))
                    return seed.taskTypeName ?? "";
            }
            return "";
        }

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

    /// <summary>One declared request: the key that asks, the flow state that serves,
    /// the sentence that explains. A row of the subsystem's API (§4c).</summary>
    [Serializable]
    public sealed class ServiceRequest
    {
        [Tooltip("The blackboard key that triggers this request — the name callers write.")]
        public string key = "";

        [Tooltip("The nodeId of the flow state that serves it.")]
        public string stateId = "";

        [Tooltip("What asking this DOES — shown wherever the API is offered.")]
        public string description = "";
    }

    /// <summary>One kind's birth gift: the task a rule-typed state starts with.</summary>
    [Serializable]
    public sealed class ServiceKindSeed
    {
        public string kind = "";

        [Tooltip("Full or simple type name of a StateTreeTaskAsset — resolved by the editor "
            + "when the state is created.")]
        public string taskTypeName = "";
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
