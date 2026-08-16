using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The ability domain's SERVICE half (M23): mounted on the scope its
    /// <see cref="ServiceDef"/> names, it answers "which row is that?" and "may this host
    /// start it?" — the reads every actor shares. Per-actor state (the active ability, its
    /// executor, cooldowns, statuses) lives on each actor's <see cref="AbilityHost"/>; this
    /// class deliberately owns none of it, which is the split both HT generations converged
    /// on (a per-actor host + shared catalog) and the opposite of the per-objective
    /// container-of-132-registrations the Unity side paid for.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Ability Service")]
    public sealed class AbilityService : StateTreeServiceBehaviour
    {
        [Tooltip("The declaration this service runs: scope, the ability registry, and the "
            + "nesting rules its rows obey.")]
        public ServiceDef definition;

        /// <summary>The catalog, through the def — null when the def is missing or names a
        /// registry of some other kind.</summary>
        public AbilityRegistry abilities =>
            definition != null ? definition.registry as AbilityRegistry : null;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (definition == null)
                Debug.LogError("[AbilityService] no ServiceDef assigned — no ability can "
                    + "resolve.", this);
            else if (abilities == null)
                Debug.LogError("[AbilityService] the ServiceDef's registry is not an "
                    + "AbilityRegistry.", this);
        }

        /// <summary>A row by name, or null with the caller left to complain — the caller
        /// knows what it was doing; this method does not.</summary>
        public AbilityDef Find(string abilityName)
        {
            AbilityRegistry registry = abilities;
            return registry != null && !string.IsNullOrEmpty(abilityName)
                ? registry.FindByName(abilityName) as AbilityDef
                : null;
        }

        /// <summary>An effect row, found through the ability registry's dependsOn closure —
        /// the same provenance chain the pickers offer from, so a name that resolves here is
        /// a name that could have been picked.</summary>
        public EffectDef FindEffect(string effectName)
        {
            return FindInClosure(effectName) as EffectDef;
        }

        /// <summary>A cue row, through the same closure (the effect registry lists the cue
        /// registry in ITS dependsOn — a chain of declarations, never a typed path).</summary>
        public CueDef FindCue(string cueName)
        {
            return FindInClosure(cueName) as CueDef;
        }

        /// <summary>A progression row, through the same closure — how a scaled effect's
        /// curve resolves (the effect registry lists the table in dependsOn).</summary>
        public ProgressionRow FindProgression(string rowName)
        {
            return FindInClosure(rowName) as ProgressionRow;
        }

        private StateTreeRegistryEntry FindInClosure(string entryName)
        {
            if (string.IsNullOrEmpty(entryName) || definition == null
                || definition.registry == null)
                return null;

            var reachable = new System.Collections.Generic.List<StateTreeRegistryAsset>();
            definition.registry.CollectWithDependencies(reachable);
            for (int i = 0; i < reachable.Count; i++)
            {
                StateTreeRegistryEntry entry = reachable[i].FindByName(entryName);
                if (entry != null && reachable[i] != definition.registry)
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// The activation GATE — the four-tag policy's read side. Refused when the def is on
        /// cooldown for this host, when it is already the active ability, or when the active
        /// ability's block tags name any of its ability tags. An active ability the incoming
        /// one CANCELS (its cancel tags name the active's ability tags) does not refuse — the
        /// host tears the active one down as part of activating.
        /// </summary>
        public bool CanActivate(AbilityHost host, AbilityDef def)
        {
            if (host == null || def == null)
                return false;
            if (!host.CooldownReady(def))
                return false;

            // WHAT THE OWNER IS, before what it is doing (M26). blockTags is one ability
            // holding another off; this is the ACTOR's own standing state refusing the verb —
            // afloat, silenced, bound. Without it a mode had no way to take a land ability
            // away except by rewriting the tree that offers it, which is the tree learning
            // about boats. The row says "not while Aboard" and every path to the ability
            // obeys, including the ones written later.
            for (int i = 0; i < def.blockedByTags.Count; i++)
            {
                if (!string.IsNullOrEmpty(def.blockedByTags[i])
                    && host.HasTag(def.blockedByTags[i]))
                    return false;
            }

            // AND WHAT THE OWNER MUST BE. The other half of the same idea, and what lets an
            // ability belong to a mode instead of to a tree: the ship's cannon is refused on
            // land by the row saying so, not by the land states carefully never offering it.
            for (int i = 0; i < def.requiredTags.Count; i++)
            {
                if (!string.IsNullOrEmpty(def.requiredTags[i])
                    && !host.HasTag(def.requiredTags[i]))
                    return false;
            }

            AbilityDef active = host.active;
            if (active == null)
                return true;
            if (active == def)
                return false;
            if (Overlaps(active.blockTags, def.abilityTags))
                return false;
            // One active ability per host — the FSM reading both HT generations used. An
            // incoming ability that neither cancels nor is blocked still WAITS its turn.
            return Overlaps(def.cancelTags, active.abilityTags);
        }

        /// <summary>Whether activating <paramref name="def"/> must cancel the host's active
        /// ability first.</summary>
        public bool Cancels(AbilityDef def, AbilityDef active)
        {
            return def != null && active != null && Overlaps(def.cancelTags, active.abilityTags);
        }

        private static bool Overlaps(System.Collections.Generic.List<string> a,
            System.Collections.Generic.List<string> b)
        {
            if (a == null || b == null)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (string.IsNullOrEmpty(a[i]))
                    continue;
                for (int j = 0; j < b.Count; j++)
                {
                    if (string.Equals(a[i], b[j], System.StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }
    }
}
