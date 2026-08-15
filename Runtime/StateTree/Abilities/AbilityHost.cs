using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ACTOR'S ABILITY STATE (M23) — the per-actor half of the ability service, the
    /// component both HT generations converged on: the shared catalog answers what an ability
    /// IS, this host owns what is RUNNING here. It runs the active ability's tree on its own
    /// executor (own context — two parallel executors must not share a parameter scope),
    /// carries the statuses effects leave on this actor, counts cooldowns, and is where
    /// EFFECTS LAND: <see cref="ApplyEffect"/> is called by <c>ApplyEffectTask</c> with this
    /// host as the TARGET — self-recovery and being-struck are the same call from different
    /// trees, which is what the first cut (self-only application on activation) could not say.
    ///
    /// A WORLD CITIZEN, because [InjectOwner] is a world contract: a task's owner facets
    /// resolve through the registry, and an ability-bearing actor is a unit the world knows —
    /// the M21 composed-object pattern rather than a loose component only GetComponent finds.
    ///
    /// It drives the two FSM behaviours the Godot side proved and the Unity side listed as
    /// missing:
    ///
    /// THE FLOOR — when nothing is active and <see cref="defaultAbility"/> names a row, it
    /// activates. "Nothing running" means something instead of nothing.
    ///
    /// THE CONTINUATION — when the active ability's tree FINISHES (M22's treeFinished; a
    /// cancelled ability returns nothing), <see cref="AbilityDef.nextOnFinish"/> activates:
    /// a combo is a wire between two rows. One hop per frame, the M22 discipline.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Ability Host")]
    public sealed class AbilityHost : WorldObjectBehaviour
    {
        /// <summary>Build sentinel for the CLI toolchain — bumped when a compile must be
        /// provably loaded (the guard-vs-stale-domain hunt of 2026-08-15).</summary>
        internal const int compileStamp = 2;

        [Tooltip("The idle floor — activated whenever nothing else is. Empty = no floor: "
            + "the host only runs what something explicitly starts.")]
        public StateTreeEntryRef<AbilityDef> defaultAbility = new StateTreeEntryRef<AbilityDef>();

        /// <summary>The active ability's row, or null between abilities.</summary>
        public AbilityDef active { get; private set; }

        /// <summary>An ability ended: the row and how — Success for a tree that finished (or
        /// an ability with no tree, which finishes on arrival), Cancelled for a pre-emption.
        /// Fired before the continuation decides what runs next.</summary>
        public event Action<AbilityDef, StateTreeStatus> abilityFinished;

        /// <summary>(previous, current) — current null between abilities.</summary>
        public event Action<AbilityDef, AbilityDef> activeAbilityChanged;

        /// <summary>An effect landed on THIS actor and its cue row fired — the listener-style
        /// observation channel (a HUD flash, a sound). The spawned prefab is the dispatched
        /// half; this event is the subscribed half.</summary>
        public event Action<CueDef, EffectDef> cueFired;

        /// <summary>An effect landed on an attribute this host does not know natively
        /// (anything but 'health') — the game's hook: (attribute, signed magnitude, effect).</summary>
        public event Action<string, float, EffectDef> attributeApplied;

        private AbilityService m_Service;
        private StateTreeExecutor m_Executor;
        private bool m_TreeFinished;
        private AbilityDef m_PendingNext;
        private readonly Dictionary<string, float> m_ReadyAt = new Dictionary<string, float>();
        private readonly List<ActiveStatus> m_Statuses = new List<ActiveStatus>();

        /// <summary>One running Duration/Infinite effect on THIS actor: the row plus this
        /// application's clock. A class, not a struct — the tick loop mutates in place.</summary>
        private sealed class ActiveStatus
        {
            public EffectDef effect;
            public int stacks;
            public float remaining;
            public float tickAccumulated;

            /// <summary>The row's magnitude AFTER level scaling, snapshotted when the effect
            /// applied — a poison keeps the strength its caster had when it landed, whatever
            /// the caster does afterwards (the GAS snapshot rule).</summary>
            public float magnitudePerStack;

            /// <summary>The granted modifier's receipt, for Modifier-operation effects —
            /// removed on expiry, re-granted when stacks change.</summary>
            public AttributeComponent.ModifierHandle modifier;
        }

        /// <summary>The service, resolved lazily — a service resolved in Awake is a race
        /// (the M20 lesson).</summary>
        public AbilityService service
        {
            get
            {
                if (m_Service == null)
                    m_Service = StateTreeContextHost.FindService<AbilityService>(gameObject);
                return m_Service;
            }
        }

        /// <summary>Whether the actor currently holds a tag — the active ability's activation
        /// tags plus every active status's granted tags. The single "is this happening to me"
        /// read conditions gate on.</summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            if (active != null && active.activationTags != null
                && active.activationTags.Contains(tag))
                return true;
            for (int i = 0; i < m_Statuses.Count; i++)
            {
                List<string> granted = m_Statuses[i].effect.grantedTags;
                if (granted != null && granted.Contains(tag))
                    return true;
            }
            return false;
        }

        /// <summary>Stack count of the named Duration effect, 0 when it is not running.</summary>
        public int StacksOf(string effectName)
        {
            for (int i = 0; i < m_Statuses.Count; i++)
            {
                if (string.Equals(m_Statuses[i].effect.name, effectName, StringComparison.Ordinal))
                    return m_Statuses[i].stacks;
            }
            return 0;
        }

        public bool CooldownReady(AbilityDef def)
        {
            return def == null
                || !m_ReadyAt.TryGetValue(def.name, out float readyAt)
                || Time.time >= readyAt;
        }

        /// <summary>The running ability tree's context, or null — how tests and tools see
        /// what an activation seeded.</summary>
        public StateTreeContext activeContext => m_Executor != null ? m_Executor.context : null;

        /// <summary>Activate by row name — the string flavor for callers without a wire.</summary>
        public bool Activate(string abilityName)
        {
            AbilityService resolved = service;
            return resolved != null && Activate(resolved.Find(abilityName));
        }

        /// <summary>
        /// Activate a row: gate through the service (cooldown, block tags, one-at-a-time),
        /// cancel the active ability when the incoming one's cancel tags say so, and start
        /// its tree — or finish immediately for a row without one. What the ability DOES,
        /// effects included, is the tree's business. False = refused.
        ///
        /// <paramref name="target"/> is the activation's PAYLOAD — the caller's search
        /// result, seeded onto the ability tree's blackboard under the row's declared
        /// <see cref="AbilityDef.targetKey"/> BEFORE anything ticks, so the first task
        /// already knows who this is about. Null, or a row with no target key, means the
        /// ability targets for itself.
        /// </summary>
        public bool Activate(AbilityDef def, GameObject target = null)
        {
            AbilityService resolved = service;
            if (def == null || resolved == null)
                return false;
            if (!resolved.CanActivate(this, def))
                return false;

            if (active != null)
                EndActive(StateTreeStatus.Cancelled, runContinuation: false);

            // Previous is always null here — a cancelled predecessor announced its own
            // (ended → null) transition inside EndActive, so this is the (null → def) half.
            active = def;
            activeAbilityChanged?.Invoke(null, def);

            if (def.tree == null)
            {
                // An ability that is only its gates and its continuation: done on arrival,
                // and the continuation gets its turn next frame (one hop per frame).
                FinishActive(StateTreeStatus.Success);
                return true;
            }

            m_TreeFinished = false;
            m_Executor = new StateTreeExecutor
            {
                data = def.tree,
                owner = gameObject,
                logLabel = "Ability '" + def.name + "'",
                logContext = this,
                parameterOverrides = def.parameters != null ? def.parameters.values : null
            };

            // THE PAYLOAD, seeded before anything ticks — an argument, not something the
            // tree observes changing (the rule parameters follow). Landing under the
            // DECLARED key's current name: the row's field is wired by id, so renaming the
            // tree's key never splits the seed from its readers.
            string landing = target != null ? LandingKeyOf(def) : "";
            if (!string.IsNullOrEmpty(landing))
            {
                m_Executor.context = new StateTreeContext(gameObject);
                m_Executor.context.blackboard[landing] = target;
            }

            m_Executor.treeFinished += OnTreeFinished;
            m_Executor.StartTree();
            if (!m_Executor.isRunning)
            {
                // A tree that refused to start (bad data — already logged) must not leave the
                // host claiming an ability is active.
                FinishActive(m_TreeFinished ? StateTreeStatus.Success : StateTreeStatus.Failure);
            }
            return true;
        }

        /// <summary>The blackboard key an activation's target lands under — the row's wired
        /// field resolved against the TREE's declaration (current name wins over the field's
        /// possibly-stale text), or the free-typed text, or empty for "no payload".</summary>
        private static string LandingKeyOf(AbilityDef def)
        {
            StateTreeKeyField field = def.targetKey;
            if (field == null)
                return "";
            if (!string.IsNullOrEmpty(field.keyId) && def.tree != null)
            {
                for (int i = 0; i < def.tree.keys.Count; i++)
                {
                    StateTreeKeyDeclaration declared = def.tree.keys[i];
                    if (declared != null && string.Equals(declared.id, field.keyId,
                        StringComparison.Ordinal))
                        return declared.name;
                }
            }
            return field.text ?? "";
        }

        /// <summary>Cancel the active ability, if any — a pre-emption, so no continuation.</summary>
        public void Cancel()
        {
            if (active != null)
                EndActive(StateTreeStatus.Cancelled, runContinuation: false);
        }

        private void OnTreeFinished()
        {
            m_TreeFinished = true;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>One frame of the host, callable headless — EditMode tests own the clock.</summary>
        public void Tick(float deltaTime)
        {
            TickStatuses(deltaTime);

            if (m_Executor != null)
            {
                m_Executor.TickTree(deltaTime);
                if (m_TreeFinished)
                    FinishActive(StateTreeStatus.Success);
                else if (m_Executor != null && !m_Executor.isRunning)
                    FinishActive(StateTreeStatus.Cancelled);
            }

            // The continuation's hop, one per frame; then the floor. Order matters: a pending
            // next outranks the default, or every combo would detour through idle.
            if (active == null && m_PendingNext != null)
            {
                AbilityDef next = m_PendingNext;
                m_PendingNext = null;
                Activate(next);
            }
            if (active == null && m_PendingNext == null
                && !string.IsNullOrEmpty(defaultAbility.entryName))
                Activate(defaultAbility.entryName);
        }

        protected override void OnDisable()
        {
            // A deactivated actor tears its run down like any pre-empted task would — the
            // granted modifiers included, or a pooled actor would come back pre-buffed.
            Cancel();
            AttributeComponent attributes = GetComponent<AttributeComponent>();
            for (int i = 0; i < m_Statuses.Count; i++)
            {
                if (m_Statuses[i].modifier != null && attributes != null)
                    attributes.RemoveModifier(m_Statuses[i].modifier);
            }
            m_Statuses.Clear();
            m_PendingNext = null;
            base.OnDisable();
        }

        private void FinishActive(StateTreeStatus status)
        {
            EndActive(status, runContinuation: status == StateTreeStatus.Success);
        }

        private void EndActive(StateTreeStatus status, bool runContinuation)
        {
            AbilityDef ended = active;
            if (ended == null)
                return;

            if (m_Executor != null)
            {
                m_Executor.treeFinished -= OnTreeFinished;
                if (m_Executor.isRunning)
                    m_Executor.StopTree();
                m_Executor = null;
            }

            if (ended.cooldownSeconds > 0f)
                m_ReadyAt[ended.name] = Time.time + ended.cooldownSeconds;

            active = null;
            abilityFinished?.Invoke(ended, status);
            activeAbilityChanged?.Invoke(ended, null);

            if (runContinuation && !string.IsNullOrEmpty(ended.nextOnFinish.entryName))
            {
                AbilityService resolved = service;
                m_PendingNext = resolved != null
                    ? resolved.Find(ended.nextOnFinish.entryName)
                    : null;
            }
        }

        // ---- effects land here -------------------------------------------------------------

        /// <summary>
        /// One effect row applied TO THIS ACTOR — by its own recovery tree or by whoever just
        /// hit it; the call does not care which, but it may say WHO (<paramref name="source"/>),
        /// because the row's cue aspect can name the caster's end of the wire. Instant
        /// magnitudes land at once (through i-frames — a hit is a hit); Duration/Infinite
        /// rows join the status list, stack by their mode, tick past i-frames, and carry
        /// their granted tags. The row's picked cue spawns per its aspect — at this actor
        /// (Target) or at the source (Source) — and <see cref="cueFired"/> tells this
        /// actor's listeners either way: the effect happened HERE.
        /// </summary>
        public void ApplyEffect(EffectDef effect, GameObject source = null)
        {
            if (effect == null)
                return;

            if (effect.duration == AbilityEffectDuration.Instant)
            {
                if (effect.operation == EffectOperation.Modifier)
                {
                    // An instant modifier would be granted and never revoked — refuse loudly
                    // rather than leak a permanent buff nobody can find.
                    Debug.LogWarning("[Ability] effect '" + effect.name + "' is an Instant "
                        + "Modifier — a grant with no expiry to revert it. Make it Duration "
                        + "or Infinite, or use a Delta.", this);
                    return;
                }
                ApplyMagnitude(effect, ScaledMagnitude(effect, source, service),
                    firstApplication: true);
            }
            else
            {
                ActiveStatus status = StackStatus(effect);
                status.magnitudePerStack = ScaledMagnitude(effect, source, service);
                if (effect.operation == EffectOperation.Modifier)
                    ReGrant(status);
                else if (effect.tickInterval > 0f)
                    ApplyMagnitude(effect, status.magnitudePerStack * status.stacks,
                        firstApplication: true);
            }

            ShowCue(effect, source);
        }

        /// <summary>The Modifier operation's grant, sized to the CURRENT stacks — the old
        /// grant reverted first, so stacking recomputes instead of accumulating drift.</summary>
        private void ReGrant(ActiveStatus status)
        {
            AttributeComponent attributes = GetComponent<AttributeComponent>();
            if (status == null || attributes == null)
                return;
            attributes.RemoveModifier(status.modifier);
            string attributeName = status.effect.attribute.entryName;
            attributes.Ensure(attributeName, 0f);
            status.modifier = attributes.AddModifier(attributeName,
                status.magnitudePerStack * status.stacks,
                Mathf.Pow(status.effect.multiplier, status.stacks));
        }

        /// <summary>
        /// The ScalableFloat read (M23 progression): a row's magnitude, times its picked
        /// progression curve evaluated at the SOURCE's level — the same balance sheet that
        /// gives a level-5 raider its hit points says how hard it hits. No scaling row, no
        /// source, or no resolvable curve = the magnitude as written.
        /// </summary>
        internal static float ScaledMagnitude(EffectDef effect, GameObject source,
            AbilityService service)
        {
            if (effect == null)
                return 0f;
            if (string.IsNullOrEmpty(effect.scaleByLevel.entryName))
                return effect.magnitude;

            ProgressionRow row = service != null
                ? service.FindProgression(effect.scaleByLevel.entryName)
                : null;
            if (row == null)
            {
                Debug.LogWarning("[Ability] effect '" + effect.name + "' scales by '"
                    + effect.scaleByLevel.entryName + "' but no progression row of that "
                    + "name is in the service's registries — applying unscaled.");
                return effect.magnitude;
            }

            int sourceLevel = 1;
            var sourceAttributes = source != null
                ? source.GetComponent<AttributeComponent>() : null;
            if (sourceAttributes != null)
                sourceLevel = sourceAttributes.level;
            return effect.magnitude * row.Evaluate(sourceLevel);
        }

        private void ShowCue(EffectDef effect, GameObject source)
        {
            AbilityService resolved = service;
            CueDef cue = resolved != null ? resolved.FindCue(effect.cue.entryName) : null;
            if (cue == null)
                return;

            // THE ASPECT (the review's question, answered in data): Target shows where the
            // effect landed — here; Source shows at the caster, falling back here when the
            // application carried none (a self-applied recovery has no other end).
            Transform where = effect.cueAspect == AbilityCueAspect.Source && source != null
                ? source.transform
                : transform;
            if (cue.prefab != null)
            {
                GameObject shown = Instantiate(cue.prefab, where.position,
                    Quaternion.identity, cue.attachToTarget ? where : null);
                // Timed Destroy is a play-mode verb; edit-mode callers (tests, tooling) own
                // their spawn's lifetime the way they own everything else they make.
                if (Application.isPlaying)
                    Destroy(shown, cue.secondsAlive > 0f ? cue.secondsAlive : 2f);
            }
            cueFired?.Invoke(cue, effect);
        }

        private ActiveStatus StackStatus(EffectDef effect)
        {
            ActiveStatus held = null;
            for (int i = 0; i < m_Statuses.Count && held == null; i++)
            {
                if (string.Equals(m_Statuses[i].effect.name, effect.name, StringComparison.Ordinal))
                    held = m_Statuses[i];
            }

            if (held == null)
            {
                held = new ActiveStatus
                {
                    effect = effect, stacks = 1, remaining = effect.seconds
                };
                m_Statuses.Add(held);
                return held;
            }

            switch (effect.stacking)
            {
                case AbilityStacking.Replace:
                    held.effect = effect;
                    held.stacks = 1;
                    held.remaining = effect.seconds;
                    break;
                case AbilityStacking.RefreshDuration:
                    held.remaining = effect.seconds;
                    break;
                case AbilityStacking.AddStacksKeepDuration:
                    held.stacks = Mathf.Min(held.stacks + 1, Mathf.Max(1, effect.maxStacks));
                    break;
                default:   // AddStacksRefreshDuration — the HT default
                    held.stacks = Mathf.Min(held.stacks + 1, Mathf.Max(1, effect.maxStacks));
                    held.remaining = effect.seconds;
                    break;
            }
            return held;
        }

        private void TickStatuses(float deltaTime)
        {
            for (int i = m_Statuses.Count - 1; i >= 0; i--)
            {
                ActiveStatus status = m_Statuses[i];
                if (status.effect.duration == AbilityEffectDuration.Duration)
                    status.remaining -= deltaTime;

                if (status.effect.tickInterval > 0f)
                {
                    status.tickAccumulated += deltaTime;
                    while (status.tickAccumulated >= status.effect.tickInterval)
                    {
                        status.tickAccumulated -= status.effect.tickInterval;
                        ApplyMagnitude(status.effect, status.magnitudePerStack * status.stacks,
                            firstApplication: false);
                    }
                }

                if (status.effect.duration == AbilityEffectDuration.Duration
                    && status.remaining <= 0f)
                {
                    // The revert half of the Modifier contract: expiry takes the grant with it.
                    if (status.modifier != null)
                    {
                        AttributeComponent attributes = GetComponent<AttributeComponent>();
                        if (attributes != null)
                            attributes.RemoveModifier(status.modifier);
                    }
                    m_Statuses.RemoveAt(i);
                }
            }
        }

        /// <summary>One signed delta onto an attribute. 'health' reaches the actor's
        /// <see cref="HealthComponent"/> natively — through i-frames on a first application
        /// (a hit is a hit) and past them on periodic ticks (a poison i-frames could shrug
        /// off would never tick). Anything else is the game's business via
        /// <see cref="attributeApplied"/>.</summary>
        private void ApplyMagnitude(EffectDef effect, float magnitude, bool firstApplication)
        {
            if (Mathf.Approximately(magnitude, 0f))
                return;

            string attributeName = effect.attribute.entryName;

            // HEALTH ROUTES THROUGH ITS RULEKEEPER: the number lives in the attribute
            // component either way, but the guard window and the death latch are health's
            // domain rules and the facade is where they are enforced.
            if (string.Equals(attributeName, HealthComponent.AttributeName,
                StringComparison.Ordinal))
            {
                var health = GetComponent<HealthComponent>();
                if (health != null)
                {
                    if (magnitude > 0f)
                        health.Heal(magnitude);
                    else if (firstApplication)
                        health.TakeDamage(-magnitude);
                    else
                        health.TickDamage(-magnitude);
                    return;
                }
            }

            // Every other attribute lands on the component directly — negative consumes,
            // positive restores, the same sign convention health uses.
            AttributeComponent attributes = GetComponent<AttributeComponent>();
            if (attributes != null && attributes.Has(attributeName))
            {
                if (magnitude > 0f)
                    attributes.Restore(attributeName, magnitude);
                else
                    attributes.Consume(attributeName, -magnitude);
                return;
            }

            attributeApplied?.Invoke(attributeName, magnitude, effect);
        }
    }
}
