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
    public sealed class ServiceDef : ScriptableObject, IStateTreeNeighbourhood
    {
        /// <summary>The kind a TREE ROOT answers to in every service's nesting rules — a
        /// well-known constant, so "what may sit at the top level" is a rule like any other:
        /// the ability service declares root → ability → effect → cue, and Add Child on the
        /// root creates an ability, never an effect (the review that put this here).</summary>
        public const string TreeRootKind = "root";

        [Tooltip("What this service is called in diagnostics and pickers.")]
        public string serviceName = "";

        [Tooltip("The service class that RUNS this def (§4g) — how tools know which "
            + "action vocabulary the request rows may pick from, and validation knows "
            + "an action the service never declared.")]
        public string serviceTypeName = "";

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
        /// THE PUBLIC REQUEST API (§4c) — what OTHER systems may ask of this subsystem,
        /// declared where the subsystem is declared. One row per request: the blackboard
        /// key that asks, the sentence that explains, and HOW it is served — a flow state
        /// (<c>stateId</c>, for handlers that wait) or def-level handling (§4g:
        /// <c>action</c> + <c>reactions</c>, for the single-frame verb-plus-beats shape).
        /// The runner derives all plumbing from these rows: entry when the key appears,
        /// consume when served. Declaration order is priority when several are pending.
        ///
        /// A request the subsystem sends ITSELF (its own skin's buttons) does not belong
        /// here — the def is a public surface, and self-talk on it is noise every reader
        /// has to learn to ignore. Those live in the service's own code
        /// (<c>DeclareInternalRequests</c>), served by exactly the same machinery.
        /// </summary>
        public List<ServiceRequest> requests = new List<ServiceRequest>();

        /// <summary>
        /// WHAT THIS SUBSYSTEM SPAWNS (§4g) — the UI rows it owns, shown by the service
        /// itself at first Update. A subsystem that declares its screen here is whole the
        /// moment its service mounts: no session tree involvement, no setup step.
        /// </summary>
        public List<StateTreeEntryRef<UiDef>> spawns = new List<StateTreeEntryRef<UiDef>>();

        /// <summary>
        /// WHAT THIS SUBSYSTEM ANNOUNCES (§4g) — the keys it writes for OTHERS, with the
        /// payload contract each carries. Declared on the def (not parasitic on a flow
        /// tree's key list), because the announcement outlives any particular serving
        /// mechanism.
        /// </summary>
        public List<ServiceAnnouncement> announcements = new List<ServiceAnnouncement>();

        /// <summary>
        /// THE BODY THIS DEF OWNS (M30.3) — empty for a subsystem, filled for a thing.
        ///
        /// The def is on top of the world object: the manifest and the world registry see the DEF,
        /// and the def spawns and controls the <see cref="WorldObjectBehaviour"/> underneath.
        /// Which is why this lives here and not on the row that places it — a placement says
        /// WHERE and WHICH ONE, never what kind of thing this is.
        /// </summary>
        public ServiceBody body = new ServiceBody();

        [Tooltip("The CONTRACTS this def claims to keep (M30.2) — 'damageable', 'openable'. A "
            + "field elsewhere can then ask for the promise instead of naming this def, and the "
            + "picker offers whoever keeps it. A claim is checkable: StateTreeContracts.Missing "
            + "says what a def promises and does not deliver.")]
        public List<StateTreeEntryRef<ContractDef>> implements =
            new List<StateTreeEntryRef<ContractDef>>();

        [Tooltip("The catalogs this def DECLARES beyond the one it manages (M30.4) — the "
            + "attribute table it draws from, the contracts it may claim. Same rule as a "
            + "registry's Depends On: what you declare is what your pickers offer.")]
        public List<StateTreeRegistryAsset> declares = new List<StateTreeRegistryAsset>();

        /// <summary>The neighbourhood rule, said by a def — read by every picker through
        /// <see cref="StateTreeOffers"/>.</summary>
        public IReadOnlyList<StateTreeRegistryAsset> DeclaredCatalogs => declares;

        [Tooltip("WHAT THIS DEF HAS (M30.4) — its attributes, from which its read/change "
            + "requests are DERIVED rather than typed. Writable is the permission, and the "
            + "runtime refuses what it forbids.")]
        public List<ServiceAttribute> attributes = new List<ServiceAttribute>();

        /// <summary>
        /// The request for a key — AUTHORED FIRST, then derived from what this def has (M30.4).
        ///
        /// The order matters and is the whole compatibility story: a def that hand-wrote
        /// "health.set" keeps its own row, with its own description and reactions, and the
        /// derived one never shadows it. Everything that validated a request against this
        /// method now validates the derived surface too, with nothing to change.
        /// </summary>
        public ServiceRequest RequestFor(string key)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                ServiceRequest row = requests[i];
                if (row != null && string.Equals(row.key, key, StringComparison.Ordinal))
                    return row;
            }
            return DerivedRequestFor(key);
        }

        /// <summary>
        /// THE ROWS NOBODY TYPES (M30.4) — one per attribute per verb it permits.
        ///
        /// Read and change are the same three sentences for every attribute anybody ever
        /// declares, so writing them out is transcription: the def says it has `health`, and
        /// `health.ask`, `health.set` and `health.add` follow. Read-only attributes derive the
        /// ask alone, which is what makes a derived surface a statement rather than a promise
        /// the runtime has not read.
        ///
        /// Nothing is stored: these are computed from <see cref="attributes"/> every time, so a
        /// renamed attribute renames its requests and a revoked permission removes them, with no
        /// stale row left behind to be served by accident.
        /// </summary>
        public void DerivedRequests(List<ServiceRequest> into)
        {
            if (into == null)
                return;
            into.Clear();
            for (int i = 0; i < attributes.Count; i++)
            {
                ServiceAttribute has = attributes[i];
                string name = has != null ? has.Name : "";
                if (string.IsNullOrEmpty(name))
                    continue;
                into.Add(Derived(name, AskVerb, has));
                if (!has.writable)
                    continue;
                into.Add(Derived(name, SetVerb, has));
                into.Add(Derived(name, AddVerb, has));
            }
        }

        /// <summary>Split a derived key into the attribute and the verb — false when it is not
        /// shaped like one, which is how an authored key with a dot in it stays authored.</summary>
        public static bool SplitDerived(string key, out string name, out string verb)
        {
            name = "";
            verb = "";
            if (string.IsNullOrEmpty(key))
                return false;
            int dot = key.LastIndexOf('.');
            if (dot <= 0 || dot >= key.Length - 1)
                return false;
            name = key.Substring(0, dot);
            verb = key.Substring(dot + 1);
            return verb == AskVerb || verb == SetVerb || verb == AddVerb;
        }

        /// <summary>The derived row for a key, or null — the same answer
        /// <see cref="DerivedRequests"/> gives, without building the list.</summary>
        public ServiceRequest DerivedRequestFor(string key)
        {
            if (!SplitDerived(key, out string name, out string verb))
                return null;

            for (int i = 0; i < attributes.Count; i++)
            {
                ServiceAttribute has = attributes[i];
                if (has == null || has.Name != name)
                    continue;
                // THE PERMISSION IS CHECKED HERE, not only drawn: a read-only attribute has no
                // set row to find, so every caller that validates against RequestFor is refused
                // by the same rule the inspector showed.
                if (verb != AskVerb && !has.writable)
                    return null;
                return Derived(name, verb, has);
            }
            return null;
        }

        /// <summary>Is this key one of the derived ones — the question the runtime asks before
        /// deciding whether to act on an attribute or to write the request onto the board.</summary>
        public bool IsDerived(string key)
        {
            return DerivedRequestFor(key) != null;
        }

        /// <summary>Ask what it is now.</summary>
        public const string AskVerb = "ask";

        /// <summary>Set it outright.</summary>
        public const string SetVerb = "set";

        /// <summary>Move it by an amount, signed.</summary>
        public const string AddVerb = "add";

        private static ServiceRequest Derived(string name, string verb, ServiceAttribute has)
        {
            string what = string.IsNullOrEmpty(has.description) ? name : has.description;
            string says = verb == AskVerb
                ? "Read " + what + " — the answer lands where the caller asks for it."
                : verb == SetVerb
                    ? "Set " + what + " outright."
                    : "Move " + what + " by an amount, signed.";
            return new ServiceRequest
            {
                key = name + "." + verb,
                action = verb,
                description = says + "  (derived from what this def has)"
            };
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

        [Tooltip("The nodeId of the flow state that serves it — for a handler that WAITS "
            + "(§4g). Empty = served at def level through 'action' and 'reactions'.")]
        public string stateId = "";

        [Tooltip("What asking this DOES — shown wherever the API is offered.")]
        public string description = "";

        [Tooltip("Optional (§4d): the request's value NAMES A ROW of this registry — "
            + "'a string' becomes 'an item of M21Items'. Typed callers are validated "
            + "against it; tools can offer rows instead of free text.")]
        public StateTreeRegistryAsset namesRowOf;

        [Tooltip("Def-level serving (§4g): the DOMAIN verb the service interprets for "
            + "this request — 'use', 'wear', 'takeoff'. Empty = no domain action, "
            + "reactions only (a pure UI request like toggle).")]
        public string action = "";

        [Tooltip("Def-level serving (§4g): the UI beats after the action, in order — "
            + "what a flow state's task list said, as rows on the def.")]
        public List<UiReaction> reactions = new List<UiReaction>();
    }

    /// <summary>Marks a string field that names a DECLARED REQUEST of some subsystem —
    /// so authoring surfaces can offer the project's request keys instead of a text box
    /// (a graph pin's picker, an inspector dropdown). The value is still a plain string:
    /// the attribute is an affordance, never a contract.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ServiceRequestKeyAttribute : Attribute
    {
    }

    /// <summary>A service's DOMAIN ACTION, declared (§4g) — the UiVerbContract twin for
    /// the OnRequest hook: what a request row's 'action' may say, readable by tools (the
    /// def inspector's action picker, FlowRules) without reading a switch statement.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ServiceActionContractAttribute : Attribute
    {
        public readonly string action;

        /// <summary>What the request VALUE means to this action — "item name", "slot name".</summary>
        public readonly string valueHint;

        public ServiceActionContractAttribute(string action, string valueHint = "")
        {
            this.action = action;
            this.valueHint = valueHint;
        }
    }

    /// <summary>One UI beat of a def-served request (§4g): call a VERB on a shown row's
    /// views — the UiCallTask, as a row. The argument is the request's value, or a
    /// blackboard key's held value (an announcement's payload travels whole this way).</summary>
    [Serializable]
    public sealed class UiReaction
    {
        [Tooltip("The UI row whose views are called.")]
        public StateTreeEntryRef<UiDef> ui = new StateTreeEntryRef<UiDef>();

        [Tooltip("The verb, in the view's vocabulary — 'toggle', 'flash', 'pulse', …")]
        public string verb = "";

        [Tooltip("Pass the request's VALUE as the verb's argument.")]
        public bool valueArgument = true;

        [Tooltip("Optional: a blackboard key whose held value rides along — a string "
            + "becomes the argument, anything richer the PAYLOAD (an announcement's "
            + "contract object, handed to the skin whole).")]
        public string argumentKey = "";
    }

    /// <summary>One announced key (§4g): what the subsystem WRITES for others, with its
    /// payload contract — the outbound half of the API, beside the requests.</summary>
    [Serializable]
    public sealed class ServiceAnnouncement
    {
        [Tooltip("The blackboard key the subsystem writes.")]
        public string key = "";

        [Tooltip("The payload's type name for contract keys — 'ItemUseResult'. Empty = "
            + "a plain value.")]
        public string payloadTypeName = "";

        [Tooltip("What landing here MEANS — shown wherever the API is offered.")]
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
